using System;
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
    ///     Adds every input that changes the player this toolchain builds at <paramref name="version"/> to
    ///     <paramref name="builder"/>, so 2 builds with the same inputs give the same player and share 1 key.
    /// </summary>
    /// <remarks>
    ///     The inputs are what goes into the build: the version as the toolchain pins it, the pinned download, the
    ///     commands it runs and the edits it makes. The code that runs the build is not an input. A change to a build
    ///     step that no input shows needs a new input that shows it, such as a build version the toolchain adds.
    /// </remarks>
    /// <param name="builder">The builder of the player key. The names <c>toolchain</c> and <c>project</c> are taken.</param>
    /// <param name="version">The version as the user typed it.</param>
    /// <returns>
    ///     The version as the toolchain pins it, the same value it adds as the input <c>version</c>, so the player folder
    ///     and the store path spell it that way and never as typed.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="version"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">There is no release of <paramref name="version"/>. Nothing is added.</exception>
    string AddKeyInputs(CacheKeyBuilder builder, string version);

    /// <summary>
    ///     Installs <paramref name="version"/> into a folder under <paramref name="outputDirectory"/> unless it is there.
    /// </summary>
    /// <param name="version">The version to install, as <see cref="AddKeyInputs"/> returns it.</param>
    /// <param name="outputDirectory">The folder for the downloads and the install.</param>
    /// <param name="progress">The receiver of the download progress or <see langword="null"/> for no reports.</param>
    /// <param name="cancellationToken">The token that stops the install.</param>
    /// <returns>
    ///     The full path of the install.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="version"/> or <paramref name="outputDirectory"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="System.Net.Http.HttpRequestException">The download fails.</exception>
    /// <exception cref="IOException">The install folder cannot be written.</exception>
    /// <exception cref="InvalidDataException">The download is damaged.</exception>
    /// <exception cref="InvalidOperationException">There is no release of <paramref name="version"/>.</exception>
    /// <exception cref="TimeoutException">The download did not connect, got no response headers or moved no bytes within a time limit.</exception>
    Task<string> InstallAsync(string version, string outputDirectory, IProgress<TransferProgress>? progress, CancellationToken cancellationToken);

    /// <summary>
    ///     Builds the player project <paramref name="projectDirectory"/> in the install <paramref name="installDirectory"/>.
    /// </summary>
    /// <param name="installDirectory">The install from <see cref="InstallAsync"/>.</param>
    /// <param name="projectDirectory">The player project, such as a folder under <c>projects</c>, outside the install.</param>
    /// <param name="cancellationToken">The token that stops the build.</param>
    /// <returns>
    ///     A task that completes once the player is built.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="installDirectory"/> or <paramref name="projectDirectory"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="IOException">The project cannot be copied into the install.</exception>
    /// <exception cref="InvalidOperationException">The build fails.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task BuildAsync(string installDirectory, string projectDirectory, CancellationToken cancellationToken);

    /// <summary>
    ///     Starts the built player in the install <paramref name="installDirectory"/>, waits until it sets up its fixture
    ///     and then stops it.
    /// </summary>
    /// <param name="installDirectory">The install from <see cref="InstallAsync"/> with the player from <see cref="BuildAsync"/>.</param>
    /// <param name="cancellationToken">The token that stops the check and the player.</param>
    /// <returns>
    ///     A task that completes once the player has set up its fixture and is stopped.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="installDirectory"/> is <see langword="null"/>.</exception>
    /// <exception cref="FileNotFoundException">The install lacks a file the player needs to start.</exception>
    /// <exception cref="InvalidOperationException">The player exits before it sets up its fixture.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="TimeoutException">The player did not set up its fixture in time.</exception>
    Task CheckAsync(string installDirectory, CancellationToken cancellationToken);
}
