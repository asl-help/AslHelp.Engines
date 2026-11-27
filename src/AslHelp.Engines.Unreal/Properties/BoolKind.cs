using AslHelp.Engines.Unreal.Structs;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Represents the kind of a bool, which reads with the mask of its property in the bool shape of the profile.
/// </summary>
internal sealed class BoolKind : LayoutKind<BoolShape>
{
    private const string ClassName = "BoolProperty";

    /// <inheritdoc/>
    public override IReadOnlyList<string> ClassNames { get; } = [ClassName];

    /// <inheritdoc/>
    public override TResult Accept<TResult>(PropertyVisitor<TResult> visitor, UnrealProperty property)
    {
        return visitor.VisitValue<bool>(property);
    }

    /// <summary>
    ///     Reads the 4-byte mask or the byte offset and field mask, whichever the engine of the profile uses.
    /// </summary>
    /// <param name="profile">The offsets of the document.</param>
    /// <returns>
    ///     The bool shape or a failure with the missing offsets.
    /// </returns>
    public override Result<BoolShape> Read(KindProfile profile)
    {
        var offsets = profile.For(ClassName);

        if (profile.BitMaskBools)
        {
            return offsets.Finish(BoolShape.BitMask(offsets.ReadProperty(ClassName, "BitMask")));
        }

        int byteOffset = offsets.ReadProperty(ClassName, "ByteOffset");
        int fieldMask = offsets.ReadProperty(ClassName, "FieldMask");

        return offsets.Finish(BoolShape.ByteMask(byteOffset, fieldMask));
    }

    /// <inheritdoc/>
    protected override Result<PropertyAccess> Bind(UnrealProperty property, ClassReader classReader, BoolShape layout)
    {
        return layout.Bind(classReader.Memory, property);
    }
}
