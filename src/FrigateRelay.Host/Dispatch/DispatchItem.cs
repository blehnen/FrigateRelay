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
/// <param name="Activity">The ambient <see cref="System.Diagnostics.Activity"/> for distributed tracing.</param>
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
internal readonly record struct DispatchItem(
    EventContext Context,
    IActionPlugin Plugin,
    IReadOnlyList<IValidationPlugin> Validators,
    Activity? Activity,
    string? PerActionSnapshotProvider = null,
    string? SubscriptionSnapshotProvider = null);
