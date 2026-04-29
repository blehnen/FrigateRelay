namespace FrigateRelay.Sources.FrigateMqtt.Payloads;

/// <summary>
/// Top-level Frigate MQTT event payload published on the <c>frigate/events</c> topic.
/// Contains the event type and before/after snapshots of the detected object.
/// </summary>
internal sealed record FrigateEvent
{
    /// <summary>
    /// The event type: <c>"new"</c>, <c>"update"</c>, or <c>"end"</c>.
    /// <list type="bullet">
    ///   <item><term>new</term><description>First message when the object is no longer a false positive.</description></item>
    ///   <item><term>update</term><description>Better snapshot found, zone change, or frame update.</description></item>
    ///   <item><term>end</term><description>Final message; <see cref="FrigateEventObject.EndTime"/> is now set.</description></item>
    /// </list>
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// The state of the detected object before this event update. Defensively nullable —
    /// in practice Frigate populates this on every event, but a null-by-spec future wire
    /// change would otherwise silently produce a NullReferenceException when the projector reads it.
    /// </summary>
    public FrigateEventObject? Before { get; init; }

    /// <summary>
    /// The state of the detected object after this event update. Defensively nullable, same rationale as <see cref="Before"/>.
    /// </summary>
    public FrigateEventObject? After { get; init; }
}
