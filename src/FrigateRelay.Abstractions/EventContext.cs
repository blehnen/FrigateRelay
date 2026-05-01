namespace FrigateRelay.Abstractions;

/// <summary>
/// An immutable, source-agnostic representation of a single detection event emitted by an event source.
/// No Frigate-specific types appear in this type; source-specific data is either projected into the named
/// properties or preserved verbatim in <see cref="RawPayload"/> for callers that need it.
/// </summary>
public sealed record EventContext
{
    /// <summary>Gets the unique identifier assigned to this event by the originating source.</summary>
    public required string EventId { get; init; }

    /// <summary>Gets the name of the camera that produced this event.</summary>
    public required string Camera { get; init; }

    /// <summary>Gets the detection label (e.g. "person", "car") assigned by the source's object detector.</summary>
    public required string Label { get; init; }

    /// <summary>Gets the list of zone names the detected object occupied at event time. Empty when no zone information is available.</summary>
    public IReadOnlyList<string> Zones { get; init; } = Array.Empty<string>();

    /// <summary>Gets the UTC timestamp at which the event began.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>Gets the opaque, provider-specific JSON payload from which this context was projected. Callers that need fields not surfaced by the named properties can deserialize this string.</summary>
    public required string RawPayload { get; init; }

    /// <summary>
    /// Gets a delegate that lazily fetches the raw snapshot bytes for this event.
    /// The delegate returns <see langword="null"/> when no snapshot is available.
    /// Callers should not invoke this from a hot path; snapshot fetching typically involves an outbound HTTP call.
    /// </summary>
    public required Func<CancellationToken, ValueTask<byte[]?>> SnapshotFetcher { get; init; }

    /// <summary>
    /// Optional alternative camera identifier used when the downstream system (e.g. Blue Iris)
    /// names the camera differently from the originating source. The host's <c>EventPump</c>
    /// populates this per-dispatch from <c>SubscriptionOptions.CameraShortName</c> via a
    /// <c>with</c>-clone before invoking action plugins; sources MUST NOT set this field
    /// (the source-agnostic invariant on <see cref="EventContext"/> is preserved).
    /// </summary>
    /// <remarks>
    /// Resolved by the <c>{camera_shortname}</c> token in <c>EventTokenTemplate</c> with a
    /// fall-through to <see cref="Camera"/> when null. See issue #32 for the legacy-parity
    /// motivation: Blue Iris returns 200 OK on unknown camera names but silently does nothing,
    /// so URL templates that send Frigate's lowercase id to a BI server expecting its own
    /// shortname (e.g. <c>DriveWayHD</c>) appear to succeed but never trigger the recording.
    /// </remarks>
    public string? CameraShortName { get; init; }
}
