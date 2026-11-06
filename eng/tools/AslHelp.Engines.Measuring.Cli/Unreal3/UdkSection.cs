namespace AslHelp.Engines.Measuring.Cli.Unreal3;

/// <summary>
///     Represents 1 section of <c>UDK.exe</c> without code, as read from the running process.
/// </summary>
/// <param name="Rva">The RVA of the section.</param>
/// <param name="Bytes">The live bytes of the section, with zeros for a page that did not read.</param>
internal sealed record UdkSection(
    uint Rva,
    byte[] Bytes);
