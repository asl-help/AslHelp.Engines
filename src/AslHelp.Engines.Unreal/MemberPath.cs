using AslHelp.Engines.Unreal.Errors;
using AslHelp.Memory;
using AslHelp.Memory.Locating;
using System.Text;

namespace AslHelp.Engines.Unreal;

/// <summary>
///     Represents a path from a global of a game through object members, read by the real class of each object on the way.
/// </summary>
/// <remarks>
///     A path is immutable. Its segments are the global, then 1 per member hop.
///     A read that succeeds doesn't allocate once each hop resolved the class of its object.
///     A class name with a number, like <c>BP_Player_C_1</c>, still allocates the name on each read of that hop.
///     Isn't thread-safe.
/// </remarks>
internal sealed class MemberPath
{
    private readonly UnrealGame _game;
    private readonly GlobalSlot _globalSlot;
    private readonly MemberHop[] _hops;

    // Keep the error of each segment that reads from a null object, because a game that's still loading fails that way on every tick.
    private readonly PathError?[] _nullErrors;

    // Keep the last failure of each segment with its cause, because a hop returns the same not-found error on every tick.
    private readonly (ResultError Cause, PathError Error)?[] _segmentErrors;

    private string? _text;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MemberPath"/> class.
    /// </summary>
    /// <param name="game">The game to read from.</param>
    /// <param name="globalSlot">The slot of the global the path starts at.</param>
    /// <param name="hops">The hop of the global, then 1 per member.</param>
    public MemberPath(UnrealGame game, GlobalSlot globalSlot, MemberHop[] hops)
    {
        _game = game;
        _globalSlot = globalSlot;
        _hops = hops;
        _nullErrors = new PathError?[hops.Length];
        _segmentErrors = new (ResultError, PathError)?[hops.Length];
    }

    /// <summary>
    ///     Gets the memory of the game.
    /// </summary>
    public IMemoryReader Memory => _game.Memory;

    /// <summary>
    ///     Gets the number of segments, the global and 1 per member hop.
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
        return new MemberPath(_game, _globalSlot, [.. _hops, new MemberHop(name)]);
    }

    /// <summary>
    ///     Reads the object pointer at the end of the path.
    /// </summary>
    /// <returns>
    ///     The address of the object or a failure. A pointer of 0 at the end returns <see cref="Address.Zero"/>.
    /// </returns>
    public Result<Address> ReadObject()
    {
        return Follow(SegmentCount);
    }

    /// <summary>
    ///     Reads the 4-byte signed integer at the end of the path.
    /// </summary>
    /// <remarks>
    ///     Needs a path with a member after the global.
    /// </remarks>
    /// <returns>
    ///     The value or a failure.
    /// </returns>
    public Result<int> ReadInt32()
    {
        int last = _hops.Length - 1;

        if (!Follow(last).TryUnwrap(out var obj, out var error) || !PropertyAt(last, obj).TryUnwrap(out var property, out error))
        {
            return error;
        }

        return property.ReadInt32(_game.Memory, obj).TryUnwrap(out int value, out error)
            ? value
            : SegmentFailed(last, error);
    }

    /// <summary>
    ///     Returns the segments of the path joined by a period, like <c>GWorld.AuthorityGameMode</c>.
    /// </summary>
    /// <returns>
    ///     The text of the path.
    /// </returns>
    public override string ToString()
    {
        return _text ??= ToString(SegmentCount);
    }

    /// <summary>
    ///     Returns the first <paramref name="segmentCount"/> segments of the path joined by a period.
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

    // Read the global, then the object pointer of each segment before segmentCount.
    private Result<Address> Follow(int segmentCount)
    {
        if (!_globalSlot.Read().TryUnwrap(out var obj, out var error))
        {
            return SegmentFailed(0, error);
        }

        for (int segment = 1; segment < segmentCount; segment++)
        {
            if (!PropertyAt(segment, obj).TryUnwrap(out var property, out error))
            {
                return error;
            }

            if (!property.ReadObject(_game.Memory, obj).TryUnwrap(out obj, out error))
            {
                return SegmentFailed(segment, error);
            }
        }

        return obj;
    }

    // Find the property of a segment on the class of the object it's read from.
    private Result<UnrealProperty> PropertyAt(int segment, Address obj)
    {
        if (obj == Address.Zero)
        {
            return _nullErrors[segment] ??= PathError.NullObject(this, segment);
        }

        if (!_game.ClassOf(obj).TryUnwrap(out var klass, out var error)
            || !_hops[segment].Resolve(klass).TryUnwrap(out var property, out error))
        {
            return SegmentFailed(segment, error);
        }

        return property;
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
