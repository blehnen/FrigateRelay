using System.Diagnostics;
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
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Trace;

namespace FrigateRelay.Host.Tests.Observability;

/// <summary>
/// Span attribute shape assertions for the spans emitted by <see cref="EventPump.PumpAsync"/>.
/// Uses <c>InMemoryExporter&lt;Activity&gt;</c> wrapped in <c>SimpleActivityExportProcessor</c>
/// so spans are captured synchronously (no batch-delay race).
/// RESEARCH.md §9: use <c>ExportProcessorType.Simple</c> for synchronous test assertions.
/// </summary>
[TestClass]
public sealed class EventPumpSpanTests
{
    // -----------------------------------------------------------------------
    // Test 1: mqtt.receive span has event.id and event.source tags (D8 table)
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task MqttReceiveSpan_HasEventIdAndSourceTags()
    {
        var (activities, tracerProvider) = BuildSimpleTracerProvider();
        using var _ = tracerProvider;

        var context = MakeContext("evt-001", "front_door", "person");
        var source = new FakeSource("FrigateMqtt", new[] { context });
        await RunPumpAsync(source, subs: SingleSubNoActions("front_door", "person"), noActions: true);

        tracerProvider.ForceFlush(5000);

        var receiveSpan = activities.FirstOrDefault(a => a.DisplayName == "mqtt.receive");
        receiveSpan.Should().NotBeNull("EventPump must emit one mqtt.receive span per event");
        receiveSpan!.Kind.Should().Be(ActivityKind.Server, "mqtt.receive is a server-side receive");

        GetTag(receiveSpan, "event.id").Should().Be("evt-001");
        GetTag(receiveSpan, "event.source").Should().Be("FrigateMqtt");
    }

    // -----------------------------------------------------------------------
    // Test 2: event.match span has camera, label, subscription_count_matched tags
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task EventMatchSpan_TagsCameraLabelAndMatchCount()
    {
        var (activities, tracerProvider) = BuildSimpleTracerProvider();
        using var _ = tracerProvider;

        var context = MakeContext("evt-002", "front_door", "person");
        var source = new FakeSource("FrigateMqtt", new[] { context });

        // Two subscriptions both matching — subscription_count_matched should be 2.
        // CooldownSeconds must be >= 1: MemoryCache requires AbsoluteExpirationRelativeToNow > 0.
        var sub1 = new SubscriptionOptions { Name = "sub_A", Camera = "front_door", Label = "person", CooldownSeconds = 1 };
        var sub2 = new SubscriptionOptions { Name = "sub_B", Camera = "front_door", Label = "person", CooldownSeconds = 1 };
        var subs = new HostSubscriptionsOptions { Subscriptions = new[] { sub1, sub2 } };

        await RunPumpAsync(source, subs: subs, noActions: true);

        tracerProvider.ForceFlush(5000);

        var matchSpan = activities.FirstOrDefault(a => a.DisplayName == "event.match");
        matchSpan.Should().NotBeNull("EventPump must emit one event.match span per event");

        // SetTag with string key + int value goes to TagObjects, not Tags.
        // GetTag reads TagObjects and converts to string.
        GetTag(matchSpan!, "event.id").Should().Be("evt-002");
        GetTag(matchSpan!, "camera").Should().Be("front_door");
        GetTag(matchSpan!, "label").Should().Be("person");
        GetTag(matchSpan!, "subscription_count_matched").Should().Be("2");
    }

    // -----------------------------------------------------------------------
    // Test 3: dispatch.enqueue span has subscription, action_count, event.id, Kind=Producer
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task DispatchEnqueueSpan_TagsSubscriptionAndActionCount()
    {
        var (activities, tracerProvider) = BuildSimpleTracerProvider();
        using var _ = tracerProvider;

        var context = MakeContext("evt-003", "side_door", "car");
        var source = new FakeSource("FrigateMqtt", new[] { context });

        var plugin1 = new StubPlugin("BlueIris");
        var plugin2 = new StubPlugin("Pushover");
        IActionPlugin[] plugins = { plugin1, plugin2 };

        // CooldownSeconds >= 1 required by MemoryCache (AbsoluteExpirationRelativeToNow must be positive).
        var sub = new SubscriptionOptions
        {
            Name = "side_sub",
            Camera = "side_door",
            Label = "car",
            CooldownSeconds = 1,
            Actions = new[]
            {
                new ActionEntry("BlueIris"),
                new ActionEntry("Pushover"),
            },
        };
        var subs = new HostSubscriptionsOptions { Subscriptions = new[] { sub } };

        await RunPumpAsync(source, subs: subs, plugins: plugins);

        tracerProvider.ForceFlush(5000);

        var enqueueSpan = activities.FirstOrDefault(a => a.DisplayName == "dispatch.enqueue");
        enqueueSpan.Should().NotBeNull("EventPump must emit one dispatch.enqueue span per matched subscription");
        enqueueSpan!.Kind.Should().Be(ActivityKind.Producer, "dispatch.enqueue is a producer span (D1)");

        GetTag(enqueueSpan, "event.id").Should().Be("evt-003");
        GetTag(enqueueSpan, "subscription").Should().Be("side_sub");
        GetTag(enqueueSpan, "action_count").Should().Be("2");
    }

    // -----------------------------------------------------------------------
    // Test 4: mqtt.receive is parent of event.match and dispatch.enqueue
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task MqttReceive_IsParentOf_EventMatch_AndDispatchEnqueue()
    {
        var (activities, tracerProvider) = BuildSimpleTracerProvider();
        using var _ = tracerProvider;

        var context = MakeContext("evt-004", "back_yard", "person");
        var source = new FakeSource("FrigateMqtt", new[] { context });

        var plugin = new StubPlugin("BlueIris");
        IActionPlugin[] plugins = { plugin };

        // CooldownSeconds >= 1 required by MemoryCache (AbsoluteExpirationRelativeToNow must be positive).
        var sub = new SubscriptionOptions
        {
            Name = "yard_sub",
            Camera = "back_yard",
            Label = "person",
            CooldownSeconds = 1,
            Actions = new[] { new ActionEntry("BlueIris") },
        };
        var subs = new HostSubscriptionsOptions { Subscriptions = new[] { sub } };

        await RunPumpAsync(source, subs: subs, plugins: plugins);

        tracerProvider.ForceFlush(5000);

        var receiveSpan = activities.FirstOrDefault(a => a.DisplayName == "mqtt.receive");
        var matchSpan   = activities.FirstOrDefault(a => a.DisplayName == "event.match");
        var enqueueSpan = activities.FirstOrDefault(a => a.DisplayName == "dispatch.enqueue");

        receiveSpan.Should().NotBeNull("mqtt.receive span must exist");
        matchSpan.Should().NotBeNull("event.match span must exist");
        enqueueSpan.Should().NotBeNull("dispatch.enqueue span must exist");

        matchSpan!.ParentSpanId.Should().Be(receiveSpan!.SpanId,
            "event.match must be a child of mqtt.receive");
        enqueueSpan!.ParentSpanId.Should().Be(receiveSpan!.SpanId,
            "dispatch.enqueue must be a child of mqtt.receive");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds a <see cref="TracerProvider"/> that captures activities synchronously via
    /// <c>InMemoryExporter&lt;Activity&gt;</c> wrapped in <c>SimpleActivityExportProcessor</c>.
    /// This is the RESEARCH.md §9 pattern: Simple processor exports spans as they finish,
    /// avoiding the 5-second batch-delay race that breaks synchronous test assertions.
    /// </summary>
    private static (List<Activity> activities, TracerProvider tracerProvider) BuildSimpleTracerProvider()
    {
        var activities = new List<Activity>();
        var exporter = new InMemoryExporter<Activity>(activities);
        var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource("FrigateRelay")
            .AddProcessor(new SimpleActivityExportProcessor(exporter))
            .Build()!;
        return (activities, tracerProvider);
    }

    /// <summary>
    /// Reads a tag value from <see cref="Activity.TagObjects"/> (which captures all tags
    /// regardless of whether they were set via the <c>string</c> or <c>object</c> overload
    /// of <see cref="Activity.SetTag"/>), then converts to string for assertion.
    /// <c>Activity.Tags</c> only reflects string-typed tags; numeric tags set via
    /// <c>SetTag(string, object?)</c> are silently absent from that enumerator.
    /// </summary>
    private static string? GetTag(Activity activity, string key)
    {
        foreach (var tag in activity.TagObjects)
        {
            if (tag.Key == key)
                return tag.Value?.ToString();
        }
        return null;
    }

    private static EventContext MakeContext(string eventId, string camera, string label) => new()
    {
        EventId = eventId,
        Camera = camera,
        Label = label,
        Zones = Array.Empty<string>(),
        StartedAt = DateTimeOffset.UtcNow,
        RawPayload = "{}",
        SnapshotFetcher = _ => ValueTask.FromResult<byte[]?>(null),
    };

    private static HostSubscriptionsOptions SingleSubNoActions(string camera, string label) =>
        new()
        {
            Subscriptions = new[]
            {
                new SubscriptionOptions
                {
                    Name = "default_sub",
                    Camera = camera,
                    Label = label,
                    // CooldownSeconds >= 1: MemoryCache requires AbsoluteExpirationRelativeToNow > Zero.
                    CooldownSeconds = 1,
                },
            },
        };

    /// <summary>
    /// Spins up <see cref="EventPump"/> with the given source and subscriptions,
    /// runs it until the source stream is exhausted, then stops it.
    /// When <paramref name="plugins"/> is non-empty, wires a real
    /// <see cref="ChannelActionDispatcher"/> so <c>EnqueueAsync</c> succeeds and the
    /// <c>dispatch.enqueue</c> span is emitted.
    /// </summary>
    private static async Task RunPumpAsync(
        IEventSource source,
        HostSubscriptionsOptions? subs = null,
        IActionPlugin[]? plugins = null,
        bool noActions = false)
    {
        subs ??= SingleSubNoActions("front_door", "person");
        plugins ??= Array.Empty<IActionPlugin>();

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var dedupe = new DedupeCache(cache);
        var monitor = new StaticMonitor<HostSubscriptionsOptions>(subs);
        var logger = new CapturingLogger<EventPump>();

        ChannelActionDispatcher? realDispatcher = null;
        IActionDispatcher dispatcher;

        if (noActions || plugins.Length == 0)
        {
            dispatcher = NoOpDispatcher.Instance;
        }
        else
        {
            var opts = Options.Create(new DispatcherOptions { DefaultQueueCapacity = 64 });
            var dLogger = new CapturingLogger<ChannelActionDispatcher>();
            realDispatcher = new ChannelActionDispatcher(plugins, dLogger, opts);
            await realDispatcher.StartAsync(CancellationToken.None);
            dispatcher = realDispatcher;
        }

        try
        {
            var pump = new EventPump(
                new[] { source }, dedupe, monitor, dispatcher,
                plugins, EmptyServiceProvider.Instance, logger);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await pump.StartAsync(cts.Token);
            await Task.Delay(400); // give pump time to process the single event (ID-22: polling improvement deferred)
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

    private sealed class StubPlugin(string name) : IActionPlugin
    {
        public string Name { get; } = name;
        public Task ExecuteAsync(EventContext ctx, SnapshotContext snapshot, CancellationToken ct) =>
            Task.CompletedTask;
    }

    private sealed class FakeSource : IEventSource
    {
        private readonly EventContext[] _events;
        public FakeSource(string name, EventContext[] events) { Name = name; _events = events; }
        public string Name { get; }

        public async IAsyncEnumerable<EventContext> ReadEventsAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var e in _events)
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
