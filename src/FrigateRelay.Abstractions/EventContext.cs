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
}
