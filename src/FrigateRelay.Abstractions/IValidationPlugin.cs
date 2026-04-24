namespace FrigateRelay.Abstractions;

/// <summary>Represents a plugin that validates an event before an action executes, returning a <see cref="Verdict"/> that may short-circuit that action.</summary>
public interface IValidationPlugin
{
    /// <summary>Gets the unique name of this validation plugin.</summary>
    string Name { get; }

    /// <summary>Validates the event and returns a <see cref="Verdict"/> indicating pass or fail for the associated action.</summary>
    /// <param name="ctx">The source-agnostic event context to validate.</param>
    /// <param name="ct">A token that signals the host is shutting down.</param>
    /// <returns>A <see cref="Task{TResult}"/> whose result is the validation verdict.</returns>
    Task<Verdict> ValidateAsync(EventContext ctx, CancellationToken ct);
}
