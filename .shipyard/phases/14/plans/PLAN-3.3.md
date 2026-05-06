---
phase: 14-v1.2-inference-engines-parallel-validation
plan: 3.3
wave: 3
dependencies: [3.1, 3.2]
must_haves:
  - At least one integration test exercising ≥ 2 validators concurrently end-to-end (CPAI + Roboflow via WireMock)
  - Real Mosquitto via Testcontainers + WireMock for both validators (existing pattern from RESEARCH §5)
  - CHANGELOG bullet under [Unreleased] / ### Added describing per-action ParallelValidators opt-in
  - Cumulative test count rises from 268 (post-PLAN-3.2) to ≥ 269
files_touched:
  - tests/FrigateRelay.IntegrationTests/ParallelValidatorsSliceTests.cs
  - CHANGELOG.md
tdd: true
risk: medium
---

# Plan 3.3: Parallel-validators integration test + CHANGELOG (PR #23)

## Context

PLAN-3.2 covers the parallel-validators behavior with unit tests; this plan ships an end-to-end integration test that proves the multi-engine story is operationally real, not hypothetical. Per CONTEXT-14 D1 (the rationale for the #13 → #14 → #23 PR ordering): "PR #23 lands after both #13 and #14 are merged so its integration test can exercise three validator types (CPAI + Roboflow + DOODS2) in a single AND chain."

This plan ships **CPAI + Roboflow** (two validators in parallel) as the minimum bar. The DOODS2 third validator is **deferred** out of pragmatism: DOODS2's gRPC transport requires the in-process gRPC test host pattern from PLAN-2.3 to be hoisted into the integration-tests project, which introduces non-trivial test-infrastructure surface. CPAI + Roboflow + DOODS2-HTTP is a stretch goal if the builder finds the integration-test ergonomics easy; otherwise CPAI + Roboflow is acceptable and matches the success criterion in ROADMAP.md line 449 ("the CPAI + Roboflow combination is the smallest meaningful coverage").

The existing `tests/FrigateRelay.IntegrationTests/MqttToBlueIrisSliceTests.cs` (RESEARCH §5) is the canonical pattern: real Mosquitto via Testcontainers + `MosquittoFixture`, WireMock for downstream HTTP. The CHANGELOG bullet wraps up PR #23 (the parallel-validators feature) with an explicit example.

Test count target: 268 (post-PLAN-3.2) → **269 (+1 new)** integration test. This hits the RESEARCH §8 final target.

## Dependencies

- **PLAN-3.1, PLAN-3.2** — `ParallelValidators` field plumbed and dispatcher branch implemented.
- The `tests/FrigateRelay.IntegrationTests/` project must already exist (it does — Phase 4+) and use `MosquittoFixture` + WireMock per RESEARCH §5.

## Tasks

### Task 1: Write the parallel-validators end-to-end integration test

**Files:** `tests/FrigateRelay.IntegrationTests/ParallelValidatorsSliceTests.cs` (new)

**Action:** create

**Description:**

Single test class modeled on `tests/FrigateRelay.IntegrationTests/MqttToBlueIrisSliceTests.cs` (RESEARCH §5). Locate the existing slice-test exemplar at execution time via `find tests/FrigateRelay.IntegrationTests -name '*SliceTests.cs'`; copy its harness setup verbatim (Mosquitto fixture, in-process host bootstrap, WireMock servers).

Test scenario: `Dispatch_ParallelValidators_CpaiAndRoboflowBothPass_ActionFires`:

1. Start a `MosquittoFixture` (RESEARCH §5).
2. Start two WireMock servers — one for CPAI, one for Roboflow. Each stubs its own validator endpoint:
   - CPAI: `POST /v1/vision/detection` returns `{"success":true,"predictions":[{"label":"person","confidence":0.92,...}]}`.
   - Roboflow: `POST /infer/object_detection` returns `{"predictions":[{"class":"person","confidence":0.92,...}]}`.
3. Start a third WireMock server for the action plugin (e.g. BlueIris-style — clone whatever the existing slice tests use).
4. Build an in-process `Host` with config layered to define:
   - One subscription matching `camera="frontdoor"`, `label="person"`.
   - One action `BlueIris` with `Validators: ["cpai", "roboflow_persons"]` and `ParallelValidators: true`.
   - Two validator entries: `cpai` (Type="CodeProjectAi", BaseUrl pointing at the CPAI WireMock), `roboflow_persons` (Type="Roboflow", ModelId="rfdetr-base/1", BaseUrl pointing at the Roboflow WireMock).
   - Snapshot provider configured per the existing slice test's pattern (so validators have a snapshot to consume).
5. Publish a Frigate `frigate/events` MQTT message that matches the subscription.
6. Wait (with a bounded timeout — e.g. 10s) for the BlueIris WireMock to receive its trigger request.

Assertions:
- BlueIris WireMock recorded exactly 1 request (action fired).
- CPAI WireMock recorded exactly 1 request.
- Roboflow WireMock recorded exactly 1 request.
- **Concurrency proof.** Each validator's WireMock stub adds an artificial `WithDelay(per_validator_delay)` (e.g. 200ms each) so the sequential floor is `2 × per_validator_delay = 400ms` and the parallel ceiling is `~per_validator_delay + scheduling_overhead ≈ 250ms`. Time the full dispatch with a `Stopwatch` and assert `elapsed.TotalMilliseconds < (per_validator_delay + safety_margin)` where `safety_margin` is computed from `per_validator_delay × 0.5` (e.g. 100ms safety on 200ms delays) — gives a 1.6×-the-safety-margin gap between the parallel ceiling and the sequential floor that won't drift on slow CI runners. Avoid hardcoded absolute thresholds (e.g. "100ms"); compute them from the configured stub delays. The two timestamps' overlap (`RequestMessage.DateTime` deltas being small) is a secondary, informational check — not the primary proof. **Fallback if timing-based assertion is still flaky:** use a shared `SemaphoreSlim` released by the test that both stubs await on entry — this proves the validators *both reached the HTTP layer concurrently* deterministically without relying on wall-clock timing.

A second assertion test (optional, only if the harness setup is cheap enough to reuse): `Dispatch_ParallelValidators_CpaiPassesRoboflowRejects_ActionDoesNotFire` with a single test method exercising the strict-AND aggregation — Roboflow stub returns a low-confidence prediction; CPAI passes; assert BlueIris WireMock recorded ZERO requests. This second case would push the test count to 270 (+2 new); the plan's minimum is 1 new test (269 total), but a second test is welcome and trivially additive given the shared harness.

**Stretch goal — DOODS2-HTTP third validator:** if the builder finds the harness ergonomic, add `doods2_persons` (Transport="Http") as a third validator on the same action and stub its WireMock. The three-validator parallel test was cited in ROADMAP.md line 449 as the "target". Skip if it adds significant complexity.

XML doc-comment on the test class explains the scenario: "End-to-end coverage of the per-action `ParallelValidators: true` opt-in (#23) with two validators (CPAI + Roboflow) running concurrently against the same dispatched action."

**Acceptance Criteria:**
- `dotnet run --project tests/FrigateRelay.IntegrationTests -c Release --no-build` exits 0 and reports at least 1 new test (`ParallelValidators` filter).
- `dotnet build FrigateRelay.sln -c Release` zero warnings.
- Cumulative test count ≥ 269 (268 post-PLAN-3.2 + 1 new): `bash .github/scripts/run-tests.sh` (with integration tests, NO `--skip-integration` flag) confirms.
- The test asserts both validator WireMocks received their request AND the timing delta is below the sequential lower bound (concrete proof of concurrency).
- The action's WireMock recorded exactly one trigger (proves strict-AND happy path).
- No new counters introduced (CONTEXT-14 OQ-4 invariant): `git diff src/FrigateRelay.Host/Dispatch/DispatcherDiagnostics.cs` since the start of Wave 3 is empty.
- Architectural invariants unchanged: no `ServicePointManager`, no `.Result`/`.Wait()`, no hard-coded IPs in the test fixture (Mosquitto + WireMock URLs are dynamically allocated by their respective frameworks).

---

### Task 2: CHANGELOG bullet for #23

**Files:** `CHANGELOG.md`

**Action:** modify

**Description:**

Add a bullet under `[Unreleased]` / `### Added` (the section already exists from PLAN-1.2 / PLAN-2.3). Bullet text:

```markdown
- **Per-action parallel validators (#23).** New `ParallelValidators: true` opt-in on
  any `ActionEntry`. When enabled, the action's validators run concurrently via
  `Task.WhenAll` instead of sequentially. Aggregation is strict-AND: every validator
  must `Verdict.Allow` for the action to fire. First-reject does NOT short-circuit
  other in-flight validators — operators get full per-validator visibility on every
  dispatch via the existing `validators.rejected` per-validator counter (no new
  counters added). Each validator's own `Timeout` and `OnError` (FailClosed/FailOpen)
  apply unchanged in parallel mode — "parallel" changes scheduling, not failure
  semantics. Default `false`; existing v1.0 / v1.1 configs upgrade unchanged.

  **Example** (`appsettings.json`):
  ```json
  "Subscriptions": [
      {
          "Camera": "frontdoor", "Label": "person",
          "Actions": [
              {
                  "Plugin": "BlueIris",
                  "Validators": ["cpai", "roboflow_persons", "doods_persons"],
                  "ParallelValidators": true
              }
          ]
      }
  ]
  ```
```

Then add a closing entry summarizing the v1.2 release shape — three issues (#13, #14, #23), all additive, semver minor (v1.2.0). The exact wording is the operator's call at tag-cut time per CONTEXT-12 D7 manual tag-cut policy; the planner's job is just to ensure each PR's changelog bullet exists by the end of its plan.

**Acceptance Criteria:**
- The `#23` bullet exists under `[Unreleased]` / `### Added` in `CHANGELOG.md`.
- The bullet describes the strict-AND aggregation and the "no first-reject short-circuit" invariant.
- The bullet describes the no-new-counters invariant (CONTEXT-14 OQ-4).
- The example uses placeholder names like `frontdoor` and validator keys like `cpai`/`roboflow_persons`/`doods_persons` — no real IPs, no real hostnames.
- All three bullets (`#13`, `#14`, `#23`) are present in the same `[Unreleased]` / `### Added` section: `grep -E '^- \*\*.*#1[34]\*\*\.' CHANGELOG.md` shows `#13` and `#14`; `grep -E '^- \*\*.*#23\*\*\.' CHANGELOG.md` shows `#23`.

## Verification

```bash
# Build clean
dotnet build FrigateRelay.sln -c Release

# Full test suite passes — unit + integration
bash .github/scripts/run-tests.sh

# Test count gate — at least 269 total (Phase 14 final target per RESEARCH §8)
TOTAL=$(bash .github/scripts/run-tests.sh 2>&1 | grep -E '^total:' | awk '{ sum += $2 } END { print sum }')
[ "$TOTAL" -ge 269 ] || { echo "test count regression: $TOTAL < 269"; exit 1; }

# Integration test for parallel-validators specifically
dotnet run --project tests/FrigateRelay.IntegrationTests -c Release --no-build -- --filter "ParallelValidators"

# Counter inventory unchanged — Phase 13's drift test still passes
dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build -- --filter "CounterInventory"

# Architectural invariants — final sweep
git grep -nE 'App\.Metrics|OpenTracing|Jaeger\.' src/                # must be empty
git grep -nE '\.(Result|Wait)\(' src/                                 # must be empty
git grep -n 'ServicePointManager' src/                                # must be empty
git grep -nE '192\.168\.|10\.0\.0\.|172\.(1[6-9]|2[0-9]|3[01])\.' src/   # must be empty

# gRPC dep containment — STILL holds at end of Phase 14
dotnet list src/FrigateRelay.Abstractions/FrigateRelay.Abstractions.csproj package --include-transitive | grep -E 'Grpc\.' && exit 1 || true
dotnet list src/FrigateRelay.Host/FrigateRelay.Host.csproj package --include-transitive | grep -E 'Grpc\.' && exit 1 || true
git grep -nE 'Grpc\.' src/FrigateRelay.Host src/FrigateRelay.Abstractions   # must be empty

# CHANGELOG references all three issues
grep -n '#13' CHANGELOG.md
grep -n '#14' CHANGELOG.md
grep -n '#23' CHANGELOG.md

# Secret scan + tripwire still clean
bash .github/scripts/secret-scan.sh
```
