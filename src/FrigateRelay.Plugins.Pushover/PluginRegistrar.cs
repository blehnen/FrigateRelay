using FrigateRelay.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace FrigateRelay.Plugins.Pushover;

/// <summary>
/// Registers Pushover plugin services into the host DI container.
/// </summary>
/// <remarks>
/// Pushover:QueueCapacity is intentionally NOT consumed here — the plugin must remain
/// free of FrigateRelay.Host dependencies (CLAUDE.md architecture invariant). Program.cs
/// reads Pushover:QueueCapacity and applies it to DispatcherOptions on the host side.
/// </remarks>
public sealed class PluginRegistrar : IPluginRegistrar
{
    /// <inheritdoc />
    public void Register(PluginRegistrationContext context)
    {
        context.Services
            .AddOptions<PushoverOptions>()
            .Bind(context.Configuration.GetSection("Pushover"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        context.Services.AddSingleton<IValidateOptions<PushoverOptions>, PushoverOptionsValidator>();

        // Parse the MessageTemplate at startup and register it as a singleton.
        // PushoverOptionsValidator runs first via ValidateOnStart, so Parse will
        // already have succeeded by the time this factory is called.
        context.Services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<PushoverOptions>>().Value;
            return EventTokenTemplate.Parse(opts.MessageTemplate, "Pushover.MessageTemplate");
        });

        var httpClientBuilder = context.Services.AddHttpClient("Pushover", (sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<PushoverOptions>>().Value;
            client.BaseAddress = new Uri(opts.BaseAddress);
            client.Timeout = opts.RequestTimeout;
        });

        httpClientBuilder.AddResilienceHandler("pushover-retry", builder =>
        {
            builder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                DelayGenerator = static args =>
                    ValueTask.FromResult<TimeSpan?>(
                        TimeSpan.FromSeconds(3 * (args.AttemptNumber + 1))),
            });
        });

        httpClientBuilder.ConfigurePrimaryHttpMessageHandler(_ => new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        });

        context.Services.AddSingleton<PushoverActionPlugin>();
        context.Services.AddSingleton<IActionPlugin>(sp =>
            sp.GetRequiredService<PushoverActionPlugin>());
    }
}
