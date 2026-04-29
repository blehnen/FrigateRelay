using FrigateRelay.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FrigateRelay.Plugins.CodeProjectAi;

/// <summary>
/// Enumerates the top-level <c>Validators:&lt;key&gt;</c> configuration dictionary, picks
/// entries with <c>Type == "CodeProjectAi"</c>, and registers one keyed
/// <see cref="IValidationPlugin"/> per instance via <c>AddKeyedSingleton</c> + named options.
/// </summary>
/// <remarks>
/// <para>
/// Per-instance <see cref="HttpClient"/> with per-instance TLS handler. Other validator types
/// (future) coexist by filtering on their own <c>Type</c> discriminator value.
/// </para>
/// <para>
/// <strong>INTENTIONALLY no <c>AddResilienceHandler</c> on the validator <see cref="HttpClient"/>.</strong>
/// Asymmetric with BlueIris/Pushover (which retry 3/6/9s). Validators are pre-action gates;
/// per-attempt retry latency would systematically delay every notification by 18s.
/// Single <see cref="CodeProjectAiOptions.Timeout"/>; fail-{closed,open} per
/// <see cref="CodeProjectAiOptions.OnError"/> (CONTEXT-7 D4 architect lock-in).
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
            if (!string.Equals(type, "CodeProjectAi", StringComparison.Ordinal))
                continue;

            // Capture for the closures below — instance.Key is a property reference, but we want
            // the value snapshot at registration time so all factories see the same instance key.
            var instanceKey = instance.Key;

            // Bind named options instance (retrieved at runtime via IOptionsMonitor.Get(key)).
            context.Services
                .AddOptions<CodeProjectAiOptions>(instanceKey)
                .Bind(instance)
                .ValidateDataAnnotations()
                .ValidateOnStart();

            // Per-instance HttpClient with per-instance TLS handler. INTENTIONALLY no
            // AddResilienceHandler — see remarks on this class.
            var clientName = $"CodeProjectAi:{instanceKey}";
            context.Services
                .AddHttpClient(clientName)
                .ConfigurePrimaryHttpMessageHandler(sp =>
                {
                    var opts = sp.GetRequiredService<IOptionsMonitor<CodeProjectAiOptions>>().Get(instanceKey);
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
            context.Services.AddKeyedSingleton<IValidationPlugin>(instanceKey, (sp, key) =>
            {
                var name = (string)key!;
                var opts = sp.GetRequiredService<IOptionsMonitor<CodeProjectAiOptions>>().Get(name);
                var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient($"CodeProjectAi:{name}");
                http.BaseAddress = new Uri(opts.BaseUrl);
                http.Timeout = opts.Timeout;
                var logger = sp.GetRequiredService<ILogger<CodeProjectAiValidator>>();
                return new CodeProjectAiValidator(name, opts, http, logger);
            });
        }
    }
}
