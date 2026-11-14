using System;

namespace AslHelp.Engines.Fixtures;

/// <summary>
///     Represents 1 file of a file set that <see cref="CacheKeyBuilder.AddFiles"/> hashes.
/// </summary>
/// <param name="RelativePath">The path of the file in the set, with '/' separators.</param>
/// <param name="Content">The bytes of the file.</param>
public sealed record CacheKeyFile(
    string RelativePath,
    ReadOnlyMemory<byte> Content);
