using AslHelp.Engines.Measuring.Cli.Unreal3;
using System.Collections.Generic;

namespace AslHelp.Engines.Measuring.Cli.Tests.Unreal3;

/// <summary>
///     Describes every offset and class size of a fake build.
/// </summary>
/// <param name="EntryIndex">The offset of the id in a name entry.</param>
/// <param name="EntryName">The offset of the text in a name entry.</param>
/// <param name="Index">The offset of the position of an object.</param>
/// <param name="Outer">The offset of the outer object.</param>
/// <param name="Name">The offset of the name id.</param>
/// <param name="Class">The offset of the class.</param>
/// <param name="Super">The offset of the super struct.</param>
/// <param name="Children">The offset of the 1st field.</param>
/// <param name="Size">The offset of the property size.</param>
/// <param name="Next">The offset of the next field.</param>
/// <param name="ElementSize">The offset of the element size of a property.</param>
/// <param name="Offset">The offset of the offset of a property.</param>
/// <param name="LinkNext">The offset of the next linked property.</param>
/// <param name="PropertySize">The size of the class Property, where the class of an object property sits.</param>
/// <param name="FieldSize">The size of the class Field, where the names of an enum sit.</param>
/// <param name="ObjectSize">The size of the class Object.</param>
/// <param name="StructSize">The size of the class Struct.</param>
/// <param name="StateSize">The size of the class State.</param>
/// <param name="ClassSize">The size of the class Class.</param>
public sealed record FakeLayout(
    int EntryIndex,
    int EntryName,
    int Index,
    int Outer,
    int Name,
    int Class,
    int Super,
    int Children,
    int Size,
    int Next,
    int ElementSize,
    int Offset,
    int LinkNext,
    int PropertySize,
    int FieldSize,
    int ObjectSize,
    int StructSize,
    int StateSize,
    int ClassSize)
{
    /// <summary>
    ///     Gets the layout that UDK 2015-02 has on x86.
    /// </summary>
    public static FakeLayout Expected { get; } = new(
        EntryIndex: 0x08,
        EntryName: 0x10,
        Index: 0x20,
        Outer: 0x28,
        Name: 0x2C,
        Class: 0x34,
        Super: 0x48,
        Children: 0x4C,
        Size: 0x50,
        Next: 0x3C,
        ElementSize: 0x44,
        Offset: 0x60,
        LinkNext: 0x64,
        PropertySize: 0x6C,
        FieldSize: 0x40,
        ObjectSize: 0x3C,
        StructSize: 0x90,
        StateSize: 0xC4,
        ClassSize: 0xF0);

    /// <summary>
    ///     Gets a layout with every offset moved, the super struct behind the children and the link below the offset.
    /// </summary>
    public static FakeLayout Shifted { get; } = new(
        EntryIndex: 0x04,
        EntryName: 0x0C,
        Index: 0x14,
        Outer: 0x24,
        Name: 0x30,
        Class: 0x3C,
        Super: 0x5C,
        Children: 0x54,
        Size: 0x64,
        Next: 0x44,
        ElementSize: 0x4C,
        Offset: 0x70,
        LinkNext: 0x50,
        PropertySize: 0x78,
        FieldSize: 0x48,
        ObjectSize: 0x44,
        StructSize: 0xA0,
        StateSize: 0xD0,
        ClassSize: 0xFC);

    /// <summary>
    ///     Creates the offsets a measurement of this layout has to return.
    /// </summary>
    /// <returns>
    ///     The offset of each member.
    /// </returns>
    public Dictionary<string, int> ToOffsets()
    {
        return new Dictionary<string, int>
        {
            [UdkMembers.Children] = Children,
            [UdkMembers.ElementSize] = ElementSize,
            [UdkMembers.EntryIndex] = EntryIndex,
            [UdkMembers.EntryName] = EntryName,
            [UdkMembers.EnumNames] = FieldSize,
            [UdkMembers.Next] = Next,
            [UdkMembers.ObjectClass] = Class,
            [UdkMembers.ObjectInternalInteger] = Index,
            [UdkMembers.ObjectName] = Name,
            [UdkMembers.ObjectOuter] = Outer,
            [UdkMembers.Offset] = Offset,
            [UdkMembers.PropertyClass] = PropertySize,
            [UdkMembers.PropertyLinkNext] = LinkNext,
            [UdkMembers.PropertySize] = Size,
            [UdkMembers.SuperField] = Super,
        };
    }
}
