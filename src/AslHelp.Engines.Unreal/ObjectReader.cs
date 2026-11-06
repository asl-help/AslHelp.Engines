using AslHelp.Engines.Unreal.Errors;
using AslHelp.Memory;
using System;

namespace AslHelp.Engines.Unreal;

/// <summary>
///     Provides reads of the name, the class and the outer object of an object.
/// </summary>
/// <remarks>
///     Reads the memory on every call, because the game can free an object and reuse its address.
/// </remarks>
/// <param name="memory">The memory to read from.</param>
/// <param name="names">The table that the name ids of the objects point into.</param>
/// <param name="layout">The offsets inside an object.</param>
/// <exception cref="ArgumentNullException">
///     <paramref name="memory"/>, <paramref name="names"/> or <paramref name="layout"/> is <see langword="null"/>.
/// </exception>
public sealed class ObjectReader(
    IMemoryReader memory,
    FlatNameTable names,
    ObjectLayout layout)
{
    private readonly ObjectLayout _layout = layout ?? throw new ArgumentNullException(nameof(layout));
    private readonly IMemoryReader _memory = memory ?? throw new ArgumentNullException(nameof(memory));
    private readonly FlatNameTable _names = names ?? throw new ArgumentNullException(nameof(names));

    /// <summary>
    ///     Reads the base name of <paramref name="obj"/>, without the number suffix.
    /// </summary>
    /// <remarks>
    ///     Gives the same text for 2 objects whose names differ only in the number.
    /// </remarks>
    /// <param name="obj">The address of the object.</param>
    /// <returns>
    ///     The base name or a failure with the object and the failed read when <paramref name="obj"/> is <see cref="Address.Zero"/>,
    ///     a read fails or the name number is negative.
    /// </returns>
    public Result<string> NameOf(Address obj)
    {
        if (obj == Address.Zero)
        {
            return ObjectError.NullObject();
        }

        var idAddress = obj + _layout.Name;

        if (!_memory.Read<int>(idAddress).TryUnwrap(out int id, out var error))
        {
            return ObjectError.NameUnreadable(obj, idAddress, _layout.Name, error);
        }

        var numberAddress = idAddress + sizeof(int);

        if (!_memory.Read<int>(numberAddress).TryUnwrap(out int number, out error))
        {
            return ObjectError.NameUnreadable(obj, numberAddress, _layout.Name, error);
        }

        if (number < 0)
        {
            return ObjectError.NegativeNumber(obj, number, _layout.Name);
        }

        return _names.Read(id).TryUnwrap(out string? name, out error)
            ? name
            : ObjectError.NameNotFound(obj, id, _layout.Name, error);
    }

    /// <summary>
    ///     Reads the class of <paramref name="obj"/>.
    /// </summary>
    /// <param name="obj">The address of the object.</param>
    /// <returns>
    ///     The address of the class or a failure with the object and the failed read. A pointer of 0 returns <see cref="Address.Zero"/>.
    /// </returns>
    public Result<Address> ClassOf(Address obj)
    {
        var address = obj + _layout.Class;

        return _memory.ReadPointer(address).TryUnwrap(out var klass, out var error)
            ? klass
            : ObjectError.ClassUnreadable(obj, address, _layout.Class, error);
    }

    /// <summary>
    ///     Reads the outer object of <paramref name="obj"/>.
    /// </summary>
    /// <param name="obj">The address of the object.</param>
    /// <returns>
    ///     The address of the outer object or a failure with the object and the failed read. A pointer of 0 returns <see cref="Address.Zero"/>.
    /// </returns>
    public Result<Address> OuterOf(Address obj)
    {
        var address = obj + _layout.Outer;

        return _memory.ReadPointer(address).TryUnwrap(out var outer, out var error)
            ? outer
            : ObjectError.OuterUnreadable(obj, address, _layout.Outer, error);
    }
}
