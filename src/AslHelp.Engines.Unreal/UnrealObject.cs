using AslHelp.Engines.Unreal.Paths;
using AslHelp.Engines.Unreal.Structs;
using AslHelp.Memory;

namespace AslHelp.Engines.Unreal;

/// <summary>
///     Represents a live object of a class, which paths can start at.
/// </summary>
/// <remarks>
///     A path from the object reads its class pointer first and fails once the address has an object of another class.
///     Isn't thread-safe.
/// </remarks>
public sealed class UnrealObject
{
    private readonly ClassReader _classReader;

    /// <summary>
    ///     Initializes a new instance of the <see cref="UnrealObject"/> class.
    /// </summary>
    /// <param name="classReader">The reader of the game.</param>
    /// <param name="address">The address of the object.</param>
    /// <param name="klass">The class of the object.</param>
    /// <param name="name">The name of the object.</param>
    internal UnrealObject(ClassReader classReader, Address address, UnrealClass klass, string name)
    {
        _classReader = classReader;
        Address = address;
        Class = klass;
        Name = name;
    }

    /// <summary>
    ///     Gets the address of the object.
    /// </summary>
    public Address Address { get; }

    /// <summary>
    ///     Gets the class of the object.
    /// </summary>
    public UnrealClass Class { get; }

    /// <summary>
    ///     Gets the name of the object, like <c>FixtureData_0</c>.
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     Gets the path that starts at the object, like <c>obj.Path.Member("Hp")</c>.
    /// </summary>
    /// <remarks>
    ///     The text of the path starts with the name of the class.
    /// </remarks>
    public UnrealPath Path => Root.Path;

    private UnrealRoot Root => field ??= new UnrealRoot(_classReader, PathRoot.Object(_classReader, this), Class.Name);

    /// <summary>
    ///     Returns the name of the object.
    /// </summary>
    /// <returns>
    ///     The name.
    /// </returns>
    public override string ToString()
    {
        return Name;
    }
}
