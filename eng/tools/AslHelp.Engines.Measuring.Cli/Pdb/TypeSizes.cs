using AsmResolver.Symbols.Pdb.Leaves;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AslHelp.Engines.Measuring.Cli.Pdb;

/// <summary>
///     Finds the size in bytes of a type record of a PDB.
/// </summary>
internal static class TypeSizes
{
    // Bounds the chain of modifiers, enums and bitfields over a base type, because a bad record can point at itself.
    private const int MaxDepth = 8;

    /// <summary>
    ///     Finds the size of the type of a member.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <param name="complete">The complete class, struct and union records by name.</param>
    /// <returns>
    ///     The size or <see langword="null"/> if the type doesn't have a size, such as <c>void</c>,
    ///     or its name doesn't have exactly 1 complete size.
    /// </returns>
    /// <exception cref="BadImageFormatException">The base types of <paramref name="type"/> chain deeper than a type can.</exception>
    public static uint? Of(CodeViewTypeRecord? type, IReadOnlyDictionary<string, List<CodeViewCompositeTypeRecord>> complete)
    {
        return Of(type, complete, 0);
    }

    /// <summary>
    ///     Gets the size of a complete class, struct or union record.
    /// </summary>
    /// <param name="record">The record.</param>
    /// <returns>
    ///     The size.
    /// </returns>
    public static uint OfComplete(CodeViewCompositeTypeRecord record)
    {
        return record is UnionTypeRecord union ? (uint)union.Size : (uint)((ClassTypeRecord)record).Size;
    }

    private static uint? Of(CodeViewTypeRecord? type, IReadOnlyDictionary<string, List<CodeViewCompositeTypeRecord>> complete, int depth)
    {
        if (depth > MaxDepth)
        {
            throw new BadImageFormatException(SR.FormatPdbTypeChainTooDeep(MaxDepth));
        }

        // Read an array length as bytes, because CodeView doesn't store a number of elements.
        return type switch
        {
            ArrayTypeRecord array => (uint)array.Length,
            BitFieldTypeRecord bitField => Of(bitField.BaseType, complete, depth + 1),
            EnumTypeRecord enumeration => Of(enumeration.BaseType, complete, depth + 1),
            ModifierTypeRecord modifier => Of(modifier.BaseType, complete, depth + 1),
            PointerTypeRecord pointer => pointer.Size,
            SimpleTypeRecord simple => OfSimple(simple),
            CodeViewCompositeTypeRecord record when record.StructureAttributes.HasFlag(StructureAttributes.FwdRef) => OfForward(record, complete),
            CodeViewCompositeTypeRecord record => OfComplete(record),
            _ => null,
        };
    }

    private static uint? OfForward(CodeViewCompositeTypeRecord forward, IReadOnlyDictionary<string, List<CodeViewCompositeTypeRecord>> complete)
    {
        if (!complete.TryGetValue(forward.Name!, out var records))
        {
            return null;
        }

        uint size = OfComplete(records[0]);

        return records.All(record => OfComplete(record) == size) ? size : null;
    }

    private static uint? OfSimple(SimpleTypeRecord simple)
    {
        return simple.Mode switch
        {
            SimpleTypeMode.Direct => OfKind(simple.Kind),
            SimpleTypeMode.NearPointer32 => 4,
            SimpleTypeMode.NearPointer64 => 8,
            _ => null,
        };
    }

    private static uint? OfKind(SimpleTypeKind kind)
    {
        return kind switch
        {
            SimpleTypeKind.Boolean8
                or SimpleTypeKind.Byte
                or SimpleTypeKind.Character8
                or SimpleTypeKind.NarrowCharacter
                or SimpleTypeKind.SByte
                or SimpleTypeKind.SignedCharacter
                or SimpleTypeKind.UnsignedCharacter => 1,
            SimpleTypeKind.Boolean16
                or SimpleTypeKind.Character16
                or SimpleTypeKind.Float16
                or SimpleTypeKind.Int16
                or SimpleTypeKind.Int16Short
                or SimpleTypeKind.UInt16
                or SimpleTypeKind.UInt16Short
                or SimpleTypeKind.WideCharacter => 2,
            SimpleTypeKind.Boolean32
                or SimpleTypeKind.Character32
                or SimpleTypeKind.Float32
                or SimpleTypeKind.Float32PartialPrecision
                or SimpleTypeKind.HResult
                or SimpleTypeKind.Int32
                or SimpleTypeKind.Int32Long
                or SimpleTypeKind.UInt32
                or SimpleTypeKind.UInt32Long => 4,
            SimpleTypeKind.Boolean64
                or SimpleTypeKind.Float64
                or SimpleTypeKind.Int64
                or SimpleTypeKind.Int64Quad
                or SimpleTypeKind.UInt64
                or SimpleTypeKind.UInt64Quad => 8,
            SimpleTypeKind.Float80 => 10,
            SimpleTypeKind.Boolean128
                or SimpleTypeKind.Float128
                or SimpleTypeKind.Int128
                or SimpleTypeKind.Int128Oct
                or SimpleTypeKind.UInt128
                or SimpleTypeKind.UInt128Oct => 16,
            _ => null,
        };
    }
}
