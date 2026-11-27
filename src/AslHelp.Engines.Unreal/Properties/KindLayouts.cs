using System.Collections.Generic;

namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Represents the layout of each kind of <see cref="PropertyKinds"/> that reads offsets from a profile document.
/// </summary>
/// <remarks>
///     Each layout is a failure with the missing paths when the document doesn't have it,
///     so a document without them still attaches and only reads of that kind fail.
/// </remarks>
internal sealed class KindLayouts
{
    private readonly Dictionary<PropertyKind, Result<object>> _layouts;

    private KindLayouts(Dictionary<PropertyKind, Result<object>> layouts)
    {
        _layouts = layouts;
    }

    /// <summary>
    ///     Reads the layout of every kind from <paramref name="profile"/>.
    /// </summary>
    /// <param name="profile">The offsets of the document.</param>
    /// <returns>
    ///     The layouts.
    /// </returns>
    public static KindLayouts Read(KindProfile profile)
    {
        Dictionary<PropertyKind, Result<object>> layouts = [];

        foreach (var kind in PropertyKinds.All)
        {
            if (kind.ReadLayout(profile) is { } layout)
            {
                layouts.Add(kind, layout);
            }
        }

        return new KindLayouts(layouts);
    }

    /// <summary>
    ///     Gets the layout of <paramref name="kind"/>.
    /// </summary>
    /// <typeparam name="TLayout">The type of the layout.</typeparam>
    /// <param name="kind">The kind.</param>
    /// <returns>
    ///     The layout or the failure with the offsets that the document doesn't have.
    /// </returns>
    public Result<TLayout> Find<TLayout>(LayoutKind<TLayout> kind)
        where TLayout : notnull
    {
        return _layouts[kind].Map(static layout => (TLayout)layout);
    }
}
