using System.Diagnostics;
using System.Diagnostics.Metrics;
using FrigateRelay.Abstractions;

namespace FrigateRelay.Host.Dispatch;

/// <summary>
/// Shared observability primitives for the dispatcher pipeline.
/// All telemetry in FrigateRelay uses the single <c>"FrigateRelay"</c> meter and activity source
/// per the CLAUDE.md observability invariant (<c>frigaterelay.*</c> counter prefix).
/// </summary>
/// <remarks>
/// All counter increment call sites MUST use the helper methods below rather than calling
/// <c>.Add()</c> on the static counter fields directly. This keeps the tag inventory
/// (key names, cardinality rules) in a single file so future tag additions are one-file edits.
/// <!-- If you add a counter here, update docs/observability.md inventory table. -->
/// </remarks>
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
    /// <para>Counter: <c>frigaterelay.dispatch.drops</c></para>
    /// <para>Tags emitted: <c>subscription</c>, <c>camera</c>, <c>reason</c></para>
    /// <para>Cardinality bounded by (configured subscriptions × configured cameras × fixed reason set).</para>
    /// <!-- If you add a counter here, update docs/observability.md inventory table. -->
    /// </summary>
    internal static readonly Counter<long> Drops =
        Meter.CreateCounter<long>("frigaterelay.dispatch.drops");

    /// <summary>
    /// <para>Counter: <c>frigaterelay.dispatch.exhausted</c></para>
    /// <para>Tags emitted: <c>subscription</c>, <c>camera</c>, <c>action</c></para>
    /// <para>Cardinality bounded by (configured subscriptions × configured cameras × registered plugins).</para>
    /// <!-- If you add a counter here, update docs/observability.md inventory table. -->
    /// </summary>
    internal static readonly Counter<long> Exhausted =
        Meter.CreateCounter<long>("frigaterelay.dispatch.exhausted");

    /// <summary>
    /// <para>Counter: <c>frigaterelay.events.received</c></para>
    /// <para>Tags emitted: <c>camera</c>, <c>label</c></para>
    /// <para>Cardinality bounded by (configured camera × label set per Frigate config).</para>
    /// <para>Incremented before subscription matching — no subscription context is available at this point.</para>
    /// <!-- If you add a counter here, update docs/observability.md inventory table. -->
    /// </summary>
    internal static readonly Counter<long> EventsReceived =
        Meter.CreateCounter<long>("frigaterelay.events.received");

    /// <summary>
    /// <para>Counter: <c>frigaterelay.events.matched</c></para>
    /// <para>Tags emitted: <c>subscription</c>, <c>camera</c>, <c>label</c></para>
    /// <para>Cardinality bounded by (configured subscriptions × configured cameras × label set per Frigate config).</para>
    /// <!-- If you add a counter here, update docs/observability.md inventory table. -->
    /// </summary>
    internal static readonly Counter<long> EventsMatched =
        Meter.CreateCounter<long>("frigaterelay.events.matched");

    /// <summary>
    /// <para>Counter: <c>frigaterelay.actions.dispatched</c></para>
    /// <para>Tags emitted: <c>subscription</c>, <c>camera</c>, <c>action</c></para>
    /// <para>Cardinality bounded by (configured subscriptions × configured cameras × registered plugins).</para>
    /// <!-- If you add a counter here, update docs/observability.md inventory table. -->
    /// </summary>
    internal static readonly Counter<long> ActionsDispatched =
        Meter.CreateCounter<long>("frigaterelay.actions.dispatched");

    /// <summary>
    /// <para>Counter: <c>frigaterelay.actions.succeeded</c></para>
    /// <para>Tags emitted: <c>subscription</c>, <c>camera</c>, <c>action</c></para>
    /// <para>Cardinality bounded by (configured subscriptions × configured cameras × registered plugins).</para>
    /// <!-- If you add a counter here, update docs/observability.md inventory table. -->
    /// </summary>
    internal static readonly Counter<long> ActionsSucceeded =
        Meter.CreateCounter<long>("frigaterelay.actions.succeeded");

    /// <summary>
    /// <para>Counter: <c>frigaterelay.actions.failed</c></para>
    /// <para>Tags emitted: <c>subscription</c>, <c>camera</c>, <c>action</c></para>
    /// <para>Cardinality bounded by (configured subscriptions × configured cameras × registered plugins).</para>
    /// <!-- If you add a counter here, update docs/observability.md inventory table. -->
    /// </summary>
    internal static readonly Counter<long> ActionsFailed =
        Meter.CreateCounter<long>("frigaterelay.actions.failed");

    /// <summary>
    /// <para>Counter: <c>frigaterelay.validators.passed</c></para>
    /// <para>Tags emitted: <c>subscription</c>, <c>camera</c>, <c>validator</c>, <c>action</c></para>
    /// <para>Cardinality bounded by (configured subscriptions × configured cameras × registered validators × registered plugins).</para>
    /// <!-- If you add a counter here, update docs/observability.md inventory table. -->
    /// </summary>
    internal static readonly Counter<long> ValidatorsPassed =
        Meter.CreateCounter<long>("frigaterelay.validators.passed");

    /// <summary>
    /// <para>Counter: <c>frigaterelay.validators.rejected</c></para>
    /// <para>Tags emitted: <c>subscription</c>, <c>camera</c>, <c>validator</c>, <c>action</c></para>
    /// <para>Cardinality bounded by (configured subscriptions × configured cameras × registered validators × registered plugins).</para>
    /// <!-- If you add a counter here, update docs/observability.md inventory table. -->
    /// </summary>
    internal static readonly Counter<long> ValidatorsRejected =
        Meter.CreateCounter<long>("frigaterelay.validators.rejected");

    /// <summary>
    /// <para>Counter: <c>frigaterelay.errors.unhandled</c></para>
    /// <para>Tags emitted: <c>component</c></para>
    /// <para>Cardinality bounded by the fixed set of pipeline component names (e.g., <c>"EventPump"</c>, <c>"ChannelActionDispatcher"</c>).</para>
    /// <!-- If you add a counter here, update docs/observability.md inventory table. -->
    /// </summary>
    internal static readonly Counter<long> ErrorsUnhandled =
        Meter.CreateCounter<long>("frigaterelay.errors.unhandled");

    // -------------------------------------------------------------------------
    // Helper methods — one per counter.
    // Tag key strings are declared ONLY here; call sites must use these helpers,
    // never call .Add() on the counter fields directly.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Increments <c>frigaterelay.events.received</c> with <c>camera</c> and <c>label</c> tags
    /// derived from <paramref name="ctx"/>.
    /// </summary>
    /// <param name="ctx">The event context for the received MQTT event.</param>
    internal static void IncrementEventsReceived(EventContext ctx) =>
        EventsReceived.Add(1, new TagList
        {
            { "camera", ctx.Camera },
            { "label", ctx.Label },
        });

    /// <summary>
    /// Increments <c>frigaterelay.events.matched</c> with <c>subscription</c>, <c>camera</c>, and
    /// <c>label</c> tags.
    /// </summary>
    /// <param name="ctx">The event context for the matched event.</param>
    /// <param name="subscription">The subscription name that matched.</param>
    internal static void IncrementEventsMatched(EventContext ctx, string subscription) =>
        EventsMatched.Add(1, new TagList
        {
            { "subscription", subscription },
            { "camera", ctx.Camera },
            { "label", ctx.Label },
        });

    /// <summary>
    /// Increments <c>frigaterelay.actions.dispatched</c> with <c>subscription</c>, <c>camera</c>,
    /// and <c>action</c> tags derived from <paramref name="item"/>.
    /// </summary>
    /// <param name="item">The dispatch item being enqueued.</param>
    internal static void IncrementActionsDispatched(DispatchItem item) =>
        ActionsDispatched.Add(1, new TagList
        {
            { "subscription", item.Subscription },
            { "camera", item.Context.Camera },
            { "action", item.Plugin.Name },
        });

    /// <summary>
    /// Increments <c>frigaterelay.actions.succeeded</c> with <c>subscription</c>, <c>camera</c>,
    /// and <c>action</c> tags derived from <paramref name="item"/>.
    /// </summary>
    /// <param name="item">The dispatch item whose action succeeded.</param>
    internal static void IncrementActionsSucceeded(DispatchItem item) =>
        ActionsSucceeded.Add(1, new TagList
        {
            { "subscription", item.Subscription },
            { "camera", item.Context.Camera },
            { "action", item.Plugin.Name },
        });

    /// <summary>
    /// Increments <c>frigaterelay.actions.failed</c> with <c>subscription</c>, <c>camera</c>,
    /// and <c>action</c> tags derived from <paramref name="item"/>.
    /// </summary>
    /// <param name="item">The dispatch item whose action failed after all retries.</param>
    internal static void IncrementActionsFailed(DispatchItem item) =>
        ActionsFailed.Add(1, new TagList
        {
            { "subscription", item.Subscription },
            { "camera", item.Context.Camera },
            { "action", item.Plugin.Name },
        });

    /// <summary>
    /// Increments <c>frigaterelay.validators.passed</c> with <c>subscription</c>, <c>camera</c>,
    /// <c>validator</c>, and <c>action</c> tags.
    /// </summary>
    /// <param name="item">The dispatch item being validated.</param>
    /// <param name="validatorName">The name of the validator that passed.</param>
    internal static void IncrementValidatorsPassed(DispatchItem item, string validatorName) =>
        ValidatorsPassed.Add(1, new TagList
        {
            { "subscription", item.Subscription },
            { "camera", item.Context.Camera },
            { "validator", validatorName },
            { "action", item.Plugin.Name },
        });

    /// <summary>
    /// Increments <c>frigaterelay.validators.rejected</c> with <c>subscription</c>, <c>camera</c>,
    /// <c>validator</c>, and <c>action</c> tags.
    /// </summary>
    /// <param name="item">The dispatch item being validated.</param>
    /// <param name="validatorName">The name of the validator that rejected.</param>
    internal static void IncrementValidatorsRejected(DispatchItem item, string validatorName) =>
        ValidatorsRejected.Add(1, new TagList
        {
            { "subscription", item.Subscription },
            { "camera", item.Context.Camera },
            { "validator", validatorName },
            { "action", item.Plugin.Name },
        });

    /// <summary>
    /// Increments <c>frigaterelay.dispatch.drops</c> with <c>subscription</c>, <c>camera</c>,
    /// and <c>reason</c> tags derived from <paramref name="item"/>.
    /// </summary>
    /// <param name="item">The evicted dispatch item.</param>
    /// <param name="reason">The drop reason (e.g., <c>"channel_full"</c>).</param>
    internal static void IncrementDrops(DispatchItem item, string reason) =>
        Drops.Add(1, new TagList
        {
            { "subscription", item.Subscription },
            { "camera", item.Context.Camera },
            { "reason", reason },
        });

    /// <summary>
    /// Increments <c>frigaterelay.dispatch.exhausted</c> with <c>subscription</c>, <c>camera</c>,
    /// and <c>action</c> tags derived from <paramref name="item"/>.
    /// </summary>
    /// <param name="item">The dispatch item whose retry pipeline was exhausted.</param>
    internal static void IncrementExhausted(DispatchItem item) =>
        Exhausted.Add(1, new TagList
        {
            { "subscription", item.Subscription },
            { "camera", item.Context.Camera },
            { "action", item.Plugin.Name },
        });

    /// <summary>
    /// Increments <c>frigaterelay.errors.unhandled</c> with a <c>component</c> tag identifying
    /// the pipeline stage where the unhandled exception occurred.
    /// </summary>
    /// <param name="component">The component name, e.g., <c>"EventPump"</c> or <c>"ChannelActionDispatcher"</c>.</param>
    internal static void IncrementErrorsUnhandled(string component) =>
        ErrorsUnhandled.Add(1, new TagList { { "component", component } });
}
