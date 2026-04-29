namespace FrigateRelay.Host.Configuration;

/// <summary>
/// Expands <see cref="SubscriptionOptions.Profile"/> references into fully-resolved
/// subscriptions before downstream validators and the dispatcher see the list.
/// </summary>
/// <remarks>
/// Per D1, every subscription must declare exactly one of <c>Profile</c> or <c>Actions</c>
/// (XOR). Setting both or neither is accumulated as an error. Per D5, profiles are a flat
/// dictionary — no nesting, no <c>BasedOn</c>, no cycle-detection needed.
/// </remarks>
internal static class ProfileResolver
{
    /// <summary>
    /// Resolves all profile references in <paramref name="options"/> and returns the
    /// effective, fully-expanded subscription list. Errors are accumulated in
    /// <paramref name="errors"/>; this method never throws.
    /// </summary>
    /// <param name="options">The bound host subscriptions options (contains both
    /// <c>Profiles</c> and <c>Subscriptions</c>).</param>
    /// <param name="errors">Accumulator list; errors are appended (never cleared).</param>
    /// <returns>
    /// The resolved subscription list. Subscriptions that fail validation (mutex violation,
    /// undefined profile) are omitted from the result so downstream validators do not
    /// produce cascading false-positive errors.
    /// </returns>
    internal static IReadOnlyList<SubscriptionOptions> Resolve(
        HostSubscriptionsOptions options,
        List<string> errors)
    {
        var resolved = new List<SubscriptionOptions>(options.Subscriptions.Count);

        foreach (var sub in options.Subscriptions)
        {
            bool hasProfile = sub.Profile is not null;
            bool hasActions = sub.Actions.Count > 0;

            if (hasProfile && hasActions)
            {
                // D1: mutex violation — both set.
                errors.Add(
                    $"Subscription '{sub.Name}' may declare either 'Profile' or 'Actions', not both.");
                continue;
            }

            if (!hasProfile && !hasActions)
            {
                // D1: mutex violation — neither set.
                errors.Add(
                    $"Subscription '{sub.Name}' must declare either 'Profile' or 'Actions'.");
                continue;
            }

            if (hasProfile)
            {
                var profileName = sub.Profile!;
                if (!options.Profiles.TryGetValue(profileName, out var profileOptions))
                {
                    var defined = string.Join(", ",
                        options.Profiles.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase));
                    errors.Add(
                        $"Subscription '{sub.Name}' references undefined profile '{profileName}'. " +
                        $"Defined profiles: [{defined}].");
                    continue;
                }

                // Emit a clone with the profile actions expanded and Profile cleared so
                // downstream code sees only the resolved action list.
                resolved.Add(sub with
                {
                    Actions = profileOptions.Actions,
                    Profile = null,
                });
                continue;
            }

            // Inline actions — emit unchanged.
            resolved.Add(sub);
        }

        return resolved.AsReadOnly();
    }
}
