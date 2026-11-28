namespace AslHelp.Engines.Profiles;

/// <summary>
///     Represents the profile that a <see cref="ProfileCatalog{TProfile}"/> picked for a target.
/// </summary>
/// <typeparam name="TProfile">The profile type of the engine.</typeparam>
/// <param name="Target">The target.</param>
/// <param name="Entry">The picked document.</param>
/// <param name="Version">The listed version of <paramref name="Entry"/> that the pick went by, which can be the line of the target.</param>
/// <param name="Match">How the catalog picked the document.</param>
public sealed record ProfileSelection<TProfile>(
    ProfileTarget Target,
    ProfileEntry<TProfile> Entry,
    ProfileVersion Version,
    ProfileMatch Match)
{
    /// <summary>
    ///     Gets the warning to show the user when no document lists the version of the target, with both versions in it.
    /// </summary>
    /// <value>
    ///     The warning or <see langword="null"/> for <see cref="ProfileMatch.Listed"/>.
    /// </value>
    public string? Warning => Match switch
    {
        ProfileMatch.SameMinor => SR.FormatSelectionSameMinor(Target, Version),
        ProfileMatch.LowerVersion => SR.FormatSelectionLowerVersion(Target, Version),
        _ => null,
    };
}
