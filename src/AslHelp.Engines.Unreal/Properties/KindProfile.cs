using System;

namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Represents the offsets of a profile document that the value kinds read at parse, with the member names of its engine.
/// </summary>
/// <param name="propertyPrefix">The prefix of the property classes, <c>U</c> for property objects and <c>F</c> for property fields.</param>
/// <param name="nameIndexMember">The member of the name id in <c>FName</c>, like <c>ComparisonIndex</c>.</param>
/// <param name="bitMaskBools">
///     <see langword="true"/> when a bool reads with 1 4-byte mask, <see langword="false"/> when it reads with a byte offset and a field mask.
/// </param>
/// <param name="readOffset">Reads the offset of a type and a member or returns <see langword="null"/> when the document doesn't have it.</param>
internal sealed class KindProfile(
    string propertyPrefix,
    string nameIndexMember,
    bool bitMaskBools,
    Func<string, string, int?> readOffset)
{
    private readonly Func<string, string, int?> _readOffset = readOffset;

    /// <summary>
    ///     Gets a value indicating whether a bool reads with 1 4-byte mask instead of a byte offset and a field mask.
    /// </summary>
    public bool BitMaskBools { get; } = bitMaskBools;

    /// <summary>
    ///     Gets the member of the name id in <c>FName</c>, like <c>Index</c> or <c>ComparisonIndex</c>.
    /// </summary>
    public string NameIndexMember { get; } = nameIndexMember;

    /// <summary>
    ///     Gets the prefix of the property classes, like <c>U</c> in <c>UBoolProperty</c>.
    /// </summary>
    public string PropertyPrefix { get; } = propertyPrefix;

    /// <summary>
    ///     Starts the reads of the offsets of 1 kind.
    /// </summary>
    /// <param name="className">The property class of the kind, like <c>BoolProperty</c>, for the failure.</param>
    /// <returns>
    ///     The reads, which collect the missing offsets.
    /// </returns>
    public KindOffsets For(string className)
    {
        return new KindOffsets(this, className);
    }

    /// <summary>
    ///     Reads the offset of <paramref name="member"/> in <paramref name="type"/>.
    /// </summary>
    /// <param name="type">The type, like <c>FScriptArray</c>.</param>
    /// <param name="member">The member, like <c>Data</c>.</param>
    /// <returns>
    ///     The offset or <see langword="null"/> when the document doesn't have it.
    /// </returns>
    public int? Read(string type, string member)
    {
        return _readOffset(type, member);
    }
}
