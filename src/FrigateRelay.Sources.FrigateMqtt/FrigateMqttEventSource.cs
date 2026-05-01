using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using FrigateRelay.Abstractions;
using FrigateRelay.Sources.FrigateMqtt.Configuration;
using FrigateRelay.Sources.FrigateMqtt.Payloads;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;

namespace FrigateRelay.Sources.FrigateMqtt;

/// <summary>
/// <see cref="IEventSource"/> backed by an MQTTnet v5 client subscribed to Frigate's
/// <c>frigate/events</c> topic. Receives JSON payloads, deserializes via
/// <see cref="FrigateJsonOptions.Default"/>, projects via <see cref="EventContextProjector.TryProject"/>,
/// and publishes to an unbounded channel that <see cref="ReadEventsAsync"/> yields from.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Reconnect loop.</strong> MQTTnet v5 removed <c>ManagedMqttClient</c>. This class runs a
/// custom ping/reconnect loop every 5 seconds, honouring the supplied cancellation token.
/// Per-client TLS is configured via <see cref="MQTTnet.MqttClientOptionsBuilder.WithTlsOptions(System.Action{MQTTnet.MqttClientTlsOptionsBuilder})"/>;
/// no global <c>ServicePointManager</c> callback is touched.
/// </para>
/// <para>
/// <strong>Channel bridge.</strong> The MQTT client is push-based; <see cref="IEventSource.ReadEventsAsync"/>
/// is pull-based. An unbounded <see cref="Channel{T}"/> bridges the two. Shutdown completes the writer
/// cleanly so callers see a normal end-of-stream.
/// </para>
/// </remarks>
public sealed class FrigateMqttEventSource : IEventSource, IAsyncDisposable
{
    private static readonly Action<ILogger, Exception?> LogMqttConnected =
        LoggerMessage.Define(LogLevel.Information, new EventId(1, "MqttConnected"), "MQTT connected");

    private static readonly Action<ILogger, Exception?> LogMqttDisconnected =
        LoggerMessage.Define(LogLevel.Information, new EventId(2, "MqttDisconnected"), "MQTT disconnected");

    private static readonly Action<ILogger, Exception, Exception?> LogMqttConnectFailed =
        LoggerMessage.Define<Exception>(LogLevel.Warning, new EventId(3, "MqttConnectFailed"), "MQTT connect attempt failed: {Error}");

    private static readonly Action<ILogger, string, Exception?> LogMqttReceiveFailed =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(4, "MqttReceiveFailed"), "Failed to process MQTT message: {Reason}");

    private static readonly Action<ILogger, string, int, string, Exception?> LogMqttSubscribeDenied =
        LoggerMessage.Define<string, int, string>(
            LogLevel.Warning,
            new EventId(5, "MqttSubscribeDenied"),
            "MQTT subscribe denied: topic={Topic} reason_code={ReasonCode} ({ReasonName})");

    private static readonly Action<ILogger, Exception?> LogMqttConnectInflight =
        LoggerMessage.Define(
            LogLevel.Debug,
            new EventId(6, "MqttConnectInflight"),
            "MQTT connect already in flight; deferring this attempt to the in-flight call");

    private readonly IMqttClient _client;
    private readonly MqttClientFactory _factory;
    private readonly FrigateMqttOptions _options;
    private readonly ILogger<FrigateMqttEventSource> _logger;
    private readonly IMqttConnectionStatus _connectionStatus;
    private readonly Channel<EventContext> _channel =
        Channel.CreateUnbounded<EventContext>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

    private Task? _reconnectTask;
    private CancellationTokenSource? _loopCts;
    private int _started;
    private int _disposed;

    /// <summary>Creates the event source with the supplied client, factory, options, logger, and connection-status tracker.</summary>
    public FrigateMqttEventSource(
        IMqttClient client,
        MqttClientFactory factory,
        IOptions<FrigateMqttOptions> options,
        ILogger<FrigateMqttEventSource> logger,
        IMqttConnectionStatus connectionStatus)
    {
        _client = client;
        _factory = factory;
        _options = options.Value;
        _logger = logger;
        _connectionStatus = connectionStatus;
        _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
    }

    /// <inheritdoc />
    public string Name => "FrigateMqtt";

    /// <inheritdoc />
    public async IAsyncEnumerable<EventContext> ReadEventsAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        EnsureStarted(ct);
        await foreach (var evt in _channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            yield return evt;
    }

    private void EnsureStarted(CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
            return;

        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _reconnectTask = Task.Run(() => RunReconnectLoopAsync(_loopCts.Token), _loopCts.Token);
    }

    private async Task RunReconnectLoopAsync(CancellationToken ct)
    {
        var clientOptions = BuildClientOptions();
        var subOptions = _factory.CreateSubscribeOptionsBuilder()
            .WithTopicFilter(_options.Topic, MqttQualityOfServiceLevel.AtMostOnce)
            .Build();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // #19: short-circuit when already connected. Without this guard, a healthy
                // connection still fired a TryPingAsync every 5s — and during slow CONNACK,
                // a second ConnectAsync could race against the in-flight one and throw
                // InvalidOperationException("Not allowed to connect while connect/disconnect
                // is pending."). IsConnected first means we don't even ping when healthy.
                if (!_client.IsConnected && !await _client.TryPingAsync(ct).ConfigureAwait(false))
                {
                    await _client.ConnectAsync(clientOptions, ct).ConfigureAwait(false);
                    var subscribeResult = await _client.SubscribeAsync(subOptions, ct).ConfigureAwait(false);

                    // #16: MQTTnet's SubscribeAsync does NOT throw when the broker returns
                    // SUBACK with a failure reason code (e.g. ACL-denied SUBSCRIBE). Inspect
                    // the per-topic result codes; if no topic was granted, treat the
                    // connection as unhealthy so /healthz returns 503 and the reconnect
                    // loop retries. Per-denial diagnostics are logged inside the helper.
                    if (ProcessSubscribeResult(subscribeResult))
                    {
                        _connectionStatus.SetConnected(true);
                        LogMqttConnected(_logger, null);
                    }
                    else
                    {
                        _connectionStatus.SetConnected(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (InvalidOperationException ex) when (
                ex.Message.Contains("connect/disconnect is pending", StringComparison.Ordinal))
            {
                // #19: previous ConnectAsync still mid-handshake — let it finish.
                // Logged at Debug, not Warning, since this is a self-induced race that
                // recovers on the next iteration; the connection state is left untouched
                // because we genuinely don't know its outcome yet.
                LogMqttConnectInflight(_logger, null);
            }
            catch (Exception ex)
            {
                _connectionStatus.SetConnected(false);
                LogMqttConnectFailed(_logger, ex, null);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Inspects the SUBACK reason codes returned by the broker. Logs a Warning per topic
    /// that was denied. Returns <see langword="true"/> when at least one topic was granted
    /// (any QoS), <see langword="false"/> when every topic in the request was denied.
    /// </summary>
    /// <remarks>
    /// Closes #16: MQTTnet's <c>SubscribeAsync</c> resolves successfully even when the
    /// broker rejects the SUBSCRIBE — the operator only finds out by digging through
    /// broker logs. This helper makes the rejection observable on the FrigateRelay side.
    /// </remarks>
    internal bool ProcessSubscribeResult(MqttClientSubscribeResult result)
    {
        var anyGranted = false;
        foreach (var item in result.Items)
        {
            if (item.ResultCode is MqttClientSubscribeResultCode.GrantedQoS0
                                 or MqttClientSubscribeResultCode.GrantedQoS1
                                 or MqttClientSubscribeResultCode.GrantedQoS2)
            {
                anyGranted = true;
            }
            else
            {
                LogMqttSubscribeDenied(
                    _logger,
                    item.TopicFilter.Topic,
                    (int)item.ResultCode,
                    item.ResultCode.ToString(),
                    null);
            }
        }
        return anyGranted;
    }

    private MqttClientOptions BuildClientOptions()
    {
        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(_options.Server, _options.Port)
            .WithClientId(_options.ClientId);

        if (_options.Tls.Enabled)
        {
            var allowInvalid = _options.Tls.AllowInvalidCertificates;
            builder = builder.WithTlsOptions(o =>
            {
                o.UseTls(true);
                if (allowInvalid)
                    o.WithCertificateValidationHandler(_ => true);
            });
        }

        return builder.Build();
    }

    private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        var raw = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);
        await TryPublishAsync(raw).ConfigureAwait(false);
    }

    /// <summary>
    /// Internal path for unit tests: deserialize, project, and publish to the channel.
    /// Production path (MQTT message handler) funnels through here too. Swallows malformed
    /// payloads and projector rejections; logs at Warning.
    /// </summary>
    internal async Task TryPublishAsync(string rawPayload)
    {
        try
        {
            var evt = JsonSerializer.Deserialize<FrigateEvent>(rawPayload, FrigateJsonOptions.Default);
            if (evt is null)
                return;
            if (!EventContextProjector.TryProject(evt, rawPayload, out var context))
                return;

            await _channel.Writer.WriteAsync(context, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogMqttReceiveFailed(_logger, ex.Message, ex);
        }
    }

    /// <summary>Test-accessible channel reader. Do NOT use in production code — use <see cref="ReadEventsAsync"/>.</summary>
    internal ChannelReader<EventContext> InternalReader => _channel.Reader;

    /// <summary>Completes the channel writer and disconnects the MQTT client cleanly. Idempotent.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        var cts = Interlocked.Exchange(ref _loopCts, null);
        if (cts is not null)
        {
            try { await cts.CancelAsync().ConfigureAwait(false); }
            catch (ObjectDisposedException) { /* source token already torn down by host shutdown */ }

            try
            {
                if (_reconnectTask is { } t)
                    await t.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }

            try { cts.Dispose(); } catch (ObjectDisposedException) { }
        }

        _channel.Writer.TryComplete();

        try
        {
            if (_client.IsConnected)
            {
                var disconnectOptions = new MqttClientDisconnectOptionsBuilder().Build();
                await _client.DisconnectAsync(disconnectOptions, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            LogMqttReceiveFailed(_logger, $"disconnect: {ex.Message}", ex);
        }

        // Mark disconnected on the way out so the health check reflects shutdown state.
        _connectionStatus.SetConnected(false);
        LogMqttDisconnected(_logger, null);
    }
}
