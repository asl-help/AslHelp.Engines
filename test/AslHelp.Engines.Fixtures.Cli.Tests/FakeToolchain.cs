using AslHelp.Engines.Fixtures.Cli.Transfer;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

/// <summary>
///     Records every call in order, installs an empty folder and builds 1 folder per player under it. Each folder has
///     the player id as the content of <c>file.bin</c>, so a test can check which build went to which player folder.
/// </summary>
/// <param name="name">The name of the toolchain.</param>
/// <param name="variantOption">The name of the option that lists the variants.</param>
internal sealed class FakeToolchain(
    string name,
    string variantOption = "--variant") : IToolchain
{
    private readonly Option<string[]> _variantOption = new(variantOption) { Description = "The variant to build." };

    /// <summary>
    ///     Gets the calls in order, such as <c>InstallAsync 1.0 output</c>.
    /// </summary>
    public List<string> Calls { get; } = [];

    /// <summary>
    ///     Gets the call that throws <see cref="StepException"/> or <see langword="null"/> for none.
    ///     A method name such as <c>BuildAsync</c> fails every call of that method and a full line such as
    ///     <c>CheckAsync folder</c> fails 1 call.
    /// </summary>
    public string? FailingStep { get; init; }

    /// <summary>
    ///     Gets the exception that <see cref="FailingStep"/> throws or <see langword="null"/> for an <see cref="InvalidOperationException"/>.
    /// </summary>
    public Exception? StepException { get; init; }

    /// <summary>
    ///     Gets the players that <see cref="ListPlayers"/> returns or <see langword="null"/> to list them from the option.
    /// </summary>
    public IReadOnlyList<string>? ListedPlayers { get; init; }

    /// <summary>
    ///     Gets the folders that <see cref="BuildAsync"/> returns or <see langword="null"/> to return the ones it builds.
    /// </summary>
    public IReadOnlyList<string>? ReturnedFolders { get; init; }

    /// <summary>
    ///     Gets the progress that <see cref="InstallAsync"/> reports once or <see langword="null"/> for none.
    /// </summary>
    public TransferProgress? Progress { get; init; }

    /// <summary>
    ///     Gets or sets the value that <see cref="AddKeyInputs"/> adds as the input <c>extra</c>, so a test changes the key
    ///     from the toolchain side.
    /// </summary>
    public string ExtraKeyInput { get; set; } = "";

    /// <summary>
    ///     Gets the version that <see cref="ListPlayers"/> lists and <see cref="AddKeyInputs"/> adds and returns or
    ///     <see langword="null"/> to use the typed one. A test sets it to check that the paths use the pinned spelling.
    /// </summary>
    public string? PinnedVersion { get; init; }

    /// <summary>
    ///     Gets the version that <see cref="AddKeyInputs"/> adds as the input <c>version</c> or <see langword="null"/> to add
    ///     the one it returns. A test sets it to check that a mismatch throws.
    /// </summary>
    public string? HashedVersion { get; init; }

    /// <inheritdoc/>
    public string Name => name;

    /// <inheritdoc/>
    public IReadOnlyList<Option> Options => [_variantOption];

    /// <inheritdoc/>
    /// <remarks>
    ///     Without <see cref="ListedPlayers"/>, the list has the pinned version without any variant and otherwise
    ///     <c>&lt;pinned&gt;-&lt;variant&gt;</c> per variant, in typed order.
    /// </remarks>
    public IReadOnlyList<string> ListPlayers(string version, ParseResult parseResult)
    {
        if (ListedPlayers is { } listed)
        {
            return listed;
        }

        string pinned = PinnedVersion ?? version;

        return parseResult.GetValue(_variantOption) is { Length: > 0 } variants
            ? [.. variants.Select(variant => $"{pinned}-{variant}")]
            : [pinned];
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     The inputs are every file of the project folder as it is on disk, the version and <see cref="ExtraKeyInput"/>.
    /// </remarks>
    public string AddKeyInputs(CacheKeyBuilder builder, string player, string projectDirectory)
    {
        string pinned = PinnedVersion ?? player;

        // Skip Record, because every build computes the key and the tests of a cache hit expect no calls.
        _ = builder
            .AddDirectory("project", projectDirectory)
            .Add("version", HashedVersion ?? pinned)
            .Add("extra", ExtraKeyInput);

        return pinned;
    }

    /// <inheritdoc/>
    public Task<string> InstallAsync(
        string player,
        string outputDirectory,
        IProgress<TransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        Record($"{nameof(InstallAsync)} {player} {outputDirectory}");

        if (Progress is { } value)
        {
            progress?.Report(value);
        }

        string install = Path.Combine(outputDirectory, "install");
        _ = Directory.CreateDirectory(install);

        return Task.FromResult(install);
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     Each player gets the folder <c>&lt;install&gt;/players/&lt;player&gt;</c> with <c>file.bin</c>.
    /// </remarks>
    public Task<IReadOnlyList<string>> BuildAsync(
        string installDirectory,
        string projectDirectory,
        IReadOnlyList<string> players,
        CancellationToken cancellationToken)
    {
        Record($"{nameof(BuildAsync)} {installDirectory} {projectDirectory} {string.Join(',', players)}");

        string[] folders = new string[players.Count];

        for (int i = 0; i < folders.Length; i++)
        {
            folders[i] = Path.Combine(installDirectory, "players", players[i]);
            TestFolder.WriteBytes(folders[i], "file.bin", Encoding.UTF8.GetBytes(players[i]));
        }

        return Task.FromResult(ReturnedFolders ?? folders);
    }

    /// <inheritdoc/>
    public Task CheckAsync(string playerDirectory, CancellationToken cancellationToken)
    {
        Record($"{nameof(CheckAsync)} {playerDirectory}");

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Adds <paramref name="call"/> to <see cref="Calls"/> and throws if it or its method is <see cref="FailingStep"/>.
    /// </summary>
    /// <param name="call">The method name and its arguments, separated by spaces.</param>
    /// <exception cref="Exception">The call or its method is <see cref="FailingStep"/>.</exception>
    private void Record(string call)
    {
        Calls.Add(call);

        if (FailingStep is { } step && (call.Equals(step, StringComparison.Ordinal) || call.StartsWith($"{step} ", StringComparison.Ordinal)))
        {
            throw StepException ?? new InvalidOperationException($"{FailingStep} failed in the fake toolchain.");
        }
    }
}
