using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace AslHelp.Engines.Tests;

public sealed class StructValueTests
{
    private static readonly string[] _names = ["X", "Y", "Z"];

    // A member name finds its value and the lookup compares ordinally.
    [Test]
    public void Indexer_AMemberName_ReturnsItsValue()
    {
        StructValue vector = new("Vector", _names, [1f, 2f, 3f]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(vector["Y"], Is.EqualTo(2f));
            Assert.That(vector.TryGetValue("y", out _), Is.False);
            Assert.That(vector.TryGetValue("Z", out object? z), Is.True);
            Assert.That(z, Is.EqualTo(3f));
        }
    }

    // A name the struct doesn't have throws with the type and every member, so a typo is easy to fix.
    [Test]
    public void Indexer_AnUnknownName_ThrowsWithTheMembers()
    {
        StructValue vector = new("Vector", _names, [1f, 2f, 3f]);

        Assert.That(
            () => vector["W"],
            Throws.TypeOf<KeyNotFoundException>().With.Message.EqualTo("Vector doesn't have a member W. Members: X, Y, Z."));
    }

    // 2 values with the same type, names and values are equal with the same hash, also with a nested struct.
    [Test]
    public void Equals_SameNamesAndValues_IsEqual()
    {
        StructValue first = new("Box", ["Min", "Size"], [new StructValue("Vector", _names, [1f, 2f, 3f]), 4]);
        StructValue second = new("Box", ["Min", "Size"], [new StructValue("Vector", ["X", "Y", "Z"], [1f, 2f, 3f]), 4]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo(second));
            Assert.That(first.GetHashCode(), Is.EqualTo(second.GetHashCode()));
        }
    }

    // 1 other member value or another type name makes 2 values differ.
    [Test]
    public void Equals_OneValueDiffers_IsNotEqual()
    {
        StructValue vector = new("Vector", _names, [1f, 2f, 3f]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(vector, Is.Not.EqualTo(new StructValue("Vector", _names, [1f, 2f, 4f])));
            Assert.That(vector, Is.Not.EqualTo(new StructValue("Rotator", _names, [1f, 2f, 3f])));
            Assert.That(vector, Is.Not.EqualTo(new StructValue("Vector", ["A", "B", "C"], [1f, 2f, 3f])));
        }
    }

    // 2 members and 3 values throw with both counts.
    [Test]
    public void Ctor_CountsDiffer_ThrowsWithBothCounts()
    {
        Assert.That(
            () => new StructValue("Vector", ["X", "Y"], [1f, 2f, 3f]),
            Throws.ArgumentException.With.Property(nameof(ArgumentException.ParamName)).EqualTo("values")
                .And.Message.StartsWith("A struct value needs 1 value per name. Names: 2, values: 3."));
    }

    // A NaN member equals itself, so a watcher of a vector with NaN doesn't report a change on every tick.
    [Test]
    public void Equals_NaNMember_IsEqual()
    {
        StructValue first = new("Vector", _names, [float.NaN, 2f, 3f]);
        StructValue second = new("Vector", _names, [float.NaN, 2f, 3f]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo(second));
            Assert.That(first.GetHashCode(), Is.EqualTo(second.GetHashCode()));
        }
    }

    // An int 1 and a float 1 are different values, so a member that changes its type counts as a change.
    [Test]
    public void Equals_SameNumberOfAnotherType_IsNotEqual()
    {
        Assert.That(new StructValue("Pair", ["A"], [1]), Is.Not.EqualTo(new StructValue("Pair", ["A"], [1f])));
    }

    // A struct without members prints empty parentheses and equals another one of the same type.
    [Test]
    public void ToString_NoMembers_PrintsEmptyParentheses()
    {
        StructValue empty = new("Empty", [], []);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(empty.ToString(), Is.EqualTo("Empty()"));
            Assert.That(empty, Is.EqualTo(new StructValue("Empty", [], [])));
            Assert.That(empty.TryGetValue("X", out _), Is.False);
        }
    }

    // The text has the type and each member with its value in the invariant culture.
    [Test]
    [SetCulture("sv-SE")]
    public void ToString_AVector_PrintsTypeAndMembers()
    {
        StructValue vector = new("Vector", _names, [1f, -2.5f, null]);

        Assert.That(vector.ToString(), Is.EqualTo("Vector(X: 1, Y: -2.5, Z: null)"));
    }
}
