using System.Collections.Generic;
using System.Text.Json.Serialization.Metadata;

namespace AslHelp.Engines.Fixtures.Cli;

/// <summary>
///     Describes 1 row of the release file of a toolchain, which <see cref="EmbeddedReleases{TRow}"/> reads.
/// </summary>
/// <typeparam name="TRow">The type of the row.</typeparam>
internal interface IReleaseRow<TRow>
    where TRow : class, IReleaseRow<TRow>
{
    /// <summary>
    ///     Gets the generated reader of an array of rows.
    /// </summary>
    static abstract JsonTypeInfo<TRow[]> ArrayTypeInfo { get; }

    /// <summary>
    ///     Gets the name of the toolchain in messages, such as the product name of the engine.
    /// </summary>
    static abstract string DisplayName { get; }

    /// <summary>
    ///     Gets the path of the release file in the repo, for messages, such as <c>eng/fixtures/&lt;engine&gt;/releases.json</c>.
    /// </summary>
    static abstract string ReleasesPath { get; }

    /// <summary>
    ///     Gets the name of the embedded release file in the assembly of <typeparamref name="TRow"/>.
    /// </summary>
    static abstract string ReleasesResource { get; }

    /// <summary>
    ///     Gets the spellings that only this row may have, such as its version. Every spelling that <see cref="Matches"/> takes is 1 of them.
    /// </summary>
    IReadOnlyList<string> Keys { get; }

    /// <summary>
    ///     Gets the spelling that the message of an unpinned version lists for this row.
    /// </summary>
    string Pin { get; }

    /// <summary>
    ///     Gets the sha1 that pins the download of the row, as 40 lowercase hex chars.
    /// </summary>
    string Sha1 { get; }

    /// <summary>
    ///     Gets the pinned version of the row, for messages.
    /// </summary>
    string Version { get; }

    /// <summary>
    ///     Checks whether <paramref name="spelling"/> is a spelling of this row.
    /// </summary>
    /// <param name="spelling">The version as the user typed it.</param>
    /// <returns>
    ///     <see langword="true"/> if the spelling picks this row.
    /// </returns>
    bool Matches(string spelling);

    /// <summary>
    ///     Throws if a value that only this kind of row has is bad.
    /// </summary>
    /// <remarks>
    ///     <see cref="EmbeddedReleases{TRow}.Read"/> calls it before the shared checks, whose messages name <see cref="Version"/>,
    ///     and before it reads <see cref="Keys"/>. A kind of row without values of its own keeps this empty default.
    /// </remarks>
    /// <exception cref="System.IO.InvalidDataException">A value is bad. The message has <see cref="ReleasesPath"/>.</exception>
    void ThrowIfInvalid()
    {
    }
}
