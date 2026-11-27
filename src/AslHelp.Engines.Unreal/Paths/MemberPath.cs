using AslHelp.Engines.Unreal.Errors;
using AslHelp.Engines.Unreal.Properties;
using AslHelp.Engines.Unreal.Structs;
using AslHelp.Memory;
using System;
using System.Text;

namespace AslHelp.Engines.Unreal.Paths;

/// <summary>
///     Represents a path from a root object of a game, like a global, through members,
///     read by the real class of each object on the way.
/// </summary>
/// <remarks>
///     A path is immutable. Its segments are the root, then 1 per member or index hop.
///     Each hop reads an object pointer or adds the offset of a struct, picked by the kind of its property.
///     After an array, a set or a map, an index hop finds 1 element and the member <c>Count</c> reads the number of elements.
///     A read that succeeds doesn't allocate once each hop resolved the class of its object.
///     A class name with a number, like <c>BP_Player_C_1</c>, still allocates the name on each read of that hop.
///     Isn't thread-safe.
/// </remarks>
internal sealed class MemberPath
{
    private readonly ClassReader _classReader;

    // Keep the error of each segment that doesn't fit the collection before it, because the hops of a path don't change.
    private readonly CollectionError?[] _collectionErrors;

    private readonly MemberHop[] _hops;

    // Keep the error of each segment that reads from a null object, because a game that's still loading fails that way on every tick.
    private readonly PathError?[] _nullErrors;

    private readonly PathRoot _root;

    // Keep the last failure of each segment with its cause, because a hop returns the same not-found error on every tick.
    private readonly (ResultError Cause, PathError Error)?[] _segmentErrors;

    private string? _text;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MemberPath"/> class.
    /// </summary>
    /// <param name="classReader">The reader of the game to read from.</param>
    /// <param name="root">The object the path starts at.</param>
    /// <param name="hops">The hop of the root, then 1 per member or index.</param>
    public MemberPath(ClassReader classReader, PathRoot root, MemberHop[] hops)
    {
        _classReader = classReader;
        _root = root;
        _hops = hops;
        _collectionErrors = new CollectionError?[hops.Length];
        _nullErrors = new PathError?[hops.Length];
        _segmentErrors = new (ResultError, PathError)?[hops.Length];
    }

    /// <summary>
    ///     Gets the memory of the game.
    /// </summary>
    public IMemoryReader Memory => _classReader.Memory;

    /// <summary>
    ///     Gets the number of segments, the root and 1 per member or index hop.
    /// </summary>
    public int SegmentCount => _hops.Length;

    /// <summary>
    ///     Creates a path with the member <paramref name="name"/> after the last segment of this path.
    /// </summary>
    /// <param name="name">The name of the member.</param>
    /// <returns>
    ///     The longer path, which shares the hops of this path.
    /// </returns>
    public MemberPath Append(string name)
    {
        return Append(new MemberHop(name));
    }

    /// <summary>
    ///     Creates a path with <paramref name="hop"/> after the last segment of this path.
    /// </summary>
    /// <param name="hop">The member or index hop.</param>
    /// <returns>
    ///     The longer path, which shares the hops of this path.
    /// </returns>
    public MemberPath Append(MemberHop hop)
    {
        return new MemberPath(_classReader, _root, [.. _hops, hop]);
    }

    /// <summary>
    ///     Reads the value at the end of the path as the type of the kind of its property.
    /// </summary>
    /// <remarks>
    ///     Needs a path with a member after the root.
    ///     Returns <paramref name="last"/> itself when the value equals it, so a value that doesn't change doesn't allocate a box.
    /// </remarks>
    /// <param name="last">The value of the last read in its own type or <see langword="null"/>.</param>
    /// <returns>
    ///     The value or a failure.
    /// </returns>
    public Result<object?> ReadBoxed(object? last)
    {
        int end = _hops.Length - 1;

        if (!Follow(end).TryUnwrap(out var container, out var error) || !AccessAt(end, container).TryUnwrap(out var target, out error))
        {
            return error;
        }

        return target.Access.ReadBoxed(Memory, target.Address, last).TryUnwrap(out object? value, out error)
            ? Result.Ok(value)
            : SegmentFailed(end, error);
    }

    /// <summary>
    ///     Reads the object or struct at the end of the path.
    /// </summary>
    /// <returns>
    ///     The address of the object or struct or a failure. An object pointer of 0 at the end returns <see cref="Address.Zero"/>.
    ///     A collection at the end returns the address of the object or struct with the collection.
    /// </returns>
    public Result<Address> ReadContainer()
    {
        return Follow(SegmentCount).TryUnwrap(out var container, out var error) ? container.Address : error;
    }

    /// <summary>
    ///     Reads the value of type <typeparamref name="T"/> at the end of the path.
    /// </summary>
    /// <remarks>
    ///     Needs a path with a member after the root.
    /// </remarks>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="last">The value of the last read or the default value. A string read returns it when the text is equal.</param>
    /// <returns>
    ///     The value or a failure.
    /// </returns>
    public Result<T> ReadValue<T>(T? last)
    {
        int end = _hops.Length - 1;

        if (!Follow(end).TryUnwrap(out var container, out var error) || !AccessAt(end, container).TryUnwrap(out var target, out error))
        {
            return error;
        }

        var value = target.Access is ValueAccess<T> valueAccess
            ? valueAccess.Read(Memory, target.Address, last)
            : target.Access.NotValueOf(typeof(T));

        return value.TryUnwrapErr(out error) ? SegmentFailed(end, error) : value;
    }

    /// <summary>
    ///     Returns the segments of the path as a script writes them, like <c>GWorld.AuthorityGameMode.Items[2]</c>.
    /// </summary>
    /// <returns>
    ///     The text of the path.
    /// </returns>
    public override string ToString()
    {
        return _text ??= ToString(SegmentCount);
    }

    /// <summary>
    ///     Returns the first <paramref name="segmentCount"/> segments of the path as a script writes them.
    /// </summary>
    /// <param name="segmentCount">The number of segments, at least 1.</param>
    /// <returns>
    ///     The text of the start of the path.
    /// </returns>
    public string ToString(int segmentCount)
    {
        StringBuilder text = new();

        for (int segment = 0; segment < segmentCount; segment++)
        {
            text.Append(_hops[segment]);
        }

        return text.ToString();
    }

    // Read the root, then step through the property or element of each segment before segmentCount.
    private Result<Container> Follow(int segmentCount)
    {
        if (!_root.Read().TryUnwrap(out var obj, out var error))
        {
            return SegmentFailed(0, error);
        }

        Container container = new(obj, null);

        for (int segment = 1; segment < segmentCount; segment++)
        {
            if (!AccessAt(segment, container).TryUnwrap(out var target, out error))
            {
                return error;
            }

            if (!target.Access.Step(Memory, target.Address).TryUnwrap(out container, out error))
            {
                return SegmentFailed(segment, error);
            }
        }

        return container;
    }

    // Return the access of a segment and the address it reads at. After a collection that's an element or the count,
    // otherwise the property of the member on the struct type or the class of the object it's read from.
    private Result<(PropertyAccess Access, Address Address)> AccessAt(int segment, Container container)
    {
        var hop = _hops[segment];

        if (container.Collection is { } collection)
        {
            if (hop.Key is { } key)
            {
                if (!collection.FindElement(Memory, container.Address, key).TryUnwrap(out var element, out var findError))
                {
                    return SegmentFailed(segment, findError);
                }

                return Result.Ok((collection.Element, element));
            }

            if (string.Equals(hop.Name, CollectionAccess.CountMember, StringComparison.Ordinal))
            {
                return Result.Ok<(PropertyAccess, Address)>((collection.Count, container.Address));
            }

            _collectionErrors[segment] ??= CollectionError.MemberAfterCollection(collection.Property.Name, hop.Name);

            return SegmentFailed(segment, _collectionErrors[segment]!);
        }

        if (hop.Key is { } index)
        {
            _collectionErrors[segment] ??= CollectionError.NotCollection(_hops[segment - 1].Name, index.Text);

            return SegmentFailed(segment, _collectionErrors[segment]!);
        }

        if (container.StructType is not { } type)
        {
            if (container.Address == Address.Zero)
            {
                return _nullErrors[segment] ??= PathError.NullObject(this, segment);
            }

            if (!_classReader.ClassOf(container.Address).TryUnwrap(out type, out var classError))
            {
                return SegmentFailed(segment, classError);
            }
        }

        if (!hop.Resolve(type).TryUnwrap(out var property, out var error) || !property.Bind().TryUnwrap(out var access, out error))
        {
            return SegmentFailed(segment, error);
        }

        return Result.Ok((access, container.Address));
    }

    // Return the kept failure of a segment when the cause is the same instance, otherwise keep a new one.
    private PathError SegmentFailed(int segment, ResultError cause)
    {
        if (_segmentErrors[segment] is { } kept && ReferenceEquals(kept.Cause, cause))
        {
            return kept.Error;
        }

        var error = PathError.SegmentFailed(this, segment, cause);
        _segmentErrors[segment] = (cause, error);

        return error;
    }
}
