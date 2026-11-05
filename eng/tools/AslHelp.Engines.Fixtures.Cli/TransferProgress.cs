namespace AslHelp.Engines.Fixtures.Cli;

/// <summary>
///     Represents how far a download or an upload has come.
/// </summary>
/// <param name="Bytes">The number of bytes of the file on disk or sent so far.</param>
/// <param name="Length">The length of the whole file or <see langword="null"/> when the server sends no length.</param>
internal readonly record struct TransferProgress(
    long Bytes,
    long? Length);
