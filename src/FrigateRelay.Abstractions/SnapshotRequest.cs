namespace FrigateRelay.Abstractions;

/// <summary>
/// Carries the parameters for a snapshot fetch operation issued to a snapshot provider.
/// </summary>
public sealed record SnapshotRequest
{
    /// <summary>Gets the event context for which a snapshot is being requested.</summary>
    public required EventContext Context { get; init; }

    /// <summary>Gets the name of the snapshot provider that should handle this request, or <see langword="null"/> to use the resolution-order default.</summary>
    public string? ProviderName { get; init; }

    /// <summary>Gets a value indicating whether the provider should overlay bounding-box annotations on the returned image, if supported.</summary>
    public bool IncludeBoundingBox { get; init; }
}
