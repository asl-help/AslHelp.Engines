using AslHelp.Engines.Unreal;
using AslHelp.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace AslHelp.Engines.Measuring.Cli.Unreal3;

/// <summary>
///     Finds the globals and the member offsets of a UDK player in its memory.
/// </summary>
internal static class UdkMeasurement
{
    /// <summary>
    ///     Measures the player whose memory is <paramref name="memory"/>.
    /// </summary>
    /// <param name="memory">The memory of the process.</param>
    /// <param name="moduleBase">The base of the executable in the process.</param>
    /// <param name="sections">The sections of the executable without code.</param>
    /// <param name="targets">The targets by global, with exactly 1 name array and 1 object array.</param>
    /// <param name="logger">The logger for the steps of the measurement.</param>
    /// <returns>
    ///     The globals and the offsets found and the reason for each global or member that was not found.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     An array, the name, class or outer offset or a class of the super chain was not found or 2 arrays with different data fit.
    /// </exception>
    public static PlayerMeasurement Measure(
        IMemoryReader memory,
        Address moduleBase,
        IReadOnlyList<UdkSection> sections,
        IReadOnlyDictionary<string, UdkTarget> targets,
        ILogger logger)
    {
        var firstNames = targets.Values.OfType<UdkNameArrayTarget>().Single().FirstNames;

        var names = UdkArrays.FindNames(memory, moduleBase, sections, firstNames);

        string nameRvas = FormatRvas(names.Rvas);
        logger.NameArrayFound(nameRvas);

        var objects = UdkArrays.FindObjects(memory, moduleBase, sections, names.Rvas);

        string objectRvas = FormatRvas(objects.Rvas);
        logger.ObjectArrayFound(objects.Pointers.Count, objectRvas);

        Dictionary<string, int> offsets = [];
        Dictionary<string, string> unmeasured = [];

        UdkMembers.Measure(memory, names, objects, offsets, unmeasured);

        logger.OffsetsFound(offsets.Count);

        Dictionary<string, IReadOnlyList<uint>> globals = [];
        HashSet<Address> known = [.. objects.Pointers];

        foreach ((string global, var target) in targets)
        {
            switch (target)
            {
                case UdkNameArrayTarget:
                {
                    globals[global] = names.Rvas;

                    break;
                }
                case UdkObjectArrayTarget:
                {
                    globals[global] = objects.Rvas;

                    break;
                }
                case UdkObjectSlotTarget when !offsets.ContainsKey(UdkMembers.SuperField):
                {
                    unmeasured[global] = UdkSR.FormatUnmeasuredNeeds(UdkMembers.SuperField);

                    break;
                }
                case UdkObjectSlotTarget slot:
                {
                    var rvas = FindSlots(memory, sections, names, known, offsets, slot);

                    if (rvas.Count == 0)
                    {
                        unmeasured[global] = UdkSR.FormatUnmeasuredSlot(slot.Class);
                    }
                    else
                    {
                        globals[global] = rvas;

                        string slotRvas = FormatRvas(rvas);
                        logger.SlotFound(global, slotRvas);
                    }

                    break;
                }
                default:
                {
                    throw new UnreachableException();
                }
            }
        }

        return new PlayerMeasurement(globals, offsets, unmeasured);
    }

    private static string FormatRvas(IEnumerable<uint> rvas)
    {
        return string.Join(", ", rvas.Select(static rva => $"0x{rva:X}"));
    }

    /// <summary>
    ///     Lists every static that points at an object in the object array that fits <paramref name="slot"/>.
    /// </summary>
    /// <param name="memory">The memory of the process.</param>
    /// <param name="sections">The sections of the executable without code.</param>
    /// <param name="names">The name array.</param>
    /// <param name="known">The objects in the object array.</param>
    /// <param name="offsets">The offsets found.</param>
    /// <param name="slot">The global to find.</param>
    /// <returns>
    ///     The RVAs in the order of <paramref name="sections"/>.
    /// </returns>
    private static List<uint> FindSlots(
        IMemoryReader memory,
        IReadOnlyList<UdkSection> sections,
        UdkNameArray names,
        HashSet<Address> known,
        Dictionary<string, int> offsets,
        UdkObjectSlotTarget slot)
    {
        ObjectLayout layout = new(
            offsets[UdkMembers.ObjectInternalInteger],
            offsets[UdkMembers.ObjectOuter],
            offsets[UdkMembers.ObjectName],
            offsets[UdkMembers.ObjectClass]);
        ObjectReader reader = new(memory, names.Table, layout);
        StructReader structs = new(memory, reader, new StructLayout(offsets[UdkMembers.SuperField], -1, -1), new FieldLayout(-1));
        List<uint> rvas = [];

        foreach ((uint rva, var value) in UdkSections.Pointers(sections, memory.Layout))
        {
            if (known.Contains(value)
                && structs.DerivesFrom(reader.ClassOf(value), slot.Class)
                && (slot.Object is null || string.Equals(reader.NameOf(value), slot.Object, StringComparison.Ordinal)))
            {
                rvas.Add(rva);
            }
        }

        return rvas;
    }
}
