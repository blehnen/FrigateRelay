using System.Runtime.CompilerServices;
using FluentAssertions;
using FrigateRelay.Abstractions;
using FrigateRelay.Host;
using FrigateRelay.Host.Configuration;
using FrigateRelay.Host.Dispatch;
using FrigateRelay.Host.Matching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;

namespace FrigateRelay.Host.Tests;

[TestClass]
public sealed class EventPumpDispatchTests
{
    [TestMethod]
    public async Task PumpAsync_OnMatchedEvent_CallsDispatcherEnqueueAsync_OncePerActionName()
    {
        var ctx = MakeContext("ev-1");
        var blueIris = Substitute.For<IActionPlugin>();
        blueIris.Name.Returns("BlueIris");

        var dispatcher = Substitute.For<IActionDispatcher>();
        var (pump, cache) = BuildPump(
            events: [ctx],
            subs: [Sub("sub1", actions: ["BlueIris"])],
            dispatcher: dispatcher,
            plugins: [blueIris]);

        using (cache) await RunPump(pump);

        await dispatcher.Received(1).EnqueueAsync(
            Arg.Is<EventContext>(c => c.EventId == "ev-1"),
            Arg.Is<IActionPlugin>(p => p.Name == "BlueIris"),
            Arg.Is<IReadOnlyList<IValidationPlugin>>(v => v.Count == 0),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task PumpAsync_OnDedupeSuppressed_DoesNotCallDispatcher()
    {
        var ctx1 = MakeContext("ev-dedupe-1");
        var ctx2 = MakeContext("ev-dedupe-2"); // same camera+label → dedupe suppresses
        var blueIris = Substitute.For<IActionPlugin>();
        blueIris.Name.Returns("BlueIris");

        var dispatcher = Substitute.For<IActionDispatcher>();
        var (pump, cache) = BuildPump(
            events: [ctx1, ctx2],
            subs: [Sub("sub1", actions: ["BlueIris"], cooldownSeconds: 300)],
            dispatcher: dispatcher,
            plugins: [blueIris]);

        using (cache) await RunPump(pump);

        // First event dispatched, second suppressed by dedupe.
        await dispatcher.Received(1).EnqueueAsync(
            Arg.Any<EventContext>(),
            Arg.Any<IActionPlugin>(),
            Arg.Any<IReadOnlyList<IValidationPlugin>>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task PumpAsync_WithMultipleActionsInSubscription_DispatchesEachOnce()
    {
        var ctx = MakeContext("ev-multi");
        var blueIris = Substitute.For<IActionPlugin>();
        blueIris.Name.Returns("BlueIris");
        var pushover = Substitute.For<IActionPlugin>();
        pushover.Name.Returns("Pushover");

        var dispatcher = Substitute.For<IActionDispatcher>();
        var (pump, cache) = BuildPump(
            events: [ctx],
            subs: [Sub("sub1", actions: ["BlueIris", "Pushover"])],
            dispatcher: dispatcher,
            plugins: [blueIris, pushover]);

        using (cache) await RunPump(pump);

        await dispatcher.Received(1).EnqueueAsync(
            Arg.Any<EventContext>(),
            Arg.Is<IActionPlugin>(p => p.Name == "BlueIris"),
            Arg.Any<IReadOnlyList<IValidationPlugin>>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());

        await dispatcher.Received(1).EnqueueAsync(
            Arg.Any<EventContext>(),
            Arg.Is<IActionPlugin>(p => p.Name == "Pushover"),
            Arg.Any<IReadOnlyList<IValidationPlugin>>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static EventContext MakeContext(string eventId) => new()
    {
        EventId = eventId,
        Camera = "front",
        Label = "person",
        Zones = Array.Empty<string>(),
        StartedAt = DateTimeOffset.UtcNow,
        RawPayload = "{}",
        SnapshotFetcher = _ => ValueTask.FromResult<byte[]?>(null),
    };

    private static SubscriptionOptions Sub(string name, string[] actions, int cooldownSeconds = 60) =>
        new()
        {
            Name = name,
            Camera = "front",
            Label = "person",
            CooldownSeconds = cooldownSeconds,
            Actions = actions.Select(a => new ActionEntry(a)).ToArray(),
        };

    private static (EventPump pump, IDisposable cache) BuildPump(
        EventContext[] events,
        SubscriptionOptions[] subs,
        IActionDispatcher dispatcher,
        IActionPlugin[] plugins)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var dedupe = new DedupeCache(cache);
        var opts = new HostSubscriptionsOptions { Subscriptions = subs };
        var monitor = new StaticMonitor<HostSubscriptionsOptions>(opts);
        var source = new SingleBatchSource("test", events);
        var pump = new EventPump(
            new IEventSource[] { source },
            dedupe,
            monitor,
            dispatcher,
            plugins,
            Substitute.For<IServiceProvider>(),
            NullLogger<EventPump>.Instance);
        return (pump, cache);
    }

    private static async Task RunPump(EventPump pump)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await pump.StartAsync(cts.Token);
        await Task.Delay(200); // let events flush through the channel
        await cts.CancelAsync();
        await pump.StopAsync(CancellationToken.None);
    }

    private sealed class SingleBatchSource(string name, EventContext[] events) : IEventSource
    {
        public string Name { get; } = name;

        public async IAsyncEnumerable<EventContext> ReadEventsAsync([EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var e in events)
            {
                ct.ThrowIfCancellationRequested();
                yield return e;
            }
            await Task.Delay(Timeout.Infinite, ct);
        }
    }

    private sealed class StaticMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
