using AslHelp.Engines.Unreal.Paths;
using NUnit.Framework;
using System;
using System.Linq;
using System.Reflection;

namespace AslHelp.Engines.Unreal.Tests.Paths;

public sealed class PathSurfaceTests
{
    // Values reach a script only through watchers, so a path doesn't have a public read, a value member or a conversion.
    [Test]
    public void PublicMembers_Paths_DoNotReadValues()
    {
        Type[] pathTypes = [typeof(UnrealPath), typeof(UnrealValuePath<int>), typeof(UnrealValuePath<string>)];
        var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        string[] names = [.. pathTypes.SelectMany(type => type.GetMembers(flags)).Select(static member => member.Name)];

        Assert.That(names, Has.None.AnyOf("Read", "Current", "Old", "Value", "op_Implicit", "op_Explicit"));
    }
}
