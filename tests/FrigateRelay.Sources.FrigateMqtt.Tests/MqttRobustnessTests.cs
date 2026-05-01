using FluentAssertions;
using FrigateRelay.Abstractions;
using FrigateRelay.Sources.FrigateMqtt.Configuration;
using FrigateRelay.TestHelpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MQTTnet;
using MQTTnet.Packets;
using NSubstitute;

namespace FrigateRelay.Sources.FrigateMqtt.Tests;

/// <summary>
/// Coverage for the v1.0.1 MQTT robustness pass:
///  - #16 silent SUBACK detection
///  - #18 default ClientId uniqueness
///  - #19 reconnect loop re-entrancy guard
/// </summary>
[TestClass]
public sealed class MqttRobustnessTests
{
    // ---------- #18: default ClientId uniqueness ----------

    [TestMethod]
    public void DefaultClientId_HasFrigateRelayPrefix()
    {
        var options = new FrigateMqttOptions();

        options.ClientId.Should().StartWith("frigate-relay-");
    }

    [TestMethod]
    public void DefaultClientId_IncludesMachineNameAndProcessId()
    {
        var options = new FrigateMqttOptions();

        options.ClientId.Should().Contain(Environment.MachineName,
            "two replicas on the same host need to disambiguate by process id, but " +
            "two replicas on different hosts should be visible by host name in broker logs");
        options.ClientId.Should().Contain(
            Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "process id is the disambiguator for two instances on the same host (the " +
            "production-container-plus-developer-debug case from issue #18)");
    }

    [TestMethod]
    public void ExplicitClientId_OverridesDefault()
    {
        var options = new FrigateMqttOptions { ClientId = "operator-supplied" };

        options.ClientId.Should().Be("operator-supplied",
            "a user who explicitly configures ClientId still owns the value " +
            "(this matters for strict MQTT 3.1.1 brokers with the 23-char limit)");
    }

    // ---------- #16: silent SUBACK detection ----------

    [TestMethod]
    public void ProcessSubscribeResult_AllGranted_ReturnsTrueAndLogsNothing()
    {
        var (source, logger) = NewSourceWithCapturingLogger(Substitute.For<IMqttClient>());

        var result = BuildSubscribeResult(
            ("frigate/events", MqttClientSubscribeResultCode.GrantedQoS0),
            ("frigate/stats", MqttClientSubscribeResultCode.GrantedQoS1));

        var anyGranted = source.ProcessSubscribeResult(result);

        anyGranted.Should().BeTrue();
        logger.Entries.Should().NotContain(e => e.Id.Name == "MqttSubscribeDenied",
            "no topic was denied — there should be no warning noise on the happy path");
    }

    [TestMethod]
    public void ProcessSubscribeResult_AllDenied_ReturnsFalseAndLogsPerTopic()
    {
        var (source, logger) = NewSourceWithCapturingLogger(Substitute.For<IMqttClient>());

        var result = BuildSubscribeResult(
            ("frigate/events", MqttClientSubscribeResultCode.NotAuthorized),
            ("frigate/stats", MqttClientSubscribeResultCode.TopicFilterInvalid));

        var anyGranted = source.ProcessSubscribeResult(result);

        anyGranted.Should().BeFalse();

        var denials = logger.Entries
            .Where(e => e.Id.Name == "MqttSubscribeDenied")
            .ToList();
        denials.Should().HaveCount(2,
            "every denied topic gets its own structured warning so operators can correlate " +
            "broker-side ACL config with the FrigateRelay log stream");
        denials.Should().AllSatisfy(d => d.Level.Should().Be(LogLevel.Warning));

        var firstDenial = denials.Single(d => d.Message.Contains("frigate/events", StringComparison.Ordinal));
        firstDenial.Message.Should().Contain("NotAuthorized");
        firstDenial.Message.Should().Contain(((int)MqttClientSubscribeResultCode.NotAuthorized).ToString(
            System.Globalization.CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void ProcessSubscribeResult_MixedGrantedAndDenied_ReturnsTrueAndLogsDeniedOnly()
    {
        var (source, logger) = NewSourceWithCapturingLogger(Substitute.For<IMqttClient>());

        var result = BuildSubscribeResult(
            ("frigate/events", MqttClientSubscribeResultCode.GrantedQoS0),
            ("frigate/restricted", MqttClientSubscribeResultCode.NotAuthorized));

        var anyGranted = source.ProcessSubscribeResult(result);

        anyGranted.Should().BeTrue(
            "at least one topic granted — we proceed and mark connected so events flow on " +
            "the topics the broker permitted");

        var denials = logger.Entries.Where(e => e.Id.Name == "MqttSubscribeDenied").ToList();
        denials.Should().HaveCount(1);
        denials[0].Message.Should().Contain("frigate/restricted");
        denials[0].Message.Should().NotContain("frigate/events",
            "happy-path topics must not pollute the warning stream");
    }

    // ---------- #19: reconnect re-entrancy guard ----------

    [TestMethod]
    [Timeout(5_000)]
    public async Task ReconnectLoop_AlreadyConnected_DoesNotPingOrConnect()
    {
        var client = Substitute.For<IMqttClient>();
        client.IsConnected.Returns(true);
        var (source, _) = NewSourceWithCapturingLogger(client);

        await DriveReconnectLoopOnceAsync(source);

        await client.DidNotReceive().PingAsync(Arg.Any<CancellationToken>());
        await client.DidNotReceive().ConnectAsync(
            Arg.Any<MqttClientOptions>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [Timeout(5_000)]
    public async Task ReconnectLoop_InflightConnect_LogsAtDebugAndDoesNotMarkUnhealthy()
    {
        var client = Substitute.For<IMqttClient>();
        client.IsConnected.Returns(false);
        // PingAsync returns Task; TryPingAsync (extension) maps exception → false. We want
        // the loop to think the connection is gone and proceed to ConnectAsync.
        client.PingAsync(Arg.Any<CancellationToken>()).Returns(
            Task.FromException(new InvalidOperationException("simulated ping failure")));
        client.ConnectAsync(Arg.Any<MqttClientOptions>(), Arg.Any<CancellationToken>())
            .Returns<MqttClientConnectResult>(_ => throw new InvalidOperationException(
                "Not allowed to connect while connect/disconnect is pending."));

        var connectionStatus = Substitute.For<IMqttConnectionStatus>();
        var (source, logger) = NewSourceWithCapturingLogger(client, connectionStatus);

        await DriveReconnectLoopOnceAsync(source);

        logger.Entries.Should().Contain(
            e => e.Id.Name == "MqttConnectInflight" && e.Level == LogLevel.Debug,
            "self-induced re-entrancy is recoverable and shouldn't pollute the WRN stream");
        logger.Entries.Should().NotContain(
            e => e.Id.Name == "MqttConnectFailed" && e.Level == LogLevel.Warning,
            "the in-flight catch must short-circuit BEFORE the generic exception handler");
        connectionStatus.DidNotReceive().SetConnected(false);
        connectionStatus.DidNotReceive().SetConnected(true);
    }

    // ---------- helpers ----------

    /// <summary>
    /// Builds a real <see cref="MqttClientSubscribeResult"/> from (topic, reason-code) tuples
    /// so <see cref="FrigateMqttEventSource.ProcessSubscribeResult"/> can be exercised
    /// directly without spinning up a broker.
    /// </summary>
    private static MqttClientSubscribeResult BuildSubscribeResult(
        params (string topic, MqttClientSubscribeResultCode code)[] entries)
    {
        var items = entries
            .Select(e => new MqttClientSubscribeResultItem(
                new MqttTopicFilterBuilder().WithTopic(e.topic).Build(),
                e.code))
            .ToList();

        return new MqttClientSubscribeResult(
            packetIdentifier: 0,
            items: items,
            reasonString: string.Empty,
            userProperties: Array.Empty<MqttUserProperty>());
    }

    private static (FrigateMqttEventSource source, CapturingLogger<FrigateMqttEventSource> logger)
        NewSourceWithCapturingLogger(
            IMqttClient client,
            IMqttConnectionStatus? connectionStatus = null)
    {
        var factory = new MqttClientFactory();
        var options = Options.Create(new FrigateMqttOptions
        {
            Server = "localhost",
            Port = 1883,
            ClientId = "frigate-relay-test",
            Topic = "frigate/events",
        });
        var logger = new CapturingLogger<FrigateMqttEventSource>();
        var status = connectionStatus ?? Substitute.For<IMqttConnectionStatus>();
        return (
            new FrigateMqttEventSource(client, factory, options, logger, status),
            logger);
    }

    /// <summary>
    /// Starts the reconnect loop (via <see cref="FrigateMqttEventSource.ReadEventsAsync"/>)
    /// long enough for one iteration, then cancels. Used by tests that want to assert
    /// what the loop did or didn't call without a real broker round-trip.
    /// </summary>
    private static async Task DriveReconnectLoopOnceAsync(FrigateMqttEventSource source)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        try
        {
            await foreach (var _ in source.ReadEventsAsync(cts.Token).ConfigureAwait(false))
            {
                // The substituted client never publishes, so we never enter the loop body —
                // the ct fires inside ReadAllAsync and bubbles out as OperationCanceledException.
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
