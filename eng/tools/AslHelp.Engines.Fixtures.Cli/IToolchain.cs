using AslHelp.Engines.Fixtures.Cli.Transfer;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli;

/// <summary>
///     Represents the release of an engine that fixture players are built with, installed at a version.
/// </summary>
internal interface IToolchain
{
    /// <summary>
    ///     Gets the name of the toolchain on the command line and of its player project folder under <c>projects</c>.
    /// </summary>
    string Name { get; }

    /// <summary>
    ///     Gets the options that this toolchain adds to its <c>build</c> subcommand. <see cref="ListPlayers"/> reads their values.
    /// </summary>
    IReadOnlyList<Option> Options { get; }

    /// <summary>
    ///     Lists the players of <paramref name="version"/>.
    /// </summary>
    /// <param name="version">The version as the user typed it.</param>
    /// <param name="parseResult">The parsed command line with the values of <see cref="Options"/>.</param>
    /// <returns>
    ///     The player ids in build order. An empty list or a repeated id fails the build. A player id is the value
    ///     <see cref="AddKeyInputs"/> returns for it.
    /// </returns>
    /// <exception cref="InvalidOperationException">There is no release of <paramref name="version"/>.</exception>
    IReadOnlyList<string> ListPlayers(string version, ParseResult parseResult);

    /// <summary>
    ///     Adds every input that changes <paramref name="player"/> to <paramref name="builder"/>,
    ///     so 2 builds with the same inputs give the same player and share 1 key.
    /// </summary>
    /// <remarks>
    ///     The inputs are what goes into the build: the version as the toolchain pins it, the pinned download, the
    ///     project files as the build reads them, the commands it runs and the edits it makes. The code that runs the
    ///     build is not an input. A change to a build step that no input shows needs a new input that shows it, such as
    ///     a build version the toolchain adds.
    /// </remarks>
    /// <param name="builder">The builder of the player key. The name <c>toolchain</c> is taken.</param>
    /// <param name="player">The player id from <see cref="ListPlayers"/>.</param>
    /// <param name="projectDirectory">
    ///     The player project, such as a folder under <c>projects</c>. The toolchain adds it as the input <c>project</c>.
    /// </param>
    /// <returns>
    ///     The version as the toolchain pins it, the same value it adds as the input <c>version</c>, so the player folder
    ///     and the store path spell it that way and never as typed.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="builder"/>, <paramref name="player"/> or <paramref name="projectDirectory"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="DirectoryNotFoundException"><paramref name="projectDirectory"/> does not exist. Nothing is added.</exception>
    /// <exception cref="IOException">A file of <paramref name="projectDirectory"/> cannot be read.</exception>
    /// <exception cref="InvalidOperationException">There is no release of <paramref name="player"/>. Nothing is added.</exception>
    string AddKeyInputs(CacheKeyBuilder builder, string player, string projectDirectory);

    /// <summary>
    ///     Installs the release of <paramref name="player"/> into a folder under <paramref name="outputDirectory"/>
    ///     unless it is there.
    /// </summary>
    /// <remarks>
    ///     A build calls this once with its 1st missing player, so every player of a version maps to 1 install.
    /// </remarks>
    /// <param name="player">A player id from <see cref="ListPlayers"/>.</param>
    /// <param name="outputDirectory">The folder for the downloads and the install.</param>
    /// <param name="progress">The receiver of the download progress or <see langword="null"/> for no reports.</param>
    /// <param name="cancellationToken">The token that stops the install.</param>
    /// <returns>
    ///     The full path of the install.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="player"/> or <paramref name="outputDirectory"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="System.Net.Http.HttpRequestException">The download fails.</exception>
    /// <exception cref="IOException">The install folder cannot be written.</exception>
    /// <exception cref="InvalidDataException">The download is damaged.</exception>
    /// <exception cref="InvalidOperationException">There is no release of <paramref name="player"/>.</exception>
    /// <exception cref="TimeoutException">The download did not connect, got no response headers or moved no bytes within a time limit.</exception>
    Task<string> InstallAsync(string player, string outputDirectory, IProgress<TransferProgress>? progress, CancellationToken cancellationToken);

    /// <summary>
    ///     Builds every player of <paramref name="players"/> from the player project <paramref name="projectDirectory"/>
    ///     in 1 pass in the install <paramref name="installDirectory"/>.
    /// </summary>
    /// <param name="installDirectory">The install from <see cref="InstallAsync"/>.</param>
    /// <param name="projectDirectory">The player project, such as a folder under <c>projects</c>, outside the install.</param>
    /// <param name="players">The player ids from <see cref="ListPlayers"/> to build.</param>
    /// <param name="cancellationToken">The token that stops the build.</param>
    /// <returns>
    ///     The full path of the folder of each built player, in the order of <paramref name="players"/>. A wrong count fails the build.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="installDirectory"/>, <paramref name="projectDirectory"/> or <paramref name="players"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="IOException">The project cannot be copied into the install.</exception>
    /// <exception cref="InvalidOperationException">The build fails.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<IReadOnlyList<string>> BuildAsync(
        string installDirectory,
        string projectDirectory,
        IReadOnlyList<string> players,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Starts the player in <paramref name="playerDirectory"/>, waits until it sets up its fixture and then stops it.
    /// </summary>
    /// <param name="playerDirectory">A player folder from <see cref="BuildAsync"/>.</param>
    /// <param name="cancellationToken">The token that stops the check and the player.</param>
    /// <returns>
    ///     A task that completes once the player has set up its fixture and is stopped.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="playerDirectory"/> is <see langword="null"/>.</exception>
    /// <exception cref="FileNotFoundException">The folder lacks a file the player needs to start.</exception>
    /// <exception cref="InvalidOperationException">The player exits before it sets up its fixture.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="TimeoutException">The player did not set up its fixture in time.</exception>
    Task CheckAsync(string playerDirectory, CancellationToken cancellationToken);
}
