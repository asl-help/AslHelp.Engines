using System;

namespace AslHelp.Engines.Fixtures.Cli;

/// <summary>
///     Describes 1 file of an archive.org item.
/// </summary>
/// <param name="Item">The item identifier.</param>
/// <param name="Name">The file name within the item.</param>
/// <param name="Hash">The sha1 from the item's listing.</param>
internal sealed record ArchiveOrgFile(
    string Item,
    string Name,
    PinnedHash Hash)
{
    /// <summary>
    ///     Gets the download URL, <c>https://archive.org/download/&lt;item&gt;/&lt;name&gt;</c>.
    /// </summary>
    public Uri Url => new($"https://archive.org/download/{Uri.EscapeDataString(Item)}/{Uri.EscapeDataString(Name)}");
}
