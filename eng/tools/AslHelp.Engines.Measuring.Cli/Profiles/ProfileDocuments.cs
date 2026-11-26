using AslHelp.Engines.Measuring.Cli.Profiles.Errors;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Measuring.Cli.Profiles;

/// <summary>
///     Writes the profile documents of a report.
/// </summary>
internal static class ProfileDocuments
{
    /// <summary>
    ///     Writes 1 document per group of <paramref name="report"/> into <paramref name="directory"/>.
    /// </summary>
    /// <param name="report">The report with the routes of its groups.</param>
    /// <param name="directory">The folder for the documents, which must exist.</param>
    /// <param name="cancellationToken">The token that stops the writes.</param>
    /// <returns>
    ///     The number of documents written or the error for 2 groups with the same file name or 2 routes with the same name. It doesn't write any document in that case.
    /// </returns>
    /// <exception cref="IOException">A document can't be written.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="UnauthorizedAccessException">A document can't be written.</exception>
    public static async Task<Result<int>> WriteAsync(MeasureReport report, string directory, CancellationToken cancellationToken)
    {
        // Compare without case, because Windows treats a.json and A.json as 1 file.
        Dictionary<string, ProfileDocument> documents = [with(StringComparer.OrdinalIgnoreCase)];

        foreach (var group in report.Groups)
        {
            if (!ProfileDocument.Create(report.Engine, group).TryUnwrap(out var document, out var error))
            {
                return error;
            }

            if (!documents.TryAdd(document.FileName, document))
            {
                return ProfileError.SameFileName(document.FileName, documents[document.FileName].Versions, document.Versions);
            }
        }

        foreach ((string fileName, var document) in documents)
        {
            string json = JsonSerializer.Serialize(document, ProfileDocumentContext.Default.ProfileDocument);
            await File.WriteAllTextAsync(Path.Combine(directory, fileName), json, cancellationToken).ConfigureAwait(false);
        }

        return documents.Count;
    }
}
