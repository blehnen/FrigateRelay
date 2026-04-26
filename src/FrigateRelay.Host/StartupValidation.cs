using FrigateRelay.Abstractions;
using FrigateRelay.Host.Configuration;

namespace FrigateRelay.Host;

/// <summary>
/// Startup validation helpers called from <c>Program.cs</c> before <c>app.RunAsync()</c>.
/// Extracted so tests can exercise validation logic without spinning up the full host.
/// </summary>
internal static class StartupValidation
{
    /// <summary>
    /// Verifies that every action name referenced by a subscription is registered as an
    /// <see cref="IActionPlugin"/> in the DI container. Throws <see cref="InvalidOperationException"/>
    /// on the first unknown name, listing the subscription, the unknown name, and all registered names
    /// (PROJECT.md S2 + CONTEXT-4 D2).
    /// </summary>
    public static void ValidateActions(
        IEnumerable<SubscriptionOptions> subscriptions,
        IEnumerable<IActionPlugin> actionPlugins)
    {
        var registeredNames = actionPlugins
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var sub in subscriptions)
        {
            foreach (var entry in sub.Actions)
            {
                if (!registeredNames.Contains(entry.Plugin))
                {
                    throw new InvalidOperationException(
                        $"Subscription '{sub.Name}' references unknown action plugin '{entry.Plugin}'. " +
                        $"Registered plugins: [{string.Join(", ", registeredNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))}]. " +
                        $"Either register the plugin or remove the reference from appsettings.");
                }
            }
        }
    }

    /// <summary>
    /// Verifies that every snapshot provider name referenced by configuration (the global
    /// <c>Snapshots:DefaultProviderName</c>, every subscription's <c>DefaultSnapshotProvider</c>,
    /// and every <see cref="ActionEntry.SnapshotProvider"/> override) is registered as an
    /// <see cref="ISnapshotProvider"/> in the DI container. Throws <see cref="InvalidOperationException"/>
    /// on the first unknown name, listing the referencing site and all registered providers (PROJECT.md S2).
    /// </summary>
    public static void ValidateSnapshotProviders(
        IEnumerable<SubscriptionOptions> subscriptions,
        string? globalDefaultProviderName,
        IEnumerable<ISnapshotProvider> snapshotProviders)
    {
        var registeredNames = snapshotProviders
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrEmpty(globalDefaultProviderName) && !registeredNames.Contains(globalDefaultProviderName))
        {
            throw new InvalidOperationException(
                $"Global Snapshots:DefaultProviderName '{globalDefaultProviderName}' is not a registered snapshot provider. " +
                $"Registered providers: [{string.Join(", ", registeredNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))}]. " +
                $"Either register the provider or remove the reference from appsettings.");
        }

        foreach (var sub in subscriptions)
        {
            if (!string.IsNullOrEmpty(sub.DefaultSnapshotProvider) && !registeredNames.Contains(sub.DefaultSnapshotProvider))
            {
                throw new InvalidOperationException(
                    $"Subscription '{sub.Name}' references unknown snapshot provider '{sub.DefaultSnapshotProvider}' " +
                    $"as its DefaultSnapshotProvider. Registered providers: " +
                    $"[{string.Join(", ", registeredNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))}].");
            }

            foreach (var entry in sub.Actions)
            {
                if (!string.IsNullOrEmpty(entry.SnapshotProvider) && !registeredNames.Contains(entry.SnapshotProvider))
                {
                    throw new InvalidOperationException(
                        $"Subscription '{sub.Name}' action '{entry.Plugin}' references unknown snapshot provider " +
                        $"'{entry.SnapshotProvider}'. Registered providers: " +
                        $"[{string.Join(", ", registeredNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))}].");
                }
            }
        }
    }
}
