using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AslHelp.Engines.Fixtures.Cli;

/// <summary>
///     Identifies 1 player by its key, its toolchain and the version the toolchain pins, with the inputs the key hashed.
/// </summary>
/// <remarks>
///     <see cref="PlayerCache"/> builds the player folder and the store paths from it and writes it as the manifest.
///     It is a class and not a record, because record equality would compare <see cref="Inputs"/> by reference.
/// </remarks>
/// <param name="key">The key of the player.</param>
/// <param name="toolchain">The name of the toolchain that builds the player.</param>
/// <param name="version">The version as the toolchain pins it.</param>
/// <param name="inputs">The inputs the key hashed, in hashed order.</param>
internal sealed class PlayerKey(
    CacheKey key,
    string toolchain,
    string version,
    IReadOnlyList<CacheKeyInput> inputs)
{
    /// <summary>
    ///     Gets the key of the player.
    /// </summary>
    [JsonIgnore]
    public CacheKey Key { get; } = key;

    /// <summary>
    ///     Gets the key as its kind, '-' and all 64 hex chars of its hash, as the manifest lists it.
    /// </summary>
    [JsonPropertyName("key")]
    public string FullKey => $"{Key.Kind}-{Key.Hash}";

    /// <summary>
    ///     Gets the name of the toolchain that builds the player.
    /// </summary>
    public string Toolchain { get; } = toolchain;

    /// <summary>
    ///     Gets the version as the toolchain pins it, in the spelling of its release list and not the typed one.
    /// </summary>
    public string Version { get; } = version;

    /// <summary>
    ///     Gets the inputs the key hashed, in hashed order.
    /// </summary>
    public IReadOnlyList<CacheKeyInput> Inputs { get; } = inputs;
}
