using AslHelp.Engines.Unreal.Errors;
using AslHelp.Engines.Unreal.Objects;
using AslHelp.Engines.Unreal.Structs;
using AslHelp.Memory;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Represents the kind of a <c>TWeakObjectPtr</c>, which reads as the address of its object through the object array.
/// </summary>
/// <remarks>
///     Reads <see cref="Address.Zero"/> when the pointer is empty or the serial number of the item doesn't match,
///     so a path past a stale pointer fails like one past a null object.
/// </remarks>
internal sealed class WeakObjectKind : LayoutKind<WeakObjectLayout>
{
    private const string ClassName = "WeakObjectProperty";

    /// <inheritdoc/>
    public override IReadOnlyList<string> ClassNames { get; } = [ClassName];

    /// <inheritdoc/>
    public override TResult Accept<TResult>(PropertyVisitor<TResult> visitor, UnrealProperty property)
    {
        return visitor.VisitValue<Address>(property);
    }

    /// <summary>
    ///     Reads the offsets of the index and the serial number inside an <c>FWeakObjectPtr</c>.
    /// </summary>
    /// <param name="profile">The offsets of the document.</param>
    /// <returns>
    ///     The layout or a failure with the missing offsets.
    /// </returns>
    public override Result<WeakObjectLayout> Read(KindProfile profile)
    {
        var offsets = profile.For(ClassName);
        int objectIndex = offsets.Read("FWeakObjectPtr", "ObjectIndex");
        int objectSerialNumber = offsets.Read("FWeakObjectPtr", "ObjectSerialNumber");

        return offsets.Finish(new WeakObjectLayout(objectIndex, objectSerialNumber));
    }

    /// <inheritdoc/>
    protected override Result<PropertyAccess> Bind(UnrealProperty property, ClassReader classReader, WeakObjectLayout layout)
    {
        return new WeakObjectAccess(property, layout, classReader.ObjectArray);
    }
}

file sealed class WeakObjectAccess(
    UnrealProperty property,
    WeakObjectLayout layout,
    IObjectArray objectArray) : ValueAccess<Address>(property)
{
    private readonly WeakObjectLayout _layout = layout;
    private readonly IObjectArray _objectArray = objectArray;

    private ValueError? _serialNumbersError;

    public override bool StepReadsMemory => true;

    public override Result<Address> Read(IMemoryReader memory, Address container, Address last)
    {
        var address = container + Property.Offset;
        var indexAddress = address + _layout.ObjectIndex;

        if (!memory.Read<int>(indexAddress).TryUnwrap(out int index, out var error))
        {
            return ValueError.Unreadable(container, Property.Name, indexAddress, error);
        }

        var serialAddress = address + _layout.ObjectSerialNumber;

        if (!memory.Read<int>(serialAddress).TryUnwrap(out int serial, out error))
        {
            return ValueError.Unreadable(container, Property.Name, serialAddress, error);
        }

        // Read an index below 0 or a serial number of 0 as empty without reading the object array, because the engine does the same.
        if (index < 0 || serial == 0)
        {
            return Address.Zero;
        }

        if (!_objectArray.ReadItem(index).TryUnwrap(out var item, out error))
        {
            return error;
        }

        if (item.Address == Address.Zero)
        {
            return Address.Zero;
        }

        if (item.SerialNumber is not { } itemSerial)
        {
            return _serialNumbersError ??= ValueError.WeakSerialNumbersMissing(Property.Name);
        }

        return itemSerial == serial ? item.Address : Address.Zero;
    }

    public override Result<Container> Step(IMemoryReader memory, Address container)
    {
        return Read(memory, container, Address.Zero).TryUnwrap(out var target, out var error)
            ? new Container(target, null)
            : error;
    }
}
