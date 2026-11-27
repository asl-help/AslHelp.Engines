using AslHelp.Engines.Unreal.Structs;

namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Represents a kind that reads offsets from the profile document at parse, like the mask members of a bool.
/// </summary>
/// <remarks>
///     A kind lists its offsets in <see cref="ReadLayout(KindProfile)"/>, so a new kind doesn't change the mappers of the profile.
/// </remarks>
/// <typeparam name="TLayout">The type of the offsets the kind reads.</typeparam>
internal abstract class LayoutKind<TLayout> : PropertyKind
    where TLayout : notnull
{
    /// <summary>
    ///     Reads the offsets of this kind from <paramref name="profile"/>.
    /// </summary>
    /// <param name="profile">The offsets of the document.</param>
    /// <returns>
    ///     The layout or a failure with the class of the kind and the path of every missing offset.
    /// </returns>
    public abstract Result<TLayout> Read(KindProfile profile);

    /// <inheritdoc/>
    public sealed override Result<object>? ReadLayout(KindProfile profile)
    {
        return Read(profile).Map(static layout => (object)layout);
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     Returns an access that fails every read when the profile doesn't have the offsets of this kind.
    /// </remarks>
    public sealed override Result<PropertyAccess> Bind(UnrealProperty property, ClassReader classReader)
    {
        return classReader.KindLayouts.Find(this).TryUnwrap(out var layout, out var error)
            ? Bind(property, classReader, layout)
            : new FailedAccess(property, error);
    }

    /// <summary>
    ///     Creates the access that reads the values of <paramref name="property"/> with the offsets of this kind.
    /// </summary>
    /// <param name="property">The property.</param>
    /// <param name="classReader">The reader of the game.</param>
    /// <param name="layout">The offsets of this kind.</param>
    /// <returns>
    ///     The access or a failure when a member of the property doesn't read, so the next call reads it again.
    /// </returns>
    protected abstract Result<PropertyAccess> Bind(UnrealProperty property, ClassReader classReader, TLayout layout);
}
