using AslHelp.Engines.Unreal.Properties;
using NUnit.Framework;

namespace AslHelp.Engines.Unreal.Tests.Properties;

public sealed class PropertyKindsTests
{
    // ClassProperty, the Ptr classes of 5.0 to 5.3 and ComponentProperty of Unreal Engine 3 all read as object pointers.
    [TestCase("ClassProperty")]
    [TestCase("ClassPtrProperty")]
    [TestCase("ComponentProperty")]
    [TestCase("ObjectPtrProperty")]
    public void Find_ObjectPropertyNames_ReturnsTheObjectKind(string className)
    {
        Assert.That(PropertyKinds.Find(className), Is.SameAs(PropertyKinds.Find("ObjectProperty")));
    }

    // Each number class gets its own kind, so Int8Property and ByteProperty don't share one.
    [Test]
    public void Find_NumberClasses_ReturnsAKindPerClass()
    {
        Assert.That(PropertyKinds.Find("Int8Property"), Is.Not.SameAs(PropertyKinds.Find("ByteProperty")));
    }

    // 5.x reflects the double members of FVector as LargeWorldCoordinatesRealProperty, which reads as a number.
    [Test]
    public void Find_LargeWorldCoordinatesReal_ReturnsANumberKind()
    {
        Assert.That(PropertyKinds.Find("LargeWorldCoordinatesRealProperty").IsNumber, Is.True);
    }

    // The collection classes and the weak pointer get their own kinds, which aren't the unsupported kind.
    [TestCase("ArrayProperty")]
    [TestCase("MapProperty")]
    [TestCase("SetProperty")]
    [TestCase("WeakObjectProperty")]
    public void Find_CollectionAndWeakClasses_ReturnTheirKinds(string className)
    {
        Assert.That(PropertyKinds.Find(className), Is.Not.SameAs(PropertyKinds.Find("NotAProperty")));
    }

    // Find compares ordinally, so a class name in another case is a kind the library doesn't read.
    [TestCase("intproperty")]
    [TestCase("TextProperty")]
    [TestCase("SoftObjectProperty")]
    public void Find_OtherClassNames_ReturnsTheUnsupportedKind(string className)
    {
        Assert.That(PropertyKinds.Find(className), Is.SameAs(PropertyKinds.Find("NotAProperty")));
    }
}
