namespace AslHelp.Engines.Profiles;

/// <summary>
///     Specifies how a <see cref="ProfileCatalog{TProfile}"/> picked a profile.
/// </summary>
public enum ProfileMatch
{
    /// <summary>
    ///     The document lists the version of the game, its line or a version that starts with the game version, like 1.2.4 for 1.2.
    /// </summary>
    Listed,

    /// <summary>
    ///     No document lists the version, so the catalog took the nearest listed version of the same major and minor.
    ///     Also the highest listed version that starts with the game version when those versions sit in more than 1 document.
    /// </summary>
    SameMinor,

    /// <summary>
    ///     No document lists the version or another of its minor, so the catalog took the highest listed version below it.
    /// </summary>
    LowerVersion,
}
