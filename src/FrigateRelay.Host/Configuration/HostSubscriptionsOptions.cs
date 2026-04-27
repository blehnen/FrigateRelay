using FrigateRelay.Host.Snapshots;

namespace FrigateRelay.Host.Configuration;

/// <summary>
/// Bound from the top-level <c>Subscriptions</c> array in <c>appsettings.json</c>.
/// Host-level (not plugin-specific) so any <c>IEventSource</c> is filtered through these rules.
/// </summary>
/// <remarks>
/// Registration in <c>Program.cs</c> (PLAN-3.1):
/// <code>services.Configure&lt;HostSubscriptionsOptions&gt;(builder.Configuration);</code>
/// A missing or empty <c>Subscriptions</c> section yields an empty list — never <see langword="null"/>.
/// </remarks>
internal sealed record HostSubscriptionsOptions
{
    /// <summary>
    /// Gets the named profiles dictionary. Bound from the top-level <c>Profiles</c> config key.
    /// Profiles are a flat dictionary of action-list shapes (D5): no BasedOn, no nesting.
    /// Subscriptions reference a profile by name via <see cref="SubscriptionOptions.Profile"/>.
    /// </summary>
    public IReadOnlyDictionary<string, ProfileOptions> Profiles { get; init; } =
        new Dictionary<string, ProfileOptions>();

    /// <summary>
    /// Gets the list of subscription rules. Defaults to an empty array when the configuration
    /// section is absent or contains no entries.
    /// </summary>
    public IReadOnlyList<SubscriptionOptions> Subscriptions { get; init; } = Array.Empty<SubscriptionOptions>();

    /// <summary>
    /// Gets the snapshot resolution options (global default provider name + cache TTL).
    /// Bound from the <c>Snapshots</c> config section. Defaults to a zero-configuration
    /// instance (no global default, 10-second sliding TTL).
    /// </summary>
    public SnapshotResolverOptions Snapshots { get; init; } = new();
}
