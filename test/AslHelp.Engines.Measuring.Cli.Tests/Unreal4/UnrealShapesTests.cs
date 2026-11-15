using AslHelp.Engines.Measuring.Cli.Unreal4;
using NUnit.Framework;
using System;

namespace AslHelp.Engines.Measuring.Cli.Tests.Unreal4;

public sealed class UnrealShapesTests
{
    // The table matches the $shape values of the engine library profiles.
    [TestCase(4, 0, "pointers", "indirect", "uproperty")]
    [TestCase(4, 6, "pointers", "indirect", "uproperty")]
    [TestCase(4, 8, "indirect", "indirect", "uproperty")]
    [TestCase(4, 10, "indirect", "indirect", "uproperty")]
    [TestCase(4, 11, "flat", "indirect", "uproperty")]
    [TestCase(4, 18, "flat", "indirect", "uproperty")]
    [TestCase(4, 20, "chunked", "indirect", "uproperty")]
    [TestCase(4, 22, "chunked", "indirect", "uproperty")]
    [TestCase(4, 23, "chunked", "chunked", "uproperty")]
    [TestCase(4, 25, "chunked", "chunked", "ffield")]
    [TestCase(5, 8, "chunked", "chunked", "ffield")]
    public void Of_ProfileVersions_MatchTheProfiles(int major, int minor, string objectArray, string nameEntry, string field)
    {
        var shapes = UnrealShapes.Of(new Version(major, minor)).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(UnrealShapes.NameOf(shapes["FUObjectArray"]), Is.EqualTo(objectArray));
            Assert.That(UnrealShapes.NameOf(shapes["FNameEntry"]), Is.EqualTo(nameEntry));
            Assert.That(UnrealShapes.NameOf(shapes["FField"]), Is.EqualTo(field));
        }
    }

    // A minor before the 1st layout of a type fails with the type and the start of the table, so a new engine line shows.
    [Test]
    public void Of_MinorBeforeTheTable_FailsWithTheType()
    {
        Assert.That(
            UnrealShapes.Of(new Version(3, 5)).UnwrapErr().Message,
            Is.EqualTo(UnrealSR.FormatShapeNotListed("FField", new Version(3, 5), new Version(4, 0))));
    }

    // The last minor before each change keeps the older shape.
    [TestCase(4, 7, "FUObjectArray", "pointers")]
    [TestCase(4, 19, "FUObjectArray", "flat")]
    [TestCase(4, 24, "FField", "uproperty")]
    public void Of_LastMinorBeforeAChange_KeepsTheOlderShape(int major, int minor, string type, string shape)
    {
        Assert.That(UnrealShapes.NameOf(UnrealShapes.Of(new Version(major, minor)).Unwrap()[type]), Is.EqualTo(shape));
    }
}
