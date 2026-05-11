using Microsoft.Extensions.Logging;

namespace FrigateRelay.TestHelpers;

public sealed class CapturingLogger<T> : ILogger<T>
{
    private const int PollIntervalMs = 25;
    private readonly Lock _entriesLock = new();

    // Direct access is preserved for assertions; reads outside the lock can race with
    // background-thread writes, but `List<T>.Count` is an int field read (atomic on x86/x64).
    // The contract is: tests should call WaitForEntriesAsync first, then read Entries
    // synchronously from the test thread once the pump/dispatcher has been stopped.
    public List<LogEntry> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var entry = new LogEntry(
            logLevel,
            eventId,
            formatter(state, exception),
            state as IReadOnlyList<KeyValuePair<string, object?>>);
        lock (_entriesLock)
        {
            Entries.Add(entry);
        }
    }

    /// <summary>
    /// Polls <see cref="Entries"/> until at least <paramref name="count"/> entries have been
    /// recorded or <paramref name="timeout"/> elapses. Uses a fixed 25ms poll interval (OQ-3).
    /// </summary>
    /// <param name="count">Minimum number of log entries to wait for.</param>
    /// <param name="timeout">Maximum time to wait before throwing <see cref="TimeoutException"/>.</param>
    /// <param name="ct">Optional cancellation token; <see cref="OperationCanceledException"/> propagates naturally.</param>
    /// <exception cref="TimeoutException">Thrown when <paramref name="count"/> entries are not observed within <paramref name="timeout"/>.</exception>
    public async Task WaitForEntriesAsync(int count, TimeSpan timeout, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            int current;
            lock (_entriesLock)
            {
                current = Entries.Count;
            }
            if (current >= count) return;
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException(
                    $"WaitForEntriesAsync: expected {count} log entries but only {current} were recorded within {timeout}.");
            await Task.Delay(TimeSpan.FromMilliseconds(PollIntervalMs), ct);
        }
    }

    public sealed record LogEntry(
        LogLevel Level,
        EventId Id,
        string Message,
        IReadOnlyList<KeyValuePair<string, object?>>? State);
}
