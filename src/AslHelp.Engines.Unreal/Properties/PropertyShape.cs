using AslHelp.Engines.Unreal.Names;
using AslHelp.Engines.Unreal.Objects;
using AslHelp.Engines.Unreal.Structs;
using AslHelp.Memory;

namespace AslHelp.Engines.Unreal.Properties;

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
    /// <param name="propertyLayout">The offsets inside a property.</param>
    /// <returns>
    ///     The shape.
    /// </returns>
    public static PropertyShape Field(
        FieldStructLayout structLayout,
        FieldPropertyLayout fieldLayout,
        FieldClassLayout classLayout,
        PropertyLayout propertyLayout)
    {
        return new FieldPropertyShape(structLayout, fieldLayout, classLayout, propertyLayout);
    }

    /// <summary>
    ///     Creates the shape of properties that are objects, which is <c>uproperty</c> in a profile.
    /// </summary>
    /// <param name="propertyLayout">The offsets inside a property.</param>
    /// <returns>
    ///     The shape.
    /// </returns>
    public static PropertyShape Object(PropertyLayout propertyLayout)
    {
        return new ObjectPropertyShape(propertyLayout);
    }

    /// <summary>
    ///     Creates the property reader of this shape.
    /// </summary>
    /// <param name="memory">The memory to read from.</param>
    /// <param name="nameTable">The name table of the game.</param>
    /// <param name="objectReader">The object reader of the game.</param>
    /// <param name="structReader">The struct reader of the game.</param>
    /// <returns>
    ///     The property reader.
    /// </returns>
    public abstract IPropertyReader Create(
        IMemoryReader memory,
        INameTable nameTable,
        ObjectReader objectReader,
        StructReader structReader);
}

file sealed class FieldPropertyShape(
    FieldStructLayout structLayout,
    FieldPropertyLayout fieldLayout,
    FieldClassLayout classLayout,
    PropertyLayout propertyLayout) : PropertyShape
{
    private readonly FieldClassLayout _classLayout = classLayout;
    private readonly FieldPropertyLayout _fieldLayout = fieldLayout;
    private readonly PropertyLayout _propertyLayout = propertyLayout;
    private readonly FieldStructLayout _structLayout = structLayout;

    public override IPropertyReader Create(
        IMemoryReader memory,
        INameTable nameTable,
        ObjectReader objectReader,
        StructReader structReader)
    {
        return new FieldPropertyReader(memory, nameTable, _structLayout, _fieldLayout, _classLayout, _propertyLayout);
    }
}

file sealed class ObjectPropertyShape(
    PropertyLayout propertyLayout) : PropertyShape
{
    private readonly PropertyLayout _propertyLayout = propertyLayout;

    public override IPropertyReader Create(
        IMemoryReader memory,
        INameTable nameTable,
        ObjectReader objectReader,
        StructReader structReader)
    {
        return new ObjectPropertyReader(memory, objectReader, structReader, _propertyLayout);
    }
}
