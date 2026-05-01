using System.Runtime.CompilerServices;
using FluentAssertions;
using FrigateRelay.Abstractions;
using FrigateRelay.Host;
using FrigateRelay.Host.Configuration;
using FrigateRelay.Host.Dispatch;
using FrigateRelay.Host.Matching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrigateRelay.Host.Tests;

[TestClass]
public sealed class EventPumpTests
{
    [TestMethod]
    public async Task ExecuteAsync_SingleMatch_LogsOneMatchedEvent()
    {
        var logger = new CapturingLogger();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var dedupe = new DedupeCache(cache);
        var subs = new HostSubscriptionsOptions
        {
            Subscriptions = new[]
            {
                new SubscriptionOptions
                {
                    Name = "front_person",
                    Camera = "front_door",
                    Label = "person",
                    CooldownSeconds = 30,
                },
            },
        };
        var monitor = new StaticMonitor<HostSubscriptionsOptions>(subs);

        var context = new EventContext
        {
            EventId = "e1",
            Camera = "front_door",
            Label = "person",
            Zones = new[] { "driveway" },
            StartedAt = DateTimeOffset.UtcNow,
            RawPayload = "{}",
            SnapshotFetcher = _ => ValueTask.FromResult<byte[]?>(null),
        };
        var source = new FakeSource("FrigateMqtt", new[] { context });

        var pump = new EventPump(new IEventSource[] { source }, dedupe, monitor, NoOpDispatcher.Instance, Array.Empty<IActionPlugin>(), EmptyServiceProvider.Instance, logger);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        // StartAsync invokes ExecuteAsync; awaiting StopAsync then awaits the running task.
        await pump.StartAsync(cts.Token);
        await Task.Delay(100); // let the one-item stream flush
        await cts.CancelAsync();
        await pump.StopAsync(CancellationToken.None);

        var matchedEntries = logger.Entries
            .Where(e => e.Level == LogLevel.Information && e.Message.Contains("Matched event"))
            .ToList();
        matchedEntries.Should().HaveCount(1, "one subscription matched the one event; one log line expected");

        // Regression for #22: the {EventId} placeholder used to collide with Serilog's
        // bridge-enriched property derived from the LoggerMessage.Define EventId argument
        // ("MatchedEvent"). Renamed to {FrigateEventId} so the call-site value renders.
        // Operators / log dashboards will now see the actual Frigate event id.
        matchedEntries[0].Message.Should().Contain("event_id=e1",
            "the rendered message must surface the call-site EventId value, not the " +
            "LoggerMessage.Define EventId struct that the placeholder used to collide with");
    }

    [TestMethod]
    public async Task ExecuteAsync_DedupeSuppressesSecondMatch()
    {
        var logger = new CapturingLogger();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var dedupe = new DedupeCache(cache);
        var subs = new HostSubscriptionsOptions
        {
            Subscriptions = new[]
            {
                new SubscriptionOptions { Name = "front_person", Camera = "cam1", Label = "person", CooldownSeconds = 60 },
            },
        };
        var monitor = new StaticMonitor<HostSubscriptionsOptions>(subs);

        EventContext Make(string id) => new()
        {
            EventId = id,
            Camera = "cam1",
            Label = "person",
            Zones = Array.Empty<string>(),
            StartedAt = DateTimeOffset.UtcNow,
            RawPayload = "{}",
            SnapshotFetcher = _ => ValueTask.FromResult<byte[]?>(null),
        };
        var source = new FakeSource("FrigateMqtt", new[] { Make("e1"), Make("e2") });
        var pump = new EventPump(new IEventSource[] { source }, dedupe, monitor, NoOpDispatcher.Instance, Array.Empty<IActionPlugin>(), EmptyServiceProvider.Instance, logger);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await pump.StartAsync(cts.Token);
        await Task.Delay(100);
        await cts.CancelAsync();
        await pump.StopAsync(CancellationToken.None);

        logger.Entries
            .Where(e => e.Level == LogLevel.Information && e.Message.Contains("Matched event"))
            .Should().HaveCount(1,
                "two events for the same (sub, camera, label) inside the cooldown window — only one should fire");
    }

    private sealed class NoOpDispatcher : IActionDispatcher
    {
        public static readonly NoOpDispatcher Instance = new();
        public ValueTask EnqueueAsync(EventContext ctx, IActionPlugin action, IReadOnlyList<IValidationPlugin> validators, string subscription, string? perActionSnapshotProvider, string? subscriptionDefaultSnapshotProvider, CancellationToken ct) => ValueTask.CompletedTask;
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static readonly EmptyServiceProvider Instance = new();
        public object? GetService(Type serviceType) => null;
    }

    private sealed class FakeSource : IEventSource
    {
        private readonly EventContext[] _events;

        public FakeSource(string name, EventContext[] events)
        {
            Name = name;
            _events = events;
        }

        public string Name { get; }

        public async IAsyncEnumerable<EventContext> ReadEventsAsync([EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var e in _events)
            {
                ct.ThrowIfCancellationRequested();
                yield return e;
            }
            // Block so the pump keeps iterating until cancelled; otherwise it exits the
            // `await foreach` immediately and Task.WhenAll completes before the logger sees anything.
            await Task.Delay(Timeout.Infinite, ct);
        }
    }

    private sealed class StaticMonitor<T> : IOptionsMonitor<T>
    {
        public StaticMonitor(T value) => CurrentValue = value;
        public T CurrentValue { get; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class CapturingLogger : ILogger<EventPump>
    {
        public List<LogEntry> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add(new LogEntry(level, id, formatter(state, exception)));

        public sealed record LogEntry(LogLevel Level, EventId Id, string Message);
    }
}
