using FluentAssertions;
using FrigateRelay.Abstractions;
using FrigateRelay.Host.Snapshots;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace FrigateRelay.Host.Tests.Snapshots;

/// <summary>
/// End-to-end snapshot resolver tests using real DI + WireMock-stubbed BlueIris and Frigate
/// HTTP servers. Proves the three-tier resolution order (per-action → subscription → global)
/// against real <see cref="ISnapshotProvider"/> implementations, satisfying ROADMAP Phase 5
/// success criterion #3.
/// </summary>
[TestClass]
public sealed class SnapshotResolutionEndToEndTests : IDisposable
{
    // Distinguishable stub payloads.
    private static readonly byte[] BlueIrisBytes = [0xAA, 0xAA, 0xAA, 0xAA];
    private static readonly byte[] FrigateBytes = [0xBB, 0xBB, 0xBB, 0xBB];

    private const string Camera = "front-yard";
    private const string EventId = "evt-e2e-001";

    private WireMockServer _blueIrisServer = null!;
    private WireMockServer _frigateServer = null!;

    // Resolver + capturing logger wired via real DI.
    private ISnapshotResolver _resolver = null!;
    private CapturingLogger<SnapshotResolver> _resolverLogger = null!;
    private ServiceProvider _serviceProvider = null!;

    [TestInitialize]
    public void Initialize()
    {
        // Start WireMock servers on random free ports.
        _blueIrisServer = WireMockServer.Start();
        _frigateServer = WireMockServer.Start();

        // BlueIris snapshot stub: GET /image/front-yard → 0xAA bytes
        _blueIrisServer
            .Given(Request.Create().WithPath("/image/front-yard").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "image/jpeg")
                .WithBody(BlueIrisBytes));

        // Frigate snapshot stub: GET /api/events/{eventId}/snapshot.jpg → 0xBB bytes
        _frigateServer
            .Given(Request.Create().WithPath($"/api/events/{EventId}/snapshot.jpg").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "image/jpeg")
                .WithBody(FrigateBytes));

        var blueIrisPort = _blueIrisServer.Port;
        var frigatePort = _frigateServer.Port;

        // Build configuration with both plugin sections so both registrars activate.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BlueIris:TriggerUrlTemplate"] = $"http://localhost:{blueIrisPort}/trigger/{{camera}}",
                ["BlueIris:SnapshotUrlTemplate"] = $"http://localhost:{blueIrisPort}/image/{{camera}}",
                ["FrigateSnapshot:BaseUrl"] = $"http://localhost:{frigatePort}",
                // Short retry delay so 404-retry in FrigateSnapshotProvider doesn't slow tests.
                ["FrigateSnapshot:Retry404Count"] = "0",
                ["Snapshots:DefaultProviderName"] = "BlueIris",
            })
            .Build();

        _resolverLogger = new CapturingLogger<SnapshotResolver>();

        // Build a ServiceCollection mirroring HostBootstrap's registrations for providers + resolver.
        var services = new ServiceCollection();

        // Shared IMemoryCache (same singleton pattern as HostBootstrap).
        services.AddSingleton<IMemoryCache>(new MemoryCache(new MemoryCacheOptions()));

        // SnapshotResolverOptions.
        services.AddOptions<SnapshotResolverOptions>()
            .Bind(config.GetSection("Snapshots"));

        // Inject the capturing logger for SnapshotResolver — used for log assertions.
        services.AddSingleton<ILogger<SnapshotResolver>>(_resolverLogger);

        // ISnapshotResolver.
        services.AddSingleton<ISnapshotResolver, SnapshotResolver>();

        // Wire plugin registrars exactly as HostBootstrap does — list then RunAll.
        var registrationContext = new PluginRegistrationContext(services, config);
        List<IPluginRegistrar> registrars = [];

        if (config.GetSection("BlueIris").Exists())
            registrars.Add(new FrigateRelay.Plugins.BlueIris.PluginRegistrar());
        if (config.GetSection("FrigateSnapshot").Exists())
            registrars.Add(new FrigateRelay.Plugins.FrigateSnapshot.PluginRegistrar());

        using var bootstrapLoggerFactory = LoggerFactory.Create(_ => { });
        var bootstrapLogger = bootstrapLoggerFactory.CreateLogger<IPluginRegistrar>();
        PluginRegistrarRunner.RunAll(registrars, registrationContext, bootstrapLogger);

        // IHttpClientFactory is required by the providers — add the default implementation.
        services.AddHttpClient();

        _serviceProvider = services.BuildServiceProvider();
        _resolver = _serviceProvider.GetRequiredService<ISnapshotResolver>();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _serviceProvider.Dispose();
        _blueIrisServer.Stop();
        _frigateServer.Stop();
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
        _blueIrisServer?.Stop();
        _frigateServer?.Stop();
    }

    private static EventContext MakeContext() => new()
    {
        EventId = EventId,
        Camera = Camera,
        Label = "person",
        Zones = [],
        StartedAt = DateTimeOffset.UtcNow,
        RawPayload = "{}",
        SnapshotFetcher = _ => ValueTask.FromResult<byte[]?>(null),
    };

    // ---------------------------------------------------------------------------
    // Test 1: global default — no overrides → BlueIris
    // ---------------------------------------------------------------------------

    [TestMethod]
    public async Task Resolve_GlobalDefault_HitsBlueIris()
    {
        var ctx = MakeContext();

        var result = await _resolver.ResolveAsync(ctx, perActionProviderName: null, subscriptionDefaultProviderName: null, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Bytes.Should().Equal(BlueIrisBytes);
        result.ProviderName.Should().Be("BlueIris");

        _blueIrisServer.LogEntries.Should().HaveCount(1, "BlueIris should receive exactly one request");
        _frigateServer.LogEntries.Should().BeEmpty("Frigate should receive no requests when global default picks BlueIris");
    }

    // ---------------------------------------------------------------------------
    // Test 2: subscription override → Frigate (beats global default)
    // ---------------------------------------------------------------------------

    [TestMethod]
    public async Task Resolve_SubscriptionOverride_HitsFrigate()
    {
        var ctx = MakeContext();

        var result = await _resolver.ResolveAsync(ctx, perActionProviderName: null, subscriptionDefaultProviderName: "Frigate", CancellationToken.None);

        result.Should().NotBeNull();
        result!.Bytes.Should().Equal(FrigateBytes);
        result.ProviderName.Should().Be("Frigate");

        _frigateServer.LogEntries.Should().HaveCount(1, "Frigate should receive exactly one request");
        _blueIrisServer.LogEntries.Should().BeEmpty("BlueIris should receive no requests when subscription picks Frigate");
    }

    // ---------------------------------------------------------------------------
    // Test 3: per-action override → Frigate (beats subscription=BlueIris) + debug log
    // ---------------------------------------------------------------------------

    [TestMethod]
    public async Task Resolve_PerActionOverride_HitsFrigate_AndEmitsDebugLog()
    {
        var ctx = MakeContext();

        var result = await _resolver.ResolveAsync(ctx, perActionProviderName: "Frigate", subscriptionDefaultProviderName: "BlueIris", CancellationToken.None);

        result.Should().NotBeNull();
        result!.Bytes.Should().Equal(FrigateBytes);
        result.ProviderName.Should().Be("Frigate");

        _frigateServer.LogEntries.Should().HaveCount(1, "Frigate should receive exactly one request");
        _blueIrisServer.LogEntries.Should().BeEmpty("BlueIris should receive no requests when per-action picks Frigate");

        // Verify the snapshot_resolved debug log emitted by SnapshotResolver.
        _resolverLogger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Debug &&
            e.Message.Contains("snapshot_resolved", StringComparison.Ordinal) &&
            e.Message.Contains("provider=Frigate", StringComparison.Ordinal) &&
            e.Message.Contains("tier=per-action", StringComparison.Ordinal),
            "resolver must emit a Debug log with snapshot_resolved, provider=Frigate, and tier=per-action");
    }
}
