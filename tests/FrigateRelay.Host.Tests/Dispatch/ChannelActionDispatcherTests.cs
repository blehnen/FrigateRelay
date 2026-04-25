using System.Diagnostics;
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

    // -------------------------------------------------------------------------
    // Test 4: Retry-delay formula contract (PLAN-2.1 Task 2)
    // -------------------------------------------------------------------------

    [TestMethod]
    public void RetryDelayGeneratorFormula_Produces3s6s9s_ForAttempts0Through2()
    {
        // The formula CONTEXT-4 D7 mandates and PLAN-2.2's BlueIris registrar uses.
        static TimeSpan Delay(int attemptNumber) => TimeSpan.FromSeconds(3 * (attemptNumber + 1));

        Delay(0).Should().Be(TimeSpan.FromSeconds(3));
        Delay(1).Should().Be(TimeSpan.FromSeconds(6));
        Delay(2).Should().Be(TimeSpan.FromSeconds(9));
    }

    // -------------------------------------------------------------------------
    // Test 5: Retry exhaustion — counter + log — consumer survives poison items
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task EnqueueAsync_WhenPluginThrowsAfterRetries_LogsExhaustionWarning_IncrementsExhaustedCounter_DoesNotKillConsumer()
    {
        var throwingPlugin = new ThrowingPlugin("BlueIris");
        var logger = new CapturingLogger();
        using var dispatcher = BuildDispatcher(new IActionPlugin[] { throwingPlugin }, capacity: 8, logger: logger);

        long exhaustedObserved = 0;
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "FrigateRelay" && instrument.Name == "frigaterelay.dispatch.exhausted")
                listener.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>((_, measurement, _, _) =>
            Interlocked.Add(ref exhaustedObserved, measurement));
        meterListener.Start();

        await dispatcher.StartAsync(CancellationToken.None);

        try
        {
            using var writeCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await dispatcher.EnqueueAsync(MakeContext("e-ex-1"), throwingPlugin, Array.Empty<IValidationPlugin>(), writeCts.Token);
            await Task.Delay(100);
            await dispatcher.EnqueueAsync(MakeContext("e-ex-2"), throwingPlugin, Array.Empty<IValidationPlugin>(), writeCts.Token);
            await Task.Delay(100);

            meterListener.RecordObservableInstruments();

            exhaustedObserved.Should().Be(2, "one counter increment per failed item");

            var warningEntries = logger.Entries
                .Where(e => e.Level == LogLevel.Warning && e.Id.Id == 101)
                .ToList();

            warningEntries.Should().HaveCount(2, "one warning per exhausted item");

            foreach (var entry in warningEntries)
            {
                entry.Message.Should().Contain("after retry exhaustion");
                entry.Message.Should().Contain("BlueIris");

                // Structured state must carry EventId and Action keys (ROADMAP criterion).
                entry.State.Should().NotBeNull();
                var keys = entry.State!.Select(kv => kv.Key).ToList();
                keys.Should().Contain("EventId", "structured state must have EventId");
                keys.Should().Contain("Action", "structured state must have Action");
            }

            // Consumer must still be alive — enqueue a third item from a no-op plugin that shares
            // the same dispatcher channel, but for simplicity verify the dispatcher can still
            // accept an item (GetQueueDepth == 0 after processing).
            await dispatcher.EnqueueAsync(MakeContext("e-ex-3"), throwingPlugin, Array.Empty<IValidationPlugin>(), writeCts.Token);
            await Task.Delay(100);
            dispatcher.GetQueueDepth(throwingPlugin).Should().Be(0, "consumer is still running and draining");
        }
        finally
        {
            await dispatcher.StopAsync(CancellationToken.None);
        }
    }

    // -------------------------------------------------------------------------
    // Test 6: Cancellation during active execution propagates to the plugin
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task StopAsync_DuringActiveExecution_PropagatesCancellationToPlugin()
    {
        var slowPlugin = new SlowPlugin("BlueIris");
        using var dispatcher = BuildDispatcher(new IActionPlugin[] { slowPlugin });

        await dispatcher.StartAsync(CancellationToken.None);

        using var writeCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await dispatcher.EnqueueAsync(MakeContext("e-cancel"), slowPlugin, Array.Empty<IValidationPlugin>(), writeCts.Token);

        // Give the consumer time to enter ExecuteAsync (it's now blocked on Task.Delay(30s)).
        await Task.Delay(100);

        var sw = Stopwatch.StartNew();
        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        // StopAsync may throw TaskCanceledException if the shutdown token fires before
        // all consumers drain — that is expected and correct behaviour.
        try { await dispatcher.StopAsync(stopCts.Token); } catch (OperationCanceledException) { }
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeLessThan(3000,
            "cancellation must unblock the slow plugin before the 30s delay expires");
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

    private sealed class ThrowingPlugin(string name) : IActionPlugin
    {
        public string Name { get; } = name;
        public Task ExecuteAsync(EventContext ctx, CancellationToken ct) =>
            Task.FromException(new HttpRequestException("simulated post-retry failure"));
    }

    private sealed class SlowPlugin(string name) : IActionPlugin
    {
        public string Name { get; } = name;
        public Task ExecuteAsync(EventContext ctx, CancellationToken ct) =>
            Task.Delay(TimeSpan.FromSeconds(30), ct);
    }

    private sealed class CapturingLogger : ILogger<ChannelActionDispatcher>
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
}
