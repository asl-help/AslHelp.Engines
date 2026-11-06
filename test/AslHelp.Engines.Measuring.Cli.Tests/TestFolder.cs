using System;
using System.IO;

namespace AslHelp.Engines.Measuring.Cli.Tests;

/// <summary>
///     Creates a fresh temp folder for 1 test.
/// </summary>
internal sealed class TestFolder
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="TestFolder"/> class and creates <see cref="Root"/>.
    /// </summary>
    public TestFolder()
    {
        Root = Path.Combine(Path.GetTempPath(), $"AslHelp.Engines.Measuring.Cli.Tests-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(Root);
    }

    /// <summary>
    ///     Gets the full path of the temp folder.
    /// </summary>
    public string Root { get; }

    /// <summary>
    ///     Deletes <see cref="Root"/> and everything in it.
    /// </summary>
    public void Delete()
    {
        Directory.Delete(Root, recursive: true);
    }
}
