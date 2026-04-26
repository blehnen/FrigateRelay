using FrigateRelay.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace FrigateRelay.Plugins.FrigateSnapshot;

/// <summary>
/// Registers FrigateSnapshot plugin services into the host DI container.
/// </summary>
public sealed class PluginRegistrar : IPluginRegistrar
{
    /// <inheritdoc />
    public void Register(PluginRegistrationContext context)
    {
        context.Services
            .AddOptions<FrigateSnapshotOptions>()
            .Bind(context.Configuration.GetSection("FrigateSnapshot"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var httpClientBuilder = context.Services.AddHttpClient("FrigateSnapshot", (sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<FrigateSnapshotOptions>>().Value;
            client.BaseAddress = new Uri(opts.BaseUrl);
            client.Timeout = opts.RequestTimeout;
        });

        httpClientBuilder.AddResilienceHandler("FrigateSnapshot-retry", builder =>
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

        context.Services.AddSingleton<FrigateSnapshotProvider>();
        context.Services.AddSingleton<ISnapshotProvider>(sp =>
            sp.GetRequiredService<FrigateSnapshotProvider>());
    }
}
