using System;

namespace AslHelp.Engines.Fixtures;

/// <summary>
///     Describes the player that <see cref="Player.Start"/> runs.
/// </summary>
/// <remarks>
///     Every player starts with the Windows hard-error dialogs off, so a missing static import ends it with
///     STATUS_DLL_NOT_FOUND instead of a dialog. See <see cref="Player"/>.
/// </remarks>
public sealed record PlayerStartInfo
{
    private static readonly TimeSpan _maxLockTimeout = TimeSpan.FromMilliseconds(int.MaxValue);

    /// <summary>
    ///     Initializes a new instance of the <see cref="PlayerStartInfo"/> class.
    /// </summary>
    /// <param name="executablePath">The path of the player executable.</param>
    /// <param name="arguments">
    ///     The command line arguments, appended as is after the quoted executable path. Pass an empty string for none.
    /// </param>
    /// <param name="workingDirectory">The folder the player starts in.</param>
    /// <exception cref="ArgumentException">
    ///     <paramref name="executablePath"/> or <paramref name="workingDirectory"/> is empty or only white space.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="executablePath"/>, <paramref name="arguments"/> or <paramref name="workingDirectory"/> is
    ///     <see langword="null"/>.
    /// </exception>
    public PlayerStartInfo(string executablePath, string arguments, string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        ExecutablePath = executablePath;
        Arguments = arguments;
        WorkingDirectory = workingDirectory;
    }

    /// <summary>
    ///     Gets the path of the player executable.
    /// </summary>
    public string ExecutablePath { get; }

    /// <summary>
    ///     Gets the command line arguments.
    /// </summary>
    /// <remarks>
    ///     The arguments are 1 string and not a list, because each player parses its own command line and no escaping
    ///     of a list fits every parser.
    /// </remarks>
    public string Arguments { get; }

    /// <summary>
    ///     Gets the folder the player starts in.
    /// </summary>
    public string WorkingDirectory { get; }

    /// <summary>
    ///     Gets a value indicating whether the player starts on a desktop of its own, so its windows never take the
    ///     focus. Defaults to <see langword="false"/>.
    /// </summary>
    public bool HiddenDesktop { get; init; }

    /// <summary>
    ///     Gets how long <see cref="Player.Start"/> waits for another player on the machine to stop. Defaults to
    ///     10 minutes.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     The value is negative or above <see cref="int.MaxValue"/> milliseconds.
    /// </exception>
    public TimeSpan LockTimeout
    {
        get;
        init
        {
            if (value < TimeSpan.Zero || value > _maxLockTimeout)
            {
                throw new ArgumentOutOfRangeException(nameof(LockTimeout), value, SR.FormatLockTimeoutOutOfRange(_maxLockTimeout));
            }

            field = value;
        }
    } = TimeSpan.FromMinutes(10);
}
