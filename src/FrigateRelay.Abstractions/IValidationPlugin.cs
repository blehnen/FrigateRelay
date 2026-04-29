namespace FrigateRelay.Abstractions;

/// <summary>Represents a plugin that validates an event before an action executes, returning a <see cref="Verdict"/> that may short-circuit that action.</summary>
/// <remarks>
/// Validators run in the dispatcher per-action pipeline BEFORE the action plugin's
/// <see cref="IActionPlugin.ExecuteAsync"/>. A failing <see cref="Verdict"/> short-circuits
/// THIS action only — other actions in the same event continue independently (PROJECT.md V3,
/// Phase 7 CONTEXT-7 D6). The <c>snapshot</c> parameter on <see cref="ValidateAsync"/> mirrors
/// Phase 6 ARCH-D2 for actions: when validators are present the dispatcher pre-resolves the
/// snapshot ONCE and shares it across the validator chain and the action, so the underlying
/// snapshot provider's HTTP fetch happens at most once per dispatch (CONTEXT-7 D1).
/// </remarks>
public interface IValidationPlugin
{
    /// <summary>Gets the unique name of this validation plugin.</summary>
    string Name { get; }

    /// <summary>Validates the event and returns a <see cref="Verdict"/> indicating pass or fail for the associated action.</summary>
    /// <param name="ctx">The source-agnostic event context to validate.</param>
    /// <param name="snapshot">
    /// Snapshot context shared with the action plugin for this dispatch. When the dispatcher has
    /// pre-resolved the snapshot (because validators are present), <see cref="SnapshotContext.ResolveAsync"/>
    /// returns the cached <see cref="SnapshotResult"/> without re-invoking the underlying provider.
    /// Validators that don't need image bytes (e.g. metadata-only checks) can ignore this parameter.
    /// </param>
    /// <param name="ct">A token that signals the host is shutting down.</param>
    /// <returns>A <see cref="Task{TResult}"/> whose result is the validation verdict.</returns>
    Task<Verdict> ValidateAsync(EventContext ctx, SnapshotContext snapshot, CancellationToken ct);
}
