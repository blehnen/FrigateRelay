using FrigateRelay.Abstractions;
using FrigateRelay.Host.Configuration;
using FrigateRelay.Host.Dispatch;
using FrigateRelay.Host.Matching;
using FrigateRelay.Host.Snapshots;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Serilog;

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
        // Serilog wiring (Worker SDK pattern — Serilog.Extensions.Hosting, NOT Serilog.AspNetCore).
        // AddSerilog on IServiceCollection is the correct API for HostApplicationBuilder in .NET 10.
        // ReadFrom.Configuration picks up Serilog:MinimumLevel overrides from appsettings.json.
        // Console + File sinks are always active; Seq is conditional on Serilog:Seq:ServerUrl (D7).
        // AddSerilog handles provider replacement — do NOT call builder.Logging.ClearProviders().
        builder.Services.AddSerilog((services, lc) =>
        {
            lc.ReadFrom.Configuration(builder.Configuration)
              .ReadFrom.Services(services)
              .Enrich.FromLogContext()
              .WriteTo.Console(
                  outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}{NewLine}{Message:lj}{NewLine}{Exception}",
                  formatProvider: null)
              .WriteTo.File(
                  path: "logs/frigaterelay-.log",
                  rollingInterval: RollingInterval.Day,
                  retainedFileCountLimit: 7,
                  formatProvider: null);

            var seqUrl = builder.Configuration["Serilog:Seq:ServerUrl"];
            if (!string.IsNullOrWhiteSpace(seqUrl))
                lc.WriteTo.Seq(seqUrl, formatProvider: null);
        });

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
        // CodeProjectAi: only register when the top-level Validators section is present.
        // The registrar itself iterates that section and only acts on Type=="CodeProjectAi"
        // entries, but gating here keeps the registrar list clean for inspection / logging.
        if (builder.Configuration.GetSection("Validators").Exists())
            registrars.Add(new FrigateRelay.Plugins.CodeProjectAi.PluginRegistrar());

        using var bootstrapLoggerFactory = LoggerFactory.Create(lb => lb.AddConsole());
        var bootstrapLogger = bootstrapLoggerFactory.CreateLogger<IPluginRegistrar>();
        PluginRegistrarRunner.RunAll(registrars, registrationContext, bootstrapLogger);
    }

    /// <summary>
    /// Post-build validation: runs the full collect-all startup validation pipeline
    /// (profile resolution → action plugins → snapshot providers → validators).
    /// A single aggregated <see cref="InvalidOperationException"/> is thrown if any
    /// errors are found, so operators see all misconfigurations at once (D7).
    /// Call after <c>builder.Build()</c>, before <c>app.RunAsync()</c>.
    /// </summary>
    public static void ValidateStartup(IServiceProvider services)
    {
        var subsOpts = services.GetRequiredService<IOptions<HostSubscriptionsOptions>>().Value;
        StartupValidation.ValidateAll(services, subsOpts);
    }
}
