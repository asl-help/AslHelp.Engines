using AslHelp.Engines.Unreal.Errors;
using AslHelp.Engines.Unreal.Properties;
using AslHelp.Engines.Unreal.Structs;
using AslHelp.Memory;
using AslHelp.Memory.Watching;
using System;
using System.Collections.Generic;
using System.Text;

namespace AslHelp.Engines.Unreal.Paths;

/// <summary>
///     Represents a path from a root object of a game, like a global, through members,
///     read by the real class of each object on the way.
/// </summary>
/// <remarks>
///     <para>
///         A path is immutable. Its segments are the root, then 1 per member, index or count hop.
///         Each hop reads an object pointer or adds the offset of a struct, picked by the kind of its property.
///         After an array, a set or a map, an index hop finds 1 element and a count hop reads the number of elements.
///     </para>
///     <para>
///         A path is 1 node of a tree. A step returns the child for its hop and keeps it once the check passed,
///         so paths built apart from 1 root with equal steps are the same node.
///         The root keeps every child as long as it lives, so a script that steps to a new key on each tick keeps every key.
///         A read with a tick counter keeps the read of each segment and the class of the object it reaches in a <see cref="TickMemo{T}"/>,
///         so every watcher of the counter reads the shared steps once per tick. A read without a counter reads every segment.
///     </para>
///     <para>
///         A read that succeeds doesn't allocate once each hop resolved the class of its object.
///         A class name with a number, like <c>BP_Player_C_1</c>, still allocates the name on each read of that hop.
///         Isn't thread-safe.
///     </para>
/// </remarks>
internal sealed class MemberPath
{
    private readonly ClassReader _classReader;
    private readonly MemberHop _hop;

    // Keep the node of each segment from the root to this one, so a read steps down without recursing.
    private readonly MemberPath[] _nodes;

    private readonly MemberPath? _parent;
    private readonly TickMemo<SegmentRead> _read = new();
    private readonly PathRoot _root;

    // Keep the error of the segment when it doesn't fit the collection before it, because the hop of a node doesn't change.
    private CollectionError? _collectionError;

    private MemberPath? _count;
    private Dictionary<PathKey, MemberPath>? _items;
    private Dictionary<string, MemberPath>? _members;

    // Keep the error of each segment that reads from a null object, because a game that's still loading fails that way on every tick.
    // Create the array on the 1st failure, so a path that reads doesn't keep one.
    private PathError?[]? _nullErrors;

    // Keep the failure of a typed read of the root alone, because a watcher reads as the same type on every tick.
    private (Type Type, PathError Error)? _rootTypeError;

    // Keep the last failure of each segment with its cause, because a hop returns the same not-found error on every tick.
    // Create the array on the 1st failure, like the null errors.
    private (ResultError Cause, PathError Error)?[]? _segmentErrors;

    private string? _text;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MemberPath"/> class for the root alone.
    /// </summary>
    /// <param name="classReader">The reader of the game to read from.</param>
    /// <param name="root">The object the path starts at.</param>
    /// <param name="text">The text of the root, like <c>GWorld</c> or the name of a class.</param>
    public MemberPath(ClassReader classReader, PathRoot root, string text)
        : this(classReader, root, null, MemberHop.Root(text))
    {
    }

    private MemberPath(ClassReader classReader, PathRoot root, MemberPath? parent, MemberHop hop)
    {
        _classReader = classReader;
        _root = root;
        _parent = parent;
        _hop = hop;
        _nodes = parent is null ? [this] : [.. parent._nodes, this];
    }

    /// <summary>
    ///     Gets the memory of the game.
    /// </summary>
    public IMemoryReader Memory => _classReader.Memory;

    /// <summary>
    ///     Gets the number of segments, the root and 1 per member, index or count hop.
    /// </summary>
    public int SegmentCount => _nodes.Length;

    /// <summary>
    ///     Returns the path with the member <paramref name="name"/> after the last segment of this path once it fits the game.
    /// </summary>
    /// <remarks>
    ///     Reads the new path up to its last segment once. A failure that comes from memory, like a null object, the root or a failed read,
    ///     doesn't fail the step, because the path can read later. Keeps the child after the check passed, so the root doesn't keep a failed step.
    /// </remarks>
    /// <param name="name">The name of the member, compared ordinally.</param>
    /// <returns>
    ///     The child or a failure when the class or struct doesn't have the member or the end of this path doesn't have members.
    ///     Each call with an equal name returns the same child.
    /// </returns>
    public Result<MemberPath> Append(string name)
    {
        var child = _members is not null && _members.TryGetValue(name, out var kept) ? kept : Child(new MemberHop(name));

        if (child.Check().TryUnwrapErr(out var error))
        {
            return error;
        }

        (_members ??= [])[name] = child;

        return child;
    }

    /// <summary>
    ///     Returns the path with the number of elements of the collection at the end of this path once it fits the game.
    /// </summary>
    /// <remarks>
    ///     Checks and keeps the child like <see cref="Append(string)"/>.
    ///     Isn't the child of <see cref="Append(string)"/> with <c>Count</c>, even though both print the same text.
    /// </remarks>
    /// <returns>
    ///     The child or a failure when the end of this path isn't a collection. Each call returns the same child.
    /// </returns>
    public Result<MemberPath> AppendCount()
    {
        var child = _count ?? Child(MemberHop.Count());

        if (child.Check().TryUnwrapErr(out var error))
        {
            return error;
        }

        return _count = child;
    }

    /// <summary>
    ///     Returns the path with the element of <paramref name="key"/> in the collection at the end of this path once it fits the game.
    /// </summary>
    /// <remarks>
    ///     Checks and keeps the child like <see cref="Append(string)"/>.
    /// </remarks>
    /// <param name="key">The index or key.</param>
    /// <returns>
    ///     The child or a failure when the end of this path isn't a collection or the key can't match its keys.
    ///     Each call with an equal key returns the same child.
    /// </returns>
    public Result<MemberPath> AppendItem(PathKey key)
    {
        var child = _items is not null && _items.TryGetValue(key, out var kept) ? kept : Child(MemberHop.Index(key));

        if (child.Check().TryUnwrapErr(out var error))
        {
            return error;
        }

        (_items ??= [])[key] = child;

        return child;
    }

    /// <summary>
    ///     Checks whether the value at the end of the path reads as <typeparamref name="T"/> with the classes of the path as the game has them now.
    /// </summary>
    /// <remarks>
    ///     Reads the path once. A failure that comes from memory doesn't fail the check, like in <see cref="Check"/>.
    /// </remarks>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <returns>
    ///     A success or a failure when the kind at the end doesn't read as <typeparamref name="T"/>.
    /// </returns>
    public Result CheckValue<T>()
    {
        if (_parent is null)
        {
            return typeof(T) == typeof(Address) ? Result.Ok() : RootTypeError(typeof(T));
        }

        if (!TryReachEnd(null, out var target, out var error))
        {
            return CheckFailure(error);
        }

        if (target.Access is ValueAccess<T> || (typeof(T) == typeof(StructValue) && target.Access.IsStruct))
        {
            return Result.Ok();
        }

        return SegmentFailed(SegmentCount - 1, target.Access.NotValueOf(typeof(T)), isMismatch: true);
    }

    /// <summary>
    ///     Reads the value at the end of the path as the type of the kind of its property.
    /// </summary>
    /// <remarks>
    ///     The root alone reads as the <see cref="Address"/> of its object.
    ///     Returns <paramref name="last"/> itself when the value equals it, so a value that doesn't change doesn't allocate a box.
    /// </remarks>
    /// <param name="ticks">The tick counter of the watcher that reads or <see langword="null"/> to read every segment.</param>
    /// <param name="last">The value of the last read in its own type or <see langword="null"/>.</param>
    /// <returns>
    ///     The value or a failure.
    /// </returns>
    public Result<object?> ReadBoxed(ITickCounter? ticks, object? last)
    {
        if (_parent is null)
        {
            var root = Follow(ticks);

            if (root.Failure is { } rootFailure)
            {
                return ToError(rootFailure);
            }

            return last is Address kept && kept == root.Container.Address ? Result.Ok<object?>(last) : Result.Ok<object?>(root.Container.Address);
        }

        if (!TryReachEnd(ticks, out var target, out var error))
        {
            return error;
        }

        return target.Access.ReadBoxed(Memory, target.Address, last).TryUnwrap(out object? value, out var readError)
            ? Result.Ok(value)
            : SegmentFailed(SegmentCount - 1, readError, target.Access.AlwaysFails);
    }

    /// <summary>
    ///     Reads the object or struct at the end of the path.
    /// </summary>
    /// <remarks>
    ///     Reads every segment, so a write in the current tick shows up.
    /// </remarks>
    /// <returns>
    ///     The address of the object or struct or a failure. An object pointer of 0 at the end returns <see cref="Address.Zero"/>.
    ///     A collection at the end returns the address of the object or struct with the collection.
    /// </returns>
    public Result<Address> ReadContainer()
    {
        var read = Follow(null);

        return read.Failure is { } failure ? ToError(failure) : read.Container.Address;
    }

    /// <summary>
    ///     Reads the value of type <typeparamref name="T"/> at the end of the path.
    /// </summary>
    /// <remarks>
    ///     The root alone reads as the <see cref="Address"/> of its object. A struct reads as a <see cref="StructValue"/>.
    /// </remarks>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="ticks">The tick counter of the watcher that reads or <see langword="null"/> to read every segment.</param>
    /// <param name="last">The value of the last read or the default value. A string read returns it when the text is equal.</param>
    /// <returns>
    ///     The value or a failure.
    /// </returns>
    public Result<T> ReadValue<T>(ITickCounter? ticks, T? last)
    {
        if (_parent is null)
        {
            if (typeof(T) != typeof(Address))
            {
                return RootTypeError(typeof(T));
            }

            var root = Follow(ticks);

            return root.Failure is { } rootFailure ? ToError(rootFailure) : (T)(object)root.Container.Address;
        }

        if (!TryReachEnd(ticks, out var target, out var error))
        {
            return error;
        }

        var value = target.Access switch
        {
            ValueAccess<T> valueAccess => valueAccess.Read(Memory, target.Address, last),
            var access when typeof(T) == typeof(StructValue) => ReadStruct(access, target.Address, last),
            var access => access.NotValueOf(typeof(T)),
        };

        return value.TryUnwrapErr(out var readError) ? SegmentFailed(SegmentCount - 1, readError, target.Access.AlwaysFails) : value;
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
            text.Append(_nodes[segment]._hop);
        }

        return text.ToString();
    }

    // Read the parent, then get the access of the last segment on its container.
    // Turn a failure into the error of this path, because the read of the parent is shared.
    private bool TryReachEnd(ITickCounter? ticks, out Target target, out PathError error)
    {
        var start = _parent!.Follow(ticks);

        if (start.Failure is not { } failure && TryAccess(ticks, start.Container, out target, out failure))
        {
            error = null!;

            return true;
        }

        target = default;
        error = ToError(failure);

        return false;
    }

    // Read the root, then step through the hop of each segment from the container of the segment before it.
    // With a tick counter, start after the last segment that kept a read for the current tick. Every path below that segment shares that read.
    // Loop instead of recursing into the parent, so a path with 1000 segments doesn't overflow the stack.
    private SegmentRead Follow(ITickCounter? ticks)
    {
        int segment = _nodes.Length - 1;
        SegmentRead read = default;

        while (segment >= 0 && (ticks is null || !_nodes[segment]._read.TryGet(ticks, out read)))
        {
            segment--;
        }

        for (segment++; segment < _nodes.Length; segment++)
        {
            var node = _nodes[segment];
            read = node.ReadSegment(ticks, read);

            if (ticks is not null)
            {
                node._read.Set(ticks, read);
            }
        }

        return read;
    }

    // Read the root or step through the hop of this segment from the read of the parent.
    private SegmentRead ReadSegment(ITickCounter? ticks, SegmentRead start)
    {
        if (_parent is null)
        {
            return _root.Read().TryUnwrap(out var obj, out var rootError)
                ? new SegmentRead(new Container(obj, null), null)
                : new SegmentRead(default, new Failure(0, rootError, IsMismatch: false));
        }

        if (start.Failure is not null)
        {
            return start with { Class = null };
        }

        if (!TryAccess(ticks, start.Container, out var target, out var failure))
        {
            return new SegmentRead(default, failure);
        }

        return target.Access.Step(Memory, target.Address).TryUnwrap(out var container, out var stepError)
            ? new SegmentRead(container, null)
            : new SegmentRead(default, new Failure(SegmentCount - 1, stepError, IsMismatch: !target.Access.StepReadsMemory));
    }

    // Return the access of this segment and the address it reads at. After a collection that's an element or the count,
    // otherwise the property of the member on the struct type or the class of the object it's read from.
    private bool TryAccess(ITickCounter? ticks, Container container, out Target target, out Failure failure)
    {
        int segment = SegmentCount - 1;
        target = default;

        if (container.Collection is { } collection)
        {
            if (_hop.Key is { } key)
            {
                if (!collection.FindElement(Memory, container.Address, key).TryUnwrap(out var element, out var findError))
                {
                    failure = new Failure(segment, findError, findError is CollectionError { IsKeyTypeMismatch: true });

                    return false;
                }

                target = new Target(collection.Element, element);
                failure = default;

                return true;
            }

            if (_hop.IsCount)
            {
                target = new Target(collection.Count, container.Address);
                failure = default;

                return true;
            }

            _collectionError ??= CollectionError.MemberAfterCollection(collection.Property.Name, _hop.Name);
            failure = new Failure(segment, _collectionError, IsMismatch: true);

            return false;
        }

        if (_hop.IsCount)
        {
            _collectionError ??= CollectionError.CountNotCollection(_parent!._hop.Name);
            failure = new Failure(segment, _collectionError, IsMismatch: true);

            return false;
        }

        if (_hop.Key is { } index)
        {
            _collectionError ??= CollectionError.NotCollection(_parent!._hop.Name, index.Text);
            failure = new Failure(segment, _collectionError, IsMismatch: true);

            return false;
        }

        if (container.StructType is not { } type)
        {
            if (container.Address == Address.Zero)
            {
                failure = new Failure(segment, null, IsMismatch: false);

                return false;
            }

            if (!_parent!.ClassOfContainer(ticks, container.Address).TryUnwrap(out type, out var classError))
            {
                failure = new Failure(segment, classError, IsMismatch: false);

                return false;
            }
        }

        if (!_hop.Resolve(type, out bool missing).TryUnwrap(out var property, out var resolveError))
        {
            failure = new Failure(segment, resolveError, missing);

            return false;
        }

        if (!property.Bind().TryUnwrap(out var access, out var bindError))
        {
            failure = new Failure(segment, bindError, IsMismatch: false);

            return false;
        }

        target = new Target(access, container.Address);
        failure = default;

        return true;
    }

    // Read the class of the object this segment reaches once per tick of a counter for every member after it,
    // in the same memo as the read, so the class always belongs to the object of that read.
    private Result<UnrealClass> ClassOfContainer(ITickCounter? ticks, Address obj)
    {
        if (ticks is null || !_read.TryGet(ticks, out var read))
        {
            return _classReader.ClassOf(obj);
        }

        if (read.Class is { } kept)
        {
            return kept;
        }

        var klass = _classReader.ClassOf(obj);
        _read.Set(ticks, read with { Class = klass });

        return klass;
    }

    // Check whether the last segment fits the classes and kinds the game has now, like a member that the class has.
    private Result Check()
    {
        return TryReachEnd(null, out _, out var error) ? Result.Ok() : CheckFailure(error);
    }

    private MemberPath Child(MemberHop hop)
    {
        return new MemberPath(_classReader, _root, this, hop);
    }

    private static Result CheckFailure(PathError error)
    {
        return error.IsMismatch ? error : Result.Ok();
    }

    private Result<T> ReadStruct<T>(PropertyAccess access, Address container, T? last)
    {
        return access.ReadStruct(Memory, container, last as StructValue).TryUnwrap(out var value, out var error) ? (T)(object)value : error;
    }

    private PathError RootTypeError(Type type)
    {
        if (_rootTypeError is { } kept && kept.Type == type)
        {
            return kept.Error;
        }

        var error = PathError.RootWrongType(this, type.Name);
        _rootTypeError = (type, error);

        return error;
    }

    // Return the kept failure of a segment when the cause is the same instance, otherwise keep a new one.
    private PathError SegmentFailed(int segment, ResultError cause, bool isMismatch = false)
    {
        _segmentErrors ??= new (ResultError, PathError)?[SegmentCount];

        if (_segmentErrors[segment] is { } kept && ReferenceEquals(kept.Cause, cause))
        {
            return kept.Error;
        }

        var error = PathError.SegmentFailed(this, segment, cause, isMismatch);
        _segmentErrors[segment] = (cause, error);

        return error;
    }

    // Turn a failure of a segment into the error of this path with its own text.
    private PathError ToError(Failure failure)
    {
        return failure.Cause is { } cause
            ? SegmentFailed(failure.Segment, cause, failure.IsMismatch)
            : (_nullErrors ??= new PathError?[SegmentCount])[failure.Segment] ??= PathError.NullObject(this, failure.Segment);
    }

    // Keep a failure without the path, so each path below a shared segment fails with its own text. Cause is null for a null object.
    private readonly record struct Failure(
        int Segment,
        ResultError? Cause,
        bool IsMismatch);

    // Keep the container after a segment or its failure. Class is set when the 1st member after the segment reads the class of the object.
    private readonly record struct SegmentRead(
        Container Container,
        Failure? Failure,
        Result<UnrealClass>? Class = null);

    private readonly record struct Target(
        PropertyAccess Access,
        Address Address);
}
