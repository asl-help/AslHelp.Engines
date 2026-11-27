using AslHelp.Engines.Unreal.Structs;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Represents 1 kind of property and creates the reads of its values.
/// </summary>
/// <remarks>
///     A kind doesn't keep state, so 1 instance serves every property of its classes.
///     A new kind is 1 more subclass and 1 more line in <see cref="PropertyKinds"/>.
///     A kind that reads offsets from the profile derives from <see cref="LayoutKind{TLayout}"/>.
/// </remarks>
internal abstract class PropertyKind
{
    /// <summary>
    ///     Gets the property classes of this kind without a prefix, like <c>IntProperty</c>.
    /// </summary>
    public abstract IReadOnlyList<string> ClassNames { get; }

    /// <summary>
    ///     Gets a value indicating whether a value of this kind is 1 number at the offset of the property, which an enum can be.
    /// </summary>
    public virtual bool IsNumber => false;

    /// <summary>
    ///     Calls the method of <paramref name="visitor"/> for this kind.
    /// </summary>
    /// <typeparam name="TResult">The type of the result of a visit.</typeparam>
    /// <param name="visitor">The visitor.</param>
    /// <param name="property">The property to visit.</param>
    /// <returns>
    ///     The result of the visit.
    /// </returns>
    public abstract TResult Accept<TResult>(PropertyVisitor<TResult> visitor, UnrealProperty property);

    /// <summary>
    ///     Creates the access that reads the values of <paramref name="property"/>.
    /// </summary>
    /// <remarks>
    ///     Reads the members of the property that the access needs, like the mask of a bool.
    ///     Returns an access that fails every read when the profile doesn't have a part that the kind needs.
    /// </remarks>
    /// <param name="property">The property, whose name, class and offset the access uses.</param>
    /// <param name="classReader">The reader of the game, with its memory, name table and kind layouts.</param>
    /// <returns>
    ///     The access or a failure when a member of the property doesn't read, so the next call reads it again.
    /// </returns>
    public abstract Result<PropertyAccess> Bind(UnrealProperty property, ClassReader classReader);

    /// <summary>
    ///     Reads the offsets of this kind from the profile document at parse.
    /// </summary>
    /// <param name="profile">The offsets of the document.</param>
    /// <returns>
    ///     The layout, a failure with the missing paths or <see langword="null"/> for a kind that doesn't read offsets.
    /// </returns>
    public virtual Result<object>? ReadLayout(KindProfile profile)
    {
        return null;
    }
}
