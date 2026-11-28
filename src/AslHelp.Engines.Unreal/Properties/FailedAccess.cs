using AslHelp.Memory;
using System;

namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Represents the reads of a property that always fail the same way, like a kind the library doesn't read
///     or a kind whose offsets the profile doesn't have.
/// </summary>
/// <param name="property">The property.</param>
/// <param name="error">The failure of every read.</param>
internal sealed class FailedAccess(
    UnrealProperty property,
    ResultError error) : PropertyAccess(property)
{
    private readonly ResultError _error = error;

    /// <inheritdoc/>
    public override bool AlwaysFails => true;

    /// <inheritdoc/>
    public override ResultError NotValueOf(Type type)
    {
        return _error;
    }

    /// <inheritdoc/>
    public override Result<object?> ReadBoxed(IMemoryReader memory, Address container, object? last)
    {
        return _error;
    }

    /// <inheritdoc/>
    public override Result<Container> Step(IMemoryReader memory, Address container)
    {
        return _error;
    }
}
