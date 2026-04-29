# Phase 9 — Plan Quality Verification (Step 6)

**Date:** 2026-04-27  
**Verifier:** Shipyard Plan Quality Check (Structure + Coverage + Feasibility)  
**Mode:** Pre-Build Plan Verification  
**Plans Reviewed:** PLAN-1.1, PLAN-2.1, PLAN-2.2, PLAN-3.1  

---

## Verdict: PASS

All structural, coverage, and feasibility checks pass. Four plans are well-structured, cover all phase requirements and decisions, have no file conflicts, and contain concrete, measurable acceptance criteria.

---

## A. ROADMAP Coverage — PASS

All 5 Phase 9 deliverables are explicitly addressed by at least one plan task:

| Deliverable | Plan | Task | Evidence |
|---|---|---|---|
| Serilog wiring (Console, File, optional Seq) | PLAN-2.2 | Task 2 (UseSerilog) + Task 1 (config sections) | `/mnt/f/git/frigaterelay/.shipyard/phases/9/plans/PLAN-2.2.md` lines 83–127 |
| OTel registration (conditional OTLP exporter) | PLAN-2.2 | Task 3 (AddOpenTelemetry) | `/mnt/f/git/frigaterelay/.shipyard/phases/9/plans/PLAN-2.2.md` lines 129–194 |
| ActivitySource + 5 spans (mqtt.receive, event.match, dispatch.enqueue, action.<name>.execute, validator.<name>.check) | PLAN-2.1 | Tasks 1–2 (EventPump + ChannelActionDispatcher instrumentation) | `/mnt/f/git/frigaterelay/.shipyard/phases/9/plans/PLAN-2.1.md` lines 45–183 |
| Meter + 8 counters (events.received, events.matched, actions.*, validators.*, errors.unhandled, + 2 pre-existing) | PLAN-1.1 | Task 2 (DispatcherDiagnostics counter declarations) | `/mnt/f/git/frigaterelay/.shipyard/phases/9/plans/PLAN-1.1.md` lines 67–105 |
| tests/FrigateRelay.Host.Tests/Observability + IntegrationTests/Observability | PLAN-3.1 | Tasks 1–3 (unit + integration test suites) | `/mnt/f/git/frigaterelay/.shipyard/phases/9/plans/PLAN-3.1.md` lines 39–184 |

**3 ROADMAP success criteria also covered:**
1. Integration test `TraceSpans_CoverFullPipeline` — PLAN-3.1 Task 3 (lines 109–126).
2. Metric test assertion — PLAN-3.1 Task 3 (lines 128–142).
3. Excluded package grep — PLAN-1.1 Task 1 + PLAN-2.2 Task 3 + PLAN-3.1 verification.

---

## B. Decision Coverage (D1–D9) — PASS

All 9 CONTEXT-9 decisions have explicit plan assignments with no gaps:

| Decision | Summary | Plan/Task | Verification in plans |
|----------|---------|-----------|----------------------|
| D1 | DispatchItem.ActivityContext (struct) + ActivityKind.Consumer | PLAN-1.1 Task 3 | ✓ Lines 106–132 specify the field flip + read/write sites. `grep -nE 'ActivityContext\s+ParentContext'` acceptance criterion. |
| D2 | OTel registered always; AddOtlpExporter conditional on endpoint | PLAN-2.2 Task 3 | ✓ Lines 136–159 show conditional `if (!string.IsNullOrWhiteSpace(otlpEndpoint))` guard. |
| D3 | Counter tags: subscription, action, validator, camera, label; errors.unhandled tagless | PLAN-1.1 Task 2 + PLAN-2.1 Tasks 1–2 | ✓ Task 2 defines 8 counters with tag-key XML docs (lines 67–105); Tasks 1–2 emit site TagList calls (PLAN-2.1 lines 59, 74, 87, 141–155). |
| D4 | ID-6 fix: graceful shutdown = ActivityStatusCode.Unset, never Error | PLAN-2.1 Task 2 | ✓ Lines 162–170 show `when (ct.IsCancellationRequested)` guard + `SetStatus(ActivityStatusCode.Unset)`. |
| D5 | Unit tests Host.Tests/Observability; integration IntegrationTests/Observability | PLAN-3.1 Tasks 1–3 | ✓ Task 1 specifies `tests/FrigateRelay.Host.Tests/Observability/` (lines 41–71); Task 3 specifies `tests/FrigateRelay.IntegrationTests/Observability/` (lines 99–149). |
| D6 | Keep hand-rolled `Action<ILogger,...>` delegates; new EventIds 500–599 | PLAN-2.1 Task 1 | ✓ Lines 96 notes "match existing style"; acceptance criterion line 105 checks `LoggerMessage.Define` use 500–599 range. |
| D7 | Serilog Seq sink conditional on Serilog:Seq:ServerUrl | PLAN-2.2 Tasks 1–2 | ✓ Task 1 (lines 39–70) sets `ServerUrl: ""` default; Task 2 (lines 106–108) shows `if (!string.IsNullOrWhiteSpace(seqUrl)) lc.WriteTo.Seq(seqUrl)`. |
| D8 | Span attribute table (finalized by architect) | PLAN-2.1 Context section | ✓ Lines 24–37 provide detailed span/attribute table with sources and notes on naming. |
| D9 | errors.unhandled at single top-level catch only | PLAN-2.1 Task 1 + PLAN-3.1 Task 2 | ✓ Task 1 (lines 89–94) enforces single-site in PumpAsync; Task 2 test `ErrorsUnhandled_DoesNotIncrement_OnRetryExhaustion` (line 89) enforces D9 semantic. |

---

## C. Plan Structure — PASS

### YAML Frontmatter
All 4 plans have valid YAML headers with required fields:
- PLAN-1.1: `phase: phase-9-observability`, `plan: 1.1`, `wave: 1`, `dependencies: []`
- PLAN-2.1: `phase: phase-9-observability`, `plan: 2.1`, `wave: 2`, `dependencies: [PLAN-1.1]`
- PLAN-2.2: `phase: phase-9-observability`, `plan: 2.2`, `wave: 2`, `dependencies: [PLAN-1.1]`
- PLAN-3.1: `phase: phase-9-observability`, `plan: 3.1`, `wave: 3`, `dependencies: [PLAN-2.1, PLAN-2.2]`

✓ All present; no parse errors in YAML.

### Wave Dependency Graph
- Wave 1 (PLAN-1.1): No dependencies. ✓
- Wave 2 (PLAN-2.1, PLAN-2.2): Both depend on PLAN-1.1 only; can run in parallel. ✓
- Wave 3 (PLAN-3.1): Depends on both PLAN-2.1 and PLAN-2.2. ✓
- **No circular dependencies. No forward references.** ✓

### Task Counts (≤3 per plan rule)
- PLAN-1.1: 3 tasks (verify via `grep -c "^### Task "`). ✓
- PLAN-2.1: 3 tasks. ✓
- PLAN-2.2: 3 tasks. ✓
- PLAN-3.1: 3 tasks. ✓

### Section Structure
All plans include required sections:
- Context (explains why the plan exists, decision binding)
- Dependencies
- Tasks (each with Description, Acceptance Criteria)
- Verification (copy-pasteable shell commands)

✓ All sections present across all plans.

---

## D. File Disjointness (Wave 2 Parallel Safety) — PASS

**PLAN-2.1 files_touched** (from lines 11–13):
```
- src/FrigateRelay.Host/Pipeline/EventPump.cs
- src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs
- .shipyard/ISSUES.md (Task 3)
```

**PLAN-2.2 files_touched** (from lines 12–16):
```
- src/FrigateRelay.Host/HostBootstrap.cs
- src/FrigateRelay.Host/Program.cs
- src/FrigateRelay.Host/appsettings.json
- src/FrigateRelay.Host/Configuration/StartupValidation.cs
```

**Overlap:** None. PLAN-2.1 and PLAN-2.2 touch disjoint files and can land in any order. ✓

**Note on `.shipyard/ISSUES.md`:** PLAN-2.1 Task 3 modifies this file to close ID-6. PLAN-2.2 does not touch it. Single-modifier rule holds. ✓

---

## E. Acceptance Criteria Quality — PASS

Every plan task has concrete, measurable acceptance criteria (no vague language like "looks good" or "is reasonable"):

### PLAN-1.1
- **Task 1:** `dotnet build ... succeeds with zero warnings`; `dotnet list ... package` shows 8 new packages at pinned versions; `git grep -nE 'App\.Metrics|...'` returns zero. ✓ Runnable commands.
- **Task 2:** `grep -c 'CreateCounter<long>'` returns `10`; `grep -nE '^internal static readonly Counter<long> ...'` returns 8. ✓ Measurable.
- **Task 3:** `grep -nE 'Activity\?\s+Activity'` returns zero; `grep -nE 'ActivityContext\s+ParentContext'` returns one; `grep -nE 'item\.Activity\b'` returns zero; `dotnet build` clean. ✓ Testable.

### PLAN-2.1
- **Task 1:** `grep -n 'StartActivity("mqtt.receive"'` returns one; `grep -nE 'EventsReceived\.Add|EventsMatched\.Add|ActionsDispatched\.Add'` returns ≥3; `grep -n 'ErrorsUnhandled.Add'` returns exactly one. ✓ Precise counts.
- **Task 2:** `grep -nE 'StartActivity\(\$"action\.'` returns one; `grep -n '"ActionDispatch"'` returns zero; `grep -n 'ActivityStatusCode.Unset'` returns ≥1. ✓ Presence/absence checks.
- **Task 3:** `grep -A 3 '^### ID-6' .shipyard/ISSUES.md | grep -i 'closed'` returns match; `grep -A 3 '^### ID-13|...' | grep -ic 'closed'` returns zero. ✓ Status validation.

### PLAN-2.2
- **Task 1:** JSON validation script (`python3 -c 'import json...'`); `bash .github/scripts/secret-scan.sh` succeeds; `dotnet build` clean. ✓ Executable.
- **Task 2:** `grep -n 'UseSerilog'` returns ≥1; `grep -n 'WriteTo.Console'` / `WriteTo.File` / `WriteTo.Seq'` all ≥1; `dotnet run --project src/FrigateRelay.Host ... smoke test within 5 seconds`. ✓ Measurable + smoke test.
- **Task 3:** `grep -n 'AddOpenTelemetry'` returns one; `grep -n 'AddSource("FrigateRelay")'` / `AddMeter` / `AddRuntimeInstrumentation` / `AddOtlpExporter` all present; negative test: `Otel__OtlpEndpoint=not-a-uri` causes non-zero exit. ✓ Negative test included.

### PLAN-3.1
- **Task 1:** `dotnet build` clean; `dotnet run --project tests/FrigateRelay.Host.Tests` reports ≥6 new passing tests; `grep -n 'OpenTelemetry.Exporter.InMemory' src/` returns zero; `grep -n 'ExportProcessorType.Simple'` returns ≥1. ✓ Test count + test-only dep enforcement.
- **Task 2:** `dotnet run --project tests/FrigateRelay.Host.Tests` reports ≥9 new passing tests; specific test filter `ErrorsUnhandled_DoesNotIncrement_OnRetryExhaustion` passes; all tests use shared `CapturingLogger<T>`. ✓ Convention enforcement.
- **Task 3:** `dotnet build` clean; `dotnet run --project tests/FrigateRelay.IntegrationTests` reports ≥2 new tests, total runtime <30s; `grep -n 'TraceSpans_CoverFullPipeline'` returns match; `git grep -nE 'App\.Metrics|...'` returns zero. ✓ SLO + test naming.

**No vague criteria found. All criteria are runnable or directly inspectable.**

---

## F. Additional Hard Rules Enforcement — PASS

| Rule | Enforced by | Status |
|------|-----------|--------|
| No App.Metrics, OpenTracing, Jaeger.* | PLAN-1.1 Task 1 + PLAN-2.2 Task 3 + PLAN-3.1 verification | ✓ Grep checks in all 3 locations |
| No Serilog.AspNetCore (Worker SDK rule) | PLAN-1.1 Task 1 + PLAN-2.2 Task 2 | ✓ Explicit "Do NOT add" lines 60, 123 |
| No OpenTelemetry.Exporter.Console | PLAN-1.1 Task 1 line 57 + PLAN-2.2 Task 3 line 165 | ✓ Explicit exclusion + acceptance criterion |
| No OpenTelemetry.Exporter.InMemory in src/ | PLAN-3.1 Task 1 line 49 + verification line 149 | ✓ Test-only restriction + grep check |
| Phase 8 visibility (no public Host types) | PLAN-1.1 Task 2 line 104 + PLAN-2.1 verification line 225 | ✓ Grep for `^public` / `^protected` |
| Shared CapturingLogger<T> usage | PLAN-3.1 Task 1 line 64 + Task 2 line 96 + final verification line 182 | ✓ Convention enforced + no-redefine rule |
| Hand-rolled logging delegates (D6) | PLAN-2.1 Task 1 lines 96–105 (manual review checkpoint) | ✓ Explicit reference to existing pattern |
| EventIds 500–599 for new logs | PLAN-2.1 Task 1 line 105 acceptance criterion | ✓ Range check in verification |
| No secrets in appsettings.json | PLAN-2.2 Task 1 lines 72–75 + secret-scan tripwire | ✓ Empty defaults + CI integration |
| errors.unhandled single-site (D9) | PLAN-2.1 Task 1 line 103 + PLAN-3.1 Task 2 line 89 | ✓ Enforcement at both definition and test sites |

All hard rules are explicitly stated and enforced via acceptance criteria.

---

## G. Open Questions from Architect — Clarified

The architect's pre-build VERIFICATION.md surfaced 3 open questions. Review status:

1. **`subscription` tag source on DispatchItem** — PLAN-2.1 Task 2 notes (lines 122–123) that if `DispatchItem` does not carry subscription name, a new field must be added. The plan correctly bounds this as a builder-discretion fix-forward. Status: **Acknowledged but deferred to builder implementation phase.** No plan revision needed; this is within expected builder flexibility.

2. **`mqtt.receive` boundary location** — PLAN-2.1 Task 1 (lines 45–59) locks the span in `EventPump.PumpAsync` with explicit justification ("avoid coupling a source plugin to DispatcherDiagnostics"). Status: **Frozen per architect intent.** Verification command will confirm the span is present in EventPump. ✓

3. **Validator span parenting** — PLAN-3.1 Task 3 (line 125) asserts validator spans are parented to `action.<name>.execute` via nested `using` blocks in PLAN-2.1 Task 2. The integration test assertion (line 125) will fail if parenting deviates. Status: **Contract explicit; integration test serves as sentinel.** ✓

---

## H. Test Count Projection — PASS

**Phase 8 baseline:** 69 unit tests in `Host.Tests` (per architect's pre-build VERIFICATION).

**Phase 9 net new (PLAN-3.1 targets):**
- ✓ Task 1 (DispatcherDiagnostics + EventPump spans): ≥6 tests
- ✓ Task 2 (Counter increments): ≥9 tests  
- ✓ Task 3 (Integration traces + counters): ≥2 tests
- **Total: ≥17 net new unit + integration tests**

**Phase 9 target Host.Tests count:** 69 + 15 (unit) = **84**. PLAN-3.1 verification block implicitly enforces this.

**Phase 9 target IntegrationTests count:** 1 (Phase 7) + 2 (Phase 9) = **≥3**. PLAN-3.1 Task 3 acceptance criterion line 146.

---

## Findings

### No Critical Issues

All structural, coverage, and feasibility checks PASS. No gaps in decision coverage, no file conflicts, no vague acceptance criteria, no missing sections.

### Minor Notes (Non-Blocking)

1. **Manual review checkpoints:** PLAN-2.1 Task 1 line 105 requires manual inspection of new EventId ranges (500–599). This is expected for logging consistency reviews. ✓ Noted in acceptance criteria.

2. **Builder discretion (DispatchItem.Subscription field):** PLAN-2.1 Task 2 defers the decision to add a `Subscription` field on `DispatchItem` to the builder, pending verification of current state. This is appropriate scope-creep avoidance. ✓ Bounded.

3. **Negative test (PLAN-2.2 Task 3):** The malformed endpoint test (`Otel__OtlpEndpoint=not-a-uri`) requires a manual smoke run. This is standard practice for config validation. ✓ Acceptable.

---

## Verdict: PASS

**All four plans are ready for build execution.**

- ✅ ROADMAP Phase 9 coverage: 5 deliverables + 3 success criteria all assigned.
- ✅ CONTEXT-9 D1–D9 all traced to explicit plan/task owners.
- ✅ Wave dependency graph is acyclic; Wave 2 (PLAN-2.1, PLAN-2.2) can execute in parallel.
- ✅ File disjointness within Wave 2 — no commit-order constraints.
- ✅ Task counts: all plans ≤3 tasks; total 12 tasks.
- ✅ Acceptance criteria: concrete, measurable, runnable; zero vague language.
- ✅ Hard rules enforced: excluded packages, visibility invariants, logging style, test-only deps.
- ✅ Test count projection: 69 → ≥86 (17+ net new tests).
- ✅ ID-6 closure path explicit (PLAN-2.1 Tasks 2–3).

**Builder may proceed to execution.** No revisions required.

---

## Appendix: Spot-Check Verification Commands

(These commands verify the above findings can be re-run post-build by the post-build verifier:)

```bash
# Check all plans have valid YAML headers
grep -c "^---" .shipyard/phases/9/plans/PLAN-*.md
# Expect: 8 (opening + closing --- per plan × 4 plans)

# Check task counts
grep -c "^### Task " .shipyard/phases/9/plans/PLAN-*.md
# Expect: PLAN-1.1.md:3, PLAN-2.1.md:3, PLAN-2.2.md:3, PLAN-3.1.md:3

# Check Wave 2 file disjointness
echo "=== PLAN-2.1 files ===" && sed -n '/^files_touched:/,/^[a-z]/p' .shipyard/phases/9/plans/PLAN-2.1.md
echo "=== PLAN-2.2 files ===" && sed -n '/^files_touched:/,/^[a-z]/p' .shipyard/phases/9/plans/PLAN-2.2.md
# (Manual visual: zero overlap expected)

# Check decision references (sample)
grep "D[1-9]" .shipyard/phases/9/plans/PLAN-*.md | wc -l
# Expect: many (all 9 decisions referenced multiple times across plans)

# Check for vague criteria (should be zero)
grep -i "looks\|seems\|reasonable\|should be\|appears" .shipyard/phases/9/plans/PLAN-*.md | wc -l
# Expect: 0
```

---

**Prepared by:** Shipyard Verifier (Plan Quality Check)  
**Date:** 2026-04-27  
**Next step:** Execute builds per Wave order (Wave 1 → Wave 2 parallel → Wave 3).
