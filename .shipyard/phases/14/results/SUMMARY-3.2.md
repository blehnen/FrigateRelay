# Build Summary: Plan 3.2

## Status: complete

## Tasks Completed

- **Task 1: Add `Task.WhenAll` parallel branch in `ChannelActionDispatcher.ConsumeAsync`** + extract `RunOneValidatorAsync` helper. Commit `362e1de`.
- **Task 2: 6 unit tests in `ChannelActionDispatcherParallelValidatorsTests.cs`** covering CONTEXT-14 D5/D6 + OQ-4. Commit `684de85`.

## Files Modified

### Source

- `src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs` (modified) — the validator-chain block at the existing line range gained an `if (item.ParallelValidators)` branch:
  - **Sequential branch** (default, `ParallelValidators=false`): existing `foreach` body refactored to call the new `RunOneValidatorAsync` helper instead of inlining the span/counter/log logic. Behavior is bit-for-bit identical pre/post; the `goto NextItem` short-circuit is preserved exactly.
  - **Parallel branch** (`ParallelValidators=true`): `Task.WhenAll(item.Validators.Select(v => RunOneValidatorAsync(v, item, plugin, shared, ct)))` collects every verdict. Strict-AND aggregation via `verdicts.Any(v => !v.Verdict.Passed)` decides whether the action fires. **No first-reject short-circuit** — every validator already ran in parallel; the post-aggregate inspection just gates the action (CONTEXT-14 D6).
  - **`RunOneValidatorAsync` helper** at `ChannelActionDispatcher.cs:354` extracts the per-validator span/counter/log work so both branches share it. Returns `Task<(IValidationPlugin Validator, Verdict Verdict)>` — the tuple lets the parallel branch correlate verdicts back to validators for logging without re-iterating `item.Validators`.
  - **Cancellation chain** (per PLAN-3.2 spec lines 30-31 + 142): validator throws `OperationCanceledException` → propagates out of the helper → `Task.WhenAll` rethrows the first exception → propagates up to `ChannelActionDispatcher.cs:259`'s existing outer `catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }` (CONTEXT-9 D4 / ID-6 fix). NO new catch added in the parallel branch.

### Tests

- `tests/FrigateRelay.Host.Tests/Dispatch/ChannelActionDispatcherParallelValidatorsTests.cs` (new) — 6 `[TestMethod]` cases:
  1. **`Dispatch_ParallelValidatorsFalse_RunsValidatorsSequentially`** — regression test: `ParallelValidators=false` (default) preserves the existing sequential `foreach` + `goto NextItem` short-circuit behavior. Validates one passes, second is NEVER called when first rejects.
  2. **`Dispatch_ParallelValidatorsTrue_AllValidatorsPass_ActionExecutes`** — parallel happy path: all validators allow → action plugin's `ExecuteAsync` is called.
  3. **`Dispatch_ParallelValidatorsTrue_AnyValidatorRejects_ActionDoesNotExecute_AllValidatorsRan`** — load-bearing CONTEXT-14 D6 assertion: validator A allows, validator B rejects; action does NOT fire AND validator A's `ValidateAsync` WAS called (proves no short-circuit — both validators ran in parallel even though one rejected).
  4. **`Dispatch_ParallelValidatorsTrue_AllRejectingValidatorsEmitTheirOwnRejectedCounter`** — OQ-4 invariant: each rejecting validator emits its own `frigaterelay.validators.rejected` counter with correct tags (`subscription`, `camera`, `action`, `validator`). No aggregate counter. Asserted via `MeterListener` from `DispatcherDiagnostics`.
  5. **`Dispatch_ParallelValidatorsTrue_OneValidatorTimesOutFailClosed_ActionDoesNotExecute`** — timeout path: a fake validator returns `Verdict.Fail("validator_timeout")` (mimicking what the real CPAI/Roboflow/DOODS2 validators do internally on `TaskCanceledException`); the verdict flows through `Task.WhenAll` like any other return value; strict-AND aggregate fails; action does NOT fire.
  6. **`Dispatch_ParallelValidatorsTrue_HostCancellation_UnwindGracefully`** — cancellation propagation: a fake validator throws `OperationCanceledException` mid-flight; the exception propagates out of `Task.WhenAll`, is caught by the existing outer `catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }` at `ChannelActionDispatcher.cs:259`; consumer loop exits gracefully; action plugin's `ExecuteAsync` is NOT called.

## Decisions Made

- **Helper extraction (`RunOneValidatorAsync`).** The architect's PLAN-3.2 spec suggested the extraction; the builder followed it. Returns `(IValidationPlugin Validator, Verdict Verdict)` so the parallel branch can correlate verdicts back to their validators for the post-aggregate logging without re-iterating `item.Validators`. The sequential branch ignores the `Validator` element and only uses the `Verdict`. ~80 lines of shared code now live in one place.
- **Sequential branch is bit-for-bit unchanged in observable behavior.** It calls the new helper but the helper is a pure refactor of the existing inline logic — same span emission, same counter increments, same log calls, same `goto NextItem`. Phase 13 PLAN-1.1's `CounterTagMatrixTests` and Phase 9's existing dispatch tests all still pass without modification.
- **No `CancellationTokenSource` per validator** in the parallel branch — each validator's own `HttpClient.Timeout` enforces itself; their internal `catch (TaskCanceledException)` returns `Verdict.Fail("validator_timeout")` per their `OnError` config; that verdict is one of the values `Task.WhenAll` collects. Test 5 proves this works without dispatcher-level CTS plumbing.
- **No new counters** (CONTEXT-14 OQ-4). Phase 13's `CounterInventoryDriftTests` was re-run during verification — still passes (1/1). No `actions.rejected_by_validators` aggregate counter was added.
- **Test stub design: class-based fakes, not NSubstitute.** Tests use small private fake `IValidationPlugin` / `IActionPlugin` classes that record call counts + return configurable verdicts. NSubstitute would work for #1, #2, #3 but the cancellation test (#6) needs to throw on a specific call, and the no-short-circuit test (#3) needs to assert call ordering — easier with explicit fakes. CapturingLogger<T> handles log assertions per CLAUDE.md convention.

## Issues Encountered

- **Builder agent stopped before committing Task 2.** Same pattern as prior waves: builder finished writing `ChannelActionDispatcherParallelValidatorsTests.cs` (untracked file) but didn't commit. Orchestrator committed Task 2 + ran verification + wrote this SUMMARY. **Lesson seed:** persistent across waves — builder prompts should include "and commit AFTER each task is verified" but it doesn't seem to help when the agent runs out of internal turns.
- **No new test deps required.** The existing `FrigateRelay.Host.Tests` csproj already has MSTest, FluentAssertions, NSubstitute, and `FrigateRelay.TestHelpers` ProjectReference. The new tests use class-based fakes for plugins + the existing `MeterListener` pattern from Phase 13's observability tests.

## Verification Results

- `dotnet build FrigateRelay.sln -c Release` — **0 warnings, 0 errors** (12.4s elapsed).
- `bash .github/scripts/run-tests.sh --skip-integration` — **282/282 passing, 0 failures**. Test count rose 276 → 282 (+6 new parallel-validators tests).
- `git grep -n 'item.ParallelValidators' src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs` — present at line 212.
- `git grep -n 'Task.WhenAll' src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs` — present at line 127 (existing) AND line 315 (new parallel branch).
- `git grep -n 'RunOneValidatorAsync' src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs` — helper at line 354, called from sequential branch (line 277) and parallel branch (line 315).
- **Phase 13 `CounterInventoryDriftTests` still passes** — `dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build -- --filter "CounterInventory"` reports 1/1 passing. OQ-4 invariant holds: no new counters added.
- `git grep -nE 'Grpc\.|App\.Metrics|OpenTracing|Jaeger\.' src/` — empty ✓.
- `git grep -nE '\.(Result|Wait)\(' src/` — empty ✓.
- `git grep -n 'Substitute.For<ILogger' tests/FrigateRelay.Host.Tests/Dispatch/` — empty ✓ (CapturingLogger only).
- 2 atomic commits on `feature/23-parallel-validators`: `362e1de` (Task 1: dispatcher branch + helper), `684de85` (Task 2: 6 unit tests).

## Next: PLAN-3.3 (integration test + CHANGELOG)

PLAN-3.3 ships:
- An end-to-end integration test in `tests/FrigateRelay.IntegrationTests/` exercising ≥ 2 validators concurrently with real Mosquitto + WireMock for both validators. Uses the `MosquittoFixture` pattern from `tests/FrigateRelay.IntegrationTests/Fixtures/MosquittoFixture.cs:1-31`.
- The CHANGELOG `[Unreleased]` `### Added` bullet for #23 — the per-action `ParallelValidators: true` flag with strict-AND aggregation semantics. References the integration test as proof.
- Concurrency-proof assertion strategy: PLAN-3.3 spec was updated post-CodeRabbit-review (PR #42 review feedback) to use a computed delay-based timing assertion OR a `SemaphoreSlim` fallback for environments where wall-clock timing is too noisy.

## Notes for the PLAN-3.2 reviewer

- The sequential branch's behavior is unchanged; only the code shape changed (calls the new helper instead of inlining). Phase 9 existing dispatcher tests + Phase 13 CounterTagMatrixTests confirm no observable regression.
- Test 3 (no short-circuit) is the load-bearing CONTEXT-14 D6 assertion. The fake validators record their `ValidateAsync` call count; both must show `1` even when one returns `Verdict.Fail`.
- Test 4 (per-validator counter emission in parallel) uses the `MeterListener` from `tests/FrigateRelay.Host.Tests/Observability/CounterTagMatrixTests.cs` pattern. The assertion is that each rejecting validator's counter increments with its own `validator` tag — the existing Phase 13 tagging behavior, just under a parallel scheduler.
- Test 6 (cancellation) does NOT add a new catch — it asserts the existing `ChannelActionDispatcher.cs:259` outer catch handles the propagated exception. Reviewer should verify no new `catch (OperationCanceledException)` was added inside the parallel branch.
