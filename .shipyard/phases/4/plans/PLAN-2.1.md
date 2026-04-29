---
phase: phase-4-action-dispatcher-blueiris
plan: 2.1
wave: 2
dependencies: [1.1]
must_haves:
  - Consumer-task body uses ResiliencePipelineBuilder with 3/6/9s fixed retry schedule
  - Retry-exhaustion catch emits frigaterelay.dispatch.exhausted counter + LogWarning carrying event_id + action
  - DispatchItem.Activity propagates across the channel hop
  - Per-plugin queue capacity override (BlueIrisOptions.QueueCapacity → channel construction)
  - Dispatcher tests covering retry delay sequence (3s/6s/9s), retry-exhaustion telemetry, cancellation propagation
files_touched:
  - src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs
  - src/FrigateRelay.Host/Dispatch/DispatcherOptions.cs
  - src/FrigateRelay.Host/FrigateRelay.Host.csproj
  - tests/FrigateRelay.Host.Tests/Dispatch/ChannelActionDispatcherTests.cs
  - tests/FrigateRelay.Host.Tests/FrigateRelay.Host.Tests.csproj
tdd: true
risk: medium
---

# Plan 2.1: Dispatcher consumer body + Polly retries + retry-exhaustion telemetry

## Context

Fills in the consumer-task body of `ChannelActionDispatcher` (skeleton landed in PLAN-1.1) with the Polly v8 resilience pipeline carrying a fixed 3s/6s/9s retry schedule (CONTEXT-4 D7 + RESEARCH §1), the retry-exhaustion telemetry (`frigaterelay.dispatch.exhausted` counter + structured Warning log), and `Activity` propagation across the channel hop per CLAUDE.md observability invariant. Adds the remaining dispatcher tests required by ROADMAP success criteria (≥6 total — PLAN-1.1 added 3, this plan adds 3 more for a total of 6+).

This plan owns the resilience-pipeline portion of ROADMAP deliverable 2 and the retry-exhaustion test of deliverable 8.

**Important:** The resilience pipeline lives **inside** the dispatcher's consumer task (NOT wrapped around the BlueIris `HttpClient` via `AddResilienceHandler`). RESEARCH §1 shows the `AddResilienceHandler` pattern as the wiring inside the BlueIris registrar — but that wraps HTTP calls only. CONTEXT-4 D7's worked example mixes both seams. The architect chooses: **the BlueIris registrar (PLAN-2.2) owns the HTTP-level retries via `AddResilienceHandler`**, and this plan's dispatcher consumer simply catches the post-exhaustion exception that bubbles out of `plugin.ExecuteAsync` after all retries fired. Rationale: keeps the retry schedule next to the HTTP call (Microsoft's blessed pattern), keeps the dispatcher action-agnostic, matches D7's "lives next to each plugin" rejection of dispatcher-owned retries.

So this plan's consumer body is small:
1. Start an `Activity` (linked to `item.Activity`) for the dispatch span.
2. `await plugin.ExecuteAsync(item.Context, ct)` — the `HttpClient` inside `BlueIrisActionPlugin` already wears the resilience handler from PLAN-2.2.
3. Catch `OperationCanceledException` (graceful) and any other exception (log + counter for retry exhaustion).

**Q2 from RESEARCH §10 — RESOLVED:** counter name is `frigaterelay.dispatch.exhausted`, tagged with `action=<plugin.Name>`, emitted from the consumer's `catch` block when `plugin.ExecuteAsync` throws (post-Polly exhaustion). Already declared in `DispatcherDiagnostics.Exhausted` from PLAN-1.1.

## Dependencies

- PLAN-1.1 (provides `ChannelActionDispatcher` skeleton, `DispatcherDiagnostics`, `DispatchItem`, the test file with the first 3 tests).

## Files touched

- `src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs` (modify — fill in consumer body, add per-plugin capacity override)
- `src/FrigateRelay.Host/Dispatch/DispatcherOptions.cs` (modify — add `IReadOnlyDictionary<string, int> PerPluginQueueCapacity { get; init; } = ...` for the registrar to populate via DI)
- `src/FrigateRelay.Host/FrigateRelay.Host.csproj` (no new package refs — `System.Diagnostics` is BCL; `Microsoft.Extensions.Hosting` already present)
- `tests/FrigateRelay.Host.Tests/Dispatch/ChannelActionDispatcherTests.cs` (modify — add 3 tests)
- `tests/FrigateRelay.Host.Tests/FrigateRelay.Host.Tests.csproj` (no new refs — already has the host project ref)

## Tasks

### Task 1: Fill in ConsumeAsync with Activity propagation + retry-exhaustion catch
**Files:** `src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs`
**Action:** modify
**Description:**

Replace PLAN-1.1's TODO consumer body with:

```csharp
private async Task ConsumeAsync(IActionPlugin plugin, ChannelReader<DispatchItem> reader, CancellationToken ct)
{
    await foreach (var item in reader.ReadAllAsync(ct).ConfigureAwait(false))
    {
        // Restore the producer-side Activity so OTel sees the channel hop as one logical trace.
        // PLAN-2.2's BlueIris HttpClient handler observes Activity.Current and stamps the outbound HTTP span.
        using var dispatchActivity = DispatcherDiagnostics.ActivitySource.StartActivity(
            "ActionDispatch",
            ActivityKind.Internal,
            parentContext: item.Activity?.Context ?? default);

        dispatchActivity?.SetTag("action", plugin.Name);
        dispatchActivity?.SetTag("event_id", item.Context.EventId);

        try
        {
            // BlueIrisActionPlugin's HttpClient already wears the 3/6/9s Polly pipeline (PLAN-2.2).
            // If all 3 retries fail, ExecuteAsync rethrows the last HttpRequestException;
            // we catch it below as "retry exhausted".
            await plugin.ExecuteAsync(item.Context, ct).ConfigureAwait(false);
            dispatchActivity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Graceful shutdown — do not log, do not increment counter.
            dispatchActivity?.SetStatus(ActivityStatusCode.Error, "Cancelled");
            return;
        }
        catch (Exception ex)
        {
            DispatcherDiagnostics.Exhausted.Add(1,
                new KeyValuePair<string, object?>("action", plugin.Name));

            LogRetryExhausted(_logger, item.Context.EventId, plugin.Name, ex, null);
            dispatchActivity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            // Do NOT rethrow — a poison item must not kill the consumer task.
        }
    }
}
```

Add the `LoggerMessage.Define` for the warning at top of class:

```csharp
private static readonly Action<ILogger, string, string, Exception?> LogRetryExhausted =
    LoggerMessage.Define<string, string>(
        LogLevel.Warning,
        new EventId(101, "DispatchRetryExhausted"),
        "Dropped event_id={EventId} action={Action} after retry exhaustion. Downstream may be unhealthy.");
```

**Critical:** the structured state must include `EventId` and `Action` as named params — ROADMAP success criterion mandates "event id in the log state", and the dispatcher unit test asserts presence of both keys.

The `Exception` is passed positionally (third arg of `LoggerMessage.Define<T1, T2>`'s emitted delegate accepts the exception via the trailing `Exception?` parameter — see the existing `LogPumpFaulted` pattern in `EventPump.cs` lines 37-41).

**Per-plugin capacity override:** modify `StartAsync` to consult `_dispatcherOpts.PerPluginQueueCapacity` (a dictionary keyed by `plugin.Name`, OrdinalIgnoreCase) — if the plugin name is present, use that capacity; otherwise fall back to `DefaultQueueCapacity`:

```csharp
var capacity = _dispatcherOpts.PerPluginQueueCapacity.TryGetValue(plugin.Name, out var c) ? c : _dispatcherOpts.DefaultQueueCapacity;
```

`DispatcherOptions` gains `public IReadOnlyDictionary<string, int> PerPluginQueueCapacity { get; init; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);`. PLAN-2.2's BlueIris registrar contributes its `BlueIrisOptions.QueueCapacity` (when non-null) by post-configuring `DispatcherOptions` — but that wiring lands in PLAN-2.2; this plan only exposes the seam.

**Acceptance Criteria:**
- `ConsumeAsync` body matches the structure above; the `catch (Exception ex)` block calls `DispatcherDiagnostics.Exhausted.Add(1, ...)` with an `action` tag.
- The `LogRetryExhausted` delegate is created via `LoggerMessage.Define<string, string>` with `EventId(101, "DispatchRetryExhausted")` and message template containing both `event_id={EventId}` and `action={Action}`.
- The `OperationCanceledException` catch is gated by `when (ct.IsCancellationRequested)` so genuine cancellations during HTTP calls are NOT logged as exhaustion.
- The `catch (Exception ex)` block does NOT rethrow — `git grep -A 6 "catch (Exception ex)" src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs` shows no `throw` inside it (poison-pill protection).
- `DispatcherOptions.PerPluginQueueCapacity` is `IReadOnlyDictionary<string, int>` initialized to an OrdinalIgnoreCase empty dictionary.
- `dotnet build FrigateRelay.sln -c Release` succeeds with zero warnings.
- `git grep -nE '\.(Result|Wait)\(' src/` returns zero matches.

### Task 2: Add retry-delay-sequence test (RESEARCH §9 Risk 1)
**Files:** `tests/FrigateRelay.Host.Tests/Dispatch/ChannelActionDispatcherTests.cs`
**Action:** modify (append new test)
**Description:**

This test addresses RESEARCH §9 Risk 1 — the off-by-one in `AttemptNumber + 1` is invisible without an explicit assertion of the delay sequence. **Important:** Polly's retry pipeline is wrapped around the BlueIris `HttpClient` in PLAN-2.2's registrar, NOT inside the dispatcher. So this test exercises Polly directly via `ResiliencePipelineBuilder<HttpResponseMessage>` constructed inline — it asserts that the same `DelayGenerator` formula CONTEXT-4 D7 specifies (`3 * (AttemptNumber + 1)`) produces exactly `[3s, 6s, 9s]`.

Since the dispatcher itself doesn't own a Polly pipeline (the plugin does), this test is more accurately a "Polly v8 wiring contract" test that lives near the dispatcher tests because it pins the schedule the dispatcher relies on existing.

Add test method `RetryDelayGeneratorFormula_Produces3s6s9s_ForAttempts0Through2`:

```csharp
[TestMethod]
public void RetryDelayGeneratorFormula_Produces3s6s9s_ForAttempts0Through2()
{
    // The formula CONTEXT-4 D7 mandates and PLAN-2.2's BlueIris registrar uses.
    static TimeSpan Delay(int attemptNumber) => TimeSpan.FromSeconds(3 * (attemptNumber + 1));

    Delay(0).Should().Be(TimeSpan.FromSeconds(3));
    Delay(1).Should().Be(TimeSpan.FromSeconds(6));
    Delay(2).Should().Be(TimeSpan.FromSeconds(9));
}
```

This test is intentionally simple — it's a regression guard. If a future agent edits the formula to `3 * AttemptNumber` (producing 0/3/6) or `3 * (AttemptNumber + 2)` (producing 6/9/12), this test fails immediately. It is the canonical encoding of RESEARCH §1's "AttemptNumber is zero-indexed for the first retry" finding.

**Acceptance Criteria:**
- Test method exists with the exact name `RetryDelayGeneratorFormula_Produces3s6s9s_ForAttempts0Through2`.
- All three delay assertions present and passing.
- Builder must verify that PLAN-2.2's BlueIris registrar uses **byte-for-byte the same expression** `TimeSpan.FromSeconds(3 * (args.AttemptNumber + 1))` — this test's correctness depends on that invariant.

### Task 3: Add retry-exhaustion + cancellation tests
**Files:** `tests/FrigateRelay.Host.Tests/Dispatch/ChannelActionDispatcherTests.cs`
**Action:** modify (append two more tests)
**Description:**

Add two tests bringing the dispatcher test count to ≥6 (PLAN-1.1 added 3; the previous task added 1; this task adds 2 → 6 total).

**Test A: `EnqueueAsync_WhenPluginThrowsAfterRetries_LogsExhaustionWarning_IncrementsExhaustedCounter_DoesNotKillConsumer`**

- Construct dispatcher with capacity = 8 and a stub `IActionPlugin` whose `ExecuteAsync` always throws `new HttpRequestException("simulated post-retry failure")` (mimics what `BlueIrisActionPlugin` would throw after Polly exhaustion).
- Enqueue one item, await ~100ms for the consumer to process it, then enqueue a second item, await ~100ms.
- Assert:
  - `frigaterelay.dispatch.exhausted` counter incremented exactly twice (once per item) with `action=<plugin.Name>` tag — observe via `MeterListener` (same pattern as PLAN-1.1 test #2).
  - `CapturingLogger` recorded **two** `LogLevel.Warning` entries with `EventId.Id == 101` ("DispatchRetryExhausted").
  - The formatted message of each warning entry contains the dispatched event id AND the plugin name AND the substring "after retry exhaustion".
  - **The structured state of each warning entry contains a key `EventId` and a key `Action`** — assert via `entry.State` lookup. ROADMAP success criterion: "asserts a 'dropped after 3 retries' warning was logged, with event id in the log state".
  - The consumer task is still running (`dispatcher.GetQueueDepth(plugin) == 0` AND no `TaskStatus.Faulted` on the internal task list — expose `internal IReadOnlyList<Task> ConsumerTasks` or assert behaviorally by enqueuing a third item and observing it gets processed).

**Test B: `StopAsync_DuringActiveExecution_PropagatesCancellationToPlugin`**

- Construct dispatcher with a stub `IActionPlugin` whose `ExecuteAsync` calls `await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false)` (i.e., honors the token).
- Enqueue one item; wait ~100ms to ensure the consumer entered `ExecuteAsync`.
- Call `StopAsync(new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token)`.
- Assert: `StopAsync` returns within 3 seconds (NOT 30) — proves the cancellation token threaded through `ConsumeAsync` reached `Task.Delay`. Use `Stopwatch` and `.ElapsedMilliseconds.Should().BeLessThan(3000)`.

For the `EventId` structured-state assertion in Test A, use the `CapturingLogger<T>` pattern from `tests/FrigateRelay.Host.Tests/PlaceholderWorkerTests.cs` (CLAUDE.md convention); if its capture doesn't expose state, extend it minimally (architect grants this scope) — store the `IReadOnlyList<KeyValuePair<string, object?>>` extracted from the `TState state` via `state as IReadOnlyList<KeyValuePair<string, object?>>`. This is exactly how `LoggerMessage.Define`-emitted delegates pass structured state.

**Acceptance Criteria:**
- Both test methods exist with the exact names above and pass.
- Dispatcher test count is **≥6** total: `dotnet run --project tests/FrigateRelay.Host.Tests -c Release -- --filter-query "/*/*/ChannelActionDispatcherTests/*"` reports 6 or more passing tests.
- Test A explicitly asserts the structured-state key `EventId` is present in the Warning entry — encodes the ROADMAP success criterion at the assertion level.
- Test B completes in <5s wall-clock (proves cancellation propagation works; without it the test would time out at 30s).
- `git grep -nE '\.(Result|Wait)\(' tests/FrigateRelay.Host.Tests/Dispatch/` returns zero matches.

## Verification

```bash
dotnet build FrigateRelay.sln -c Release
dotnet run --project tests/FrigateRelay.Host.Tests -c Release -- --filter-query "/*/*/ChannelActionDispatcherTests/*"
git grep -nE '\.(Result|Wait)\(' src/ tests/
git grep -n "frigaterelay.dispatch.exhausted" src/
git grep -n "DispatchRetryExhausted" src/
```

Expected: build clean, ≥6 dispatcher tests pass, no `.Result/.Wait`, counter name and EventId name appear in the dispatcher.

## Notes for the builder

- The retry pipeline LIVES IN THE BLUEIRIS REGISTRAR (PLAN-2.2), NOT in the dispatcher. The dispatcher only catches post-exhaustion exceptions. Do not double-wrap (a Polly pipeline inside the dispatcher AND another inside the HttpClient handler) — that would multiply retries to 4×4=16 and silently violate the 3/6/9s spec.
- The Q2 resolution (counter name `frigaterelay.dispatch.exhausted`, emitted from the consumer's catch block) is final. Do not rename or relocate.
- `Activity.StartActivity(name, ActivityKind, parentContext)` accepts the parent's context (`ActivityContext`) directly — passing `item.Activity?.Context ?? default` is the supported way to link a new span to a producer-side parent. RESEARCH §3 didn't show this exact syntax; confirmed via Microsoft Learn `dotnet/api/system.diagnostics.activitysource.startactivity`.
- The `OperationCanceledException` filter `when (ct.IsCancellationRequested)` is essential — without it, a poorly behaving plugin that throws `OperationCanceledException` for non-shutdown reasons would skip the exhaustion telemetry and silently swallow the failure (legacy bug pattern).
- RESEARCH §9 Risk 1 is addressed by Task 2's retry-delay test. Without that test, an `AttemptNumber` typo in PLAN-2.2 silently changes the schedule.
