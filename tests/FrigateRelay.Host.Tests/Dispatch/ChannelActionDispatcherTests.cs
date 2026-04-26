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

        var logger = new CapturingLogger<ChannelActionDispatcher>();
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
            await dispatcher.EnqueueAsync(ctx1, blueIris, Array.Empty<IValidationPlugin>(), ct: writeCts.Token);
            await dispatcher.EnqueueAsync(ctx2, blueIris, Array.Empty<IValidationPlugin>(), ct: writeCts.Token);
            // This 3rd enqueue should trigger drop-oldest (evicts ctx1) and fire the callback.
            await dispatcher.EnqueueAsync(ctx3, blueIris, Array.Empty<IValidationPlugin>(), ct: writeCts.Token);

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
        await dispatcher.EnqueueAsync(ctx, blueIris, Array.Empty<IValidationPlugin>(), ct: enqueueCts.Token);

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
        public Task ExecuteAsync(EventContext ctx, SnapshotContext snapshot, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class BlockingPlugin(string name, Task blockUntil) : IActionPlugin
    {
        public string Name { get; } = name;
        public async Task ExecuteAsync(EventContext ctx, SnapshotContext snapshot, CancellationToken ct) =>
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
        var logger = new CapturingLogger<ChannelActionDispatcher>();
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
            await dispatcher.EnqueueAsync(MakeContext("e-ex-1"), throwingPlugin, Array.Empty<IValidationPlugin>(), ct: writeCts.Token);
            await Task.Delay(100);
            await dispatcher.EnqueueAsync(MakeContext("e-ex-2"), throwingPlugin, Array.Empty<IValidationPlugin>(), ct: writeCts.Token);
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
            await dispatcher.EnqueueAsync(MakeContext("e-ex-3"), throwingPlugin, Array.Empty<IValidationPlugin>(), ct: writeCts.Token);
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
        await dispatcher.EnqueueAsync(MakeContext("e-cancel"), slowPlugin, Array.Empty<IValidationPlugin>(), ct: writeCts.Token);

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
    // Test 7 (Phase 7 PLAN-1.1 Task 3): validator chain short-circuits on first fail
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task EnqueueAsync_WhenValidatorFails_SkipsAction_LogsValidatorRejected()
    {
        // CONTEXT-7 D6 + ROADMAP Phase 7: a failing validator short-circuits THIS action only,
        // logs validator_rejected with action + validator + reason in structured state.
        var blueIris = new RecordingPlugin("BlueIris");
        var failing = new StubValidator("strict-person", Verdict.Fail("low_confidence"));
        var passing = new StubValidator("never-runs", Verdict.Pass());
        var logger = new CapturingLogger<ChannelActionDispatcher>();
        using var dispatcher = BuildDispatcher(new IActionPlugin[] { blueIris }, logger: logger);

        await dispatcher.StartAsync(CancellationToken.None);
        try
        {
            using var writeCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await dispatcher.EnqueueAsync(
                MakeContext("e-rejected"),
                blueIris,
                new IValidationPlugin[] { failing, passing },
                ct: writeCts.Token);

            await Task.Delay(200);

            blueIris.Executed.Should().Be(0, "action MUST NOT execute when a validator fails");
            failing.Calls.Should().Be(1, "first validator runs");
            passing.Calls.Should().Be(0, "second validator MUST NOT run after first fails (short-circuit)");

            var rejected = logger.Entries
                .Where(e => e.Level == LogLevel.Warning && e.Id.Name == "ValidatorRejected")
                .ToList();
            rejected.Should().HaveCount(1, "exactly one validator_rejected log per failed verdict");

            var entry = rejected[0];
            entry.Message.Should().Contain("strict-person");
            entry.Message.Should().Contain("BlueIris");
            entry.Message.Should().Contain("low_confidence");

            // CONTEXT-7 D7: structured state must carry event_id, camera, label, action,
            // validator, reason — operators key alerts off these fields, so coverage is mandatory.
            entry.State.Should().NotBeNull();
            var keys = entry.State!.Select(kv => kv.Key).ToList();
            keys.Should().Contain("EventId");
            keys.Should().Contain("Camera");
            keys.Should().Contain("Label");
            keys.Should().Contain("Action");
            keys.Should().Contain("Validator");
            keys.Should().Contain("Reason");
        }
        finally
        {
            await dispatcher.StopAsync(CancellationToken.None);
        }
    }

    // -------------------------------------------------------------------------
    // Test 8 (Phase 7 PLAN-1.1 Task 3): SnapshotContext is shared between
    // validator chain and action — provider hit at most once per dispatch.
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task EnqueueAsync_WhenValidatorsPresent_PreResolvesSnapshotOnce_SharesWithAction()
    {
        // RESEARCH §5: when validators are present the dispatcher must pre-resolve the
        // snapshot ONCE and pass the cached SnapshotContext.PreResolved to both the
        // validator chain and the action. Two ResolveAsync calls (validator + action)
        // must produce ONE underlying resolver invocation.
        var resolver = new CountingResolver();
        var validator = new SnapshotReadingValidator("inspect");
        var plugin = new SnapshotReadingPlugin("BlueIris");
        var logger = new CapturingLogger<ChannelActionDispatcher>();
        using var dispatcher = BuildDispatcher(new IActionPlugin[] { plugin }, logger: logger, resolver: resolver);

        await dispatcher.StartAsync(CancellationToken.None);
        try
        {
            using var writeCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await dispatcher.EnqueueAsync(
                MakeContext("e-share"),
                plugin,
                new IValidationPlugin[] { validator },
                perActionSnapshotProvider: "Frigate",
                ct: writeCts.Token);

            await Task.Delay(200);

            resolver.Calls.Should().Be(1, "underlying resolver MUST be invoked exactly once when validators are present");
            validator.ObservedSnapshot.Should().NotBeNull("validator received the resolved snapshot");
            plugin.ObservedSnapshot.Should().NotBeNull("action received the resolved snapshot");
            plugin.ObservedSnapshot.Should().BeSameAs(validator.ObservedSnapshot,
                "both validator and action must observe the SAME SnapshotResult instance (PreResolved sharing)");
        }
        finally
        {
            await dispatcher.StopAsync(CancellationToken.None);
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static ChannelActionDispatcher BuildDispatcher(
        IEnumerable<IActionPlugin> plugins,
        int capacity = 256,
        CapturingLogger<ChannelActionDispatcher>? logger = null,
        ISnapshotResolver? resolver = null)
    {
        logger ??= new CapturingLogger<ChannelActionDispatcher>();
        var options = Options.Create(new DispatcherOptions { DefaultQueueCapacity = capacity });
        return new ChannelActionDispatcher(plugins, logger, options, resolver);
    }

    private sealed class ThrowingPlugin(string name) : IActionPlugin
    {
        public string Name { get; } = name;
        public Task ExecuteAsync(EventContext ctx, SnapshotContext snapshot, CancellationToken ct) =>
            Task.FromException(new HttpRequestException("simulated post-retry failure"));
    }

    private sealed class SlowPlugin(string name) : IActionPlugin
    {
        public string Name { get; } = name;
        public Task ExecuteAsync(EventContext ctx, SnapshotContext snapshot, CancellationToken ct) =>
            Task.Delay(TimeSpan.FromSeconds(30), ct);
    }

    private sealed class RecordingPlugin(string name) : IActionPlugin
    {
        private int _executed;
        public int Executed => _executed;
        public string Name { get; } = name;
        public Task ExecuteAsync(EventContext ctx, SnapshotContext snapshot, CancellationToken ct)
        {
            Interlocked.Increment(ref _executed);
            return Task.CompletedTask;
        }
    }

    private sealed class StubValidator(string name, Verdict verdict) : IValidationPlugin
    {
        private int _calls;
        public int Calls => _calls;
        public string Name { get; } = name;
        public Task<Verdict> ValidateAsync(EventContext ctx, SnapshotContext snapshot, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(verdict);
        }
    }

    private sealed class SnapshotReadingValidator(string name) : IValidationPlugin
    {
        public string Name { get; } = name;
        public SnapshotResult? ObservedSnapshot { get; private set; }
        public async Task<Verdict> ValidateAsync(EventContext ctx, SnapshotContext snapshot, CancellationToken ct)
        {
            ObservedSnapshot = await snapshot.ResolveAsync(ctx, ct).ConfigureAwait(false);
            return Verdict.Pass();
        }
    }

    private sealed class SnapshotReadingPlugin(string name) : IActionPlugin
    {
        public string Name { get; } = name;
        public SnapshotResult? ObservedSnapshot { get; private set; }
        public async Task ExecuteAsync(EventContext ctx, SnapshotContext snapshot, CancellationToken ct)
        {
            ObservedSnapshot = await snapshot.ResolveAsync(ctx, ct).ConfigureAwait(false);
        }
    }

    private sealed class CountingResolver : ISnapshotResolver
    {
        private int _calls;
        private readonly SnapshotResult _result = new()
        {
            Bytes = [0xFF, 0xD8, 0xFF, 0xE0],
            ContentType = "image/jpeg",
            ProviderName = "Frigate",
        };
        public int Calls => _calls;
        public ValueTask<SnapshotResult?> ResolveAsync(
            EventContext context,
            string? perActionProviderName,
            string? subscriptionDefaultProviderName,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return ValueTask.FromResult<SnapshotResult?>(_result);
        }
    }

}
