// Turn off native AOT, because the check only runs from source and the NuGet audit flags the AOT compiler package.
#:property PublishAot=false

// Fails on a tracked file that git treats as binary, stores with CRLF line endings, starts with a UTF-8 byte order mark or
// has bytes that are not valid UTF-8.
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Unicode;

ProcessStartInfo startInfo = new("git", ["ls-files", "--eol", "-z"])
{
    RedirectStandardOutput = true,
    RedirectStandardError = true,
};

var output = await Process.RunAndCaptureTextAsync(startInfo).ConfigureAwait(false);

if (output.ExitStatus.ExitCode != 0)
{
    await Console.Error.WriteAsync(output.StandardError).ConfigureAwait(false);
    return 1;
}

var failed = false;

// Split each entry, which git writes as "<index eol> <working tree eol> <attributes>\t<path>".
foreach (var entry in output.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries))
{
    var indexLineEnding = entry[..entry.IndexOf(' ', StringComparison.Ordinal)];
    var path = entry[(entry.IndexOf('\t', StringComparison.Ordinal) + 1)..];

    // Fail on a file that git treats as binary, because git never normalizes the line endings of a binary file.
    if (indexLineEnding == "i/-text")
    {
        Console.WriteLine($"{path}: git treats it as binary, not as text.");
        failed = true;
        continue;
    }

    // Fail on CRLF in the index, because a file that skipped normalization keeps the CRLF line endings in every clone.
    if (indexLineEnding is "i/crlf" or "i/mixed")
    {
        Console.WriteLine($"{path}: git stores it with CRLF line endings.");
        failed = true;
    }

    var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);

    if (bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble))
    {
        Console.WriteLine($"{path}: starts with a UTF-8 byte order mark.");
        failed = true;
    }

    if (!Utf8.IsValid(bytes))
    {
        Console.WriteLine($"{path}: is not valid UTF-8.");
        failed = true;
    }
}

return failed ? 1 : 0;
