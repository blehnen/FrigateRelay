using FluentAssertions;
using FrigateRelay.Host;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrigateRelay.Host.Tests;

[TestClass]
public sealed class PlaceholderWorkerTests
{
    [TestMethod]
    public async Task ExecuteAsync_LogsHostStarted_ExactlyOnce()
    {
        var logger = new CapturingLogger<PlaceholderWorker>();
        var worker = new PlaceholderWorker(logger);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        // Give the worker a moment to enter ExecuteAsync and emit the log line.
        await Task.Delay(50);
        await cts.CancelAsync();
        await worker.StopAsync(CancellationToken.None);

        logger.Entries
            .Where(e => e.Level == LogLevel.Information && e.Message == "Host started")
            .Should()
            .ContainSingle("the worker must log \"Host started\" exactly once");
    }

    [TestMethod]
    public async Task ExecuteAsync_OnCancellation_CompletesWithoutThrowing()
    {
        var logger = new CapturingLogger<PlaceholderWorker>();
        var worker = new PlaceholderWorker(logger);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () =>
        {
            await worker.StartAsync(cts.Token);
            await worker.StopAsync(CancellationToken.None);
        };

        await act.Should().NotThrowAsync(
            "cancellation is the expected shutdown path — the worker must not surface OperationCanceledException");
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, eventId, formatter(state, exception)));
        }

        public sealed record LogEntry(LogLevel Level, EventId Id, string Message);
    }
}
