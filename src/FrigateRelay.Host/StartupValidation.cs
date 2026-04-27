using FrigateRelay.Abstractions;
using FrigateRelay.Host.Configuration;
using FrigateRelay.Host.Snapshots;
using Microsoft.Extensions.Options;

namespace FrigateRelay.Host;

/// <summary>
/// Startup validation helpers called from <c>Program.cs</c> before <c>app.RunAsync()</c>.
/// Extracted so tests can exercise validation logic without spinning up the full host.
/// </summary>
internal static class StartupValidation
{
    /// <summary>
    /// Runs the full collect-all startup validation pipeline in the correct order:
    /// profile resolution → action-plugin existence → snapshot-provider existence →
    /// per-action validator existence. All passes share a single error accumulator;
    /// if any errors are present after all passes, a single aggregated
    /// <see cref="InvalidOperationException"/> is thrown whose message lists every
    /// error on its own indented line so operators see all misconfigurations at once
    /// (D7 — collect-all retrofit).
    /// </summary>
    /// <param name="services">The built <see cref="IServiceProvider"/>.</param>
    /// <param name="options">The bound <see cref="HostSubscriptionsOptions"/>.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown once (not per-error) when any validation error is detected.
    /// Message starts with <c>"Startup configuration invalid:"</c>.
    /// </exception>
    internal static void ValidateAll(IServiceProvider services, HostSubscriptionsOptions options)
    {
        var errors = new List<string>();

        // Pass 0 — observability endpoint URI validation (D2 fail-fast on malformed endpoint).
        // GetService (not GetRequired) so unit tests that build a minimal ServiceCollection
        // without IConfiguration still exercise passes 1-4 without failure.
        var configuration = services.GetService<IConfiguration>();
        if (configuration is not null)
            ValidateObservability(configuration, errors);

        // Pass 1 — profile resolution (D1 mutex + undefined-profile guard).
        var resolved = ProfileResolver.Resolve(options, errors);

        // Pass 2 — action-plugin existence.
        var actionPlugins = services.GetRequiredService<IEnumerable<IActionPlugin>>();
        ValidateActions(resolved, actionPlugins, errors);

        // Pass 3 — snapshot-provider existence (global default + per-sub + per-action).
        var snapshotProviders = services.GetRequiredService<IEnumerable<ISnapshotProvider>>();
        var snapshotOpts = services.GetService<IOptions<SnapshotResolverOptions>>()?.Value;
        ValidateSnapshotProviders(resolved, snapshotOpts?.DefaultProviderName, snapshotProviders, errors);

        // Pass 4 — per-action validator key resolution.
        ValidateValidators(resolved, services, errors);

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Startup configuration invalid:\n  - " + string.Join("\n  - ", errors));
        }
    }

    /// <summary>
    /// Validates observability endpoint URIs. A non-empty <c>Otel:OtlpEndpoint</c> or
    /// <c>Serilog:Seq:ServerUrl</c> that is not a valid absolute URI is an operator error
    /// that would cause silent export failures at runtime. Accumulates into
    /// <paramref name="errors"/> (D7 collect-all).
    /// </summary>
    internal static void ValidateObservability(IConfiguration config, ICollection<string> errors)
    {
        // ID-17: validate whichever value HostBootstrap will actually use — config key first,
        // OTEL_EXPORTER_OTLP_ENDPOINT env var as fallback. If neither is set, no validation needed.
        var endpoint = config["Otel:OtlpEndpoint"] ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
        if (!string.IsNullOrWhiteSpace(endpoint) && !Uri.TryCreate(endpoint, UriKind.Absolute, out _))
            errors.Add($"Otel:OtlpEndpoint '{endpoint}' is not a valid absolute URI.");

        var seq = config["Serilog:Seq:ServerUrl"];
        if (!string.IsNullOrWhiteSpace(seq) && !Uri.TryCreate(seq, UriKind.Absolute, out _))
            errors.Add($"Serilog:Seq:ServerUrl '{seq}' is not a valid absolute URI.");
    }

    /// <summary>
    /// Verifies that every action name referenced by a subscription is registered as an
    /// <see cref="IActionPlugin"/> in the DI container. Accumulates errors into
    /// <paramref name="errors"/> rather than throwing (D7 collect-all).
    /// </summary>
    internal static void ValidateActions(
        IEnumerable<SubscriptionOptions> subscriptions,
        IEnumerable<IActionPlugin> actionPlugins,
        List<string> errors)
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
                    errors.Add(
                        $"Subscription '{sub.Name}' references unknown action plugin '{entry.Plugin}'. " +
                        $"Registered plugins: [{string.Join(", ", registeredNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))}]. " +
                        $"Either register the plugin or remove the reference from appsettings.");
                }
            }
        }
    }

    /// <summary>
    /// Verifies that every snapshot provider name referenced by configuration is registered
    /// as an <see cref="ISnapshotProvider"/>. Accumulates errors into
    /// <paramref name="errors"/> rather than throwing (D7 collect-all).
    /// </summary>
    internal static void ValidateSnapshotProviders(
        IEnumerable<SubscriptionOptions> subscriptions,
        string? globalDefaultProviderName,
        IEnumerable<ISnapshotProvider> snapshotProviders,
        List<string> errors)
    {
        var registeredNames = snapshotProviders
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrEmpty(globalDefaultProviderName) && !registeredNames.Contains(globalDefaultProviderName))
        {
            errors.Add(
                $"Global Snapshots:DefaultProviderName '{globalDefaultProviderName}' is not a registered snapshot provider. " +
                $"Registered providers: [{string.Join(", ", registeredNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))}]. " +
                $"Either register the provider or remove the reference from appsettings.");
        }

        foreach (var sub in subscriptions)
        {
            if (!string.IsNullOrEmpty(sub.DefaultSnapshotProvider) && !registeredNames.Contains(sub.DefaultSnapshotProvider))
            {
                errors.Add(
                    $"Subscription '{sub.Name}' references unknown snapshot provider '{sub.DefaultSnapshotProvider}' " +
                    $"as its DefaultSnapshotProvider. Registered providers: " +
                    $"[{string.Join(", ", registeredNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))}].");
            }

            foreach (var entry in sub.Actions)
            {
                if (!string.IsNullOrEmpty(entry.SnapshotProvider) && !registeredNames.Contains(entry.SnapshotProvider))
                {
                    errors.Add(
                        $"Subscription '{sub.Name}' action '{entry.Plugin}' references unknown snapshot provider " +
                        $"'{entry.SnapshotProvider}'. Registered providers: " +
                        $"[{string.Join(", ", registeredNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))}].");
                }
            }
        }
    }

    /// <summary>
    /// Verifies that every named validator instance referenced by any
    /// <see cref="ActionEntry.Validators"/> resolves to a registered keyed
    /// <see cref="IValidationPlugin"/>. Accumulates errors into
    /// <paramref name="errors"/> rather than throwing (D7 collect-all).
    /// </summary>
    internal static void ValidateValidators(
        IEnumerable<SubscriptionOptions> subscriptions,
        IServiceProvider services,
        List<string> errors)
    {
        var subList = subscriptions as IList<SubscriptionOptions> ?? subscriptions.ToList();
        for (int i = 0; i < subList.Count; i++)
        {
            var sub = subList[i];
            for (int j = 0; j < sub.Actions.Count; j++)
            {
                var action = sub.Actions[j];
                if (action.Validators is null || action.Validators.Count == 0) continue;
                foreach (var key in action.Validators)
                {
                    var plugin = services.GetKeyedService<IValidationPlugin>(key);
                    if (plugin is null)
                    {
                        errors.Add(
                            $"Validator '{key}' is referenced by Subscription[{i}].Actions[{j}].Validators " +
                            $"but not registered. Check the top-level Validators section and ensure each " +
                            $"instance has a recognized Type.");
                    }
                }
            }
        }
    }
}
