using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

/// <summary>
///     Describes a release row with a version and an alias as its keys, read from <c>fake-releases.json</c>.
/// </summary>
/// <param name="Version">The version.</param>
/// <param name="Alias">A 2nd spelling of the row.</param>
/// <param name="Sha1">The sha1 of the download.</param>
internal sealed record FakeRelease(
    string Version,
    string Alias,
    string Sha1) : IReleaseRow<FakeRelease>
{
    /// <summary>
    ///     Represents the alias that fails the check of the row.
    /// </summary>
    public const string InvalidAlias = "invalid";

    /// <inheritdoc/>
    public static JsonTypeInfo<FakeRelease[]> ArrayTypeInfo => FakeReleaseContext.Default.FakeReleaseArray;

    /// <inheritdoc/>
    public static string DisplayName => "Fake";

    /// <inheritdoc/>
    public static string ReleasesPath => "eng/fixtures/fake/releases.json";

    /// <inheritdoc/>
    public static string ReleasesResource => "fake-releases.json";

    /// <inheritdoc/>
    [JsonIgnore]
    public IReadOnlyList<string> Keys => [Version, Alias];

    /// <inheritdoc/>
    [JsonIgnore]
    public string Pin => Version;

    /// <inheritdoc/>
    public bool Matches(string spelling)
    {
        return spelling.Equals(Version, StringComparison.OrdinalIgnoreCase) || spelling.Equals(Alias, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public void ThrowIfInvalid()
    {
        if (Alias == InvalidAlias)
        {
            throw new InvalidDataException($"{ReleasesPath} has a bad alias.");
        }
    }
}
