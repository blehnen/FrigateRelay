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
    /// </summary>
    internal static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
