using System.Text.Json;
using System.Text.Json.Serialization;

namespace FrigateRelay.Sources.FrigateMqtt;

/// <summary>
/// Shared <see cref="JsonSerializerOptions"/> for deserializing Frigate MQTT event payloads.
/// Uses <see cref="JsonNamingPolicy.SnakeCaseLower"/> to map wire-format snake_case field names
/// to PascalCase .NET properties without per-property <c>[JsonPropertyName]</c> attributes.
/// </summary>
internal static class FrigateJsonOptions
{
    /// <summary>
    /// The singleton options instance used for all Frigate payload serialization and deserialization.
    /// Settings: snake_case lower naming policy, case-insensitive property matching, ignore null on write.
    /// <para>
    /// Sealed via <see cref="JsonSerializerOptions.MakeReadOnly()"/> after construction so downstream
    /// callers cannot silently mutate the shared instance.
    /// </para>
    /// </summary>
    internal static readonly JsonSerializerOptions Default = CreateReadOnly();

    private static JsonSerializerOptions CreateReadOnly()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        // populateMissingResolver: true lets MakeReadOnly auto-install a DefaultJsonTypeInfoResolver
        // when none is configured. Without this, .NET 10 throws "JsonSerializerOptions instance must
        // specify a TypeInfoResolver setting before being marked as read-only."
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
