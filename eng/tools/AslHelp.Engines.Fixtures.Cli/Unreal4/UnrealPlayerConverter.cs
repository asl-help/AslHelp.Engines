using AslHelp.Engines.Fixtures.Unreal4;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AslHelp.Engines.Fixtures.Cli.Unreal4;

/// <summary>
///     Reads and writes an <see cref="UnrealPlayer"/> as its tag, such as <c>win-x64-shipping</c>.
/// </summary>
internal sealed class UnrealPlayerConverter : JsonConverter<UnrealPlayer>
{
    /// <inheritdoc/>
    /// <remarks>
    ///     Gets a <c>null</c> too, so a <c>null</c> in a list of players fails like a bad tag.
    /// </remarks>
    public override bool HandleNull => true;

    /// <inheritdoc/>
    /// <exception cref="JsonException">The value isn't a player tag.</exception>
    public override UnrealPlayer Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? tag = reader.GetString();

        return UnrealPlayer.TryParse(tag, out var player)
            ? player
            : throw new JsonException(UnrealSR.FormatReleasesBadPlayer(tag ?? "null"));
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, UnrealPlayer value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        writer.WriteStringValue(value.Tag);
    }
}
