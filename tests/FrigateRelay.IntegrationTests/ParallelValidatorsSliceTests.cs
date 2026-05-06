using System.Globalization;
using FluentAssertions;
using FrigateRelay.Host;
using FrigateRelay.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MQTTnet;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace FrigateRelay.IntegrationTests;

/// <summary>
/// End-to-end coverage of the per-action <c>ParallelValidators: true</c> opt-in (#23) with
/// two validators (CPAI + Roboflow) running concurrently against the same dispatched action.
/// Real Mosquitto via Testcontainers; WireMock for both validator HTTP stubs and for the
/// BlueIris action trigger stub.
///
/// <para><strong>Concurrency proof method — SemaphoreSlim gate (PLAN-3.3 §spec line 66–67 fallback).</strong>
/// The timing-based approach (measuring wall-clock elapsed) was evaluated and rejected: the
/// Frigate snapshot pre-resolve step (WireMock HTTP round-trip inside the dispatcher, before
/// validators run) contributes 200–2500 ms of inherently variable overhead on the same
/// machine, making the sequential-vs-parallel gap of 2× ValidatorDelayMs meaningless against
/// that noise floor. The SemaphoreSlim gate proves concurrency deterministically: both
/// validator WireMock stubs block waiting for a <see cref="SemaphoreSlim"/> held by the test;
/// when both stubs are simultaneously waiting, the test releases the semaphore twice. This
/// proves both validators reached the HTTP layer <em>before either one returned</em> — i.e.,
/// they are scheduled concurrently, not sequentially.</para>
/// </summary>
[TestClass]
public sealed class ParallelValidatorsSliceTests
{
    // -------------------------------------------------------------------------
    // Concurrency-gate semaphore capacity.
    // Both validator stubs acquire this semaphore on entry (count starts at 0,
    // so both block). The test waits until both are blocked, then releases twice.
    // If validators ran sequentially the second stub would not even start until
    // the first returned — so you'd never have two concurrent waiters.
    // -------------------------------------------------------------------------
    private const int SemaphoreInitial = 0;
    private const int SemaphoreCapacity = 2;

    // -------------------------------------------------------------------------
    // Test 1: Both validators pass → action fires.
    //         SemaphoreSlim gate proves both validators reached the HTTP layer
    //         concurrently (PLAN-3.3 §spec line 66–67 SemaphoreSlim fallback).
    // -------------------------------------------------------------------------
    [TestMethod]
    [Timeout(60_000)]
    public async Task Dispatch_ParallelValidators_CpaiAndRoboflowBothPass_ActionFires_ConcurrencyProven()
    {
        await using var mosquitto = new MosquittoFixture();
        await mosquitto.InitializeAsync().ConfigureAwait(false);

        // Gate: starts closed (0). Both validator stubs try to acquire; the test
        // waits for both stubs to be simultaneously waiting, then releases both.
        using var gate = new SemaphoreSlim(SemaphoreInitial, SemaphoreCapacity);
        // Tracks how many stubs are currently blocked on gate.WaitAsync().
        var blockedCount = new System.Runtime.CompilerServices.StrongBox<int>(0);

        using var wireMockCpai     = StartCpaiStubWithGate(confidence: 0.92, gate, blockedCount);
        using var wireMockRoboflow = StartRoboflowStubWithGate(confidence: 0.92, gate, blockedCount);
        using var wireMockBlueIris = StartBlueIrisStub();
        using var wireMockFrigate  = StartFrigateSnapshotStub();

        using var app = await BuildHostAsync(
            mosquitto,
            wireMockCpai, wireMockRoboflow,
            wireMockBlueIris, wireMockFrigate).ConfigureAwait(false);

        try
        {
            await PublishOneEventAsync(mosquitto, eventId: "ev-parallel-both-pass-001").ConfigureAwait(false);

            // Wait until both stubs are simultaneously blocked on the gate (up to 10 s).
            // This is the concurrency proof: if validators ran sequentially the second
            // stub would not start until the first returned — so blockedCount would
            // never reach 2 while the gate is still closed.
            var gateDeadline = DateTime.UtcNow.AddSeconds(10);
            while (blockedCount.Value < 2 && DateTime.UtcNow < gateDeadline)
                await Task.Delay(25).ConfigureAwait(false);

            blockedCount.Value.Should().Be(2,
                "both validators must reach the HTTP layer concurrently before either returns " +
                "(proves parallel scheduling, not sequential execution)");

            // Release both stubs so they return their responses.
            gate.Release(2);

            // Now wait for BlueIris to fire (up to 10 s from release).
            var actionDeadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < actionDeadline)
            {
                if (wireMockBlueIris.LogEntries.Any()) break;
                await Task.Delay(50).ConfigureAwait(false);
            }

            // Both validators must have been called.
            wireMockCpai.FindLogEntries(Request.Create().UsingPost().WithPath("/v1/vision/detection"))
                .Should().HaveCount(1, "CPAI validator must be invoked");
            wireMockRoboflow.FindLogEntries(Request.Create().UsingPost().WithPath("/infer/object_detection"))
                .Should().HaveCount(1, "Roboflow validator must be invoked");

            // Action must have fired (strict-AND: both passed).
            wireMockBlueIris.FindLogEntries(Request.Create().UsingGet().WithPath("/admin"))
                .Should().HaveCount(1, "BlueIris trigger must fire when both validators pass");
        }
        finally
        {
            // Ensure stubs aren't left blocking if the test fails mid-way.
            gate.Release(SemaphoreCapacity);
            await app.StopAsync().ConfigureAwait(false);
        }
    }

    // -------------------------------------------------------------------------
    // Test 2: Roboflow rejects (low confidence) while CPAI passes.
    //         Action must NOT fire; BOTH validator WireMocks must record 1 request
    //         (proves no first-reject short-circuit per CONTEXT-14 D6).
    // -------------------------------------------------------------------------
    [TestMethod]
    [Timeout(60_000)]
    public async Task Dispatch_ParallelValidators_CpaiPassesRoboflowRejects_ActionDoesNotFire_BothValidatorsRan()
    {
        await using var mosquitto = new MosquittoFixture();
        await mosquitto.InitializeAsync().ConfigureAwait(false);

        using var wireMockCpai      = StartCpaiStub(confidence: 0.92);
        using var wireMockRoboflow  = StartRoboflowStub(confidence: 0.10); // below MinConfidence=0.7
        using var wireMockBlueIris  = StartBlueIrisStub();
        using var wireMockFrigate   = StartFrigateSnapshotStub();

        using var app = await BuildHostAsync(
            mosquitto,
            wireMockCpai, wireMockRoboflow,
            wireMockBlueIris, wireMockFrigate).ConfigureAwait(false);

        try
        {
            await PublishOneEventAsync(mosquitto, eventId: "ev-parallel-roboflow-rejects-001").ConfigureAwait(false);

            // Wait for both validators to be hit (up to 10 s).
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                var cpaiHit     = wireMockCpai.LogEntries.Any();
                var roboflowHit = wireMockRoboflow.LogEntries.Any();
                if (cpaiHit && roboflowHit) break;
                await Task.Delay(50).ConfigureAwait(false);
            }

            // Both validators must have been invoked (no first-reject short-circuit).
            wireMockCpai.FindLogEntries(Request.Create().UsingPost().WithPath("/v1/vision/detection"))
                .Should().HaveCount(1, "CPAI validator must run even when Roboflow rejects (no short-circuit)");
            wireMockRoboflow.FindLogEntries(Request.Create().UsingPost().WithPath("/infer/object_detection"))
                .Should().HaveCount(1, "Roboflow validator must run — it is the rejecting validator");

            // Give the dispatcher a moment to settle before asserting BlueIris is empty.
            await Task.Delay(500).ConfigureAwait(false);

            // Action must NOT fire (strict-AND aggregation: Roboflow rejected).
            wireMockBlueIris.FindLogEntries(Request.Create().UsingGet().WithPath("/admin"))
                .Should().BeEmpty("BlueIris action must be suppressed when any validator rejects");
        }
        finally
        {
            await app.StopAsync().ConfigureAwait(false);
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a CPAI WireMock stub that blocks on <paramref name="gate"/> before responding.
    /// Increments <paramref name="blockedCount"/> while waiting so the test can observe
    /// how many validators are simultaneously at the HTTP layer.
    /// </summary>
    private static WireMockServer StartCpaiStubWithGate(double confidence, SemaphoreSlim gate, System.Runtime.CompilerServices.StrongBox<int> blockedCount)
    {
        var server = WireMockServer.Start();
        var body = "{\"success\":true,\"code\":200,\"predictions\":[{" +
                   "\"label\":\"person\"," +
                   "\"confidence\":" + confidence.ToString("0.00", CultureInfo.InvariantCulture) +
                   ",\"x_min\":1,\"y_min\":2,\"x_max\":3,\"y_max\":4}]}";
        // WireMock.Net callback: block until the test releases the gate.
        // blockedCount is intentionally an int ref captured by the lambda — Interlocked
        // keeps the increment/decrement thread-safe from WireMock's thread pool.
        var capturedGate = gate;
        server.Given(Request.Create().UsingPost().WithPath("/v1/vision/detection"))
            .RespondWith(Response.Create()
                .WithCallback(_ =>
                {
                    Interlocked.Increment(ref blockedCount.Value);
                    capturedGate.Wait(); // blocks until test releases
                    Interlocked.Decrement(ref blockedCount.Value);
                    return new WireMock.ResponseMessage
                    {
                        StatusCode = 200,
                        Headers = new Dictionary<string, WireMock.Types.WireMockList<string>>
                        {
                            ["Content-Type"] = new(["application/json"]),
                        },
                        BodyData = new WireMock.Util.BodyData
                        {
                            DetectedBodyType = WireMock.Types.BodyType.String,
                            BodyAsString = body,
                        },
                    };
                }));
        return server;
    }

    /// <summary>
    /// Creates a Roboflow WireMock stub that blocks on <paramref name="gate"/> before responding.
    /// </summary>
    private static WireMockServer StartRoboflowStubWithGate(double confidence, SemaphoreSlim gate, System.Runtime.CompilerServices.StrongBox<int> blockedCount)
    {
        var server = WireMockServer.Start();
        var body = "{\"predictions\":[{" +
                   "\"class\":\"person\"," +
                   "\"confidence\":" + confidence.ToString("0.00", CultureInfo.InvariantCulture) +
                   ",\"class_id\":0,\"x\":10,\"y\":10,\"width\":50,\"height\":80}]," +
                   "\"image\":{\"width\":640,\"height\":480},\"time\":0.01}";
        var capturedGate = gate;
        server.Given(Request.Create().UsingPost().WithPath("/infer/object_detection"))
            .RespondWith(Response.Create()
                .WithCallback(_ =>
                {
                    Interlocked.Increment(ref blockedCount.Value);
                    capturedGate.Wait();
                    Interlocked.Decrement(ref blockedCount.Value);
                    return new WireMock.ResponseMessage
                    {
                        StatusCode = 200,
                        Headers = new Dictionary<string, WireMock.Types.WireMockList<string>>
                        {
                            ["Content-Type"] = new(["application/json"]),
                        },
                        BodyData = new WireMock.Util.BodyData
                        {
                            DetectedBodyType = WireMock.Types.BodyType.String,
                            BodyAsString = body,
                        },
                    };
                }));
        return server;
    }

    private static WireMockServer StartCpaiStub(double confidence)
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

    private static WireMockServer StartRoboflowStub(double confidence)
    {
        var server = WireMockServer.Start();
        var body = "{\"predictions\":[{" +
                   "\"class\":\"person\"," +
                   "\"confidence\":" + confidence.ToString("0.00", CultureInfo.InvariantCulture) +
                   ",\"class_id\":0,\"x\":10,\"y\":10,\"width\":50,\"height\":80}]," +
                   "\"image\":{\"width\":640,\"height\":480},\"time\":0.01}";
        server.Given(Request.Create().UsingPost().WithPath("/infer/object_detection"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(body));
        return server;
    }

    private static WireMockServer StartBlueIrisStub()
    {
        var server = WireMockServer.Start();
        server.Given(Request.Create().UsingGet().WithPath("/admin"))
            .RespondWith(Response.Create().WithStatusCode(200));
        return server;
    }

    private static WireMockServer StartFrigateSnapshotStub()
    {
        var server = WireMockServer.Start();
        server.Given(Request.Create().UsingGet().WithPath("/api/events/*"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "image/jpeg")
                .WithBody(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 }));
        return server;
    }

    private static async Task<IHost> BuildHostAsync(
        MosquittoFixture mosquitto,
        WireMockServer cpai,
        WireMockServer roboflow,
        WireMockServer blueIris,
        WireMockServer frigateSnap)
    {
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            // BlueIris action plugin.
            ["BlueIris:TriggerUrlTemplate"] = $"{blueIris.Urls[0]}/admin?camera={{camera}}&trigger=1",
            ["BlueIris:RequestTimeout"] = "00:00:05",

            // Frigate snapshot provider (validators need a snapshot to send).
            ["FrigateSnapshot:BaseUrl"] = frigateSnap.Urls[0],
            ["FrigateSnapshot:RequestTimeout"] = "00:00:05",
            ["FrigateSnapshot:Retry404Count"] = "0",
            ["Snapshots:DefaultProviderName"] = "Frigate",

            // Two validator instances: CPAI + Roboflow.
            ["Validators:cpai:Type"] = "CodeProjectAi",
            ["Validators:cpai:BaseUrl"] = cpai.Urls[0],
            ["Validators:cpai:MinConfidence"] = "0.7",
            ["Validators:cpai:AllowedLabels:0"] = "person",
            ["Validators:cpai:OnError"] = "FailClosed",
            ["Validators:cpai:Timeout"] = "00:00:10",

            ["Validators:roboflow_persons:Type"] = "Roboflow",
            ["Validators:roboflow_persons:BaseUrl"] = roboflow.Urls[0],
            ["Validators:roboflow_persons:ModelId"] = "rfdetr-base/1",
            ["Validators:roboflow_persons:MinConfidence"] = "0.7",
            ["Validators:roboflow_persons:AllowedLabels:0"] = "person",
            ["Validators:roboflow_persons:OnError"] = "FailClosed",
            ["Validators:roboflow_persons:Timeout"] = "00:00:10",

            // MQTT source.
            ["FrigateMqtt:Server"] = mosquitto.Hostname,
            ["FrigateMqtt:Port"] = mosquitto.Port.ToString(CultureInfo.InvariantCulture),
            ["FrigateMqtt:Topic"] = "frigate/events",

            // One subscription: frontdoor/person → BlueIris with both validators in parallel.
            ["Subscriptions:0:Name"] = "FrontDoor",
            ["Subscriptions:0:Camera"] = "frontdoor",
            ["Subscriptions:0:Label"] = "person",
            ["Subscriptions:0:Actions:0:Plugin"] = "BlueIris",
            ["Subscriptions:0:Actions:0:Validators:0"] = "cpai",
            ["Subscriptions:0:Actions:0:Validators:1"] = "roboflow_persons",
            ["Subscriptions:0:Actions:0:ParallelValidators"] = "true",
        });

        HostBootstrap.ConfigureServices(builder);

        var app = builder.Build();
        HostBootstrap.ValidateStartup(app.Services);
        await app.StartAsync().ConfigureAwait(false);

        // Wait for MQTT client to connect.
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
                "camera": "frontdoor",
                "label": "person",
                "stationary": false,
                "false_positive": false,
                "score": 0.91,
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
}
