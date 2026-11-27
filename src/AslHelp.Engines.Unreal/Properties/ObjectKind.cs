using AslHelp.Engines.Unreal.Errors;
using AslHelp.Engines.Unreal.Structs;
using AslHelp.Memory;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Represents the kind whose value is a pointer to an object, which a path goes on through.
/// </summary>
internal sealed class ObjectKind : PropertyKind
{
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

    /// <inheritdoc/>
    public override Result<PropertyAccess> Bind(UnrealProperty property, ClassReader classReader)
    {
        return new ObjectAccess(property);
    }
}

file sealed class ObjectAccess(
    UnrealProperty property) : ValueAccess<Address>(property)
{
    public override Result<Address> Read(IMemoryReader memory, Address container, Address last)
    {
        var address = container + Property.Offset;

        return memory.ReadPointer(address).TryUnwrap(out var target, out var error)
            ? target
            : ValueError.Unreadable(container, Property.Name, address, error);
    }

    public override Result<Container> Step(IMemoryReader memory, Address container)
    {
        return Read(memory, container, Address.Zero).TryUnwrap(out var target, out var error)
            ? new Container(target, null)
            : error;
    }
}
