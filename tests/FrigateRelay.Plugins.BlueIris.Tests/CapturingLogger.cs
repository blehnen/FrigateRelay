using Microsoft.Extensions.Logging;

namespace FrigateRelay.Plugins.BlueIris.Tests;

/// <summary>
/// In-test ILogger implementation that captures log entries for assertion.
/// Duplicated from FrigateRelay.Host.Tests — acceptable per PLAN-2.1 notes (separate assembly,
/// no shared test-utility project yet). See commit c68dfaf for the canonical original.
/// </summary>
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
