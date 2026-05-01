namespace FrigateRelay.Host.Configuration;

/// <summary>
/// Represents a single subscription rule bound from the top-level <c>Subscriptions</c> array in
/// <c>appsettings.json</c>. Each subscription describes which camera/label/zone combination triggers
/// an action and the per-subscription cooldown that prevents repeated firing.
/// </summary>
/// <remarks>
/// Matching semantics:
/// <list type="bullet">
///   <item><description><see cref="Camera"/> and <see cref="Label"/> are compared case-insensitively (OrdinalIgnoreCase).</description></item>
///   <item><description>When <see cref="Zone"/> is <see langword="null"/> or empty, the subscription matches any event regardless of zone.</description></item>
///   <item><description>When <see cref="Zone"/> is non-empty, the event's <c>EventContext.Zones</c> list must contain it (case-insensitive).</description></item>
/// </list>
/// The D5 stationary/false_positive guard is applied by the source (e.g. the FrigateMqtt plugin's projector)
/// <em>before</em> events reach the matcher — this record has no knowledge of those flags.
/// </remarks>
internal sealed record SubscriptionOptions
{
    /// <summary>Gets the unique name of this subscription, used as the dedupe-cache bucket key.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the camera name to match (case-insensitive).</summary>
    public required string Camera { get; init; }

    /// <summary>
    /// Optional alternative camera identifier surfaced through the <c>{camera_shortname}</c>
    /// token in plugin URL/message templates. When <see langword="null"/>, <c>{camera_shortname}</c>
    /// falls through to <see cref="Camera"/>.
    /// </summary>
    /// <remarks>
    /// Restores legacy <c>FrigateMQTTProcessingService.CameraShortName</c> semantics (issue #32).
    /// Use when the downstream system (e.g. Blue Iris) names this camera differently from the
    /// originating source — e.g. Frigate id <c>"driveway"</c> vs Blue Iris shortname
    /// <c>"DriveWayHD"</c>. The dedupe-cache and matcher continue to key on <see cref="Camera"/>;
    /// only template substitution sees this value.
    /// </remarks>
    public string? CameraShortName { get; init; }

    /// <summary>Gets the detection label to match (e.g. <c>"person"</c>, <c>"car"</c>) (case-insensitive).</summary>
    public required string Label { get; init; }

    /// <summary>
    /// Gets the zone name to match. When <see langword="null"/> or empty the subscription matches
    /// events in any zone (or events that carry no zone information).
    /// </summary>
    public string? Zone { get; init; }

    /// <summary>
    /// Gets the cooldown duration in seconds. Repeated firings of the same (subscription, camera, label)
    /// combination within this window are suppressed by <c>DedupeCache</c>.
    /// Defaults to 60 seconds.
    /// </summary>
    public int CooldownSeconds { get; init; } = 60;

    /// <summary>
    /// Gets the name of the profile to use for this subscription's action list.
    /// Mutually exclusive with <see cref="Actions"/> (D1): setting both is a startup
    /// configuration error; setting neither is also a startup configuration error.
    /// When set, the effective action list is resolved from <c>Profiles[Profile]</c>
    /// by the profile resolution validator before the dispatcher sees the subscription.
    /// </summary>
    public string? Profile { get; init; }

    /// <summary>
    /// Gets the list of action entries that fire for this subscription. Empty (default)
    /// means no actions fire — fail-safe per CONTEXT-4 D2. Unknown plugin names cause startup
    /// failure (PROJECT.md S2). Plugin name match is case-insensitive ordinal.
    /// Each entry may carry an optional per-action snapshot provider override.
    /// </summary>
    public IReadOnlyList<ActionEntry> Actions { get; init; } = Array.Empty<ActionEntry>();

    /// <summary>
    /// Gets the default snapshot provider name for all actions in this subscription.
    /// When <see langword="null"/>, the global default from <c>Snapshots:DefaultProviderName</c> is used.
    /// Per-action <see cref="ActionEntry.SnapshotProvider"/> takes precedence over this value.
    /// </summary>
    public string? DefaultSnapshotProvider { get; init; }
}
