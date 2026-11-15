using AslHelp.Engines.Measuring.Cli.Pdb;

namespace AslHelp.Engines.Measuring.Cli.Unreal4;

/// <summary>
///     Describes a compile-time constant, whose name in C++ is the key of the target, such as <c>Scope::Value</c>.
///     The report lists its value as <c>Scope.Value</c> next to the offsets of <c>Scope</c>.
/// </summary>
/// <param name="From">The 1st minor version with the constant or <see langword="null"/> if every earlier version has it.</param>
/// <param name="Until">The last minor version with the constant or <see langword="null"/> if every later version has it.</param>
internal sealed record UnrealConstantTarget(
    string? From = null,
    string? Until = null) : UnrealTarget(From, Until)
{
    /// <summary>
    ///     Gets the key of a constant in the report, with a dot between the scope and the name.
    /// </summary>
    /// <param name="name">The name in C++, such as <c>Scope::Value</c>.</param>
    /// <returns>
    ///     The key, such as <c>Scope.Value</c>. <see langword="null"/> if the name doesn't have a scope.
    /// </returns>
    public static string? ReportKey(string name)
    {
        return CppName.TrySplitScope(name, out string? scope, out string? member) ? $"{scope}.{member}" : null;
    }
}
