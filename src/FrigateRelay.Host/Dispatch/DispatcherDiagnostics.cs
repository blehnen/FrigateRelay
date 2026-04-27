using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace FrigateRelay.Host.Dispatch;

/// <summary>
/// Shared observability primitives for the dispatcher pipeline.
/// All telemetry in FrigateRelay uses the single <c>"FrigateRelay"</c> meter and activity source
/// per the CLAUDE.md observability invariant (<c>frigaterelay.*</c> counter prefix).
/// </summary>
internal static class DispatcherDiagnostics
{
    /// <summary>
    /// The shared meter for all FrigateRelay metrics. Named <c>"FrigateRelay"</c> per CLAUDE.md.
    /// </summary>
    internal static readonly Meter Meter = new("FrigateRelay");

    /// <summary>
    /// The shared activity source for FrigateRelay distributed tracing. Named <c>"FrigateRelay"</c> per CLAUDE.md.
    /// </summary>
    internal static readonly ActivitySource ActivitySource = new("FrigateRelay");

    /// <summary>
    /// Incremented when a <see cref="DispatchItem"/> is evicted from a full channel (drop-oldest).
    /// Tagged with <c>action</c> (plugin name) at emit time.
    /// </summary>
    internal static readonly Counter<long> Drops =
        Meter.CreateCounter<long>("frigaterelay.dispatch.drops");

    /// <summary>
    /// Incremented when the Polly resilience pipeline exhausts all retries for a plugin invocation.
    /// Tagged with <c>action</c> (plugin name) at emit time.
    /// Emitted by PLAN-2.1 when the retry pipeline surfaces its final exception; declared here
    /// for shared ownership alongside <see cref="Drops"/>.
    /// </summary>
    internal static readonly Counter<long> Exhausted =
        Meter.CreateCounter<long>("frigaterelay.dispatch.exhausted");

    /// <summary>
    /// Incremented by EventPump.PumpAsync each time a Frigate event is received from the MQTT source,
    /// before subscription matching.
    /// Tagged with <c>camera</c>, <c>label</c> per CONTEXT-9 D3.
    /// </summary>
    internal static readonly Counter<long> EventsReceived =
        Meter.CreateCounter<long>("frigaterelay.events.received");

    /// <summary>
    /// Incremented by EventPump.PumpAsync each time an event matches at least one subscription rule
    /// and is queued for dispatch.
    /// Tagged with <c>camera</c>, <c>label</c>, <c>subscription</c> per CONTEXT-9 D3.
    /// </summary>
    internal static readonly Counter<long> EventsMatched =
        Meter.CreateCounter<long>("frigaterelay.events.matched");

    /// <summary>
    /// Incremented by ChannelActionDispatcher.EnqueueAsync each time a DispatchItem is written to
    /// a plugin channel (before validator checks and plugin execution).
    /// Tagged with <c>subscription</c>, <c>action</c> per CONTEXT-9 D3.
    /// </summary>
    internal static readonly Counter<long> ActionsDispatched =
        Meter.CreateCounter<long>("frigaterelay.actions.dispatched");

    /// <summary>
    /// Incremented by ChannelActionDispatcher.ConsumeAsync after a plugin ExecuteAsync completes
    /// without throwing.
    /// Tagged with <c>subscription</c>, <c>action</c> per CONTEXT-9 D3.
    /// </summary>
    internal static readonly Counter<long> ActionsSucceeded =
        Meter.CreateCounter<long>("frigaterelay.actions.succeeded");

    /// <summary>
    /// Incremented by ChannelActionDispatcher.ConsumeAsync when a plugin ExecuteAsync throws after
    /// all Polly retries are exhausted.
    /// Tagged with <c>subscription</c>, <c>action</c> per CONTEXT-9 D3.
    /// </summary>
    internal static readonly Counter<long> ActionsFailed =
        Meter.CreateCounter<long>("frigaterelay.actions.failed");

    /// <summary>
    /// Incremented by ChannelActionDispatcher.ConsumeAsync each time a validator returns a passing
    /// verdict for an action invocation.
    /// Tagged with <c>subscription</c>, <c>action</c>, <c>validator</c> per CONTEXT-9 D3.
    /// </summary>
    internal static readonly Counter<long> ValidatorsPassed =
        Meter.CreateCounter<long>("frigaterelay.validators.passed");

    /// <summary>
    /// Incremented by ChannelActionDispatcher.ConsumeAsync each time a validator returns a rejecting
    /// verdict, short-circuiting that action only.
    /// Tagged with <c>subscription</c>, <c>action</c>, <c>validator</c> per CONTEXT-9 D3.
    /// </summary>
    internal static readonly Counter<long> ValidatorsRejected =
        Meter.CreateCounter<long>("frigaterelay.validators.rejected");

    /// <summary>
    /// Incremented from the outermost catch in EventPump.PumpAsync and
    /// ChannelActionDispatcher.ConsumeAsync when an unexpected exception escapes normal error
    /// handling. Intentionally tagless (single alarmable series) per CONTEXT-9 D3/D9.
    /// </summary>
    internal static readonly Counter<long> ErrorsUnhandled =
        Meter.CreateCounter<long>("frigaterelay.errors.unhandled");
}
