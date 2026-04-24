namespace FrigateRelay.Abstractions;

/// <summary>Represents a plugin that executes a side-effecting action (e.g. trigger a camera, send a notification) in response to a matched event.</summary>
public interface IActionPlugin
{
    /// <summary>Gets the unique name of this action plugin.</summary>
    string Name { get; }

    /// <summary>Executes the action for the given event context.</summary>
    /// <param name="ctx">The source-agnostic event context that matched a subscription rule.</param>
    /// <param name="ct">A token that signals the host is shutting down.</param>
    /// <returns>A <see cref="Task"/> that completes when the action has finished.</returns>
    Task ExecuteAsync(EventContext ctx, CancellationToken ct);
}
