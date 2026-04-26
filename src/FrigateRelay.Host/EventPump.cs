using FrigateRelay.Abstractions;
using FrigateRelay.Host.Configuration;
using FrigateRelay.Host.Dispatch;
using FrigateRelay.Host.Matching;
using Microsoft.Extensions.Options;

namespace FrigateRelay.Host;

/// <summary>
/// Background service that pumps events from every registered <see cref="IEventSource"/>,
/// runs subscription matching, filters via <see cref="DedupeCache"/>, and dispatches each
/// matched (sub, event) pair to the named action plugins via <see cref="IActionDispatcher"/>.
/// </summary>
/// <remarks>
/// Each <see cref="IEventSource"/> runs on its own pump task so a slow or stuck source does not
/// starve others. Task teardown follows the stopping token; shutdown is orderly.
/// </remarks>
internal sealed class EventPump : BackgroundService
{
    private static readonly Action<ILogger, string, string, string, string, string, Exception?> LogMatchedEvent =
        LoggerMessage.Define<string, string, string, string, string>(
            LogLevel.Information,
            new EventId(1, "MatchedEvent"),
            "Matched event: source={Source} subscription={Subscription} camera={Camera} label={Label} event_id={EventId}");

    private static readonly Action<ILogger, string, Exception?> LogPumpStopped =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(2, "PumpStopped"),
            "Event pump stopped for source={Source}");

    private static readonly Action<ILogger, string, Exception, Exception?> LogPumpFaulted =
        LoggerMessage.Define<string, Exception>(
            LogLevel.Error,
            new EventId(3, "PumpFaulted"),
            "Event pump faulted for source={Source}: {Error}");

    private static readonly Action<ILogger, string, string, string, Exception?> LogDispatchEnqueued =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Debug,
            new EventId(4, "DispatchEnqueued"),
            "Enqueued action={Action} subscription={Subscription} event_id={EventId}");

    private readonly List<IEventSource> _sources;
    private readonly DedupeCache _dedupe;
    private readonly IOptionsMonitor<HostSubscriptionsOptions> _subsMonitor;
    private readonly IActionDispatcher _dispatcher;
    private readonly Dictionary<string, IActionPlugin> _actionsByName;
    private readonly IServiceProvider _services;
    private readonly ILogger<EventPump> _logger;

    public EventPump(
        IEnumerable<IEventSource> sources,
        DedupeCache dedupe,
        IOptionsMonitor<HostSubscriptionsOptions> subsMonitor,
        IActionDispatcher dispatcher,
        IEnumerable<IActionPlugin> actionPlugins,
        IServiceProvider services,
        ILogger<EventPump> logger)
    {
        _sources = sources.ToList();
        _dedupe = dedupe;
        _subsMonitor = subsMonitor;
        _dispatcher = dispatcher;
        _actionsByName = actionPlugins.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        _services = services;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_sources.Count == 0)
            return;

        var tasks = _sources.Select(src => PumpAsync(src, stoppingToken)).ToList();
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task PumpAsync(IEventSource source, CancellationToken ct)
    {
        try
        {
            await foreach (var context in source.ReadEventsAsync(ct).WithCancellation(ct).ConfigureAwait(false))
            {
                var subs = _subsMonitor.CurrentValue.Subscriptions;
                var matches = SubscriptionMatcher.Match(context, subs);
                foreach (var sub in matches)
                {
                    if (!_dedupe.TryEnter(sub, context)) continue;
                    LogMatchedEvent(_logger, source.Name, sub.Name, context.Camera, context.Label, context.EventId, null);

                    foreach (var entry in sub.Actions)
                    {
                        // Lookup is guaranteed to succeed: Program.cs validated all sub.Actions
                        // against registered plugins at startup. An IndexerKeyNotFoundException here
                        // indicates a startup-validation bug — throw rather than silently drop.
                        var plugin = _actionsByName[entry.Plugin];

                        // Resolve per-action validator instances by key (CONTEXT-7 D2 / RESEARCH §2).
                        // Treat null and empty Validators identically (PLAN-1.2 contract). Resolution is
                        // safe at this point because StartupValidation.ValidateValidators ran in
                        // HostBootstrap.ValidateStartup and confirmed every key resolves.
                        IReadOnlyList<IValidationPlugin> validators = entry.Validators is { Count: > 0 } keys
                            ? keys.Select(k => _services.GetRequiredKeyedService<IValidationPlugin>(k)).ToArray()
                            : Array.Empty<IValidationPlugin>();

                        await _dispatcher.EnqueueAsync(
                            context, plugin, validators,
                            entry.SnapshotProvider, sub.DefaultSnapshotProvider, ct).ConfigureAwait(false);
                        LogDispatchEnqueued(_logger, plugin.Name, sub.Name, context.EventId, null);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Graceful shutdown.
        }
        catch (Exception ex)
        {
            LogPumpFaulted(_logger, source.Name, ex, null);
        }
        finally
        {
            LogPumpStopped(_logger, source.Name, null);
        }
    }
}
