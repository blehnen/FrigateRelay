using System.Text.Json.Serialization;

namespace FrigateRelay.Plugins.Pushover;

/// <summary>Represents the JSON response from the Pushover messages API.</summary>
internal sealed record PushoverResponse(
    [property: JsonPropertyName("status")] int Status,
    [property: JsonPropertyName("request")] string? Request,
    [property: JsonPropertyName("errors")] IReadOnlyList<string>? Errors);
