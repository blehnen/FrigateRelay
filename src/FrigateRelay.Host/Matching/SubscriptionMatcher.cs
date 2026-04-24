using FrigateRelay.Abstractions;
using FrigateRelay.Host.Configuration;

namespace FrigateRelay.Host.Matching;

/// <summary>
/// Pure, stateless matcher that filters a list of <see cref="SubscriptionOptions"/> against an
/// <see cref="EventContext"/>. Returns <em>all</em> matching subscriptions in configured order (D1).
/// </summary>
/// <remarks>
/// <para>
/// This class implements Decision D1: every subscription that matches the event fires — there is no
/// early exit after the first match. Overlapping subscriptions are intentional and each has its own
/// dedupe bucket in <c>DedupeCache</c>.
/// </para>
/// <para>
/// <strong>D5 contract:</strong> the stationary/false_positive guard is the source's responsibility.
/// The FrigateMqtt plugin's projector must strip stationary or false-positive <c>update</c>/<c>end</c>
/// events before they reach the channel. This matcher only ever sees post-D5 events and does not
/// inspect those flags.
/// </para>
/// </remarks>
public static class SubscriptionMatcher
{
    /// <summary>
    /// Returns all subscriptions from <paramref name="subs"/> that match <paramref name="context"/>.
    /// </summary>
    /// <param name="context">The source-agnostic event to match against.</param>
    /// <param name="subs">The full list of configured subscriptions, evaluated in order.</param>
    /// <returns>
    /// An <see cref="IReadOnlyList{T}"/> containing every matching subscription, preserving
    /// the order from <paramref name="subs"/>. Returns an empty list when nothing matches.
    /// </returns>
    public static IReadOnlyList<SubscriptionOptions> Match(
        EventContext context,
        IReadOnlyList<SubscriptionOptions> subs)
    {
        List<SubscriptionOptions>? matches = null;

        foreach (var sub in subs)
        {
            if (!string.Equals(sub.Camera, context.Camera, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.Equals(sub.Label, context.Label, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.IsNullOrEmpty(sub.Zone) &&
                !context.Zones.Contains(sub.Zone, StringComparer.OrdinalIgnoreCase))
                continue;

            (matches ??= []).Add(sub);
        }

        return (IReadOnlyList<SubscriptionOptions>?)matches ?? Array.Empty<SubscriptionOptions>();
    }
}
