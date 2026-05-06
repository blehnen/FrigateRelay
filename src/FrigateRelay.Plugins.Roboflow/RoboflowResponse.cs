using System.Text.Json.Serialization;

namespace FrigateRelay.Plugins.Roboflow;

/// <summary>JSON shape returned by <c>POST /infer/object_detection</c>.</summary>
/// <remarks>
/// Confidence values are in the 0.0–1.0 range (e.g. <c>0.92</c> for 92% confidence).
/// No normalization is required — compare directly to <see cref="RoboflowOptions.MinConfidence"/>.
/// </remarks>
internal sealed record RoboflowResponse(
    [property: JsonPropertyName("image")] RoboflowImage? Image,
    [property: JsonPropertyName("predictions")] IReadOnlyList<RoboflowPrediction>? Predictions,
    [property: JsonPropertyName("time")] double Time);

/// <summary>Image dimensions from a <c>/infer/object_detection</c> response.</summary>
internal sealed record RoboflowImage(
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height);

/// <summary>One detected object from a <c>/infer/object_detection</c> response.</summary>
internal sealed record RoboflowPrediction(
    [property: JsonPropertyName("class")] string Label,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("class_id")] int? ClassId,
    [property: JsonPropertyName("x")] double X,
    [property: JsonPropertyName("y")] double Y,
    [property: JsonPropertyName("width")] double Width,
    [property: JsonPropertyName("height")] double Height);

/// <summary>Request body sent to <c>POST /infer/object_detection</c>.</summary>
internal sealed record RoboflowRequest(
    [property: JsonPropertyName("model_id")] string ModelId,
    [property: JsonPropertyName("image")] RoboflowRequestImage Image,
    [property: JsonPropertyName("confidence")] double Confidence);

/// <summary>Image payload within <see cref="RoboflowRequest"/>.</summary>
internal sealed record RoboflowRequestImage(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("value")] string Value);
