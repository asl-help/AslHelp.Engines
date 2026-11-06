using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace AslHelp.Engines.Measuring.Cli.Tests;

public sealed class VersionGroupsTests
{
    private static readonly string[] _a = ["a"];
    private static readonly string[] _aAndB = ["a", "b"];
    private static readonly string[] _aAndC = ["a", "c"];
    private static readonly string[] _b = ["b"];
    private static readonly string[] _bAndC = ["b", "c"];
    private static readonly string[] _bAndD = ["b", "d"];
    private static readonly string[] _cAndA = ["c", "a"];
    private static readonly Dictionary<string, IReadOnlyList<uint>> _noGlobals = [];
    private static readonly Dictionary<string, string> _noReasons = [];

    // 2 rows with the same types, members and values share 1 group with the offsets of the 1st row.
    [Test]
    public void Find_EqualOffsets_PutsBothVersionsInOneGroup()
    {
        var groups = VersionGroups.Find([Row("a", ("Type.Member", 0x28)), Row("b", ("Type.Member", 0x28))]);

        Assert.That(groups, Has.Count.EqualTo(1));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(groups[0].Versions, Is.EqualTo(_aAndB));
            Assert.That(groups[0].Offsets["Type"]["Member"], Is.EqualTo("0x28"));
        }
    }

    // A new group doesn't have routes, because the search runs afterward in CommandLine.
    [Test]
    public void Find_NewGroup_HasNoRoutes()
    {
        var groups = VersionGroups.Find([Row("a", ("Type.Member", 0x28))]);

        Assert.That(groups[0].Routes, Is.Empty);
    }

    // 1 member with another value is enough to keep 2 rows apart.
    [Test]
    public void Find_DifferentValue_SeparatesTheVersions()
    {
        var groups = VersionGroups.Find([
            Row("a", ("Type.First", 0x28), ("Type.Second", 0x2C)),
            Row("b", ("Type.First", 0x28), ("Type.Second", 0x30))]);

        AssertTwoGroups(groups, _a, _b);
    }

    // A member missing from 1 row keeps it apart even when every shared value is equal, because a member in unmeasured doesn't have an offset.
    [Test]
    public void Find_DifferentMemberKeys_SeparatesTheVersions()
    {
        var groups = VersionGroups.Find([
            Row("a", ("Type.First", 0x28), ("Type.Second", 0x2C)),
            Row("b", ("Type.First", 0x28))]);

        AssertTwoGroups(groups, _a, _b);
    }

    // The same member and value under another type keep 2 rows apart.
    [Test]
    public void Find_DifferentTypeKeys_SeparatesTheVersions()
    {
        var groups = VersionGroups.Find([Row("a", ("First.Member", 0x28)), Row("b", ("Second.Member", 0x28))]);

        AssertTwoGroups(groups, _a, _b);
    }

    // Rows group by equal offsets and not by position, so a group can skip a version between its members.
    [Test]
    public void Find_NonNeighborVersions_GroupsThem()
    {
        var groups = VersionGroups.Find([
            Row("a", ("Type.Member", 0x28)),
            Row("b", ("Type.Member", 0x2C)),
            Row("c", ("Type.Member", 0x28))]);

        AssertTwoGroups(groups, _aAndC, _b);
    }

    // A failed row between 2 equal rows does not join a group or split the 2 rows.
    [Test]
    public void Find_FailedRow_JoinsNoGroup()
    {
        var groups = VersionGroups.Find([
            Row("a", ("Type.Member", 0x28)),
            PlayerRow.Failed("failed", "The player did not start."),
            Row("b", ("Type.Member", 0x28))]);

        Assert.That(groups, Has.Count.EqualTo(1));
        Assert.That(groups[0].Versions, Is.EqualTo(_aAndB));
    }

    // 2 rows with empty offsets do not group as equal, because Find skips empty maps.
    [Test]
    public void Find_EmptyOffsets_JoinsNoGroup()
    {
        var groups = VersionGroups.Find([Row("a"), Row("b")]);

        Assert.That(groups, Is.Empty);
    }

    // The groups follow their 1st version and the versions inside a group keep the order of the run.
    [Test]
    public void Find_SeveralGroups_OrdersByFirstVersion()
    {
        var groups = VersionGroups.Find([
            Row("a", ("Type.Member", 0x28)),
            Row("b", ("Type.Member", 0x2C)),
            Row("c", ("Type.Member", 0x28)),
            Row("d", ("Type.Member", 0x2C))]);

        AssertTwoGroups(groups, _aAndC, _bAndD);
    }

    // A run where every player failed doesn't have any groups.
    [Test]
    public void Find_AllRowsFailed_ReturnsEmptyList()
    {
        var groups = VersionGroups.Find([PlayerRow.Failed("a", "The player did not start."), PlayerRow.Failed("b", "The player did not start.")]);

        Assert.That(groups, Is.Empty);
    }

    // A version with its own offsets forms a group of 1, so every measured row with offsets is in exactly 1 group.
    [Test]
    public void Find_RowAlone_FormsAGroupOf1()
    {
        var groups = VersionGroups.Find([Row("a", ("Type.Member", 0x28))]);

        Assert.That(groups, Has.Count.EqualTo(1));
        Assert.That(groups[0].Versions, Is.EqualTo(_a));
    }

    // Members in another insertion order are the same map, even though Measured keeps members with equal offsets in insertion order.
    [Test]
    public void Find_SameMapsInOtherOrder_PutsBothVersionsInOneGroup()
    {
        var groups = VersionGroups.Find([
            Row("a", ("First.Member", 0x28), ("First.Other", 0x28), ("Second.Member", 0x2C)),
            Row("b", ("Second.Member", 0x2C), ("First.Other", 0x28), ("First.Member", 0x28))]);

        Assert.That(groups, Has.Count.EqualTo(1));
        Assert.That(groups[0].Versions, Is.EqualTo(_aAndB));
    }

    // A later row with more members than the 1st row of a group stays apart, the same as when the larger row comes 1st.
    [Test]
    public void Find_LaterRowHasMoreMembers_SeparatesTheVersions()
    {
        var groups = VersionGroups.Find([
            Row("a", ("Type.First", 0x28)),
            Row("b", ("Type.First", 0x28), ("Type.Second", 0x2C))]);

        AssertTwoGroups(groups, _a, _b);
    }

    // The comparison uses the offsets only, so other globals do not split 2 rows.
    [Test]
    public void Find_DifferentGlobals_PutsBothVersionsInOneGroup()
    {
        var groups = VersionGroups.Find([
            Row("a", new Dictionary<string, IReadOnlyList<uint>> { ["Global"] = [0x100] }, _noReasons, ("Type.Member", 0x28)),
            Row("b", new Dictionary<string, IReadOnlyList<uint>> { ["Global"] = [0x200] }, _noReasons, ("Type.Member", 0x28))]);

        Assert.That(groups, Has.Count.EqualTo(1));
    }

    // A member in unmeasured keeps its row apart from a row with that member and in a group with a row that also lacks it.
    [Test]
    public void Find_MemberInUnmeasured_GroupsOnlyWithRowsThatLackIt()
    {
        Dictionary<string, string> unmeasured = new() { ["Type.Second"] = "Not found." };

        var groups = VersionGroups.Find([
            Row("a", ("Type.First", 0x28), ("Type.Second", 0x2C)),
            Row("b", _noGlobals, unmeasured, ("Type.First", 0x28)),
            Row("c", _noGlobals, unmeasured, ("Type.First", 0x28))]);

        AssertTwoGroups(groups, _a, _bAndC);
    }

    // The group order is the row order, because the caller passes the rows in the order of the run.
    [Test]
    public void Find_RowsOutOfVersionOrder_KeepsTheRowOrder()
    {
        var groups = VersionGroups.Find([
            Row("c", ("Type.Member", 0x28)),
            Row("b", ("Type.Member", 0x2C)),
            Row("a", ("Type.Member", 0x28))]);

        AssertTwoGroups(groups, _cAndA, _b);
    }

    private static void AssertTwoGroups(IReadOnlyList<VersionGroup> groups, string[] first, string[] second)
    {
        Assert.That(groups, Has.Count.EqualTo(2));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(groups[0].Versions, Is.EqualTo(first));
            Assert.That(groups[1].Versions, Is.EqualTo(second));
        }
    }

    private static PlayerRow Row(string version, params (string Key, int Offset)[] offsets)
    {
        return Row(version, _noGlobals, _noReasons, offsets);
    }

    private static PlayerRow Row(
        string version,
        IReadOnlyDictionary<string, IReadOnlyList<uint>> globals,
        IReadOnlyDictionary<string, string> unmeasured,
        params (string Key, int Offset)[] offsets)
    {
        return PlayerRow.Measured(version, new PlayerMeasurement(
            globals,
            offsets.ToDictionary(static offset => offset.Key, static offset => offset.Offset),
            unmeasured,
            "file.bin"));
    }
}
