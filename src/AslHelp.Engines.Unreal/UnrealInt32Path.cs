using AslHelp.Memory;
using AslHelp.Memory.Watching;
using System;

namespace AslHelp.Engines.Unreal;

/// <summary>
///     Represents a path from a global of a game through object members to a 4-byte signed integer, like
///     <c>GWorld.AuthorityGameMode.FixtureData.Hp</c>.
/// </summary>
/// <remarks>
///     Read the value with a watcher from <see cref="CreateWatcher"/>.
///     When a read fails, like on a null object on the way, the watcher applies its <see cref="Watcher.FailAction"/>.
/// </remarks>
public sealed class UnrealInt32Path : IWatcherSource
{
    private readonly MemberPath _memberPath;

    /// <summary>
    ///     Initializes a new instance of the <see cref="UnrealInt32Path"/> class.
    /// </summary>
    /// <param name="memberPath">The path to read.</param>
    internal UnrealInt32Path(MemberPath memberPath)
    {
        _memberPath = memberPath;
    }

    /// <summary>
    ///     Creates a new watcher for the value at the end of the path.
    /// </summary>
    /// <param name="ticks">The tick counter that limits the watcher to 1 read per tick.</param>
    /// <returns>
    ///     The watcher.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="ticks"/> is <see langword="null"/>.
    /// </exception>
    public Watcher<int> CreateWatcher(ITickCounter ticks)
    {
        return new Int32PathWatcher(ticks, _memberPath);
    }

    /// <inheritdoc/>
    Watcher IWatcherSource.CreateWatcher(ITickCounter ticks)
    {
        return CreateWatcher(ticks);
    }

    /// <summary>
    ///     Returns the global and the members of the path joined by a period.
    /// </summary>
    /// <returns>
    ///     The text of the path, like <c>GWorld.AuthorityGameMode.FixtureData.Hp</c>.
    /// </returns>
    public override string ToString()
    {
        return _memberPath.ToString();
    }

    /// <summary>
    ///     Reads the value at the end of the path.
    /// </summary>
    /// <returns>
    ///     The value or a failure.
    /// </returns>
    internal Result<int> Read()
    {
        return _memberPath.ReadInt32();
    }
}

file sealed class Int32PathWatcher(
    ITickCounter ticks,
    MemberPath memberPath) : Watcher<int>(ticks, memberPath.Memory)
{
    protected override Result<int> Read(IMemoryReader reader)
    {
        return memberPath.ReadInt32();
    }

    public override string ToString()
    {
        return SR.FormatPathInt32Watcher(memberPath);
    }
}
