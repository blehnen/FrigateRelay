# Review: Plan 3.2

## Verdict: MINOR_ISSUES

---

## Stage 1: Spec Compliance
**Verdict: PASS**

### Task 1: Implement parallel branch in ChannelActionDispatcher.cs

- Status: PASS
- Evidence: `src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs`
  - `item.ParallelValidators` branch at line 212 inside the `if (item.Validators.Count > 0)` block.
  - `Task.WhenAll` present at line 318 in `RunValidatorsInParallelAsync`.
  - `RunOneValidatorAsync` helper at line 354 (private static, correct signature).
  - Sequential branch at lines 272–299 (`RunValidatorsSequentiallyAsync`) calls the helper and preserves `goto NextItem` short-circuit logic (returns `true` on first rejection — caller does `goto NextItem`).
  - `actionActivity?.SetTag("outcome", "validator_rejected")` + `goto NextItem` at lines 229–232, shared by both branches.
  - Outer `catch (OperationCanceledException) when (ct.IsCancellationRequested)` at line 246 is unchanged — no new catch block added inside the parallel branch.
- Notes: The spec offered a choice on counter placement (inside helper vs. in callers). The builder placed `IncrementValidatorsPassed`/`IncrementValidatorsRejected` in the callers, not in `RunOneValidatorAsync`. This is correct: the helper returns `(Validator, Verdict)`; the callers decide. The spec's pseudocode for `RunOneValidatorAsync` at lines 87–101 did not include counter calls, so the callers-own-counters pattern is the intended shape.

### Task 2: 6 unit tests in ChannelActionDispatcherParallelValidatorsTests.cs

- Status: PASS (with one behavioral gap flagged in Stage 2)
- Evidence: `tests/FrigateRelay.Host.Tests/Dispatch/ChannelActionDispatcherParallelValidatorsTests.cs` — 6 `[TestMethod]`s present with correct names (underscores, `Method_Condition_Expected` per CLAUDE.md).
  - Test 1 (`Dispatch_ParallelValidatorsFalse_RunsValidatorsSequentially`): `shouldNotRun.Calls.Should().Be(0)` asserts sequential short-circuit. PASS.
  - Test 2 (`Dispatch_ParallelValidatorsTrue_AllValidatorsPass_ActionExecutes`): `plugin.Executed.Should().Be(1)`, both validators' `Calls == 1`. PASS.
  - Test 3 (`Dispatch_ParallelValidatorsTrue_AnyValidatorRejects_ActionDoesNotExecute_AllValidatorsRan`): 3 validators; all `Calls == 1` asserted. Load-bearing D6 assertion present. PASS.
  - Test 4 (`Dispatch_ParallelValidatorsTrue_AllRejectingValidatorsEmitTheirOwnRejectedCounter`): `MeterListener` capture of `frigaterelay.validators.rejected`; asserts 2 increments tagged `alpha` and `beta`, plus `action`/`subscription` tags. OQ-4 invariant verified. PASS.
  - Test 5 (`Dispatch_ParallelValidatorsTrue_OneValidatorTimesOutFailClosed_ActionDoesNotExecute`): `SlowFailValidator` swallows its own cancellation and returns `Verdict.Fail("validator_timeout")`. Asserts `plugin.Executed == 0` and per-validator counter tags. PASS.
  - Test 6 (`Dispatch_ParallelValidatorsTrue_HostCancellation_UnwindGracefully`): see Important finding below.
  - `CapturingLogger<ChannelActionDispatcher>` used throughout. `Substitute.For<ILogger` grep confirms absence per SUMMARY-3.2. PASS.
  - Test-double classes are concrete fakes (`StubValidator`, `RecordingPlugin`, `SlowFailValidator`, `CancellationAwareValidator`), not NSubstitute mocks. Correct per CLAUDE.md convention for concurrency-sensitive assertions.

---

## Stage 2: Code Quality

### Critical

None.

### Important

1. **Test 6 does not exercise the cancellation path it claims to test**
   - File: `tests/FrigateRelay.Host.Tests/Dispatch/ChannelActionDispatcherParallelValidatorsTests.cs`, lines 306–361.
   - `CancellationAwareValidator.ValidateAsync` does `await Task.Delay(30s, ct)` where `ct` is `_stoppingCts.Token` from `StartAsync`. `StopAsync` (dispatcher line 119) calls `channel.Writer.Complete()` and awaits consumer tasks — it never calls `_stoppingCts.Cancel()`. The 30-second `Task.Delay` is therefore never cancelled by the test's `StopAsync(stopCts.Token)` call; the test escapes the `StopAsync` timeout (3 seconds via `stopCts`) by catching the resulting `OperationCanceledException` and proceeding.
   - The assertion `plugin.Executed.Should().Be(0)` passes vacuously because the validator is still blocking — not because the `OperationCanceledException` propagated through `Task.WhenAll` and was caught by the outer graceful-shutdown handler at `ChannelActionDispatcher.cs:246`. The log-cleanliness assertion (`errorLogs.Should().BeEmpty()`) also passes vacuously because the consumer task is still alive when the assertion runs.
   - The spec explicitly requires: "Assert the dispatch loop unwinds gracefully: the validator's `OperationCanceledException` propagates out of `Task.WhenAll`, propagates up the stack, and is caught by `ChannelActionDispatcher.ConsumeAsync`'s existing outer `catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }` — so no exception escapes `ConsumeAsync`." None of that is actually proven.
   - Remediation: `StopAsync` should call `_stoppingCts.Cancel()` before (or alongside) completing writers so the consumer-loop CT is signalled. Alternatively, expose `_stoppingCts` for test-only override via `InternalsVisibleTo`, or add a test-oriented `CancelInternal()` helper. The test then: (a) enqueues, (b) waits for the validator to enter `ValidateAsync` (use a `SemaphoreSlim` or `ManualResetEventSlim` in `CancellationAwareValidator` to signal entry), (c) calls `_stoppingCts.Cancel()` (or `StopAsync` if it properly cancels the CTS), (d) waits for the consumer task to complete, (e) asserts `plugin.Executed == 0` and no error logs. This proves the actual propagation chain instead of timing luck.
   - Note: The production code path itself is correct — if `_stoppingCts` were cancelled, the `OperationCanceledException` from `Task.Delay(..., ct)` would propagate exactly as designed. The gap is in the test's inability to trigger the cancellation on `_stoppingCts`.

2. **`StopAsync` never cancels `_stoppingCts`** (production gap, linked to above)
   - File: `src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs`, lines 119–128.
   - `StopAsync` completes writers and awaits consumer tasks but never calls `_stoppingCts.Cancel()`. The consumer loop's `ReadAllAsync(ct)` exits when the channel drains, but any in-flight `ValidateAsync` or `ExecuteAsync` call that respects cancellation (e.g., `HttpClient`-backed calls that check `ct`) will not be interrupted on shutdown — they must time out naturally. The spec relies on the cancellation propagation chain for graceful unwind, but that chain is only reachable if the CTS is cancelled.
   - In the generic host, `IHostedService.StopAsync` receives the shutdown `CancellationToken` (for the drain window), not a signal to cancel internal work. The pattern for `BackgroundService` subclasses is to cancel the internal CTS from `StopAsync`. The dispatcher is a raw `IHostedService`, so this must be done manually.
   - Remediation: Add `await _stoppingCts.CancelAsync().ConfigureAwait(false);` (or `_stoppingCts.Cancel();`) at the top of `StopAsync`, before `channel.Writer.Complete()`. This signals all in-flight `HttpClient`/`Task.Delay` calls to abort, consistent with the `ct.IsCancellationRequested` graceful-shutdown handler at line 246.

### Suggestions

1. **`RunOneValidatorAsync` span does not propagate parent context from `item.ParentContext`**
   - File: `src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs`, lines 358–359.
   - The helper calls `DispatcherDiagnostics.ActivitySource.StartActivity(validatorSpanName, ActivityKind.Internal)` without passing `parentContext`. The action-level span (`action.<name>.execute`) correctly passes `parentContext: item.ParentContext` (line 179). Validator spans created from `StartActivity(name, kind)` will implicitly parent to the current ambient activity — which is the action span if the helper is called inside the action span's `using` block. This is correct for the sequential path (called while the `using var actionActivity` is in scope). For the parallel path, each `RunOneValidatorAsync` task also executes within the same ambient activity context because `Task.WhenAll` does not hop execution context — ambient `Activity.Current` flows into child tasks via `ExecutionContext` capture. This is incidentally correct, but fragile: if the implementation ever changes to use `Task.Run(...)` instead of `Select(v => RunOneValidatorAsync(...))`, the ambient context would need explicit propagation. Low-priority, no action required in this plan. Documenting for PLAN-3.3 awareness.

2. **Test timing uses raw `Task.Delay` waits (200–500 ms) rather than deterministic signaling**
   - File: `tests/FrigateRelay.Host.Tests/Dispatch/ChannelActionDispatcherParallelValidatorsTests.cs`, lines 53, 94, 135, 193, 271.
   - Tests 1–4 use `await Task.Delay(200–300)` to let the background consumer process the enqueued item. This is a polling pattern; on a loaded CI host (e.g., matrix job on `ubuntu-latest` with resource contention), 200 ms may not be enough. Phase 13's integration tests use `MosquittoFixture` with proper async completion signals. The unit tests here would be more robust with a `RecordingPlugin` that exposes a `Task ExecutedSignal` (a `TaskCompletionSource`) that tests `await` with a 5-second timeout instead of a fixed `Task.Delay`. This is a pre-existing pattern in the Phase 9 dispatcher tests — worth backfilling but not a blocker for this plan.

---

## Positive

- **`RunOneValidatorAsync` extraction is clean.** Span construction, tag-setting, and verdict tagging are in one place; both paths call it. Activity name `validator.<name>.check` matches the spec exactly (line 357).
- **Parallel strict-AND aggregation (`verdicts.Any(v => !v.Verdict.Passed)`) is correct.** No short-circuit in the post-`WhenAll` iteration — the loop collects ALL rejections and emits ALL per-validator counters and logs before returning. `anyRejected` is set progressively, not early-returned.
- **No new counters added.** `DispatcherDiagnostics` is unchanged; counter inventory drift-test stays clean (OQ-4 invariant met).
- **Counter calls are in the correct callers.** `IncrementValidatorsPassed`/`IncrementValidatorsRejected` are called from `RunValidatorsSequentiallyAsync` and from `RunValidatorsInParallelAsync`'s post-aggregate loop — NOT from `RunOneValidatorAsync`. This matches the spec's pseudocode and correctly separates the per-validator span lifecycle (helper's responsibility) from the counter lifecycle (caller's responsibility).
- **No `.Result`/`.Wait()` introduced.** All async paths use `await` + `ConfigureAwait(false)` consistently.
- **Test 4 MeterListener pattern is correct.** `meterListener.EnableMeasurementEvents(instrument)` + `SetMeasurementEventCallback<long>` matches the Phase 13 observability test pattern. The `capturedTags.Should().HaveCount(2)` assertion is the right shape for OQ-4.
- **Class-based fakes, not NSubstitute, for concurrency tests.** `Interlocked.Increment` on call counters avoids torn reads under parallel scheduling — correct choice for Tests 2, 3.
- **Test 3 three-validator shape.** Using three validators (not two) makes the "all ran despite one rejection" assertion more convincing — both the validator before and after the rejector are independently asserted.

---

## Stage 1 (Correctness) check results

All 7 must-haves from the spec header verified:
- Branch on `item.ParallelValidators`: PASS (line 212).
- `Task.WhenAll` strict-AND, no first-reject short-circuit: PASS (lines 314–318, 320–342).
- Per-validator `validators.rejected` counter in parallel mode: PASS (lines 329, `IncrementValidatorsRejected`).
- 6 new unit tests: PASS (6 `[TestMethod]`s in `ChannelActionDispatcherParallelValidatorsTests.cs`).
- Counter inventory unchanged: PASS (`DispatcherDiagnostics.cs` has no new counters).
- Test count 276 → 282 (+6): PASS per SUMMARY-3.2 verification.
- No `Grpc.*`/`App.Metrics`/`OpenTracing`/`Jaeger.*`/`.Result`/`.Wait`/hard-coded IPs: PASS.

## Stage 2 (Integration) check results

- No conflicts with PLAN-3.1 plumbing: PASS. PLAN-3.1 added `ParallelValidators` to `DispatchItem`; PLAN-3.2 consumes it. Touch points are disjoint.
- `CapturingLogger<T>` used (not NSubstitute on `ILogger<T>`): PASS.
- No CHANGELOG entry: PASS (correctly deferred to PLAN-3.3).
- Architectural invariants: PASS.
- Test count gate (276 → 282 = +6): PASS per SUMMARY-3.2.
- `CounterInventoryDriftTests` still 1/1: PASS per SUMMARY-3.2.

---

## Findings

### Critical

None.

### Important

1. **Test 6 does not exercise the claimed cancellation propagation path** — `StopAsync` never cancels `_stoppingCts`, so `CancellationAwareValidator`'s `Task.Delay(..., ct)` is never interrupted. The test passes vacuously on a timing window, not on the actual `OperationCanceledException` → `Task.WhenAll` → outer catch chain. See Stage 2 above for remediation.

2. **`StopAsync` never cancels `_stoppingCts`** — in-flight `HttpClient`/validator calls that honour cancellation are not interrupted on host shutdown. Remediation: `await _stoppingCts.CancelAsync()` at the top of `StopAsync` before completing writers.

### Suggestions

1. Per-validator span parent context propagation may become fragile if parallel tasks are ever spawned via `Task.Run`.
2. Fixed `Task.Delay` polling in tests 1–5 should be replaced with `TaskCompletionSource`-based signaling for robustness on loaded CI hosts.

---

## Final verdict

**MINOR_ISSUES — PLAN-3.3 CAN PROCEED.**

The production behavior change is correct: `Task.WhenAll` strict-AND parallel branch is properly implemented; per-validator counter emission is preserved; sequential back-compat is intact; no new counters added. The two Important findings are real gaps — the `StopAsync`/CTS gap is a production correctness issue (validators will not be interrupted on shutdown), and Test 6 measures the wrong thing — but neither blocks PLAN-3.3's integration test and CHANGELOG work. Both should be fixed before the PR is merged to `main`.

Critical: 0 | Important: 2 | Suggestions: 2
