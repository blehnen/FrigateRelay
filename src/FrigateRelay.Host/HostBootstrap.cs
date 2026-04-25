using FrigateRelay.Abstractions;
using FrigateRelay.Host.Configuration;
using FrigateRelay.Host.Dispatch;
using FrigateRelay.Host.Matching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace FrigateRelay.Host;

/// <summary>
/// Centralises all DI registration so both <c>Program.cs</c> and integration tests share
/// the exact same wiring path without duplicating setup code.
/// </summary>
internal static class HostBootstrap
{
    /// <summary>
    /// Registers all host-scope and plugin services on <paramref name="builder"/>.
    /// Must be called before <c>builder.Build()</c>.
    /// </summary>
    public static void ConfigureServices(HostApplicationBuilder builder)
    {
        // Host-scope services.
        builder.Services.AddOptions<HostSubscriptionsOptions>()
            .Bind(builder.Configuration);

        builder.Services.AddSingleton<IMemoryCache>(new MemoryCache(new MemoryCacheOptions()));
        builder.Services.AddSingleton<DedupeCache>();
        builder.Services.AddHostedService<EventPump>();

        // ChannelActionDispatcher: one singleton fills IActionDispatcher + IHostedService roles.
        builder.Services.AddSingleton<ChannelActionDispatcher>();
        builder.Services.AddSingleton<IActionDispatcher>(sp => sp.GetRequiredService<ChannelActionDispatcher>());
        builder.Services.AddHostedService(sp => sp.GetRequiredService<ChannelActionDispatcher>());

        // DispatcherOptions: optional "Dispatcher" section + per-plugin capacity overrides.
        builder.Services.AddOptions<DispatcherOptions>()
            .Bind(builder.Configuration.GetSection("Dispatcher"))
            .Configure(opts =>
            {
                var blueIrisCapacity = builder.Configuration.GetValue<int?>("BlueIris:QueueCapacity");
                if (blueIrisCapacity is { } c)
                {
                    opts.PerPluginQueueCapacity = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["BlueIris"] = c,
                    };
                }
            });

        // Plugin registrars — each is added only when its config section is present so
        // ValidateOnStart does not reject required fields when the plugin is not in use.
        var registrationContext = new PluginRegistrationContext(builder.Services, builder.Configuration);
        List<IPluginRegistrar> registrars = [new FrigateRelay.Sources.FrigateMqtt.PluginRegistrar()];
        if (builder.Configuration.GetSection("BlueIris").Exists())
            registrars.Add(new FrigateRelay.Plugins.BlueIris.PluginRegistrar());

        using var bootstrapLoggerFactory = LoggerFactory.Create(lb => lb.AddConsole());
        var bootstrapLogger = bootstrapLoggerFactory.CreateLogger<IPluginRegistrar>();
        PluginRegistrarRunner.RunAll(registrars, registrationContext, bootstrapLogger);
    }

    /// <summary>
    /// Post-build validation: fails fast when a subscription references an unknown action plugin.
    /// Call after <c>builder.Build()</c>, before <c>app.RunAsync()</c>.
    /// </summary>
    public static void ValidateStartup(IServiceProvider services)
    {
        var subsOpts = services.GetRequiredService<IOptions<HostSubscriptionsOptions>>().Value;
        var actionPlugins = services.GetRequiredService<IEnumerable<IActionPlugin>>();
        StartupValidation.ValidateActions(subsOpts.Subscriptions, actionPlugins);
    }
}
