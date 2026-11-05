using System;
using System.Collections.Generic;
using System.Linq;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

/// <summary>
///     Records every progress value on the calling thread, so a test reads the values before the call returns.
/// </summary>
internal sealed class RecordingProgress : IProgress<TransferProgress>
{
    /// <summary>
    ///     Gets the reported values in order.
    /// </summary>
    public List<TransferProgress> Values { get; } = [];

    /// <summary>
    ///     Gets the byte counts of the reported values in order.
    /// </summary>
    public List<long> Bytes => [.. Values.Select(value => value.Bytes)];

    /// <inheritdoc/>
    public void Report(TransferProgress value)
    {
        Values.Add(value);
    }
}
