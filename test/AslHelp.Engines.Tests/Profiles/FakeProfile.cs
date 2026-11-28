using AslHelp.Engines.Profiles;
using AslHelp.Memory.Locating;
using System.Linq;

namespace AslHelp.Engines.Tests.Profiles;

/// <summary>
///     Represents a profile of a made-up engine called <c>fake</c>, which reads its document only through public members of AslHelp.Engines.
/// </summary>
/// <param name="speedOffset">The offset of <c>Speed</c> in a <c>Thing</c>.</param>
/// <param name="thingsLocator">The routes to the <c>Things</c> global.</param>
internal sealed class FakeProfile(
    int speedOffset,
    MemoryLocator thingsLocator)
{
    /// <summary>
    ///     The value of <c>engine</c> in the documents of the made-up engine.
    /// </summary>
    public const string Engine = "fake";

    /// <summary>
    ///     Gets the offset of <c>Speed</c> in a <c>Thing</c>.
    /// </summary>
    public int SpeedOffset { get; } = speedOffset;

    /// <summary>
    ///     Gets the routes to the <c>Things</c> global.
    /// </summary>
    public MemoryLocator ThingsLocator { get; } = thingsLocator;

    /// <summary>
    ///     Writes a document of the made-up engine with <c>Speed</c> at 0x10 and 1 route to <c>Things</c>.
    /// </summary>
    /// <param name="key">The key or <see langword="null"/> for a document without a key.</param>
    /// <param name="versions">The versions of the document.</param>
    /// <returns>
    ///     The document.
    /// </returns>
    public static string Document(string? key, params string[] versions)
    {
        return Write(key is null ? "" : $"\"key\": \"{key}\",", versions);
    }

    /// <summary>
    ///     Writes a document of the made-up engine for 1.0 without a key and with more keys at the root.
    /// </summary>
    /// <param name="rootKeys">The keys to add, each with its value and a comma, like <c>"kee": 1,</c>.</param>
    /// <returns>
    ///     The document.
    /// </returns>
    public static string DocumentWith(string rootKeys)
    {
        return Write(rootKeys, ["1.0"]);
    }

    /// <summary>
    ///     Maps a document of the made-up engine.
    /// </summary>
    /// <param name="reader">The reader of the document.</param>
    /// <returns>
    ///     The profile or a failure for another engine or a missing part.
    /// </returns>
    public static Result<FakeProfile> Map(ProfileReader reader)
    {
        if (reader.Engine != Engine)
        {
            return new OtherEngineError(reader.Engine);
        }

        int speedOffset = reader.ReadOffset("Thing", "Speed");
        var thingsLocator = reader.ReadGlobal("Things");

        // Skip GetFailure, because the reader fails the document after the map when a part is missing.
        return new FakeProfile(speedOffset, thingsLocator!);
    }

    private static string Write(string rootKeys, string[] versions)
    {
        string versionList = string.Join(", ", versions.Select(static version => $"\"{version}\""));

        return $$"""
            {
              "engine": "{{Engine}}",
              {{rootKeys}}
              "versions": [{{versionList}}],
              "offsets": { "Thing": { "Speed": "0x10" } },
              "globals": { "Things": { "1.0": [{ "forward": "A1 ????????", "offset": 1 }, { "read": "global" }] } }
            }
            """;
    }
}

/// <summary>
///     Represents a document of another engine than the made-up one.
/// </summary>
/// <param name="Engine">The engine of the document.</param>
file sealed record OtherEngineError(
    string Engine) : ResultError
{
    /// <inheritdoc/>
    public override string Message => $"The fake mapper doesn't read '{Engine}'.";
}
