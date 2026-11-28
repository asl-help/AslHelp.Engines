namespace AslHelp.Engines.Profiles;

/// <summary>
///     Specifies what <see cref="ProfileCatalog{TProfile}.Select"/> does when no document lists the version of the target or its line.
/// </summary>
public enum ProfileGuessHandling
{
    /// <summary>
    ///     Fail with the target and the nearest listed version.
    /// </summary>
    Disallow,

    /// <summary>
    ///     Take the nearest listed version, with <see cref="ProfileSelection{TProfile}.Warning"/> set.
    /// </summary>
    Allow,
}
