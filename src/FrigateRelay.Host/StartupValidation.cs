using System.Text.RegularExpressions;
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
    /// Escapes operator-controlled string values for safe inclusion in structured log
    /// error messages. Returns <see cref="string.Empty"/> for <see langword="null"/>;
    /// for non-null, replaces carriage-return (<c>\r</c>) and line-feed (<c>\n</c>)
    /// characters with their printable escape sequences (<c>\\r</c> and <c>\\n</c>)
    /// so the result is always a single safe log line (ID-13 / CWE-117 closure).
    /// </summary>
    /// <param name="value">The operator-supplied string (may be null).</param>
    /// <returns>A sanitized, single-line representation of <paramref name="value"/>.</returns>
    internal static string Sanitize(string? value)
    {
        if (value is null) return string.Empty;
        return value.Replace("\r", @"\r").Replace("\n", @"\n");
    }

    /// <summary>
    /// Permissive-printable allowlist: alphanumeric, space, dot, dash, underscore (D1).
    /// Rejects CRLF, null bytes, control chars, slashes, colons, and at-signs.
    /// Not <c>[GeneratedRegex]</c> because <c>internal static class</c> cannot be <c>partial</c>.
    /// </summary>
    private static readonly Regex NameAllowlist = new("^[A-Za-z0-9_. -]+$", RegexOptions.Compiled);

    /// <summary>
    /// Validates that subscription names, profile keys, plugin names, and validator instance
    /// keys contain only the permissive-printable character set defined in D1
    /// (<c>^[A-Za-z0-9_. -]+$</c>). Accumulates errors into <paramref name="errors"/>
    /// (D7 collect-all). Called from <see cref="ValidateAll"/> before profile resolution
    /// so profile keys are checked before any lookup is attempted (ID-19 closure).
    /// </summary>
    internal static void ValidateNames(HostSubscriptionsOptions options, List<string> errors)
    {
        const string allowedDesc = "only [A-Za-z0-9_. -] are permitted (CRLF, control chars, slashes, colons, and at-signs are rejected).";

        // Subscription names.
        foreach (var sub in options.Subscriptions)
        {
            if (!string.IsNullOrEmpty(sub.Name) && !NameAllowlist.IsMatch(sub.Name))
                errors.Add($"Subscription name '{Sanitize(sub.Name)}' is invalid; {allowedDesc}");
        }

        // Profile keys.
        foreach (var key in options.Profiles.Keys)
        {
            if (!string.IsNullOrEmpty(key) && !NameAllowlist.IsMatch(key))
                errors.Add($"Profile name '{Sanitize(key)}' is invalid; {allowedDesc}");
        }

        // Plugin names referenced in subscriptions.
        foreach (var sub in options.Subscriptions)
        {
            foreach (var entry in sub.Actions)
            {
                if (!string.IsNullOrEmpty(entry.Plugin) && !NameAllowlist.IsMatch(entry.Plugin))
                    errors.Add($"Plugin name '{Sanitize(entry.Plugin)}' is invalid; {allowedDesc}");
            }
        }

        // Plugin names referenced in profiles.
        foreach (var profile in options.Profiles.Values)
        {
            foreach (var entry in profile.Actions)
            {
                if (!string.IsNullOrEmpty(entry.Plugin) && !NameAllowlist.IsMatch(entry.Plugin))
                    errors.Add($"Plugin name '{Sanitize(entry.Plugin)}' is invalid; {allowedDesc}");
            }
        }

        // Validator instance keys in subscriptions.
        foreach (var sub in options.Subscriptions)
        {
            foreach (var entry in sub.Actions)
            {
                if (entry.Validators is null) continue;
                foreach (var validatorKey in entry.Validators)
                {
                    if (!string.IsNullOrEmpty(validatorKey) && !NameAllowlist.IsMatch(validatorKey))
                        errors.Add($"Validator name '{Sanitize(validatorKey)}' is invalid; {allowedDesc}");
                }
            }
        }

        // Validator instance keys in profiles.
        foreach (var profile in options.Profiles.Values)
        {
            foreach (var entry in profile.Actions)
            {
                if (entry.Validators is null) continue;
                foreach (var validatorKey in entry.Validators)
                {
                    if (!string.IsNullOrEmpty(validatorKey) && !NameAllowlist.IsMatch(validatorKey))
                        errors.Add($"Validator name '{Sanitize(validatorKey)}' is invalid; {allowedDesc}");
                }
            }
        }
    }

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
        {
            ValidateObservability(configuration, errors);
            ValidateSerilogPath(configuration, errors);   // ID-21: reject path-traversal / UNC / off-allowlist absolute paths
        }

        // Pass 0.5 — name allowlist (D1 permissive-printable; before resolve so profile keys are checked).
        ValidateNames(options, errors);

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
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            // ID-20: scheme allowlist — produce a structured diagnostic at startup instead of
            // an ArgumentException from deep inside the OTLP exporter on first metric/span flush.
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
                errors.Add($"Otel:OtlpEndpoint '{Sanitize(endpoint)}' is not a valid absolute URI.");
            else if (uri.Scheme is not ("http" or "https" or "grpc"))
                errors.Add($"Otel:OtlpEndpoint '{Sanitize(endpoint)}' has unsupported scheme '{Sanitize(uri.Scheme)}'; allowed: http, https, grpc.");
        }

        var seq = config["Serilog:Seq:ServerUrl"];
        if (!string.IsNullOrWhiteSpace(seq) && !Uri.TryCreate(seq, UriKind.Absolute, out _))
            errors.Add($"Serilog:Seq:ServerUrl '{Sanitize(seq)}' is not a valid absolute URI.");
    }

    /// <summary>
    /// Validates that every <c>Serilog:WriteTo:*:Args:path</c> value is safe to open at startup.
    /// Rejects paths that contain <c>..</c> (path traversal), start with <c>\\</c> (UNC), are
    /// absolute paths outside the allowed prefixes (<c>/var/log/frigaterelay/</c>, <c>/app/logs/</c>),
    /// or — when running on Windows — are Windows-style absolute paths (e.g. <c>C:\Windows\...</c>).
    /// Relative paths and absent/empty values are accepted without error.
    /// Accumulates into <paramref name="errors"/> — never throws (D7 collect-all, ID-21 + ID-27 closure).
    /// </summary>
    /// <param name="config">Configuration root.</param>
    /// <param name="errors">Collect-all error accumulator (D7).</param>
    /// <param name="isWindows">
    /// Optional platform predicate (D5 test seam). When <c>null</c> (production default), resolves to
    /// <see cref="System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform"/>. Tests inject a
    /// fixed predicate to exercise both branches deterministically without requiring a Windows test
    /// agent.
    /// </param>
    /// <remarks>
    /// Do NOT log the raw path value via <c>ILogger</c> — it is operator-controlled and could
    /// contain log-spoofing payloads (ID-13). It surfaces only in the aggregated exception message,
    /// sanitized via <see cref="Sanitize"/>.
    /// </remarks>
    internal static void ValidateSerilogPath(IConfiguration config, ICollection<string> errors, Func<bool>? isWindows = null)
    {
        var allowlist = new[] { "/var/log/frigaterelay/", "/app/logs/" };
        var onWindows = (isWindows ?? (() => System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)))();
        foreach (var sink in config.GetSection("Serilog:WriteTo").GetChildren())
        {
            var path = sink["Args:path"];
            if (string.IsNullOrWhiteSpace(path)) continue;

            if (path.Contains(".."))
                errors.Add($"Serilog:WriteTo path '{Sanitize(path)}' contains '..' path traversal segments and is rejected.");
            else if (path.StartsWith(@"\\", StringComparison.Ordinal))
                errors.Add($"Serilog:WriteTo path '{Sanitize(path)}' is a UNC path and is not permitted.");
            else if (path.StartsWith('/') &&
                     !allowlist.Any(prefix => path.StartsWith(prefix, StringComparison.Ordinal)))
                errors.Add($"Serilog:WriteTo path '{Sanitize(path)}' is an absolute path outside the allowed prefixes ({string.Join(", ", allowlist)}).");
            else if (onWindows && IsWindowsRootedPath(path))
                errors.Add($"Serilog:WriteTo path '{Sanitize(path)}' is a Windows-style absolute path and is not permitted.");
        }
    }

    // Detects a Windows-style absolute path (drive letter form, e.g. C:\foo or D:/bar) without
    // relying on Path.IsPathRooted, which depends on the host OS and would return false on Linux
    // for these patterns. Used by ValidateSerilogPath only when the host is Windows (D5 predicate).
    private static bool IsWindowsRootedPath(string path) =>
        path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/');

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
                        $"Subscription '{Sanitize(sub.Name)}' references unknown action plugin '{Sanitize(entry.Plugin)}'. " +
                        $"Registered plugins: [{string.Join(", ", registeredNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).Select(Sanitize))}]. " +
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
                $"Global Snapshots:DefaultProviderName '{Sanitize(globalDefaultProviderName)}' is not a registered snapshot provider. " +
                $"Registered providers: [{string.Join(", ", registeredNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).Select(Sanitize))}]. " +
                $"Either register the provider or remove the reference from appsettings.");
        }

        foreach (var sub in subscriptions)
        {
            if (!string.IsNullOrEmpty(sub.DefaultSnapshotProvider) && !registeredNames.Contains(sub.DefaultSnapshotProvider))
            {
                errors.Add(
                    $"Subscription '{Sanitize(sub.Name)}' references unknown snapshot provider '{Sanitize(sub.DefaultSnapshotProvider)}' " +
                    $"as its DefaultSnapshotProvider. Registered providers: " +
                    $"[{string.Join(", ", registeredNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).Select(Sanitize))}].");
            }

            foreach (var entry in sub.Actions)
            {
                if (!string.IsNullOrEmpty(entry.SnapshotProvider) && !registeredNames.Contains(entry.SnapshotProvider))
                {
                    errors.Add(
                        $"Subscription '{Sanitize(sub.Name)}' action '{Sanitize(entry.Plugin)}' references unknown snapshot provider " +
                        $"'{Sanitize(entry.SnapshotProvider)}'. Registered providers: " +
                        $"[{string.Join(", ", registeredNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).Select(Sanitize))}].");
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
                            $"Validator '{Sanitize(key)}' is referenced by Subscription[{i}].Actions[{j}].Validators " +
                            $"but not registered. Check the top-level Validators section and ensure each " +
                            $"instance has a recognized Type.");
                    }
                }
            }
        }
    }
}
