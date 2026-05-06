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
/// <remarks>
/// <see cref="Label"/> is nullable because System.Text.Json does not enforce non-nullable
/// annotations on positional record parameters (<c>RespectNullableAnnotations</c> is off by
/// default). A missing or null <c>class</c> field on the wire would otherwise deserialize as
/// null and crash the AllowedLabels comparison. The validator skips predictions with a
/// null/empty label.
/// </remarks>
internal sealed record RoboflowPrediction(
    [property: JsonPropertyName("class")] string? Label,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("class_id")] int? ClassId,
    [property: JsonPropertyName("x")] double X,
    [property: JsonPropertyName("y")] double Y,
    [property: JsonPropertyName("width")] double Width,
    [property: JsonPropertyName("height")] double Height);

/// <summary>Request body sent to <c>POST /infer/object_detection</c>.</summary>
/// <remarks>
/// <see cref="ApiKey"/> is null when unset so STJ omits the <c>api_key</c> field entirely
/// rather than sending an empty string. Roboflow's `ObjectDetectionInferenceRequest` schema
/// treats `api_key` as nullable; the validator constructs this with `null` when
/// <see cref="RoboflowOptions.ApiKey"/> is empty.
/// </remarks>
internal sealed record RoboflowRequest(
    [property: JsonPropertyName("model_id")] string ModelId,
    [property: JsonPropertyName("image")] RoboflowRequestImage Image,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("api_key"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ApiKey = null);

/// <summary>Image payload within <see cref="RoboflowRequest"/>.</summary>
internal sealed record RoboflowRequestImage(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("value")] string Value);
