using Microsoft.Extensions.Logging;

namespace FrigateRelay.TestHelpers;

public sealed class CapturingLogger<T> : ILogger<T>
{
    public List<LogEntry> Entries { get; } = new();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Entries.Add(new LogEntry(
            logLevel,
            eventId,
            formatter(state, exception),
            state as IReadOnlyList<KeyValuePair<string, object?>>));

    public sealed record LogEntry(
        LogLevel Level,
        EventId Id,
        string Message,
        IReadOnlyList<KeyValuePair<string, object?>>? State);
}
