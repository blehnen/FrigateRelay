using FrigateRelay.Abstractions;
using FrigateRelay.Host.Configuration;
using FrigateRelay.Host.Dispatch;
using FrigateRelay.Host.Matching;
using FrigateRelay.Host.Snapshots;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace FrigateRelay.Host;

/// <summary>
/// Centralises all DI registration so both <c>Program.cs</c> and integration tests share
/// the exact same wiring path without duplicating setup code.
/// </summary>
internal static class HostBootstrap
{
    private static readonly string[] PluginNamesWithQueueCapacity = ["BlueIris", "Pushover"];

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
                var merged = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                foreach (var pluginName in PluginNamesWithQueueCapacity)
                {
                    if (builder.Configuration.GetValue<int?>($"{pluginName}:QueueCapacity") is { } cap)
                        merged[pluginName] = cap;
                }

                if (merged.Count > 0)
                    opts.PerPluginQueueCapacity = merged;
            });

        // Snapshot resolver options + resolver singleton.
        builder.Services.AddOptions<SnapshotResolverOptions>()
            .Bind(builder.Configuration.GetSection("Snapshots"))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        builder.Services.AddSingleton<ISnapshotResolver, SnapshotResolver>();

        // Plugin registrars — each is added only when its config section is present so
        // ValidateOnStart does not reject required fields when the plugin is not in use.
        var registrationContext = new PluginRegistrationContext(builder.Services, builder.Configuration);
        List<IPluginRegistrar> registrars = [new FrigateRelay.Sources.FrigateMqtt.PluginRegistrar()];
        if (builder.Configuration.GetSection("BlueIris").Exists())
            registrars.Add(new FrigateRelay.Plugins.BlueIris.PluginRegistrar());
        if (builder.Configuration.GetSection("FrigateSnapshot").Exists())
            registrars.Add(new FrigateRelay.Plugins.FrigateSnapshot.PluginRegistrar());
        if (builder.Configuration.GetSection("Pushover").Exists())
            registrars.Add(new FrigateRelay.Plugins.Pushover.PluginRegistrar());

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

        var snapshotOpts = services.GetRequiredService<IOptions<SnapshotResolverOptions>>().Value;
        var snapshotProviders = services.GetRequiredService<IEnumerable<ISnapshotProvider>>();
        StartupValidation.ValidateSnapshotProviders(subsOpts.Subscriptions, snapshotOpts.DefaultProviderName, snapshotProviders);
    }
}
