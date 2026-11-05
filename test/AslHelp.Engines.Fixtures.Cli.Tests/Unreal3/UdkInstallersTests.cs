using AslHelp.Engines.Fixtures.Cli.Unreal3;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AslHelp.Engines.Fixtures.Cli.Tests.Unreal3;

public sealed class UdkInstallersTests
{
    private const string InstallerPrefix = "UDKInstall-";
    private const string InstallerSuffix = ".exe";

    // The file lists each version once in ordinal order, so the error message lists them in version order.
    [Test]
    public void All_Rows_AreUniqueAndOrdered()
    {
        string[] versions = new string[UdkInstallers.All.Count];

        for (int i = 0; i < versions.Length; i++)
        {
            versions[i] = UdkInstallers.All[i].Version;
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(versions, Is.Not.Empty);
            Assert.That(versions, Is.Unique);
            Assert.That(versions, Is.Ordered.Using((IComparer<string>)StringComparer.Ordinal));
        }
    }

    // The embedded file parses and every row pins a sha1, so a half-copied row fails CI and not a download.
    [Test]
    public void All_EmbeddedFile_ParsesWithSha1InEveryRow()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(UdkInstallers.All, Is.Not.Empty);

            foreach (var row in UdkInstallers.All)
            {
                Assert.That(row.Sha1, Does.Match("^[0-9a-f]{40}$"), row.Version);
            }
        }
    }

    // Every row maps to UDKInstall-<version>.exe of the udkinstaller item with its sha1, so every installer downloads pinned.
    [Test]
    public void All_EveryRow_MapsToItsPinnedFile()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(UdkInstallers.All, Is.Not.Empty);

            foreach (var row in UdkInstallers.All)
            {
                var installer = row.File;
                Assert.That(installer.Name, Is.EqualTo($"{InstallerPrefix}{row.Version}{InstallerSuffix}"), installer.Name);
                Assert.That(installer.Item, Is.EqualTo("udkinstaller"), installer.Name);
                Assert.That(installer.Hash.Hex, Is.EqualTo(row.Sha1), installer.Name);
            }
        }
    }

    // A lowercase version matches, so the row comes back with the pinned spelling and sha1.
    [Test]
    public void Find_LowercaseVersion_ReturnsThePinnedInstaller()
    {
        var installer = UdkInstallers.Find("2011-03-beta2");

        Assert.That(installer, Is.EqualTo(new UdkInstaller(
                "2011-03-BETA2",
                "1c578892b9adfb861a62d4960cc5cfd80e26801f",
                null)));
    }

    // The version of the readme example has been built end to end, so its row has a checked date.
    [Test]
    public void Find_ReadmeExampleVersion_IsChecked()
    {
        Assert.That(UdkInstallers.Find("2015-02").Checked, Is.Not.Null);
    }

    // An unpinned version is refused with the pinned versions, the data file and the readme section, without a parameter
    // suffix, because the CLI prints the message as is.
    [Test]
    public void Find_UnpinnedVersion_ThrowsWithThePinnedVersions()
    {
        Assert.That(
            () => UdkInstallers.Find("1999-01"),
            Throws.InvalidOperationException
                .With.Message.Contains("1999-01")
                .And.Message.Contains("2009-11-BETA")
                .And.Message.Contains("2015-02")
                .And.Message.Contains("installers.json")
                .And.Message.Contains("Adding a UDK version")
                .And.Message.Not.Contains("(Parameter"));
    }

    // A row reads with camelCase keys and checked as a date or null, as installers.json writes them.
    [Test]
    public void Read_Rows_ReadsVersionSha1AndChecked()
    {
        using var json = Utf8Stream("""
            [
              { "version": "1.0", "sha1": "0000000000000000000000000000000000000001", "checked": null },
              { "version": "2.0", "sha1": "0000000000000000000000000000000000000002", "checked": "2026-11-01" }
            ]
            """);

        UdkInstaller[] expected =
        [
            new("1.0", "0000000000000000000000000000000000000001", null),
            new("2.0", "0000000000000000000000000000000000000002", new DateOnly(2026, 11, 1)),
        ];

        var installers = UdkInstallers.Read(json);

        Assert.That(installers, Is.EqualTo(expected));
    }

    // A row that lacks a key is refused, so a half-copied row never reads as a null sha1.
    [Test]
    public void Read_RowWithoutSha1_ThrowsInvalidDataException()
    {
        using var json = Utf8Stream("""[{ "version": "1.0", "checked": null }]""");

        Assert.That(
            () => UdkInstallers.Read(json),
            Throws.TypeOf<InvalidDataException>().With.InnerException.TypeOf<JsonException>());
    }

    // A null value of a key that is not nullable is refused, so a half-copied row never reads as a null version.
    [Test]
    public void Read_RowWithNullVersion_ThrowsInvalidDataException()
    {
        using var json = Utf8Stream("""[{ "version": null, "sha1": "0000000000000000000000000000000000000001", "checked": null }]""");

        Assert.That(
            () => UdkInstallers.Read(json),
            Throws.TypeOf<InvalidDataException>().With.InnerException.TypeOf<JsonException>());
    }

    // A key outside the 3 is refused, so a misspelled key is caught at the parse and not read as a missing value.
    [Test]
    public void Read_RowWithUnknownKey_ThrowsInvalidDataException()
    {
        using var json = Utf8Stream("""[{ "version": "1.0", "sha1": "0000000000000000000000000000000000000001", "checked": null, "note": "" }]""");

        Assert.That(
            () => UdkInstallers.Read(json),
            Throws.TypeOf<InvalidDataException>().With.InnerException.TypeOf<JsonException>());
    }

    // A damaged file is refused with installers.json in the message, so the CLI output points at the file to fix and not only a JSON path.
    [Test]
    public void Read_DamagedJson_ThrowsWithTheFileName()
    {
        using var json = Utf8Stream("""[{ "version": "1.0" "sha1": "0000000000000000000000000000000000000001", "checked": null }]""");

        Assert.That(
            () => UdkInstallers.Read(json),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("installers.json"));
    }

    // An uppercase sha1 is refused at load with the file and the version, so a bad pin fails every build and not only the download.
    [Test]
    public void Read_UppercaseSha1_ThrowsWithTheFileNameAndVersion()
    {
        using var json = Utf8Stream("""[{ "version": "1.0", "sha1": "000000000000000000000000000000000000000A", "checked": null }]""");

        Assert.That(
            () => UdkInstallers.Read(json),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("installers.json").And.Message.Contains("'1.0'"));
    }

    // The JSON literal null is refused, so a file that is only null never reads as no versions.
    [Test]
    public void Read_NullLiteral_ThrowsInvalidDataException()
    {
        using var json = Utf8Stream("null");

        Assert.That(
            () => UdkInstallers.Read(json),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("installers.json"));
    }

    private static MemoryStream Utf8Stream(string text)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(text));
    }
}
