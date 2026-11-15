using AslHelp.Engines.Measuring.Cli.Pdb.Errors;
using AsmResolver.Symbols.Pdb;
using AsmResolver.Symbols.Pdb.Records;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace AslHelp.Engines.Measuring.Cli.Pdb;

/// <summary>
///     Finds the RVAs of the globals and functions of a PDB by name.
/// </summary>
internal sealed class SymbolIndex
{
    private readonly Dictionary<string, (uint First, uint? Other)> _functions = [with(StringComparer.Ordinal)];
    private readonly HashSet<uint> _globalRvas = [];
    private readonly Dictionary<string, (uint First, uint? Other)> _globals = [with(StringComparer.Ordinal)];
    private readonly IReadOnlyList<uint> _sectionRvas;

    private SymbolIndex(IReadOnlyList<uint> sectionRvas)
    {
        _sectionRvas = sectionRvas;
    }

    /// <summary>
    ///     Builds the index with 1 pass over the public symbols.
    /// </summary>
    /// <param name="symbols">The records of the global symbol stream. Every record but a public stays out.</param>
    /// <param name="sectionRvas">The RVA of each section in section order. A symbol from any other section stays out.</param>
    /// <returns>
    ///     The index.
    /// </returns>
    public static SymbolIndex Build(IEnumerable<ICodeViewSymbol> symbols, IReadOnlyList<uint> sectionRvas)
    {
        SymbolIndex index = new(sectionRvas);

        foreach (var symbol in symbols)
        {
            if (symbol is PublicSymbol @public)
            {
                index.AddPublic(@public);
            }
        }

        return index;
    }

    /// <summary>
    ///     Finds the RVA of a global in the public symbols.
    /// </summary>
    /// <param name="name">
    ///     The decorated name or the name in C++, such as <c>Scope::Value</c>.
    ///     A template scope or a special member only has its decorated name.
    ///     A C global on x86 keeps its leading underscore, such as <c>_value</c>.
    /// </param>
    /// <returns>
    ///     The RVA or a failure with the name when there isn't a global with the name or the name finds 2 RVAs.
    /// </returns>
    public Result<uint> GlobalRva(string name)
    {
        return Find(_globals, name);
    }

    /// <summary>
    ///     Checks that a global in the public symbols sits at an RVA.
    /// </summary>
    /// <param name="rva">The RVA.</param>
    /// <returns>
    ///     <see langword="true"/> if the public symbols have a global at <paramref name="rva"/>.
    /// </returns>
    public bool HasGlobalAt(uint rva)
    {
        return _globalRvas.Contains(rva);
    }

    /// <summary>
    ///     Finds the RVA of a function in the public symbols.
    /// </summary>
    /// <param name="name">
    ///     The decorated name or the name in C++, such as <c>Scope::Get</c>.
    ///     A template scope or a special member only has its decorated name.
    /// </param>
    /// <returns>
    ///     The RVA or a failure with the name when there isn't a function with the name or overloads of the name find 2 RVAs.
    /// </returns>
    public Result<uint> FunctionRva(string name)
    {
        return Find(_functions, name);
    }

    private static Result<uint> Find(Dictionary<string, (uint First, uint? Other)> index, string name)
    {
        if (!index.TryGetValue(name, out var rvas))
        {
            return PdbError.SymbolNotFound(name);
        }

        return rvas.Other is uint other ? PdbError.SymbolAmbiguous(name, rvas.First, other) : rvas.First;
    }

    private static void Add(Dictionary<string, (uint First, uint? Other)> index, string name, uint rva)
    {
        ref var rvas = ref CollectionsMarshal.GetValueRefOrAddDefault(index, name, out bool exists);

        if (!exists)
        {
            rvas = (rva, null);
        }
        else if (rvas.First != rva)
        {
            rvas.Other ??= rva;
        }
    }

    private void AddPublic(PublicSymbol symbol)
    {
        if (!TryGetRva(symbol.SegmentIndex, symbol.Offset, out uint rva))
        {
            return;
        }

        string decorated = symbol.Name!;
        var index = symbol.IsFunction ? _functions : _globals;

        Add(index, decorated, rva);

        if (Undecorator.Undecorate(decorated) is string undecorated)
        {
            Add(index, undecorated, rva);
        }

        if (!symbol.IsFunction)
        {
            _ = _globalRvas.Add(rva);
        }
    }

    private bool TryGetRva(ushort section, uint offset, out uint rva)
    {
        // Refuse section 0, because sections count from 1 and an absolute symbol doesn't have an RVA.
        if (section == 0 || section > _sectionRvas.Count)
        {
            rva = 0;

            return false;
        }

        rva = _sectionRvas[section - 1] + offset;

        return true;
    }
}

/// <summary>
///     Reads the name in C++ out of a decorated name.
/// </summary>
/// <remarks>
///     Reads only plain scopes and doesn't call <c>UnDecorateSymbolName</c>, because that would be 1 P/Invoke for each public of a PDB.
/// </remarks>
file static class Undecorator
{
    /// <summary>
    ///     Reads <c>?Name@Inner@Outer@@...</c> as <c>Outer::Inner::Name</c>.
    /// </summary>
    /// <param name="decorated">The decorated name.</param>
    /// <returns>
    ///     The name or <see langword="null"/> for a name without decoration, a template scope, a back reference or a special member,
    ///     because those need the whole decoration rules.
    /// </returns>
    public static string? Undecorate(string decorated)
    {
        if (!decorated.StartsWith('?', StringComparison.Ordinal))
        {
            return null;
        }

        int end = decorated.IndexOf("@@", StringComparison.Ordinal);

        if (end < 0)
        {
            return null;
        }

        var scopes = decorated.AsSpan(1, end - 1);
        int separators = 0;
        bool partStart = true;

        foreach (char character in scopes)
        {
            if (partStart && (character is '@' or '?' || char.IsAsciiDigit(character)))
            {
                return null;
            }

            partStart = character == '@';
            separators += partStart ? 1 : 0;
        }

        if (partStart)
        {
            return null;
        }

        // Build the text in 1 allocation, because the index undecorates every public of a PDB.
        return string.Create(
            scopes.Length + separators,
            (decorated, end),
            static (text, state) => WriteReversed(state.decorated.AsSpan(1, state.end - 1), text));
    }

    // Writes the parts of the scopes from the last to the 1st with :: between them.
    private static void WriteReversed(ReadOnlySpan<char> scopes, Span<char> text)
    {
        while (true)
        {
            int separator = scopes.LastIndexOf('@');
            var part = scopes[(separator + 1)..];

            part.CopyTo(text);
            text = text[part.Length..];

            if (separator < 0)
            {
                return;
            }

            "::".CopyTo(text);
            text = text[2..];
            scopes = scopes[..separator];
        }
    }
}
