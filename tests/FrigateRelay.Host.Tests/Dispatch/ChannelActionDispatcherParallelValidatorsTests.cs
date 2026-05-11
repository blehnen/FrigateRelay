using System.Diagnostics.Metrics;
using FluentAssertions;
using FrigateRelay.Abstractions;
using FrigateRelay.Host.Dispatch;
using FrigateRelay.Host.Observability;
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

            // Deterministic wait: the first (failing) validator must have been called and
            // the dispatcher must have completed processing this dispatch item. Sequential
            // short-circuit guarantees the second validator was NOT called once the first
            // returned, but we wait on the FAILING validator's call to confirm processing
            // actually happened (otherwise a 0/0/0 state would also pass the assertions).
            await WaitUntilAsync(() => failing.Calls == 1, TimeSpan.FromSeconds(5),
                "first validator to be called");

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

            await WaitUntilAsync(() => plugin.Executed == 1, TimeSpan.FromSeconds(5),
                "action plugin to execute after both validators pass");

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

            // Wait until ALL three validators have been called — strict-AND with no
            // short-circuit means every validator runs regardless of outcome (D6).
            await WaitUntilAsync(
                () => v1.Calls == 1 && v2.Calls == 1 && v3.Calls == 1,
                TimeSpan.FromSeconds(5),
                "all three validators to be called");

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

            // Both rejecting validators must have emitted their own counter increment.
            await WaitUntilAsync(() => capturedTags.Count >= 2, TimeSpan.FromSeconds(5),
                "two validators.rejected counter samples (one per rejecting validator)");

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
    /// A validator that internally translates its own timeout into <c>Verdict.Fail("validator_timeout")</c>
    /// (FailClosed semantics applied by the validator itself, before returning) still blocks the
    /// action when running in parallel mode — fail-closed invariant. The immediately-passing
    /// co-validator's pass counter is still incremented because parallel mode does not short-circuit
    /// on a single rejection.
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

            // Wait until both counters have been emitted: one rejected (slow validator
            // returned Fail) and one passed (fast validator). Polling is bounded — the
            // slow validator's internal Task.Delay is 50 ms, so 5 s is generous on CI.
            await WaitUntilAsync(
                () => rejectedTags.Count >= 1 && passedTags.Count >= 1,
                TimeSpan.FromSeconds(5),
                "both rejected (slow) and passed (fast) counter samples");

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

        await dispatcher.StartAsync(CancellationToken.None);

        // Enqueue with a separate write CTS so we can write while the dispatcher is up.
        using var writeCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await dispatcher.EnqueueAsync(
            MakeContext("e-cancel"),
            plugin,
            [blocking],
            parallelValidators: true,
            ct: writeCts.Token);

        // Deterministic wait for the consumer to enter the validator. Without this signal
        // the test could pass vacuously: StopAsync's drain-token timeout would fire before
        // the validator ever blocked, so we'd never actually exercise the cancellation
        // chain (REVIEW-3.2 Important #1). The TCS is set the instant ValidateAsync begins.
        await blocking.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Now trigger graceful host shutdown. StopAsync cancels the internal stopping CTS
        // (REVIEW-3.2 Important #2 fix), which signals the validator's `ct`. The validator's
        // Task.Delay(30s, ct) throws OperationCanceledException; that propagates out of
        // Task.WhenAll, up the dispatch's call stack, and is caught by ConsumeAsync's
        // existing outer `catch (OperationCanceledException) when (ct.IsCancellationRequested)`
        // handler at ChannelActionDispatcher.cs:259, which exits the consumer loop gracefully.
        // The drain-window CTS gives StopAsync up to 5 seconds to await the consumer task.
        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await dispatcher.StopAsync(stopCts.Token);

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
        return new ChannelActionDispatcher(plugins, logger, options, metricsTagWriter: CreatePassthroughTagWriter(), snapshotResolver: null);
    }

    private static MetricsTagWriter CreatePassthroughTagWriter() =>
        new(new StaticOptionsMonitor<MetricsTagsOptions>(new MetricsTagsOptions()));

    /// <summary>
    /// Polls <paramref name="predicate"/> every 10 ms until it returns true or
    /// <paramref name="timeout"/> elapses. Replaces wall-clock <c>Task.Delay</c>
    /// settle windows that pass on fast machines but flake on busy CI runners.
    /// </summary>
    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout, string description)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) return;
            await Task.Delay(10).ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Timed out after {timeout.TotalMilliseconds:F0} ms waiting for: {description}");
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
    /// <c>Verdict.Fail(failReason)</c> — modelling a real validator whose own
    /// <c>OnError: FailClosed</c> path observed an internal timeout and translated it
    /// into a failing verdict before returning. The dispatcher never sees an exception;
    /// it only sees a slow validator that ultimately rejected. Used together with a
    /// fast-passing co-validator to prove parallel scheduling preserves per-validator
    /// counter emission AND strict-AND aggregation in a fail-closed scenario.
    /// </summary>
    private sealed class SlowFailValidator(string name, TimeSpan delay, string failReason) : IValidationPlugin
    {
        public string Name { get; } = name;
        public async Task<Verdict> ValidateAsync(EventContext ctx, SnapshotContext snapshot, CancellationToken ct)
        {
            // Intentionally NOT cancellable: this models a validator that has already
            // caught its own internal timeout and is on its return path with a baked-in
            // FailClosed verdict. The dispatcher's host CT does not interrupt this branch.
            await Task.Delay(delay, CancellationToken.None).ConfigureAwait(false);
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
        /// <summary>
        /// Signals when the validator has entered <see cref="ValidateAsync"/> and is about to
        /// block on the cancellation token. Tests await this to ensure the consumer task is
        /// genuinely in-flight before triggering host shutdown — without this, the
        /// "host cancellation unwinds gracefully" test could pass vacuously by hitting its
        /// own timeout before the validator ever ran (REVIEW-3.2 finding).
        /// </summary>
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Name { get; } = name;
        public async Task<Verdict> ValidateAsync(EventContext ctx, SnapshotContext snapshot, CancellationToken ct)
        {
            Entered.TrySetResult();
            await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
            return Verdict.Pass(); // never reached — the 30s delay is interrupted by ct cancellation on host shutdown.
        }
    }
}
