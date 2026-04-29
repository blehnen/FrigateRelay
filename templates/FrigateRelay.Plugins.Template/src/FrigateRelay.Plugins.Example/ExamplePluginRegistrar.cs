using FrigateRelay.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace FrigateRelay.Plugins.Example;

public sealed class ExamplePluginRegistrar : IPluginRegistrar
{
    public void Register(PluginRegistrationContext context)
    {
        context.Services.AddSingleton<IActionPlugin, ExampleActionPlugin>();
        context.Services.AddOptions<ExampleOptions>()
            .Bind(context.Configuration.GetSection("Example"));
    }
}
