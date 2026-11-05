using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

/// <summary>
///     Records every call in order and installs a folder with 1 file.
/// </summary>
/// <param name="name">The name of the toolchain.</param>
internal sealed class FakeToolchain(
    string name) : IToolchain
{
    /// <summary>
    ///     Gets the calls in order, such as <c>InstallAsync 1.0 output</c>.
    /// </summary>
    public List<string> Calls { get; } = [];

    /// <summary>
    ///     Gets the name of the method that throws an <see cref="InvalidOperationException"/> or <see langword="null"/> for none.
    /// </summary>
    public string? FailingStep { get; init; }

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
    ///     Gets the version that <see cref="AddKeyInputs"/> adds and returns or <see langword="null"/> to use the typed one.
    ///     A test sets it to check that the paths use the pinned spelling.
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
    public string AddKeyInputs(CacheKeyBuilder builder, string version)
    {
        string pinned = PinnedVersion ?? version;

        // Skip Record, because every build computes the key and the tests of a cache hit expect no calls.
        _ = builder.Add("version", HashedVersion ?? pinned).Add("extra", ExtraKeyInput);

        return pinned;
    }

    /// <inheritdoc/>
    public Task<string> InstallAsync(
        string version,
        string outputDirectory,
        IProgress<TransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        Record($"{nameof(InstallAsync)} {version} {outputDirectory}");

        if (Progress is { } value)
        {
            progress?.Report(value);
        }

        string install = Path.Combine(outputDirectory, "install");
        TestFolder.WriteBytes(install, "file.bin", [1, 2, 3]);

        return Task.FromResult(install);
    }

    /// <inheritdoc/>
    public Task BuildAsync(string installDirectory, string projectDirectory, CancellationToken cancellationToken)
    {
        Record($"{nameof(BuildAsync)} {installDirectory} {projectDirectory}");

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task CheckAsync(string installDirectory, CancellationToken cancellationToken)
    {
        Record($"{nameof(CheckAsync)} {installDirectory}");

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Adds <paramref name="call"/> to <see cref="Calls"/> and throws if its method is <see cref="FailingStep"/>.
    /// </summary>
    /// <param name="call">The method name and its arguments, separated by spaces.</param>
    /// <exception cref="InvalidOperationException">The method is <see cref="FailingStep"/>.</exception>
    private void Record(string call)
    {
        Calls.Add(call);

        if (FailingStep is not null && call.StartsWith($"{FailingStep} ", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{FailingStep} failed in the fake toolchain.");
        }
    }
}
