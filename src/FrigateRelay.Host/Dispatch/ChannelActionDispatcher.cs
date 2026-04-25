using System.Diagnostics;
using System.Threading.Channels;
using FrigateRelay.Abstractions;
using Microsoft.Extensions.Options;

namespace FrigateRelay.Host.Dispatch;

/// <summary>
/// Routes matched events to per-plugin bounded channels and processes them with 2 consumer tasks
/// per plugin. Implements <see cref="IHostedService"/> directly (not <c>BackgroundService</c>) so
/// channels are constructed in <see cref="StartAsync"/> and fully drained in <see cref="StopAsync"/>
/// after <c>Writer.Complete()</c>.
/// </summary>
/// <remarks>
/// Channel topology is one bounded <c>Channel&lt;DispatchItem&gt;</c> per <see cref="IActionPlugin"/>
/// instance (CONTEXT-4 D1). Overflow mode is drop-oldest; each eviction increments the
/// <c>frigaterelay.dispatch.drops</c> counter and emits a structured warning log (D6).
/// Consumer task body is a skeleton in this plan; PLAN-2.1 wraps it in a Polly resilience pipeline.
/// </remarks>
internal sealed class ChannelActionDispatcher : IActionDispatcher, IHostedService, IDisposable
{
    private static readonly Action<ILogger, string, string, int, Exception?> LogDropped =
        LoggerMessage.Define<string, string, int>(
            LogLevel.Warning,
            new EventId(10, "DispatchItemDropped"),
            "Dropped event_id={EventId} action={Action} queue_full capacity={Capacity}. Downstream may be unhealthy.");

    private static readonly Action<ILogger, string, Exception?> LogPluginNotRegistered =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(11, "PluginNotRegistered"),
            "EnqueueAsync called for plugin={PluginName} which is not registered in the dispatcher.");

    private readonly List<IActionPlugin> _plugins;
    private readonly ILogger<ChannelActionDispatcher> _logger;
    private readonly int _capacity;

    private Dictionary<IActionPlugin, Channel<DispatchItem>> _channels = new();
    private readonly Dictionary<string, IActionPlugin> _actionsByName =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Task> _consumerTasks = new();
    private CancellationTokenSource _stoppingCts = new();

    /// <summary>
    /// Case-insensitive ordinal lookup from plugin name → plugin instance.
    /// Exposed for tests via <c>InternalsVisibleTo</c>.
    /// </summary>
    internal IReadOnlyDictionary<string, IActionPlugin> ActionsByName => _actionsByName;

    public ChannelActionDispatcher(
        IEnumerable<IActionPlugin> plugins,
        ILogger<ChannelActionDispatcher> logger,
        IOptions<DispatcherOptions> options)
    {
        _plugins = plugins.ToList();
        _logger = logger;
        _capacity = options.Value.DefaultQueueCapacity;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _stoppingCts = new CancellationTokenSource();
        _channels = new Dictionary<IActionPlugin, Channel<DispatchItem>>(_plugins.Count);

        foreach (var plugin in _plugins)
        {
            _actionsByName[plugin.Name] = plugin;

            var capacity = _capacity;
            var channelOptions = new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleWriter = true,
                SingleReader = false,
                AllowSynchronousContinuations = false,
            };

            var channel = Channel.CreateBounded<DispatchItem>(channelOptions, evicted =>
            {
                DispatcherDiagnostics.Drops.Add(
                    1,
                    new KeyValuePair<string, object?>("action", plugin.Name));
                LogDropped(_logger, evicted.Context.EventId, plugin.Name, capacity, null);
            });

            _channels[plugin] = channel;

            // 2 consumer tasks per channel (CONTEXT-4 D1).
            // CancellationToken.None is intentional: the stopping CTS token is passed into
            // ConsumeAsync itself (not into Task.Run) so the task is not cancelled externally
            // before it can drain the channel.
            var ct = _stoppingCts.Token;
            _consumerTasks.Add(Task.Run(() => ConsumeAsync(plugin, channel.Reader, ct), CancellationToken.None));
            _consumerTasks.Add(Task.Run(() => ConsumeAsync(plugin, channel.Reader, ct), CancellationToken.None));
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Complete all writers so ReadAllAsync exits when the queue drains.
        foreach (var channel in _channels.Values)
            channel.Writer.Complete();

        // Await all consumer tasks, respecting the host shutdown token.
        if (_consumerTasks.Count > 0)
            await Task.WhenAll(_consumerTasks).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask EnqueueAsync(
        EventContext ctx,
        IActionPlugin action,
        IReadOnlyList<IValidationPlugin> validators,
        CancellationToken ct)
    {
        if (!_channels.TryGetValue(action, out var channel))
        {
            LogPluginNotRegistered(_logger, action.Name, null);
            throw new InvalidOperationException(
                $"Plugin '{action.Name}' is not registered in the dispatcher. " +
                "Ensure startup validation prevents unknown plugin names from reaching EnqueueAsync.");
        }

        await channel.Writer.WriteAsync(
            new DispatchItem(ctx, action, validators, Activity.Current),
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the number of items currently queued for the specified plugin channel.
    /// Exposed for tests via <c>InternalsVisibleTo</c>.
    /// </summary>
    internal int GetQueueDepth(IActionPlugin plugin) =>
        _channels.TryGetValue(plugin, out var channel) ? channel.Reader.Count : 0;

    /// <inheritdoc />
    public void Dispose() => _stoppingCts.Dispose();

    private static async Task ConsumeAsync(
        IActionPlugin plugin,
        ChannelReader<DispatchItem> reader,
        CancellationToken ct)
    {
        await foreach (var item in reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            try
            {
                // PLAN-2.1 fills in: wrap in the Polly resilience pipeline with 3x retry (3s/6s/9s)
                // and emit frigaterelay.dispatch.exhausted + LogWarning on final failure.
                await plugin.ExecuteAsync(item.Context, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Graceful shutdown — exit the loop.
                return;
            }
            catch (Exception)
            {
                // PLAN-2.1: emit frigaterelay.dispatch.exhausted counter + structured warning here.
            }
        }
    }
}
