using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Diagnostics.Debug;
using Windows.Win32.System.JobObjects;
using Windows.Win32.System.StationsAndDesktops;
using Windows.Win32.System.Threading;

namespace AslHelp.Engines.Fixtures;

/// <summary>
///     Runs 1 player per machine inside a job object that ends the player when the job closes.
/// </summary>
/// <remarks>
///     The player is created suspended inside the job, so a caller that ends during <see cref="Start"/> never leaves a
///     suspended player outside the job. Every process the player starts joins the job too, so the whole tree ends on
///     <see cref="Dispose"/> or when the process that called <see cref="Start"/> crashes, because the OS then closes
///     the last job handle.
///     <para/>
///     A player on a hidden desktop runs on a fresh desktop of the current window station. Nothing ever switches to
///     that desktop, so the player's windows never take the focus.
///     <para/>
///     The player starts with the Windows hard-error dialogs off (SEM_FAILCRITICALERRORS, SEM_NOGPFAULTERRORBOX and
///     SEM_NOOPENFILEERRORBOX). A missing static import then ends the player with 0xC0000135 (STATUS_DLL_NOT_FOUND)
///     instead of a dialog that nobody can click on a hidden desktop or in a disconnected session. Every process the
///     player starts inherits the error mode unless that process changes its own. <see cref="Start"/> sets these
///     flags on the whole calling process during the CreateProcessW call and then puts the old value back. A crash on
///     another thread in that time skips the error dialog. A SetErrorMode call from another thread in that time is
///     undone.
///     <para/>
///     Player has no finalizer, so always dispose a player. A player that is never disposed keeps the machine lock
///     until the process that called <see cref="Start"/> exits. Every later <see cref="Start"/> in that process then
///     times out. The process tree of that player ends sooner, once the garbage collector finalizes the handle of
///     its job.
/// </remarks>
public sealed class Player : IDisposable
{
    /// <summary>
    ///     The start of the name of each hidden desktop, which a new GUID follows.
    /// </summary>
    internal const string DesktopPrefix = "AslHelp.Engines.Fixtures.Player-";

    private const THREAD_ERROR_MODE ChildErrorMode =
        THREAD_ERROR_MODE.SEM_FAILCRITICALERRORS | THREAD_ERROR_MODE.SEM_NOGPFAULTERRORBOX | THREAD_ERROR_MODE.SEM_NOOPENFILEERRORBOX;

    private const uint ExitTimeoutMilliseconds = 5000;

    private readonly CloseDesktopSafeHandle? _desktop;
    private readonly SafeFileHandle _job;
    private readonly PlayerLock _lock;
    private readonly Process _process;
    private readonly SafeFileHandle _processHandle;

    private int _disposed;

    private Player(PlayerLock playerLock, SafeFileHandle job, CloseDesktopSafeHandle? desktop, SafeFileHandle processHandle, Process process)
    {
        _lock = playerLock;
        _job = job;
        _desktop = desktop;
        _processHandle = processHandle;
        _process = process;

        Id = process.Id;
    }

    /// <summary>
    ///     Gets the process id of the player.
    /// </summary>
    public int Id { get; }

    /// <summary>
    ///     Gets the player process.
    /// </summary>
    /// <remarks>
    ///     The process belongs to the player and <see cref="Dispose"/> disposes it, so do not dispose it yourself.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The player is disposed.</exception>
    public Process Process
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            return _process;
        }
    }

    /// <summary>
    ///     Gets a value indicating whether the player process has ended. <see langword="true"/> once the player is
    ///     disposed.
    /// </summary>
    public bool HasExited => Volatile.Read(ref _disposed) != 0 || PInvoke.WaitForSingleObject(_processHandle, 0) == WAIT_EVENT.WAIT_OBJECT_0;

    /// <summary>
    ///     Starts a player once no other player runs on the machine.
    /// </summary>
    /// <param name="info">The executable, arguments and folder of the player to start.</param>
    /// <returns>
    ///     The running player.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="info"/> is <see langword="null"/>.</exception>
    /// <exception cref="DirectoryNotFoundException">
    ///     The working folder does not exist or its path is not a valid path.
    /// </exception>
    /// <exception cref="FileNotFoundException">
    ///     The executable does not exist or its path is not a valid path.
    /// </exception>
    /// <exception cref="TimeoutException">
    ///     Another player kept the machine lock past <see cref="PlayerStartInfo.LockTimeout"/>.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">The machine lock exists under another user account.</exception>
    /// <exception cref="Win32Exception">
    ///     Creating the job, the desktop or the process failed. The machine lock is free again.
    /// </exception>
    public static Player Start(PlayerStartInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        // Resolve both paths here, because CreateProcessW resolves a relative executable against the caller's folder
        // and takes the working folder only as a full path.
        string? executablePath = GetFullPath(info.ExecutablePath);
        string? workingDirectory = GetFullPath(info.WorkingDirectory);

        if (executablePath is null || !File.Exists(executablePath))
        {
            string path = executablePath ?? info.ExecutablePath;

            throw new FileNotFoundException(SR.Format(SR.PlayerNotFound, path), path);
        }

        if (workingDirectory is null || !Directory.Exists(workingDirectory))
        {
            throw new DirectoryNotFoundException(SR.Format(SR.WorkingFolderNotFound, workingDirectory ?? info.WorkingDirectory));
        }

        var playerLock = PlayerLock.Acquire(info.LockTimeout);
        SafeFileHandle? job = null;
        CloseDesktopSafeHandle? desktop = null;

        try
        {
            job = CreateJob();

            string? desktopName = null;
            if (info.HiddenDesktop)
            {
                desktopName = $"{DesktopPrefix}{Guid.NewGuid():N}";
                desktop = CreateDesktop(desktopName);
            }

            var (processHandle, process) = Run(job, executablePath, info.Arguments, workingDirectory, desktopName);

            return new Player(playerLock, job, desktop, processHandle, process);
        }
        catch
        {
            job?.Dispose();
            desktop?.Dispose();
            playerLock.Dispose();

            throw;
        }
    }

    /// <summary>
    ///     Ends the player and every process it started, closes the desktop and gives the machine lock back.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            // Close the job instead of terminating it, because a crashed caller ends the tree the same way, so every
            // Dispose tests that path.
            _job.Dispose();

            // Wait on the player's own process handle and not on Process, because the caller may have disposed Process. Ignore a
            // wait that times out, because the kernel finishes the kill on its own.
            _ = PInvoke.WaitForSingleObject(_processHandle, ExitTimeoutMilliseconds);
        }
        finally
        {
            _processHandle.Dispose();
            _process.Dispose();
            _desktop?.Dispose();
            _lock.Dispose();
        }
    }

    private static unsafe SafeFileHandle CreateJob()
    {
        var job = PInvoke.CreateJobObject(null, null);
        if (job.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            var exception = Win32Error(error, SR.JobCreateFailed, SR.JobCreateFix);
            job.Dispose();

            throw exception;
        }

        JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits = new()
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION { LimitFlags = JOB_OBJECT_LIMIT.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE },
        };

        if (!PInvoke.SetInformationJobObject(
            job,
            JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation,
            new ReadOnlySpan<byte>(&limits, sizeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION))))
        {
            int error = Marshal.GetLastPInvokeError();
            var exception = Win32Error(error, SR.JobKillOnCloseFailed, SR.JobKillOnCloseFix);
            job.Dispose();

            throw exception;
        }

        return job;
    }

    private static CloseDesktopSafeHandle CreateDesktop(string name)
    {
        var desktop = PInvoke.CreateDesktop(name, 0, (uint)GENERIC_ACCESS_RIGHTS.GENERIC_ALL, null);
        if (desktop.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            var exception = Win32Error(error, SR.DesktopCreateFailed, SR.DesktopCreateFix);
            desktop.Dispose();

            throw exception;
        }

        return desktop;
    }

    private static (SafeFileHandle Handle, Process Process) Run(
        SafeFileHandle job,
        string executablePath,
        string arguments,
        string workingDirectory,
        string? desktop)
    {
        var information = CreateSuspendedInJob(job, executablePath, arguments, workingDirectory, desktop);

        SafeFileHandle? processHandle = new(information.hProcess, ownsHandle: true);
        using SafeFileHandle threadHandle = new(information.hThread, ownsHandle: true);
        Process? process = null;

        // Let the job close in Start end the suspended child when a step below fails, so this method only closes its handles.
        try
        {
            process = Process.GetProcessById((int)information.dwProcessId);

            // Open the handle of the Process while the child is suspended, because without it ExitCode throws and a
            // reused id makes HasExited report another process.
            _ = process.Handle;

            if (PInvoke.ResumeThread(threadHandle) == uint.MaxValue)
            {
                int error = Marshal.GetLastPInvokeError();
                throw Win32Error(error, SR.Format(SR.ResumeFailed, executablePath), SR.ResumeFix);
            }

            var started = (processHandle, process);
            processHandle = null;
            process = null;

            return started;
        }
        finally
        {
            process?.Dispose();
            processHandle?.Dispose();
        }
    }

    private static unsafe PROCESS_INFORMATION CreateSuspendedInJob(
        SafeFileHandle job,
        string executablePath,
        string arguments,
        string workingDirectory,
        string? desktop)
    {
        const uint AttributeCount = 1;

        // Pass the command line as a writable span, because CreateProcessW may write to it.
        Span<char> commandLine = $"\"{executablePath}\" {arguments}\0".ToCharArray();

        // Get the buffer size of a list with the job attribute and drop the result, because this call fails by design and only fills the size.
        nuint size = 0;
        _ = PInvoke.InitializeProcThreadAttributeList(default, AttributeCount, 0, &size);

        byte* buffer = stackalloc byte[(int)size];
        LPPROC_THREAD_ATTRIBUTE_LIST attributeList = new(buffer);

        if (!PInvoke.InitializeProcThreadAttributeList(attributeList, AttributeCount, 0, &size))
        {
            int error = Marshal.GetLastPInvokeError();
            throw Win32Error(error, SR.Format(SR.JobListFailed, executablePath), SR.JobListFix);
        }

        // Keep the job handle in a local that outlives the list, because the list points at it until the list is deleted.
        var jobHandle = (HANDLE)job.DangerousGetHandle();

        try
        {
            // Add the job to the list, so the child is created inside the job and a caller that ends during Start never leaves it outside.
            if (!PInvoke.UpdateProcThreadAttribute(
                attributeList,
                0,
                PInvoke.PROC_THREAD_ATTRIBUTE_JOB_LIST,
                &jobHandle,
                (nuint)sizeof(HANDLE),
                null,
                null))
            {
                int error = Marshal.GetLastPInvokeError();
                throw Win32Error(error, SR.Format(SR.JobListFailed, executablePath), SR.JobListFix);
            }

            fixed (char* desktopPointer = desktop)
            {
                // Pass the bare desktop name, because a name without a backslash means the current window station.
                STARTUPINFOEXW startupInfo = new()
                {
                    StartupInfo = new STARTUPINFOW { cb = (uint)sizeof(STARTUPINFOEXW), lpDesktop = desktopPointer },
                    lpAttributeList = attributeList,
                };

                // Turn the hard-error dialogs off in this process for the CreateProcessW call only, because the child
                // copies the process error mode when it is created and the thread error mode never reaches it. Start runs
                // under the machine lock, so no other Start puts an older mode back in between.
                var previousErrorMode = (THREAD_ERROR_MODE)PInvoke.GetErrorMode();
                _ = PInvoke.SetErrorMode(previousErrorMode | ChildErrorMode);

                try
                {
                    // Pass the STARTUPINFOW at the start of startupInfo, because CreateProcessW reads the attribute list
                    // after it when EXTENDED_STARTUPINFO_PRESENT is set.
                    if (!PInvoke.CreateProcess(
                        executablePath,
                        ref commandLine,
                        null,
                        null,
                        false,
                        PROCESS_CREATION_FLAGS.CREATE_SUSPENDED | PROCESS_CREATION_FLAGS.EXTENDED_STARTUPINFO_PRESENT,
                        null,
                        workingDirectory,
                        in startupInfo.StartupInfo,
                        out var information))
                    {
                        // Read the error before the SetErrorMode in finally, because that call may change it.
                        int error = Marshal.GetLastPInvokeError();
                        throw Win32Error(error, SR.Format(SR.StartFailed, executablePath), SR.StartFix);
                    }

                    return information;
                }
                finally
                {
                    _ = PInvoke.SetErrorMode(previousErrorMode);
                }
            }
        }
        finally
        {
            PInvoke.DeleteProcThreadAttributeList(attributeList);
        }
    }

    private static string? GetFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (ArgumentException)
        {
            // Treat a path with a null character as missing, because GetFullPath throws on it.
            return null;
        }
    }

    // Take the error code from the caller, because the message lookups can run P/Invokes that change the last error.
    private static Win32Exception Win32Error(int error, string failure, string fix)
    {
        return new Win32Exception(error, $"{failure} {Marshal.GetPInvokeErrorMessage(error)} {fix}");
    }
}
