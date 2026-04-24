using FrigateRelay.Abstractions;
using FrigateRelay.Host.Configuration;
using FrigateRelay.Host.Matching;
using Microsoft.Extensions.Options;

namespace FrigateRelay.Host;

/// <summary>
/// Background service that pumps events from every registered <see cref="IEventSource"/>,
/// runs subscription matching, filters via <see cref="DedupeCache"/>, and logs one matched-event
/// line per (sub, event) pair that passes both the matcher and dedupe.
/// </summary>
/// <remarks>
/// <para>
/// Wave 3 host integration. No actions are fired — Phase 4 adds the action dispatcher. For now,
/// matched events terminate at a structured log line so manual smoke tests can verify the pipeline.
/// </para>
/// <para>
/// Each <see cref="IEventSource"/> runs on its own pump task so a slow or stuck source does not
/// starve others. Task teardown follows the stopping token; shutdown is orderly.
/// </para>
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

    private readonly List<IEventSource> _sources;
    private readonly DedupeCache _dedupe;
    private readonly IOptionsMonitor<HostSubscriptionsOptions> _subsMonitor;
    private readonly ILogger<EventPump> _logger;

    public EventPump(
        IEnumerable<IEventSource> sources,
        DedupeCache dedupe,
        IOptionsMonitor<HostSubscriptionsOptions> subsMonitor,
        ILogger<EventPump> logger)
    {
        _sources = sources.ToList();
        _dedupe = dedupe;
        _subsMonitor = subsMonitor;
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
                    if (_dedupe.TryEnter(sub, context))
                    {
                        LogMatchedEvent(_logger, source.Name, sub.Name, context.Camera, context.Label, context.EventId, null);
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
