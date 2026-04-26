using FluentAssertions;
using FrigateRelay.Abstractions;
using FrigateRelay.Plugins.FrigateSnapshot;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace FrigateRelay.Plugins.FrigateSnapshot.Tests;

[TestClass]
public class FrigateSnapshotProviderTests
{
    // Minimal JPEG magic bytes (SOI marker).
    private static readonly byte[] FakeJpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];

    private static EventContext NewCtx(string eventId = "abc123") => new()
    {
        EventId = eventId,
        Camera = "front",
        Label = "person",
        Zones = Array.Empty<string>(),
        StartedAt = DateTimeOffset.UnixEpoch,
        RawPayload = "{}",
        SnapshotFetcher = static _ => ValueTask.FromResult<byte[]?>(null),
    };

    private static SnapshotRequest NewRequest(string eventId = "abc123", bool includeBbox = false) => new()
    {
        Context = NewCtx(eventId),
        IncludeBoundingBox = includeBbox,
    };

    /// <summary>
    /// Builds a <see cref="FrigateSnapshotProvider"/> wired to a real <see cref="IHttpClientFactory"/>
    /// pointed at the given WireMock server URL, using the provided options overrides.
    /// </summary>
    private static (FrigateSnapshotProvider Provider, CapturingLogger<FrigateSnapshotProvider> Logger)
        BuildProvider(string baseUrl, Action<FrigateSnapshotOptions>? configureOptions = null)
    {
        var opts = new FrigateSnapshotOptions { BaseUrl = baseUrl };
        if (configureOptions != null)
        {
            // Build a fresh record with the overrides applied via a helper.
            opts = ApplyOverrides(opts, configureOptions);
        }

        var logger = new CapturingLogger<FrigateSnapshotProvider>();

        var services = new ServiceCollection();
        services.AddHttpClient("FrigateSnapshot", client =>
        {
            client.BaseAddress = new Uri(opts.BaseUrl);
            client.Timeout = opts.RequestTimeout;
        });

        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IHttpClientFactory>();
        var provider = new FrigateSnapshotProvider(factory, new OptionsWrapper<FrigateSnapshotOptions>(opts), logger);
        return (provider, logger);
    }

    // Record mutation helper — creates a new record with specific properties overridden.
    private static FrigateSnapshotOptions ApplyOverrides(FrigateSnapshotOptions opts, Action<OptionsMutator> configure)
    {
        var mutator = new OptionsMutator(opts);
        configure(mutator);
        return mutator.Build();
    }

    private static FrigateSnapshotOptions ApplyOverrides(FrigateSnapshotOptions opts, Action<FrigateSnapshotOptions> _)
        => opts; // unused overload guard

    // Mutable builder for FrigateSnapshotOptions (records are immutable, so we use with-expressions).
    private sealed class OptionsMutator(FrigateSnapshotOptions source)
    {
        private FrigateSnapshotOptions _opts = source;
        public void UseThumbnail(bool v) => _opts = _opts with { UseThumbnail = v };
        public void IncludeBoundingBox(bool v) => _opts = _opts with { IncludeBoundingBox = v };
        public void Retry404Count(int v) => _opts = _opts with { Retry404Count = v };
        public void Retry404Delay(TimeSpan v) => _opts = _opts with { Retry404Delay = v };
        public void ApiToken(string v) => _opts = _opts with { ApiToken = v };
        public FrigateSnapshotOptions Build() => _opts;
    }

    // Simplified build helper accepting a pre-built options record directly.
    private static (FrigateSnapshotProvider Provider, CapturingLogger<FrigateSnapshotProvider> Logger)
        BuildProviderWithOptions(FrigateSnapshotOptions opts)
    {
        var logger = new CapturingLogger<FrigateSnapshotProvider>();
        var services = new ServiceCollection();
        services.AddHttpClient("FrigateSnapshot", client =>
        {
            client.BaseAddress = new Uri(opts.BaseUrl);
            client.Timeout = opts.RequestTimeout;
        });
        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IHttpClientFactory>();
        return (new FrigateSnapshotProvider(factory, new OptionsWrapper<FrigateSnapshotOptions>(opts), logger), logger);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Test 1: happy path — snapshot.jpg 200 returns a populated SnapshotResult
    // ─────────────────────────────────────────────────────────────────────
    [TestMethod]
    public async Task FetchAsync_SnapshotJpg_200_ReturnsResult()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/api/events/abc123/snapshot.jpg").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "image/jpeg")
                .WithBody(FakeJpegBytes));

        var opts = new FrigateSnapshotOptions { BaseUrl = server.Urls[0] };
        var (provider, _) = BuildProviderWithOptions(opts);

        var result = await provider.FetchAsync(NewRequest("abc123"), CancellationToken.None);

        result.Should().NotBeNull();
        result!.ProviderName.Should().Be("Frigate");
        result.Bytes.Should().Equal(FakeJpegBytes);
        result.ContentType.Should().Be("image/jpeg");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Test 2: UseThumbnail=true hits /thumbnail.jpg, NOT /snapshot.jpg
    // ─────────────────────────────────────────────────────────────────────
    [TestMethod]
    public async Task FetchAsync_ThumbnailMode_HitsThumbnailPath()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/api/events/abc123/thumbnail.jpg").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "image/jpeg")
                .WithBody(FakeJpegBytes));

        var opts = new FrigateSnapshotOptions { BaseUrl = server.Urls[0], UseThumbnail = true };
        var (provider, _) = BuildProviderWithOptions(opts);

        var result = await provider.FetchAsync(NewRequest("abc123"), CancellationToken.None);

        result.Should().NotBeNull("thumbnail path should have been hit");

        // Verify WireMock received the thumbnail path, not the snapshot path.
        server.LogEntries.Should().HaveCount(1);
        server.LogEntries.Single().RequestMessage!.Path
            .Should().Be("/api/events/abc123/thumbnail.jpg");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Test 3: IncludeBoundingBox=true appends bbox=1 query param
    // ─────────────────────────────────────────────────────────────────────
    [TestMethod]
    public async Task FetchAsync_BoundingBox_AppendsQueryParam()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/api/events/abc123/snapshot.jpg").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "image/jpeg")
                .WithBody(FakeJpegBytes));

        var opts = new FrigateSnapshotOptions { BaseUrl = server.Urls[0], IncludeBoundingBox = true };
        var (provider, _) = BuildProviderWithOptions(opts);

        await provider.FetchAsync(NewRequest("abc123"), CancellationToken.None);

        server.LogEntries.Should().HaveCount(1);
        var query = server.LogEntries.Single().RequestMessage!.Query;
        query.Should().NotBeNull();
        query!.Should().ContainKey("bbox");
        query!["bbox"]!.Should().ContainSingle("1");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Test 4: 404 with Retry404Count=0 — returns null, logs warning, 1 request
    // ─────────────────────────────────────────────────────────────────────
    [TestMethod]
    public async Task FetchAsync_404_NoRetries_ReturnsNull()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/api/events/abc123/snapshot.jpg").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404));

        var opts = new FrigateSnapshotOptions { BaseUrl = server.Urls[0], Retry404Count = 0 };
        var (provider, logger) = BuildProviderWithOptions(opts);

        var result = await provider.FetchAsync(NewRequest("abc123"), CancellationToken.None);

        result.Should().BeNull("404 with no retries must return null (D2 fail-open)");
        server.LogEntries.Should().HaveCount(1, "no retry means exactly 1 request");
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Warning,
            "a warning should be logged on non-success");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Test 5: 404-retry loop — 1st call 404, 2nd call 200, result non-null
    // ─────────────────────────────────────────────────────────────────────
    [TestMethod]
    public async Task FetchAsync_404_OneRetry_HitsTwice_ThenSucceeds()
    {
        using var server = WireMockServer.Start();

        // First call → 404
        server
            .Given(Request.Create().WithPath("/api/events/abc123/snapshot.jpg").UsingGet())
            .InScenario("404-retry")
            .WillSetStateTo("retried")
            .RespondWith(Response.Create().WithStatusCode(404));

        // Second call → 200
        server
            .Given(Request.Create().WithPath("/api/events/abc123/snapshot.jpg").UsingGet())
            .InScenario("404-retry")
            .WhenStateIs("retried")
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "image/jpeg")
                .WithBody(FakeJpegBytes));

        var opts = new FrigateSnapshotOptions
        {
            BaseUrl = server.Urls[0],
            Retry404Count = 1,
            Retry404Delay = TimeSpan.FromMilliseconds(50), // fast for test speed
        };
        var (provider, logger) = BuildProviderWithOptions(opts);

        var result = await provider.FetchAsync(NewRequest("abc123"), CancellationToken.None);

        result.Should().NotBeNull("second attempt should succeed");
        server.LogEntries.Should().HaveCount(2, "exactly 2 requests: initial 404 + 1 retry");

        logger.Entries.Should().Contain(
            e => e.Id.Name == "frigate_snapshot_404_retry",
            "a 404-retry debug log should be emitted");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Test 6: ApiToken sends Authorization: Bearer header
    // ─────────────────────────────────────────────────────────────────────
    [TestMethod]
    public async Task FetchAsync_ApiToken_SendsBearerHeader()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/api/events/abc123/snapshot.jpg").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "image/jpeg")
                .WithBody(FakeJpegBytes));

        var opts = new FrigateSnapshotOptions
        {
            BaseUrl = server.Urls[0],
            ApiToken = "test-token-not-real",
        };
        var (provider, _) = BuildProviderWithOptions(opts);

        await provider.FetchAsync(NewRequest("abc123"), CancellationToken.None);

        server.LogEntries.Should().HaveCount(1);
        var headers = server.LogEntries.Single().RequestMessage!.Headers;
        headers.Should().ContainKey("Authorization");
        headers!["Authorization"].Should().ContainSingle("Bearer test-token-not-real");
    }
}
