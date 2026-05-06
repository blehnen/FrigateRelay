using System.Text.Json.Serialization;

namespace FrigateRelay.Plugins.Doods2;

/// <summary>JSON shape returned by <c>POST /detect</c> (HTTP transport).</summary>
/// <remarks>
/// Confidence values are in the 0–100 range (e.g. <c>87.4</c> for 87.4% confidence).
/// The validator normalizes by dividing by 100 before comparing to
/// <see cref="Doods2Options.MinConfidence"/>.
/// </remarks>
internal sealed record Doods2HttpResponse(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("detections")] IReadOnlyList<Doods2Detection>? Detections);

/// <summary>One detected object from a <c>/detect</c> response.</summary>
/// <remarks>
/// <see cref="Label"/> is nullable because System.Text.Json does not enforce non-nullable
/// annotations on positional record parameters (<c>RespectNullableAnnotations</c> is off by
/// default). A missing or null <c>label</c> field on the wire would otherwise deserialize as
/// null and crash the AllowedLabels comparison. The validator skips detections with a
/// null/empty label.
/// </remarks>
internal sealed record Doods2Detection(
    [property: JsonPropertyName("top")] float Top,
    [property: JsonPropertyName("left")] float Left,
    [property: JsonPropertyName("bottom")] float Bottom,
    [property: JsonPropertyName("right")] float Right,
    [property: JsonPropertyName("label")] string? Label,
    [property: JsonPropertyName("confidence")] double Confidence); // 0-100 scale per DOODS2; validator normalizes

/// <summary>Request body sent to <c>POST /detect</c> (HTTP transport).</summary>
internal sealed record Doods2HttpRequest(
    [property: JsonPropertyName("detector_name")] string DetectorName,
    [property: JsonPropertyName("data")] string Data,
    [property: JsonPropertyName("detect")] Dictionary<string, double> Detect);
