using AslHelp.Memory;
using AslHelp.Memory.Watching;
using System;

namespace AslHelp.Engines.Unreal.Paths;

/// <summary>
///     Represents a path from a root object of a game through members to a value of type <typeparamref name="T"/>, like
///     <c>GWorld.AuthorityGameMode.FixtureData.Hp</c>.
/// </summary>
/// <remarks>
///     Create one with <see cref="UnrealPath.Value{T}"/> and read the value with a watcher from <see cref="CreateWatcher"/>.
///     When a read fails, like on a null object on the way, the watcher applies its <see cref="Watcher.FailAction"/>.
///     Isn't thread-safe, because a read keeps the property of each hop.
/// </remarks>
/// <typeparam name="T">The type of the value.</typeparam>
internal sealed class UnrealValuePath<T> : IWatcherSource<T>
{
    private readonly MemberPath _memberPath;

    /// <summary>
    ///     Initializes a new instance of the <see cref="UnrealValuePath{T}"/> class.
    /// </summary>
    /// <param name="memberPath">The path to read.</param>
    public UnrealValuePath(MemberPath memberPath)
    {
        _memberPath = memberPath;
    }

    /// <summary>
    ///     Creates a new watcher for the value at the end of the path.
    /// </summary>
    /// <remarks>
    ///     A steady read doesn't allocate. A string watcher keeps its text while the characters stay the same.
    /// </remarks>
    /// <param name="ticks">The tick counter that limits the watcher to 1 read per tick.</param>
    /// <returns>
    ///     The watcher.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="ticks"/> is <see langword="null"/>.
    /// </exception>
    public Watcher<T> CreateWatcher(ITickCounter ticks)
    {
        return new ValuePathWatcher<T>(ticks, _memberPath);
    }

    /// <inheritdoc/>
    Watcher IWatcherSource.CreateWatcher(ITickCounter ticks)
    {
        return CreateWatcher(ticks);
    }

    /// <summary>
    ///     Returns the root and the steps of the path.
    /// </summary>
    /// <returns>
    ///     The text of the path, like <c>GWorld.AuthorityGameMode.FixtureData.Hp</c>.
    /// </returns>
    public override string ToString()
    {
        return _memberPath.ToString();
    }
}

file sealed class ValuePathWatcher<T> : Watcher<T>
{
    private readonly MemberPath _memberPath;

    // Keep the last value apart from Current, because Clear empties Current and a string read returns the last text when it's equal.
    private T? _last;

    public ValuePathWatcher(ITickCounter ticks, MemberPath memberPath)
        : base(ticks)
    {
        _memberPath = memberPath;
    }

    public override string ToString()
    {
        return SR.FormatPathValueWatcher(typeof(T).Name, _memberPath);
    }

    protected override Result<T> Read()
    {
        var result = _memberPath.ReadValue(Ticks, _last);

        if (result.IsOk)
        {
            _last = result.Value;
        }

        return result;
    }
}
