using System.Text.Json;
using System.Text.Json.Serialization;

namespace FrigateRelay.Host.Configuration;

/// <summary>
/// A dual-form <see cref="JsonConverter{T}"/> for <see cref="ActionEntry"/> that accepts:
/// <list type="bullet">
///   <item><description>String form: <c>"BlueIris"</c> → <c>new ActionEntry("BlueIris")</c></description></item>
///   <item><description>Object form: <c>{"Plugin":"BlueIris","SnapshotProvider":"Frigate"}</c></description></item>
///   <item><description>Mixed arrays combining both forms are supported at the array level.</description></item>
/// </list>
/// This preserves backward compatibility with Phase 4 fixtures and live config files.
/// </summary>
public sealed class ActionEntryJsonConverter : JsonConverter<ActionEntry>
{
    /// <inheritdoc />
    public override ActionEntry Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var plugin = reader.GetString()
                ?? throw new JsonException("ActionEntry string form must not be null.");
            return new ActionEntry(plugin);
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            var dto = JsonSerializer.Deserialize<ActionEntryDto>(ref reader, options)
                ?? throw new JsonException("ActionEntry object form deserialized to null.");
            if (string.IsNullOrEmpty(dto.Plugin))
                throw new JsonException("ActionEntry object form requires a non-empty 'Plugin' field.");
            return new ActionEntry(dto.Plugin, dto.SnapshotProvider);
        }

        throw new JsonException(
            $"Expected a string or an object with a 'Plugin' field for ActionEntry, but got {reader.TokenType}.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, ActionEntry value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("Plugin", value.Plugin);
        if (value.SnapshotProvider is not null)
            writer.WriteString("SnapshotProvider", value.SnapshotProvider);
        writer.WriteEndObject();
    }

    // Private DTO used only for object-form deserialization to avoid recursive converter invocation.
    private sealed record ActionEntryDto(string Plugin, string? SnapshotProvider = null);
}
