using AslHelp.Engines.Measuring.Cli.Pdb;
using AslHelp.Engines.Measuring.Cli.Routes;
using AslHelp.Engines.Measuring.Cli.Tests.Pdb;
using AslHelp.Engines.Measuring.Cli.Tests.Routes;
using AslHelp.Engines.Measuring.Cli.Unreal4;
using NUnit.Framework;
using System;
using System.IO;
using System.Threading;

namespace AslHelp.Engines.Measuring.Cli.Tests.Unreal4;

public sealed class UnrealStaticsTests
{
    private const uint Constructor = CodeBuilder.CodeRva;
    private const uint DataRva = 0x2000;
    private const string DecoratedX64 = "??0Table@@QEAA@XZ";
    private const string DecoratedX86 = "??0Table@@QAE@XZ";
    private const uint OtherDataRva = 0x2010;
    private const uint SecondCodeRva = 0x3000;
    private const string Type = "Table";

    private static readonly Guid _guid = new("d43d3384-a767-42b9-8883-d83e5ab17338");
    private static readonly uint[] _sections = [CodeBuilder.CodeRva, DataRva, SecondCodeRva];

    private TestFolder _folder = null!;

    [SetUp]
    public void SetUp()
    {
        _folder = new TestFolder();
    }

    [TearDown]
    public void TearDown()
    {
        _folder.Delete();
    }

    // x64 code loads the address of the static with lea right before the call, as the 4.19 players do.
    [Test]
    public void FindThroughConstructor_CallOnX64_ReturnsTheStatic()
    {
        var code = new CodeBuilder(true).Ret().LeaRcx(DataRva).CallTo(Constructor);

        Assert.That(Find(code.Build()).Unwrap(), Is.EqualTo(DataRva));
    }

    // x86 code moves the address of the static into ecx as a number, as the 4.19 players do.
    [Test]
    public void FindThroughConstructor_CallOnX86_ReturnsTheStatic()
    {
        var code = new CodeBuilder(false).Ret().MovEcxAddress(DataRva).CallTo(Constructor);

        Assert.That(Find(code.Build(), DecoratedX86).Unwrap(), Is.EqualTo(DataRva));
    }

    // A caller can keep the object in another register and move it over before the call.
    [Test]
    public void FindThroughConstructor_ThroughAnotherRegister_ReturnsTheStatic()
    {
        var code = new CodeBuilder(true).Ret().Raw(0x48, 0x8D, 0x05).Relative(DataRva).Raw(0x48, 0x89, 0xC1).CallTo(Constructor);

        Assert.That(Find(code.Build()).Unwrap(), Is.EqualTo(DataRva));
    }

    // Instructions that leave the register alone don't count, also a test of it and a conditional jump.
    [Test]
    public void FindThroughConstructor_OtherInstructionsBeforeTheCall_ReturnsTheStatic()
    {
        var code = new CodeBuilder(true)
            .Ret()
            .LeaRcx(DataRva)
            .Raw(0x31, 0xD2)
            .Raw(0x48, 0x85, 0xC9)
            .Raw(0x74, 0x00)
            .CallTo(Constructor);

        Assert.That(Find(code.Build()).Unwrap(), Is.EqualTo(DataRva));
    }

    // Several calls that pass 1 static are 1 result, because an inlined getter constructs its static at each site.
    [Test]
    public void FindThroughConstructor_TwoCallsOfOneStatic_ReturnsIt()
    {
        var code = new CodeBuilder(true).Ret().LeaRcx(DataRva).CallTo(Constructor).LeaRcx(DataRva).CallTo(Constructor);

        Assert.That(Find(code.Build()).Unwrap(), Is.EqualTo(DataRva));
    }

    // 2 calls that pass different statics fail with both, because either one could be the wrong one.
    [Test]
    public void FindThroughConstructor_TwoCallsOfTwoStatics_FailsListingBoth()
    {
        var code = new CodeBuilder(true).Ret().LeaRcx(DataRva).CallTo(Constructor).LeaRcx(OtherDataRva).CallTo(Constructor);

        Assert.That(Find(code.Build()).UnwrapErr().Message, Is.EqualTo(UnrealSR.FormatConstructedStaticAmbiguous(Type, "2000", "2010")));
    }

    // A call on an object on the stack doesn't count, so the call on the static still gets found.
    [Test]
    public void FindThroughConstructor_CallOnAStackObject_SkipsIt()
    {
        var code = new CodeBuilder(true).Ret().Raw(0x48, 0x8D, 0x4C, 0x24, 0x20).CallTo(Constructor).LeaRcx(DataRva).CallTo(Constructor);

        Assert.That(Find(code.Build()).Unwrap(), Is.EqualTo(DataRva));
    }

    // A call in a later code section counts too, with the address from its own section.
    [Test]
    public void FindThroughConstructor_CallInSecondSection_ReturnsTheStatic()
    {
        var first = new CodeBuilder(true).Ret().Section();
        var second = new CodeBuilder(true, rva: SecondCodeRva).LeaRcx(DataRva).CallTo(Constructor).Section();

        Assert.That(Find(new CodeImage(true, CodeBuilder.DefaultBase, CodeBuilder.ImageSize, [first, second])).Unwrap(), Is.EqualTo(DataRva));
    }

    // The search looks at 127 instructions in front of the call, with the load at the last one still found.
    [Test]
    public void FindThroughConstructor_LoadAtTheEndOfTheWindow_ReturnsTheStatic()
    {
        var code = new CodeBuilder(true).Ret().LeaRcx(DataRva).Fillers(126).CallTo(Constructor);

        Assert.That(Find(code.Build()).Unwrap(), Is.EqualTo(DataRva));
    }

    // A load further away belongs to other code by then, so the search doesn't use it.
    [Test]
    public void FindThroughConstructor_LoadPastTheWindow_Fails()
    {
        var code = new CodeBuilder(true).Ret().LeaRcx(DataRva).Fillers(127).CallTo(Constructor);

        AssertNotFound(code.Build());
    }

    // A function without a call of the constructor fails with the type and the RVA.
    [Test]
    public void FindThroughConstructor_NoCall_Fails()
    {
        AssertNotFound(new CodeBuilder(true).Ret().LeaRcx(DataRva).Build());
    }

    // A call of another function doesn't count, also with the static in the register.
    [Test]
    public void FindThroughConstructor_CallOfAnotherFunction_Fails()
    {
        AssertNotFound(new CodeBuilder(true).Ret().Ret().LeaRcx(DataRva).CallTo(Constructor + 1).Build());
    }

    // Only a call on a stack object fails, because it doesn't show where the static is.
    [Test]
    public void FindThroughConstructor_OnlyAStackObject_Fails()
    {
        AssertNotFound(new CodeBuilder(true).Ret().Raw(0x48, 0x8D, 0x4C, 0x24, 0x20).CallTo(Constructor).Build());
    }

    // The value of a static isn't its address, so a pointer loaded out of it doesn't count.
    [Test]
    public void FindThroughConstructor_PointerLoadedFromAStatic_Fails()
    {
        AssertNotFound(new CodeBuilder(true).Ret().Raw(0x48, 0x8B, 0x0D).Relative(DataRva).CallTo(Constructor).Build());
    }

    // An address inside the code isn't a static.
    [Test]
    public void FindThroughConstructor_AddressInsideTheCode_Fails()
    {
        AssertNotFound(new CodeBuilder(true).Ret().LeaRcx(Constructor).CallTo(Constructor).Build());
    }

    // A plain size such as 0x408 isn't an address, because it sits below the image.
    [Test]
    public void FindThroughConstructor_PlainNumberInTheRegister_Fails()
    {
        AssertNotFound(new CodeBuilder(true).Ret().Raw(0xB9, 0x08, 0x04, 0x00, 0x00).CallTo(Constructor).Build());
    }

    // A return, a breakpoint or another call after the load can change the register before the call.
    [TestCase(new byte[] { 0xC3 })]
    [TestCase(new byte[] { 0xCC })]
    [TestCase(new byte[] { 0xE8, 0x00, 0x00, 0x00, 0x00 })]
    public void FindThroughConstructor_BoundaryAfterTheLoad_Fails(byte[] boundary)
    {
        AssertNotFound(new CodeBuilder(true).Ret().LeaRcx(DataRva).Raw(boundary).CallTo(Constructor).Build());
    }

    // A load at the end of 1 section doesn't reach a call at the start of the next one.
    [Test]
    public void FindThroughConstructor_LoadInThePreviousSection_Fails()
    {
        var first = new CodeBuilder(true).Ret().LeaRcx(DataRva).Section();
        var second = new CodeBuilder(true, rva: SecondCodeRva).CallTo(Constructor).Section();

        AssertNotFound(new CodeImage(true, CodeBuilder.DefaultBase, CodeBuilder.ImageSize, [first, second]));
    }

    // Each architecture has its own decorated name, so the name of the other one isn't found and the failure has the name to look for.
    [TestCase(true, DecoratedX86, DecoratedX64)]
    [TestCase(false, DecoratedX64, DecoratedX86)]
    public void FindThroughConstructor_NameOfTheOtherArchitecture_FailsWithTheName(bool is64Bit, string listed, string expected)
    {
        var image = new CodeBuilder(is64Bit).Ret().Build();

        Assert.That(Find(image, listed).UnwrapErr().Message, Is.EqualTo(SR.FormatPdbSymbolNotFound(expected)));
    }

    private void AssertNotFound(CodeImage image)
    {
        Assert.That(Find(image).UnwrapErr().Message, Is.EqualTo(UnrealSR.FormatConstructedStaticNotFound(Type, "1000")));
    }

    private Result<uint> Find(CodeImage image, string decorated = DecoratedX64)
    {
        FakePdb pdb = new(_guid, _sections);
        pdb.AddPublic(decorated, 1, 0, isFunction: true);

        string path = Path.Combine(_folder.Root, "file.pdb");
        pdb.Write(path);

        using var file = PdbFile.Open(path).Unwrap();

        return UnrealStatics.FindThroughConstructor(file, image, Type, CancellationToken.None);
    }
}
