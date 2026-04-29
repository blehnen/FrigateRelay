namespace FrigateRelay.Host.Configuration;

/// <summary>Represents a named action profile (D5): a flat list of action entries with no nesting or BasedOn inheritance. Subscriptions reference a profile by name via <see cref="SubscriptionOptions.Profile"/>; the host expands the reference into the subscription's effective action list at startup.</summary>
internal sealed record ProfileOptions
{
    /// <summary>Ordered list of actions executed for any subscription that references this profile. Per-action validators and snapshot overrides are carried on each <see cref="ActionEntry"/>.</summary>
    public IReadOnlyList<ActionEntry> Actions { get; init; } = Array.Empty<ActionEntry>();
}
