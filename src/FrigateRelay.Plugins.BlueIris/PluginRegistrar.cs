using FrigateRelay.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace FrigateRelay.Plugins.BlueIris;

/// <summary>
/// Registers BlueIris plugin services into the host DI container.
/// </summary>
/// <remarks>
/// BlueIris:QueueCapacity is intentionally NOT consumed here — the plugin must remain
/// free of FrigateRelay.Host dependencies (CLAUDE.md architecture invariant). Program.cs
/// (PLAN-3.1) reads BlueIris:QueueCapacity and applies it to DispatcherOptions on the host side.
/// </remarks>
public sealed class PluginRegistrar : IPluginRegistrar
{
    /// <inheritdoc />
    public void Register(PluginRegistrationContext context)
    {
        context.Services
            .AddOptions<BlueIrisOptions>()
            .Bind(context.Configuration.GetSection("BlueIris"))
            .Validate(o => !string.IsNullOrWhiteSpace(o.TriggerUrlTemplate),
                      "BlueIris.TriggerUrlTemplate is required.")
            .Validate(o =>
            {
                try { _ = BlueIrisUrlTemplate.Parse(o.TriggerUrlTemplate); return true; }
                catch { return false; }
            }, "BlueIris.TriggerUrlTemplate contains an unknown placeholder. Allowed: {camera}, {label}, {event_id}, {zone}.")
            .ValidateOnStart();

        context.Services.AddSingleton(sp =>
            BlueIrisUrlTemplate.Parse(sp.GetRequiredService<IOptions<BlueIrisOptions>>().Value.TriggerUrlTemplate));

        // Snapshot provider — opt-in when SnapshotUrlTemplate is configured.
        // Read directly from IConfiguration (same source as the Options bind above) to avoid
        // building a second ServiceProvider inside Register().
        var snapshotUrlTemplate = context.Configuration.GetSection("BlueIris")["SnapshotUrlTemplate"];
        if (!string.IsNullOrWhiteSpace(snapshotUrlTemplate))
        {
            var parsedSnapshot = BlueIrisUrlTemplate.Parse(snapshotUrlTemplate, "BlueIris.SnapshotUrlTemplate"); // fail-fast at startup if invalid
            context.Services.AddSingleton(new BlueIrisSnapshotUrlTemplate(parsedSnapshot));
            context.Services.AddSingleton<ISnapshotProvider, BlueIrisSnapshotProvider>();
        }

        var httpClientBuilder = context.Services.AddHttpClient("BlueIris", (sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<BlueIrisOptions>>().Value;
            client.Timeout = opts.RequestTimeout;
        });

        httpClientBuilder.AddResilienceHandler("BlueIris-retry", builder =>
        {
            builder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                DelayGenerator = static args =>
                    ValueTask.FromResult<TimeSpan?>(
                        TimeSpan.FromSeconds(3 * (args.AttemptNumber + 1))),
            });
        });

        httpClientBuilder.ConfigurePrimaryHttpMessageHandler(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<BlueIrisOptions>>().Value;
            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            };
            if (opts.AllowInvalidCertificates)
            {
#pragma warning disable CA5359 // opt-in TLS skip per CLAUDE.md architecture invariant
                handler.SslOptions.RemoteCertificateValidationCallback =
                    static (_, _, _, _) => true;
#pragma warning restore CA5359
            }
            return handler;
        });

        context.Services.AddSingleton<BlueIrisActionPlugin>();
        context.Services.AddSingleton<IActionPlugin>(sp =>
            sp.GetRequiredService<BlueIrisActionPlugin>());
    }
}
