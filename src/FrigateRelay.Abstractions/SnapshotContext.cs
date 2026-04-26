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
/// <para>
/// <strong>Pre-resolved variant (Phase 7 D1, RESEARCH §5).</strong> When a dispatch has validators,
/// the dispatcher resolves the snapshot ONCE up front, then constructs a second
/// <see cref="SnapshotContext"/> via <see cref="SnapshotContext(SnapshotResult?)"/> that returns
/// the cached result without re-invoking the resolver. This guarantees the underlying provider's
/// HTTP fetch happens at most once even when the validator chain and the action both read the
/// snapshot. The two ctors are mutually exclusive — a value built from one path will never silently
/// fall through to the other.
/// </para>
/// </remarks>
public readonly struct SnapshotContext
{
    private readonly ISnapshotResolver? _resolver;
    private readonly SnapshotResult? _preResolved;
    private readonly bool _hasPreResolved;

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
        _preResolved = null;
        _hasPreResolved = false;
    }

    /// <summary>
    /// Initialises a <see cref="SnapshotContext"/> wrapping a pre-resolved <see cref="SnapshotResult"/>
    /// (or <see langword="null"/> when upstream resolution returned nothing). Used by the dispatcher
    /// to share ONE resolved snapshot across the validator chain and the action plugin so the
    /// underlying provider's HTTP fetch happens at most once per dispatch.
    /// </summary>
    /// <param name="preResolved">The cached snapshot result, or <see langword="null"/> for "explicitly nothing."</param>
    /// <remarks>
    /// The <see cref="_hasPreResolved"/> flag distinguishes "explicitly null pre-resolved" from
    /// <c>default(SnapshotContext)</c> (which has no resolver and no flag set, and also returns null)
    /// — both observable behaviors are identical from the caller's perspective, but the flag prevents
    /// any future resolver-fallback logic from accidentally firing when a pre-resolved null was intended.
    /// </remarks>
    public SnapshotContext(SnapshotResult? preResolved)
    {
        _resolver = null;
        PerActionProviderName = null;
        SubscriptionDefaultProviderName = null;
        _preResolved = preResolved;
        _hasPreResolved = true;
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
        if (_hasPreResolved)
            return ValueTask.FromResult(_preResolved);

        if (_resolver is null)
            return ValueTask.FromResult<SnapshotResult?>(null);

        return _resolver.ResolveAsync(context, PerActionProviderName, SubscriptionDefaultProviderName, ct);
    }
}
