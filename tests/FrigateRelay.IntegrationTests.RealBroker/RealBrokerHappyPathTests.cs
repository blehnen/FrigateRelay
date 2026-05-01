using System.Globalization;
using FluentAssertions;
using FrigateRelay.Host;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MQTTnet;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace FrigateRelay.IntegrationTests.RealBroker;

/// <summary>
/// End-to-end smoke against an operator-supplied real Mosquitto (or other
/// MQTT 3.1.1/5.0) broker. Proves the full pipeline — FrigateRelay subscribes,
/// Frigate-shaped event arrives via the broker, BlueIris action fires — works
/// against real broker behaviour (TCP, real CONNACK/SUBACK round-trips, real
/// keepalive timing) rather than the in-process Testcontainers Mosquitto used
/// by the default integration suite.
///
/// Skipped on CI by default — see <see cref="RealBrokerEnvironment"/>.
/// </summary>
[TestClass]
public sealed class RealBrokerHappyPathTests
{
    [TestMethod]
    [Timeout(60_000)]
    public async Task RealBroker_PublishSubscribe_BlueIrisActionFires()
    {
        RealBrokerEnvironment.SkipIfNotEnabled();

        var (brokerHost, brokerPort) = RealBrokerEnvironment.GetBroker();
        var (brokerUser, brokerPass) = RealBrokerEnvironment.GetCredentials();
        var topic = RealBrokerEnvironment.NewIsolatedTopic();
        var hostClientId = RealBrokerEnvironment.NewIsolatedClientId();

        // WireMock BlueIris stub on an ephemeral local port.
        using var wireMock = WireMockServer.Start();
        wireMock.Given(Request.Create()
                .UsingGet()
                .WithPath("/admin")
                .WithParam("camera", "front")
                .WithParam("trigger", "1"))
            .RespondWith(Response.Create().WithStatusCode(200));

        var config = new Dictionary<string, string?>
        {
            ["BlueIris:TriggerUrlTemplate"] = $"{wireMock.Urls[0]}/admin?camera={{camera}}&trigger=1",
            ["BlueIris:RequestTimeout"] = "00:00:05",
            ["FrigateMqtt:Server"] = brokerHost,
            ["FrigateMqtt:Port"] = brokerPort.ToString(CultureInfo.InvariantCulture),
            ["FrigateMqtt:Topic"] = topic,
            ["FrigateMqtt:ClientId"] = hostClientId,
            ["Subscriptions:0:Name"] = "RealBrokerSmoke",
            ["Subscriptions:0:Camera"] = "front",
            ["Subscriptions:0:Label"] = "person",
            ["Subscriptions:0:Actions:0:Plugin"] = "BlueIris",
        };
        if (!string.IsNullOrEmpty(brokerUser))
            config["FrigateMqtt:Username"] = brokerUser;
        if (!string.IsNullOrEmpty(brokerPass))
            config["FrigateMqtt:Password"] = brokerPass;

        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.Configuration.AddInMemoryCollection(config);

        HostBootstrap.ConfigureServices(builder);

        using var app = builder.Build();
        HostBootstrap.ValidateStartup(app.Services);

        await app.StartAsync().ConfigureAwait(false);

        // Wait for the host to subscribe before publishing — same race-avoidance
        // pattern as MqttToBlueIrisSliceTests.
        var hostMqttClient = app.Services.GetRequiredService<IMqttClient>();
        var connectDeadline = DateTime.UtcNow.AddSeconds(15);
        while (!hostMqttClient.IsConnected && DateTime.UtcNow < connectDeadline)
            await Task.Delay(50).ConfigureAwait(false);
        hostMqttClient.IsConnected.Should().BeTrue(
            "host MQTT client must connect to the real broker within 15s");

        IMqttClient? publisher = null;
        try
        {
            var payload = """
                {
                  "type": "new",
                  "before": null,
                  "after": {
                    "id": "ev-realbroker-001",
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

            var factory = new MqttClientFactory();
            publisher = factory.CreateMqttClient();
            var publisherOptions = new MqttClientOptionsBuilder()
                .WithClientId(RealBrokerEnvironment.NewIsolatedClientId() + "-pub")
                .WithTcpServer(brokerHost, brokerPort);
            if (!string.IsNullOrEmpty(brokerUser))
                publisherOptions = publisherOptions.WithCredentials(brokerUser, brokerPass);

            await publisher.ConnectAsync(publisherOptions.Build()).ConfigureAwait(false);
            await publisher.PublishStringAsync(topic, payload).ConfigureAwait(false);

            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                if (wireMock.LogEntries.Any()) break;
                await Task.Delay(100).ConfigureAwait(false);
            }

            var matchingRequests = wireMock.FindLogEntries(
                Request.Create()
                    .UsingGet()
                    .WithPath("/admin")
                    .WithParam("camera", "front")).ToList();
            matchingRequests.Should().HaveCount(1,
                "exactly one BlueIris trigger should fire for one matched Frigate event " +
                "delivered via the real broker");
        }
        finally
        {
            if (publisher is not null)
            {
                if (publisher.IsConnected)
                    await publisher.DisconnectAsync().ConfigureAwait(false);
                publisher.Dispose();
            }
            await app.StopAsync().ConfigureAwait(false);
        }
    }
}
