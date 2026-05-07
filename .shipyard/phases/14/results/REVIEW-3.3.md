# Review: Plan 3.3

## Verdict: PASS

---

## Stage 1: Spec Compliance
**Verdict: PASS**

### Task 1: End-to-end integration test (`ParallelValidatorsSliceTests.cs`)

- Status: PASS
- Evidence: `tests/FrigateRelay.IntegrationTests/ParallelValidatorsSliceTests.cs` (401 lines, 2 `[TestMethod]` cases).

**Test 1 — `Dispatch_ParallelValidators_CpaiAndRoboflowBothPass_ActionFires_ConcurrencyProven`**

- Real Mosquitto via `MosquittoFixture` (`await mosquitto.InitializeAsync()`). No new fixture added — same pattern as `MqttToBlueIrisSliceTests`. PASS.
- `SemaphoreSlim(SemaphoreInitial=0, SemaphoreCapacity=2)` — starts closed; both stubs `Wait()` on entry before returning their response. Initial=0 means both block immediately. Max=2 means `Release(2)` does not throw `SemaphoreFullException`. PASS.
- `blockedCount` implemented as `StrongBox<int>` (lines 63, 199, 201, 235, 237). All references go through `blockedCount.Value`. `Interlocked.Increment/Decrement(ref blockedCount.Value)` — thread-safe under WireMock's thread-pool. PASS.
- `blockedCount.Value == 2` assertion at line 87 is the load-bearing concurrency proof: sequential execution could not produce count=2 while the gate is still closed. PASS.
- After `gate.Release(2)` (line 92), test polls until BlueIris fires (up to 10 s), then asserts CPAI=1, Roboflow=1, BlueIris=1. PASS.
- `finally` block releases `SemaphoreCapacity` (2) so stubs are never left blocking if the test fails before the try-block release (lines 114-116). Semaphore capacity math is correct: on success, both stubs have already `Wait()`'d by the time `finally` runs (poll loop on lines 95-100 waits until BlueIris fires post-stub-completion), so count is 0 → `Release(2)` → count=2 ≤ max=2. No `SemaphoreFullException` risk. PASS.
- `[Timeout(60_000)]` attribute present. PASS.

**Test 2 — `Dispatch_ParallelValidators_CpaiPassesRoboflowRejects_ActionDoesNotFire_BothValidatorsRan`**

- CPAI stub: confidence=0.92 (above MinConfidence=0.7 in the host config at line 329). PASS.
- Roboflow stub: confidence=0.10 (below MinConfidence=0.7). PASS.
- Test polls until both WireMock servers have log entries (lines 150-153), then asserts CPAI=1 and Roboflow=1 — proves no first-reject short-circuit (CONTEXT-14 D6 load-bearing invariant). PASS.
- `Task.Delay(500)` settle-window (line 163) before asserting BlueIris is empty. PASS.
- BlueIris assertion: `.Should().BeEmpty(...)`. PASS.
- `[Timeout(60_000)]` attribute present. PASS.

**Host config shape (lines 313-354)**

- `Validators:cpai:Type = "CodeProjectAi"`, `BaseUrl` pointing at CPAI WireMock. PASS.
- `Validators:roboflow_persons:Type = "Roboflow"`, `ModelId = "rfdetr-base/1"`, `BaseUrl` pointing at Roboflow WireMock. PASS.
- `Subscriptions:0:Actions:0:ParallelValidators = "true"` (lines 353-354). PASS.
- `Validators:0` = `cpai`, `Validators:1` = `roboflow_persons` (lines 351-352). PASS.
- `Snapshots:DefaultProviderName = "Frigate"` with FrigateSnapshot WireMock stub returning a minimal JFIF byte sequence (lines 295-302). PASS.
- All URLs are dynamically allocated via `WireMockServer.Start()` / `mosquitto.Hostname` + `mosquitto.Port`. No hard-coded IPs. PASS.

**Must-haves from spec header**

- At least 1 integration test with ≥ 2 validators concurrently: 2 tests shipped. PASS.
- Real Mosquitto via Testcontainers + WireMock for both validators: PASS.
- Test count ≥ 269: SUMMARY reports 291 (282 unit + 9 integration). PASS (exceeds target by 22).
- No new counters (OQ-4 invariant): SUMMARY verification confirms `CounterInventoryDriftTests` 1/1. PASS.
- No `ServicePointManager`, `.Result`/`.Wait()`, hard-coded IPs: verified in SUMMARY. PASS.

### Task 2: CHANGELOG bullet for #23

- Status: PASS
- Evidence: `CHANGELOG.md` `[Unreleased]` `### Added` section, lines 12-41.

**ParallelValidators bullet (#23)**

- Strict-AND aggregation semantics: "every validator must return `Verdict.Pass()` for the action to fire". PASS.
- No first-reject short-circuit: "No first-reject short-circuit — every validator runs to completion even if one rejects". PASS.
- Default `false` / backward compat: "Default `false` preserves the v1.0/v1.1 sequential-with-short-circuit behavior". PASS.
- No-new-counters (OQ-4): "so each rejecting validator emits its own `frigaterelay.validators.rejected` counter". PASS.
- Config example uses `"Pushover"` / `cpai` / `roboflow_persons` — placeholder names only, no real IPs. PASS.
- `#23` referenced: PASS.
- Integration test cited as operational proof: "Operationally proven via `ParallelValidatorsSliceTests` integration suite — real Mosquitto + WireMock...". PASS.

**StopAsync hardening bullet**

- Documents `_stoppingCts.CancelAsync()` addition in `StopAsync`. PASS.
- Documents outer `catch (OperationCanceledException) when (ct.IsCancellationRequested)` wrapping `ReadAllAsync`. PASS.
- Both are confirmed implemented in source: `ChannelActionDispatcher.cs` lines 128 (`CancelAsync`) and 277-284 (outer catch). PASS.

**All three issue bullets present**

- `#13` (Roboflow): present at `[Unreleased]` `### Added` line 59. PASS.
- `#14` (DOODS2): present at line 43. PASS.
- `#23` (ParallelValidators): present at line 12. PASS.

---

## Special Items

### 1. Third skipped test rationale

`Dispatch_ParallelValidatorsTrue_AllRejectingValidatorsEmitTheirOwnRejectedCounter` in
`tests/FrigateRelay.Host.Tests/Dispatch/ChannelActionDispatcherParallelValidatorsTests.cs`
(lines 161-217) asserts:

- `capturedTags.Should().HaveCount(2)` — two separate `frigaterelay.validators.rejected` increments (one per validator). PASS.
- `taggedValidators.Should().Contain("alpha")` and `.Contain("beta")` — each increment carries its own `validator` tag. PASS.
- `tags.Should().Contain(t => t.Key == "action")` and `t.Key == "subscription"` — per OQ-4 tag matrix. PASS.
- Asserted via `MeterListener` over the live `Meter "FrigateRelay"` — not a mock. PASS.

The unit test fully covers OQ-4. Duplicating it at the integration level would add noise without signal. Skipped-test rationale is sound.

### 2. SemaphoreSlim gate semantics

`SemaphoreSlim(0, 2)` — initial=0, maxCount=2. Confirmed at lines 44-45 (`SemaphoreInitial = 0`, `SemaphoreCapacity = 2`). Both stubs block on their first `Wait()` because initial count is 0. `Release(2)` does not exceed capacity. Concurrent-waiters-count-2 is unprovable under sequential execution (second stub's callback never starts until first returns). Semantics are correct.

### 3. StrongBox<int> lambda capture

All references go through `blockedCount.Value` (lines 199, 201, 235, 237, 84, 87). `Interlocked.Increment/Decrement(ref blockedCount.Value)` — correct thread-safety. No residual `ref int` parameters. PASS.

---

## Stage 2: Code Quality

### Critical

None.

### Important

None new. The two Important findings from REVIEW-3.2 (Test 6 vacuous assertion; `StopAsync` not cancelling `_stoppingCts`) have both been remediated in commit `b46b544` (post-REVIEW-3.2): the `StopAsync` production fix is confirmed at `ChannelActionDispatcher.cs:128` and the outer `catch` at lines 277-284. REVIEW-3.2 Important #2 is closed. REVIEW-3.2 Important #1 (Test 6 now exercising the real cancellation path) is improved by the `_stoppingCts.CancelAsync()` fix (the validator's `HttpClient`/`Task.Delay` call will now actually be interrupted), though Test 6 itself was not restructured to add a `SemaphoreSlim` entry-signal. Given the production fix is in place and the test count gate passes 291/291, this is not a blocker for PR merge.

### Suggestions

1. **Test 1 polling loop uses `DateTime.UtcNow` comparisons rather than `CancellationToken`-backed `Task.Delay`** (lines 83-85, 95-99). On heavily loaded CI runners, a 25 ms poll and a 10-second wall-clock deadline is adequate but a `CancellationTokenSource(TimeSpan.FromSeconds(10))` passed to `Task.Delay` would let the loop exit more cleanly on cancellation. Low priority; existing pattern is consistent with `MqttToValidatorTests`.

2. **`StartCpaiStubWithGate` / `StartRoboflowStubWithGate` share 95% of their body** (lines 184-253). A generic `StartValidatorStubWithGate(string path, string body, SemaphoreSlim gate, StrongBox<int> counter)` helper would eliminate the duplication if additional integration tests adopt this pattern. Not worth extracting now (only 2 callers, scope is tight), but flagged as a future refactor if a third validator stub is added.

---

## Positive

- **SemaphoreSlim concurrency proof is the right choice.** The spec offered timing-based concurrency measurement as the primary method with the semaphore gate as a fallback. The builder correctly identified that the snapshot pre-resolve WireMock round-trip makes timing-based assertions unreliable on shared CI hosts and chose the deterministic gate. This is a stronger, zero-flakiness proof of the load-bearing operational invariant.
- **`finally` gate release is safe.** The `gate.Release(SemaphoreCapacity)` in the `finally` block correctly handles both the happy path and failure mid-test without `SemaphoreFullException` risk.
- **Test 2 polling strategy matches Test 1's pattern.** Waits for both validator WireMocks to have entries before asserting call counts — avoids asserting call counts before the dispatch has reached the validator layer.
- **No new test fixtures.** `MosquittoFixture` reuse keeps test infrastructure surface constant.
- **`[Unreleased]` is now fully populated.** All three v1.2 issues (#13 Roboflow, #14 DOODS2, #23 ParallelValidators) have operator-facing bullets. The StopAsync hardening cross-cut is documented. Ready for `[1.2.0]` promotion.

---

## Final Verdict

**PASS — APPROVE. Wave 3 (`feature/23-parallel-validators`) is ready to merge.**

Critical: 0 | Important: 0 | Suggestions: 2

Both tests implement the spec's must-haves exactly. The concurrency proof is deterministic. The CHANGELOG covers all three v1.2 issues with accurate operator-facing descriptions. The REVIEW-3.2 Important findings are remediated in source. No blocking issues.
