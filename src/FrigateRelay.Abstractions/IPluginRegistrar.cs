namespace FrigateRelay.Abstractions;

/// <summary>Implemented by each plugin project to register its services and configuration bindings into the host's DI container.</summary>
public interface IPluginRegistrar
{
    /// <summary>Registers the plugin's services into the <see cref="PluginRegistrationContext.Services"/> collection and binds any required configuration.</summary>
    /// <param name="context">The registration context exposing the host's <see cref="PluginRegistrationContext.Services"/> and <see cref="PluginRegistrationContext.Configuration"/>.</param>
    void Register(PluginRegistrationContext context);
}
