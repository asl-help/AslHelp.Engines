using AslHelp.Engines.Unreal.Paths;
using AslHelp.Engines.Unreal.Structs;

namespace AslHelp.Engines.Unreal;

/// <summary>
///     Represents an object that paths and member chains start at, like a subsystem, found again on each read.
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

        // Create the hop of the root once, because the path and the chain start at the same root.
        var hop = MemberHop.Root(text);
        Path = new UnrealPath(new MemberPath(classReader, root, [hop]));
        Chain = new MemberChain<MemberHop>(new MemberResolver(classReader, root), hop);
    }

    /// <summary>
    ///     Gets the member chain that starts at the object, for a script that writes members through <see langword="dynamic"/>.
    /// </summary>
    /// <remarks>
    ///     <see cref="UnrealGame.GWorldChain"/> lists what a chain takes.
    /// </remarks>
    public dynamic Chain { get; }

    /// <summary>
    ///     Gets the path that starts at the object.
    /// </summary>
    /// <remarks>
    ///     Getting the path doesn't read memory.
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
