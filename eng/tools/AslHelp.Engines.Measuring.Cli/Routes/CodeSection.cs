using System;

namespace AslHelp.Engines.Measuring.Cli.Routes;

/// <summary>
///     Represents 1 section or the headers of an executable file.
/// </summary>
/// <param name="Rva">The RVA of the section.</param>
/// <param name="Bytes">The bytes of the section in the file, which stay valid until their image is disposed.</param>
internal sealed record CodeSection(
    uint Rva,
    ReadOnlyMemory<byte> Bytes);
