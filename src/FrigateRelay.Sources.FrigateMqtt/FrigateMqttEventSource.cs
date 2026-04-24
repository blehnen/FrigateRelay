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

    private readonly IMqttClient _client;
    private readonly MqttClientFactory _factory;
    private readonly FrigateMqttOptions _options;
    private readonly ILogger<FrigateMqttEventSource> _logger;
    private readonly Channel<EventContext> _channel =
        Channel.CreateUnbounded<EventContext>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

    private Task? _reconnectTask;
    private CancellationTokenSource? _loopCts;
    private int _started;

    /// <summary>Creates the event source with the supplied client, factory, options, and logger.</summary>
    public FrigateMqttEventSource(
        IMqttClient client,
        MqttClientFactory factory,
        IOptions<FrigateMqttOptions> options,
        ILogger<FrigateMqttEventSource> logger)
    {
        _client = client;
        _factory = factory;
        _options = options.Value;
        _logger = logger;
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
                if (!await _client.TryPingAsync(ct).ConfigureAwait(false))
                {
                    await _client.ConnectAsync(clientOptions, ct).ConfigureAwait(false);
                    await _client.SubscribeAsync(subOptions, ct).ConfigureAwait(false);
                    LogMqttConnected(_logger, null);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
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

    /// <summary>Completes the channel writer and disconnects the MQTT client cleanly.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_loopCts is { } cts)
        {
            await cts.CancelAsync().ConfigureAwait(false);
            try
            {
                if (_reconnectTask is { } t)
                    await t.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            cts.Dispose();
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

        LogMqttDisconnected(_logger, null);
    }
}
