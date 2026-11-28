using System.Collections.Generic;

namespace AslHelp.Engines.Profiles;

/// <summary>
///     Represents 1 document of a <see cref="ProfileCatalog{TProfile}"/> with the profile its mapper made of it.
/// </summary>
/// <typeparam name="TProfile">The profile type of the engine.</typeparam>
public sealed class ProfileEntry<TProfile>
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ProfileEntry{TProfile}"/> class.
    /// </summary>
    /// <param name="engine">The value of <c>engine</c>.</param>
    /// <param name="key">The value of <c>key</c> or <see langword="null"/> for a document without a key.</param>
    /// <param name="versions">The versions under <c>versions</c>.</param>
    /// <param name="profile">The profile.</param>
    /// <param name="source">The file path, the resource name or the name passed with the text or stream of the document.</param>
    internal ProfileEntry(string engine, string? key, IReadOnlyList<ProfileVersion> versions, TProfile profile, string source)
    {
        Engine = engine;
        Key = key;
        Versions = versions;
        Profile = profile;
        Source = source;
    }

    /// <summary>
    ///     Gets the value of <c>engine</c>.
    /// </summary>
    public string Engine { get; }

    /// <summary>
    ///     Gets the value of <c>key</c> or <see langword="null"/> for a document without a key, which fits every key of its engine.
    /// </summary>
    public string? Key { get; }

    /// <summary>
    ///     Gets the profile.
    /// </summary>
    public TProfile Profile { get; }

    /// <summary>
    ///     Gets the file path, the resource name or the name passed with the text or stream of the document.
    /// </summary>
    public string Source { get; }

    /// <summary>
    ///     Gets the versions under <c>versions</c> in the order of the document.
    /// </summary>
    public IReadOnlyList<ProfileVersion> Versions { get; }
}
