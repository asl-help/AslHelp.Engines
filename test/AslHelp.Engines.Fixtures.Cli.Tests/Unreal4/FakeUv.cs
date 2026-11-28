using System;
using System.IO;
using System.Text;

namespace AslHelp.Engines.Fixtures.Cli.Tests.Unreal4;

/// <summary>
///     Writes a <c>uv.cmd</c> that records how it was started, runs the given batch lines and exits with a code.
/// </summary>
/// <remarks>
///     A start with <c>--measure</c> only records its command line apart, prints the install size and exits with 0, so the
///     records of the install stay the ones of the last install start.
/// </remarks>
internal sealed class FakeUv
{
    private readonly string _argumentsPath;
    private readonly string _measureArgumentsPath;
    private readonly string _workingDirectoryPath;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FakeUv"/> class and writes the file, with a measure that prints 0 bytes.
    /// </summary>
    /// <param name="folder">The existing folder for the file and its records.</param>
    /// <param name="exitCode">The code the file exits with.</param>
    /// <param name="lines">The batch lines to run, such as <c>echo text</c>.</param>
    public FakeUv(string folder, int exitCode, params string[] lines)
        : this(folder, 0L, exitCode, lines)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="FakeUv"/> class and writes the file.
    /// </summary>
    /// <param name="folder">The existing folder for the file and its records.</param>
    /// <param name="installBytes">The size the measure prints or <see langword="null"/> for a measure that prints none.</param>
    /// <param name="exitCode">The code the file exits with.</param>
    /// <param name="lines">The batch lines to run, such as <c>echo text</c>.</param>
    public FakeUv(string folder, long? installBytes, int exitCode, params string[] lines)
    {
        FilePath = Path.Combine(folder, "uv.cmd");
        _argumentsPath = Path.Combine(folder, "arguments.txt");
        _measureArgumentsPath = Path.Combine(folder, "measure-arguments.txt");
        _workingDirectoryPath = Path.Combine(folder, "working-directory.txt");

        string printSize = installBytes is { } bytes ? $"echo install bytes: {bytes}" : "rem";

        // Jump instead of a block, because a ) in a path would end a block. Put the redirects first, because cmd reads a digit right before '>' as a stream number.
        string[] script =
        [
            "@echo off",
            "echo %* | findstr /c:\" --measure \" >nul",
            "if errorlevel 1 goto install",
            $"> \"{_measureArgumentsPath}\" echo %*",
            printSize,
            "exit /b 0",
            ":install",
            $"> \"{_argumentsPath}\" echo %*",
            $"> \"{_workingDirectoryPath}\" echo %CD%",
            .. lines,
            $"exit /b {exitCode}",
        ];

        File.WriteAllLines(FilePath, script, Encoding.ASCII);
    }

    /// <summary>
    ///     Gets the command line of the last install start, without the file.
    /// </summary>
    public string Arguments => File.ReadAllText(_argumentsPath).Trim();

    /// <summary>
    ///     Gets the full path of the file.
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    ///     Gets the command line of the last measure start, without the file.
    /// </summary>
    public string MeasureArguments => File.ReadAllText(_measureArgumentsPath).Trim();

    /// <summary>
    ///     Gets a value indicating whether the file was started.
    /// </summary>
    public bool Ran => File.Exists(_argumentsPath);

    /// <summary>
    ///     Gets the path of the script in the command line of the last start, the argument after <c>-I</c>.
    /// </summary>
    public string ScriptPath
    {
        get
        {
            const string Option = "-I ";
            const string ScriptFile = "install_engine.py";

            string arguments = Arguments;
            int start = arguments.IndexOf(Option, StringComparison.Ordinal) + Option.Length;
            int end = arguments.IndexOf(ScriptFile, StringComparison.Ordinal) + ScriptFile.Length;

            return arguments[start..end].Trim('"');
        }
    }

    /// <summary>
    ///     Gets the working directory of the last start.
    /// </summary>
    public string WorkingDirectory => File.ReadAllText(_workingDirectoryPath).Trim();
}
