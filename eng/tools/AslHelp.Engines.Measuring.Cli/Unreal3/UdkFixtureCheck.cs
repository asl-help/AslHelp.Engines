using AslHelp.Engines.Unreal;
using AslHelp.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AslHelp.Engines.Measuring.Cli.Unreal3;

/// <summary>
///     Checks the offsets found against the value the fixture game sets when play begins.
/// </summary>
internal static class UdkFixtureCheck
{
    /// <summary>
    ///     Represents the name of the class the fixture game spawns.
    /// </summary>
    public const string ClassName = "FixtureData";

    /// <summary>
    ///     Represents the value the fixture game gives the field.
    /// </summary>
    public const int ExpectedValue = 1337;

    /// <summary>
    ///     Represents the name of the field to read.
    /// </summary>
    public const string FieldName = "IntValue";

    private const string DefaultObjectPrefix = "Default__";
    private const int UnknownOffset = -1;

    /// <summary>
    ///     Reads the field of the spawned object through the offsets found and refuses any other value than <see cref="ExpectedValue"/>.
    /// </summary>
    /// <param name="memory">The memory of the process.</param>
    /// <param name="measured">The objects found and the reader with the offsets found.</param>
    /// <param name="offsets">The offsets found.</param>
    /// <param name="logger">The logger for the value read.</param>
    /// <returns>
    ///     A failure when Children, Next or Offset was not found, the class, the spawned object or the field does not exist,
    ///     the fields, the offset of the field or the value do not read or the value is not <see cref="ExpectedValue"/>.
    /// </returns>
    public static Result Run(IMemoryReader memory, UdkMeasuredObjects measured, IReadOnlyDictionary<string, int> offsets, ILogger logger)
    {
        foreach (string member in (string[])[UdkMembers.Children, UdkMembers.Next, UdkMembers.Offset])
        {
            if (!offsets.ContainsKey(member))
            {
                return UdkError.FixtureCheckNeeds(member);
            }
        }

        var objects = measured.Reader;
        StructReader structs = new(
            memory,
            objects,
            new StructLayout(UnknownOffset, offsets[UdkMembers.Children], UnknownOffset),
            new FieldLayout(offsets[UdkMembers.Next]));

        // Compare the class and not only the name, because a property of the game has the same name as the class.
        var fixtureClass = measured.ObjectsByName[ClassName]
            .FirstOrDefault(obj => objects.ClassOf(obj).UnwrapOr(Address.Zero) == measured.ClassClass);

        if (fixtureClass == Address.Zero)
        {
            return UdkError.ClassNotFound(ClassName);
        }

        // Try the objects with the class name first, because the game spawns the instance under that name and each other object costs 1 read.
        // Skip the class default objects, because they have the class too but not the value the game sets.
        // Skip an object whose name does not read, because a live instance always has a name.
        var instance = measured.ObjectsByName[ClassName]
            .Concat(measured.ObjectsByName
                .Where(static group => group.Key is { } name && !name.StartsWith(DefaultObjectPrefix, StringComparison.Ordinal))
                .SelectMany(static group => group))
            .FirstOrDefault(obj => objects.ClassOf(obj).UnwrapOr(Address.Zero) == fixtureClass);

        if (instance == Address.Zero)
        {
            return UdkError.FixtureInstanceNotFound();
        }

        var field = Address.Zero;
        ResultError? firstNameError = null;

        foreach (var child in structs.ChildrenOf(fixtureClass))
        {
            if (!child.TryUnwrap(out var address, out var chainError))
            {
                return UdkError.FixtureChildrenUnreadable(chainError);
            }

            // Go on past a name that does not read, because the field behind it can still be IntValue.
            if (!objects.NameOf(address).TryUnwrap(out string? name, out var nameError))
            {
                firstNameError ??= nameError;

                continue;
            }

            if (string.Equals(name, FieldName, StringComparison.Ordinal))
            {
                field = address;

                break;
            }
        }

        if (field == Address.Zero)
        {
            return firstNameError is null ? UdkError.FixtureFieldNotFound() : UdkError.FixtureFieldNameUnreadable(firstNameError);
        }

        var offsetAddress = field + offsets[UdkMembers.Offset];

        if (!memory.Read<int>(offsetAddress).TryUnwrap(out int fieldOffset, out var error))
        {
            return UdkError.FixtureOffsetUnreadable(offsetAddress, error);
        }

        var valueAddress = instance + fieldOffset;

        if (!memory.Read<int>(valueAddress).TryUnwrap(out int value, out error))
        {
            return UdkError.FixtureValueUnreadable(valueAddress, error);
        }

        if (value != ExpectedValue)
        {
            return UdkError.FixtureValueWrong(ExpectedValue, value);
        }

        logger.FixtureValueRead(value, instance);

        return Result.Ok();
    }
}
