using AslHelp.Memory;

namespace AslHelp.Engines.Unreal;

/// <summary>
///     Represents the storage shape of the properties that a profile lists and creates the <see cref="IPropertyReader"/> of that shape.
/// </summary>
internal abstract class PropertyShape
{
    /// <summary>
    ///     Creates the shape of properties that aren't objects, which is <c>ffield</c> in a profile.
    /// </summary>
    /// <param name="structLayout">The offsets inside a struct.</param>
    /// <param name="fieldLayout">The offsets inside a property that come from its base field.</param>
    /// <param name="classLayout">The offsets inside a field class.</param>
    /// <param name="layout">The offsets inside a property.</param>
    /// <returns>
    ///     The shape.
    /// </returns>
    public static PropertyShape Field(
        FieldStructLayout structLayout,
        FieldPropertyLayout fieldLayout,
        FieldClassLayout classLayout,
        PropertyLayout layout)
    {
        return new FieldPropertyShape(structLayout, fieldLayout, classLayout, layout);
    }

    /// <summary>
    ///     Creates the shape of properties that are objects, which is <c>uproperty</c> in a profile.
    /// </summary>
    /// <param name="layout">The offsets inside a property.</param>
    /// <returns>
    ///     The shape.
    /// </returns>
    public static PropertyShape Object(PropertyLayout layout)
    {
        return new ObjectPropertyShape(layout);
    }

    /// <summary>
    ///     Creates the property reader of this shape.
    /// </summary>
    /// <param name="memory">The memory to read from.</param>
    /// <param name="names">The name table of the game.</param>
    /// <param name="objects">The object reader of the game.</param>
    /// <param name="structs">The struct reader of the game.</param>
    /// <returns>
    ///     The property reader.
    /// </returns>
    public abstract IPropertyReader Create(IMemoryReader memory, INameTable names, ObjectReader objects, StructReader structs);
}

file sealed class FieldPropertyShape(
    FieldStructLayout structLayout,
    FieldPropertyLayout fieldLayout,
    FieldClassLayout classLayout,
    PropertyLayout layout) : PropertyShape
{
    private readonly FieldClassLayout _classLayout = classLayout;
    private readonly FieldPropertyLayout _fieldLayout = fieldLayout;
    private readonly PropertyLayout _layout = layout;
    private readonly FieldStructLayout _structLayout = structLayout;

    public override IPropertyReader Create(IMemoryReader memory, INameTable names, ObjectReader objects, StructReader structs)
    {
        return new FieldPropertyReader(memory, names, _structLayout, _fieldLayout, _classLayout, _layout);
    }
}

file sealed class ObjectPropertyShape(
    PropertyLayout layout) : PropertyShape
{
    private readonly PropertyLayout _layout = layout;

    public override IPropertyReader Create(IMemoryReader memory, INameTable names, ObjectReader objects, StructReader structs)
    {
        return new ObjectPropertyReader(memory, objects, structs, _layout);
    }
}
