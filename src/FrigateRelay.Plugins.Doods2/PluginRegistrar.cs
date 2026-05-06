using FrigateRelay.Abstractions;
using FrigateRelay.Plugins.Doods2.Grpc;
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FrigateRelay.Plugins.Doods2;

/// <summary>
/// Enumerates the top-level <c>Validators:&lt;key&gt;</c> configuration dictionary, picks
/// entries with <c>Type == "Doods2"</c>, and registers one keyed
/// <see cref="IValidationPlugin"/> per instance via <c>AddKeyedSingleton</c> + named options.
/// </summary>
/// <remarks>
/// <para>
/// Per-instance <see cref="HttpClient"/> with per-instance TLS handler. Per-instance gRPC
/// <see cref="GrpcChannel"/> reused across calls (HTTP/2 multiplexing — channel lifetime
/// matches the keyed-singleton validator lifetime). Other validator types (CPAI, Roboflow)
/// coexist by filtering on their own <c>Type</c> discriminator value.
/// </para>
/// <para>
/// <strong>INTENTIONALLY no <c>AddResilienceHandler</c> on the validator <see cref="HttpClient"/>.</strong>
/// Asymmetric with BlueIris/Pushover (which retry 3/6/9s). Validators are pre-action gates;
/// per-attempt retry latency would systematically delay every notification.
/// Single <see cref="Doods2Options.Timeout"/>; fail-{closed,open} per
/// <see cref="Doods2Options.OnError"/> (CONTEXT-7 D4 / CONTEXT-14 D4 architect lock-in).
/// </para>
/// <para>
/// <strong>Both transport clients are always registered for every instance.</strong> The
/// validator picks which one to invoke based on <see cref="Doods2Options.Transport"/>, but
/// keeping the unused client wired (rather than null) keeps the registrar branch-free and
/// makes the validator constructor testable without conditional-DI gymnastics.
/// </para>
/// </remarks>
public sealed class PluginRegistrar : IPluginRegistrar
{
    /// <inheritdoc />
    public void Register(PluginRegistrationContext context)
    {
        var validatorsSection = context.Configuration.GetSection("Validators");
        if (!validatorsSection.Exists()) return;

        foreach (var instance in validatorsSection.GetChildren())
        {
            var type = instance["Type"];
            if (!string.Equals(type, "Doods2", StringComparison.Ordinal))
                continue;

            // Capture for the closures below — instance.Key is a property reference, but we want
            // the value snapshot at registration time so all factories see the same instance key.
            var instanceKey = instance.Key;

            // Bind named options instance (retrieved at runtime via IOptionsMonitor.Get(key)).
            context.Services
                .AddOptions<Doods2Options>(instanceKey)
                .Bind(instance)
                .ValidateDataAnnotations()
                .ValidateOnStart();

            // Per-instance HttpClient with per-instance TLS handler. INTENTIONALLY no
            // AddResilienceHandler — see remarks on this class.
            var clientName = $"Doods2:{instanceKey}";
            context.Services
                .AddHttpClient(clientName)
                .ConfigurePrimaryHttpMessageHandler(sp =>
                {
                    var opts = sp.GetRequiredService<IOptionsMonitor<Doods2Options>>().Get(instanceKey);
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

            // Keyed validator plugin — resolved by EventPump via IServiceProvider.GetRequiredKeyedService.
            // Both transport clients (HttpClient + GrpcChannel-backed gRPC client) are always built;
            // the validator's ValidateAsync branches on Doods2Options.Transport at call time.
            context.Services.AddKeyedSingleton<IValidationPlugin>(instanceKey, (sp, key) =>
            {
                var name = (string)key!;
                var opts = sp.GetRequiredService<IOptionsMonitor<Doods2Options>>().Get(name);

                var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient($"Doods2:{name}");
                http.BaseAddress = new Uri(opts.BaseUrl);
                http.Timeout = opts.Timeout;

                // GrpcChannel is thread-safe and HTTP/2-multiplexed — single channel per validator
                // instance is correct; do NOT recreate per-call. Channel lifetime = singleton.
                // Note: GrpcChannel does NOT honor the per-instance TLS-skip handler; gRPC users
                // who need that should terminate TLS at a sidecar or run plaintext (h2c).
                var channel = GrpcChannel.ForAddress(opts.BaseUrl);
                var grpcClient = new odrpc.odrpcClient(channel);

                var logger = sp.GetRequiredService<ILogger<Doods2Validator>>();
                return new Doods2Validator(name, opts, http, grpcClient, logger);
            });
        }
    }
}
