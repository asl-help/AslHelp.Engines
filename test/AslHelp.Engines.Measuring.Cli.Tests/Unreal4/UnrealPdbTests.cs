using AslHelp.Engines.Measuring.Cli.Pdb;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;

namespace AslHelp.Engines.Measuring.Cli.Tests.Unreal4;

/// <summary>
///     Pins the values of the 4.19 x64 Shipping player, read from its real PDB.
/// </summary>
public sealed class UnrealPdbTests
{
    private const string PdbVariable = "FIXTURES_UNREAL_PDB";

    private static readonly uint[] _ioBufferSizes = [8, 48];
    private static readonly uint[] _localeSizes = [344, 352];

    // The PDB and the executable beside it come from 1 link.
    [Test]
    public void Open_RealPdb_IdentityMatchesExe()
    {
        string path = PdbPath();
        using var pdb = PdbFile.Open(path).Unwrap();

        var executable = ExecutableIdentity.Read(Path.ChangeExtension(path, ".exe")).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pdb.Identity, Is.EqualTo(new PdbIdentity(new Guid("d43d3384-a767-42b9-8883-d83e5ab17338"), 1)));
            Assert.That(pdb.Identity.Check(executable).IsOk, Is.True);
            Assert.That(executable.PdbName, Does.EndWith("Fixture-Win64-Shipping.pdb"));
        }
    }

    // The types the measurer reads have these sizes and offsets, with base class members folded and 1 layout per type.
    [Test]
    public void FindLayouts_RealPdb_PinsSizesAndOffsets()
    {
        using var pdb = PdbFile.Open(PdbPath()).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            CheckType(
                pdb,
                "UObjectBase",
                0x28,
                ("ObjectFlags", 0x8),
                ("InternalIndex", 0xC),
                ("ClassPrivate", 0x10),
                ("NamePrivate", 0x18),
                ("OuterPrivate", 0x20));
            CheckType(pdb, "UField", 0x30, ("Next", 0x28), ("ClassPrivate", 0x10));
            CheckType(
                pdb,
                "UStruct",
                0x88,
                ("Next", 0x28),
                ("SuperStruct", 0x30),
                ("Children", 0x38),
                ("PropertiesSize", 0x40),
                ("NamePrivate", 0x18));
            CheckType(pdb, "UProperty", 0x70, ("ArrayDim", 0x30), ("ElementSize", 0x34), ("PropertyFlags", 0x38), ("Offset_Internal", 0x44));
            CheckType(pdb, "UObjectPropertyBase", 0x78, ("PropertyClass", 0x70), ("ArrayDim", 0x30));
            CheckType(pdb, "FNameEntry", 0x810, ("Index", 0x0), ("AnsiName", 0x10), ("WideName", 0x10));
            CheckType(pdb, "FUObjectArray", 0x1A0, ("ObjObjects", 0x10), ("MasterSerialNumber", 0x198));
            CheckType(pdb, "FFixedUObjectArray", 0x10, ("Objects", 0x0), ("MaxElements", 0x8), ("NumElements", 0xC));
            CheckType(pdb, "FUObjectItem", 0x18, ("Object", 0x0), ("SerialNumber", 0x10));
        }
    }

    // Member sizes come from the type of the member, so the array sizes check that an array length is in bytes.
    [Test]
    public void FindLayouts_RealPdb_PinsMemberSizes()
    {
        using var pdb = PdbFile.Open(PdbPath()).Unwrap();

        var entry = pdb.FindLayouts("FNameEntry").Unwrap().Single();
        var array = pdb.FindLayouts("FUObjectArray").Unwrap().Single();
        var item = pdb.FindLayouts("FUObjectItem").Unwrap().Single();
        var name = pdb.FindLayouts("UObjectBase").Unwrap().Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.Member("AnsiName")!.Size, Is.EqualTo(0x400));
            Assert.That(entry.Member("WideName")!.Size, Is.EqualTo(0x800));
            Assert.That(array.Member("ObjObjects")!.Size, Is.EqualTo(0x10));
            Assert.That(array.Member("MasterSerialNumber")!.Size, Is.EqualTo(0x4));
            Assert.That(item.Member("Object")!.Size, Is.EqualTo(0x8));
            Assert.That(item.Member("Flags")!.Size, Is.EqualTo(0x4));
            Assert.That(name.Member("NamePrivate")!.Size, Is.EqualTo(0x8));
            Assert.That(name.Member("ObjectFlags")!.Size, Is.EqualTo(0x4));
        }
    }

    // The own member counts of the largest types fit what DIA lists for them, so a field list didn't lose members.
    [Test]
    public void FindLayouts_RealPdb_PinsOwnMemberCounts()
    {
        using var pdb = PdbFile.Open(PdbPath()).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            CheckOwnMembers(pdb, "UWorld", 0x9A0, 121);
            CheckOwnMembers(pdb, "UClass", 0x1F8, 19);
            CheckOwnMembers(pdb, "AFixtureData", 0x4F0, 29);
            CheckOwnMembers(pdb, "ULevel", 0x330, 56);
        }
    }

    // UClass has a 31-bit and a 1-bit field in 1 storage word.
    [Test]
    public void FindLayouts_RealPdb_ReportsBitFieldBits()
    {
        using var pdb = PdbFile.Open(PdbPath()).Unwrap();

        var layout = pdb.FindLayouts("UClass").Unwrap().Single();
        var unique = layout.Member("ClassUnique")!;
        var cooked = layout.Member("bCooked")!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(unique.Offset, Is.EqualTo(0xB0));
            Assert.That(unique.Size, Is.EqualTo(4));
            Assert.That(unique.BitPosition, Is.Zero);
            Assert.That(unique.BitCount, Is.EqualTo(31));
            Assert.That(cooked.Offset, Is.EqualTo(0xB0));
            Assert.That(cooked.BitPosition, Is.EqualTo(31));
            Assert.That(cooked.BitCount, Is.EqualTo(1));
        }
    }

    // Compile units with another CRT header complete _iobuf and threadlocaleinfostruct differently, so both layouts come back.
    [Test]
    public void FindLayouts_RealPdb_ReturnsEveryDistinctLayout()
    {
        using var pdb = PdbFile.Open(PdbPath()).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pdb.FindLayouts("_iobuf").Unwrap().Select(static layout => layout.Size), Is.EquivalentTo(_ioBufferSizes));
            Assert.That(pdb.FindLayouts("threadlocaleinfostruct").Unwrap().Select(static layout => layout.Size), Is.EquivalentTo(_localeSizes));
        }
    }

    // The fixture of this player only forward declares UEngine and never mentions the other 2.
    [Test]
    public void FindLayouts_RealPdb_FailsForwardOnlyAndAbsent()
    {
        using var pdb = PdbFile.Open(PdbPath()).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pdb.FindLayouts("UEngine").UnwrapErr().Message, Is.EqualTo(SR.FormatPdbTypeForwardOnly("UEngine")));
            Assert.That(pdb.FindLayouts("UGameEngine").UnwrapErr().Message, Is.EqualTo(SR.FormatPdbTypeNotFound("UGameEngine")));
            Assert.That(
                pdb.FindLayouts("FChunkedFixedUObjectArray").UnwrapErr().Message,
                Is.EqualTo(SR.FormatPdbTypeNotFound("FChunkedFixedUObjectArray")));
        }
    }

    // A template and a nested type read under the exact spelling of the PDB.
    [Test]
    public void FindLayouts_RealPdb_FindsTemplateAndNestedNames()
    {
        using var pdb = PdbFile.Open(PdbPath()).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                pdb.FindLayouts("TArray<FUObjectArray::FUObjectCreateListener *,FDefaultAllocator>").Unwrap().Single().Size,
                Is.EqualTo(0x10));
            Assert.That(pdb.FindLayouts("FUObjectArray::FUObjectCreateListener").Unwrap().Single().Size, Is.EqualTo(0x8));
            Assert.That(pdb.FindLayouts("::FUObjectArray.FUObjectCreateListener").Unwrap().Single().Size, Is.EqualTo(0x8));
        }
    }

    // The globals resolve through publics in both spellings and FName::GetNames resolves as a function.
    [Test]
    public void GlobalRva_RealPdb_PinsGlobalsAndFunction()
    {
        using var pdb = PdbFile.Open(PdbPath()).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pdb.GlobalRva("GUObjectArray").Unwrap(), Is.EqualTo(0x2ABE500));
            Assert.That(pdb.GlobalRva("?GUObjectArray@@3VFUObjectArray@@A").Unwrap(), Is.EqualTo(0x2ABE500));
            Assert.That(pdb.GlobalRva("GWorld").Unwrap(), Is.EqualTo(0x2AC7F58));
            Assert.That(pdb.GlobalRva("?GWorld@@3VUWorldProxy@@A").Unwrap(), Is.EqualTo(0x2AC7F58));
            Assert.That(pdb.GlobalRva("GEngine").Unwrap(), Is.EqualTo(0x2AD6E78));
            Assert.That(pdb.GlobalRva("?GEngine@@3PEAVUEngine@@EA").Unwrap(), Is.EqualTo(0x2AD6E78));
            Assert.That(pdb.FunctionRva("FName::GetNames").Unwrap(), Is.EqualTo(0x223760));
            Assert.That(pdb.GlobalRva("FName::GetNames").UnwrapErr().Message, Is.EqualTo(SR.FormatPdbSymbolNotFound("FName::GetNames")));
        }
    }

    private static string PdbPath()
    {
        string? path = Environment.GetEnvironmentVariable(PdbVariable);
        Assume.That(path is not null && File.Exists(path), $"Set {PdbVariable} to the PDB of the 4.19 x64 Shipping player to run this test.");

        return path!;
    }

    private static void CheckType(PdbFile pdb, string type, uint size, params (string Member, uint Offset)[] members)
    {
        var layout = pdb.FindLayouts(type).Unwrap().Single();

        Assert.That(layout.Size, Is.EqualTo(size), type);

        foreach ((string member, uint offset) in members)
        {
            Assert.That(layout.Member(member)?.Offset, Is.EqualTo(offset), $"{type}.{member}");
        }
    }

    private static void CheckOwnMembers(PdbFile pdb, string type, uint size, int count)
    {
        var layout = pdb.FindLayouts(type).Unwrap().Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(layout.Size, Is.EqualTo(size), type);
            Assert.That(layout.Members.Count(member => member.DeclaredBy == type), Is.EqualTo(count), type);
        }
    }
}
