using AslHelp.Engines.Unreal.Errors;
using AslHelp.Memory;
using NUnit.Framework;
using System.Collections;
using System.Globalization;
using System.Linq;

namespace AslHelp.Engines.Unreal.Tests;

public sealed class SRTests
{
    // Pin the full name, because ILRepack renames a second SR with the same name and its lookup then fails.
    [Test]
    public void ResourceName_IsUniqueToThisAssembly()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(typeof(SR).FullName, Is.EqualTo("AslHelp.Engines.Unreal.SR"));
            Assert.That(typeof(SR).Assembly.GetManifestResourceNames(), Does.Contain("AslHelp.Engines.Unreal.SR.resources"));
        }
    }

    [Test]
    public void Property_ReadsTheResource()
    {
        Assert.That(SR.ObjectNull, Is.EqualTo("The object pointer is 0."));
    }

    [Test]
    public void FormatMethod_FillsThePlaceholders()
    {
        Assert.That(SR.FormatNameIdOutOfRange(7, 3), Is.EqualTo("Name 7 is outside the array of 3 names."));
    }

    // Read the neutral text, because the assembly doesn't ship a satellite for any culture.
    [Test]
    [SetUICulture("de-DE")]
    public void OtherUICulture_FallsBackToTheNeutralText()
    {
        Assert.That(SR.ObjectNull, Is.EqualTo("The object pointer is 0."));
    }

    [Test]
    public void EveryEntry_HasText()
    {
        var set = SR.ResourceManager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: true);

        Assert.That(set, Is.Not.Null);

        var entries = set!.Cast<DictionaryEntry>().ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entries, Is.Not.Empty);
            Assert.That(entries.Select(static entry => entry.Value as string), Has.None.Null.And.None.Empty);
        }
    }

    // Pass the offset as hex text, because the generator only handles a plain placeholder.
    [Test]
    public void Message_FormatsTheOffsetAsHex()
    {
        Address obj = new(0x1000);
        Address address = new(0x1010);

        Assert.That(
            ObjectError.ClassUnreadable(obj, address, 0x2C, ObjectError.NullObject()).Message,
            Is.EqualTo(
                $"Can't read the class of the object at {obj}: reading {address} failed. The object pointer is 0. "
                + "Check ObjectLayout.Class (0x2C) against this engine version."));
    }
}
