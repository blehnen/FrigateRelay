using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FrigateRelay.Abstractions;

/// <summary>
/// Carries the host's <see cref="IServiceCollection"/> and <see cref="IConfiguration"/> to a plugin registrar,
/// giving each plugin everything it needs to register its services and bind its own configuration section
/// without exposing the full host builder.
/// </summary>
public sealed class PluginRegistrationContext
{
    /// <summary>Gets the service collection into which the plugin registers its dependencies.</summary>
    public required IServiceCollection Services { get; init; }

    /// <summary>Gets the host configuration from which the plugin can bind its own options section.</summary>
    public required IConfiguration Configuration { get; init; }

    /// <summary>Initializes a new <see cref="PluginRegistrationContext"/> with the supplied service collection and configuration.</summary>
    /// <param name="services">The host's service collection. Must not be <see langword="null"/>.</param>
    /// <param name="configuration">The host's configuration. Must not be <see langword="null"/>.</param>
    [SetsRequiredMembers]
    public PluginRegistrationContext(IServiceCollection services, IConfiguration configuration)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }
}
