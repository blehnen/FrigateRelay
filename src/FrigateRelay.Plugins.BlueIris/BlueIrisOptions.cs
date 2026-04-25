namespace FrigateRelay.Plugins.BlueIris;

/// <summary>
/// Configuration options for the BlueIris action plugin.
/// </summary>
public sealed record BlueIrisOptions
{
    /// <summary>Trigger URL template with allowlisted placeholders {camera} {label} {event_id} {zone} (CONTEXT-4 D3).</summary>
    public required string TriggerUrlTemplate { get; init; }

    /// <summary>When true, the BlueIris HttpClient skips TLS validation. Per-plugin opt-in only (CLAUDE.md invariant). Default false.</summary>
    public bool AllowInvalidCertificates { get; init; }

    /// <summary>HttpClient timeout for the trigger request. Default 10s.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Optional override for the dispatcher channel capacity. When null, dispatcher default (256) is used.</summary>
    public int? QueueCapacity { get; init; }
}
