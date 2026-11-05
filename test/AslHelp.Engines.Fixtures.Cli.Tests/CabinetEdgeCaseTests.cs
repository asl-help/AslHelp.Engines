using NUnit.Framework;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

public sealed class CabinetEdgeCaseTests
{
    private TestFolder _folder = null!;

    [SetUp]
    public void SetUp()
    {
        _folder = new TestFolder();
    }

    [TearDown]
    public void TearDown()
    {
        _folder.Delete();
    }

    // A missing cabinet fails with the error line of expand.exe after the command line, so the message shows why without a 2nd run.
    [Test]
    [Platform("Win")]
    public async Task ExpandAsync_MissingCabinet_PutsTheToolErrorInTheMessage()
    {
        string folder = _folder.NewFolder();
        string cabinetPath = Path.Combine(folder, "files.cab");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Cabinet.ExpandAsync(cabinetPath, "*", folder, CancellationToken.None));

        // Compare without case, because expand.exe prints the path in lower case.
        Assert.That(exception!.Message.Split(Environment.NewLine)[1..], Has.Some.Contains(cabinetPath).IgnoreCase);
    }
}
