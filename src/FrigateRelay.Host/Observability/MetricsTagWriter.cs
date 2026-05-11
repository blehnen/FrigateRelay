using Microsoft.Extensions.Options;

namespace FrigateRelay.Host.Observability;

/// <summary>
/// Normalizes the <c>camera</c> metrics tag value before it enters the
/// <c>DispatcherDiagnostics</c> counter pipeline, bounding time-series cardinality
/// when the operator has configured <see cref="MetricsTagsOptions.KnownCameras"/>.
/// </summary>
/// <remarks>
/// <para>
/// Register as a singleton via <c>services.AddSingleton&lt;MetricsTagWriter&gt;()</c>.
/// The constructor accepts <see cref="IOptionsMonitor{TOptions}"/> so the allowlist
/// set is picked up on any future config reload without a service restart.
/// </para>
/// <para>
/// Normalization rules (see CONTEXT-16 D1 / D3):
/// <list type="bullet">
///   <item><description>The camera value is <see langword="null"/> or empty → returned as-is.</description></item>
///   <item><description><see cref="MetricsTagsOptions.KnownCameras"/> is empty → returned as-is (preserves current behavior for all operators who have not configured the allowlist).</description></item>
///   <item><description>Non-empty allowlist + camera is a member (case-insensitive, <see cref="StringComparer.OrdinalIgnoreCase"/>) → returned as-is (caller's casing preserved).</description></item>
///   <item><description>Non-empty allowlist + camera is NOT a member → returns the literal string <c>"other"</c>.</description></item>
/// </list>
/// </para>
/// <para>
/// <strong>Casing note (CONTEXT-16 D3):</strong> matching is deliberately case-insensitive,
/// diverging from the project-wide convention of case-sensitive name lookups.
/// This is intentional — Frigate camera IDs are sometimes case-inconsistent across the
/// operator's pipeline, and the cardinality protection goal is more important than
/// enforcing case parity. See <c>docs/observability.md</c> for the operator-facing
/// documentation of this divergence.
/// </para>
/// </remarks>
internal sealed class MetricsTagWriter
{
    private readonly IOptionsMonitor<MetricsTagsOptions> _monitor;

    public MetricsTagWriter(IOptionsMonitor<MetricsTagsOptions> monitor)
    {
        _monitor = monitor;
    }

    /// <summary>
    /// Returns <paramref name="camera"/> unchanged if the allowlist is empty or if the
    /// value is a member of the allowlist. Returns <c>"other"</c> for any non-member value
    /// when the allowlist is non-empty.
    /// </summary>
    /// <param name="camera">The camera name tag value from <c>EventContext.Camera</c>.</param>
    public string? NormalizeCameraTag(string? camera)
    {
        if (string.IsNullOrEmpty(camera))
            return camera;

        var known = _monitor.CurrentValue.KnownCameras;
        if (known.Length == 0)
            return camera;

        // Build the HashSet on each call against the current config value.
        // This is intentionally simple for the v1.3.0 scope: the set is small
        // (operator-configured camera names, typically <20) and config reloads
        // are infrequent. A cached-with-OnChange approach can be added later
        // if profiling shows this is a hot path.
        var set = new HashSet<string>(known, StringComparer.OrdinalIgnoreCase);
        return set.Contains(camera) ? camera : "other";
    }
}
