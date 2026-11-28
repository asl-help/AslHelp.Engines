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
    public IMemberPath Path => new FakePath(this, []);
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
    FakeObject root,
    FakeStep[] steps) : IMemberPath
{
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

    public Result<T> Read<T>()
    {
        if (!Read(out _).TryUnwrap(out object? value, out var error))
        {
            return error;
        }

        return value is T typed ? typed : new WrongTypeError(this, typeof(T).Name);
    }

    public override string ToString()
    {
        return root.Name + string.Concat<FakeStep>(steps);
    }

    public Result<IWatcherSource<T>> Value<T>()
    {
        if (Read(out _).TryUnwrap(out object? value) && value is not T)
        {
            return new WrongTypeError(this, typeof(T).Name);
        }

        return Result.Ok<IWatcherSource<T>>(new FakeSource<T>(this));
    }

    // Read the value at the end. fits is false when the path doesn't fit the members it reads, so a step fails at once.
    private Result<object?> Read(out bool fits)
    {
        fits = true;
        object? current = root;

        for (int i = 0; i < steps.Length; i++)
        {
            var start = Start(i);

            switch (steps[i], current)
            {
                case (_, null):
                {
                    return new NullObjectError(this, start);
                }
                case (MemberStep member, FakeObject obj):
                {
                    if (!obj.Members.TryGetValue(member.Name, out current))
                    {
                        fits = false;

                        return new MissingMemberError(obj.ClassName, member.Name);
                    }

                    break;
                }
                case (MemberStep member, FakeStruct value):
                {
                    int index = Array.IndexOf(value.Names, member.Name);

                    if (index < 0)
                    {
                        fits = false;

                        return new MissingMemberError(value.TypeName, member.Name);
                    }

                    current = value.Values[index];
                    break;
                }
                case (ItemStep { Key.Value: long index }, List<object?> list):
                {
                    if (index >= list.Count)
                    {
                        return new MissingItemError(start, steps[i]);
                    }

                    current = list[(int)index];
                    break;
                }
                case (ItemStep { Key.Value: string key }, Dictionary<string, object?> map):
                {
                    if (!map.TryGetValue(key, out current))
                    {
                        return new MissingItemError(start, steps[i]);
                    }

                    break;
                }
                case (CountStep, ICollection collection):
                {
                    current = collection.Count;
                    break;
                }
                default:
                {
                    fits = false;

                    return new MismatchError(start, steps[i]);
                }
            }
        }

        return current is FakeStruct fake ? new StructValue(fake.TypeName, fake.Names, [.. fake.Values]) : Result.Ok(current);
    }

    private FakePath Start(int stepCount)
    {
        var start = new FakeStep[stepCount];
        Array.Copy(steps, start, stepCount);

        return new FakePath(root, start);
    }

    private Result<IMemberPath> Step(FakeStep step)
    {
        FakePath path = new(root, [.. steps, step]);

        return !path.Read(out bool fits).TryUnwrapErr(out var error) || fits ? Result.Ok<IMemberPath>(path) : error;
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

file sealed class FakeWatcher<T>(
    ITickCounter ticks,
    FakePath path) : Watcher<T>(ticks)
{
    protected override Result<T> Read()
    {
        return path.Read<T>();
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
    FakePath Start,
    FakeStep Step) : ResultError
{
    public override string Message => $"{Start} doesn't take {Step}.";
}

file sealed record MissingItemError(
    FakePath Start,
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
    FakePath Path,
    FakePath Start) : ResultError
{
    public override string Message => $"Can't read {Path}: {Start} is null.";
}

file sealed record WrongTypeError(
    FakePath Path,
    string TypeName) : ResultError
{
    public override string Message => $"{Path} isn't a {TypeName}.";
}
