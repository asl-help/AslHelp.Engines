using AslHelp.Engines.Measuring.Cli.Profiles;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static AslHelp.Engines.Measuring.Cli.Tests.Profiles.TestGroups;

namespace AslHelp.Engines.Measuring.Cli.Tests.Profiles;

public sealed class ProfileDocumentsTests
{
    private static readonly string[] _globalsInOrder = ["Zeta", "Alpha", "Mid"];

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

    // A report without groups doesn't write a document and returns 0.
    [Test]
    public async Task WriteAsync_NoGroups_WritesNothing()
    {
        var written = await ProfileDocuments.WriteAsync(Report(), _folder.Root, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(written.Unwrap(), Is.Zero);
            Assert.That(Directory.GetFiles(_folder.Root), Is.Empty);
        }
    }

    // The error for a 3rd group that gets the file of the 1st lists those 2 groups and leaves the 2nd out.
    [Test]
    public async Task WriteAsync_ThirdGroupSameFileNameAsFirst_ListsBothGroups()
    {
        var report = Report(Group(null, ["1.0-beta", "1.1"]), Group(null, ["2.0"]), Group(null, ["1.0-BETA"]));

        var written = await ProfileDocuments.WriteAsync(report, _folder.Root, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(written.TryUnwrapErr(out var error), Is.True);
            Assert.That(error!.Message, Does.Contain("[1.0-beta, 1.1]").And.Contain("[1.0-BETA]").And.Contain("engine-1.0-BETA.json"));
            Assert.That(error.Message, Does.Not.Contain("2.0"));
            Assert.That(Directory.GetFiles(_folder.Root), Is.Empty);
        }
    }

    // 2 routes with the same name in the 2nd group fail the write before the document of the 1st group.
    [Test]
    public async Task WriteAsync_TwoRoutesSameName_WritesNothing()
    {
        var group = Group("a", ["2.0", "2.0-a"], ("Global", RoutesOf(Route("2.0-a"), Route("2.0"))));

        var written = await ProfileDocuments.WriteAsync(Report(Group(null, ["1.0"]), group), _folder.Root, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(written.TryUnwrapErr(out _), Is.True);
            Assert.That(Directory.GetFiles(_folder.Root), Is.Empty);
        }
    }

    // A canceled token stops the write before the 1st document.
    [Test]
    public void WriteAsync_CanceledToken_ThrowsAndWritesNothing()
    {
        using CancellationTokenSource source = new();
        source.Cancel();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                async () => await ProfileDocuments.WriteAsync(Report(Group(null, ["1.0"]), Group(null, ["2.0"])), _folder.Root, source.Token),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(Directory.GetFiles(_folder.Root), Is.Empty);
        }
    }

    // The globals keep the order of the report, not the alphabetical order.
    [Test]
    public async Task WriteAsync_GlobalsOutOfNameOrder_KeepsReportOrder()
    {
        var group = Group(null, ["1.0"], ("Zeta", RoutesOf(Route("1.0"))), ("Alpha", RoutesOf(Route("1.0"))), ("Mid", RoutesOf(Route("1.0"))));

        _ = await ProfileDocuments.WriteAsync(Report(group), _folder.Root, CancellationToken.None);

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_folder.Root, "engine-1.0.json")));

        Assert.That(json.RootElement.GetProperty("globals").EnumerateObject().Select(static global => global.Name), Is.EqualTo(_globalsInOrder));
    }

    private static MeasureReport Report(params VersionGroup[] groups)
    {
        return new MeasureReport("engine", [], groups);
    }
}
