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
    /// <exception cref="InvalidOperationException">
    ///     Children, Next or Offset was not found, the class, the spawned object or the field does not exist,
    ///     the offset of the field or the value does not read or the value is not <see cref="ExpectedValue"/>.
    /// </exception>
    public static void Run(IMemoryReader memory, UdkMeasuredObjects measured, IReadOnlyDictionary<string, int> offsets, ILogger logger)
    {
        foreach (string member in (string[])[UdkMembers.Children, UdkMembers.Next, UdkMembers.Offset])
        {
            if (!offsets.ContainsKey(member))
            {
                throw new InvalidOperationException(UdkSR.FormatFixtureCheckNeeds(member));
            }
        }

        var objects = measured.Reader;
        StructReader structs = new(
            memory,
            objects,
            new StructLayout(UnknownOffset, offsets[UdkMembers.Children], UnknownOffset),
            new FieldLayout(offsets[UdkMembers.Next]));

        // Compare the class and not only the name, because a property of the game has the same name as the class.
        var fixtureClass = measured.ObjectsByName[ClassName].FirstOrDefault(obj => objects.ClassOf(obj) == measured.ClassClass);

        if (fixtureClass == Address.Zero)
        {
            throw new InvalidOperationException(UdkSR.FormatClassNotFound(ClassName));
        }

        // Try the objects with the class name first, because the game spawns the instance under that name and each other object costs 1 read.
        // Skip the class default objects, because they have the class too but not the value the game sets.
        // Skip an object whose name does not read, because a live instance always has a name.
        var instance = measured.ObjectsByName[ClassName]
            .Concat(measured.ObjectsByName
                .Where(static group => group.Key?.StartsWith(DefaultObjectPrefix, StringComparison.Ordinal) == false)
                .SelectMany(static group => group))
            .FirstOrDefault(obj => objects.ClassOf(obj) == fixtureClass);

        if (instance == Address.Zero)
        {
            throw new InvalidOperationException(UdkSR.FixtureInstanceNotFound);
        }

        var field = structs.ChildrenOf(fixtureClass).FirstOrDefault(
            child => string.Equals(objects.NameOf(child), FieldName, StringComparison.Ordinal));

        if (field == Address.Zero)
        {
            throw new InvalidOperationException(UdkSR.FixtureFieldNotFound);
        }

        var offsetAddress = field + offsets[UdkMembers.Offset];

        if (!memory.Read<int>(offsetAddress).TryUnwrap(out int fieldOffset))
        {
            throw new InvalidOperationException(UdkSR.FormatFixtureOffsetUnreadable(offsetAddress));
        }

        var valueAddress = instance + fieldOffset;

        if (!memory.Read<int>(valueAddress).TryUnwrap(out int value))
        {
            throw new InvalidOperationException(UdkSR.FormatFixtureValueUnreadable(valueAddress));
        }

        if (value != ExpectedValue)
        {
            throw new InvalidOperationException(UdkSR.FormatFixtureValueWrong(ExpectedValue, value));
        }

        logger.FixtureValueRead(value, instance);
    }
}
