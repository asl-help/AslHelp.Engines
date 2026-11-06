namespace AslHelp.Engines.Measuring.Cli.Routes;

/// <summary>
///     Represents 1 executable section of an executable file.
/// </summary>
/// <param name="Rva">The RVA of the section.</param>
/// <param name="Bytes">The bytes of the section in the file.</param>
internal sealed record CodeSection(
    uint Rva,
    byte[] Bytes);
