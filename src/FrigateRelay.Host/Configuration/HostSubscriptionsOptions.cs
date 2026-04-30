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
    /// <remarks>
    /// Settable (not <c>init</c>) so the <c>IPostConfigureOptions</c> registered in
    /// <c>HostBootstrap</c> can replace the bound list with the profile-resolved version
    /// (Profile references expanded into concrete Actions). Without that mutation,
    /// runtime consumers see the raw bound list where <see cref="SubscriptionOptions.Profile"/>
    /// is set and <see cref="SubscriptionOptions.Actions"/> is empty, which causes the
    /// dispatcher's per-action loop to silently no-op.
    /// </remarks>
    public IReadOnlyList<SubscriptionOptions> Subscriptions { get; set; } = Array.Empty<SubscriptionOptions>();
}
