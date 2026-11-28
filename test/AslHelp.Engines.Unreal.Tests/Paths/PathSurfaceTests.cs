using AslHelp.Engines.Unreal.Paths;
using NUnit.Framework;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace AslHelp.Engines.Unreal.Tests.Paths;

public sealed class PathSurfaceTests
{
    // Values reach a script only through watchers, so a path doesn't have a public read, a value property or a conversion.
    // Value<T> only gives the source of a typed watcher, whose type isn't public.
    [Test]
    public void PublicMembers_Paths_DoNotReadValues()
    {
        var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        string[] names = [.. typeof(UnrealPath).GetMembers(flags).Select(static member => member.Name)];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(names, Has.None.AnyOf("Read", "Current", "Old", "op_Implicit", "op_Explicit"));
            Assert.That(typeof(UnrealValuePath<int>).IsPublic, Is.False);
        }
    }

    // Dynamic member access belongs to the plugins, so neither library has a dynamic object, a dynamic member or Microsoft.CSharp.
    [Test]
    public void Assemblies_Libraries_DoNotUseDynamic()
    {
        Assembly[] assemblies = [typeof(IMemberPath).Assembly, typeof(UnrealPath).Assembly];
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var types = assemblies.SelectMany(static assembly => assembly.GetTypes()).ToArray();

        string[] dynamicTypes =
        [
            .. types.Where(static type => typeof(IDynamicMetaObjectProvider).IsAssignableFrom(type)).Select(static type => type.FullName!),
        ];
        string[] dynamicMembers =
        [
            .. types.SelectMany(type => type.GetMembers(flags))
                .Where(static member => DynamicTarget(member)?.IsDefined(typeof(DynamicAttribute), false) == true)
                .Select(static member => $"{member.DeclaringType}.{member.Name}"),
        ];
        string[] references = [.. assemblies.SelectMany(static assembly => assembly.GetReferencedAssemblies()).Select(static name => name.Name!)];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dynamicTypes, Is.Empty);
            Assert.That(dynamicMembers, Is.Empty);
            Assert.That(references, Has.No.Member("Microsoft.CSharp"));
        }
    }

    // Return the part of a member that gets DynamicAttribute when its type is dynamic.
    private static ICustomAttributeProvider? DynamicTarget(MemberInfo member)
    {
        return member switch
        {
            PropertyInfo property => property,
            FieldInfo field => field,
            MethodInfo method => method.ReturnParameter,
            _ => null,
        };
    }
}
