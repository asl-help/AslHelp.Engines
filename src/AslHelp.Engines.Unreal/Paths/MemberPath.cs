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

    // Keep that the hop of this segment resolved on a class, a struct type or a collection, because a resolved hop doesn't change.
    private bool _checked;

    // Keep the error of the segment when it doesn't fit the collection before it, because the hop of a node doesn't change.
    private CollectionError? _collectionError;

    private MemberPath? _count;

    // Keep the error of a member that neither the declared class nor a loaded class derived from it has.
    private ClassError? _declaredError;

    // Keep that a loaded derived class has the member, so a later check doesn't list the derived classes again.
    private bool _derivedHasMember;

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
    ///     Gets a value indicating whether a step or a read checked the hop of the last segment against the classes of the game.
    /// </summary>
    /// <remarks>
    ///     The root is always checked. A step on a path that doesn't read checks the classes that the properties declare
    ///     and stays unchecked when only a derived class can have the member.
    /// </remarks>
    public bool IsChecked => _parent is null || _checked;

    /// <summary>
    ///     Gets the number of segments, the root and 1 per member, index or count hop.
    /// </summary>
    public int SegmentCount => _nodes.Length;

    /// <summary>
    ///     Returns the path with the member <paramref name="name"/> after the last segment of this path once it fits the game.
    /// </summary>
    /// <remarks>
    ///     Reads the new path up to its last segment once. When a failure comes from memory, like a null object, the root or a failed read,
    ///     checks the segments after the last one that reads against the classes their properties declare.
    ///     The step fails when neither the declared class nor a loaded class derived from it has the member.
    ///     The same step can pass once the game loads a derived class with the member.
    ///     Keeps the child after the check passed, so the root doesn't keep a failed step.
    /// </remarks>
    /// <param name="name">The name of the member. A name that only matches in another case finds the member too.</param>
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
    ///     Reads the path once. When the path doesn't read, checks the kind that the declared classes give, like <see cref="Append"/>.
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

        if (!CheckEnd().TryUnwrap(out var access, out var error))
        {
            return error;
        }

        return access is null || Fits<T>(access) ? Result.Ok() : SegmentFailed(SegmentCount - 1, access.NotValueOf(typeof(T)), isShapeMismatch: true);
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

        if (!Fits<T>(target.Access))
        {
            return SegmentFailed(SegmentCount - 1, target.Access.NotValueOf(typeof(T)), isShapeMismatch: true);
        }

        var value = target.Access is ValueAccess<T> valueAccess
            ? valueAccess.Read(Memory, target.Address, last)
            : ReadStruct(target.Access, target.Address, last);

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
            read = node.ReadSegment(ticks, read, out _);

            if (ticks is not null)
            {
                node._read.Set(ticks, read);
            }
        }

        return read;
    }

    // Read the root or step through the hop of this segment from the read of the parent.
    // Return the access of the hop too, null for the root or when the hop doesn't resolve.
    private SegmentRead ReadSegment(ITickCounter? ticks, SegmentRead start, out PropertyAccess? access)
    {
        access = null;

        if (_parent is null)
        {
            return _root.Read().TryUnwrap(out var obj, out var rootError)
                ? new SegmentRead(new Container(obj, null), null)
                : new SegmentRead(default, new Failure(0, rootError, IsShapeMismatch: false));
        }

        if (start.Failure is not null)
        {
            return start with { Class = null };
        }

        if (!TryAccess(ticks, start.Container, out var target, out var failure))
        {
            return new SegmentRead(default, failure);
        }

        access = target.Access;

        return target.Access.Step(Memory, target.Address).TryUnwrap(out var container, out var stepError)
            ? new SegmentRead(container, null)
            : new SegmentRead(default, new Failure(SegmentCount - 1, stepError, IsShapeMismatch: !target.Access.StepReadsMemory));
    }

    // Return the access of this segment and the address it reads at. After a collection that's an element or the count,
    // otherwise the property of the member on the struct type or the class of the object it's read from.
    private bool TryAccess(ITickCounter? ticks, Container container, out Target target, out Failure failure)
    {
        int segment = SegmentCount - 1;
        target = default;

        if (HopMismatch(container.Collection) is { } mismatch)
        {
            failure = new Failure(segment, mismatch, IsShapeMismatch: true);

            return false;
        }

        if (container.Collection is { } collection)
        {
            if (_hop.Key is { } key)
            {
                if (!collection.FindElement(Memory, container.Address, key).TryUnwrap(out var element, out var findError))
                {
                    bool keyTypeMismatch = findError is CollectionError { IsKeyTypeMismatch: true };
                    _checked |= !keyTypeMismatch;
                    failure = new Failure(segment, findError, keyTypeMismatch);

                    return false;
                }

                target = new Target(collection.Element, element);
            }
            else
            {
                target = new Target(collection.Count, container.Address);
            }

            _checked = true;
            failure = default;

            return true;
        }

        if (container.StructType is not { } type)
        {
            if (container.Address == Address.Zero)
            {
                failure = new Failure(segment, null, IsShapeMismatch: false);

                return false;
            }

            if (!_parent!.ClassOfContainer(ticks, container.Address).TryUnwrap(out type, out var classError))
            {
                failure = new Failure(segment, classError, IsShapeMismatch: false);

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
            failure = new Failure(segment, bindError, IsShapeMismatch: false);

            return false;
        }

        target = new Target(access, container.Address);
        failure = default;
        _checked = true;

        return true;
    }

    // Return the failure of a hop that doesn't fit the kind of container before it, like a member after a collection
    // or an index after an object, otherwise null.
    private CollectionError? HopMismatch(CollectionAccess? collection)
    {
        if (collection is not null)
        {
            return _hop.Key is null && !_hop.IsCount
                ? _collectionError ??= CollectionError.MemberAfterCollection(collection.Property.Name, _hop.Name)
                : null;
        }

        if (_hop.IsCount)
        {
            return _collectionError ??= CollectionError.CountNotCollection(ParentText());
        }

        return _hop.Key is { } index ? _collectionError ??= CollectionError.NotCollection(ParentText(), index.Text) : null;
    }

    // Return the member before this segment with the indexes after it, like Items[1], because the name of an index hop is only its key.
    private string ParentText()
    {
        string indexes = "";

        for (var node = _parent!; ; node = node._parent!)
        {
            if (node._hop.Key is null)
            {
                return node._hop.Name + indexes;
            }

            indexes = node._hop + indexes;
        }
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
        return CheckEnd().TryUnwrapErr(out var error) ? error : Result.Ok();
    }

    // Read the segments once up to the 1st one that fails, then check the hops from there on against the classes their properties declare.
    // Return the access of the last segment, a shape mismatch or null when the path doesn't read and the declared classes can't confirm the hop.
    private Result<PropertyAccess?> CheckEnd()
    {
        SegmentRead read = default;
        PropertyAccess? access = null;
        Failure? failure = null;
        int segment = 0;

        for (; segment < _nodes.Length - 1; segment++)
        {
            var next = _nodes[segment].ReadSegment(null, read, out var nextAccess);

            if (next.Failure is { } nextFailure)
            {
                failure = nextFailure;
                break;
            }

            (read, access) = (next, nextAccess);
        }

        if (failure is null)
        {
            if (TryAccess(null, read.Container, out var target, out var endFailure))
            {
                return target.Access;
            }

            failure = endFailure;
        }

        return failure.Value.IsShapeMismatch ? ToError(failure.Value) : CheckDeclared(segment, read, access);
    }

    // Check the hops from the segment that failed on against the classes their properties declare.
    // Return null as soon as a declared class doesn't read or can't confirm the hop, because the read on a live object checks it later.
    private Result<PropertyAccess?> CheckDeclared(int segment, SegmentRead before, PropertyAccess? beforeAccess)
    {
        if (DeclaredStart(segment, before, beforeAccess) is not { } container)
        {
            return Result.Ok<PropertyAccess?>(null);
        }

        PropertyAccess? access = null;

        // Start at the 1st hop when the root itself doesn't read, because the root doesn't have a hop to check.
        for (segment = Math.Max(segment, 1); segment < _nodes.Length; segment++)
        {
            if (access is not null)
            {
                if (!access.StepDeclared(Memory).TryUnwrap(out var stepped, out var stepError))
                {
                    return ToError(new Failure(segment - 1, stepError, IsShapeMismatch: true));
                }

                if (stepped is not { } next)
                {
                    return Result.Ok<PropertyAccess?>(null);
                }

                container = next;
            }

            if (!_nodes[segment].AccessDeclared(container).TryUnwrap(out access, out var error))
            {
                return ToError(new Failure(segment, error, IsShapeMismatch: true));
            }

            if (access is null)
            {
                return Result.Ok<PropertyAccess?>(null);
            }
        }

        return access;
    }

    // Return the container that the 1st failing segment reads from, which is the live container before it, the declared class of the root
    // or the class that the property of a null object pointer declares. Return null when that doesn't read.
    private DeclaredContainer? DeclaredStart(int segment, SegmentRead before, PropertyAccess? beforeAccess)
    {
        var container = before.Container;

        if (segment > 0 && container.Collection is { } collection)
        {
            return new DeclaredContainer(null, IsObject: false, collection);
        }

        if (segment > 0 && container.StructType is { } structType)
        {
            return new DeclaredContainer(structType, IsObject: false);
        }

        if (segment > 0 && container.Address != Address.Zero)
        {
            return _classReader.ClassOf(container.Address).TryUnwrap(out var real, out _) ? new DeclaredContainer(real, IsObject: true) : null;
        }

        if (segment <= 1)
        {
            return _root.ReadDeclaredClass().TryUnwrap(out var rootClass, out _) ? new DeclaredContainer(rootClass, IsObject: true) : null;
        }

        return beforeAccess is not null && beforeAccess.StepDeclared(Memory).TryUnwrap(out var declared, out _) ? declared : null;
    }

    // Return the access of this segment on a declared container, null when the declared classes can't confirm the hop or a read fails,
    // or the error of a hop that doesn't fit.
    private Result<PropertyAccess?> AccessDeclared(DeclaredContainer container)
    {
        if (HopMismatch(container.Collection) is { } mismatch)
        {
            return mismatch;
        }

        if (container.Collection is { } collection)
        {
            if (_hop.Key is not { } key)
            {
                _checked = true;

                return collection.Count;
            }

            // Fail a key of another type, because the key type doesn't depend on memory.
            // Leave the step unchecked otherwise, because only a read finds the key.
            return collection.CheckKey(key).TryUnwrapErr(out var keyError) && keyError is CollectionError { IsKeyTypeMismatch: true }
                ? keyError
                : collection.Element;
        }

        if (!_hop.Resolve(container.Type!, out bool missing).TryUnwrap(out var property, out var error))
        {
            if (!missing)
            {
                return Result.Ok<PropertyAccess?>(null);
            }

            // Fail a member that a struct type doesn't have, because a struct type doesn't have derived types.
            if (!container.IsObject)
            {
                return error;
            }

            return DerivedHasMember(container.Type!) is false
                ? _declaredError ??= ClassError.PropertyNotDeclared(container.Type!.Name, _hop.Name)
                : Result.Ok<PropertyAccess?>(null);
        }

        if (!property.Bind().TryUnwrap(out var access, out _))
        {
            return Result.Ok<PropertyAccess?>(null);
        }

        _checked = true;

        return access;
    }

    // Check whether a loaded class that derives from klass declares the member itself. Return null when a read fails.
    private bool? DerivedHasMember(UnrealClass klass)
    {
        if (_derivedHasMember)
        {
            return true;
        }

        bool? declares = _classReader.DerivedDeclares(klass, _hop.Name);
        _derivedHasMember = declares is true;

        return declares;
    }

    private MemberPath Child(MemberHop hop)
    {
        return new MemberPath(_classReader, _root, this, hop);
    }

    private static bool Fits<T>(PropertyAccess access)
    {
        return access is ValueAccess<T> || (typeof(T) == typeof(StructValue) && access.IsStruct);
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
    private PathError SegmentFailed(int segment, ResultError cause, bool isShapeMismatch = false)
    {
        _segmentErrors ??= new (ResultError, PathError)?[SegmentCount];

        if (_segmentErrors[segment] is { } kept && ReferenceEquals(kept.Cause, cause))
        {
            return kept.Error;
        }

        var error = PathError.SegmentFailed(this, segment, cause, isShapeMismatch);
        _segmentErrors[segment] = (cause, error);

        return error;
    }

    // Turn a failure of a segment into the error of this path with its own text.
    private PathError ToError(Failure failure)
    {
        return failure.Cause is { } cause
            ? SegmentFailed(failure.Segment, cause, failure.IsShapeMismatch)
            : (_nullErrors ??= new PathError?[SegmentCount])[failure.Segment] ??= PathError.NullObject(this, failure.Segment);
    }

    // Keep a failure without the path, so each path below a shared segment fails with its own text. Cause is null for a null object.
    private readonly record struct Failure(
        int Segment,
        ResultError? Cause,
        bool IsShapeMismatch);

    // Keep the container after a segment or its failure. Class is set when the 1st member after the segment reads the class of the object.
    private readonly record struct SegmentRead(
        Container Container,
        Failure? Failure,
        Result<UnrealClass>? Class = null);

    private readonly record struct Target(
        PropertyAccess Access,
        Address Address);
}
