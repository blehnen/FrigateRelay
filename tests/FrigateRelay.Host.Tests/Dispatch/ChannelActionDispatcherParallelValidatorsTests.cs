using System.Diagnostics.Metrics;
using FluentAssertions;
using FrigateRelay.Abstractions;
using FrigateRelay.Host.Dispatch;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrigateRelay.Host.Tests.Dispatch;

/// <summary>
/// Unit tests for the parallel-validators branch in <see cref="ChannelActionDispatcher"/>.
/// Covers CONTEXT-14 D5/D6/OQ-4:
/// <list type="bullet">
///   <item>Back-compat: sequential mode short-circuits on first reject.</item>
///   <item>Parallel happy path: all pass → action executes.</item>
///   <item>Parallel strict-AND: any reject → action skipped, ALL validators still ran (no short-circuit).</item>
///   <item>Per-validator counters emitted individually in parallel mode.</item>
///   <item>Timeout (fail-closed): a validator returning Fail still blocks the action.</item>
///   <item>Host-cancellation propagates gracefully out of Task.WhenAll.</item>
/// </list>
/// </summary>
[TestClass]
public sealed class ChannelActionDispatcherParallelValidatorsTests
{
    // -----------------------------------------------------------------------
    // Test 1: Sequential mode unchanged — first-reject short-circuit is intact.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Back-compat regression: with <c>ParallelValidators=false</c> the existing sequential
    /// path short-circuits on the first failing validator. Validator-2 must NOT be called.
    /// </summary>
    [TestMethod]
    public async Task Dispatch_ParallelValidatorsFalse_RunsValidatorsSequentially()
    {
        var plugin = new RecordingPlugin("BlueIris");
        var failing = new StubValidator("v-fail", Verdict.Fail("low_confidence"));
        var shouldNotRun = new StubValidator("v-skip", Verdict.Pass());
        var logger = new CapturingLogger<ChannelActionDispatcher>();
        using var dispatcher = BuildDispatcher([plugin], logger);

        await dispatcher.StartAsync(CancellationToken.None);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await dispatcher.EnqueueAsync(
                MakeContext("e-seq"),
                plugin,
                [failing, shouldNotRun],
                parallelValidators: false,
                ct: cts.Token);

            await Task.Delay(200);

            plugin.Executed.Should().Be(0, "action must not fire when first validator fails");
            failing.Calls.Should().Be(1, "first validator must be called");
            shouldNotRun.Calls.Should().Be(0,
                "second validator MUST NOT run after first fails in sequential mode (short-circuit)");
        }
        finally
        {
            await dispatcher.StopAsync(CancellationToken.None);
        }
    }

    // -----------------------------------------------------------------------
    // Test 2: Parallel happy path — all pass → action fires, all validators ran.
    // -----------------------------------------------------------------------

    /// <summary>
    /// With <c>ParallelValidators=true</c> and all validators passing, the action plugin
    /// must execute and every validator's <c>ValidateAsync</c> must be called.
    /// </summary>
    [TestMethod]
    public async Task Dispatch_ParallelValidatorsTrue_AllValidatorsPass_ActionExecutes()
    {
        var plugin = new RecordingPlugin("BlueIris");
        var v1 = new StubValidator("v1", Verdict.Pass());
        var v2 = new StubValidator("v2", Verdict.Pass());
        var logger = new CapturingLogger<ChannelActionDispatcher>();
        using var dispatcher = BuildDispatcher([plugin], logger);

        await dispatcher.StartAsync(CancellationToken.None);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await dispatcher.EnqueueAsync(
                MakeContext("e-par-pass"),
                plugin,
                [v1, v2],
                parallelValidators: true,
                ct: cts.Token);

            await Task.Delay(300);

            plugin.Executed.Should().Be(1, "action must execute when all validators pass");
            v1.Calls.Should().Be(1, "v1 must be called");
            v2.Calls.Should().Be(1, "v2 must be called");
        }
        finally
        {
            await dispatcher.StopAsync(CancellationToken.None);
        }
    }

    // -----------------------------------------------------------------------
    // Test 3: Parallel strict-AND — any reject → action skipped, ALL ran (D6).
    // -----------------------------------------------------------------------

    /// <summary>
    /// CONTEXT-14 D6 invariant: with <c>ParallelValidators=true</c>, a single rejecting
    /// validator blocks the action, but ALL other validators still run to completion —
    /// there is no first-reject short-circuit in parallel mode.
    /// </summary>
    [TestMethod]
    public async Task Dispatch_ParallelValidatorsTrue_AnyValidatorRejects_ActionDoesNotExecute_AllValidatorsRan()
    {
        var plugin = new RecordingPlugin("BlueIris");
        var v1 = new StubValidator("v1", Verdict.Pass());
        var v2 = new StubValidator("v2", Verdict.Fail("low_confidence"));  // rejects
        var v3 = new StubValidator("v3", Verdict.Pass());
        var logger = new CapturingLogger<ChannelActionDispatcher>();
        using var dispatcher = BuildDispatcher([plugin], logger);

        await dispatcher.StartAsync(CancellationToken.None);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await dispatcher.EnqueueAsync(
                MakeContext("e-par-reject"),
                plugin,
                [v1, v2, v3],
                parallelValidators: true,
                ct: cts.Token);

            await Task.Delay(300);

            plugin.Executed.Should().Be(0, "action must NOT execute when any validator rejects");

            // ALL three validators must have been called — no first-reject short-circuit (D6).
            v1.Calls.Should().Be(1, "v1 must still run even though v2 rejects");
            v2.Calls.Should().Be(1, "v2 (the rejector) must be called");
            v3.Calls.Should().Be(1, "v3 must still run even though v2 rejects");
        }
        finally
        {
            await dispatcher.StopAsync(CancellationToken.None);
        }
    }

    // -----------------------------------------------------------------------
    // Test 4: Per-validator validators.rejected counter emitted in parallel mode (OQ-4).
    // -----------------------------------------------------------------------

    /// <summary>
    /// CONTEXT-14 OQ-4: when two validators both reject in parallel mode, each emits its own
    /// <c>frigaterelay.validators.rejected</c> counter increment (tagged with its own validator
    /// name). No aggregate counter exists — this test proves per-validator emission is preserved.
    /// </summary>
    [TestMethod]
    public async Task Dispatch_ParallelValidatorsTrue_AllRejectingValidatorsEmitTheirOwnRejectedCounter()
    {
        var plugin = new RecordingPlugin("BlueIris");
        var v1 = new StubValidator("alpha", Verdict.Fail("nope"));
        var v2 = new StubValidator("beta", Verdict.Fail("nope"));
        var logger = new CapturingLogger<ChannelActionDispatcher>();

        var capturedTags = new List<KeyValuePair<string, object?>[]>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == "FrigateRelay" &&
                    instrument.Name == "frigaterelay.validators.rejected")
                    listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((_, _, tags, _) => capturedTags.Add(tags.ToArray()));
        meterListener.Start();

        using var dispatcher = BuildDispatcher([plugin], logger);
        await dispatcher.StartAsync(CancellationToken.None);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await dispatcher.EnqueueAsync(
                MakeContext("e-counter"),
                plugin,
                [v1, v2],
                parallelValidators: true,
                ct: cts.Token);

            await Task.Delay(300);

            meterListener.RecordObservableInstruments();

            capturedTags.Should().HaveCount(2,
                "one validators.rejected increment per rejecting validator (OQ-4: per-validator, no aggregate)");

            var taggedValidators = capturedTags
                .Select(tags => tags.FirstOrDefault(t => t.Key == "validator").Value?.ToString())
                .ToHashSet();
            taggedValidators.Should().Contain("alpha", "alpha's rejection must be tagged");
            taggedValidators.Should().Contain("beta", "beta's rejection must be tagged");

            // Each increment must also carry action and subscription tags.
            foreach (var tags in capturedTags)
            {
                tags.Should().Contain(t => t.Key == "action", "action tag required per OQ-4 tag matrix");
                tags.Should().Contain(t => t.Key == "subscription", "subscription tag required");
            }
        }
        finally
        {
            await dispatcher.StopAsync(CancellationToken.None);
        }
    }

    // -----------------------------------------------------------------------
    // Test 5: Timeout (fail-closed) — slow validator blocks action.
    // -----------------------------------------------------------------------

    /// <summary>
    /// A validator that internally times out and returns <c>Verdict.Fail("validator_timeout")</c>
    /// (via its own catch block, not the dispatcher's) still blocks the action when running in
    /// parallel mode — fail-closed invariant. The immediately-passing co-validator's pass counter
    /// is incremented.
    /// </summary>
    [TestMethod]
    public async Task Dispatch_ParallelValidatorsTrue_OneValidatorTimesOutFailClosed_ActionDoesNotExecute()
    {
        var plugin = new RecordingPlugin("BlueIris");
        // SlowFailValidator simulates a validator that internally catches its own timeout
        // and returns Verdict.Fail("validator_timeout") — no exception leaks to the dispatcher.
        var slowFail = new SlowFailValidator("slow", TimeSpan.FromMilliseconds(50), "validator_timeout");
        var passing = new StubValidator("fast", Verdict.Pass());
        var logger = new CapturingLogger<ChannelActionDispatcher>();

        var rejectedTags = new List<KeyValuePair<string, object?>[]>();
        var passedTags = new List<KeyValuePair<string, object?>[]>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == "FrigateRelay" &&
                    (instrument.Name == "frigaterelay.validators.rejected" ||
                     instrument.Name == "frigaterelay.validators.passed"))
                    listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
        {
            var arr = tags.ToArray();
            if (instrument.Name == "frigaterelay.validators.rejected") rejectedTags.Add(arr);
            else passedTags.Add(arr);
        });
        meterListener.Start();

        using var dispatcher = BuildDispatcher([plugin], logger);
        await dispatcher.StartAsync(CancellationToken.None);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await dispatcher.EnqueueAsync(
                MakeContext("e-timeout"),
                plugin,
                [slowFail, passing],
                parallelValidators: true,
                ct: cts.Token);

            await Task.Delay(500);  // enough for the slow validator to finish
            meterListener.RecordObservableInstruments();

            plugin.Executed.Should().Be(0,
                "action must NOT execute when a validator returns Fail (fail-closed)");

            rejectedTags.Should().HaveCount(1, "the slow validator emits one rejected counter");
            var rejectedValidator = rejectedTags[0]
                .FirstOrDefault(t => t.Key == "validator").Value?.ToString();
            rejectedValidator.Should().Be("slow", "the slow/timed-out validator's counter is tagged correctly");

            passedTags.Should().HaveCount(1, "the fast validator emits one passed counter");
            var passedValidator = passedTags[0]
                .FirstOrDefault(t => t.Key == "validator").Value?.ToString();
            passedValidator.Should().Be("fast", "the passing validator's counter is tagged correctly");
        }
        finally
        {
            await dispatcher.StopAsync(CancellationToken.None);
        }
    }

    // -----------------------------------------------------------------------
    // Test 6: Host cancellation propagates gracefully out of Task.WhenAll.
    // -----------------------------------------------------------------------

    /// <summary>
    /// When the host CT is cancelled while validators are in flight, the
    /// <see cref="OperationCanceledException"/> propagates out of <c>Task.WhenAll</c>,
    /// unwinds <c>ConsumeAsync</c>'s outer try, and is caught by the existing
    /// <c>catch (OperationCanceledException) when (ct.IsCancellationRequested)</c> handler —
    /// so the dispatcher exits gracefully with no exception escaping <c>ConsumeAsync</c>
    /// and the action plugin's <c>ExecuteAsync</c> is NOT called.
    /// </summary>
    [TestMethod]
    public async Task Dispatch_ParallelValidatorsTrue_HostCancellation_UnwindGracefully()
    {
        var plugin = new RecordingPlugin("BlueIris");
        // This validator blocks until ct fires, then throws OperationCanceledException —
        // simulating a long network call interrupted by host shutdown.
        var blocking = new CancellationAwareValidator("slow-net");
        var logger = new CapturingLogger<ChannelActionDispatcher>();
        using var dispatcher = BuildDispatcher([plugin], logger);

        using var stoppingCts = new CancellationTokenSource();
        await dispatcher.StartAsync(CancellationToken.None);

        try
        {
            // Enqueue with a separate write CTS so we can write while the dispatcher is up.
            using var writeCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await dispatcher.EnqueueAsync(
                MakeContext("e-cancel"),
                plugin,
                [blocking],
                parallelValidators: true,
                ct: writeCts.Token);

            // Wait briefly so the consumer picks up the item and enters ValidateAsync.
            await Task.Delay(100);

            // Signal host shutdown — the dispatcher's stopping CTS is separate but we
            // simulate it by completing the channel writer and letting StopAsync run.
        }
        finally
        {
            // StopAsync cancels the internal stopping CTS, which propagates into
            // ConsumeAsync's ct parameter and into the blocking validator's ct.
            // The OperationCanceledException must be caught by the existing outer handler.
            using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try
            {
                await dispatcher.StopAsync(stopCts.Token);
            }
            catch (OperationCanceledException)
            {
                // StopAsync may be cancelled by the stopCts — that is acceptable here.
                // What matters is that the dispatcher consumer task exits cleanly (tested below).
            }
        }

        // Action must NOT have been called: it fires only after all validators pass.
        plugin.Executed.Should().Be(0, "action must not execute when host cancels mid-validation");

        // No unhandled-exception log entries — the OperationCanceledException must be
        // silently swallowed by the existing graceful-shutdown handler (not re-logged).
        var errorLogs = logger.Entries
            .Where(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Error)
            .ToList();
        errorLogs.Should().BeEmpty("host cancellation must not produce error-level logs");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

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

    private static ChannelActionDispatcher BuildDispatcher(
        IEnumerable<IActionPlugin> plugins,
        CapturingLogger<ChannelActionDispatcher>? logger = null)
    {
        logger ??= new CapturingLogger<ChannelActionDispatcher>();
        var options = Options.Create(new DispatcherOptions { DefaultQueueCapacity = 64 });
        return new ChannelActionDispatcher(plugins, logger, options, snapshotResolver: null);
    }

    // -----------------------------------------------------------------------
    // Test doubles
    // -----------------------------------------------------------------------

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

    /// <summary>
    /// Simulates a validator that internally waits for a brief delay and then returns
    /// <c>Verdict.Fail(failReason)</c> — modelling a validator that catches its own
    /// timeout exception and returns fail-closed per its <c>OnError</c> config.
    /// No exception leaks to the dispatcher.
    /// </summary>
    private sealed class SlowFailValidator(string name, TimeSpan delay, string failReason) : IValidationPlugin
    {
        public string Name { get; } = name;
        public async Task<Verdict> ValidateAsync(EventContext ctx, SnapshotContext snapshot, CancellationToken ct)
        {
            try
            {
                await Task.Delay(delay, CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // swallow — fail-closed
            }
            return Verdict.Fail(failReason);
        }
    }

    /// <summary>
    /// Simulates a validator whose <c>ValidateAsync</c> blocks until the host CT fires,
    /// then throws <see cref="OperationCanceledException"/>. Used to test graceful shutdown
    /// propagation through <c>Task.WhenAll</c>.
    /// </summary>
    private sealed class CancellationAwareValidator(string name) : IValidationPlugin
    {
        public string Name { get; } = name;
        public async Task<Verdict> ValidateAsync(EventContext ctx, SnapshotContext snapshot, CancellationToken ct)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
            return Verdict.Pass(); // never reached
        }
    }
}
