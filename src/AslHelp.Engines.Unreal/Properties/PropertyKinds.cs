using AslHelp.Engines.Unreal.Errors;
using AslHelp.Engines.Unreal.Structs;
using System;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Provides the kind of each property class that the library reads.
/// </summary>
internal static class PropertyKinds
{
    private static readonly PropertyKind[] _all =
    [
        new NumberKind<sbyte>("Int8Property"),
        new NumberKind<short>("Int16Property"),
        new NumberKind<int>("IntProperty"),
        new NumberKind<long>("Int64Property"),
        new NumberKind<byte>("ByteProperty"),
        new NumberKind<ushort>("UInt16Property"),
        new NumberKind<uint>("UInt32Property"),
        new NumberKind<ulong>("UInt64Property"),
        new NumberKind<float>("FloatProperty"),
        new NumberKind<double>("DoubleProperty"),
        new NumberKind<double>("LargeWorldCoordinatesRealProperty"),
        new BoolKind(),
        new EnumKind(),
        new NameKind(),
        new StringKind(),
        new ObjectKind(),
        new WeakObjectKind(),
        new StructKind(),
        new ArrayKind(),
        new SetKind(),
        new MapKind(),
    ];

    private static readonly Dictionary<string, PropertyKind> _kinds = Map(_all);

    private static readonly PropertyKind _unsupported = new UnsupportedKind();

    /// <summary>
    ///     Gets every kind that the library reads, so the profile reads the offsets of each one.
    /// </summary>
    public static IReadOnlyList<PropertyKind> All => _all;

    /// <summary>
    ///     Finds the kind of the property class <paramref name="className"/>.
    /// </summary>
    /// <remarks>
    ///     Compares ordinally.
    /// </remarks>
    /// <param name="className">The property class without a prefix, like <c>IntProperty</c>.</param>
    /// <returns>
    ///     The kind or a kind whose reads fail with the class when the library doesn't read the class.
    /// </returns>
    public static PropertyKind Find(string className)
    {
        return _kinds.TryGetValue(className, out var kind) ? kind : _unsupported;
    }

    private static Dictionary<string, PropertyKind> Map(PropertyKind[] kinds)
    {
        Dictionary<string, PropertyKind> map = [];

        foreach (var kind in kinds)
        {
            foreach (string className in kind.ClassNames)
            {
                map.Add(className, kind);
            }
        }

        return map;
    }
}

file sealed class UnsupportedKind : PropertyKind
{
    public override IReadOnlyList<string> ClassNames { get; } = [];

    public override TResult Accept<TResult>(PropertyVisitor<TResult> visitor, UnrealProperty property)
    {
        return visitor.VisitUnsupported(property);
    }

    public override Result<PropertyAccess> Bind(UnrealProperty property, ClassReader classReader)
    {
        return new FailedAccess(property, ValueError.Unsupported(property.Name, property.ClassName));
    }
}
