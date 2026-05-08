namespace FrigateRelay.Host.Observability;

/// <summary>
/// Bound from the <c>Otel:MetricsTags</c> configuration section.
/// Controls the camera-tag cardinality allowlist for the FrigateRelay counter pipeline.
/// </summary>
/// <remarks>
/// When <see cref="KnownCameras"/> is empty (the default), all camera values pass through
/// unchanged — preserving the behavior for operators who have not configured this feature.
/// When non-empty, any camera value not in the allowlist is folded to the literal string
/// <c>"other"</c> by <see cref="MetricsTagWriter"/>, bounding cardinality to
/// <c>|KnownCameras| + 1</c> distinct values.
/// </remarks>
internal sealed record MetricsTagsOptions
{
    /// <summary>
    /// The set of camera names whose metric tag values are passed through unchanged.
    /// Matching is case-insensitive (<see cref="StringComparer.OrdinalIgnoreCase"/>).
    /// </summary>
    public string[] KnownCameras { get; init; } = Array.Empty<string>();
}
