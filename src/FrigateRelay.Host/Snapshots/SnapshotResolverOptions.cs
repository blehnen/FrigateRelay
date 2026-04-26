namespace FrigateRelay.Host.Snapshots;

/// <summary>
/// Configuration options for the snapshot resolution pipeline.
/// Bound from the <c>Snapshots</c> config section by <c>HostBootstrap</c>.
/// </summary>
internal sealed record SnapshotResolverOptions
{
    /// <summary>
    /// Gets the name of the globally-configured default snapshot provider.
    /// Used as the third-tier fallback when neither the per-action nor per-subscription
    /// provider name is set. <see langword="null"/> disables the global tier.
    /// </summary>
    public string? DefaultProviderName { get; init; }

    /// <summary>
    /// Gets the sliding cache TTL for resolved snapshots.
    /// Defaults to 10 seconds — enough to cover the typical Frigate event lifetime
    /// plus retry slack without holding large JPEG payloads in memory indefinitely.
    /// </summary>
    public TimeSpan CacheSlidingTtl { get; init; } = TimeSpan.FromSeconds(10);
}
