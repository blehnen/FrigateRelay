# Plan Critique: Phase 16 — v1.3.0 Surface Inventory
**Date:** 2026-05-07
**Phase:** 16 (v1.3.0 Minor Release)
**Type:** Plan Review — Feasibility Stress Test (Step 6a)

---

## Verdict

**READY** with minor builder clarifications.

All three plans are **feasible and well-researched**. File paths, API surfaces, and verification commands are concrete and runnable. No blocking dependencies exist between Wave 1 plans. CHANGELOG.md is the only shared file (sequential dispatch mitigates friction per Phase 15 lesson). A few pre-build verifications are required — flagged below.

---

## Per-Plan Findings

### PLAN-1.1: MetricsTagWriter + KnownCameras allowlist (#18)

| # | Check | Status | Details |
|---|-------|--------|---------|
| 1 | File paths exist | PASS | EventPump.cs, ChannelActionDispatcher.cs, HostBootstrap.cs, CounterIncrementTests.cs, EventPumpSpanTests.cs, docs/observability.md, README.md, CHANGELOG.md all present. New greenfield paths `src/FrigateRelay.Host/Observability/{MetricsTagWriter,MetricsTagsOptions}.cs` and `tests/FrigateRelay.Host.Tests/Observability/MetricsCardinalityTests.cs` confirmed absent (expected). |
| 2 | API surface matches | PASS | `DispatcherDiagnostics` is `static class` with 8 camera-tagged `IncrementXxx()` static helpers (confirmed at lines 132–254). All signatures accept `EventContext` or `DispatchItem` + optional string; none have `string camera` overloads yet (PLAN-1.1 Task 2 adds them per RC-3 design). `CapturingLogger<T>` field is `Entries` (not `Records`, confirmed). |
| 3 | Verify commands runnable | PASS | All verification commands use valid syntax: `dotnet build FrigateRelay.sln -c Release` (FrigateRelay.sln exists), `dotnet run --project tests/FrigateRelay.Host.Tests -c Release -- --filter "MetricsCardinalityTests"` (MTP filter syntax correct), `git grep -n 'NormalizeCameraTag'` and `git grep -nE 'Otel:MetricsTags\|KnownCameras'` (valid patterns). |
| 4 | Forward references | PASS | No cross-plan file dependencies within Wave 1. PLAN-1.2 does not reference `MetricsTagWriter`. PLAN-1.3 does not reference observability changes. |
| 5 | Hidden file dependencies | PASS | CHANGELOG.md is the only shared file (acknowledged in Notes). All other files disjoint across PLAN-1.1, PLAN-1.2, PLAN-1.3. |
| 6 | Complexity | PASS | PLAN-1.1 touches 11 files (including docs and CHANGELOG). Spans 3 directories: `src/FrigateRelay.Host/Observability/` (new), `src/FrigateRelay.Host/`, `tests/FrigateRelay.Host.Tests/Observability/`, `docs/`. Medium risk — greenfield directory creation + multiple test-helper modifications, but the scope is narrow (single counter tag normalization concern). |

### PLAN-1.2: Replace Task.Delay with WaitForEntriesAsync (#22)

| # | Check | Status | Details |
|---|-------|--------|---------|
| 1 | File paths exist | PASS | `tests/FrigateRelay.TestHelpers/CapturingLogger.cs` present. CounterIncrementTests.cs and EventPumpSpanTests.cs present. 4 fragility sites at lines 285, 359, 393, 425 confirmed via inspection. CHANGELOG.md present. |
| 2 | API surface matches | PASS | `CapturingLogger<T>.Entries` field confirmed (List<LogEntry>). New `WaitForEntriesAsync(int count, TimeSpan timeout, CancellationToken ct = default)` signature matches CONTEXT-16 D2 spec exactly. All 4 Task.Delay call sites use numeric delays (400, 400, 300, 200/100) matching RESEARCH.md fragility list. 2 `Task.Delay(Timeout.Infinite, ct)` sites in BatchSource (line 481) and FakeSource (line 337) confirmed — excluded from greppable invariant per OQ-5 correction. |
| 3 | Verify commands runnable | PASS | `git grep -nE 'Task\.Delay\([0-9]'` and `git grep -nE 'Task\.Delay\(Timeout\.Infinite'` syntax valid (RESEARCH.md RC-1 / OQ-5 note: tightened invariant excludes Timeout.Infinite pattern). `dotnet run --project tests/FrigateRelay.Host.Tests -c Release` valid. |
| 4 | Forward references | PASS | No PLAN-1.2 output consumed by PLAN-1.1 or PLAN-1.3 within Wave 1. WaitForEntriesAsync extension does not change test counts or fail any baseline tests. |
| 5 | Hidden file dependencies | PASS | CHANGELOG.md only. Test-helper files and observability test files disjoint from PLAN-1.1 and PLAN-1.3. |
| 6 | Complexity | PASS | PLAN-1.2 touches 4 files across 2 directories (testhelpers + observability tests). Low risk — purely mechanical polling helper addition + 4 site replacements under existing test coverage. |

### PLAN-1.3: Registrar HttpClient unification + CPAI test backfill (#30)

| # | Check | Status | Details |
|---|-------|--------|---------|
| 1 | File paths exist | PASS | All three registrars (CodeProjectAi, Roboflow, Doods2) PluginRegistrar.cs files present at lines 75–84 (byte-for-byte symmetric per RESEARCH.md). Roboflow + Doods2 registrar tests exist (5 tests each, confirmed). CPAI test file `CodeProjectAiValidatorTests.cs` present; new `CodeProjectAiPluginRegistrarTests.cs` absent (expected). CHANGELOG.md present. |
| 2 | API surface matches | PASS | All three registrars follow identical pattern: `AddHttpClient(clientName).ConfigurePrimaryHttpMessageHandler(...)` at lines ~54–72, keyed-singleton factory at lines ~75–84. Both patterns confirmed identical across trio (CPAI/Roboflow/DOODS2). No per-plugin variants. BaseAddress/Timeout mutations at lines 80–81 in factory bodies confirmed. Test mirror target (Doods2PluginRegistrarTests.cs) has 5 tests with correct shape per RESEARCH.md. |
| 3 | Verify commands runnable | PASS | `dotnet run --project tests/FrigateRelay.Plugins.CodeProjectAi.Tests -c Release -- --filter "CodeProjectAiPluginRegistrarTests"` valid (project exists). `dotnet run --project tests/FrigateRelay.Plugins.Roboflow.Tests -c Release` and `dotnet run --project tests/FrigateRelay.Plugins.Doods2.Tests -c Release` valid. `git log --oneline -1 -- <three registrar paths>` command syntax valid. `git grep -nE '(http\|client)\.(BaseAddress\|Timeout)\s*='` syntax valid. |
| 4 | Forward references | PASS | No PLAN-1.1 or PLAN-1.2 outputs consumed. Registrar refactor is self-contained; test backfill does not depend on other plans. |
| 5 | Hidden file dependencies | PASS | CHANGELOG.md only. Plugin registrar files and test files fully disjoint across plans. |
| 6 | Complexity | PASS | PLAN-1.3 touches 5 files (3 registrars + 1 new test file + CHANGELOG). Spans 6 directories (3 plugins + 3 test projects). Medium risk — atomic 3-file commit requirement (D6) is non-negotiable but achievable in a single `git commit` call. CPAI test backfill is straightforward mirror. |

---

## File → Plan Ownership Map

```
src/FrigateRelay.Host/Observability/MetricsTagWriter.cs          → PLAN-1.1
src/FrigateRelay.Host/Observability/MetricsTagsOptions.cs        → PLAN-1.1
src/FrigateRelay.Host/EventPump.cs                               → PLAN-1.1
src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs        → PLAN-1.1
src/FrigateRelay.Host/HostBootstrap.cs                           → PLAN-1.1
tests/FrigateRelay.Host.Tests/Observability/MetricsCardinalityTests.cs → PLAN-1.1
tests/FrigateRelay.Host.Tests/Observability/CounterIncrementTests.cs → PLAN-1.1
tests/FrigateRelay.Host.Tests/Observability/EventPumpSpanTests.cs → PLAN-1.1

tests/FrigateRelay.TestHelpers/CapturingLogger.cs                → PLAN-1.2
tests/FrigateRelay.Host.Tests/Observability/EventPumpSpanTests.cs → PLAN-1.2 (shared with PLAN-1.1)
tests/FrigateRelay.Host.Tests/Observability/CounterIncrementTests.cs → PLAN-1.2 (shared with PLAN-1.1)

src/FrigateRelay.Plugins.CodeProjectAi/PluginRegistrar.cs       → PLAN-1.3
src/FrigateRelay.Plugins.Roboflow/PluginRegistrar.cs            → PLAN-1.3
src/FrigateRelay.Plugins.Doods2/PluginRegistrar.cs              → PLAN-1.3
tests/FrigateRelay.Plugins.CodeProjectAi.Tests/CodeProjectAiPluginRegistrarTests.cs → PLAN-1.3

CHANGELOG.md                                                      → PLAN-1.1, PLAN-1.2, PLAN-1.3 (shared)
docs/observability.md                                            → PLAN-1.1
README.md                                                        → PLAN-1.1
```

**Note:** CounterIncrementTests.cs and EventPumpSpanTests.cs are touched by both PLAN-1.1 (constructor injection + counter call sites) and PLAN-1.2 (Task.Delay replacement). Builder must apply both sets of changes atomically to these files to avoid merge conflicts. Recommend PLAN-1.1 first (adds MetricsTagWriter param + normalization), then PLAN-1.2 (replaces polling sites in the same methods).

---

## Risk Callouts

### RC-1: Observability directory greenfield creation
PLAN-1.1 Task 1 creates `src/FrigateRelay.Host/Observability/` directory (does not exist yet). Ensure directory is created before adding `.cs` files. Low risk; straightforward file addition.

### RC-2: Atomic 3-file commit for registrars (PLAN-1.3 D6)
PLAN-1.3 Task 1 mandates all three registrar files committed together in a single commit. Builder must use `git add` on all three, then `git commit -m "..."` once. No per-plugin split across commits. Recommend using glob: `git add src/FrigateRelay.Plugins.*/PluginRegistrar.cs` or explicit per-file. Verify post-commit: `git log --oneline -1 -- <three paths>` shows one commit hash covering all three.

### RC-3: Task.Delay invariant scope tightening (PLAN-1.2 OQ-5)
ROADMAP success criterion and greppable invariant must be updated to reflect RESEARCH.md RC-1 / OQ-5 tightening. Original wording: "git grep -nE 'Task\.Delay' tests/FrigateRelay.Host.Tests/Observability/ returns empty". Corrected to: "git grep -nE 'Task\.Delay\([0-9]' tests/FrigateRelay.Host.Tests/Observability/ returns empty" (numeric delays only). The 2 `Task.Delay(Timeout.Infinite, ct)` cancellation-await sites in BatchSource.ReadEventsAsync and FakeSource.ReadEventsAsync (lines 481, 337) are structurally correct and intentionally excluded. PLAN-1.2 CHANGELOG entry explicitly notes this correction.

### RC-4: ChannelActionDispatcher.EnqueueAsync signature at build start
PLAN-1.1 Task 2 adds `MetricsTagWriter` parameter to `EventPump` and `ChannelActionDispatcher` constructors, which requires updating test helper calls to both classes. RESEARCH.md RC-4 notes: `CounterIncrementTests.cs:418–421` calls `dispatcher.EnqueueAsync(...)` with named params `perActionSnapshotProvider`, `subscriptionDefaultSnapshotProvider`, `ct` but omits `parallelValidators`. The test's `NoOpDispatcher` stub at line 509 includes `parallelValidators` in its signature, confirming the parameter exists. **Builder must verify at baseline (before PLAN-1.1 build) that this call site compiles under `dotnet build FrigateRelay.sln -c Release`**. If it compiles at baseline, the parameter is either passed positionally or the interface has a default (unlikely per the stub). If it fails, PLAN-1.1 Task 2 must add the `parallelValidators:` argument to line 418–421.

---

## What Builder Must Verify at Start of Build

Before executing any plan in Phase 16:

1. **Baseline compilation gate (RC-4):** Run `dotnet build FrigateRelay.sln -c Release` and confirm zero errors. Specifically verify that `CounterIncrementTests.cs:418–421` call to `dispatcher.EnqueueAsync(...)` compiles as-is. If it fails, report the error and do not proceed — this is a pre-existing condition, not introduced by Phase 16. PLAN-1.1 Task 2 notes this as RC-4 baseline-compile check.

2. **Directory creation:** Before PLAN-1.1 Task 1 adds files, ensure `src/FrigateRelay.Host/Observability/` directory exists or will be created as part of the first file write (most file systems auto-create parent dirs, but verify CI behavior).

3. **Test count baseline:** Confirm current Host.Tests count is 151 as stated in CONTEXT-16 and RESEARCH.md. Run `dotnet run --project tests/FrigateRelay.Host.Tests -c Release` and note the final pass/fail count. Phase 16 success criterion: all 151 existing pass + 3 new from PLAN-1.1 (#18) = 154 minimum.

4. **Wave 1 execution order:** Plans are parallel (no inter-plan deps), but recommend sequential execution **within each plan file pair** that shares test files (CounterIncrementTests.cs, EventPumpSpanTests.cs). Execute PLAN-1.1 first (adds MetricsTagWriter injection), then PLAN-1.2 (replaces polling within those same files). This avoids merge conflicts on shared test files. PLAN-1.3 can run in parallel.

5. **Atomic commit enforcement:** After PLAN-1.3 Task 1 modifications are complete, verify the 3-file atomic commit: `git log --oneline -1 -- src/FrigateRelay.Plugins.CodeProjectAi/PluginRegistrar.cs src/FrigateRelay.Plugins.Roboflow/PluginRegistrar.cs src/FrigateRelay.Plugins.Doods2/PluginRegistrar.cs` must show a single commit hash covering all three paths. If three separate commits appear, the build is non-compliant with D6 requirement.

---

## Summary

All three plans are **well-researched and feasible**. File paths are concrete and verified. API surfaces match codebase reality. Verification commands are runnable with valid syntax. No blocking circular dependencies exist between plans. CHANGELOG.md is the only multi-plan touch point (sequential dispatch mitigates).

**Pre-build verifications (RC-4 baseline compile + directory setup + test-count baseline) are straightforward but required.** Once confirmed, builder can execute PLAN-1.1 → PLAN-1.2 → PLAN-1.3 in order with confidence.

**Atomic 3-file commit requirement (PLAN-1.3 D6) is achievable in single `git commit` call** — recommend explicit per-file add or glob pattern.

Recommend **PASS** to builder dispatch step.
