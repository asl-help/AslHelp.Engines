using AslHelp.Memory;
using AslHelp.Memory.Watching;
using System;
using System.Collections;
using System.Collections.Generic;

namespace AslHelp.Engines.Tests;

/// <summary>
///     Represents an object of a small engine that only exists in the tests, with a class name and members by name.
/// </summary>
/// <remarks>
///     The engine only uses public types of <c>AslHelp.Engines</c>, so it shows that an engine outside the package can implement
///     <see cref="IMemberPath"/>. A member is an <see cref="int"/>, a <see cref="float"/>, a <see cref="string"/>,
///     another object or <see langword="null"/> for an object that isn't there, a <see cref="List{T}"/> of items,
///     a <see cref="Dictionary{TKey, TValue}"/> of items by key or a <see cref="FakeStruct"/>.
/// </remarks>
/// <param name="name">The name of the object, which starts the text of its path.</param>
/// <param name="className">The name of the class of the object.</param>
internal sealed class FakeObject(
    string name,
    string className)
{
    /// <summary>
    ///     Gets the name of the class of the object.
    /// </summary>
    public string ClassName { get; } = className;

    /// <summary>
    ///     Gets the members of the object by name, which a test changes between ticks.
    /// </summary>
    public Dictionary<string, object?> Members { get; } = [];

    /// <summary>
    ///     Gets the name of the object.
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    ///     Gets the path that starts at the object.
    /// </summary>
    public IMemberPath Path => field ??= FakePath.Root(this);

    /// <summary>
    ///     Gets the number of member lookups, which a test compares to count the reads of a path.
    /// </summary>
    public int Reads { get; private set; }

    /// <summary>
    ///     Looks a member up and counts the lookup.
    /// </summary>
    /// <param name="name">The name of the member.</param>
    /// <param name="value">The value of the member or <see langword="null"/>.</param>
    /// <returns>
    ///     <see langword="true"/> when the object has the member.
    /// </returns>
    public bool TryRead(string name, out object? value)
    {
        Reads++;

        return Members.TryGetValue(name, out value);
    }
}

/// <summary>
///     Represents a struct of the fake engine, with members in a fixed order.
/// </summary>
/// <param name="typeName">The name of the struct type.</param>
/// <param name="names">The names of the members.</param>
/// <param name="values">The value of each member, which a test changes between ticks.</param>
internal sealed class FakeStruct(
    string typeName,
    string[] names,
    object?[] values)
{
    /// <summary>
    ///     Gets the names of the members.
    /// </summary>
    public string[] Names { get; } = names;

    /// <summary>
    ///     Gets the name of the struct type.
    /// </summary>
    public string TypeName { get; } = typeName;

    /// <summary>
    ///     Gets the value of each member.
    /// </summary>
    public object?[] Values { get; } = values;
}

file sealed class FakePath(
    FakeNode node) : IMemberPath
{
    public static FakePath Root(FakeObject root)
    {
        return new FakePath(new FakeNode(root, null, null));
    }

    public Result<IMemberPath> Count()
    {
        return Step(new CountStep());
    }

    public Watcher<object?> CreateWatcher(ITickCounter ticks)
    {
        return new FakeWatcher<object?>(ticks, this);
    }

    Watcher IWatcherSource.CreateWatcher(ITickCounter ticks)
    {
        return CreateWatcher(ticks);
    }

    public Result<IMemberPath> Item(PathKey key)
    {
        return Step(new ItemStep(key));
    }

    public Result<IMemberPath> Member(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return Step(new MemberStep(name));
    }

    // Read the value at the end, once per tick of a watcher's counter and every time for a step check.
    public Result<T> Read<T>(ITickCounter? ticks)
    {
        if (!node.Read(ticks, out _).TryUnwrap(out object? value, out var error))
        {
            return error;
        }

        return ToValue(value) is T typed ? typed : new WrongTypeError(node, typeof(T).Name);
    }

    public override string ToString()
    {
        return node.ToString();
    }

    public Result<IWatcherSource<T>> Value<T>()
    {
        if (node.Read(null, out _).TryUnwrap(out object? value) && ToValue(value) is not T)
        {
            return new WrongTypeError(node, typeof(T).Name);
        }

        return Result.Ok<IWatcherSource<T>>(new FakeSource<T>(this));
    }

    private static object? ToValue(object? value)
    {
        return value is FakeStruct fake ? new StructValue(fake.TypeName, fake.Names, [.. fake.Values]) : value;
    }

    private Result<IMemberPath> Step(FakeStep step)
    {
        var child = node.Child(step);

        return !child.Read(null, out bool fits).TryUnwrapErr(out var error) || fits ? Result.Ok<IMemberPath>(new FakePath(child)) : error;
    }
}

// Keep 1 node per step, so paths built apart with equal steps share the TickMemo of each step.
file sealed class FakeNode(
    FakeObject root,
    FakeNode? parent,
    FakeStep? step)
{
    private readonly Dictionary<FakeStep, FakeNode> _children = [];
    private readonly TickMemo<Result<object?>> _value = new();

    public FakeNode Child(FakeStep childStep)
    {
        if (!_children.TryGetValue(childStep, out var child))
        {
            child = new FakeNode(root, this, childStep);
            _children.Add(childStep, child);
        }

        return child;
    }

    // Read the value at this step. fits is false when the path doesn't fit the members it reads, so a step fails at once.
    public Result<object?> Read(ITickCounter? ticks, out bool fits)
    {
        fits = true;

        if (ticks is not null && _value.TryGet(ticks, out var kept))
        {
            return kept;
        }

        var value = ReadStep(ticks, out fits);

        if (ticks is not null)
        {
            _value.Set(ticks, value);
        }

        return value;
    }

    public override string ToString()
    {
        return parent is null ? root.Name : parent.ToString() + step;
    }

    private Result<object?> ReadStep(ITickCounter? ticks, out bool fits)
    {
        fits = true;

        if (parent is null)
        {
            return Result.Ok<object?>(root);
        }

        if (!parent.Read(ticks, out fits).TryUnwrap(out object? current, out var error))
        {
            // Show this path in a null object error of a step before it, like a read of the whole path does.
            return error is NullObjectError nullObject ? new NullObjectError(this, nullObject.Start) : error;
        }

        switch (step, current)
        {
            case (_, null):
            {
                return new NullObjectError(this, parent);
            }
            case (MemberStep member, FakeObject obj):
            {
                if (!obj.TryRead(member.Name, out current))
                {
                    fits = false;

                    return new MissingMemberError(obj.ClassName, member.Name);
                }

                return Result.Ok(current);
            }
            case (MemberStep member, FakeStruct value):
            {
                int index = Array.IndexOf(value.Names, member.Name);

                if (index < 0)
                {
                    fits = false;

                    return new MissingMemberError(value.TypeName, member.Name);
                }

                return Result.Ok(value.Values[index]);
            }
            case (ItemStep { Key.Value: long index }, List<object?> list):
            {
                return index < list.Count ? Result.Ok(list[(int)index]) : new MissingItemError(parent, step);
            }
            case (ItemStep { Key.Value: string key }, Dictionary<string, object?> map):
            {
                return map.TryGetValue(key, out current) ? Result.Ok(current) : new MissingItemError(parent, step);
            }
            case (CountStep, ICollection collection):
            {
                return Result.Ok<object?>(collection.Count);
            }
            default:
            {
                fits = false;

                return new MismatchError(parent, step!);
            }
        }
    }
}

file sealed class FakeSource<T>(
    FakePath path) : IWatcherSource<T>
{
    public Watcher<T> CreateWatcher(ITickCounter ticks)
    {
        return new FakeWatcher<T>(ticks, path);
    }

    Watcher IWatcherSource.CreateWatcher(ITickCounter ticks)
    {
        return CreateWatcher(ticks);
    }
}

file sealed class FakeWatcher<T> : Watcher<T>
{
    private readonly FakePath _path;

    public FakeWatcher(ITickCounter ticks, FakePath path)
        : base(ticks)
    {
        _path = path;
    }

    protected override Result<T> Read()
    {
        return _path.Read<T>(Ticks);
    }
}
file abstract record FakeStep;

file sealed record CountStep : FakeStep
{
    public override string ToString()
    {
        return ".Count";
    }
}

file sealed record ItemStep(
    PathKey Key) : FakeStep
{
    public override string ToString()
    {
        return $"[{Key}]";
    }
}

file sealed record MemberStep(
    string Name) : FakeStep
{
    public override string ToString()
    {
        return "." + Name;
    }
}

file sealed record MismatchError(
    FakeNode Start,
    FakeStep Step) : ResultError
{
    public override string Message => $"{Start} doesn't take {Step}.";
}

file sealed record MissingItemError(
    FakeNode Start,
    FakeStep Step) : ResultError
{
    public override string Message => $"{Start} doesn't have the item {Step}.";
}

file sealed record MissingMemberError(
    string TypeName,
    string Name) : ResultError
{
    public override string Message => $"{TypeName} doesn't have a member {Name}.";
}

file sealed record NullObjectError(
    FakeNode Path,
    FakeNode Start) : ResultError
{
    public override string Message => $"Can't read {Path}: {Start} is null.";
}

file sealed record WrongTypeError(
    FakeNode Path,
    string TypeName) : ResultError
{
    public override string Message => $"{Path} isn't a {TypeName}.";
}
