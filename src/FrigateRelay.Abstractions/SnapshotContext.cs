namespace FrigateRelay.Abstractions;

/// <summary>
/// A lightweight value type passed to <see cref="IActionPlugin.ExecuteAsync"/> that encapsulates
/// snapshot resolution for a single action invocation.
/// </summary>
/// <remarks>
/// <para>
/// The dispatcher constructs one <see cref="SnapshotContext"/> per dispatch using the two
/// provider-name tiers it already carries: the per-action override and the per-subscription
/// default. The plugin calls <see cref="ResolveAsync"/> without knowing about subscription
/// configuration or the three-tier resolution logic — the resolver handles it.
/// </para>
/// <para>
/// <c>default(SnapshotContext)</c> is safe: <see cref="ResolveAsync"/> short-circuits and returns
/// <see langword="null"/> when no resolver is present. Plugins that do not use snapshots
/// (e.g. BlueIris trigger) accept the parameter and ignore it.
/// </para>
/// </remarks>
public readonly struct SnapshotContext
{
    private readonly ISnapshotResolver? _resolver;

    /// <summary>Gets the per-action snapshot provider name override, if set.</summary>
    public string? PerActionProviderName { get; }

    /// <summary>Gets the per-subscription default snapshot provider name, if set.</summary>
    public string? SubscriptionDefaultProviderName { get; }

    /// <summary>
    /// Initialises a <see cref="SnapshotContext"/> with the resolver and provider name tiers
    /// that the dispatcher determined for this action invocation.
    /// </summary>
    /// <param name="resolver">The host-managed resolver that performs three-tier lookup.</param>
    /// <param name="perActionProviderName">Per-action provider name override; <see langword="null"/> to fall through.</param>
    /// <param name="subscriptionDefaultProviderName">Per-subscription default provider name; <see langword="null"/> to fall through.</param>
    public SnapshotContext(ISnapshotResolver resolver, string? perActionProviderName, string? subscriptionDefaultProviderName)
    {
        _resolver = resolver;
        PerActionProviderName = perActionProviderName;
        SubscriptionDefaultProviderName = subscriptionDefaultProviderName;
    }

    /// <summary>
    /// Attempts to resolve and fetch a snapshot for <paramref name="context"/>.
    /// </summary>
    /// <param name="context">The event for which a snapshot is requested.</param>
    /// <param name="ct">A token that signals the host is shutting down.</param>
    /// <returns>
    /// A <see cref="SnapshotResult"/> on success, or <see langword="null"/> when no provider is
    /// configured or the resolver returns nothing. Never throws for missing-provider cases.
    /// </returns>
    public ValueTask<SnapshotResult?> ResolveAsync(EventContext context, CancellationToken ct)
    {
        if (_resolver is null)
            return ValueTask.FromResult<SnapshotResult?>(null);

        return _resolver.ResolveAsync(context, PerActionProviderName, SubscriptionDefaultProviderName, ct);
    }
}
