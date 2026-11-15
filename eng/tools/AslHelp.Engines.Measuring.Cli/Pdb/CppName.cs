using System;
using System.Diagnostics.CodeAnalysis;

namespace AslHelp.Engines.Measuring.Cli.Pdb;

/// <summary>
///     Splits C++ identifiers at the last scope separator.
/// </summary>
internal static class CppName
{
    private const string ScopeSeparator = "::";

    /// <summary>
    ///     Splits a name into its scope and the name in that scope.
    /// </summary>
    /// <param name="name">The name, such as <c>Scope::Value</c>.</param>
    /// <param name="scope">The scope, such as <c>Scope</c>.</param>
    /// <param name="member">The name in the scope, such as <c>Value</c>.</param>
    /// <returns>
    ///     <see langword="true"/> if the name has a scope and a name after it.
    /// </returns>
    public static bool TrySplitScope(
        string name,
        [NotNullWhen(true)] out string? scope,
        [NotNullWhen(true)] out string? member)
    {
        int separator = name.LastIndexOf(ScopeSeparator, StringComparison.Ordinal);

        if (separator <= 0 || separator + ScopeSeparator.Length >= name.Length)
        {
            scope = null;
            member = null;

            return false;
        }

        scope = name[..separator];
        member = name[(separator + ScopeSeparator.Length)..];

        return true;
    }
}
