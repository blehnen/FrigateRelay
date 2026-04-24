namespace FrigateRelay.Sources.FrigateMqtt.Payloads;

/// <summary>
/// Represents a single detected object snapshot within a Frigate MQTT event.
/// Used for both the <c>before</c> and <c>after</c> fields of <see cref="FrigateEvent"/>
/// — the schema is identical for both.
/// </summary>
/// <remarks>
/// Wire format is snake_case JSON; deserialized via <see cref="FrigateJsonOptions.Default"/>
/// which applies <c>JsonNamingPolicy.SnakeCaseLower</c> globally.
/// </remarks>
internal sealed record FrigateEventObject
{
    /// <summary>
    /// Stable unique identifier for this detection across all messages for the same event.
    /// Format: <c>"{start_time}-{random}"</c> (e.g. <c>"1714000001.123456-abc123"</c>).
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The camera name as configured in Frigate.
    /// </summary>
    public required string Camera { get; init; }

    /// <summary>
    /// The detected object label (e.g. <c>"person"</c>, <c>"car"</c>).
    /// </summary>
    public required string Label { get; init; }

    /// <summary>
    /// Optional recognized sub-label or attribute (e.g. a recognized face name).
    /// Null when not present.
    /// </summary>
    public string? SubLabel { get; init; }

    /// <summary>
    /// Current detection confidence score in the range 0.0–1.0.
    /// </summary>
    public double Score { get; init; }

    /// <summary>
    /// Highest confidence score recorded across all frames for this detection.
    /// </summary>
    public double TopScore { get; init; }

    /// <summary>
    /// Unix epoch timestamp (seconds, floating-point) when this detection started.
    /// Convert with <c>DateTimeOffset.UnixEpoch.AddSeconds(StartTime)</c>.
    /// </summary>
    public double StartTime { get; init; }

    /// <summary>
    /// Unix epoch timestamp (seconds, floating-point) when this detection ended.
    /// Null until the event <c>type</c> is <c>"end"</c>.
    /// </summary>
    public double? EndTime { get; init; }

    /// <summary>
    /// True when the object's movement has stopped and it is considered stationary.
    /// On <c>update</c> and <c>end</c> events, stationary objects are skipped by the
    /// subscription matcher (stationary guard, D5).
    /// </summary>
    public bool Stationary { get; init; }

    /// <summary>
    /// True when the object is actively moving. Inverse of <see cref="Stationary"/>.
    /// </summary>
    public bool Active { get; init; }

    /// <summary>
    /// True when Frigate has classified this detection as a false positive.
    /// On <c>update</c> and <c>end</c> events, false positive detections are skipped
    /// by the subscription matcher (false-positive guard, D5).
    /// </summary>
    public bool FalsePositive { get; init; }

    /// <summary>
    /// Zone names that the object is currently inside. Empty array when no zones are active.
    /// </summary>
    public IReadOnlyList<string> CurrentZones { get; init; } = [];

    /// <summary>
    /// All zone names the object has entered during its detection lifetime.
    /// Empty array until the object enters a configured zone.
    /// </summary>
    public IReadOnlyList<string> EnteredZones { get; init; } = [];

    /// <summary>
    /// True when Frigate has saved a snapshot image for this detection.
    /// </summary>
    public bool HasSnapshot { get; init; }

    /// <summary>
    /// True when a video clip exists for this detection.
    /// </summary>
    public bool HasClip { get; init; }

    /// <summary>
    /// Always <c>null</c> in published <c>frigate/events</c> MQTT messages per Frigate documentation.
    /// Thumbnails are published on separate per-camera topics; Phase 5 snapshot providers handle fetching.
    /// </summary>
    public string? Thumbnail { get; init; }

    /// <summary>
    /// Unix epoch timestamp (seconds, floating-point) of the captured frame for this snapshot.
    /// </summary>
    public double FrameTime { get; init; }
}
