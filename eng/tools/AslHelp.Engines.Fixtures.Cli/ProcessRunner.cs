using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Win32;

namespace AslHelp.Engines.Fixtures.Cli;

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
    /// <param name="fileName">The full path of the tool.</param>
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
    /// <exception cref="System.ComponentModel.Win32Exception">The tool cannot be started.</exception>
    public static async Task RunAsync(ProcessStartInfo info, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(info);

        // Check both redirects before the start, because ReadAllLinesAsync checks them only on its 1st read, once the tool runs.
        if (!info.RedirectStandardOutput || !info.RedirectStandardError)
        {
            throw new ArgumentException(SR.FormatProcessOutputNotRedirected(info.FileName), nameof(info));
        }

        // Check the token before the start, so a canceled call starts no tool.
        cancellationToken.ThrowIfCancellationRequested();

        Queue<string> tail = new(TailLineCount);

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

                if (tail.Count == TailLineCount)
                {
                    _ = tail.Dequeue();
                }

                tail.Enqueue(line.Content);
            }

            // Wait on the process handle, because Process.WaitForExitAsync returns as soon as the exit code is set, before the exe can be deleted.
            status = await process.SafeHandle.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Kill the tree and wait for the tool to exit, so a folder the tool writes to can be deleted after a cancel or a failed read.
            process.Kill(entireProcessTree: true);
            _ = await process.SafeHandle.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);

            throw;
        }

        if (status.ExitCode != 0)
        {
            throw new InvalidOperationException(FailureMessage(info, status.ExitCode, tail));
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
