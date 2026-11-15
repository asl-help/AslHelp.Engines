using AslHelp.Engines.Measuring.Cli.Pdb.Errors;
using AsmResolver.IO;
using AsmResolver.Symbols.Pdb.Leaves;
using AsmResolver.Symbols.Pdb.Records;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;

namespace AslHelp.Engines.Measuring.Cli.Pdb;

/// <summary>
///     Finds the values of the compile-time constants of a PDB by name.
/// </summary>
/// <remarks>
///     A constant is a value of an enum or a <c>static constexpr</c> member, which the PDB lists as a constant symbol.
/// </remarks>
internal sealed class ConstantIndex
{
    private readonly Dictionary<string, List<EnumTypeRecord>> _enums;
    private readonly Dictionary<string, List<long?>> _symbols;

    private ConstantIndex(Dictionary<string, List<EnumTypeRecord>> enums, Dictionary<string, List<long?>> symbols)
    {
        _enums = enums;
        _symbols = symbols;
    }

    /// <summary>
    ///     Builds the index with 1 pass over the enums of the type stream and the records of the symbol record stream.
    /// </summary>
    /// <param name="leaves">The records of the type stream.</param>
    /// <param name="symbolRecords">The symbol record stream.</param>
    /// <returns>
    ///     The index.
    /// </returns>
    public static ConstantIndex Build(IEnumerable<ITpiLeaf> leaves, BinaryStreamReader symbolRecords)
    {
        Dictionary<string, List<EnumTypeRecord>> enums = [with(StringComparer.Ordinal)];

        foreach (var leaf in leaves)
        {
            if (leaf is not EnumTypeRecord record || record.StructureAttributes.HasFlag(StructureAttributes.FwdRef))
            {
                continue;
            }

            // Add the enum under its own name for Enum::Value and under its scope for Scope::Value, which an unnamed enum only has.
            string name = record.Name!;
            Add(enums, name, record);

            if (CppName.TrySplitScope(name, out string? scope, out _))
            {
                Add(enums, scope, record);
            }
        }

        return new ConstantIndex(enums, ConstantSymbols.Read(symbolRecords));
    }

    /// <summary>
    ///     Finds the value of a constant.
    /// </summary>
    /// <param name="name">
    ///     The name in C++, such as <c>Scope::Value</c>, which finds a constant symbol with that name
    ///     and a value of an enum that is called <c>Scope</c> or sits in <c>Scope</c>.
    /// </param>
    /// <returns>
    ///     The value or a failure with the name when there isn't a constant with the name, a value doesn't fit a <see cref="long"/>,
    ///     the name finds 2 values or the records of an enum don't read.
    /// </returns>
    public Result<long> Find(string name)
    {
        List<long?> values = _symbols.TryGetValue(name, out var symbols) ? [.. symbols] : [];

        if (CppName.TrySplitScope(name, out string? scope, out string? member) && _enums.TryGetValue(scope, out var records))
        {
            foreach (var record in records)
            {
                try
                {
                    values.AddRange(Values(record, member));
                }
                catch (Exception exception) when (PdbFile.IsBadData(exception))
                {
                    return PdbError.TypeUnreadable(record.Name!, exception.Message);
                }
            }
        }

        if (values.Contains(null))
        {
            return PdbError.ConstantDoesNotFit(name);
        }

        long[] distinct = [.. values.Select(static value => value!.Value).Distinct()];

        return distinct switch
        {
            [] => PdbError.ConstantNotFound(name),
            [long value] => value,
            [long first, long other, ..] => PdbError.ConstantAmbiguous(name, first, other),
        };
    }

    private static void Add<T>(Dictionary<string, List<T>> index, string name, T value)
    {
        ref var values = ref CollectionsMarshal.GetValueRefOrAddDefault(index, name, out _);
        (values ??= []).Add(value);
    }

    private static IEnumerable<long?> Values(EnumTypeRecord record, string member)
    {
        if (record.Fields is not { } fields)
        {
            yield break;
        }

        foreach (var field in fields.Entries)
        {
            if (field is EnumerateField enumerate && enumerate.Name == member)
            {
                yield return ToInt64(enumerate.Value);
            }
        }
    }

    // Return null for an unsigned 64-bit value past the signed range, because it doesn't fit.
    private static long? ToInt64(object value)
    {
        return value switch
        {
            ulong unsigned => unsigned <= long.MaxValue ? (long)unsigned : null,
            sbyte or byte or short or ushort or int or uint or long => Convert.ToInt64(value, CultureInfo.InvariantCulture),
            _ => null,
        };
    }
}

/// <summary>
///     Reads the constant symbols out of the bytes of the symbol record stream.
/// </summary>
/// <remarks>
///     AsmResolver reads the value of a constant symbol as 2 bytes, so a value from 0x8000 on comes back as its kind and moves the name.
/// </remarks>
file static class ConstantSymbols
{
    private const int HeaderSize = 2 * sizeof(ushort);
    private const int TypeIndexSize = sizeof(uint);

    /// <summary>
    ///     Reads each constant symbol with a value of a known kind.
    /// </summary>
    /// <param name="reader">The symbol record stream.</param>
    /// <returns>
    ///     The values by name, with <see langword="null"/> for a value that doesn't fit a <see cref="long"/>, such as a float.
    ///     A name that several compile units list has several values.
    /// </returns>
    public static Dictionary<string, List<long?>> Read(BinaryStreamReader reader)
    {
        Dictionary<string, List<long?>> symbols = [with(StringComparer.Ordinal)];

        while (reader.CanRead(HeaderSize))
        {
            ushort length = reader.ReadUInt16();
            uint next = reader.RelativeOffset + length;

            if ((CodeViewSymbolType)reader.ReadUInt16() == CodeViewSymbolType.Constant)
            {
                reader.RelativeOffset += TypeIndexSize;

                // Skip a symbol with a value of an unknown kind, because the name after the value can't be found.
                if (TryReadValue(ref reader, out long? value))
                {
                    ref var values = ref CollectionsMarshal.GetValueRefOrAddDefault(symbols, reader.ReadUtf8String(), out _);
                    (values ??= []).Add(value);
                }
            }

            if (next > reader.Length)
            {
                break;
            }

            reader.RelativeOffset = next;
        }

        return symbols;
    }

    // Return true with a null value for a value that doesn't fit, such as a float or a large UQuadWord, because the name still follows it.
    private static bool TryReadValue(ref BinaryStreamReader reader, out long? value)
    {
        var kind = (CodeViewLeafKind)reader.ReadUInt16();

        value = kind switch
        {
            < CodeViewLeafKind.Numeric => (ushort)kind,
            CodeViewLeafKind.Char => reader.ReadSByte(),
            CodeViewLeafKind.Short => reader.ReadInt16(),
            CodeViewLeafKind.UShort => reader.ReadUInt16(),
            CodeViewLeafKind.Long => reader.ReadInt32(),
            CodeViewLeafKind.ULong => reader.ReadUInt32(),
            CodeViewLeafKind.QuadWord => reader.ReadInt64(),
            CodeViewLeafKind.UQuadWord => ToSigned(reader.ReadUInt64()),
            _ => null,
        };

        return value is not null || kind is CodeViewLeafKind.UQuadWord || TrySkipValue(ref reader, kind);
    }

    private static bool TrySkipValue(ref BinaryStreamReader reader, CodeViewLeafKind kind)
    {
        int size = kind switch
        {
            CodeViewLeafKind.Real16 => 2,
            CodeViewLeafKind.Real32 => 4,
            CodeViewLeafKind.Real48 => 6,
            CodeViewLeafKind.Real64 or CodeViewLeafKind.Complex32 or CodeViewLeafKind.Date => 8,
            CodeViewLeafKind.Real80 => 10,
            CodeViewLeafKind.Real128
                or CodeViewLeafKind.Complex64
                or CodeViewLeafKind.OctWord
                or CodeViewLeafKind.UOctWord
                or CodeViewLeafKind.Decimal => 16,
            CodeViewLeafKind.Complex80 => 20,
            CodeViewLeafKind.Complex128 => 32,
            CodeViewLeafKind.VarString => reader.ReadUInt16(),
            _ => -1,
        };

        if (size < 0)
        {
            return false;
        }

        reader.RelativeOffset += (uint)size;

        return true;
    }

    private static long? ToSigned(ulong value)
    {
        return value <= long.MaxValue ? (long)value : null;
    }
}
