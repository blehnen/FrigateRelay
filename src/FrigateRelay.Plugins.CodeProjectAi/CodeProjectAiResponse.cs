using System.Text.Json.Serialization;

namespace FrigateRelay.Plugins.CodeProjectAi;

/// <summary>JSON shape returned by <c>POST /v1/vision/detection</c>.</summary>
/// <remarks>
/// HTTP status is typically 200 even for application-level failures — the plugin must check
/// <see cref="Success"/>, not just the response status (RESEARCH §1).
/// </remarks>
internal sealed record CodeProjectAiResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("predictions")] IReadOnlyList<CodeProjectAiPrediction>? Predictions,
    [property: JsonPropertyName("code")] int Code);

/// <summary>One detected object from a <c>vision/detection</c> response.</summary>
internal sealed record CodeProjectAiPrediction(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("x_min")] int XMin,
    [property: JsonPropertyName("y_min")] int YMin,
    [property: JsonPropertyName("x_max")] int XMax,
    [property: JsonPropertyName("y_max")] int YMax);
