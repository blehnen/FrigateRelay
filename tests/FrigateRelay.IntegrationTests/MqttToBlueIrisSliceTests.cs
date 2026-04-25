using System.Globalization;
using FluentAssertions;
using FrigateRelay.Host;
using FrigateRelay.IntegrationTests.Fixtures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MQTTnet;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

using MsHost = Microsoft.Extensions.Hosting.Host;

namespace FrigateRelay.IntegrationTests;

[TestClass]
public sealed class MqttToBlueIrisSliceTests
{
    [TestMethod]
    [Timeout(30_000)]
    public async Task MqttToBlueIris_HappyPath()
    {
        // 1. Mosquitto container.
        await using var mosquitto = new MosquittoFixture();
        await mosquitto.InitializeAsync().ConfigureAwait(false);

        // 2. WireMock BlueIris stub — ephemeral port, no hard-coded address.
        using var wireMock = WireMockServer.Start();
        wireMock.Given(Request.Create()
                .UsingGet()
                .WithPath("/admin")
                .WithParam("camera", "front")
                .WithParam("trigger", "1"))
            .RespondWith(Response.Create().WithStatusCode(200));

        // 3. Build host with in-memory config pointing at both containers.
        var builder = MsHost.CreateApplicationBuilder(Array.Empty<string>());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BlueIris:TriggerUrlTemplate"] = $"{wireMock.Urls[0]}/admin?camera={{camera}}&trigger=1",
            ["BlueIris:RequestTimeout"] = "00:00:05",
            ["FrigateMqtt:Server"] = mosquitto.Hostname,
            ["FrigateMqtt:Port"] = mosquitto.Port.ToString(CultureInfo.InvariantCulture),
            ["FrigateMqtt:Topic"] = "frigate/events",
            ["Subscriptions:0:Name"] = "FrontCam",
            ["Subscriptions:0:Camera"] = "front",
            ["Subscriptions:0:Label"] = "person",
            ["Subscriptions:0:Actions:0"] = "BlueIris",
        });

        HostBootstrap.ConfigureServices(builder);

        using var app = builder.Build();
        HostBootstrap.ValidateStartup(app.Services);

        await app.StartAsync().ConfigureAwait(false);

        // Wait for the host's MQTT client to connect and subscribe before publishing.
        // The reconnect loop runs in the background after StartAsync returns; polling
        // here avoids a race where the published message arrives before the subscription.
        var hostMqttClient = app.Services.GetRequiredService<IMqttClient>();
        var connectDeadline = DateTime.UtcNow.AddSeconds(10);
        while (!hostMqttClient.IsConnected && DateTime.UtcNow < connectDeadline)
            await Task.Delay(50).ConfigureAwait(false);
        hostMqttClient.IsConnected.Should().BeTrue("host MQTT client must connect within 10s");

        IMqttClient? mqttClient = null;
        try
        {
            // 4. Publish a Frigate "new" event payload.
            var payload = """
                {
                  "type": "new",
                  "before": null,
                  "after": {
                    "id": "ev-test-001",
                    "camera": "front",
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

            var mqttFactory = new MqttClientFactory();
            mqttClient = mqttFactory.CreateMqttClient();
            await mqttClient.ConnectAsync(new MqttClientOptionsBuilder()
                .WithTcpServer(mosquitto.Hostname, mosquitto.Port)
                .Build()).ConfigureAwait(false);
            await mqttClient.PublishStringAsync("frigate/events", payload).ConfigureAwait(false);

            // 5. Poll WireMock until the BlueIris trigger arrives (up to 10s — well under 30s SLO).
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                if (wireMock.LogEntries.Any()) break;
                await Task.Delay(100).ConfigureAwait(false);
            }

            // 6. Assert exactly one GET to /admin with camera=front.
            var matchingRequests = wireMock.FindLogEntries(
                Request.Create()
                    .UsingGet()
                    .WithPath("/admin")
                    .WithParam("camera", "front")).ToList();
            matchingRequests.Should().HaveCount(1,
                "exactly one BlueIris trigger should fire for one matched Frigate event");
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
