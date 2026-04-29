using FrigateRelay.Abstractions;
using FrigateRelay.Host.Configuration;
using FrigateRelay.Host.Dispatch;
using FrigateRelay.Host.Health;
using FrigateRelay.Host.Matching;
using FrigateRelay.Host.Snapshots;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;

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
    public static void ConfigureServices(WebApplicationBuilder builder)
    {
        // Serilog wiring (Serilog.Extensions.Hosting works with both Worker and Web SDK).
        // AddSerilog on IServiceCollection is the correct API for HostApplicationBuilder in .NET 10.
        // ReadFrom.Configuration picks up Serilog:MinimumLevel overrides from appsettings.json.
        // Console + File sinks are always active; Seq is conditional on Serilog:Seq:ServerUrl (D7).
        // AddSerilog handles provider replacement — do NOT call builder.Logging.ClearProviders().
        builder.Services.AddSerilog((services, lc) =>
        {
            ApplyLoggerConfiguration(lc, builder.Configuration, builder.Environment.EnvironmentName, services);
        });

        // OpenTelemetry registration (D2 — ActivitySource + Meter always registered;
        // OTLP exporter only when Otel:OtlpEndpoint or OTEL_EXPORTER_OTLP_ENDPOINT is set).
        // IConfiguration key takes precedence; env var is the fallback for container deployments.
        var otlpEndpoint = builder.Configuration["Otel:OtlpEndpoint"]
            ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName: "FrigateRelay"))
            .WithTracing(b =>
            {
                b.AddSource("FrigateRelay");
                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                    b.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
            })
            .WithMetrics(b =>
            {
                b.AddMeter("FrigateRelay");
                b.AddRuntimeInstrumentation();
                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                    b.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
            });

        // MQTT connection status singleton — registered before plugin registrars so DI can
        // resolve it when constructing FrigateMqttEventSource (which injects IMqttConnectionStatus).
        // MqttConnectionStatus is the concrete impl; IMqttConnectionStatus is the Abstractions contract.
        builder.Services.AddSingleton<IMqttConnectionStatus, MqttConnectionStatus>();

        // Health checks — /healthz returns 200 only when MQTT connected AND host past ApplicationStarted.
        // ResponseWriter is HealthzResponseWriter (System.Text.Json, no UI package).
        builder.Services.AddHealthChecks()
            .AddCheck<MqttHealthCheck>("mqtt-and-startup");

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

    /// <summary>
    /// Applies the full Serilog sink configuration to <paramref name="lc"/>.
    /// Extracted for testability — tests can call this directly without a full host.
    /// </summary>
    /// <param name="lc">The <see cref="LoggerConfiguration"/> to configure.</param>
    /// <param name="configuration">Application configuration (reads Serilog and Logging sections).</param>
    /// <param name="environmentName">
    /// ASPNETCORE_ENVIRONMENT value; pass <c>"Docker"</c> to suppress the file sink.
    /// </param>
    /// <param name="services">
    /// Optional service provider for <c>ReadFrom.Services</c>; pass <c>null</c> in tests.
    /// </param>
    internal static void ApplyLoggerConfiguration(
        LoggerConfiguration lc,
        IConfiguration configuration,
        string environmentName,
        IServiceProvider? services = null)
    {
        var configured = lc.ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}{NewLine}{Message:lj}{NewLine}{Exception}",
                formatProvider: null);

        if (services is not null)
            configured = configured.ReadFrom.Services(services);

        // File sink is suppressed in container deployments (ASPNETCORE_ENVIRONMENT=Docker).
        // Containers should rely on `docker logs` (stdout capture) — writing to the writable
        // container layer defeats log capture and fills the layer. Console sink is always active.
        // Closes ID-23 (PLAN-2.1). Non-Docker deploys (Production, Development) retain the file sink.
        if (!string.Equals(environmentName, "Docker", StringComparison.OrdinalIgnoreCase))
        {
            var useCompactJson = string.Equals(
                configuration["Logging:File:CompactJson"],
                "true",
                StringComparison.OrdinalIgnoreCase);

            if (useCompactJson)
            {
                configured.WriteTo.File(
                    formatter: new CompactJsonFormatter(),
                    path: "logs/frigaterelay-.log",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7);
            }
            else
            {
                configured.WriteTo.File(
                    path: "logs/frigaterelay-.log",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}{NewLine}{Message:lj}{NewLine}{Exception}",
                    formatProvider: null);
            }
        }

        var seqUrl = configuration["Serilog:Seq:ServerUrl"];
        if (!string.IsNullOrWhiteSpace(seqUrl))
            configured.WriteTo.Seq(seqUrl, formatProvider: null);
    }
}
