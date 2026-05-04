using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using FluentAssertions;
using FrigateRelay.Abstractions;
using FrigateRelay.Host;
using FrigateRelay.Host.Configuration;
using FrigateRelay.Host.Dispatch;
using FrigateRelay.Host.Matching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrigateRelay.Host.Tests.Observability;

/// <summary>
/// Counter increment unit tests per PLAN-3.1 Task 2 and CONTEXT-9 D3.
/// Uses BCL <see cref="MeterListener"/> (no NuGet package) filtered to
/// <c>instrument.Meter.Name == "FrigateRelay"</c> so dotnet.* runtime noise is excluded.
/// All tests use CooldownSeconds >= 1 (MemoryCache rejects TimeSpan.Zero TTL).
/// </summary>
[TestClass]
public sealed class CounterIncrementTests
{
    // -----------------------------------------------------------------------
    // Test 1: events.received increments once with camera+label tags
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task EventsReceived_Increments_WithCameraAndLabelTags()
    {
        var measurements = new List<(string name, long value, IReadOnlyList<KeyValuePair<string, object?>> tags)>();
        using var listener = BuildListener(measurements, "frigaterelay.events.received");

        var ctx = MakeContext("e1", "front_door", "person");
        await RunPumpAsync(new[] { ctx },
            subs: new[] { Sub("sub1", camera: "front_door", label: "person") });

        listener.RecordObservableInstruments();

        var hits = measurements.Where(m => m.name == "frigaterelay.events.received").ToList();
        hits.Should().HaveCount(1, "one event received = one counter increment");
        hits[0].value.Should().Be(1);
        TagValue(hits[0].tags, "camera").Should().Be("front_door");
        TagValue(hits[0].tags, "label").Should().Be("person");
    }

    // -----------------------------------------------------------------------
    // Test 2: events.matched increments per matched subscription with all 3 tags
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task EventsMatched_Increments_PerMatchedSubscription()
    {
        var measurements = new List<(string name, long value, IReadOnlyList<KeyValuePair<string, object?>> tags)>();
        using var listener = BuildListener(measurements, "frigaterelay.events.matched");

        var ctx = MakeContext("e2", "front_door", "person");
        // Two subs that both match — expect 2 increments
        await RunPumpAsync(new[] { ctx }, subs: new[]
        {
            Sub("sub_A", camera: "front_door", label: "person"),
            Sub("sub_B", camera: "front_door", label: "person"),
        });

        listener.RecordObservableInstruments();

        var hits = measurements.Where(m => m.name == "frigaterelay.events.matched").ToList();
        hits.Should().HaveCount(2, "two subscriptions matched = two increments");
        hits.All(m => TagValue(m.tags, "camera") == "front_door").Should().BeTrue();
        hits.All(m => TagValue(m.tags, "label") == "person").Should().BeTrue();
        var expectedSubs = new List<string?> { "sub_A", "sub_B" };
        hits.Select(m => TagValue(m.tags, "subscription")).Should()
            .BeEquivalentTo(expectedSubs, "both matched subscriptions must be tagged");
    }

    // -----------------------------------------------------------------------
    // Test 3: actions.dispatched increments once per EnqueueAsync call
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task ActionsDispatched_Increments_PerEnqueueAsyncCall()
    {
        var measurements = new List<(string name, long value, IReadOnlyList<KeyValuePair<string, object?>> tags)>();
        using var listener = BuildListener(measurements, "frigaterelay.actions.dispatched");

        var ctx = MakeContext("e3", "front_door", "person");
        // 1 sub × 2 actions = 2 dispatched
        var plugin1 = new StubPlugin("BlueIris");
        var plugin2 = new StubPlugin("Pushover");
        var twoActions = new List<string> { "BlueIris", "Pushover" };
        await RunPumpAsync(new[] { ctx },
            subs: new[] { Sub("sub1", camera: "front_door", label: "person", actions: twoActions.ToArray()) },
            plugins: new IActionPlugin[] { plugin1, plugin2 });

        listener.RecordObservableInstruments();

        var hits = measurements.Where(m => m.name == "frigaterelay.actions.dispatched").ToList();
        hits.Should().HaveCount(2, "two action entries = two dispatched increments");
        var expectedActions = new List<string?> { "BlueIris", "Pushover" };
        hits.Select(m => TagValue(m.tags, "action")).Should()
            .BeEquivalentTo(expectedActions, "each action name must appear as a tag");
        hits.All(m => TagValue(m.tags, "subscription") == "sub1").Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Test 4: actions.succeeded increments after plugin ExecuteAsync returns normally
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task ActionsSucceeded_Tags_SubscriptionAndAction_OnNormalReturn()
    {
        var measurements = new List<(string name, long value, IReadOnlyList<KeyValuePair<string, object?>> tags)>();
        using var listener = BuildListener(measurements, "frigaterelay.actions.succeeded");

        var plugin = new StubPlugin("BlueIris");
        await RunDispatcherAsync(plugin, validators: Array.Empty<IValidationPlugin>(),
            subscription: "sub_ok", shouldThrow: false);

        listener.RecordObservableInstruments();

        var hits = measurements.Where(m => m.name == "frigaterelay.actions.succeeded").ToList();
        hits.Should().HaveCount(1);
        TagValue(hits[0].tags, "subscription").Should().Be("sub_ok");
        TagValue(hits[0].tags, "action").Should().Be("BlueIris");
    }

    // -----------------------------------------------------------------------
    // Test 5: actions.failed increments after plugin throws (retry exhaustion)
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task ActionsFailed_Tags_SubscriptionAndAction_OnRetryExhaustion()
    {
        var measurements = new List<(string name, long value, IReadOnlyList<KeyValuePair<string, object?>> tags)>();
        using var listener = BuildListener(measurements, "frigaterelay.actions.failed");

        var plugin = new ThrowingPlugin("BlueIris");
        await RunDispatcherAsync(plugin, validators: Array.Empty<IValidationPlugin>(),
            subscription: "sub_fail", shouldThrow: true);

        listener.RecordObservableInstruments();

        var hits = measurements.Where(m => m.name == "frigaterelay.actions.failed").ToList();
        hits.Should().HaveCount(1);
        TagValue(hits[0].tags, "subscription").Should().Be("sub_fail");
        TagValue(hits[0].tags, "action").Should().Be("BlueIris");
    }

    // -----------------------------------------------------------------------
    // Test 6: validators.passed increments with subscription+action+validator tags
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task ValidatorsPassed_Tags_SubscriptionActionValidator()
    {
        var measurements = new List<(string name, long value, IReadOnlyList<KeyValuePair<string, object?>> tags)>();
        using var listener = BuildListener(measurements, "frigaterelay.validators.passed");

        var plugin = new StubPlugin("Pushover");
        var validator = new StubValidator("strict-person", Verdict.Pass());
        await RunDispatcherAsync(plugin, validators: new IValidationPlugin[] { validator },
            subscription: "sub_val", shouldThrow: false);

        listener.RecordObservableInstruments();

        var hits = measurements.Where(m => m.name == "frigaterelay.validators.passed").ToList();
        hits.Should().HaveCount(1, "one validator passed = one increment");
        TagValue(hits[0].tags, "subscription").Should().Be("sub_val");
        TagValue(hits[0].tags, "action").Should().Be("Pushover");
        TagValue(hits[0].tags, "validator").Should().Be("strict-person");
    }

    // -----------------------------------------------------------------------
    // Test 7: validators.rejected increments and action plugin is NEVER called
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task ValidatorsRejected_Tags_SubscriptionActionValidator_OnFail()
    {
        var measurements = new List<(string name, long value, IReadOnlyList<KeyValuePair<string, object?>> tags)>();
        using var listener = BuildListener(measurements,
            "frigaterelay.validators.rejected", "frigaterelay.actions.succeeded");

        var plugin = new RecordingPlugin("Pushover");
        var validator = new StubValidator("strict-person", Verdict.Fail("low_confidence"));
        await RunDispatcherAsync(plugin, validators: new IValidationPlugin[] { validator },
            subscription: "sub_reject", shouldThrow: false);

        listener.RecordObservableInstruments();

        var rejected = measurements.Where(m => m.name == "frigaterelay.validators.rejected").ToList();
        rejected.Should().HaveCount(1, "one failed verdict = one rejected increment");
        TagValue(rejected[0].tags, "validator").Should().Be("strict-person");
        TagValue(rejected[0].tags, "action").Should().Be("Pushover");

        // Short-circuit: action must NOT have been called (PROJECT.md V3)
        plugin.Executed.Should().Be(0, "validator rejection must short-circuit the action");
        measurements.Where(m => m.name == "frigaterelay.actions.succeeded")
            .Should().BeEmpty("no success counter when validator rejects");
    }

    // -----------------------------------------------------------------------
    // Test 8: errors.unhandled increments ONCE with NO tags when IEventSource throws
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task ErrorsUnhandled_Increments_OnPumpFault_Once_TaggedWithComponent()
    {
        var measurements = new List<(string name, long value, IReadOnlyList<KeyValuePair<string, object?>> tags)>();
        using var listener = BuildListener(measurements, "frigaterelay.errors.unhandled");

        // FaultingSource throws from ReadEventsAsync — pump outermost catch fires
        var source = new FaultingSource("FrigateMqtt");
        await RunPumpAsyncWithSource(source, subs: new[] { Sub("sub1") });

        listener.RecordObservableInstruments();

        var hits = measurements.Where(m => m.name == "frigaterelay.errors.unhandled").ToList();
        hits.Should().HaveCount(1, "single unhandled error = one increment");
        hits[0].value.Should().Be(1);
        hits[0].tags.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new KeyValuePair<string, object?>("component", "EventPump"),
                "Phase 13 issue #35 / CONTEXT-13 D-section adds the `component` tag so operators can triage unhandled errors by failing subsystem");
    }

    // -----------------------------------------------------------------------
    // Test 9: errors.unhandled does NOT increment on retry exhaustion (D9)
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task ErrorsUnhandled_DoesNotIncrement_OnRetryExhaustion()
    {
        var measurements = new List<(string name, long value, IReadOnlyList<KeyValuePair<string, object?>> tags)>();
        using var listener = BuildListener(measurements, "frigaterelay.errors.unhandled");

        // ThrowingPlugin exhausts retries — increments actions.failed, NOT errors.unhandled
        var plugin = new ThrowingPlugin("BlueIris");
        await RunDispatcherAsync(plugin, validators: Array.Empty<IValidationPlugin>(),
            subscription: "sub_exhaust", shouldThrow: true);

        listener.RecordObservableInstruments();

        measurements.Where(m => m.name == "frigaterelay.errors.unhandled")
            .Should().BeEmpty(
                "retry exhaustion increments actions.failed only; errors.unhandled is reserved " +
                "for unexpected escapes from the pipeline (CONTEXT-9 D9)");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds a <see cref="MeterListener"/> that captures <c>Add</c> calls on the named
    /// instruments from the <c>"FrigateRelay"</c> meter.
    /// </summary>
    private static MeterListener BuildListener(
        List<(string name, long value, IReadOnlyList<KeyValuePair<string, object?>> tags)> sink,
        params string[] instrumentNames)
    {
        var nameSet = new HashSet<string>(instrumentNames);
        var ml = new MeterListener();
        ml.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "FrigateRelay" &&
                (nameSet.Count == 0 || nameSet.Contains(instrument.Name)))
                listener.EnableMeasurementEvents(instrument);
        };
        ml.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            var tagList = new List<KeyValuePair<string, object?>>();
            foreach (var t in tags)
                tagList.Add(new KeyValuePair<string, object?>(t.Key, t.Value));
            lock (sink)
                sink.Add((instrument.Name, measurement, tagList));
        });
        ml.Start();
        return ml;
    }

    private static string? TagValue(IReadOnlyList<KeyValuePair<string, object?>> tags, string key)
    {
        foreach (var kv in tags)
            if (kv.Key == key) return kv.Value?.ToString();
        return null;
    }

    private static EventContext MakeContext(string eventId, string camera = "front_door", string label = "person") =>
        new()
        {
            EventId = eventId,
            Camera = camera,
            Label = label,
            Zones = Array.Empty<string>(),
            StartedAt = DateTimeOffset.UtcNow,
            RawPayload = "{}",
            SnapshotFetcher = _ => ValueTask.FromResult<byte[]?>(null),
        };

    private static SubscriptionOptions Sub(
        string name,
        string camera = "front_door",
        string label = "person",
        string[]? actions = null,
        int cooldownSeconds = 60) =>
        new()
        {
            Name = name,
            Camera = camera,
            Label = label,
            CooldownSeconds = cooldownSeconds,
            Actions = (actions ?? Array.Empty<string>())
                .Select(a => new ActionEntry(a)).ToArray(),
        };

    /// <summary>
    /// Runs the full EventPump → ChannelActionDispatcher pipeline end-to-end.
    /// All subscriptions are wired with the provided plugins.
    /// </summary>
    private static async Task RunPumpAsync(
        EventContext[] events,
        SubscriptionOptions[] subs,
        IActionPlugin[]? plugins = null)
    {
        plugins ??= Array.Empty<IActionPlugin>();

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var dedupe = new DedupeCache(cache);
        var monitor = new StaticMonitor<HostSubscriptionsOptions>(
            new HostSubscriptionsOptions { Subscriptions = subs });
        var logger = new CapturingLogger<EventPump>();
        var source = new BatchSource("test", events);

        // If plugins supplied, wire a real dispatcher so ActionsDispatched fires correctly.
        ChannelActionDispatcher? realDispatcher = null;
        IActionDispatcher dispatcher;
        if (plugins.Length > 0)
        {
            var opts = Options.Create(new DispatcherOptions { DefaultQueueCapacity = 64 });
            var dLogger = new CapturingLogger<ChannelActionDispatcher>();
            realDispatcher = new ChannelActionDispatcher(plugins, dLogger, opts);
            await realDispatcher.StartAsync(CancellationToken.None);
            dispatcher = realDispatcher;
        }
        else
        {
            dispatcher = NoOpDispatcher.Instance;
        }

        try
        {
            var pump = new EventPump(
                new[] { (IEventSource)source }, dedupe, monitor,
                dispatcher, plugins, EmptyServiceProvider.Instance, logger);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await pump.StartAsync(cts.Token);
            await Task.Delay(400); // ID-22 polling improvement deferred
            await cts.CancelAsync();
            await pump.StopAsync(CancellationToken.None);
        }
        finally
        {
            if (realDispatcher is not null)
            {
                await realDispatcher.StopAsync(CancellationToken.None);
                realDispatcher.Dispose();
            }
        }
    }

    /// <summary>
    /// Variant of RunPumpAsync that uses a pre-built IEventSource directly (e.g. FaultingSource).
    /// </summary>
    private static async Task RunPumpAsyncWithSource(
        IEventSource source,
        SubscriptionOptions[] subs)
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var dedupe = new DedupeCache(cache);
        var monitor = new StaticMonitor<HostSubscriptionsOptions>(
            new HostSubscriptionsOptions { Subscriptions = subs });
        var logger = new CapturingLogger<EventPump>();

        var pump = new EventPump(
            new[] { source }, dedupe, monitor,
            NoOpDispatcher.Instance, Array.Empty<IActionPlugin>(),
            EmptyServiceProvider.Instance, logger);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await pump.StartAsync(cts.Token);
        await Task.Delay(300); // ID-22 polling improvement deferred
        await cts.CancelAsync();
        await pump.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// Runs a single <see cref="ChannelActionDispatcher.ConsumeAsync"/> path by enqueuing
    /// one item and letting the consumer process it.
    /// </summary>
    private static async Task RunDispatcherAsync(
        IActionPlugin plugin,
        IValidationPlugin[] validators,
        string subscription,
        bool shouldThrow)
    {
        var opts = Options.Create(new DispatcherOptions { DefaultQueueCapacity = 64 });
        var logger = new CapturingLogger<ChannelActionDispatcher>();
        using var dispatcher = new ChannelActionDispatcher(
            new[] { plugin }, logger, opts);

        await dispatcher.StartAsync(CancellationToken.None);
        try
        {
            var ctx = MakeContext("e-disp");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await dispatcher.EnqueueAsync(ctx, plugin, validators, subscription,
                perActionSnapshotProvider: null,
                subscriptionDefaultSnapshotProvider: null,
                ct: cts.Token);

            // Give consumer time to process: ThrowingPlugin fails immediately,
            // StubPlugin succeeds immediately. (ID-22 polling improvement deferred.)
            await Task.Delay(shouldThrow ? 200 : 100);
        }
        finally
        {
            await dispatcher.StopAsync(CancellationToken.None);
        }
    }

    // -----------------------------------------------------------------------
    // Stub types
    // -----------------------------------------------------------------------

    private sealed class StubPlugin(string name) : IActionPlugin
    {
        public string Name { get; } = name;
        public Task ExecuteAsync(EventContext ctx, SnapshotContext snapshot, CancellationToken ct) =>
            Task.CompletedTask;
    }

    private sealed class ThrowingPlugin(string name) : IActionPlugin
    {
        public string Name { get; } = name;
        public Task ExecuteAsync(EventContext ctx, SnapshotContext snapshot, CancellationToken ct) =>
            Task.FromException(new InvalidOperationException("simulated failure"));
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
        public string Name { get; } = name;
        public Task<Verdict> ValidateAsync(EventContext ctx, SnapshotContext snapshot, CancellationToken ct) =>
            Task.FromResult(verdict);
    }

    private sealed class BatchSource(string name, EventContext[] events) : IEventSource
    {
        public string Name { get; } = name;
        public async IAsyncEnumerable<EventContext> ReadEventsAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var e in events)
            {
                ct.ThrowIfCancellationRequested();
                yield return e;
            }
            await Task.Delay(Timeout.Infinite, ct);
        }
    }

    /// <summary>
    /// Throws immediately from <see cref="ReadEventsAsync"/> to exercise the outermost
    /// pump catch block (D9: errors.unhandled single-site).
    /// </summary>
    private sealed class FaultingSource(string name) : IEventSource
    {
        public string Name { get; } = name;
        public async IAsyncEnumerable<EventContext> ReadEventsAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            // Yield nothing, then throw — exercises EventPump.PumpAsync outermost catch (D9).
            await Task.Yield();
            if (!ct.IsCancellationRequested)
                throw new InvalidOperationException("simulated source fault");
            yield break;
        }
    }

    private sealed class NoOpDispatcher : IActionDispatcher
    {
        public static readonly NoOpDispatcher Instance = new();
        public ValueTask EnqueueAsync(
            EventContext ctx, IActionPlugin action, IReadOnlyList<IValidationPlugin> validators,
            string subscription, string? perActionSnapshotProvider,
            string? subscriptionDefaultSnapshotProvider, CancellationToken ct) => ValueTask.CompletedTask;
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static readonly EmptyServiceProvider Instance = new();
        public object? GetService(Type serviceType) => null;
    }

    private sealed class StaticMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
