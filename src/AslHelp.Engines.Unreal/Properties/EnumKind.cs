using AslHelp.Engines.Unreal.Errors;
using AslHelp.Engines.Unreal.Structs;
using AslHelp.Memory;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Represents the kind of an enum, which reads as the number of its underlying property, like a byte for <c>enum class : uint8</c>.
/// </summary>
internal sealed class EnumKind : LayoutKind<int>
{
    private const string ClassName = "EnumProperty";

    /// <inheritdoc/>
    public override IReadOnlyList<string> ClassNames { get; } = [ClassName];

    /// <inheritdoc/>
    public override TResult Accept<TResult>(PropertyVisitor<TResult> visitor, UnrealProperty property)
    {
        return visitor.VisitEnum(property);
    }

    /// <summary>
    ///     Reads the offset of the pointer to the underlying property.
    /// </summary>
    /// <remarks>
    ///     Reads it under the unreal4 name in an unreal3 document too, because Unreal Engine 3 doesn't have <c>EnumProperty</c>
    ///     and only a read of one would fail.
    /// </remarks>
    /// <param name="profile">The offsets of the document.</param>
    /// <returns>
    ///     The offset or a failure with its path.
    /// </returns>
    public override Result<int> Read(KindProfile profile)
    {
        var offsets = profile.For(ClassName);

        return offsets.Finish(offsets.ReadProperty(ClassName, "UnderlyingProp"));
    }

    /// <summary>
    ///     Creates the access that reads the value of <paramref name="property"/> as the number of its underlying property.
    /// </summary>
    /// <remarks>
    ///     Binds the kind of the underlying property to the enum property, because the value sits at the offset of the enum
    ///     and a message shows the enum.
    /// </remarks>
    /// <param name="property">The property.</param>
    /// <param name="classReader">The reader of the game.</param>
    /// <param name="layout">The offset of the pointer to the underlying property.</param>
    /// <returns>
    ///     The access or a failure when the underlying property doesn't read.
    /// </returns>
    protected override Result<PropertyAccess> Bind(UnrealProperty property, ClassReader classReader, int layout)
    {
        var address = property.Address + layout;

        if (!classReader.Memory.ReadPointer(address).TryUnwrap(out var underlying, out var error))
        {
            return ValueError.EnumUnderlyingUnreadable(property.Name, address, error);
        }

        if (underlying == Address.Zero)
        {
            return new FailedAccess(property, ValueError.EnumUnderlyingNull(property.Name, property.Address, layout));
        }

        if (!classReader.ReadProperty(underlying).TryUnwrap(out var underlyingProperty, out error))
        {
            return error;
        }

        // Fail any other kind than a number, because binding it would read members that the enum property doesn't have
        // and an enum under an enum would read the same pointer forever.
        return underlyingProperty.Kind.IsNumber
            ? underlyingProperty.Kind.Bind(property, classReader)
            : new FailedAccess(property, ValueError.EnumUnderlyingNotNumber(property.Name, underlyingProperty.ClassName, layout));
    }
}
