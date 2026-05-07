# Verification Report

**Phase:** 16 — v1.3.0 Minor Release
**Date:** 2026-05-07
**Type:** plan-review (pre-execution coverage check)
**Reviewer:** verifier agent (claude-sonnet-4-6)

---

## Results

| # | Criterion | Status | Evidence |
|---|-----------|--------|----------|
| 1 | Issue #18 covered by exactly one plan | PASS | PLAN-1.1 front-matter `must_haves` and `files_touched` are exclusively #18 files. PLAN-1.2 and PLAN-1.3 front-matter contain no overlap with #18's source files (`MetricsTagWriter.cs`, `MetricsTagsOptions.cs`, `EventPump.cs`, `ChannelActionDispatcher.cs`, `HostBootstrap.cs`, `MetricsCardinalityTests.cs`). |
| 2 | Issue #22 covered by exactly one plan | PASS | PLAN-1.2 owns `CapturingLogger.cs` + the 4 observability test files. PLAN-1.1 touches `CounterIncrementTests.cs` and `EventPumpSpanTests.cs` also (for RC-5 constructor-boilerplate updates) — see finding F-1 below. |
| 3 | Issue #30 covered by exactly one plan | PASS | PLAN-1.3 owns all three `PluginRegistrar.cs` files and `CodeProjectAiPluginRegistrarTests.cs`. No other plan touches those paths. |
| 4 | PLAN-1.1 has ≤3 tasks, all with required sections | PASS | Exactly 3 tasks. Each has: Files, Action, Description, TDD flag, Acceptance Criteria. Plan has Context, Dependencies, Tasks, Verification. |
| 5 | PLAN-1.2 has ≤3 tasks, all with required sections | PASS | Exactly 2 tasks. Each has required sections. Plan has Context, Dependencies, Tasks, Verification, Notes. |
| 6 | PLAN-1.3 has ≤3 tasks, all with required sections | PASS | Exactly 3 tasks. Each has required sections. Plan has Context, Dependencies, Tasks, Verification, Notes. |
| 7 | All 3 plans declare Wave 1, no dependencies | PASS | Front-matter: all three have `wave: 1` and `dependencies: []`. |
| 8 | No two plans modify the same file except CHANGELOG.md | WARN | `CHANGELOG.md` shared — acknowledged in all three plans' Notes sections as "sequential dispatch eliminates merge friction." Additionally, PLAN-1.1 Task 2 lists `CounterIncrementTests.cs` and `EventPumpSpanTests.cs` in `files_touched`, and PLAN-1.2 Task 2 also lists those two files. This is a real file conflict — see finding F-1. |
| 9 | Acceptance criteria are testable via command/grep/invocation | PASS | Every criterion has a greppable invariant, a `dotnet run` test command, or a `dotnet build` zero-warnings check. No subjective criteria found. |
| 10 | D1 — MetricsTagWriter is a string normalizer at callers, not wrapping Counter<T>.Add | PASS | PLAN-1.1 must_have: "DispatcherDiagnostics static class is NOT modified (RC-3) — normalization happens at the callers." Task 2 description explicitly states additive overloads only, writer injected into `EventPump` + `ChannelActionDispatcher` constructors. |
| 11 | D2 — WaitForEntriesAsync on CapturingLogger<T>, field name Entries | PASS | PLAN-1.2 must_have: "Polls Entries.Count >= count at 25ms intervals." Task 1 signature uses `Entries.Count`. Acceptance criterion: "`git grep -n ' Records' tests/FrigateRelay.TestHelpers/CapturingLogger.cs` returns no false-positive references — the field remains `Entries`." |
| 12 | D3 — Case-insensitive OrdinalIgnoreCase | PASS | PLAN-1.1 must_have: "Allowlist match uses HashSet<string>(StringComparer.OrdinalIgnoreCase) per CONTEXT-16 D3." Task 1 description specifies `HashSet<string>(StringComparer.OrdinalIgnoreCase)`. CHANGELOG entry text includes "Case-insensitive (`OrdinalIgnoreCase`)". |
| 13 | D4 — Cameras-only, no KnownLabels | PASS | PLAN-1.1 Notes: "Do not introduce `KnownLabels` — D4 scopes v1.3.0 to cameras-only." `MetricsTagsOptions` record in Task 1 description has only `KnownCameras` field. |
| 14 | D5 — Config key is `Otel:MetricsTags:KnownCameras` | PASS | PLAN-1.1 Task 1 description: `services.Configure<MetricsTagsOptions>(config.GetSection("Otel:MetricsTags"))`. Task 3 CHANGELOG entry text matches D5 JSON shape from CONTEXT-16. |
| 15 | D6 — Atomic 3-file commit for #30, only CPAI backfill | PASS | PLAN-1.3 must_have: "Atomic 3-file commit (per CONTEXT-16 D6)..." and "CPAI registrar test backfill: 5 tests...". Notes section explicitly states "DOODS2 already has 5 tests (RC-2). Do NOT add Doods2PluginRegistrarTests.cs." |
| 16 | D7 — 3 plans, 1 wave | PASS | Three plan files present: PLAN-1.1.md, PLAN-1.2.md, PLAN-1.3.md. All `wave: 1`. |
| 17 | D8 — CHANGELOG sections: Added for #18, Changed for #30, Internal for #22 | PASS | PLAN-1.1 Task 3: `### Added` entry for #18. PLAN-1.2 Task 2: `### Internal` entry for #22 (with invariant-correction line). PLAN-1.3 Task 3: `### Changed` entry for #30. |
| 18 | PROJECT.md non-goal compliance — no forbidden deps introduced | PASS | All three plans involve only: `System.Text.Json` (not Newtonsoft), `IConfiguration` (not SharpConfig), MSTest/NSubstitute (no new test deps), `IHttpClientFactory`/OTel (no DotNetWorkQueue, App.Metrics, OpenTracing, Jaeger). No runtime DLL plugin discovery, no web UI, no hot-reload config changes. |
| 19 | Cardinality discipline — no event_id tag introduced | PASS | PLAN-1.1 description: `MetricsTagWriter.NormalizeCameraTag` only normalizes the `camera` string value and adds no tag keys. RESEARCH.md: "The new helper cannot accidentally introduce `event_id`." No plan adds any new tag key beyond `camera`. |
| 20 | OQ-5 correction — PLAN-1.2 uses tightened Task.Delay grep (`Task\.Delay\([0-9]`) | PASS | PLAN-1.2 must_have: "Tightened greppable invariant: `git grep -nE 'Task\\.Delay\\([0-9]'`...". Task 2 Acceptance Criteria: `git grep -nE 'Task\.Delay\([0-9]' tests/FrigateRelay.Host.Tests/Observability/` returns empty. Verification section mirrors this. The ROADMAP original `Task\.Delay` is superseded explicitly in PLAN-1.2. |
| 21 | ROADMAP SC-1: Build zero warnings | PASS | All three plans include `dotnet build FrigateRelay.sln -c Release` in Verification sections and zero-warnings Acceptance Criteria. |
| 22 | ROADMAP SC-2: 3 new MetricsCardinalityTests pass (closes #18a/b/c) | PASS | PLAN-1.1 must_have: "TDD: ≥3 tests in...MetricsCardinalityTests.cs (known-camera passthrough, unknown folded to 'other', empty-allowlist passthrough)." Task 1 specifies all three test bodies and names. |
| 23 | ROADMAP SC-2: 10 optional PluginRegistrar backfill tests pass (closes #30 surface) | PASS | PLAN-1.3 plans 5 new CPAI tests. Notes section correctly states DOODS2 already has 5 (RC-2 correction). The ROADMAP says "Optional 10" but RESEARCH clarified only 5 are net-new; PLAN-1.3 plans the 5 correctly and cites the correction. |
| 24 | ROADMAP SC-2: 4 polling-refactored sites pass (closes #22) | PASS | PLAN-1.2 must_have lists all 4 sites by file and line. Acceptance criteria include `git grep -n 'WaitForEntriesAsync'...returns exactly 4 hits`. |
| 25 | ROADMAP SC-3: Greppable invariant for #22 (corrected to numeric-delays pattern) | PASS | PLAN-1.2 carries the corrected pattern explicitly and acknowledges it as a ROADMAP correction in both Notes and CHANGELOG entry. |
| 26 | ROADMAP SC-4: App.Metrics/OpenTracing/Jaeger grep returns empty | PASS | PLAN-1.1 Verification includes `git grep -nE 'App\.Metrics|OpenTracing|Jaeger\.' src/` returning empty as an explicit check. |
| 27 | ROADMAP SC-5: event_id cardinality-bomb tripwire unchanged | PASS | PLAN-1.1 Verification includes `git grep '"event_id"' src/FrigateRelay.Host/Dispatch/` returning empty. |
| 28 | ROADMAP SC-6: No regression in counter tag shape when KnownCameras is empty | PASS | PLAN-1.1 Task 2 AC states "empty allowlist == passthrough" and existing tests get a `StaticOptionsMonitor` with empty `KnownCameras` so existing assertions are unchanged. PLAN-1.1 must_have: "passthrough when empty (preserves current behavior)". |
| 29 | ROADMAP SC-7: Atomic 3-file commit for #30 greppable invariant | PASS | PLAN-1.3 must_have and Verification include the `git log --oneline -1 -- <three paths>` check. Task 1 step 5 spells out the atomic-commit requirement for the build agent. |
| 30 | ROADMAP SC-8: README.md and docs/observability.md updated | PASS | PLAN-1.1 Task 3 covers both files. Acceptance Criteria: `grep -n KnownCameras README.md docs/observability.md CHANGELOG.md` returns ≥3 hits. |
| 31 | ROADMAP SC-9: CHANGELOG [1.3.0] section formatted correctly | PASS | PLAN-1.1/1.2/1.3 Task 3 entries are all in `[Unreleased]` (correct — the release step promotes Unreleased to 1.3.0). Added / Internal / Changed sections match D8. |
| 32 | ROADMAP SC-10: Single merged PR, then v1.3.0 tag | MANUAL | Plan review cannot verify the PR/tag cut — this is a post-build shipping step. Covered by D9 in CONTEXT-16 but not verifiable at plan-review time. |

---

## Gaps

### F-1 (WARN): File conflict on CounterIncrementTests.cs and EventPumpSpanTests.cs between PLAN-1.1 and PLAN-1.2

**Evidence:** PLAN-1.1 `files_touched` lists `tests/FrigateRelay.Host.Tests/Observability/CounterIncrementTests.cs` and `tests/FrigateRelay.Host.Tests/Observability/EventPumpSpanTests.cs` (Task 2 updates the ~10 builder methods that `new` `EventPump`/`ChannelActionDispatcher` to inject `MetricsTagWriter`). PLAN-1.2 `files_touched` also lists both files (Task 2 replaces `Task.Delay` at 4 sites in those same files).

**Impact:** If both plans are executed concurrently, the same files are modified by two separate tasks — standard wave-parallel conflict. However, all three plans' Notes sections state "CHANGELOG.md is shared with PLAN-1.2 and PLAN-1.3 — sequential dispatch eliminates merge friction." The sequential-dispatch acknowledgment in the Notes is for CHANGELOG only; the two additional shared test files are not called out.

**Severity:** Low. Sequential dispatch (the documented Phase 15-adopted strategy) resolves this automatically — PLAN-1.1 runs first (adds constructor parameter to test builders), then PLAN-1.2 runs on the result (replaces `Task.Delay` lines in the same files). But the plan documentation should state this explicitly rather than implicitly.

**Recommendation:** Architect adds a sentence to PLAN-1.2 Notes (and/or PLAN-1.1 Notes) explicitly stating: "`CounterIncrementTests.cs` and `EventPumpSpanTests.cs` are also modified by PLAN-1.1 Task 2; sequential dispatch with PLAN-1.1 before PLAN-1.2 is required for those files as well, not only CHANGELOG.md."

**Blocking?** No — sequential dispatch is the documented Phase 15 strategy and the architect has already adopted it. Builder will not run both in parallel.

### F-2 (NOTE): PLAN-1.3 Task 2 Step 4 — DynamicProxyGenAssembly2 check is conditional

**Evidence:** PLAN-1.3 Task 2 step 4: "If NSubstitute is used to mock any `internal` types (e.g., the validator interface), the `CodeProjectAi.Tests.csproj` must already have `<InternalsVisibleTo Include="DynamicProxyGenAssembly2" />`... If missing, add it." RESEARCH.md confirms `tests/FrigateRelay.Plugins.CodeProjectAi.Tests/` currently has only `CodeProjectAiValidatorTests.cs` — the builder needs to check that csproj before writing tests that mock internal types.

**Impact:** Minor. If the csproj lacks the entry and NSubstitute is used, the build fails with NS2003. The plan correctly flags the check; the conditional instruction is clear.

**Severity:** Non-blocking. Plan is correct; it places the responsibility on the builder to inspect and add if missing.

### F-3 (NOTE): ROADMAP SC-10 (PR/tag cut) is unverifiable at plan-review time

**Evidence:** The ROADMAP success criterion "One merged PR on `main`, then `v1.3.0` tag" cannot be verified until after the build phase and PR. Marked MANUAL in the results table.

---

## File-to-Plan Ownership Map

| File | Owner Plan | Conflict? |
|---|---|---|
| `src/FrigateRelay.Host/Observability/MetricsTagWriter.cs` (new) | PLAN-1.1 | None |
| `src/FrigateRelay.Host/Observability/MetricsTagsOptions.cs` (new) | PLAN-1.1 | None |
| `src/FrigateRelay.Host/EventPump.cs` | PLAN-1.1 | None |
| `src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs` | PLAN-1.1 | None |
| `src/FrigateRelay.Host/HostBootstrap.cs` | PLAN-1.1 | None |
| `tests/FrigateRelay.Host.Tests/Observability/MetricsCardinalityTests.cs` (new) | PLAN-1.1 | None |
| `tests/FrigateRelay.Host.Tests/Observability/CounterIncrementTests.cs` | PLAN-1.1 (Task 2, boilerplate) **+ PLAN-1.2 (Task 2, delay sites)** | Sequential conflict — see F-1 |
| `tests/FrigateRelay.Host.Tests/Observability/EventPumpSpanTests.cs` | PLAN-1.1 (Task 2, boilerplate) **+ PLAN-1.2 (Task 2, delay sites)** | Sequential conflict — see F-1 |
| `docs/observability.md` | PLAN-1.1 | None |
| `README.md` | PLAN-1.1 | None |
| `tests/FrigateRelay.TestHelpers/CapturingLogger.cs` | PLAN-1.2 | None |
| `src/FrigateRelay.Plugins.CodeProjectAi/PluginRegistrar.cs` | PLAN-1.3 | None |
| `src/FrigateRelay.Plugins.Roboflow/PluginRegistrar.cs` | PLAN-1.3 | None |
| `src/FrigateRelay.Plugins.Doods2/PluginRegistrar.cs` | PLAN-1.3 | None |
| `tests/FrigateRelay.Plugins.CodeProjectAi.Tests/CodeProjectAiPluginRegistrarTests.cs` (new) | PLAN-1.3 | None |
| `CHANGELOG.md` | PLAN-1.1 + PLAN-1.2 + PLAN-1.3 (shared additive) | Acknowledged sequential-dispatch |

---

## Issue Coverage Table

| Issue ID | Description | Covered By | Tasks |
|---|---|---|---|
| #18 | `Otel:MetricsTags:KnownCameras` allowlist + `MetricsTagWriter` | PLAN-1.1 | Task 1 (types + DI), Task 2 (callers + test builders), Task 3 (docs + CHANGELOG) |
| #22 | Replace 4 `Task.Delay` fragility sites with `WaitForEntriesAsync` | PLAN-1.2 | Task 1 (helper method), Task 2 (4 site replacements + CHANGELOG) |
| #30 | Atomic 3-file PluginRegistrar HttpClient shape unification + CPAI backfill | PLAN-1.3 | Task 1 (3-file refactor), Task 2 (5 CPAI tests), Task 3 (CHANGELOG) |

No gaps — all 3 issues covered exactly once.

---

## ROADMAP Success-Criterion Table

| ROADMAP Criterion | Covering Plan/Task | Status |
|---|---|---|
| `dotnet build FrigateRelay.sln -c Release` zero warnings on Linux + Windows | All three plans' Verification sections | Covered |
| All existing tests pass; 3 new MetricsCardinalityTests pass (#18a/b/c) | PLAN-1.1 Task 1 | Covered |
| Optional 10 PluginRegistrar backfill tests pass (#30 surface coverage) | PLAN-1.3 Task 2 (5 CPAI only; DOODS2 already has 5 per RC-2) | Covered (correctly scoped to 5 net-new per RESEARCH.md) |
| 4 polling-refactored test sites pass (#22) | PLAN-1.2 Task 2 | Covered |
| `git grep -nE 'Task\.Delay' tests/FrigateRelay.Host.Tests/Observability/` returns empty | PLAN-1.2 corrects this to `Task\.Delay\([0-9]` pattern per OQ-5 | Covered with documented correction |
| `git grep -nE 'App\.Metrics\|OpenTracing\|Jaeger\.' src/` returns empty | PLAN-1.1 Verification | Covered |
| `git grep '"event_id"' src/FrigateRelay.Host/Dispatch/` returns empty | PLAN-1.1 Verification | Covered |
| No regression in counter tag shape when KnownCameras is empty | PLAN-1.1 Task 2 AC (passthrough = empty allowlist) | Covered |
| Atomic 3-file commit for #30 (`git log` invariant) | PLAN-1.3 Task 1 must_have + Verification | Covered |
| README.md + docs/observability.md updated with KnownCameras example | PLAN-1.1 Task 3 | Covered |
| CHANGELOG [1.3.0]: #18 in Added, #22+#30 in Internal/Changed, [Unreleased] empty after release | PLAN-1.1/1.2/1.3 Task 3 (each) | Covered |
| Single merged PR on main, then v1.3.0 tag | CONTEXT-16 D9; cannot verify at plan-review | MANUAL |

---

## Recommendations

1. **F-1 (low priority):** Update PLAN-1.1 and/or PLAN-1.2 Notes to explicitly name `CounterIncrementTests.cs` and `EventPumpSpanTests.cs` as additional sequential-dispatch dependencies (not just CHANGELOG.md). The builder will likely infer this correctly, but clarity prevents ambiguity. This does not require re-sending the plan to the architect; a verbal note to the build agent is sufficient.

2. **F-3 (informational):** ROADMAP SC-10 (PR/tag) is a post-build shipping step. Mark as MANUAL in build verification; verifier will check during ship verification.

3. **No blocking architectural rework needed.** All design decisions D1–D9 are faithfully reflected. All 3 issues covered without overlap. Both file conflicts are managed via the documented sequential-dispatch strategy.

---

## Verdict

**PASS (with 1 WARN)**

All three Phase 16 plans cover their respective issue requirements completely. The 3 issues (#18, #22, #30) are covered exactly once each with no double-coverage. Plan structure is conformant (≤3 tasks, all required sections, all acceptance criteria are greppable/runnable). The OQ-5 invariant tightening is correctly implemented in PLAN-1.2. The single WARN (F-1) is a documentation gap — `CounterIncrementTests.cs` and `EventPumpSpanTests.cs` appear in both PLAN-1.1 and PLAN-1.2 `files_touched` without explicit sequential-ordering language for those specific files. Sequential dispatch resolves the conflict operationally, but the plans acknowledge sequential ordering only for CHANGELOG.md. No architect rework required; builder should be made aware of the sequential requirement for those two test files.
