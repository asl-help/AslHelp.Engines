using System.CommandLine;
using System.CommandLine.Help;
using System.IO;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli;

/// <summary>
///     Parses and runs the fixture commands.
/// </summary>
internal static class CommandLine
{
    /// <summary>
    ///     Parses <paramref name="args"/> and runs the command in them.
    /// </summary>
    /// <param name="args">The command and its arguments.</param>
    /// <param name="output">The writer for help and progress.</param>
    /// <param name="error">The writer for parse errors and failures.</param>
    /// <returns>
    ///     The exit code of the run.
    /// </returns>
    public static Task<int> RunAsync(string[] args, TextWriter output, TextWriter error)
    {
        RootCommand root = [with(SR.RootDescription)];

        // Print the help and fail, so a script that passes no command stops.
        root.SetAction(parseResult =>
        {
            _ = new HelpAction().Invoke(parseResult);
            return 1;
        });

        InvocationConfiguration configuration = new()
        {
            Output = output,
            Error = error,
        };

        return root.Parse(args).InvokeAsync(configuration);
    }
}
