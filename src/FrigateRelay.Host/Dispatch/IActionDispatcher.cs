using FrigateRelay.Abstractions;

namespace FrigateRelay.Host.Dispatch;

/// <summary>
/// Seam for dispatching a matched event to an action plugin via a per-plugin bounded channel.
/// The dispatcher is the sole gateway from <c>EventPump</c> to plugin execution.
/// </summary>
/// <remarks>
/// This interface intentionally lives in <c>FrigateRelay.Host</c> (not <c>Abstractions</c>)
/// because it is an internal host seam consumed only by <c>EventPump</c>.
/// Plugins never depend on this interface; they are resolved via <see cref="IActionPlugin"/>.
/// </remarks>
internal interface IActionDispatcher
{
    /// <summary>
    /// Enqueues an event for asynchronous execution by the specified action plugin.
    /// </summary>
    /// <param name="ctx">The source-agnostic event context that matched a subscription rule.</param>
    /// <param name="action">The action plugin that should process this event.</param>
    /// <param name="validators">
    /// Per-action validation plugins that must pass before the action executes.
    /// Phase 4 always passes <c>Array.Empty&lt;IValidationPlugin&gt;()</c>; Phase 7 populates
    /// per-action validators (CONTEXT-4 D4).
    /// </param>
    /// <param name="subscription">
    /// The subscription name that produced this dispatch item.
    /// Carried for telemetry tagging per CONTEXT-9 D3.
    /// </param>
    /// <param name="perActionSnapshotProvider">
    /// Per-action snapshot provider override from <c>ActionEntry.SnapshotProvider</c>;
    /// <see langword="null"/> falls through to per-subscription, then global tiers.
    /// </param>
    /// <param name="subscriptionDefaultSnapshotProvider">
    /// Per-subscription default snapshot provider from <c>SubscriptionOptions.DefaultSnapshotProvider</c>;
    /// <see langword="null"/> falls through to the global default.
    /// </param>
    /// <param name="ct">A token that signals the host is shutting down.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when the item has been enqueued.</returns>
    ValueTask EnqueueAsync(
        EventContext ctx,
        IActionPlugin action,
        IReadOnlyList<IValidationPlugin> validators,
        string subscription = "",
        string? perActionSnapshotProvider = null,
        string? subscriptionDefaultSnapshotProvider = null,
        CancellationToken ct = default);
}
