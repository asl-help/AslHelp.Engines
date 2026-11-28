using AslHelp.Engines.Unreal.Errors;
using AslHelp.Engines.Unreal.Names;
using AslHelp.Engines.Unreal.Structs;
using AslHelp.Memory;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Represents the kind of a name, which reads as the text of its id in the name table with its number, like <c>Actor_4</c>.
/// </summary>
internal sealed class NameKind : LayoutKind<NameValueLayout>
{
    private const string ClassName = "NameProperty";

    /// <inheritdoc/>
    public override IReadOnlyList<string> ClassNames { get; } = [ClassName];

    /// <inheritdoc/>
    public override TResult Accept<TResult>(PropertyVisitor<TResult> visitor, UnrealProperty property)
    {
        return visitor.VisitValue<string>(property);
    }

    /// <summary>
    ///     Reads the offsets of the id and the number inside a name.
    /// </summary>
    /// <remarks>
    ///     Reads the id under the member name of the engine of the profile, like <c>ComparisonIndex</c>.
    /// </remarks>
    /// <param name="profile">The offsets of the document.</param>
    /// <returns>
    ///     The layout or a failure with the missing offsets.
    /// </returns>
    public override Result<NameValueLayout> Read(KindProfile profile)
    {
        var offsets = profile.For(ClassName);
        int index = offsets.Read("FName", profile.NameIndexMember);
        int number = offsets.Read("FName", "Number");

        return offsets.Finish(new NameValueLayout(index, number));
    }

    /// <inheritdoc/>
    protected override Result<PropertyAccess> Bind(UnrealProperty property, ClassReader classReader, NameValueLayout layout)
    {
        return new NameAccess(property, layout, classReader.NameTable);
    }
}

file sealed class NameAccess(
    UnrealProperty property,
    NameValueLayout layout,
    INameTable nameTable) : ValueAccess<string>(property)
{
    private const int MaxKeptTexts = 1024;

    private readonly NameValueLayout _layout = layout;
    private readonly INameTable _nameTable = nameTable;

    // Keep the text of each id and number, because the name table read allocates the text and a name array or key reads many names.
    private readonly Dictionary<long, string> _texts = [];

    public override Result<string> Read(IMemoryReader memory, Address container, string? last)
    {
        var address = container + Property.Offset;
        var idAddress = address + _layout.Index;

        if (!memory.Read<int>(idAddress).TryUnwrap(out int id, out var error))
        {
            return ValueError.Unreadable(container, Property.Name, idAddress, error);
        }

        var numberAddress = address + _layout.Number;

        if (!memory.Read<int>(numberAddress).TryUnwrap(out int number, out error))
        {
            return ValueError.Unreadable(container, Property.Name, numberAddress, error);
        }

        if (number < 0)
        {
            return PropertyError.NameNumberNegative(address, number);
        }

        long key = ((long)id << 32) | (uint)number;

        if (_texts.TryGetValue(key, out string? kept))
        {
            return kept;
        }

        if (!_nameTable.Read(id).TryUnwrap(out string? text, out error))
        {
            return error;
        }

        // Clear the kept texts at the bound, because a long-running game keeps creating names.
        if (_texts.Count >= MaxKeptTexts)
        {
            _texts.Clear();
        }

        string name = NameMembers.Join(text, number);
        _texts.Add(key, name);

        return name;
    }
}
