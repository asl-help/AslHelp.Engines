using AslHelp.Engines.Measuring.Cli.Unreal4;
using AslHelp.Engines.Measuring.Cli.Unreal4.Errors;
using NUnit.Framework;

namespace AslHelp.Engines.Measuring.Cli.Tests.Unreal4;

public sealed class UnrealErrorTests
{
    // Every distinct value of a type shows in hex, so a person can compare them with the PDB.
    [Test]
    public void LayoutsDiffer_TwoValues_ListsBoth()
    {
        Assert.That(UnrealLayoutError.LayoutsDiffer("Type", "Member", [0x8, 0x10]).Message, Does.Contain("Type.Member").And.Contain("0x8, 0x10"));
    }
}
