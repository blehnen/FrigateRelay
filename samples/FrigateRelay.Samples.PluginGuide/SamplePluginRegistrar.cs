using FrigateRelay.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace FrigateRelay.Samples.PluginGuide;

/// <summary>
/// Registers all sample plugin services into the host DI container.
/// The host discovers this class and calls <see cref="Register"/> at startup.
/// </summary>
/// <remarks>
/// <para>
/// Every plugin assembly exposes exactly one <see cref="IPluginRegistrar"/> implementation.
/// The host resolves all registrars from DI and invokes <see cref="Register"/> once per
/// registrar during application startup.
/// </para>
/// <para>
/// All plugin service registrations use <c>AddSingleton</c>. The dispatcher resolves
/// action, validation, and snapshot provider plugins once at startup — transient or
/// scoped lifetimes are not supported.
/// </para>
/// <para>
/// Prefer <see cref="IHttpClientFactory"/> (via <c>AddHttpClient</c>) over constructing
/// <see cref="System.Net.Http.HttpClient"/> instances directly, to benefit from connection
/// pooling, Polly resilience pipelines, and per-named-client TLS configuration.
/// </para>
/// </remarks>
public sealed class SamplePluginRegistrar : IPluginRegistrar
{
    /// <inheritdoc />
    public void Register(PluginRegistrationContext context)
    {
        // Bind plugin options from the "Sample" configuration section.
        context.Services
            .AddOptions<SamplePluginOptions>()
            .Bind(context.Configuration.GetSection("Sample"));

        // Register the action plugin.
        context.Services.AddSingleton<IActionPlugin, SampleActionPlugin>();

        // Register the validation plugin.
        context.Services.AddSingleton<IValidationPlugin, SampleValidationPlugin>();

        // Register the snapshot provider.
        context.Services.AddSingleton<ISnapshotProvider, SampleSnapshotProvider>();
    }
}
