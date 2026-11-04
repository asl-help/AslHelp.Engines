namespace AslHelp.Engines.Fixtures;

/// <summary>
///     Represents 1 input of a <see cref="CacheKey"/> as <see cref="CacheKeyBuilder.Build"/> hashed it.
/// </summary>
/// <param name="Name">The name of the input, in lowercase ascii letters, digits and '-'.</param>
/// <param name="Value">
///     The text of a value input or, for a folder input, the sha256 over the folder payload as 64 lowercase hex chars.
/// </param>
public sealed record CacheKeyInput(
    string Name,
    string Value);
