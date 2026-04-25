using System.Diagnostics.Metrics;
using FluentAssertions;
using FrigateRelay.Abstractions;
using FrigateRelay.Host.Dispatch;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrigateRelay.Host.Tests.Dispatch;

[TestClass]
public sealed class ChannelActionDispatcherTests
{
    // -------------------------------------------------------------------------
    // Test 1: StartAsync populates the case-insensitive ActionsByName dictionary.
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task StartAsync_RegistersOneChannelPerPlugin_ExposesCaseInsensitiveLookup()
    {
        var blueIris = new StubPlugin("BlueIris");
        var pushover = new StubPlugin("Pushover");
        using var dispatcher = BuildDispatcher(new IActionPlugin[] { blueIris, pushover });

        await dispatcher.StartAsync(CancellationToken.None);

        try
        {
            dispatcher.ActionsByName.Should().HaveCount(2);
            dispatcher.ActionsByName["BlueIris"].Should().BeSameAs(blueIris, "exact-case lookup must work");
            dispatcher.ActionsByName["blueiris"].Should().BeSameAs(blueIris, "lower-case lookup must work (OrdinalIgnoreCase)");
            dispatcher.ActionsByName["PUSHOVER"].Should().BeSameAs(pushover, "upper-case lookup must work");
        }
        finally
        {
            await dispatcher.StopAsync(CancellationToken.None);
        }
    }

    // -------------------------------------------------------------------------
    // Test 2: When the channel is full, drop-oldest fires the itemDropped callback,
    //         which increments frigaterelay.dispatch.drops and emits a Warning log.
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task EnqueueAsync_WhenChannelFull_IncrementsDropsCounter_LogsWarning()
    {
        // BlockingPlugin's ExecuteAsync blocks until released — keeps items in the queue.
        var blockingTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blueIris = new BlockingPlugin("BlueIris", blockingTcs.Task);

        var logger = new CapturingLogger();
        // Capacity=2 so the 3rd enqueue triggers drop-oldest.
        using var dispatcher = BuildDispatcher(new IActionPlugin[] { blueIris }, capacity: 2, logger: logger);

        long dropsObserved = 0;
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "FrigateRelay" && instrument.Name == "frigaterelay.dispatch.drops")
                listener.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            Interlocked.Add(ref dropsObserved, measurement);
        });
        meterListener.Start();

        await dispatcher.StartAsync(CancellationToken.None);

        try
        {
            var ctx1 = MakeContext("e1");
            var ctx2 = MakeContext("e2");
            var ctx3 = MakeContext("e3");

            // Fill the channel (capacity=2) then overflow with a 3rd write.
            // WriteAsync with a timeout ct so the test doesn't hang if the channel
            // unexpectedly blocks (it shouldn't — capacity=2, first two fit immediately).
            using var writeCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await dispatcher.EnqueueAsync(ctx1, blueIris, Array.Empty<IValidationPlugin>(), writeCts.Token);
            await dispatcher.EnqueueAsync(ctx2, blueIris, Array.Empty<IValidationPlugin>(), writeCts.Token);
            // This 3rd enqueue should trigger drop-oldest (evicts ctx1) and fire the callback.
            await dispatcher.EnqueueAsync(ctx3, blueIris, Array.Empty<IValidationPlugin>(), writeCts.Token);

            // Give the MeterListener callback a moment to fire (it's synchronous on the
            // itemDropped callback which fires inside WriteAsync on the same thread, but
            // RecordObservableInstruments is needed for observable counters — for Add-based
            // counters the callback is invoked inline, so a tiny yield is sufficient).
            await Task.Yield();
            meterListener.RecordObservableInstruments();

            dropsObserved.Should().Be(1, "exactly one item should have been dropped");

            logger.Entries
                .Where(e => e.Level == LogLevel.Warning
                            && e.Message.Contains("event_id=")
                            && e.Message.Contains("queue_full"))
                .Should().HaveCount(1, "one structured drop warning expected");
        }
        finally
        {
            blockingTcs.SetResult(); // unblock consumers so StopAsync can drain
            await dispatcher.StopAsync(CancellationToken.None);
        }
    }

    // -------------------------------------------------------------------------
    // Test 3: StopAsync drains the channel and completes within the shutdown token.
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task StopAsync_CompletesWriters_AwaitsConsumers_GracefulWithinToken()
    {
        var blueIris = new StubPlugin("BlueIris"); // ExecuteAsync returns Task.CompletedTask immediately

        using var dispatcher = BuildDispatcher(new IActionPlugin[] { blueIris });
        await dispatcher.StartAsync(CancellationToken.None);

        var ctx = MakeContext("e-stop");
        using var enqueueCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await dispatcher.EnqueueAsync(ctx, blueIris, Array.Empty<IValidationPlugin>(), enqueueCts.Token);

        // Give the consumer a moment to pick up the item.
        await Task.Delay(50);

        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var stopTask = dispatcher.StopAsync(stopCts.Token);

        await stopTask.WaitAsync(TimeSpan.FromSeconds(5));
        stopTask.IsCompleted.Should().BeTrue("StopAsync must complete within the 5-second shutdown window");

        dispatcher.GetQueueDepth(blueIris).Should().Be(0, "channel should be fully drained after StopAsync");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static ChannelActionDispatcher BuildDispatcher(
        IEnumerable<IActionPlugin> plugins,
        int capacity = 256,
        CapturingLogger? logger = null)
    {
        logger ??= new CapturingLogger();
        var options = Options.Create(new DispatcherOptions { DefaultQueueCapacity = capacity });
        return new ChannelActionDispatcher(plugins, logger, options);
    }

    private static EventContext MakeContext(string eventId) => new()
    {
        EventId = eventId,
        Camera = "front_door",
        Label = "person",
        Zones = Array.Empty<string>(),
        StartedAt = DateTimeOffset.UtcNow,
        RawPayload = "{}",
        SnapshotFetcher = _ => ValueTask.FromResult<byte[]?>(null),
    };

    private sealed class StubPlugin(string name) : IActionPlugin
    {
        public string Name { get; } = name;
        public Task ExecuteAsync(EventContext ctx, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class BlockingPlugin(string name, Task blockUntil) : IActionPlugin
    {
        public string Name { get; } = name;
        public async Task ExecuteAsync(EventContext ctx, CancellationToken ct) =>
            await blockUntil.WaitAsync(ct).ConfigureAwait(false);
    }

    private sealed class CapturingLogger : ILogger<ChannelActionDispatcher>
    {
        public List<LogEntry> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add(new LogEntry(level, id, formatter(state, exception)));

        public sealed record LogEntry(LogLevel Level, EventId Id, string Message);
    }
}
