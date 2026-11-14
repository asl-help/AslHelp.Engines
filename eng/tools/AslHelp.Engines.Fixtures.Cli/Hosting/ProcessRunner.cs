using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Win32;

namespace AslHelp.Engines.Fixtures.Cli.Hosting;

/// <summary>
///     Runs a command line tool without a window, reads its output and fails on a code other than 0 with the last lines of that output.
/// </summary>
internal static class ProcessRunner
{
    /// <summary>
    ///     Represents the most output lines that the failure message of <see cref="RunAsync"/> has, which are the last lines that are not blank.
    /// </summary>
    public const int TailLineCount = 10;

    // Decode the output with the OEM code page, because a tool without a window gets a console of its own, which starts in that code page.
    private static readonly Encoding _outputEncoding = GetOemEncoding();

    /// <summary>
    ///     Creates the start info of a tool that runs without a window, ends with this process, takes the given arguments and pipes its output.
    /// </summary>
    /// <param name="fileName">The full path of the tool or its name on <c>PATH</c>.</param>
    /// <param name="arguments">The arguments, 1 per item and unquoted.</param>
    /// <returns>
    ///     The start info.
    /// </returns>
    public static ProcessStartInfo NewStartInfo(string fileName, params string[] arguments)
    {
        // Pipe both streams, so the output stays off the console and RunAsync keeps its last lines for the failure message.
        return new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            KillOnParentExit = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = _outputEncoding,
            StandardErrorEncoding = _outputEncoding,
        };
    }

    /// <summary>
    ///     Runs the tool of <paramref name="info"/> and waits for it to exit.
    /// </summary>
    /// <remarks>
    ///     Both pipes are read while the tool runs, so a full pipe never blocks the tool and nothing reaches the console.
    ///     Only the last <see cref="TailLineCount"/> lines that are not blank are kept. The read ends once every writer has closed the pipes,
    ///     so a child that outlives the tool with a pipe open keeps the call waiting until the child exits or the token is canceled.
    ///     A failed read ends the tool and every process it started, as a cancel does.
    /// </remarks>
    /// <param name="info">The start info from <see cref="NewStartInfo"/>.</param>
    /// <param name="cancellationToken">The token that ends the tool and every process it started.</param>
    /// <returns>
    ///     A task that completes once the tool exits with code 0.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="info"/> does not redirect both standard output and standard error.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="info"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    ///     The tool exits with a code other than 0. The message has the command line, the code and the kept lines.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="Win32Exception">The tool cannot be started.</exception>
    public static async Task RunAsync(ProcessStartInfo info, CancellationToken cancellationToken)
    {
        Queue<string> tail = new(TailLineCount);

        int exitCode = await RunWithLinesAsync(info, line => AddToTail(tail, line), cancellationToken).ConfigureAwait(false);

        if (exitCode != 0)
        {
            throw new InvalidOperationException(FailureMessage(info, exitCode, tail));
        }
    }

    /// <summary>
    ///     Adds <paramref name="line"/> to <paramref name="tail"/> and drops the oldest line once it has <see cref="TailLineCount"/> lines.
    /// </summary>
    /// <param name="tail">The last output lines, oldest first.</param>
    /// <param name="line">The line to add.</param>
    public static void AddToTail(Queue<string> tail, string line)
    {
        ArgumentNullException.ThrowIfNull(tail);

        if (tail.Count == TailLineCount)
        {
            _ = tail.Dequeue();
        }

        tail.Enqueue(line);
    }

    /// <summary>
    ///     Runs the tool of <paramref name="info"/>, passes each line that is not blank to <paramref name="onLine"/> and returns its exit code.
    /// </summary>
    /// <remarks>
    ///     Both pipes are read while the tool runs, as in <see cref="RunAsync"/>. A code other than 0 doesn't throw, so the caller can map it.
    ///     A callback that throws ends the tool and every process it started, as a cancel does.
    /// </remarks>
    /// <param name="info">The start info from <see cref="NewStartInfo"/>.</param>
    /// <param name="onLine">The receiver of the lines. It gets 1 line at a time.</param>
    /// <param name="cancellationToken">The token that ends the tool and every process it started.</param>
    /// <returns>
    ///     The exit code.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="info"/> does not redirect both standard output and standard error.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="info"/> or <paramref name="onLine"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="Win32Exception">The tool cannot be started.</exception>
    public static async Task<int> RunWithLinesAsync(ProcessStartInfo info, Action<string> onLine, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(onLine);

        // Check both redirects before the start, because ReadAllLinesAsync checks them only on its 1st read, once the tool runs.
        if (!info.RedirectStandardOutput || !info.RedirectStandardError)
        {
            throw new ArgumentException(SR.FormatProcessOutputNotRedirected(info.FileName), nameof(info));
        }

        // Check the token before the start, so a canceled call starts no tool.
        cancellationToken.ThrowIfCancellationRequested();

        using Process process = new() { StartInfo = info };
        _ = process.Start();

        ProcessExitStatus status;

        try
        {
            // Read both pipes to their end before the wait, so a tool that fills a pipe never blocks on it.
            await foreach (var line in process.ReadAllLinesAsync(cancellationToken).ConfigureAwait(false))
            {
                // Skip blank lines, so they never push a line with text out of the tail.
                if (string.IsNullOrWhiteSpace(line.Content))
                {
                    continue;
                }

                onLine(line.Content);
            }

            // Wait on the process handle, because Process.WaitForExitAsync returns as soon as the exit code is set, before the exe can be deleted.
            status = await process.SafeHandle.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Kill the tree and wait for the tool to exit, so a folder the tool writes to can be deleted after a cancel or a failed read.
            // Skip the wait after a failed kill, because a tool that didn't end would block it forever.
            if (TryKillTree(process))
            {
                _ = await process.SafeHandle.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }

            throw;
        }

        return status.ExitCode;
    }

    /// <summary>
    ///     Kills <paramref name="process"/> and every process it started.
    /// </summary>
    /// <remarks>
    ///     Catches a failed kill, because it runs in a catch block and its exception would hide the cancel or the failed read.
    /// </remarks>
    /// <param name="process">The started tool.</param>
    /// <returns>
    ///     <see langword="true"/> when the kill succeeded, <see langword="false"/> when it failed.
    /// </returns>
    private static bool TryKillTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);

            return true;
        }
        catch (Exception exception) when (exception is AggregateException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    /// <summary>
    ///     Creates the message for a tool that exited with a code other than 0.
    /// </summary>
    /// <param name="info">The start info of the tool.</param>
    /// <param name="exitCode">The code the tool exited with.</param>
    /// <param name="tail">The last output lines that are not blank, in the order the tool printed them.</param>
    /// <returns>
    ///     The message with the command line, the code and 1 line per item of <paramref name="tail"/>.
    /// </returns>
    private static string FailureMessage(ProcessStartInfo info, int exitCode, Queue<string> tail)
    {
        string commandLine = FormatCommandLine(info);

        return tail.Count == 0
            ? SR.FormatProcessFailed(commandLine, exitCode)
            : SR.FormatProcessFailedWithOutput(commandLine, exitCode, Environment.NewLine + string.Join(Environment.NewLine, tail));
    }

    /// <summary>
    ///     Formats the file name and the arguments of <paramref name="info"/> as 1 command line, the way Process passes them on Windows.
    ///     <see cref="ProcessStartInfo.Arguments"/> stays as it is and each item of <see cref="ProcessStartInfo.ArgumentList"/> is quoted.
    /// </summary>
    /// <param name="info">The start info of the tool.</param>
    /// <returns>
    ///     The command line.
    /// </returns>
    private static string FormatCommandLine(ProcessStartInfo info)
    {
        StringBuilder commandLine = new();
        AppendArgument(commandLine, info.FileName);

        if (info.Arguments.Length > 0)
        {
            _ = commandLine.Append(' ').Append(info.Arguments);
        }

        foreach (string argument in info.ArgumentList)
        {
            _ = commandLine.Append(' ');
            AppendArgument(commandLine, argument);
        }

        return commandLine.ToString();
    }

    /// <summary>
    ///     Appends <paramref name="argument"/> to <paramref name="commandLine"/> and quotes it if it is empty, has white space or has a double quote.
    /// </summary>
    /// <param name="commandLine">The command line to append to.</param>
    /// <param name="argument">The file name or an argument, unquoted.</param>
    private static void AppendArgument(StringBuilder commandLine, string argument)
    {
        if (argument.Length > 0 && !argument.AsSpan().ContainsAnyWhiteSpace() && !argument.Contains('"', StringComparison.Ordinal))
        {
            _ = commandLine.Append(argument);
            return;
        }

        _ = commandLine.Append('"');
        int backslashes = 0;

        foreach (char c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                // Double the backslashes before a quote and escape the quote, so the tool reads both as text.
                _ = commandLine.Append('\\', (2 * backslashes) + 1);
            }
            else
            {
                _ = commandLine.Append('\\', backslashes);
            }

            _ = commandLine.Append(c);
            backslashes = 0;
        }

        // Double the backslashes before the closing quote, so the tool reads them as text and the quote still ends the argument.
        _ = commandLine.Append('\\', 2 * backslashes).Append('"');
    }

    /// <summary>
    ///     Gets the encoding of the OEM code page of the operating system.
    /// </summary>
    /// <returns>
    ///     The encoding.
    /// </returns>
    private static Encoding GetOemEncoding()
    {
        int codePage = (int)PInvoke.GetOEMCP();

        // Look in CodePagesEncodingProvider first, because Encoding.GetEncoding finds only the Unicode code pages, ASCII and Latin1 without it.
        return CodePagesEncodingProvider.Instance.GetEncoding(codePage) ?? Encoding.GetEncoding(codePage);
    }
}
