using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace AslHelp.Engines.Measuring.Cli.Tests;

public sealed class VersionGroupsEdgeTests
{
    private static readonly string[] _aAndC = ["a", "c"];
    private static readonly string[] _keys = ["first", "second"];
    private static readonly Dictionary<string, IReadOnlyList<uint>> _noGlobals = [];
    private static readonly Dictionary<string, int> _noOffsets = [];
    private static readonly Dictionary<string, string> _noReasons = [];
    private static readonly Dictionary<string, IReadOnlyList<uint>> _oneGlobal = new() { ["Global"] = [0x100] };

    // Rows with a global and without offsets group by their key, because the route search runs per group and a list can leave out every type.
    [Test]
    public void Find_RowsWithOnlyGlobals_GroupByKey()
    {
        var groups = VersionGroups.Find(
            [Row("a", _oneGlobal), Row("b", _oneGlobal), Row("c", _oneGlobal)],
            static version => version == "b" ? "second" : "first", static (_, _) => true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(groups.Select(static group => group.Key), Is.EqualTo(_keys));
            Assert.That(groups[0].Versions, Is.EqualTo(_aAndC));
            Assert.That(groups[0].Offsets, Is.Empty);
        }
    }

    // A row without any value stays out, also when the rows around it have a global.
    [Test]
    public void Find_RowWithoutAnyValue_JoinsNoGroup()
    {
        var groups = VersionGroups.Find(
            [Row("a", _oneGlobal), Row("b", _noGlobals), Row("c", _oneGlobal)],
            static _ => "first",
            static (_, _) => true);

        Assert.That(groups.Single().Versions, Is.EqualTo(_aAndC));
    }

    private static PlayerRow Row(string version, Dictionary<string, IReadOnlyList<uint>> globals)
    {
        return PlayerRow.Measured(version, new PlayerMeasurement(globals, _noOffsets, _noReasons, "file.bin"));
    }
}
