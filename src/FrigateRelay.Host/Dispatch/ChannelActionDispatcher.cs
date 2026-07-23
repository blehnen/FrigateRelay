using System.Diagnostics;
using System.Threading.Channels;
using FrigateRelay.Abstractions;
using FrigateRelay.Host.Observability;
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
            "Dropped event_id={FrigateEventId} action={Action} queue_full capacity={Capacity}. Downstream may be unhealthy.");

    private static readonly Action<ILogger, string, Exception?> LogPluginNotRegistered =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(11, "PluginNotRegistered"),
            "EnqueueAsync called for plugin={PluginName} which is not registered in the dispatcher.");

    private static readonly Action<ILogger, string, string, Exception?> LogRetryExhausted =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(101, "DispatchRetryExhausted"),
            "Dropped event_id={FrigateEventId} action={Action} after retry exhaustion. Downstream may be unhealthy.");

    // CONTEXT-7 D6 + D7: a failing validator short-circuits THIS action only; structured
    // state must carry event_id, camera, label, action, validator, reason for operator alerting.
    private static readonly Action<ILogger, string, string, string, string, string, string, Exception?> LogValidatorRejected =
        LoggerMessage.Define<string, string, string, string, string, string>(
            LogLevel.Warning,
            new EventId(20, "ValidatorRejected"),
            "validator_rejected event_id={FrigateEventId} camera={Camera} label={Label} action={Action} validator={Validator} reason={Reason}");

    private readonly List<IActionPlugin> _plugins;
    private readonly ILogger<ChannelActionDispatcher> _logger;
    private readonly DispatcherOptions _dispatcherOpts;
    private readonly ISnapshotResolver? _snapshotResolver;
    private readonly MetricsTagWriter _metricsTagWriter;

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
        MetricsTagWriter metricsTagWriter,
        ISnapshotResolver? snapshotResolver = null)
    {
        _plugins = plugins.ToList();
        _logger = logger;
        _dispatcherOpts = options.Value;
        _metricsTagWriter = metricsTagWriter;
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
                DispatcherDiagnostics.IncrementDrops(
                    _metricsTagWriter.NormalizeCameraTag(evicted.Context.Camera),
                    evicted.Subscription,
                    "channel_full");
                LogDropped(_logger, evicted.Context.EventId, evicted.Plugin.Name, capacity, null);
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
        // Cancel the internal stopping CTS first so any in-flight ValidateAsync /
        // ExecuteAsync that respects cancellation (HttpClient calls, Task.Delay) is
        // interrupted promptly. This is the signal the consumer-loop's outer
        // cancellation handler (the OperationCanceledException-when-token-cancelled
        // filter) relies on for graceful unwinding (CONTEXT-9 D4 / ID-6 fix).
        // IHostedService.StopAsync receives the host's drain-window token, NOT a
        // signal to cancel internal work, so we must trigger _stoppingCts manually.
        await _stoppingCts.CancelAsync().ConfigureAwait(false);

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
        string subscription = "",
        string? perActionSnapshotProvider = null,
        string? subscriptionDefaultSnapshotProvider = null,
        bool parallelValidators = false,
        CancellationToken ct = default)
    {
        if (!_channels.TryGetValue(action, out var channel))
        {
            LogPluginNotRegistered(_logger, action.Name, null);
            throw new InvalidOperationException(
                $"Plugin '{action.Name}' is not registered in the dispatcher. " +
                "Ensure startup validation prevents unknown plugin names from reaching EnqueueAsync.");
        }

        var item = new DispatchItem(ctx, action, validators, Activity.Current?.Context ?? default,
            subscription, perActionSnapshotProvider, subscriptionDefaultSnapshotProvider,
            ParallelValidators: parallelValidators);
        DispatcherDiagnostics.IncrementActionsDispatched(
            _metricsTagWriter.NormalizeCameraTag(item.Context.Camera),
            item.Subscription,
            item.Plugin.Name);

        await channel.Writer.WriteAsync(item, ct).ConfigureAwait(false);
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
        try
        {
            await foreach (var item in reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                await ProcessItemAsync(plugin, item, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Graceful shutdown — the consumer's CT was cancelled either while ReadAllAsync was
            // awaiting the next item OR during in-flight validator/action work (rethrown from
            // ProcessItemAsync). Either way, exit cleanly without logging or counter-incrementing
            // (CONTEXT-9 D4 / ID-6 fix).
        }
    }

    /// <summary>
    /// Processes a single dequeued <see cref="DispatchItem"/>: opens the consumer-side action span,
    /// pre-resolves the shared snapshot when validators are present, runs the validator chain
    /// (short-circuiting THIS action on rejection), then executes the plugin under its Polly
    /// pipeline. A poison item is swallowed (logged + counted) so it cannot kill the consumer task;
    /// a host-shutdown cancellation is rethrown so <see cref="ConsumeAsync"/> unwinds gracefully.
    /// </summary>
    private async Task ProcessItemAsync(IActionPlugin plugin, DispatchItem item, CancellationToken ct)
    {
        // action.<name>.execute span — consumer-side, parented to the producer's ActivityContext
        // captured across the Channel<T> boundary (PLAN-2.1 Task 2 / CONTEXT-9 D1).
        var spanName = $"action.{plugin.Name.ToLowerInvariant()}.execute";
        using var actionActivity = DispatcherDiagnostics.ActivitySource.StartActivity(
            spanName,
            ActivityKind.Consumer,
            parentContext: item.ParentContext);

        actionActivity?.SetTag("event.id", item.Context.EventId);
        actionActivity?.SetTag("action", plugin.Name);
        actionActivity?.SetTag("subscription", item.Subscription);

        try
        {
            // Build the initial resolver-backed SnapshotContext from per-action /
            // per-subscription tiers carried on the DispatchItem.
            // NOTE: this dispatcher stores the resolver in a field named
            // snapshotResolver, whereas SnapshotContext's own backing field is
            // named resolver — different files, do not confuse the two.
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
                if (await RunValidatorsAsync(item, plugin, shared, ct).ConfigureAwait(false))
                {
                    actionActivity?.SetTag("outcome", "validator_rejected");
                    actionActivity?.SetStatus(ActivityStatusCode.Ok, "ValidatorRejected");
                    return; // this action does not execute; ConsumeAsync moves to the next item.
                }
            }
            else
            {
                shared = initial;
            }

            // BlueIrisActionPlugin's HttpClient wears the 3/6/9s Polly pipeline (PLAN-2.2).
            // If all retries fail, ExecuteAsync rethrows the last exception; caught below.
            await plugin.ExecuteAsync(item.Context, shared, ct).ConfigureAwait(false);
            actionActivity?.SetTag("outcome", "success");
            actionActivity?.SetStatus(ActivityStatusCode.Ok);
            DispatcherDiagnostics.IncrementActionsSucceeded(
                _metricsTagWriter.NormalizeCameraTag(item.Context.Camera),
                item.Subscription,
                item.Plugin.Name);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Graceful shutdown — not an error (CONTEXT-9 D4 / ID-6 fix). Do NOT log, do NOT
            // increment a failure counter. Rethrow so ConsumeAsync's outer handler stops the
            // consumer task instead of dequeuing the next item.
            actionActivity?.SetStatus(ActivityStatusCode.Unset);
            throw;
        }
        catch (Exception ex)
        {
            var normalizedCamera = _metricsTagWriter.NormalizeCameraTag(item.Context.Camera);
            DispatcherDiagnostics.IncrementExhausted(
                normalizedCamera,
                item.Subscription,
                item.Plugin.Name);
            DispatcherDiagnostics.IncrementActionsFailed(
                normalizedCamera,
                item.Subscription,
                item.Plugin.Name);

            LogRetryExhausted(_logger, item.Context.EventId, plugin.Name, ex);
            actionActivity?.SetTag("outcome", "failure");
            actionActivity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            // Do NOT rethrow — a poison item must not kill the consumer task.
        }
    }

    /// <summary>
    /// Runs the validator chain for <paramref name="item"/> against the pre-resolved
    /// <paramref name="shared"/> snapshot, dispatching to the parallel or sequential strategy.
    /// Returns <see langword="true"/> if any validator rejected (the action must not execute).
    /// </summary>
    private Task<bool> RunValidatorsAsync(
        DispatchItem item, IActionPlugin plugin, SnapshotContext shared, CancellationToken ct) =>
        item.ParallelValidators
            // Parallel path (CONTEXT-14 D6): all validators run concurrently via Task.WhenAll with
            // strict-AND aggregation — no first-reject short-circuit. A host-shutdown cancellation
            // propagates out of Task.WhenAll to the caller's graceful-shutdown handler.
            ? RunValidatorsInParallelAsync(item, plugin, shared, ct)
            // Sequential path (original behavior, back-compat invariant):
            // first-reject short-circuits THIS action only (PROJECT.md V3).
            : RunValidatorsSequentiallyAsync(item, plugin, shared, ct);

    /// <summary>
    /// Runs each validator in <paramref name="item"/> sequentially, short-circuiting on the first
    /// rejection (original v1.0/v1.1 behavior). Returns <see langword="true"/> if any validator
    /// rejected; <see langword="false"/> if all passed.
    /// </summary>
    private async Task<bool> RunValidatorsSequentiallyAsync(
        DispatchItem item, IActionPlugin plugin, SnapshotContext shared, CancellationToken ct)
    {
        foreach (var validator in item.Validators)
        {
            var (_, verdict) = await RunOneValidatorAsync(validator, item, plugin, shared, ct).ConfigureAwait(false);

            if (verdict.Passed)
            {
                DispatcherDiagnostics.IncrementValidatorsPassed(
                    _metricsTagWriter.NormalizeCameraTag(item.Context.Camera),
                    item.Subscription,
                    validator.Name,
                    item.Plugin.Name);
            }
            else
            {
                DispatcherDiagnostics.IncrementValidatorsRejected(
                    _metricsTagWriter.NormalizeCameraTag(item.Context.Camera),
                    item.Subscription,
                    validator.Name,
                    item.Plugin.Name);
                LogValidatorRejected(
                    _logger,
                    item.Context.EventId,
                    item.Context.Camera,
                    item.Context.Label,
                    plugin.Name,
                    validator.Name,
                    verdict.Reason ?? "(no reason)",
                    null);
                return true; // short-circuit: this action does not execute.
            }
        }
        return false; // all validators passed.
    }

    /// <summary>
    /// Runs all validators in <paramref name="item"/> concurrently via <c>Task.WhenAll</c>.
    /// Strict-AND aggregation: if ANY validator rejects, returns <see langword="true"/>.
    /// No first-reject short-circuit — every validator runs to completion (CONTEXT-14 D6).
    /// Per-validator counters and logs are emitted for each result (CONTEXT-14 OQ-4).
    /// <para>
    /// <see cref="OperationCanceledException"/> from host shutdown propagates naturally out of
    /// <c>Task.WhenAll</c> to the outer graceful-shutdown handler in <see cref="ConsumeAsync"/>.
    /// </para>
    /// </summary>
    private async Task<bool> RunValidatorsInParallelAsync(
        DispatchItem item, IActionPlugin plugin, SnapshotContext shared, CancellationToken ct)
    {
        var tasks = item.Validators
            .Select(v => RunOneValidatorAsync(v, item, plugin, shared, ct))
            .ToList();

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        bool anyRejected = false;
        foreach (var (validator, verdict) in results)
        {
            if (verdict.Passed)
            {
                DispatcherDiagnostics.IncrementValidatorsPassed(
                    _metricsTagWriter.NormalizeCameraTag(item.Context.Camera),
                    item.Subscription,
                    validator.Name,
                    item.Plugin.Name);
            }
            else
            {
                DispatcherDiagnostics.IncrementValidatorsRejected(
                    _metricsTagWriter.NormalizeCameraTag(item.Context.Camera),
                    item.Subscription,
                    validator.Name,
                    item.Plugin.Name);
                LogValidatorRejected(
                    _logger,
                    item.Context.EventId,
                    item.Context.Camera,
                    item.Context.Label,
                    plugin.Name,
                    validator.Name,
                    verdict.Reason ?? "(no reason)",
                    null);
                anyRejected = true;
            }
        }
        return anyRejected;
    }

    /// <summary>
    /// Executes a single validator, wrapping the call with its distributed-trace span.
    /// The span name, tags, and verdict tagging are identical between the sequential and
    /// parallel paths — this helper keeps them in one place.
    /// <para>
    /// <see cref="OperationCanceledException"/> is NOT caught here; it propagates to the caller
    /// so <c>Task.WhenAll</c> can rethrow it and the graceful-shutdown handler fires.
    /// </para>
    /// </summary>
    private static async Task<(IValidationPlugin Validator, Verdict Verdict)> RunOneValidatorAsync(
        IValidationPlugin validator, DispatchItem item, IActionPlugin plugin, SnapshotContext shared, CancellationToken ct)
    {
        var validatorSpanName = $"validator.{validator.Name.ToLowerInvariant()}.check";
        using var vActivity = DispatcherDiagnostics.ActivitySource.StartActivity(
            validatorSpanName, ActivityKind.Internal);
        vActivity?.SetTag("event.id", item.Context.EventId);
        vActivity?.SetTag("validator", validator.Name);
        vActivity?.SetTag("action", plugin.Name);
        vActivity?.SetTag("subscription", item.Subscription);

        var verdict = await validator.ValidateAsync(item.Context, shared, ct).ConfigureAwait(false);
        vActivity?.SetTag("verdict", verdict.Passed ? "pass" : "fail");
        if (!verdict.Passed)
            vActivity?.SetTag("reason", verdict.Reason);

        return (validator, verdict);
    }
}
