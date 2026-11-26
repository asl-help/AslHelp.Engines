using AslHelp.Engines.Unreal.Errors;
using AslHelp.Memory;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal;

/// <summary>
///     Represents 1 segment of a <see cref="MemberPath"/>: the global it starts at or a member with the property it resolved to for each class.
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

    private MemberHop(string name, string text)
    {
        Name = name;
        _text = text;
    }

    /// <summary>
    ///     Gets the name of the global or the member.
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     Creates the hop for the global a path starts at.
    /// </summary>
    /// <param name="name">The name of the global, like <c>GWorld</c>.</param>
    /// <returns>
    ///     The hop.
    /// </returns>
    public static MemberHop Global(string name)
    {
        return new MemberHop(name, name);
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
    ///     Returns the hop as a script writes it: the name of the global or a member with its period, like <c>.Hp</c>.
    /// </summary>
    /// <returns>
    ///     The text of the hop.
    /// </returns>
    public override string ToString()
    {
        return _text;
    }
}
