using System.Globalization;
using FluentAssertions;
using FrigateRelay.Host;
using FrigateRelay.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MQTTnet;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace FrigateRelay.IntegrationTests;

[TestClass]
public sealed class MqttToBothActionsTests
{
    [TestMethod]
    [Timeout(30_000)]
    public async Task Event_FansOut_ToBlueIrisAndPushover()
    {
        // 1. Mosquitto container.
        await using var mosquitto = new MosquittoFixture();
        await mosquitto.InitializeAsync().ConfigureAwait(false);

        // 2. WireMock BlueIris stub.
        using var wireMockBlueIris = WireMockServer.Start();
        wireMockBlueIris.Given(Request.Create()
                .UsingGet()
                .WithPath("/admin")
                .WithParam("camera", "front")
                .WithParam("trigger", "1"))
            .RespondWith(Response.Create().WithStatusCode(200));

        // 3. WireMock Pushover stub — POST /1/messages.json returns success body.
        using var wireMockPushover = WireMockServer.Start();
        wireMockPushover.Given(Request.Create()
                .UsingPost()
                .WithPath("/1/messages.json"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"status\":1,\"request\":\"itest-req\"}"));

        // 4. Build host with both plugins configured.
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BlueIris:TriggerUrlTemplate"] = $"{wireMockBlueIris.Urls[0]}/admin?camera={{camera}}&trigger=1",
            ["BlueIris:RequestTimeout"] = "00:00:05",
            ["Pushover:AppToken"] = "test-app-token",
            ["Pushover:UserKey"] = "test-user-key",
            ["Pushover:BaseAddress"] = wireMockPushover.Urls[0],
            ["Pushover:RequestTimeout"] = "00:00:05",
            ["FrigateMqtt:Server"] = mosquitto.Hostname,
            ["FrigateMqtt:Port"] = mosquitto.Port.ToString(CultureInfo.InvariantCulture),
            ["FrigateMqtt:Topic"] = "frigate/events",
            ["Subscriptions:0:Name"] = "FrontCam",
            ["Subscriptions:0:Camera"] = "front",
            ["Subscriptions:0:Label"] = "person",
            ["Subscriptions:0:Actions:0:Plugin"] = "BlueIris",
            ["Subscriptions:0:Actions:1:Plugin"] = "Pushover",
        });

        HostBootstrap.ConfigureServices(builder);

        using var app = builder.Build();
        HostBootstrap.ValidateStartup(app.Services);

        await app.StartAsync().ConfigureAwait(false);

        // Wait for the host MQTT client to connect.
        var hostMqttClient = app.Services.GetRequiredService<IMqttClient>();
        var connectDeadline = DateTime.UtcNow.AddSeconds(10);
        while (!hostMqttClient.IsConnected && DateTime.UtcNow < connectDeadline)
            await Task.Delay(50).ConfigureAwait(false);
        hostMqttClient.IsConnected.Should().BeTrue("host MQTT client must connect within 10s");

        IMqttClient? mqttClient = null;
        try
        {
            // 5. Publish a single Frigate "new" event.
            var payload = """
                {
                  "type": "new",
                  "before": null,
                  "after": {
                    "id": "ev-fanout-001",
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

            var mqttFactory = new MqttClientFactory();
            mqttClient = mqttFactory.CreateMqttClient();
            await mqttClient.ConnectAsync(new MqttClientOptionsBuilder()
                .WithTcpServer(mosquitto.Hostname, mosquitto.Port)
                .Build()).ConfigureAwait(false);
            await mqttClient.PublishStringAsync("frigate/events", payload).ConfigureAwait(false);

            // 6. Poll until both WireMock stubs have recorded a request (up to 10s).
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                if (wireMockBlueIris.LogEntries.Any() && wireMockPushover.LogEntries.Any())
                    break;
                await Task.Delay(100).ConfigureAwait(false);
            }

            // 7. Assert exactly one GET to BlueIris and one POST to Pushover.
            var blueIrisRequests = wireMockBlueIris.FindLogEntries(
                Request.Create()
                    .UsingGet()
                    .WithPath("/admin")
                    .WithParam("camera", "front")).ToList();
            blueIrisRequests.Should().HaveCount(1,
                "exactly one BlueIris trigger should fire for one matched Frigate event");

            var pushoverRequests = wireMockPushover.FindLogEntries(
                Request.Create()
                    .UsingPost()
                    .WithPath("/1/messages.json")).ToList();
            pushoverRequests.Should().HaveCount(1,
                "exactly one Pushover notification should fire for one matched Frigate event");
        }
        finally
        {
            if (mqttClient is not null)
            {
                if (mqttClient.IsConnected)
                    await mqttClient.DisconnectAsync().ConfigureAwait(false);
                mqttClient.Dispose();
            }
            await app.StopAsync().ConfigureAwait(false);
        }
    }
}
