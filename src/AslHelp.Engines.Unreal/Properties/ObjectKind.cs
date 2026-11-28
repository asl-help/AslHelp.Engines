using AslHelp.Engines.Unreal.Errors;
using AslHelp.Engines.Unreal.Structs;
using AslHelp.Memory;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Represents the kind whose value is a pointer to an object, which a path goes on through.
/// </summary>
/// <remarks>
///     A profile without the offset of the declared class still reads every object property. Only a step after a null object stays unchecked.
/// </remarks>
internal sealed class ObjectKind : LayoutKind<ObjectPropertyLayout>
{
    private const string ClassName = "ObjectProperty";

    // Read ClassProperty as a pointer to the class object, the Ptr classes because 5.0 to 5.3 print TObjectPtr properties with them
    // and ComponentProperty because Unreal Engine 3 prints component pointers with it.
    /// <inheritdoc/>
    public override IReadOnlyList<string> ClassNames { get; } =
        ["ClassProperty", "ClassPtrProperty", "ComponentProperty", "ObjectProperty", "ObjectPtrProperty"];

    /// <inheritdoc/>
    public override TResult Accept<TResult>(PropertyVisitor<TResult> visitor, UnrealProperty property)
    {
        return visitor.VisitValue<Address>(property);
    }

    /// <summary>
    ///     Reads the offset of the declared class in an object property, which only some profiles have.
    /// </summary>
    /// <param name="profile">The offsets of the document.</param>
    /// <returns>
    ///     The layout, also when the document doesn't have the offset.
    /// </returns>
    public override Result<ObjectPropertyLayout> Read(KindProfile profile)
    {
        return new ObjectPropertyLayout(ReadPropertyClassOffset(profile));
    }

    /// <summary>
    ///     Reads the offset of the pointer to the declared class in an object property, which weak pointers share.
    /// </summary>
    /// <param name="profile">The offsets of the document.</param>
    /// <returns>
    ///     The offset or <see langword="null"/> when the document doesn't have it.
    /// </returns>
    public static int? ReadPropertyClassOffset(KindProfile profile)
    {
        return profile.Read(profile.PropertyPrefix + ClassName, "PropertyClass");
    }

    /// <inheritdoc/>
    protected override Result<PropertyAccess> Bind(UnrealProperty property, ClassReader classReader, ObjectPropertyLayout layout)
    {
        return new ObjectAccess(property, classReader, layout.PropertyClass);
    }
}

file sealed class ObjectAccess(
    UnrealProperty property,
    ClassReader classReader,
    int? propertyClass) : PointerAccess(property, classReader, propertyClass)
{
    public override Result<Address> Read(IMemoryReader memory, Address container, Address last)
    {
        var address = container + Property.Offset;

        return memory.ReadPointer(address).TryUnwrap(out var target, out var error)
            ? target
            : ValueError.Unreadable(container, Property.Name, address, error);
    }
}
