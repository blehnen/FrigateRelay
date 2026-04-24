namespace FrigateRelay.Abstractions;

/// <summary>Represents a source of detection events that the host pipeline consumes asynchronously.</summary>
public interface IEventSource
{
    /// <summary>Gets the unique name of this event source.</summary>
    string Name { get; }

    /// <summary>Produces a continuous stream of <see cref="EventContext"/> values until the <paramref name="ct"/> token is cancelled.</summary>
    /// <param name="ct">A token that signals the host is shutting down.</param>
    /// <returns>An asynchronous sequence of detection events.</returns>
    IAsyncEnumerable<EventContext> ReadEventsAsync(CancellationToken ct);
}
