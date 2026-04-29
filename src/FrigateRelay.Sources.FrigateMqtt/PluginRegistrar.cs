using FrigateRelay.Abstractions;
using FrigateRelay.Sources.FrigateMqtt.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MQTTnet;

namespace FrigateRelay.Sources.FrigateMqtt;

/// <summary>
/// Registers the FrigateMqtt event source and its MQTTnet v5 client with the host DI container.
/// </summary>
/// <remarks>
/// <para>
/// Registers strictly transport-scoped services. Subscription matching, dedupe, and the host-side
/// event pump live in <c>FrigateRelay.Host</c> and are registered there (see PLAN-3.1) — this
/// registrar never touches matcher / dedupe / cache / subscription configuration.
/// </para>
/// <para>
/// Options binding: <see cref="FrigateMqttOptions"/> binds from the <c>FrigateMqtt</c> configuration
/// section. Missing or partial sections fall back to the property-default values on the option records.
/// </para>
/// </remarks>
public sealed class PluginRegistrar : IPluginRegistrar
{
    /// <inheritdoc />
    public void Register(PluginRegistrationContext context)
    {
        context.Services
            .AddOptions<FrigateMqttOptions>()
            .Bind(context.Configuration.GetSection("FrigateMqtt"));

        // MqttClientFactory is cheap and stateless — one per host is plenty.
        context.Services.AddSingleton<MqttClientFactory>();

        // Single IMqttClient per host. Dispose handled by the event source's IAsyncDisposable
        // when DI tears down; MQTTnet's IMqttClient is IDisposable and the container calls it.
        context.Services.AddSingleton<IMqttClient>(sp =>
            sp.GetRequiredService<MqttClientFactory>().CreateMqttClient());

        // Register as the concrete type first, then alias to IEventSource so the host can
        // enumerate all IEventSource implementations without caring about plugin identities.
        context.Services.AddSingleton<FrigateMqttEventSource>();
        context.Services.AddSingleton<IEventSource>(
            sp => sp.GetRequiredService<FrigateMqttEventSource>());
    }
}
