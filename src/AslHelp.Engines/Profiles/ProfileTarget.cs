namespace AslHelp.Engines.Profiles;

/// <summary>
///     Represents the game that a <see cref="ProfileCatalog{TProfile}"/> picks a profile for.
/// </summary>
/// <param name="Engine">The engine as the documents write it under <c>engine</c>, like <c>my-engine</c>.</param>
/// <param name="Version">The engine version of the game, read with the version parser that the catalog uses.</param>
/// <param name="Key">
///     The build of the game as the documents write it under <c>key</c>, like <c>x64</c>,
///     or <see langword="null"/> for an engine whose documents don't have a key.
/// </param>
public sealed record ProfileTarget(
    string Engine,
    ProfileVersion Version,
    string? Key)
{
    /// <summary>
    ///     Returns the engine, the version and the key for a message, like <c>my-engine 1.2.0 x64</c>.
    /// </summary>
    /// <returns>
    ///     The text.
    /// </returns>
    public override string ToString()
    {
        return Key is null ? $"{Engine} {Version}" : $"{Engine} {Version} {Key}";
    }
}
