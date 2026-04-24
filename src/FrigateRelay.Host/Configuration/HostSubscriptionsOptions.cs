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
public sealed record HostSubscriptionsOptions
{
    /// <summary>
    /// Gets the list of subscription rules. Defaults to an empty array when the configuration
    /// section is absent or contains no entries.
    /// </summary>
    public IReadOnlyList<SubscriptionOptions> Subscriptions { get; init; } = Array.Empty<SubscriptionOptions>();
}
