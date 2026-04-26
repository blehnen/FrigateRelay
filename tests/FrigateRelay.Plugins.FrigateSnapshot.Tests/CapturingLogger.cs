// See tests/FrigateRelay.Host.Tests/CapturingLogger.cs — duplicated per existing convention.
// Rule of Three: extraction to tests/FrigateRelay.TestUtilities/ is tracked in ISSUES.md (ID pending).
using Microsoft.Extensions.Logging;

namespace FrigateRelay.Plugins.FrigateSnapshot.Tests;

internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<LogEntry> Entries { get; } = new();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Entries.Add(new LogEntry(
            level,
            id,
            formatter(state, exception),
            state as IReadOnlyList<KeyValuePair<string, object?>>));

    public sealed record LogEntry(
        LogLevel Level,
        EventId Id,
        string Message,
        IReadOnlyList<KeyValuePair<string, object?>>? State);
}
