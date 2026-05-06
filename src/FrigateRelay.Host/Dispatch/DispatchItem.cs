using System.Diagnostics;
using FrigateRelay.Abstractions;

namespace FrigateRelay.Host.Dispatch;

/// <summary>
/// A lightweight value type that carries all state needed to execute one action plugin invocation.
/// Stored in per-plugin bounded channels; the struct design avoids GC pressure when channels hold
/// large numbers of pending items.
/// </summary>
/// <param name="Context">The source-agnostic event that triggered this dispatch.</param>
/// <param name="Plugin">The action plugin to invoke.</param>
/// <param name="Validators">Validators to run before the action executes.</param>
/// <param name="ParentContext">The captured trace context of the producing Activity (per CONTEXT-9 D1). Default = no parent (root span on consumer).</param>
/// <param name="Subscription">
/// The subscription name that produced this dispatch item.
/// Carried for telemetry tagging per CONTEXT-9 D3 (tags: subscription, action).
/// </param>
/// <param name="PerActionSnapshotProvider">
/// The per-action snapshot provider name from <c>ActionEntry.SnapshotProvider</c>.
/// <see langword="null"/> means fall through to the per-subscription or global tier.
/// Populated by <c>EventPump</c>; consumed by the action executor in Plan 3.1.
/// </param>
/// <param name="SubscriptionSnapshotProvider">
/// The per-subscription snapshot provider name from <c>SubscriptionOptions.DefaultSnapshotProvider</c>.
/// <see langword="null"/> means fall through to the global tier.
/// Populated by <c>EventPump</c>; consumed by the action executor in Plan 3.1.
/// </param>
/// <param name="ParallelValidators">
/// When <see langword="true"/>, <c>ChannelActionDispatcher.ConsumeAsync</c> runs this item's
/// validators concurrently via
/// <see cref="System.Threading.Tasks.Task.WhenAll(System.Collections.Generic.IEnumerable{System.Threading.Tasks.Task})"/>
/// with strict-AND aggregation (CONTEXT-14 D6).
/// Populated by <c>EventPump</c> from <c>ActionEntry.ParallelValidators</c>.
/// Default <see langword="false"/> preserves v1.0 / v1.1 sequential-with-short-circuit behavior.
/// </param>
internal readonly record struct DispatchItem(
    EventContext Context,
    IActionPlugin Plugin,
    IReadOnlyList<IValidationPlugin> Validators,
    ActivityContext ParentContext,
    string Subscription = "",
    string? PerActionSnapshotProvider = null,
    string? SubscriptionSnapshotProvider = null,
    bool ParallelValidators = false);   // NEW for #23 / CONTEXT-14 D5
