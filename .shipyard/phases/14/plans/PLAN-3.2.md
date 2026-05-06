---
phase: 14-v1.2-inference-engines-parallel-validation
plan: 3.2
wave: 3
dependencies: [3.1]
must_haves:
  - Branch in ChannelActionDispatcher.cs:200-246 selecting parallel vs sequential based on item.ParallelValidators
  - Task.WhenAll path implements strict-AND with no first-reject short-circuit (CONTEXT-14 D6)
  - Per-validator validators.rejected counter still emitted in parallel mode (CONTEXT-14 OQ-4)
  - At least 6 new unit tests in tests/FrigateRelay.Host.Tests
  - Counter inventory unchanged — no new counters added (CONTEXT-14 OQ-4)
  - Cumulative test count rises from 262 (post-Wave 2) to ≥ 268
files_touched:
  - src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs
  - tests/FrigateRelay.Host.Tests/ChannelActionDispatcherParallelValidatorsTests.cs
tdd: true
risk: medium
---

# Plan 3.2: Parallel-validators branch in ChannelActionDispatcher + unit tests (PR #23)

## Context

PLAN-3.1 plumbed the `ParallelValidators` field onto `ActionEntry` and `DispatchItem`. This plan implements the dispatcher branch that consumes it. The change is localized to `src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs:200-246` (RESEARCH §2.1) — the `if (item.Validators.Count > 0)` block that today runs validators sequentially with `goto NextItem` short-circuit.

Per CONTEXT-14 D5/D6 + OQ-4:
- When `item.ParallelValidators == false`: existing sequential `foreach` body unchanged.
- When `item.ParallelValidators == true`: validators run via `Task.WhenAll`; **strict-AND aggregation** — ALL must pass for the action to fire; first-reject does NOT cancel other in-flight validators (CONTEXT-14 D6 explicit); each rejecting validator emits its own `validators.rejected` counter (OQ-4 — no aggregate counter, keeps the v1.1 counter-inventory drift test clean).
- Per-validator `Timeout` is enforced by each validator's own `HttpClient.Timeout` (RESEARCH §2.3) — the dispatcher does NOT layer a per-task `CancellationTokenSource`. Validator-internal `catch (TaskCanceledException)` returns its own `Verdict.Fail("validator_timeout")` per its `OnError` config; that verdict is one of the verdicts that `Task.WhenAll` collects.
- Host-shutdown propagation: `OperationCanceledException when ct.IsCancellationRequested` from any validator MUST bubble out of `Task.WhenAll` (which rethrows the first exception by default) AND propagate up to `ChannelActionDispatcher.ConsumeAsync`'s existing outer `catch (OperationCanceledException) when (ct.IsCancellationRequested)` block at `ChannelActionDispatcher.cs:259`, which exits the consumer loop gracefully without logging or incrementing a failure counter (CONTEXT-9 D4 / ID-6 fix). Net effect: the validator's exception unwinds `Task.WhenAll`, then unwinds the dispatch's outer `try`, then is silently swallowed by the existing graceful-shutdown handler — no exception escapes `ConsumeAsync` but the consume loop returns.

Test count target: 262 (post-Wave 2) → **268 (+6 new)** for the parallel-mode unit tests.

## Dependencies

- **PLAN-3.1** — `DispatchItem.ParallelValidators` field must exist and be propagated by `EventPump`.

## Tasks

### Task 1: Implement parallel branch in ChannelActionDispatcher.cs

**Files:** `src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs`

**Action:** modify

**Description:**

Refactor the `if (item.Validators.Count > 0)` block at lines 200-246 (RESEARCH §2.1) to branch on `item.ParallelValidators`:

```csharp
if (item.Validators.Count > 0)
{
    var preResolved = await initial.ResolveAsync(item.Context, ct).ConfigureAwait(false);
    shared = new SnapshotContext(preResolved);

    bool anyRejected;
    if (item.ParallelValidators)
        anyRejected = await RunValidatorsInParallelAsync(item, plugin, shared, ct).ConfigureAwait(false);
    else
        anyRejected = await RunValidatorsSequentiallyAsync(item, plugin, shared, ct).ConfigureAwait(false);

    if (anyRejected)
    {
        actionActivity?.SetTag("outcome", "validator_rejected");
        actionActivity?.SetStatus(ActivityStatusCode.Ok, "ValidatorRejected");
        goto NextItem;
    }
}
else
{
    shared = initial;
}
```

Then extract two private async methods on the class:

**`RunValidatorsSequentiallyAsync(DispatchItem item, IActionPlugin plugin, SnapshotContext shared, CancellationToken ct)`** — exact body of the existing `foreach (var validator in item.Validators)` loop at lines 209-245, minus the `goto NextItem` (replace with `return true;` to signal "rejected"). The method returns `false` if all validators pass, `true` if any rejected. Activity creation, tag-setting, counter calls (`IncrementValidatorsPassed` / `IncrementValidatorsRejected`), and `LogValidatorRejected` calls all stay byte-identical — sequential mode is **unchanged behavior** (back-compat invariant).

**`RunValidatorsInParallelAsync(DispatchItem item, IActionPlugin plugin, SnapshotContext shared, CancellationToken ct)`** — new implementation:
1. Build a list of `Task<(IValidationPlugin validator, Verdict verdict)>` by calling each validator concurrently. Each task wraps the `validator.ValidateAsync(...)` call together with the same activity-creation + tag-setting that the sequential path does. The activity span MUST still be `validator.<name>.check` per validator (RESEARCH §2.1 lines 211-218 — preserve span name + tags so traces remain pivotable per CONTEXT-13's tag matrix).
2. `await Task.WhenAll(tasks)` — `OperationCanceledException` from host shutdown propagates naturally (Task.WhenAll rethrows the first exception; the consumer loop's outer catch handles it).
3. After all tasks complete: iterate `(validator, verdict)` pairs. For each, increment the appropriate counter (`IncrementValidatorsPassed` or `IncrementValidatorsRejected` — same calls as sequential mode, preserves OQ-4 per-validator emission with full tag matrix). For rejecting validators, also emit `LogValidatorRejected` with the same arguments as the sequential path (so dashboards + log-based alerting still work identically).
4. Return `true` if ANY verdict was a reject (`!verdict.Passed`), else `false`. **No first-reject short-circuit** — every validator runs to completion (CONTEXT-14 D6 explicit).

Helper for building one validator-task (private method):
```csharp
private async Task<(IValidationPlugin Validator, Verdict Verdict)> RunOneValidatorAsync(
    IValidationPlugin validator, DispatchItem item, IActionPlugin plugin, SnapshotContext shared, CancellationToken ct)
{
    var validatorSpanName = $"validator.{validator.Name.ToLowerInvariant()}.check";
    using var vActivity = DispatcherDiagnostics.ActivitySource.StartActivity(
        validatorSpanName, ActivityKind.Internal);
    vActivity?.SetTag("event.id", item.Context.EventId);
    vActivity?.SetTag("validator", validator.Name);
    vActivity?.SetTag("action", plugin.Name);
    vActivity?.SetTag("subscription", item.Subscription);

    var verdict = await validator.ValidateAsync(item.Context, shared, ct).ConfigureAwait(false);
    vActivity?.SetTag("verdict", verdict.Passed ? "pass" : "fail");
    if (!verdict.Passed) vActivity?.SetTag("reason", verdict.Reason);
    return (validator, verdict);
}
```

This helper is also reusable from the sequential path (the activity bookkeeping is identical). Optional: refactor the sequential path to call this same helper — keeps validator-span construction in one place. If the refactor is too invasive, leave the sequential path's existing inline code untouched and accept a small duplication; the back-compat invariant is more important than DRY.

**No new counters added.** `DispatcherDiagnostics.IncrementValidatorsPassed` / `IncrementValidatorsRejected` are called from both branches with identical arguments; counter inventory matches v1.1 baseline (CONTEXT-14 OQ-4 + counter-inventory-drift test continues to pass).

**Acceptance Criteria:**
- `dotnet build FrigateRelay.sln -c Release` zero warnings.
- `git grep -n 'item.ParallelValidators' src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs` returns at least 1 match.
- `git grep -n 'Task.WhenAll' src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs` returns at least 1 match.
- All existing tests pass — sequential-mode behavior unchanged: `dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build -- --filter "ChannelActionDispatcher"` exits 0.
- Counter inventory is unchanged — `tests/FrigateRelay.Host.Tests/CounterInventoryDriftTests.cs` (or whatever the Phase 13 inventory test is named — locate via `grep -rln Inventory tests/FrigateRelay.Host.Tests/`) continues to pass without modification.
- The forbidden tag `event_id` is NOT added on counter increments in the parallel branch (only on activity spans, which is allowed): `git grep -n '"event_id"' src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs` returns matches only on activity-span `SetTag` calls (existing pattern), not on counter calls.
- No `.Result` / `.Wait()` introduced: `git grep -nE '\.(Result|Wait)\(' src/` empty.

---

### Task 2: Write 6 ChannelActionDispatcher parallel-validators unit tests

**Files:** `tests/FrigateRelay.Host.Tests/ChannelActionDispatcherParallelValidatorsTests.cs` (new)

**Action:** create (TDD — write tests first, then verify against Task 1's implementation)

**Description:**

New test class in the existing `tests/FrigateRelay.Host.Tests/` project (no new csproj needed). Use NSubstitute to mock `IValidationPlugin` instances (the dispatcher's external dependency); use the existing test patterns from `ChannelActionDispatcherTests.cs` (find via `grep -l ChannelActionDispatcher tests/FrigateRelay.Host.Tests/`) for harness setup.

The 6 tests:

1. `Dispatch_ParallelValidatorsFalse_RunsValidatorsSequentially` — back-compat test. Two validator mocks; record their `ValidateAsync` invocation order via `ICallInfo`-based callbacks. Run with `item.ParallelValidators = false`. Assert both validators called; assert validator-2 NOT called when validator-1 returns `Verdict.Fail` (short-circuit invariant for sequential mode). Verifies the sequential branch is intact.

2. `Dispatch_ParallelValidatorsTrue_AllValidatorsPass_ActionExecutes` — happy path. Two validator mocks both return `Verdict.Pass()`. Run with `ParallelValidators = true`. Assert action plugin's `ExecuteAsync` was called once. Assert both validators ran (use NSubstitute `.Received(1).ValidateAsync(...)` on both).

3. `Dispatch_ParallelValidatorsTrue_AnyValidatorRejects_ActionDoesNotExecute_AllValidatorsRan` — strict-AND test. Three validators: V1 passes, V2 rejects, V3 passes. Run with `ParallelValidators = true`. Assert action plugin `ExecuteAsync` was NOT called. Assert all three validators' `ValidateAsync` was called (`Received(1)` on each) — proves no first-reject short-circuit (CONTEXT-14 D6).

4. `Dispatch_ParallelValidatorsTrue_AllRejectingValidatorsEmitTheirOwnRejectedCounter` — counter test. Two validators both return `Verdict.Fail("nope")`. Run with `ParallelValidators = true`. Use a `MeterListener`-based capture (mirror `tests/FrigateRelay.Host.Tests/CounterTests.cs` or whatever the Phase 13 counter-test pattern is — find via `grep -rln MeterListener tests/FrigateRelay.Host.Tests/`). Assert exactly 2 increments to `validators.rejected`, one tagged `validator=v1`, one tagged `validator=v2`. Verifies CONTEXT-14 OQ-4: per-validator emission preserved.

5. `Dispatch_ParallelValidatorsTrue_OneValidatorTimesOutFailClosed_ActionDoesNotExecute` — timeout test. Use a real `Doods2Validator`-style mock (or a fake `IValidationPlugin` that internally awaits `Task.Delay` past its `Timeout` and returns `Verdict.Fail("validator_timeout")` per its own catch-block). Run with `ParallelValidators = true` alongside one immediately-passing validator. Assert action `ExecuteAsync` NOT called; assert the immediately-passing validator's pass counter incremented; assert the timing-out validator's reject counter incremented with reason hint `validator_timeout`. Validates RESEARCH §2.3: validator's own `Timeout` works in parallel mode without dispatcher-level CTS plumbing.

6. `Dispatch_ParallelValidatorsTrue_HostCancellation_UnwindGracefully` — cancellation test. Use a fake `IValidationPlugin` whose `ValidateAsync` throws `OperationCanceledException` when its `ct` is signalled. The test cancels the dispatcher's CT mid-flight (use a `CancellationTokenSource` whose `Cancel()` runs after the `Task.WhenAll` is in flight — the simplest reliable shape is to give the validator a `Task.Delay` that completes only when `ct` fires). Assert the dispatch loop unwinds gracefully: the validator's `OperationCanceledException` propagates out of `Task.WhenAll`, propagates up the stack, and is caught by `ChannelActionDispatcher.ConsumeAsync`'s existing outer `catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }` (line 259) — so no exception escapes `ConsumeAsync` *because the existing graceful-shutdown handler catches it* (consistent with the contract above). The action plugin's `ExecuteAsync` must NOT be called.

XML doc-comment per test summarizing the case. Use `[TestMethod]` attributes; test names use underscores (CLAUDE.md "Conventions" — `Method_Condition_Expected`).

**Acceptance Criteria:**
- All 6 tests pass: `dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build -- --filter "ParallelValidators"` exits 0 and reports 6 tests.
- `dotnet build FrigateRelay.sln -c Release` zero warnings.
- Cumulative test count ≥ 268 (262 post-Wave 2 + 6 new): `bash .github/scripts/run-tests.sh --skip-integration` confirms.
- Test #3 explicitly asserts ALL validators ran when one rejected (the no-short-circuit invariant from CONTEXT-14 D6).
- Test #4 explicitly asserts per-validator `validators.rejected` counter emission (CONTEXT-14 OQ-4).
- All existing host tests still pass: `dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build` exits 0 (sequential mode behavior unchanged).
- Tests use the shared `CapturingLogger<T>` from `FrigateRelay.TestHelpers` for log assertions, NOT NSubstitute on `ILogger<T>` (CLAUDE.md "Conventions" — log-assertion helper precedent).

## Verification

```bash
# Build clean
dotnet build FrigateRelay.sln -c Release

# All host tests pass — including the new parallel-mode suite + the back-compat sequential tests
dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build

# Test count gate — at least 268 total (262 post-Wave 2 + 6 new)
TOTAL=$(bash .github/scripts/run-tests.sh --skip-integration 2>&1 | grep -E '^total:' | awk '{ sum += $2 } END { print sum }')
[ "$TOTAL" -ge 268 ] || { echo "test count regression: $TOTAL < 268"; exit 1; }

# Counter inventory unchanged — Phase 13's drift test still passes
dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build -- --filter "CounterInventory"

# Parallel branch lives in the dispatcher
git grep -n 'item.ParallelValidators' src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs   # must have at least 1 match
git grep -n 'Task.WhenAll' src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs              # must have at least 1 match

# Architectural invariants
git grep -nE 'App\.Metrics|OpenTracing|Jaeger\.' src/                # must be empty
git grep -nE '\.(Result|Wait)\(' src/                                 # must be empty
git grep -n 'ServicePointManager' src/                                # must be empty

# Tests use the shared CapturingLogger
git grep -n 'Substitute.For<ILogger' tests/FrigateRelay.Host.Tests/ChannelActionDispatcherParallelValidatorsTests.cs    # must be empty
```
