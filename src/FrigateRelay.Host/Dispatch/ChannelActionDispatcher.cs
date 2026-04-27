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

    private static readonly Action<ILogger, string, string, Exception?> LogRetryExhausted =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(101, "DispatchRetryExhausted"),
            "Dropped event_id={EventId} action={Action} after retry exhaustion. Downstream may be unhealthy.");

    // CONTEXT-7 D6 + D7: a failing validator short-circuits THIS action only; structured
    // state must carry event_id, camera, label, action, validator, reason for operator alerting.
    private static readonly Action<ILogger, string, string, string, string, string, string, Exception?> LogValidatorRejected =
        LoggerMessage.Define<string, string, string, string, string, string>(
            LogLevel.Warning,
            new EventId(20, "ValidatorRejected"),
            "validator_rejected event_id={EventId} camera={Camera} label={Label} action={Action} validator={Validator} reason={Reason}");

    private readonly List<IActionPlugin> _plugins;
    private readonly ILogger<ChannelActionDispatcher> _logger;
    private readonly DispatcherOptions _dispatcherOpts;
    private readonly ISnapshotResolver? _snapshotResolver;

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
        IOptions<DispatcherOptions> options,
        ISnapshotResolver? snapshotResolver = null)
    {
        _plugins = plugins.ToList();
        _logger = logger;
        _dispatcherOpts = options.Value;
        _snapshotResolver = snapshotResolver;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _stoppingCts = new CancellationTokenSource();
        _channels = new Dictionary<IActionPlugin, Channel<DispatchItem>>(_plugins.Count);

        foreach (var plugin in _plugins)
        {
            _actionsByName[plugin.Name] = plugin;

            var capacity = _dispatcherOpts.PerPluginQueueCapacity.TryGetValue(plugin.Name, out var c)
                ? c
                : _dispatcherOpts.DefaultQueueCapacity;
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
        string? perActionSnapshotProvider = null,
        string? subscriptionDefaultSnapshotProvider = null,
        CancellationToken ct = default)
    {
        if (!_channels.TryGetValue(action, out var channel))
        {
            LogPluginNotRegistered(_logger, action.Name, null);
            throw new InvalidOperationException(
                $"Plugin '{action.Name}' is not registered in the dispatcher. " +
                "Ensure startup validation prevents unknown plugin names from reaching EnqueueAsync.");
        }

        await channel.Writer.WriteAsync(
            new DispatchItem(ctx, action, validators, Activity.Current?.Context ?? default,
                perActionSnapshotProvider, subscriptionDefaultSnapshotProvider),
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

    private async Task ConsumeAsync(
        IActionPlugin plugin,
        ChannelReader<DispatchItem> reader,
        CancellationToken ct)
    {
        await foreach (var item in reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            // Restore the producer-side Activity so OTel sees the channel hop as one logical trace.
            using var dispatchActivity = DispatcherDiagnostics.ActivitySource.StartActivity(
                "ActionDispatch",
                ActivityKind.Consumer,
                parentContext: item.ParentContext);

            dispatchActivity?.SetTag("action", plugin.Name);
            dispatchActivity?.SetTag("event_id", item.Context.EventId);

            try
            {
                // Build the initial resolver-backed SnapshotContext from per-action /
                // per-subscription tiers carried on the DispatchItem.
                // NOTE: ChannelActionDispatcher's field is `_snapshotResolver`;
                // SnapshotContext's own backing field is `_resolver` — different files,
                // do not confuse the two.
                var initial = _snapshotResolver is null
                    ? default
                    : new SnapshotContext(_snapshotResolver, item.PerActionSnapshotProvider, item.SubscriptionSnapshotProvider);

                // Pre-resolve ONCE when validators are present so the validator chain
                // and the action observe the SAME SnapshotResult (RESEARCH §5).
                // No validators → action plugin resolves lazily on its own; no fetch
                // for BlueIris-only subscriptions that don't read the snapshot at all.
                SnapshotContext shared;
                if (item.Validators.Count > 0)
                {
                    var preResolved = await initial.ResolveAsync(item.Context, ct).ConfigureAwait(false);
                    shared = new SnapshotContext(preResolved);

                    // Validator chain — runs ABOVE the action's Polly retry pipeline (CONTEXT-7 D11).
                    // A failing verdict short-circuits THIS action only; the consumer continues to
                    // the next DispatchItem. Other actions in the same event proceed independently
                    // because each is its own DispatchItem (CONTEXT-7 D6 / PROJECT.md V3).
                    foreach (var validator in item.Validators)
                    {
                        var verdict = await validator.ValidateAsync(item.Context, shared, ct).ConfigureAwait(false);
                        if (!verdict.Passed)
                        {
                            LogValidatorRejected(
                                _logger,
                                item.Context.EventId,
                                item.Context.Camera,
                                item.Context.Label,
                                plugin.Name,
                                validator.Name,
                                verdict.Reason ?? "(no reason)",
                                null);
                            dispatchActivity?.SetStatus(ActivityStatusCode.Ok, "ValidatorRejected");
                            goto NextItem; // skip the rest of this dispatch; do not invoke the action.
                        }
                    }
                }
                else
                {
                    shared = initial;
                }

                // BlueIrisActionPlugin's HttpClient wears the 3/6/9s Polly pipeline (PLAN-2.2).
                // If all retries fail, ExecuteAsync rethrows the last exception; caught below.
                await plugin.ExecuteAsync(item.Context, shared, ct).ConfigureAwait(false);
                dispatchActivity?.SetStatus(ActivityStatusCode.Ok);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Graceful shutdown — do not log, do not increment counter.
                dispatchActivity?.SetStatus(ActivityStatusCode.Error, "Cancelled");
                return;
            }
            catch (Exception ex)
            {
                DispatcherDiagnostics.Exhausted.Add(1,
                    new KeyValuePair<string, object?>("action", plugin.Name));

                LogRetryExhausted(_logger, item.Context.EventId, plugin.Name, ex);
                dispatchActivity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
                // Do NOT rethrow — a poison item must not kill the consumer task.
            }
            NextItem:;
        }
    }
}
