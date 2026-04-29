namespace FrigateRelay.Abstractions;

/// <summary>
/// Resolves a snapshot for an event using a three-tier provider name lookup:
/// <list type="number">
///   <item><description><b>Per-action override</b> — the action-level provider name, if set.</description></item>
///   <item><description><b>Per-subscription default</b> — the subscription-level provider name, if set.</description></item>
///   <item><description><b>Global default</b> — the host-configured <c>DefaultSnapshotProvider</c> name injected into the implementation.</description></item>
/// </list>
/// The first non-null, non-empty name that matches a registered <see cref="ISnapshotProvider"/> wins.
/// Returns <see langword="null"/> when no tier resolves to a known provider (fail-open: the calling
/// action continues without a snapshot rather than throwing).
/// </summary>
public interface ISnapshotResolver
{
    /// <summary>
    /// Attempts to resolve and fetch a snapshot for <paramref name="context"/>.
    /// </summary>
    /// <param name="context">The event context for which a snapshot is requested.</param>
    /// <param name="perActionProviderName">
    /// The provider name declared on the individual action, or <see langword="null"/> if not overridden.
    /// </param>
    /// <param name="subscriptionDefaultProviderName">
    /// The provider name declared as the subscription-level default, or <see langword="null"/> if not set.
    /// </param>
    /// <param name="cancellationToken">A token that signals the host is shutting down.</param>
    /// <returns>
    /// A <see cref="SnapshotResult"/> when a provider successfully fetches the image, or
    /// <see langword="null"/> when no tier resolves or the provider returns no image.
    /// </returns>
    ValueTask<SnapshotResult?> ResolveAsync(
        EventContext context,
        string? perActionProviderName,
        string? subscriptionDefaultProviderName,
        CancellationToken cancellationToken);
}
