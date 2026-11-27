using AslHelp.Engines.Unreal.Errors;
using AslHelp.Engines.Unreal.Properties;
using AslHelp.Memory;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal.Paths;

/// <summary>
///     Represents 1 segment of a <see cref="MemberPath"/>, which is the root it starts at,
///     a member with the property it resolved to for each class or an index of a collection.
/// </summary>
/// <remarks>
///     Every path and chain that extends another one shares its hops, so a class gets resolved once per hop.
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

    private MemberHop(string name, string text, ElementKey? key = null)
    {
        Name = name;
        _text = text;
        Key = key;
    }

    /// <summary>
    ///     Gets the index or key of an index hop or <see langword="null"/> for the root or a member.
    /// </summary>
    public ElementKey? Key { get; }

    /// <summary>
    ///     Gets the text of the root, the name of the member or the text of the index.
    /// </summary>
    public string Name { get; }

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
    /// <param name="key">The index or key, which is an integer, a <see cref="bool"/> or a <see cref="string"/>.</param>
    /// <returns>
    ///     The hop, whose text is the key in brackets, like <c>[2]</c> or <c>["two"]</c>.
    /// </returns>
    public static MemberHop Index(object key)
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
    /// <returns>
    ///     The property or a failure when the class doesn't have it or a read of its properties fails.
    /// </returns>
    public Result<UnrealProperty> Resolve(UnrealClass klass)
    {
        if (_properties.TryGetValue(klass.Address, out var kept) && kept.Class == klass)
        {
            return kept.Property;
        }

        if (!klass.FindPropertyOrNull(Name).TryUnwrap(out var property, out var error))
        {
            return error;
        }

        Result<UnrealProperty> resolved = property is null ? ClassError.PropertyNotFound(klass.Name, Name) : property;
        _properties[klass.Address] = (klass, resolved);

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
