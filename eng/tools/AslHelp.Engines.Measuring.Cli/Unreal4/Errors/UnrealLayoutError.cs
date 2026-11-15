using System;
using System.Collections.Generic;
using System.Linq;

namespace AslHelp.Engines.Measuring.Cli.Unreal4.Errors;

/// <summary>
///     Represents a type, a member or a constant of an Unreal player that was not found or does not fit.
/// </summary>
internal sealed record UnrealLayoutError : ResultError
{
    private UnrealLayoutError(string message)
    {
        Message = message;
    }

    /// <inheritdoc/>
    public override string Message { get; }

    /// <summary>
    ///     Creates the error for a constant whose value doesn't fit the 32 bits of a report value.
    /// </summary>
    /// <param name="name">The name of the constant.</param>
    /// <param name="value">The value.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UnrealLayoutError ConstantOutOfRange(string name, long value)
    {
        return new(UnrealSR.FormatConstantOutOfRange(name, value));
    }

    /// <summary>
    ///     Creates the error for a value that isn't the same in every layout of a type.
    /// </summary>
    /// <param name="type">The name of the type.</param>
    /// <param name="member">The name of the member or <c>sizeof</c>.</param>
    /// <param name="values">The distinct values.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UnrealLayoutError LayoutsDiffer(string type, string member, IEnumerable<uint> values)
    {
        return new(UnrealSR.FormatLayoutsDiffer(type, member, string.Join(", ", values.Select(static value => $"0x{value:X}"))));
    }

    /// <summary>
    ///     Creates the error for a member that a type doesn't have inside its range.
    /// </summary>
    /// <param name="type">The name of the type.</param>
    /// <param name="member">The name of the member.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UnrealLayoutError MemberNotFound(string type, string member)
    {
        return new(UnrealSR.FormatMemberNotFound(type, member));
    }

    /// <summary>
    ///     Creates the error for a version before the 1st layout that the table of shapes lists for a type.
    /// </summary>
    /// <param name="type">The name of the type.</param>
    /// <param name="minor">The minor version of the player.</param>
    /// <param name="first">The minor version of the 1st layout.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UnrealLayoutError ShapeNotListed(string type, Version minor, Version first)
    {
        return new(UnrealSR.FormatShapeNotListed(type, minor, first));
    }
}
