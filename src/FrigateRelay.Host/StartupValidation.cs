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
            foreach (var actionName in sub.Actions)
            {
                if (!registeredNames.Contains(actionName))
                {
                    throw new InvalidOperationException(
                        $"Subscription '{sub.Name}' references unknown action plugin '{actionName}'. " +
                        $"Registered plugins: [{string.Join(", ", registeredNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))}]. " +
                        $"Either register the plugin or remove the reference from appsettings.");
                }
            }
        }
    }
}
