using AslHelp.Memory;

namespace AslHelp.Engines.Unreal.Objects;

/// <summary>
///     Represents the storage shape of the object array that a profile lists and creates the <see cref="IObjectArray"/> of that shape.
/// </summary>
internal abstract class ObjectArrayShape
{
    /// <summary>
    ///     Creates the shape of chunks of items behind a pointer to the chunk pointers, which is <c>chunked</c> in a profile.
    /// </summary>
    /// <param name="layout">The offsets inside the array.</param>
    /// <param name="item">The offsets inside an item.</param>
    /// <returns>
    ///     The shape.
    /// </returns>
    public static ObjectArrayShape Chunked(ChunkedObjectArrayLayout layout, ObjectItemLayout item)
    {
        return new ChunkedObjectArrayShape(layout, item);
    }

    /// <summary>
    ///     Creates the shape of chunks of pointers behind a table inside the array, which is <c>indirect</c> in a profile.
    /// </summary>
    /// <param name="layout">The offsets inside the array.</param>
    /// <returns>
    ///     The shape.
    /// </returns>
    public static ObjectArrayShape Indirect(IndirectObjectArrayLayout layout)
    {
        return new IndirectObjectArrayShape(layout);
    }

    /// <summary>
    ///     Creates the shape of items behind 1 pointer, which is <c>flat</c> in a profile.
    /// </summary>
    /// <param name="layout">The offsets inside the array.</param>
    /// <param name="item">The offsets inside an item.</param>
    /// <returns>
    ///     The shape.
    /// </returns>
    public static ObjectArrayShape Item(ItemObjectArrayLayout layout, ObjectItemLayout item)
    {
        return new ItemObjectArrayShape(layout, item);
    }

    /// <summary>
    ///     Creates the shape of 1 array of object pointers, which is <c>pointers</c> in a profile.
    /// </summary>
    /// <returns>
    ///     The shape.
    /// </returns>
    public static ObjectArrayShape Pointer()
    {
        return new PointerObjectArrayShape();
    }

    /// <summary>
    ///     Creates the object array of this shape.
    /// </summary>
    /// <param name="memory">The memory to read from.</param>
    /// <param name="array">The address of the array, which is <c>ObjObjects</c> in <c>FUObjectArray</c> or <c>GObjObjects</c> itself.</param>
    /// <returns>
    ///     The object array or a failure when the layout doesn't fit the pointer size of <paramref name="memory"/>.
    /// </returns>
    public abstract Result<IObjectArray> Create(IMemoryReader memory, Address array);
}

file sealed class ChunkedObjectArrayShape(
    ChunkedObjectArrayLayout layout,
    ObjectItemLayout item) : ObjectArrayShape
{
    private readonly ObjectItemLayout _item = item;
    private readonly ChunkedObjectArrayLayout _layout = layout;

    public override Result<IObjectArray> Create(IMemoryReader memory, Address array)
    {
        if (ObjectSlots.ValidateChunks(_layout).TryUnwrapErr(out var error)
            || ObjectSlots.ValidateItem(_item, memory.Layout.PointerSize).TryUnwrapErr(out error))
        {
            return error;
        }

        return Result.Ok<IObjectArray>(new ChunkedObjectArray(memory, array, _layout, _item));
    }
}

file sealed class IndirectObjectArrayShape(
    IndirectObjectArrayLayout layout) : ObjectArrayShape
{
    private readonly IndirectObjectArrayLayout _layout = layout;

    public override Result<IObjectArray> Create(IMemoryReader memory, Address array)
    {
        return Result.Ok<IObjectArray>(new IndirectObjectArray(memory, array, _layout));
    }
}

file sealed class ItemObjectArrayShape(
    ItemObjectArrayLayout layout,
    ObjectItemLayout item) : ObjectArrayShape
{
    private readonly ObjectItemLayout _item = item;
    private readonly ItemObjectArrayLayout _layout = layout;

    public override Result<IObjectArray> Create(IMemoryReader memory, Address array)
    {
        return ObjectSlots.ValidateItem(_item, memory.Layout.PointerSize).TryUnwrapErr(out var error)
            ? error
            : Result.Ok<IObjectArray>(new ItemObjectArray(memory, array, _layout, _item));
    }
}

file sealed class PointerObjectArrayShape : ObjectArrayShape
{
    public override Result<IObjectArray> Create(IMemoryReader memory, Address array)
    {
        return Result.Ok<IObjectArray>(new PointerObjectArray(memory, array));
    }
}
