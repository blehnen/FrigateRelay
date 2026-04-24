using FluentAssertions;
using FrigateRelay.Abstractions;
using FrigateRelay.Sources.FrigateMqtt;
using FrigateRelay.Sources.FrigateMqtt.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MQTTnet;
using NSubstitute;

namespace FrigateRelay.Sources.FrigateMqtt.Tests;

[TestClass]
public sealed class FrigateMqttEventSourceTests
{
    private const string NewEventJson = """
        {
          "type": "new",
          "before": null,
          "after": {
            "id": "1714000000.1-abc",
            "camera": "front_door",
            "label": "person",
            "current_zones": ["driveway"],
            "entered_zones": ["driveway"],
            "stationary": false,
            "false_positive": false,
            "start_time": 1714000000.1,
            "has_snapshot": true,
            "thumbnail": null
          }
        }
        """;

    private const string StationaryUpdateJson = """
        {
          "type": "update",
          "before": { "id": "x", "camera": "c", "label": "person", "current_zones": [], "entered_zones": [], "stationary": false, "false_positive": false, "start_time": 1.0, "has_snapshot": false, "thumbnail": null },
          "after":  { "id": "x", "camera": "c", "label": "person", "current_zones": [], "entered_zones": [], "stationary": true,  "false_positive": false, "start_time": 1.0, "has_snapshot": false, "thumbnail": null }
        }
        """;

    private static FrigateMqttEventSource NewSource()
    {
        var client = Substitute.For<IMqttClient>();
        var factory = new MqttClientFactory();
        var options = Options.Create(new FrigateMqttOptions
        {
            Server = "localhost",
            Port = 1883,
            ClientId = "frigate-relay-test",
            Topic = "frigate/events",
        });
        return new FrigateMqttEventSource(client, factory, options, NullLogger<FrigateMqttEventSource>.Instance);
    }

    [TestMethod]
    public void Name_ReturnsFrigateMqtt()
    {
        using var cts = new CancellationTokenSource();
        var source = NewSource();

        source.Name.Should().Be("FrigateMqtt");
    }

    [TestMethod]
    public async Task TryPublishAsync_NewEvent_WritesProjectedContextToChannel()
    {
        var source = NewSource();

        await source.TryPublishAsync(NewEventJson);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var ctx = await source.InternalReader.ReadAsync(timeout.Token);

        ctx.Camera.Should().Be("front_door");
        ctx.Label.Should().Be("person");
        ctx.EventId.Should().Be("1714000000.1-abc");
        ctx.Zones.Should().Contain("driveway");
        ctx.RawPayload.Should().Be(NewEventJson);
    }

    [TestMethod]
    public async Task TryPublishAsync_StationaryUpdate_ProducesNothing()
    {
        var source = NewSource();

        await source.TryPublishAsync(StationaryUpdateJson);

        // Channel should have no items — the projector rejected the event.
        source.InternalReader.TryRead(out _).Should().BeFalse(
            "D5 stationary-guard on update events filters at the projector");
    }

    [TestMethod]
    public async Task TryPublishAsync_MalformedJson_Swallows()
    {
        var source = NewSource();

        var act = async () => await source.TryPublishAsync("{ not valid json");

        await act.Should().NotThrowAsync(
            "bad payloads must not propagate exceptions out of the MQTT handler path");
        source.InternalReader.TryRead(out _).Should().BeFalse();
    }

    [TestMethod]
    public async Task DisposeAsync_CompletesChannelSoReaderCanFinish()
    {
        var source = NewSource();
        await source.TryPublishAsync(NewEventJson);

        await source.DisposeAsync();

        // After disposal, ReadAllAsync terminates cleanly — no more items after the one published.
        var count = 0;
        await foreach (var _ in source.InternalReader.ReadAllAsync(CancellationToken.None))
            count++;
        count.Should().Be(1);
    }
}
