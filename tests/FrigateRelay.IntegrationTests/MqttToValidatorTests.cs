using System.Collections.Concurrent;
using System.Globalization;
using FluentAssertions;
using FrigateRelay.Host;
using FrigateRelay.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MQTTnet;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace FrigateRelay.IntegrationTests;

[TestClass]
public sealed class MqttToValidatorTests
{
    // -------------------------------------------------------------------------
    // Test 1: validator on Pushover only — low-confidence verdict short-circuits
    // Pushover; BlueIris (no validator) still fires.
    // -------------------------------------------------------------------------
    [TestMethod]
    [Timeout(60_000)]
    public async Task Validator_ShortCircuits_OnlyAttachedAction()
    {
        await using var mosquitto = new MosquittoFixture();
        await mosquitto.InitializeAsync().ConfigureAwait(false);

        using var wireMockBlueIris = StartBlueIrisStub();
        using var wireMockPushover = StartPushoverStub();
        using var wireMockFrigate = StartFrigateSnapshotStub();
        using var wireMockCodeProject = StartCodeProjectStub(confidence: 0.20); // BELOW MinConfidence=0.7

        var captureProvider = new CapturingLoggerProvider();
        using var app = await BuildHostAsync(
            mosquitto, wireMockBlueIris, wireMockPushover, wireMockFrigate, wireMockCodeProject,
            captureProvider).ConfigureAwait(false);

        try
        {
            await PublishOneEventAsync(mosquitto, eventId: "ev-validator-reject-001").ConfigureAwait(false);

            // Allow up to 10s for BlueIris to receive its trigger and CodeProject to be invoked.
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                if (wireMockBlueIris.LogEntries.Any() && wireMockCodeProject.LogEntries.Any())
                    break;
                await Task.Delay(100).ConfigureAwait(false);
            }

            // BlueIris fired exactly once (no validator gate).
            wireMockBlueIris.FindLogEntries(Request.Create().UsingGet().WithPath("/admin"))
                .Should().HaveCount(1, "BlueIris has no validator and must fire");

            // CodeProject was invoked exactly once (Pushover's validator chain ran).
            wireMockCodeProject.FindLogEntries(Request.Create().UsingPost().WithPath("/v1/vision/detection"))
                .Should().HaveCount(1, "CodeProject.AI was queried once for Pushover's validator");

            // Give the dispatcher a moment more to ensure Pushover would NOT have been called
            // even if it were going to (validator short-circuited).
            await Task.Delay(500).ConfigureAwait(false);

            // Pushover received NO requests (validator short-circuited that action only).
            wireMockPushover.FindLogEntries(Request.Create().UsingPost().WithPath("/1/messages.json"))
                .Should().BeEmpty("Pushover validator returned Verdict.Fail; action MUST be skipped");

            // Structured validator_rejected log entry is present.
            var rejectedEntries = captureProvider.Entries
                .Where(e => e.Category == "FrigateRelay.Host.Dispatch.ChannelActionDispatcher"
                         && e.EventId.Name == "ValidatorRejected")
                .ToList();
            rejectedEntries.Should().HaveCount(1, "exactly one validator_rejected log per failed verdict");
            rejectedEntries[0].Message.Should().Contain("Pushover");
            rejectedEntries[0].Message.Should().Contain("strict-person");
        }
        finally
        {
            await app.StopAsync().ConfigureAwait(false);
        }
    }

    // -------------------------------------------------------------------------
    // Test 2: high-confidence verdict → both actions fire end-to-end.
    // -------------------------------------------------------------------------
    [TestMethod]
    [Timeout(60_000)]
    public async Task Validator_Pass_BothActionsFire()
    {
        await using var mosquitto = new MosquittoFixture();
        await mosquitto.InitializeAsync().ConfigureAwait(false);

        using var wireMockBlueIris = StartBlueIrisStub();
        using var wireMockPushover = StartPushoverStub();
        using var wireMockFrigate = StartFrigateSnapshotStub();
        using var wireMockCodeProject = StartCodeProjectStub(confidence: 0.92); // ABOVE MinConfidence=0.7

        var captureProvider = new CapturingLoggerProvider();
        using var app = await BuildHostAsync(
            mosquitto, wireMockBlueIris, wireMockPushover, wireMockFrigate, wireMockCodeProject,
            captureProvider).ConfigureAwait(false);

        try
        {
            await PublishOneEventAsync(mosquitto, eventId: "ev-validator-pass-001").ConfigureAwait(false);

            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                if (wireMockBlueIris.LogEntries.Any() && wireMockPushover.LogEntries.Any())
                    break;
                await Task.Delay(100).ConfigureAwait(false);
            }

            wireMockBlueIris.FindLogEntries(Request.Create().UsingGet().WithPath("/admin"))
                .Should().HaveCount(1, "BlueIris fires unconditionally");
            wireMockPushover.FindLogEntries(Request.Create().UsingPost().WithPath("/1/messages.json"))
                .Should().HaveCount(1, "Pushover fires when validator passes");
            wireMockCodeProject.FindLogEntries(Request.Create().UsingPost().WithPath("/v1/vision/detection"))
                .Should().HaveCount(1, "CodeProject.AI was queried exactly once for the validator chain");

            captureProvider.Entries
                .Should().NotContain(e => e.EventId.Name == "ValidatorRejected",
                    "no validator_rejected log when verdict passes");
        }
        finally
        {
            await app.StopAsync().ConfigureAwait(false);
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static WireMockServer StartBlueIrisStub()
    {
        var server = WireMockServer.Start();
        server.Given(Request.Create().UsingGet().WithPath("/admin"))
            .RespondWith(Response.Create().WithStatusCode(200));
        return server;
    }

    private static WireMockServer StartPushoverStub()
    {
        var server = WireMockServer.Start();
        server.Given(Request.Create().UsingPost().WithPath("/1/messages.json"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"status\":1,\"request\":\"itest-req\"}"));
        return server;
    }

    private static WireMockServer StartFrigateSnapshotStub()
    {
        var server = WireMockServer.Start();
        // Match any /api/events/.../snapshot.jpg with fake JPEG bytes.
        server.Given(Request.Create().UsingGet().WithPath("/api/events/*"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "image/jpeg")
                .WithBody(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 }));
        return server;
    }

    private static WireMockServer StartCodeProjectStub(double confidence)
    {
        var server = WireMockServer.Start();
        var responseBody = "{\"success\":true,\"code\":200,\"predictions\":[{" +
                           "\"label\":\"person\"," +
                           "\"confidence\":" + confidence.ToString("0.00", CultureInfo.InvariantCulture) +
                           ",\"x_min\":1,\"y_min\":2,\"x_max\":3,\"y_max\":4}]}";
        server.Given(Request.Create().UsingPost().WithPath("/v1/vision/detection"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(responseBody));
        return server;
    }

    private static async Task<IHost> BuildHostAsync(
        MosquittoFixture mosquitto,
        WireMockServer blueIris,
        WireMockServer pushover,
        WireMockServer frigateSnap,
        WireMockServer codeProject,
        CapturingLoggerProvider capture)
    {
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BlueIris:TriggerUrlTemplate"] = $"{blueIris.Urls[0]}/admin?camera={{camera}}&trigger=1",
            ["BlueIris:RequestTimeout"] = "00:00:05",

            ["Pushover:AppToken"] = "test-app-token",
            ["Pushover:UserKey"] = "test-user-key",
            ["Pushover:BaseAddress"] = pushover.Urls[0],
            ["Pushover:RequestTimeout"] = "00:00:05",

            ["FrigateSnapshot:BaseUrl"] = frigateSnap.Urls[0],
            ["FrigateSnapshot:RequestTimeout"] = "00:00:05",
            ["FrigateSnapshot:Retry404Count"] = "0",
            ["Snapshots:DefaultProviderName"] = "Frigate",

            ["Validators:strict-person:Type"] = "CodeProjectAi",
            ["Validators:strict-person:BaseUrl"] = codeProject.Urls[0],
            ["Validators:strict-person:MinConfidence"] = "0.7",
            ["Validators:strict-person:AllowedLabels:0"] = "person",
            ["Validators:strict-person:OnError"] = "FailClosed",
            ["Validators:strict-person:Timeout"] = "00:00:05",

            ["FrigateMqtt:Server"] = mosquitto.Hostname,
            ["FrigateMqtt:Port"] = mosquitto.Port.ToString(CultureInfo.InvariantCulture),
            ["FrigateMqtt:Topic"] = "frigate/events",

            ["Subscriptions:0:Name"] = "FrontCam",
            ["Subscriptions:0:Camera"] = "front",
            ["Subscriptions:0:Label"] = "person",
            ["Subscriptions:0:Actions:0:Plugin"] = "BlueIris",
            ["Subscriptions:0:Actions:1:Plugin"] = "Pushover",
            ["Subscriptions:0:Actions:1:Validators:0"] = "strict-person",
        });

        builder.Logging.SetMinimumLevel(LogLevel.Debug);

        HostBootstrap.ConfigureServices(builder);

        // Register the capture provider AFTER ConfigureServices so it survives
        // AddSerilog's logging-provider replacement. Service-collection registration
        // bypasses ILoggingBuilder's pipeline, which AddSerilog clears.
        // (REVIEW-3.1 Important #3 / Wave 2 regression remediation.)
        builder.Services.AddSingleton<ILoggerProvider>(capture);

        var app = builder.Build();
        HostBootstrap.ValidateStartup(app.Services);
        await app.StartAsync().ConfigureAwait(false);

        // Wait for the host MQTT client to connect.
        var hostMqttClient = app.Services.GetRequiredService<IMqttClient>();
        var connectDeadline = DateTime.UtcNow.AddSeconds(10);
        while (!hostMqttClient.IsConnected && DateTime.UtcNow < connectDeadline)
            await Task.Delay(50).ConfigureAwait(false);
        hostMqttClient.IsConnected.Should().BeTrue("host MQTT client must connect within 10s");

        return app;
    }

    private static async Task PublishOneEventAsync(MosquittoFixture mosquitto, string eventId)
    {
        var payload = $$"""
            {
              "type": "new",
              "before": null,
              "after": {
                "id": "{{eventId}}",
                "camera": "front",
                "label": "person",
                "stationary": false,
                "false_positive": false,
                "score": 0.88,
                "start_time": 1745558400.0,
                "current_zones": ["driveway"],
                "entered_zones": ["driveway"]
              }
            }
            """;

        var factory = new MqttClientFactory();
        using var client = factory.CreateMqttClient();
        await client.ConnectAsync(new MqttClientOptionsBuilder()
            .WithTcpServer(mosquitto.Hostname, mosquitto.Port)
            .Build()).ConfigureAwait(false);
        await client.PublishStringAsync("frigate/events", payload).ConfigureAwait(false);
        if (client.IsConnected)
            await client.DisconnectAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Per-test log sink capturing all entries from all categories. Used to assert
    /// presence/absence of the structured validator_rejected log emitted by
    /// ChannelActionDispatcher (EventId 20).
    /// </summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentBag<CapturedEntry> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CategoryLogger(categoryName, Entries);

        public void Dispose() { /* no-op */ }

        private sealed class CategoryLogger(string category, ConcurrentBag<CapturedEntry> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                sink.Add(new CapturedEntry(category, logLevel, eventId, formatter(state, exception)));
            }
        }
    }

    private sealed record CapturedEntry(string Category, LogLevel Level, EventId EventId, string Message);
}
