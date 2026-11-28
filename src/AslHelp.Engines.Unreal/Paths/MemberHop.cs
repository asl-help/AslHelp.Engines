using AslHelp.Engines.Unreal.Errors;
using AslHelp.Engines.Unreal.Properties;
using AslHelp.Memory;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal.Paths;

/// <summary>
///     Represents 1 segment of a <see cref="MemberPath"/>, which is the root it starts at,
///     a member with the property it resolved to for each class, an index of a collection or its count.
/// </summary>
/// <remarks>
///     Every path that extends another one shares its hops, so a class gets resolved once per hop.
///     Isn't thread-safe.
/// </remarks>
internal sealed class MemberHop
{
    // Key by address and check the kept class, because UnrealGame gives a new UnrealClass for a class at a reused address.
    private readonly Dictionary<Address, (UnrealClass Class, Result<UnrealProperty> Property)> _properties = [];

    private readonly string _text;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MemberHop"/> class for a member.
    /// </summary>
    /// <param name="name">The name of the member.</param>
    public MemberHop(string name)
        : this(name, "." + name)
    {
    }

    private MemberHop(string name, string text, ElementKey? key = null, bool isCount = false)
    {
        Name = name;
        _text = text;
        Key = key;
        IsCount = isCount;
    }

    /// <summary>
    ///     Gets a value indicating whether the hop reads the number of elements of the collection before it.
    /// </summary>
    public bool IsCount { get; }

    /// <summary>
    ///     Gets the index or key of an index hop or <see langword="null"/> for any other hop.
    /// </summary>
    public ElementKey? Key { get; }

    /// <summary>
    ///     Gets the text of the root, the name of the member, the text of the index or <c>Count</c>.
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     Creates the hop for the number of elements of a collection.
    /// </summary>
    /// <returns>
    ///     The hop, whose text is <c>.Count</c>.
    /// </returns>
    public static MemberHop Count()
    {
        return new MemberHop(CollectionAccess.CountMember, "." + CollectionAccess.CountMember, isCount: true);
    }

    /// <summary>
    ///     Creates the hop for the root a path starts at.
    /// </summary>
    /// <param name="text">The text of the root, like <c>GWorld</c> or the name of a class.</param>
    /// <returns>
    ///     The hop.
    /// </returns>
    public static MemberHop Root(string text)
    {
        return new MemberHop(text, text);
    }

    /// <summary>
    ///     Creates the hop for an index of an array or a key of a set or a map.
    /// </summary>
    /// <param name="key">The index or key.</param>
    /// <returns>
    ///     The hop, whose text is the key in brackets, like <c>[2]</c> or <c>["two"]</c>.
    /// </returns>
    public static MemberHop Index(PathKey key)
    {
        ElementKey elementKey = new(key);

        return new MemberHop(elementKey.Text, $"[{elementKey.Text}]", elementKey);
    }

    /// <summary>
    ///     Finds the property of the member on <paramref name="klass"/>.
    /// </summary>
    /// <remarks>
    ///     Keeps the property or the not-found error for the class. Doesn't keep a failed read of the properties, so the next call reads again.
    /// </remarks>
    /// <param name="klass">The class of the object the member is read from.</param>
    /// <param name="missing"><see langword="true"/> when the class doesn't have the member.</param>
    /// <returns>
    ///     The property or a failure when the class doesn't have it or a read of its properties fails.
    /// </returns>
    public Result<UnrealProperty> Resolve(UnrealClass klass, out bool missing)
    {
        if (_properties.TryGetValue(klass.Address, out var kept) && kept.Class == klass)
        {
            missing = kept.Property.IsErr;

            return kept.Property;
        }

        if (!klass.FindPropertyOrNull(Name).TryUnwrap(out var property, out var error))
        {
            missing = false;

            return error;
        }

        Result<UnrealProperty> resolved = property is null ? ClassError.PropertyNotFound(klass.Name, Name) : property;
        _properties[klass.Address] = (klass, resolved);
        missing = property is null;

        return resolved;
    }

    /// <summary>
    ///     Returns the hop as a script writes it, like <c>GWorld</c>, <c>.Hp</c> or <c>[2]</c>.
    /// </summary>
    /// <returns>
    ///     The text of the hop.
    /// </returns>
    public override string ToString()
    {
        return _text;
    }
}
