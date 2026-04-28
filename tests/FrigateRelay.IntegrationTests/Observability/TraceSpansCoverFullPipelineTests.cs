using System.Diagnostics;
using System.Diagnostics.Metrics;
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
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Trace;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace FrigateRelay.IntegrationTests.Observability;

/// <summary>
/// Phase 9 ROADMAP success criteria integration tests (PLAN-3.1 Task 3 / CONTEXT-9 D5).
///
/// Both tests spin up a real Mosquitto container (Testcontainers) + WireMock stubs for
/// BlueIris, Pushover, FrigateSnapshot, and CodeProject.AI — the full pipeline.
/// OpenTelemetry capture uses <c>InMemoryExporter&lt;Activity&gt;</c> wrapped in
/// <c>SimpleActivityExportProcessor</c> so spans are available synchronously after
/// <c>tracerProvider.ForceFlush()</c>.
/// </summary>
[TestClass]
public sealed class TraceSpansCoverFullPipelineTests
{
    // -----------------------------------------------------------------------
    // ROADMAP success criterion 1:
    // TraceSpans_CoverFullPipeline — 1 MQTT event → assert 1 mqtt.receive root
    // + children event.match, dispatch.enqueue, action.<name>.execute, all
    // sharing the root TraceId and correctly parented via ParentSpanId.
    // -----------------------------------------------------------------------

    [TestMethod]
    [Timeout(60_000)]
    public async Task TraceSpans_CoverFullPipeline()
    {
        await using var mosquitto = new MosquittoFixture();
        await mosquitto.InitializeAsync().ConfigureAwait(false);

        using var wireMockBlueIris  = StartBlueIrisStub();
        using var wireMockPushover  = StartPushoverStub();
        using var wireMockFrigate   = StartFrigateSnapshotStub();
        using var wireMockCodeProj  = StartCodeProjectStub(confidence: 0.92); // above MinConfidence

        var activities = new List<Activity>();
        var exporter   = new InMemoryExporter<Activity>(activities);

        using var app = await BuildHostAsync(
            mosquitto, wireMockBlueIris, wireMockPushover, wireMockFrigate, wireMockCodeProj,
            traceProcessor: new SimpleActivityExportProcessor(exporter)).ConfigureAwait(false);

        try
        {
            await PublishOneEventAsync(mosquitto, "ev-trace-001").ConfigureAwait(false);

            // Poll until both action stubs record their hit (≤ 15s).
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                if (wireMockBlueIris.LogEntries.Any() && wireMockPushover.LogEntries.Any())
                    break;
                await Task.Delay(100).ConfigureAwait(false);
            }

            // Give spans a moment to close, then flush the in-memory exporter.
            await Task.Delay(200).ConfigureAwait(false);
        }
        finally
        {
            await app.StopAsync().ConfigureAwait(false);
        }

        // ── Assertions ────────────────────────────────────────────────────────

        // 1. Exactly one mqtt.receive root span.
        var receiveSpan = activities.Where(a => a.DisplayName == "mqtt.receive").ToList();
        receiveSpan.Should().HaveCount(1, "one mqtt.receive span per published event");
        var root = receiveSpan[0];
        root.Kind.Should().Be(ActivityKind.Server);
        root.ParentSpanId.Should().Be(default(ActivitySpanId),
            "mqtt.receive must be a root span (no parent)");

        var traceId = root.TraceId;

        // 2. All FrigateRelay spans share the same TraceId.
        var allSpans = activities.Where(a => a.TraceId == traceId).ToList();
        allSpans.Should().HaveCountGreaterThanOrEqualTo(4,
            "expect at least mqtt.receive, event.match, dispatch.enqueue, and one action span");

        // 3. event.match is a child of mqtt.receive.
        var matchSpan = allSpans.FirstOrDefault(a => a.DisplayName == "event.match");
        matchSpan.Should().NotBeNull("event.match span must be emitted");
        matchSpan!.ParentSpanId.Should().Be(root.SpanId,
            "event.match must be parented to mqtt.receive");

        // 4. dispatch.enqueue is a child of mqtt.receive (Producer kind, CONTEXT-9 D1).
        var enqueueSpan = allSpans.FirstOrDefault(a => a.DisplayName == "dispatch.enqueue");
        enqueueSpan.Should().NotBeNull("dispatch.enqueue span must be emitted");
        enqueueSpan!.Kind.Should().Be(ActivityKind.Producer);
        enqueueSpan.ParentSpanId.Should().Be(root.SpanId,
            "dispatch.enqueue must be parented to mqtt.receive");

        // 5. At least one action.<name>.execute span exists (Consumer kind).
        var actionSpans = allSpans
            .Where(a => a.DisplayName.EndsWith(".execute", StringComparison.Ordinal))
            .ToList();
        actionSpans.Should().NotBeEmpty("at least one action execute span must be emitted");
        actionSpans.Should().AllSatisfy(a =>
            a.Kind.Should().Be(ActivityKind.Consumer,
                $"action span '{a.DisplayName}' must be Consumer kind"));

        // 5b. validator.<name>.check span is parented to its action span (REVIEW-3.1 Important #2).
        // Span name uses the validator instance key from config (e.g. "strict-person"),
        // not the plugin type name. See ChannelActionDispatcher: $"validator.{validator.Name.ToLowerInvariant()}.check"
        var validatorSpan = allSpans.FirstOrDefault(a => a.DisplayName == "validator.strict-person.check");
        validatorSpan.Should().NotBeNull("validator span must be emitted under its action span");
        var pushoverSpan = actionSpans.FirstOrDefault(a => a.DisplayName == "action.pushover.execute");
        if (pushoverSpan is not null && validatorSpan is not null)
        {
            validatorSpan.ParentSpanId.Should().Be(pushoverSpan.SpanId,
                "validator span must be parented to its action span (D8 hierarchy)");
        }

        // 6. Every FrigateRelay span carries the event.id attribute (D8 join-by-correlation).
        allSpans.Should().AllSatisfy(a =>
            GetTag(a, "event.id").Should().NotBeNullOrEmpty(
                $"span '{a.DisplayName}' must carry event.id tag for Tempo/Jaeger correlation"));
    }

    // -----------------------------------------------------------------------
    // ROADMAP success criterion 2:
    // Counters_Increment_PerD3_TagDimensions — 1 event, 2 actions (one with
    // validator, one without); assert counter totals and tag dimensions.
    // -----------------------------------------------------------------------

    [TestMethod]
    [Timeout(60_000)]
    public async Task Counters_Increment_PerD3_TagDimensions()
    {
        await using var mosquitto = new MosquittoFixture();
        await mosquitto.InitializeAsync().ConfigureAwait(false);

        using var wireMockBlueIris  = StartBlueIrisStub();
        using var wireMockPushover  = StartPushoverStub();
        using var wireMockFrigate   = StartFrigateSnapshotStub();
        using var wireMockCodeProj  = StartCodeProjectStub(confidence: 0.92); // validator passes

        var measurements = new List<(string name, long value, IReadOnlyList<KeyValuePair<string, object?>> tags)>();
        using var meterListener = BuildMeterListener(measurements);

        using var app = await BuildHostAsync(
            mosquitto, wireMockBlueIris, wireMockPushover, wireMockFrigate, wireMockCodeProj,
            traceProcessor: null).ConfigureAwait(false);

        try
        {
            await PublishOneEventAsync(mosquitto, "ev-counter-001").ConfigureAwait(false);

            // Poll until both stubs record hits (validator passes → Pushover fires too).
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                if (wireMockBlueIris.LogEntries.Any() && wireMockPushover.LogEntries.Any())
                    break;
                await Task.Delay(100).ConfigureAwait(false);
            }

            await Task.Delay(200).ConfigureAwait(false); // let counter callbacks drain
            meterListener.RecordObservableInstruments();
        }
        finally
        {
            await app.StopAsync().ConfigureAwait(false);
        }

        // ── Counter assertions (ROADMAP metric test criterion) ────────────────

        long Total(string name) =>
            measurements.Where(m => m.name == name).Sum(m => m.value);

        Total("frigaterelay.events.received").Should().Be(1,
            "one MQTT event received");
        Total("frigaterelay.events.matched").Should().Be(1,
            "one subscription matched");
        Total("frigaterelay.actions.dispatched").Should().Be(2,
            "two actions (BlueIris + Pushover) dispatched");
        Total("frigaterelay.actions.succeeded").Should().Be(2,
            "both actions succeeded (validator passes for Pushover)");
        Total("frigaterelay.validators.passed").Should().Be(1,
            "one validator (strict-person on Pushover) passed");
        Total("frigaterelay.validators.rejected").Should().Be(0,
            "no validators rejected");
        Total("frigaterelay.errors.unhandled").Should().Be(0,
            "no unhandled errors");

        // Tag dimension spot-checks (D3).
        var dispatched = measurements.Where(m => m.name == "frigaterelay.actions.dispatched").ToList();
        dispatched.Should().HaveCount(2);
        dispatched.Select(m => TagValue(m.tags, "action")).Should()
            .Contain("BlueIris").And.Contain("Pushover");

        var validatorPassed = measurements
            .Where(m => m.name == "frigaterelay.validators.passed")
            .ToList();
        validatorPassed.Should().HaveCount(1);
        TagValue(validatorPassed[0].tags, "validator").Should().Be("strict-person");
        TagValue(validatorPassed[0].tags, "action").Should().Be("Pushover");
    }

    // -----------------------------------------------------------------------
    // Helpers — WireMock stubs
    // -----------------------------------------------------------------------

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
                  .WithBody("{\"status\":1,\"request\":\"itest-obs\"}"));
        return server;
    }

    private static WireMockServer StartFrigateSnapshotStub()
    {
        var server = WireMockServer.Start();
        server.Given(Request.Create().UsingGet().WithPath("/api/events/*"))
              .RespondWith(Response.Create()
                  .WithStatusCode(200)
                  .WithHeader("Content-Type", "image/jpeg")
                  .WithBody(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }));
        return server;
    }

    private static WireMockServer StartCodeProjectStub(double confidence)
    {
        var server = WireMockServer.Start();
        var body = "{\"success\":true,\"code\":200,\"predictions\":[{" +
                   "\"label\":\"person\"," +
                   "\"confidence\":" + confidence.ToString("0.00", CultureInfo.InvariantCulture) +
                   ",\"x_min\":1,\"y_min\":2,\"x_max\":3,\"y_max\":4}]}";
        server.Given(Request.Create().UsingPost().WithPath("/v1/vision/detection"))
              .RespondWith(Response.Create()
                  .WithStatusCode(200)
                  .WithHeader("Content-Type", "application/json")
                  .WithBody(body));
        return server;
    }

    // -----------------------------------------------------------------------
    // Helpers — host builder
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds and starts the full host. If <paramref name="traceProcessor"/> is non-null it is
    /// added to the TracerProvider so spans are captured in-memory
    /// (SimpleActivityExportProcessor for synchronous export).
    /// </summary>
    private static async Task<IHost> BuildHostAsync(
        MosquittoFixture mosquitto,
        WireMockServer blueIris,
        WireMockServer pushover,
        WireMockServer frigateSnap,
        WireMockServer codeProject,
        BaseProcessor<Activity>? traceProcessor)
    {
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BlueIris:TriggerUrlTemplate"] = $"{blueIris.Urls[0]}/admin?camera={{camera}}&trigger=1",
            ["BlueIris:RequestTimeout"]     = "00:00:05",

            ["Pushover:AppToken"]           = "test-app-token",
            ["Pushover:UserKey"]            = "test-user-key",
            ["Pushover:BaseAddress"]        = pushover.Urls[0],
            ["Pushover:RequestTimeout"]     = "00:00:05",

            ["FrigateSnapshot:BaseUrl"]         = frigateSnap.Urls[0],
            ["FrigateSnapshot:RequestTimeout"]  = "00:00:05",
            ["FrigateSnapshot:Retry404Count"]   = "0",
            ["Snapshots:DefaultProviderName"]   = "Frigate",

            ["Validators:strict-person:Type"]             = "CodeProjectAi",
            ["Validators:strict-person:BaseUrl"]          = codeProject.Urls[0],
            ["Validators:strict-person:MinConfidence"]    = "0.7",
            ["Validators:strict-person:AllowedLabels:0"]  = "person",
            ["Validators:strict-person:OnError"]          = "FailClosed",
            ["Validators:strict-person:Timeout"]          = "00:00:05",

            ["FrigateMqtt:Server"] = mosquitto.Hostname,
            ["FrigateMqtt:Port"]   = mosquitto.Port.ToString(CultureInfo.InvariantCulture),
            ["FrigateMqtt:Topic"]  = "frigate/events",

            ["Subscriptions:0:Name"]                        = "FrontCam",
            ["Subscriptions:0:Camera"]                      = "front",
            ["Subscriptions:0:Label"]                       = "person",
            ["Subscriptions:0:Actions:0:Plugin"]            = "BlueIris",
            ["Subscriptions:0:Actions:1:Plugin"]            = "Pushover",
            ["Subscriptions:0:Actions:1:Validators:0"]      = "strict-person",
        });

        // Suppress most logging noise in tests but keep Warning+ so errors surface.
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        // Register all plugins + host services via standard bootstrap.
        HostBootstrap.ConfigureServices(builder);

        // If a trace processor was provided, attach it so the in-memory exporter
        // receives spans.  We hook into the OTel TracerProvider AFTER ConfigureServices
        // has called AddOpenTelemetry() by re-configuring the same builder extension.
        if (traceProcessor is not null)
        {
            builder.Services.ConfigureOpenTelemetryTracerProvider(
                b => b.AddProcessor(traceProcessor));
        }

        var app = builder.Build();
        HostBootstrap.ValidateStartup(app.Services);
        await app.StartAsync().ConfigureAwait(false);

        // Wait for MQTT client to connect (mirrors existing test pattern).
        var hostMqttClient = app.Services.GetRequiredService<IMqttClient>();
        var connectDeadline = DateTime.UtcNow.AddSeconds(10);
        while (!hostMqttClient.IsConnected && DateTime.UtcNow < connectDeadline)
            await Task.Delay(50).ConfigureAwait(false);
        hostMqttClient.IsConnected.Should().BeTrue("host MQTT client must connect within 10s");

        return app;
    }

    // -----------------------------------------------------------------------
    // Helpers — MQTT publisher
    // -----------------------------------------------------------------------

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

    // -----------------------------------------------------------------------
    // Helpers — MeterListener + tag extraction
    // -----------------------------------------------------------------------

    private static MeterListener BuildMeterListener(
        List<(string name, long value, IReadOnlyList<KeyValuePair<string, object?>> tags)> sink)
    {
        var ml = new MeterListener();
        ml.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "FrigateRelay")
                listener.EnableMeasurementEvents(instrument);
        };
        ml.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            var tagList = new List<KeyValuePair<string, object?>>();
            foreach (var t in tags)
                tagList.Add(new KeyValuePair<string, object?>(t.Key, t.Value));
            lock (sink)
                sink.Add((instrument.Name, measurement, tagList));
        });
        ml.Start();
        return ml;
    }

    private static string? GetTag(Activity activity, string key)
    {
        foreach (var tag in activity.TagObjects)
            if (tag.Key == key) return tag.Value?.ToString();
        return null;
    }

    private static string? TagValue(IReadOnlyList<KeyValuePair<string, object?>> tags, string key)
    {
        foreach (var kv in tags)
            if (kv.Key == key) return kv.Value?.ToString();
        return null;
    }
}
