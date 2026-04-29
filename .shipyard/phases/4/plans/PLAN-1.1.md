---
phase: phase-4-action-dispatcher-blueiris
plan: 1.1
wave: 1
dependencies: []
must_haves:
  - IActionDispatcher contract with validators parameter (D4)
  - DispatchItem readonly record struct carrying ctx + plugin + validators + Activity
  - ChannelActionDispatcher skeleton (IHostedService, channel-per-plugin, itemDropped callback wired, frigaterelay.dispatch.drops counter, graceful StopAsync)
  - Dispatcher unit tests covering channel construction, drop-callback firing, graceful shutdown
files_touched:
  - src/FrigateRelay.Host/Dispatch/IActionDispatcher.cs
  - src/FrigateRelay.Host/Dispatch/DispatchItem.cs
  - src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs
  - src/FrigateRelay.Host/Dispatch/DispatcherDiagnostics.cs
  - src/FrigateRelay.Host/FrigateRelay.Host.csproj
  - tests/FrigateRelay.Host.Tests/Dispatch/ChannelActionDispatcherTests.cs
tdd: true
risk: medium
---

# Plan 1.1: IActionDispatcher contract + ChannelActionDispatcher skeleton

## Context

Establishes the long-lived `IActionDispatcher` seam (CONTEXT-4 D4) and the dispatcher skeleton with channel construction, the built-in `itemDropped` callback (RESEARCH §3 — corrects CONTEXT-4's wrapper-based assumption), the graceful-shutdown idiom (RESEARCH §3 + Risk 3), and the `frigaterelay.dispatch.drops` counter (D6). The actual consumer-task body (Polly retries + retry-exhaustion telemetry) is deferred to PLAN-2.1 so this wave can land with stable contracts that PLAN-1.2 (BlueIris plugin) can compile against.

This plan owns ROADMAP deliverables 1, 2 (skeleton only — no Polly yet), and 3 from the Phase 4 deliverable list, plus the first three of the ≥6 dispatcher unit tests required by ROADMAP success criteria.

**Architect decisions resolved inline:**
- `IActionDispatcher` lives in `src/FrigateRelay.Host/Dispatch/` (NOT in `Abstractions`) — Host-internal seam consumed only by `EventPump`. Plugins never see this interface; they are enumerated via `IEnumerable<IActionPlugin>`.
- `DispatchItem` is `readonly record struct DispatchItem(EventContext Context, IActionPlugin Plugin, IReadOnlyList<IValidationPlugin> Validators, Activity? Activity)` — cheap to enqueue, no GC pressure.
- `ChannelActionDispatcher` implements `IHostedService` directly (NOT `BackgroundService`) per RESEARCH §9 Risk 3 — channels constructed in `StartAsync`, drained in `StopAsync` after `Writer.Complete()`.

## Dependencies

None (Wave 1).

## Files touched

- `src/FrigateRelay.Host/Dispatch/IActionDispatcher.cs` (create)
- `src/FrigateRelay.Host/Dispatch/DispatchItem.cs` (create)
- `src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs` (create — skeleton; consumer body is a TODO that drains then no-ops in this plan)
- `src/FrigateRelay.Host/Dispatch/DispatcherDiagnostics.cs` (create — `static readonly Meter Meter = new("FrigateRelay")` + `Counter<long>` for drops + exhausted, plus `static readonly ActivitySource ActivitySource = new("FrigateRelay")`)
- `src/FrigateRelay.Host/FrigateRelay.Host.csproj` (modify — no new package refs needed; `System.Threading.Channels` is in BCL)
- `tests/FrigateRelay.Host.Tests/Dispatch/ChannelActionDispatcherTests.cs` (create)

## Tasks

### Task 1: Define IActionDispatcher + DispatchItem + DispatcherDiagnostics
**Files:** `src/FrigateRelay.Host/Dispatch/IActionDispatcher.cs`, `src/FrigateRelay.Host/Dispatch/DispatchItem.cs`, `src/FrigateRelay.Host/Dispatch/DispatcherDiagnostics.cs`
**Action:** create
**Description:**

Create the `IActionDispatcher` interface in `namespace FrigateRelay.Host.Dispatch`:

```csharp
public interface IActionDispatcher
{
    ValueTask EnqueueAsync(
        EventContext ctx,
        IActionPlugin action,
        IReadOnlyList<IValidationPlugin> validators,
        CancellationToken ct);
}
```

The validators parameter is empty in Phase 4 (D4) — Phase 7 populates it. Document this in XML doc comments and reference D4.

Create `DispatchItem`:

```csharp
internal readonly record struct DispatchItem(
    EventContext Context,
    IActionPlugin Plugin,
    IReadOnlyList<IValidationPlugin> Validators,
    Activity? Activity);
```

Mark `internal` — only `ChannelActionDispatcher` and tests (via `<InternalsVisibleTo>`) consume it.

Create `DispatcherDiagnostics` (static class) holding the shared `Meter` named `"FrigateRelay"` and `ActivitySource` named `"FrigateRelay"` per CLAUDE.md observability invariant. Pre-create the two `Counter<long>` instances:
- `frigaterelay.dispatch.drops` — increments on queue-overflow eviction
- `frigaterelay.dispatch.exhausted` — increments on Polly retry exhaustion (PLAN-2.1 emits; declared here for shared ownership)

Both counters are tagged with `action` (the plugin name) at emit time.

**Acceptance Criteria:**
- `src/FrigateRelay.Host/Dispatch/IActionDispatcher.cs` defines `public interface IActionDispatcher` with the four-parameter `EnqueueAsync` returning `ValueTask`.
- XML doc on `validators` parameter explicitly states "Phase 4 always passes Array.Empty<IValidationPlugin>(); Phase 7 populates per-action validators (CONTEXT-4 D4)."
- `DispatchItem` is `internal readonly record struct` with the four named members in the order specified.
- `DispatcherDiagnostics` exposes `internal static readonly Meter Meter = new("FrigateRelay")`, `internal static readonly ActivitySource ActivitySource = new("FrigateRelay")`, and two `Counter<long>` fields (`Drops`, `Exhausted`) created via `Meter.CreateCounter<long>("frigaterelay.dispatch.drops")` and `"frigaterelay.dispatch.exhausted"`.
- `dotnet build FrigateRelay.sln -c Release` succeeds with zero warnings.

### Task 2: Implement ChannelActionDispatcher skeleton
**Files:** `src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs`
**Action:** create
**Description:**

Create `internal sealed class ChannelActionDispatcher : IActionDispatcher, IHostedService`. Uses constructor injection of `IEnumerable<IActionPlugin>`, `ILogger<ChannelActionDispatcher>`, and `IOptions<DispatcherOptions>` (declare a small `DispatcherOptions` record with `int DefaultQueueCapacity { get; init; } = 256;` in the same file or a sibling file — keeps capacity overrideable for tests; no config binding required for v1, but `Bind("Dispatcher")` in PLAN-3.1 if convenient).

Internal state:
- `Dictionary<IActionPlugin, Channel<DispatchItem>>` mapping plugin instance → bounded channel.
- `Dictionary<string, IActionPlugin>` mapping `plugin.Name` (case-insensitive ordinal) → plugin instance — used by EventPump (PLAN-3.1) to resolve action names.
- `List<Task>` for the consumer tasks.
- `CancellationTokenSource _stoppingCts` for shutdown coordination.

Per RESEARCH §3, channel construction MUST use the `Channel.CreateBounded<DispatchItem>(BoundedChannelOptions, Action<DispatchItem>? itemDropped)` overload. The `itemDropped` callback closes over the plugin name + logger + meter:

```csharp
itemDropped: evicted =>
{
    DispatcherDiagnostics.Drops.Add(1, new KeyValuePair<string, object?>("action", plugin.Name));
    LogDropped(_logger, evicted.Context.EventId, plugin.Name, capacity, null);
}
```

Use `LoggerMessage.Define` for the warning log (mirrors `EventPump.cs` style):
```
"Dropped event_id={EventId} action={Action} queue_full capacity={Capacity}. Downstream may be unhealthy."
```

Channel options: `FullMode = DropOldest`, `SingleWriter = true`, `SingleReader = false`, `AllowSynchronousContinuations = false`.

`StartAsync`:
1. For each `IActionPlugin` in `_plugins`, construct one bounded channel (capacity from `DispatcherOptions.DefaultQueueCapacity`, default 256 — D5).
2. Start 2 consumer tasks per channel via `_consumerTasks.Add(Task.Run(() => ConsumeAsync(plugin, channel.Reader, _stoppingCts.Token)))`.
3. Return `Task.CompletedTask`.

`ConsumeAsync` (skeleton in this plan — TODO comment for the Polly body to be filled in by PLAN-2.1):
```csharp
private async Task ConsumeAsync(IActionPlugin plugin, ChannelReader<DispatchItem> reader, CancellationToken ct)
{
    await foreach (var item in reader.ReadAllAsync(ct).ConfigureAwait(false))
    {
        try
        {
            // PLAN-2.1 fills in: await plugin.ExecuteAsync(item.Context, ct) wrapped by the resilience pipeline.
            await plugin.ExecuteAsync(item.Context, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { /* graceful */ }
        catch (Exception)
        {
            // PLAN-2.1: emit frigaterelay.dispatch.exhausted + LogWarning here.
        }
    }
}
```

`StopAsync` (per RESEARCH §3 graceful shutdown):
1. For each channel, call `channel.Writer.Complete()`.
2. `await Task.WhenAll(_consumerTasks).WaitAsync(cancellationToken).ConfigureAwait(false);` — propagates the host shutdown token if drain takes too long.

`EnqueueAsync` looks up the channel by `action` (the `IActionPlugin` instance), then `await channel.Writer.WriteAsync(new DispatchItem(ctx, action, validators, Activity.Current), ct)`. Throw `InvalidOperationException` if the plugin isn't registered (defensive — EventPump's startup validation should prevent this, but the dispatcher must fail loudly if it slips through).

Expose two **internal** members for tests (gated by `<InternalsVisibleTo>` MSBuild item — already present in `FrigateRelay.Host.csproj` per CLAUDE.md conventions):
- `internal IReadOnlyDictionary<string, IActionPlugin> ActionsByName` (case-insensitive ordinal `Dictionary<string, IActionPlugin>(StringComparer.OrdinalIgnoreCase)` exposed via `IReadOnlyDictionary`).
- `internal int GetQueueDepth(IActionPlugin plugin)` returning `channel.Reader.Count` — for backpressure tests.

**Acceptance Criteria:**
- `ChannelActionDispatcher` is `internal sealed`, implements both `IActionDispatcher` and `IHostedService`.
- Constructor uses `[System.Diagnostics.CodeAnalysis.SetsRequiredMembers]` IF it has required init members (it does not — pure ctor). No required-members marker needed.
- `Channel.CreateBounded<DispatchItem>(BoundedChannelOptions, Action<DispatchItem>?)` is invoked with both arguments; `git grep -n 'CreateBounded' src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs` shows the two-arg form.
- `BoundedChannelOptions` instance has `FullMode = BoundedChannelFullMode.DropOldest`, `SingleWriter = true`, `SingleReader = false`.
- `StopAsync` calls `Writer.Complete()` on each channel and awaits `Task.WhenAll(...).WaitAsync(cancellationToken)`.
- `git grep -nE '\.(Result|Wait)\(' src/FrigateRelay.Host/Dispatch/` returns zero matches.
- `dotnet build FrigateRelay.sln -c Release` succeeds with zero warnings.

### Task 3: Write skeleton dispatcher unit tests
**Files:** `tests/FrigateRelay.Host.Tests/Dispatch/ChannelActionDispatcherTests.cs`
**Action:** create
**Description:**

Following the Phase-3 `CapturingLogger<T>` precedent (CLAUDE.md "Conventions" — NOT NSubstitute on `ILogger<T>`), create three TDD tests:

1. **`StartAsync_RegistersOneChannelPerPlugin_ExposesCaseInsensitiveLookup`** — given two stub `IActionPlugin` instances named `"BlueIris"` and `"Pushover"`, after `StartAsync` the dispatcher's internal `ActionsByName` dictionary returns the instance for both `"BlueIris"` and `"blueiris"` (case-insensitive ordinal).

2. **`EnqueueAsync_WhenChannelFull_FiresItemDroppedCallback_IncrementsDropsCounter_LogsWarning`** — construct dispatcher with capacity = 2, enqueue 3 items synchronously without consumers running (use a stub plugin whose `ExecuteAsync` blocks on a `TaskCompletionSource`). Assert: (a) `frigaterelay.dispatch.drops` counter incremented by 1 with `action=BlueIris` tag (use `MeterListener` to observe), (b) the `CapturingLogger` recorded a `LogLevel.Warning` entry whose formatted message contains `event_id=` and `queue_full`, (c) the latest item is still in the queue (drop-OLDEST semantics).

3. **`StopAsync_CompletesWriters_AwaitsConsumers_GracefulWithinToken`** — start dispatcher, enqueue one item with a stub plugin whose `ExecuteAsync` returns `Task.CompletedTask` immediately. Call `StopAsync(ct)` with a token that has a 5-second timeout. Assert: the call completes within the timeout AND `dispatcher.GetQueueDepth(plugin)` returns 0 (drained).

Test names use underscores per CLAUDE.md convention. Use FluentAssertions 6.12.2 syntax (`.Should().Be(...)`, `.Should().HaveCount(...)`). NSubstitute for the stub `IActionPlugin` instances (`Substitute.For<IActionPlugin>()` with `plugin.Name.Returns("BlueIris")`).

`MeterListener` pattern for counter observation:
```csharp
var observed = new List<KeyValuePair<string, object?>[]>();
using var listener = new MeterListener();
listener.InstrumentPublished = (instr, l) =>
{
    if (instr.Meter.Name == "FrigateRelay" && instr.Name == "frigaterelay.dispatch.drops")
        l.EnableMeasurementEvents(instr);
};
listener.SetMeasurementEventCallback<long>((instr, value, tags, state) =>
    observed.Add(tags.ToArray()));
listener.Start();
```

**Acceptance Criteria:**
- File contains exactly the three test methods named above.
- All three tests pass: `dotnet run --project tests/FrigateRelay.Host.Tests -c Release -- --filter-query "/*/*/ChannelActionDispatcherTests/*"`.
- Test #2 verifies the counter increment via `MeterListener` (not just the log) — assertion uses `observed.Should().ContainSingle(tags => tags.Any(t => t.Key == "action" && (string)t.Value! == "BlueIris"))`.
- Test #3 completes in <2s wall-clock.
- No `.Result` / `.Wait()` in the test file: `git grep -nE '\.(Result|Wait)\(' tests/FrigateRelay.Host.Tests/Dispatch/` returns zero matches.

## Verification

```bash
dotnet build FrigateRelay.sln -c Release
dotnet run --project tests/FrigateRelay.Host.Tests -c Release -- --filter-query "/*/*/ChannelActionDispatcherTests/*"
git grep -n "CreateBounded" src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs
git grep -nE '\.(Result|Wait)\(' src/ tests/
git grep -n "ServicePointManager" src/
```

Expected: build clean, all 3 tests pass, `CreateBounded` line uses the two-arg overload, `.Result/.Wait` and `ServicePointManager` greps return zero matches.

## Notes for the builder

- RESEARCH §3 is the precise reference for the `itemDropped` overload. Do NOT wrap `TryWrite` — that was the conservative approach in CONTEXT-4 that RESEARCH corrected.
- The consumer-task body is intentionally minimal in this plan (just `await plugin.ExecuteAsync(...)`). PLAN-2.1 wraps it in the Polly resilience pipeline AND adds the retry-exhaustion telemetry. Don't anticipate that work here.
- `DispatcherOptions.DefaultQueueCapacity = 256` is the global fallback; per-plugin overrides (e.g., `BlueIrisOptions.QueueCapacity`) are NOT consulted by the dispatcher in this plan — that wiring lands in PLAN-2.2 (BlueIris registrar passes `QueueCapacity` into a per-plugin override map). For now, capacity is 256 for every plugin in tests.
- `<InternalsVisibleTo>` for `FrigateRelay.Host.Tests` is already set per CLAUDE.md conventions; verify by reading `src/FrigateRelay.Host/FrigateRelay.Host.csproj` — should already contain `<InternalsVisibleTo Include="FrigateRelay.Host.Tests" />`.
- The `ActionsByName` dictionary uses `StringComparer.OrdinalIgnoreCase` — case-insensitive ordinal lookup is mandated by D2.
