using System;

namespace AslHelp.Engines.Unreal;

/// <summary>
///     Represents the file version in the version resource of a module file.
/// </summary>
/// <param name="Text">The file version text, like <c>4.19.2-0+++UE4+Release-4.19</c>. <see langword="null"/> when the file doesn't have one.</param>
/// <param name="Numbers">
///     The 4 numbers of the fixed file version, 0.0.0.0 when the file doesn't have a version resource. They don't come from
///     <paramref name="Text"/> and can differ from it.
/// </param>
internal sealed record ModuleFileVersion(
    string? Text,
    Version Numbers);
