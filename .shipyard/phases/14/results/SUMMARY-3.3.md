# Build Summary: Plan 3.3

## Status: complete

## Tasks Completed

- **Task 1: End-to-end integration test** for `ParallelValidators` (CPAI + Roboflow concurrently against real Mosquitto + WireMock + the host process). Commit `3f00331`.
- **Task 2: CHANGELOG `[Unreleased]` `### Added` bullets** for #23 + the cross-cutting `StopAsync` hardening. Commit `f1767f7`.

## Files Modified

### Tests

- `tests/FrigateRelay.IntegrationTests/ParallelValidatorsSliceTests.cs` (new) — 401 lines, 2 `[TestMethod]` cases:

  1. **`Dispatch_ParallelValidators_CpaiAndRoboflowBothPass_ActionFires_ConcurrencyProven`**
     - Both validator stubs use a `SemaphoreSlim` gate that the test holds closed.
     - Test waits until `blockedCount.Value == 2` (both stubs simultaneously waiting on the gate) — provable concurrency.
     - Releases the gate; both stubs return `Allow`; BlueIris HTTP trigger is called exactly once.
  2. **`Dispatch_ParallelValidators_CpaiPassesRoboflowRejects_ActionDoesNotFire_BothValidatorsRan`**
     - Roboflow returns `confidence=0.10` (below `MinConfidence=0.7`) → rejects.
     - CPAI returns high confidence → would pass.
     - Both validator WireMocks must record exactly 1 request — proves no first-reject short-circuit (CONTEXT-14 D6).
     - BlueIris HTTP trigger must NOT fire — strict-AND aggregation rejects the action.

### CHANGELOG

- `CHANGELOG.md` `[Unreleased]` `### Added` gains two bullets (placed at the top per descending-chronological convention):
  1. **`ParallelValidators: true` opt-in (#23)** — full operator-facing description of strict-AND aggregation, no-short-circuit invariant, default-false backward compat, configuration shape, and the integration-test concurrency proof.
  2. **`ChannelActionDispatcher` graceful-shutdown hardening** — documents the `StopAsync._stoppingCts.CancelAsync()` addition and the outer `try/catch` wrapping `ReadAllAsync(ct)` in `ConsumeAsync` (REVIEW-3.2 Important #2 fix). Cross-cutting correctness fix, not specific to ParallelValidators.

`[Unreleased]` is now ready for promotion to `[1.2.0]` once Phase 14 ships:
- DOODS2 (#14)
- Roboflow (#13)
- ParallelValidators (#23)
- StopAsync hardening
- ID-29 hotfix (already in `[Unreleased]` from earlier — carryover for v1.1.x window)

## Decisions Made

- **Concurrency proof: SemaphoreSlim gate, not wall-clock timing.** PLAN-3.3 §spec line 66-67 documented the `SemaphoreSlim` fallback for environments where timing-based assertions are too noisy. The builder evaluated the timing approach and rejected it: the Frigate snapshot pre-resolve step (a WireMock HTTP round-trip inside the dispatcher BEFORE validators run) contributes 200-2500ms of inherently variable overhead on the same machine, swamping the sequential-vs-parallel gap of `2 × ValidatorDelayMs ≈ 400ms`. The semaphore proves concurrency deterministically with zero CI flakiness risk.

  Mechanism: both stubs `Wait()` on a `SemaphoreSlim(0, 2)` (starts closed). The test asserts `blockedCount.Value == 2` — both stubs are simultaneously inside their callback, blocked on the gate. If validators ran sequentially the second stub would never enter its callback until the first returned, so the count would never reach 2 while the gate is closed.

- **Per-validator counter emission test deliberately skipped at the integration level.** Already covered by PLAN-3.2's Test 4 (`ChannelActionDispatcherParallelValidatorsTests.AllRejectingValidatorsEmitTheirOwnRejectedCounter`) which asserts the same OQ-4 invariant via `MeterListener` at the unit level. Adding it to the integration test would duplicate coverage with no additional signal — the integration test class is already 401 lines and a third test wouldn't justify the added complexity.

- **DOODS2 NOT included in this integration test.** PLAN-3.3 §spec settled on CPAI + Roboflow as the smallest meaningful coverage. Adding DOODS2 would inflate the test (a third WireMock stub, third validator config block) without changing what's being proven (concurrency + no short-circuit). The unit-level `ChannelActionDispatcherParallelValidatorsTests.cs` already covers the three-validator case.

- **`StrongBox<int>` for the lambda-captured counter.** C# does not allow `ref` parameters in lambda captures. The builder originally tried `ref int blockedCount` parameters and the build failed (CS1628). Refactored to wrap the counter in `System.Runtime.CompilerServices.StrongBox<int>` (a reference-typed cell) so the lambda can capture by reference cleanly. `Interlocked.Increment(ref blockedCount.Value)` keeps the increment thread-safe under WireMock's thread-pool callbacks.

## Issues Encountered

- **Builder agent stopped mid-build with WireMock callback API investigation.** Same pattern as prior waves: builder finished the test class structure (401 lines, 2 of 3 originally-planned tests) but stopped before fixing the `ref int blockedCount` lambda-capture compile errors AND before committing. Orchestrator finished the StrongBox refactor, ran the integration tests against a live Mosquitto via Testcontainers (2/2 passing in 6.3s), committed Task 1, wrote the CHANGELOG bullets (Task 2), and wrote this SUMMARY. **Lesson seed:** PLAN-3.3 hit the same builder-context-budget issue we've seen across every wave. For future PRs, integration-test plans should be split — harness scaffolding in one plan, test cases in a second plan — so each fits in one builder context.

- **Per-validator counter emission integration test skipped (with rationale).** PLAN-3.3 §spec listed 3 tests; only 2 shipped. The 3rd duplicates PLAN-3.2 Test 4. SUMMARY explicitly documents the rationale.

## Verification Results

- `dotnet build FrigateRelay.sln -c Release` — **0 warnings, 0 errors**.
- `bash .github/scripts/run-tests.sh --skip-integration` — **282/282 passing** (unit-only, no Docker required).
- `bash .github/scripts/run-tests.sh` — **291/291 passing** (full suite including integration; Docker required for Mosquitto). Test count: 282 unit + 9 integration (7 existing + 2 new) = 291. Plan target was 285+; we exceed by 6.
- `dotnet run --project tests/FrigateRelay.IntegrationTests -c Release --no-build -- --filter "ParallelValidators"` — **2/2 passing in 6.3s**.
- `git grep -n 'item.ParallelValidators' src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs` — present.
- `git grep -n 'Task.WhenAll' src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs` — present in two places (consumer-task aggregator at line 127, parallel branch at line 318).
- `dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build -- --filter "CounterInventory"` — Phase 13 drift test still passes 1/1 (OQ-4 invariant: no new counters added).
- `grep -n 'ParallelValidators\|#23' CHANGELOG.md` — multiple matches in the new bullet under `[Unreleased]` `### Added`.
- `git grep -nE 'Grpc\.|App\.Metrics|OpenTracing|Jaeger\.' src/` — empty ✓.
- `git grep -nE '\.(Result|Wait)\(' src/` — empty ✓.
- 2 atomic commits on `feature/23-parallel-validators`: `3f00331` (Task 1: integration test), `f1767f7` (Task 2: CHANGELOG).

## Wave 3 final state — PHASE 14 BUILD COMPLETE

11 commits total on `feature/23-parallel-validators`:

| Commit | Plan | Scope |
|---|---|---|
| `3e50c83` | PLAN-3.1 T1 | ActionEntry record + JsonConverter + 6 tests |
| `9f64d69` | PLAN-3.1 T2+3 | DispatchItem / IActionDispatcher / EventPump plumbing |
| `c083891` | SUMMARY-3.1 | |
| `55983d8` | REVIEW-3.1 | TypeConverter gap-fill (3 IConfiguration.Bind tests) + REVIEW commit |
| `362e1de` | PLAN-3.2 T1 | Dispatcher parallel branch + RunOneValidatorAsync helper |
| `684de85` | PLAN-3.2 T2 | 6 dispatcher unit tests |
| `751c9cb` | SUMMARY-3.2 | |
| `b46b544` | REVIEW-3.2 | StopAsync hardening + Test 6 deterministic signal + outer try/catch |
| `3f00331` | PLAN-3.3 T1 | Integration test (this commit) |
| `f1767f7` | PLAN-3.3 T2 | CHANGELOG bullets (this commit) |
| `<next>`  | SUMMARY-3.3 | This file |

**Test count progression Phase 14:**
- v1.1.0 baseline: 242
- Wave 1 PR #42 (Roboflow): 242 → 251 → 256 (+5 PluginRegistrar tests for codecov) → 258 (+2 ApiKey + JsonException tests after CodeRabbit review)
- Wave 2 PR #43 (DOODS2): 258 → 267 (+9 HTTP-only tests after gRPC reversal)
- Wave 3 PR (this branch): 267 → 273 (+6 ActionEntry binding) → 276 (+3 IConfiguration.Bind gap-fill) → 282 (+6 dispatcher unit tests) → 291 (+2 integration tests, +1 from full suite delta)

**Phase 14 FINAL test count: 291 (49 new tests across the phase).**

## Notes for the PLAN-3.3 reviewer

- Integration test uses `MosquittoFixture` from `tests/FrigateRelay.IntegrationTests/Fixtures/MosquittoFixture.cs:1-31` — same pattern as `MqttToBlueIrisSliceTests` and `MqttToValidatorTests`.
- Both WireMock validator stubs match the canonical request paths: `/v1/vision/detection` (CPAI) and `/infer/object_detection` (Roboflow). Confidences are 0.0–1.0 scale for Roboflow stub (matches plugin contract).
- The `SemaphoreSlim` gate pattern is reusable — could be extracted to a `Fixtures/SemaphoreGatedStub.cs` helper if future integration tests want the same concurrency-proof shape. Not done here to keep PR scope tight.
- The `await Task.Delay(500)` after asserting both validators were called (Test 2 line 163) is a settle-window so BlueIris doesn't have a chance to fire after the assertion. 500ms is generous for CI; could be tightened with `TaskCompletionSource` signaling if it ever proves too long.
- No new test stubs needed — the existing `MosquittoFixture` + WireMock pattern handled everything.

## Next: PLAN-3.3 reviewer, then push + open PR for #23

After REVIEW-3.3 PASS, push `feature/23-parallel-validators` and open PR #44 against `main`. Once merged, `[Unreleased]` is fully populated and ready for promotion to `[1.2.0]` per `RELEASING.md`.

Phase 14 / v1.2 endgame: PR #44 merge → CHANGELOG promotion → `git tag v1.2.0` (operator-cut) → release.yml builds and pushes multi-arch GHCR images.
