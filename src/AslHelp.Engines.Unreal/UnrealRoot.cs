using AslHelp.Engines.Unreal.Paths;
using AslHelp.Engines.Unreal.Structs;

namespace AslHelp.Engines.Unreal;

/// <summary>
///     Represents an object that paths start at, like a global or a subsystem. Each read finds the object again.
/// </summary>
/// <remarks>
///     A root whose object isn't there fails each read and recovers once the object is there.
///     Isn't thread-safe.
/// </remarks>
public sealed class UnrealRoot
{
    private readonly string _text;

    /// <summary>
    ///     Initializes a new instance of the <see cref="UnrealRoot"/> class.
    /// </summary>
    /// <param name="classReader">The reader of the game.</param>
    /// <param name="root">The object that the paths start at.</param>
    /// <param name="text">The text of the root in a path, like <c>GWorld</c> or the name of a class.</param>
    internal UnrealRoot(ClassReader classReader, PathRoot root, string text)
    {
        _text = text;
        Path = new UnrealPath(new MemberPath(classReader, root, [MemberHop.Root(text)]));
    }

    /// <summary>
    ///     Gets the path that starts at the object.
    /// </summary>
    /// <remarks>
    ///     Getting the path doesn't read memory. Its watcher reads the address of the object.
    /// </remarks>
    public UnrealPath Path { get; }

    /// <summary>
    ///     Returns the text of the root in a path.
    /// </summary>
    /// <returns>
    ///     The text, like <c>GWorld</c> or the name of a class.
    /// </returns>
    public override string ToString()
    {
        return _text;
    }
}
