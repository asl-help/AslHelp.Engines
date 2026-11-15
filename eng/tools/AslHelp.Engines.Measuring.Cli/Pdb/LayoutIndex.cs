using AslHelp.Engines.Measuring.Cli.Pdb.Errors;
using AsmResolver.Symbols.Pdb.Leaves;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AslHelp.Engines.Measuring.Cli.Pdb;

/// <summary>
///     Finds the layouts of the classes, structs and unions of a PDB by name.
/// </summary>
internal sealed class LayoutIndex
{
    private readonly Dictionary<string, List<CodeViewCompositeTypeRecord>> _complete;
    private readonly HashSet<string> _forward;
    private readonly Func<FieldListLeaf, FieldListLeaf?> _nextFieldList;

    private LayoutIndex(
        Dictionary<string, List<CodeViewCompositeTypeRecord>> complete,
        HashSet<string> forward,
        Func<FieldListLeaf, FieldListLeaf?> nextFieldList)
    {
        _complete = complete;
        _forward = forward;
        _nextFieldList = nextFieldList;
    }

    /// <summary>
    ///     Builds the index with 1 pass over the records of the type stream.
    /// </summary>
    /// <param name="leaves">The records of the type stream.</param>
    /// <param name="nextFieldList">
    ///     Gets the list that continues a field list past the record limit or <see langword="null"/> if there isn't one.
    /// </param>
    /// <returns>
    ///     The index.
    /// </returns>
    public static LayoutIndex Build(IEnumerable<ITpiLeaf> leaves, Func<FieldListLeaf, FieldListLeaf?> nextFieldList)
    {
        Dictionary<string, List<CodeViewCompositeTypeRecord>> complete = [with(StringComparer.Ordinal)];
        HashSet<string> forward = [with(StringComparer.Ordinal)];

        foreach (var leaf in leaves)
        {
            if (leaf is EnumTypeRecord || leaf is not CodeViewCompositeTypeRecord record)
            {
                continue;
            }

            string name = record.Name!;

            if (record.StructureAttributes.HasFlag(StructureAttributes.FwdRef))
            {
                _ = forward.Add(name);
            }
            else if (complete.TryGetValue(name, out var records))
            {
                records.Add(record);
            }
            else
            {
                complete.Add(name, [record]);
            }
        }

        return new LayoutIndex(complete, forward, nextFieldList);
    }

    /// <summary>
    ///     Finds every distinct layout of a type.
    /// </summary>
    /// <param name="typeName">
    ///     The name as the PDB spells it, such as <c>List&lt;Item *,Default&gt;</c>.
    ///     A leading <c>::</c> and <c>.</c> between scopes also find a name spelled with <c>::</c>.
    /// </param>
    /// <returns>
    ///     The layouts in record order or a failure with the name when the PDB only has forward references, doesn't have the type
    ///     or the records of the type or a base class don't read.
    ///     A type has several layouts when compile units complete it differently, such as with other packing, so the caller picks.
    /// </returns>
    public Result<IReadOnlyList<TypeLayout>> Find(string typeName)
    {
        string name = typeName.StartsWith("::", StringComparison.Ordinal) ? typeName[2..] : typeName;

        if (!_complete.TryGetValue(name, out var records))
        {
            string scoped = name.Replace(".", "::", StringComparison.Ordinal);

            if (!_complete.TryGetValue(scoped, out records))
            {
                return _forward.Contains(name) || _forward.Contains(scoped) ? PdbError.TypeForwardOnly(typeName) : PdbError.TypeNotFound(typeName);
            }
        }

        try
        {
            return Result.Ok<IReadOnlyList<TypeLayout>>(Layouts(records, []));
        }
        catch (Exception exception) when (PdbFile.IsBadData(exception))
        {
            return PdbError.TypeUnreadable(typeName, exception.Message);
        }
    }

    private List<TypeLayout> Layouts(IEnumerable<CodeViewCompositeTypeRecord> records, HashSet<CodeViewCompositeTypeRecord> folding)
    {
        return [.. records.SelectMany(record => Fold(record, folding)).Distinct()];
    }

    /// <summary>
    ///     Folds the own members and the base class members of a record.
    /// </summary>
    /// <param name="record">The complete record.</param>
    /// <param name="folding">The records between the type of the lookup and this record.</param>
    /// <returns>
    ///     1 layout for each combination of base class layouts, so a base class with 2 layouts gives 2 layouts.
    /// </returns>
    private IEnumerable<TypeLayout> Fold(CodeViewCompositeTypeRecord record, HashSet<CodeViewCompositeTypeRecord> folding)
    {
        string name = record.Name!;

        // Refuse a cycle, because 2 compile units can derive 2 types from each other and the recursion would end the process.
        if (!folding.Add(record))
        {
            throw new BadImageFormatException(SR.FormatPdbBaseClassCycle(name));
        }

        List<TypeMember> own = [];
        List<BaseClassField> bases = [];
        HashSet<FieldListLeaf> lists = [];

        for (var list = record.Fields; list is not null; list = _nextFieldList(list))
        {
            // Refuse a list that comes back, because this loop would only end when memory runs out.
            if (!lists.Add(list))
            {
                throw new BadImageFormatException(SR.FormatPdbFieldListCycle(name));
            }

            foreach (var field in list.Entries)
            {
                // Leave static members and virtual bases out, because they don't have a fixed offset in the layout.
                switch (field)
                {
                    case InstanceDataField data:
                    {
                        own.Add(ToMember(data, name));
                        break;
                    }
                    case BaseClassField baseClass:
                    {
                        bases.Add(baseClass);
                        break;
                    }
                }
            }
        }

        IEnumerable<IReadOnlyList<TypeMember>> combinations = [own];

        foreach (var baseClass in bases)
        {
            uint offset = (uint)baseClass.Offset;
            var baseLayouts = Layouts(CompleteRecords(baseClass.BaseType!), folding);

            combinations = [.. combinations.SelectMany(members => baseLayouts.Select(baseLayout => Join(members, baseLayout.Members, offset)))];
        }

        _ = folding.Remove(record);

        uint size = TypeSizes.OfComplete(record);

        return combinations.Select(members => new TypeLayout(name, size, members));
    }

    private List<CodeViewCompositeTypeRecord> CompleteRecords(CodeViewTypeRecord baseType)
    {
        // Refuse an enum too, because it's a composite record without members or a class size.
        if (baseType is EnumTypeRecord || baseType is not CodeViewCompositeTypeRecord record)
        {
            throw new BadImageFormatException(SR.FormatPdbBaseClassNotClass(baseType.LeafKind));
        }

        if (!record.StructureAttributes.HasFlag(StructureAttributes.FwdRef))
        {
            return [record];
        }

        // Throw the same exception as a bad record, because a missing base class would lose members.
        return _complete.TryGetValue(record.Name!, out var records)
            ? records
            : throw new BadImageFormatException(SR.FormatPdbBaseClassIncomplete(record.Name));
    }

    private TypeMember ToMember(InstanceDataField field, string declaredBy)
    {
        var bits = field.DataType as BitFieldTypeRecord;

        return new TypeMember(field.Name!, (uint)field.Offset, TypeSizes.Of(field.DataType, _complete), bits?.Position, bits?.Length, declaredBy);
    }

    private static List<TypeMember> Join(IReadOnlyList<TypeMember> members, IReadOnlyList<TypeMember> baseMembers, uint offset)
    {
        return [.. members, .. baseMembers.Select(member => member with { Offset = member.Offset + offset })];
    }
}
