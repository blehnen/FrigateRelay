using FrigateRelay.Abstractions;
using FrigateRelay.Sources.FrigateMqtt.Payloads;

namespace FrigateRelay.Sources.FrigateMqtt;

/// <summary>
/// Projects a deserialized <see cref="FrigateEvent"/> into a source-agnostic <see cref="EventContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>D5 guard (stationary / false-positive skip).</strong>
/// When <see cref="FrigateEvent.Type"/> is <c>"update"</c> or <c>"end"</c>,
/// events are skipped (return value <see langword="false"/>) if
/// <c>after.stationary == true</c> OR <c>after.false_positive == true</c>.
/// On <c>"new"</c> events these flags are ignored and projection always proceeds.
/// The guard is applied before any allocation so skipped events incur minimal cost.
/// </para>
/// <para>
/// <strong>OQ4 zone union.</strong>
/// <see cref="EventContext.Zones"/> is the deduplicated union of
/// <c>before.current_zones</c>, <c>before.entered_zones</c>,
/// <c>after.current_zones</c>, and <c>after.entered_zones</c>.
/// Case-insensitive deduplication uses <see cref="StringComparer.OrdinalIgnoreCase"/>;
/// original casing from the first occurrence is preserved in the output.
/// </para>
/// </remarks>
internal static class EventContextProjector
{
    /// <summary>
    /// Attempts to project the supplied <paramref name="evt"/> into an <see cref="EventContext"/>.
    /// </summary>
    /// <param name="evt">The deserialized Frigate event. Must not be <see langword="null"/>.</param>
    /// <param name="rawPayload">
    /// The original UTF-8 JSON string from which <paramref name="evt"/> was deserialized.
    /// Stored verbatim in <see cref="EventContext.RawPayload"/>.
    /// </param>
    /// <param name="context">
    /// When this method returns <see langword="true"/>, contains the projected <see cref="EventContext"/>;
    /// otherwise the value is <see langword="default"/> and must not be used by the caller.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the event was projected successfully;
    /// <see langword="false"/> when the D5 guard suppressed the event (no context produced).
    /// </returns>
    internal static bool TryProject(FrigateEvent evt, string rawPayload, out EventContext context)
    {
        // D5 guard: skip update/end if after reports stationary or false_positive.
        // Applied before any allocations for minimal overhead on the hot skip path.
        var after = evt.After;
        var before = evt.Before;

        if (evt.Type is "update" or "end")
        {
            if (after?.Stationary == true || after?.FalsePositive == true)
            {
                context = default!;
                return false;
            }
        }

        // Build zone union from all four arrays (OQ4).
        // HashSet with OrdinalIgnoreCase deduplicates case-insensitively;
        // insertion order is preserved implicitly by iterating arrays in a fixed order.
        var zones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddZones(zones, before?.CurrentZones);
        AddZones(zones, before?.EnteredZones);
        AddZones(zones, after?.CurrentZones);
        AddZones(zones, after?.EnteredZones);

        // Defensive null-coalescing for EventId / Camera / Label / StartedAt.
        // In normal Frigate operation After is always populated; Before/After are nullable
        // only as a defensive measure against future wire-format changes (PLAN-1.1 note).
        var eventId = after?.Id ?? before?.Id ?? Guid.NewGuid().ToString();
        var camera = after?.Camera ?? before?.Camera ?? string.Empty;
        var label = after?.Label ?? before?.Label ?? string.Empty;
        var startTime = after?.StartTime ?? before?.StartTime ?? 0.0;

        context = new EventContext
        {
            EventId = eventId,
            Camera = camera,
            Label = label,
            Zones = zones.Count > 0 ? [.. zones] : Array.Empty<string>(),
            StartedAt = DateTimeOffset.UnixEpoch.AddSeconds(startTime),
            RawPayload = rawPayload,
            // D3 revised: thumbnail is always null in frigate/events MQTT messages.
            // Phase 5 snapshot providers handle HTTP-based snapshot fetching.
            SnapshotFetcher = static _ => ValueTask.FromResult<byte[]?>(null),
        };

        return true;
    }

    private static void AddZones(HashSet<string> target, IReadOnlyList<string>? zones)
    {
        if (zones is null) return;
        foreach (var zone in zones)
        {
            target.Add(zone);
        }
    }
}
