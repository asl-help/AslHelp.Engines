namespace AslHelp.Engines.Measuring.Cli.Unreal4;

/// <summary>
///     Describes a static without a symbol, found as the 1st static that a function with a public symbol reads.
/// </summary>
/// <param name="Function">The name of the function in C++, such as <c>Scope::Get</c>.</param>
/// <param name="From">The 1st minor version with the static or <see langword="null"/> if every earlier version has it.</param>
/// <param name="Until">The last minor version with the static or <see langword="null"/> if every later version has it.</param>
internal sealed record UnrealFunctionStaticTarget(
    string Function,
    string? From = null,
    string? Until = null) : UnrealTarget(From, Until);
