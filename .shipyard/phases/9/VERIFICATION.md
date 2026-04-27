# Phase 9 — Pre-Build VERIFICATION

**Phase.** 9 — Observability
**Status.** Plans authored 2026-04-27. Pre-build verification — to be re-run by post-build verifier after builds complete.
**Plans authored.** PLAN-1.1, PLAN-2.1, PLAN-2.2, PLAN-3.1.

---

## ROADMAP success criteria coverage matrix

ROADMAP Phase 9 lists three verifiable success criteria. Each must map to a plan.

| # | ROADMAP success criterion | Plan that lands it | Verifying test/command |
|---|---------------------------|--------------------|------------------------|
| 1 | Integration test `TraceSpans_CoverFullPipeline` asserts one root span per MQTT event with 4 expected child spans, all under the root activity id. | **PLAN-3.1** Task 3 | `dotnet run --project tests/FrigateRelay.IntegrationTests -c Release -- --filter-query "/*/*/TraceSpansCoverFullPipelineTests/TraceSpans_CoverFullPipeline"` |
| 2 | Metric test asserts that one matched event dispatched to two actions (one validated, one not) increments `events.received=1`, `events.matched=1`, `actions.dispatched=2`, `actions.succeeded=2`, `validators.passed=1`. | **PLAN-3.1** Task 3 (`Counters_Increment_PerD3_TagDimensions`) — anchored by per-counter unit tests in **PLAN-3.1** Task 2 | `dotnet run --project tests/FrigateRelay.IntegrationTests -c Release` |
| 3 | `git grep -nE 'App\.Metrics\|OpenTracing\|Jaeger\.' src/` returns zero matches. | **PLAN-1.1** (no excluded packages added) + **PLAN-2.2** (no excluded exporters wired) — verified by every plan's verification block. | `git grep -nE 'App\.Metrics\|OpenTracing\|Jaeger\.' src/` |

All 3 ROADMAP criteria are covered.

---

## CONTEXT-9 decision coverage matrix (D1–D9)

| Decision | Summary | Plan + Task that lands it | Lands in (file:rough-line) |
|----------|---------|---------------------------|----------------------------|
| D1 | `DispatchItem` carries `ActivityContext` (struct) instead of `Activity?`; consumer span uses `ActivityKind.Consumer`. | PLAN-1.1 Task 3 | `src/FrigateRelay.Host/Dispatch/DispatchItem.cs:25` (positional record param flipped); `src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs:~150` (write site), `~173` (read site, `ActivityKind.Consumer`). |
| D2 | OTel registered always; `AddOtlpExporter` only when `Otel:OtlpEndpoint` / `OTEL_EXPORTER_OTLP_ENDPOINT` non-empty. | PLAN-2.2 Task 3 | `src/FrigateRelay.Host/HostBootstrap.cs` (`AddOpenTelemetry` block, conditional `AddOtlpExporter` guarded by `string.IsNullOrWhiteSpace`). |
| D3 | Counter tags: `subscription`, `action`, `validator`, `camera`, `label`. `errors.unhandled` is tagless. | PLAN-1.1 Task 2 (declarations w/ tag-key XML docs); PLAN-2.1 Tasks 1 + 2 (emit-site `TagList` calls). | `src/FrigateRelay.Host/Dispatch/DispatcherDiagnostics.cs` + `Pipeline/EventPump.cs` + `Dispatch/ChannelActionDispatcher.cs`. |
| D4 | ID-6 fix: graceful shutdown sets `ActivityStatusCode.Unset`, never `Error`. | PLAN-2.1 Task 2 | `src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs:~238`. |
| D5 | Unit tests in `Host.Tests/Observability/`; integration in `IntegrationTests/Observability/`. | PLAN-3.1 Tasks 1, 2, 3 | `tests/FrigateRelay.Host.Tests/Observability/`, `tests/FrigateRelay.IntegrationTests/Observability/`. |
| D6 | Keep hand-rolled `Action<ILogger,...>` delegates. New EventIds 500–599. | PLAN-2.1 Task 1 (manual review check) | Any new `LoggerMessage.Define` calls in `EventPump.cs` / `ChannelActionDispatcher.cs`. |
| D7 | Serilog Seq sink registered only when `Serilog:Seq:ServerUrl` non-empty. | PLAN-2.2 Task 2 + Task 1 (config default `""`) | `src/FrigateRelay.Host/Program.cs` (or `HostBootstrap`) `if (!string.IsNullOrWhiteSpace(seqUrl)) lc.WriteTo.Seq(...)`; `appsettings.json` `Serilog:Seq:ServerUrl: ""`. |
| D8 | Span attribute table — finalized by architect in PLAN-2.1 (table at top of plan). | PLAN-2.1 (entire plan) | `Pipeline/EventPump.cs` + `Dispatch/ChannelActionDispatcher.cs`. |
| D9 | `errors.unhandled` increments only at `EventPump.PumpAsync`'s top-level catch (single tagless series). | PLAN-2.1 Task 1 (single-site enforcement); PLAN-3.1 Task 2 (`ErrorsUnhandled_DoesNotIncrement_OnRetryExhaustion` test). | `src/FrigateRelay.Host/Pipeline/EventPump.cs` outermost `catch (Exception ex)` block. |

All 9 decisions have an explicit plan owner.

---

## Plan structure findings

### Wave ordering rationale
- **Wave 1 (PLAN-1.1):** Foundation — package refs, counter declarations, `DispatchItem` field flip. No file-level dependencies on any other plan; everything Wave 2 needs to compile/run is in place after Wave 1.
- **Wave 2 (PLAN-2.1, PLAN-2.2 — parallel):** Producer/consumer instrumentation (PLAN-2.1) and host wiring (PLAN-2.2). Disjoint file sets — see "File disjointness" below.
- **Wave 3 (PLAN-3.1):** Tests. Depends on both Wave 2 plans because integration test exercises the full host wiring (PLAN-2.2) plus the spans/counters (PLAN-2.1).

### File disjointness (parallel-safe within wave)
- Wave 1: only PLAN-1.1; trivially disjoint.
- Wave 2:
  - PLAN-2.1 touches: `Pipeline/EventPump.cs`, `Dispatch/ChannelActionDispatcher.cs`, `.shipyard/ISSUES.md`.
  - PLAN-2.2 touches: `HostBootstrap.cs`, `Program.cs`, `appsettings.json`, `Configuration/StartupValidation.cs`.
  - **Zero file overlap.** Plans can land in either order.
- Wave 3: only PLAN-3.1.

### Task counts (≤3 per plan)
- PLAN-1.1: 3 tasks. ✅
- PLAN-2.1: 3 tasks. ✅
- PLAN-2.2: 3 tasks. ✅
- PLAN-3.1: 3 tasks. ✅

### Total plan count: 4 plans, ≤5 limit. ✅

---

## Issue closure coverage

| Issue ID | Status target | Plan that closes it |
|----------|---------------|---------------------|
| ID-6 (`OperationCanceledException` → `ActivityStatusCode.Error` bug) | Closed in Phase 9 per CONTEXT-9 D4. | **PLAN-2.1 Task 2** (one-line fix) + **PLAN-2.1 Task 3** (status update in `.shipyard/ISSUES.md`). |
| ID-13, ID-14, ID-15 | Out of scope (CONTEXT-9 "Out of scope" section) — explicitly NOT touched. | None — pre-build verifier confirms `.shipyard/ISSUES.md` ID-13/14/15 entries remain Open. |

---

## Acceptance criteria quality audit

Every Phase 9 plan's tasks include `Acceptance Criteria:` with at least one runnable command (`grep`, `dotnet build`, `dotnet run --project tests/...`, `python3 -c '...'`). Subjective criteria ("looks correct") are not used. Spot-check:

- PLAN-1.1 Task 2: `grep -c 'CreateCounter<long>' .../DispatcherDiagnostics.cs` returns `10`. ✅ measurable.
- PLAN-2.1 Task 2: `grep -n '"ActionDispatch"' .../ChannelActionDispatcher.cs` returns zero (legacy span name removed). ✅ measurable.
- PLAN-2.2 Task 3: negative test — set `Otel__OtlpEndpoint=not-a-uri` env var, run host, expect non-zero exit. ✅ runnable.
- PLAN-3.1 Task 3: `dotnet run --project tests/FrigateRelay.IntegrationTests -c Release -- --filter-query ".../TraceSpans_CoverFullPipeline"` ✅ runnable.

---

## Hard rules enforced across all plans

| Hard rule | Where enforced |
|-----------|----------------|
| No `App.Metrics`, `OpenTracing`, `Jaeger.*` reintroduced | PLAN-1.1 Task 1 grep verification + PLAN-2.2 Task 3 grep + PLAN-3.1 verification. |
| No `Serilog.AspNetCore` (Worker SDK uses `Serilog.Extensions.Hosting`) | PLAN-1.1 + PLAN-2.2 grep checks. |
| No `OpenTelemetry.Exporter.Console` | PLAN-1.1 Task 1 + PLAN-2.2 Task 3. |
| No `OpenTelemetry.Exporter.InMemory` in `src/` (test-only) | PLAN-3.1 explicit `grep -rn 'OpenTelemetry.Exporter.InMemory' src/` returns zero. |
| All new types `internal`/`internal sealed`/`internal static` (Phase 8 visibility sweep) | PLAN-1.1 Task 2 explicit `grep -E '^(public\|protected) '` returns zero in modified Host source files. |
| Use shared `CapturingLogger<T>` from `FrigateRelay.TestHelpers` | PLAN-3.1 Task 1 + final verification grep. |
| EventIds for new logging delegates in 500–599 range | PLAN-2.1 Task 1 manual review + RESEARCH.md §1 cross-reference. |
| No hard-coded IPs/hostnames in `appsettings.json` | PLAN-2.2 Task 1 (defaults `""`) + Phase 2 secret-scan tripwire still passes. |
| `errors.unhandled` increments at exactly one site (D9) | PLAN-2.1 Task 1 + PLAN-3.1 Task 2 enforcing test. |

---

## Test count baseline + targets

- **Phase 8 baseline:** 69 unit tests in `Host.Tests` (per Phase 8 SUMMARY/REVIEW results).
- **Phase 9 net new (PLAN-3.1):** ≥ 6 span tests + ≥ 9 counter tests + ≥ 2 integration tests = **≥ 17 net new tests**.
- **Phase 9 target Host.Tests count:** ≥ 84 (69 + 15 unit) — PLAN-3.1 verification block enforces this implicitly via the per-task ≥ counts.
- **Phase 9 target IntegrationTests count:** ≥ 3 (existing 1 from Phase 7 + 2 new from Phase 9 PLAN-3.1 Task 3).

---

## Open questions surfaced for builder/reviewer

1. **`subscription` tag source on `DispatchItem`** — PLAN-2.1 Task 2 notes that if `DispatchItem` does not currently carry the subscription name, it must be added as an additive positional record param. The architect did not confirm the current `DispatchItem` field set carries it. Builder must verify `DispatchItem.cs` after PLAN-1.1 Task 3 lands and add the field if missing. This is bounded scope (single field), so left as a builder-discretion fix-forward rather than a separate plan.

2. **`mqtt.receive` boundary location** — RESEARCH.md §10 surfaced two options (in `FrigateMqttEventSource` vs. in `EventPump`). PLAN-2.1 Task 1 ARCHITECT-DECIDED: span starts at `EventPump.PumpAsync` to avoid coupling a source plugin to the host's `DispatcherDiagnostics`. This decision is FROZEN; builder must not move the span into the source plugin.

3. **Validator span parenting** — PLAN-3.1 Task 3 asserts the validator span is parented to `action.<name>.execute`, not to `dispatch.enqueue`. PLAN-2.1 Task 2's `using` blocks naturally produce this parenting because the validator-loop `using` is nested inside the action `using`. If implementation in PLAN-2.1 deviates, the integration test will fail and PLAN-2.1 must be revisited.

---

## Final pre-build readiness

- 4 plans authored under `.shipyard/phases/9/plans/`. ✅
- All 9 decisions traceable to a plan + task. ✅
- All 3 ROADMAP success criteria traceable to a verifying command. ✅
- No file-overlap conflicts within Wave 2. ✅
- ID-6 closure path is explicit. ✅
- Test count gate is explicit (≥ 17 net new). ✅

Phase 9 is ready for build.

---

## Plan Quality Verification (Step 6) — Verifier Structural Audit

**Date:** 2026-04-27  
**Verifier:** Shipyard Plan Quality Check (Pre-Build Structure Validation)  
**Scope:** YAML validity, ROADMAP/Decision coverage, file disjointness, acceptance criteria quality, hard rule enforcement.  

### Verdict: PASS

All structural, coverage, and feasibility checks pass. Four plans are well-structured, cover all phase requirements and decisions, have no file conflicts, and contain concrete, measurable acceptance criteria. Builder may proceed to execution. No revisions required.

### A. ROADMAP Coverage — PASS

All 5 Phase 9 deliverables are explicitly addressed by at least one plan task:
- ✅ Serilog wiring (Console, File, optional Seq) — PLAN-2.2 Tasks 1–2
- ✅ OTel registration (conditional OTLP exporter) — PLAN-2.2 Task 3
- ✅ ActivitySource + 5 spans — PLAN-2.1 Tasks 1–2
- ✅ Meter + 8 counters — PLAN-1.1 Task 2
- ✅ tests/FrigateRelay.Host.Tests/Observability + IntegrationTests/Observability — PLAN-3.1 Tasks 1–3

All 3 ROADMAP success criteria also covered (TraceSpans_CoverFullPipeline, Counters increments, excluded packages grep).

### B. Decision Coverage (D1–D9) — PASS

All 9 CONTEXT-9 decisions have explicit plan assignments with no gaps or overlaps:
- ✅ D1 (ActivityContext struct + Consumer kind) — PLAN-1.1 Task 3
- ✅ D2 (OTel conditional OTLP) — PLAN-2.2 Task 3
- ✅ D3 (Counter tags) — PLAN-1.1 Task 2 + PLAN-2.1 Tasks 1–2
- ✅ D4 (ID-6 fix) — PLAN-2.1 Task 2
- ✅ D5 (Test split) — PLAN-3.1 Tasks 1–3
- ✅ D6 (Hand-rolled logging) — PLAN-2.1 Task 1
- ✅ D7 (Seq conditional) — PLAN-2.2 Tasks 1–2
- ✅ D8 (Span attributes) — PLAN-2.1 Context/Task 2
- ✅ D9 (errors.unhandled single-site) — PLAN-2.1 Task 1 + PLAN-3.1 Task 2

### C. Plan Structure — PASS

**YAML Frontmatter:** All 4 plans have valid headers with `phase`, `plan`, `wave`, `dependencies`, `must_haves`, `files_touched`.

**Wave Dependency Graph:**
- Wave 1 (PLAN-1.1): no dependencies ✅
- Wave 2 (PLAN-2.1, PLAN-2.2): both depend on PLAN-1.1; parallel-safe ✅
- Wave 3 (PLAN-3.1): depends on PLAN-2.1 + PLAN-2.2 ✅
- No circular dependencies, no forward references ✅

**Task Counts:** All 4 plans have exactly 3 tasks each (≤3 limit). ✅

**Section Structure:** All plans include Context, Dependencies, Tasks (with Description + Acceptance Criteria), and Verification sections. ✅

### D. File Disjointness (Wave 2 Parallel Safety) — PASS

**PLAN-2.1 files_touched:**
- src/FrigateRelay.Host/Pipeline/EventPump.cs
- src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs
- .shipyard/ISSUES.md

**PLAN-2.2 files_touched:**
- src/FrigateRelay.Host/HostBootstrap.cs
- src/FrigateRelay.Host/Program.cs
- src/FrigateRelay.Host/appsettings.json
- src/FrigateRelay.Host/Configuration/StartupValidation.cs

**Result:** Zero file overlap. PLAN-2.1 and PLAN-2.2 can land in any order. ✅

### E. Acceptance Criteria Quality — PASS

Every task has concrete, measurable acceptance criteria (no vague language). Spot-checks:
- PLAN-1.1 Task 2: `grep -c 'CreateCounter<long>'` returns `10` ✅ Measurable
- PLAN-2.1 Task 2: `grep -n '"ActionDispatch"'` returns zero ✅ Presence/absence
- PLAN-2.2 Task 3: Negative test (malformed endpoint) causes non-zero exit ✅ Runnable
- PLAN-3.1 Task 3: `dotnet run --project tests/FrigateRelay.IntegrationTests ...` with filter-query ✅ Executable

**Zero vague criteria found.** All criteria are runnable or directly inspectable.

### F. Hard Rules Enforcement — PASS

All 9 hard rules enforced via acceptance criteria:
- ✅ No App.Metrics, OpenTracing, Jaeger.* — PLAN-1.1 Task 1 + PLAN-2.2 Task 3 + PLAN-3.1
- ✅ No Serilog.AspNetCore — PLAN-1.1 Task 1 + PLAN-2.2 Task 2
- ✅ No OpenTelemetry.Exporter.Console — PLAN-1.1 Task 1 + PLAN-2.2 Task 3
- ✅ No OpenTelemetry.Exporter.InMemory in src/ — PLAN-3.1 Task 1 + verification
- ✅ Phase 8 visibility (no public Host types) — PLAN-1.1 Task 2 + PLAN-2.1 verification
- ✅ Shared CapturingLogger<T> — PLAN-3.1 Tasks 1–2 + final verification
- ✅ EventIds 500–599 for new logging — PLAN-2.1 Task 1
- ✅ No secrets in appsettings.json — PLAN-2.2 Task 1 + secret-scan tripwire
- ✅ errors.unhandled single-site (D9) — PLAN-2.1 Task 1 + PLAN-3.1 Task 2

### G. Test Count Projection — PASS

- Phase 8 baseline: 69 unit tests in Host.Tests
- Phase 9 net new: ≥6 span + ≥9 counter + ≥2 integration = ≥17 net new tests
- Phase 9 target Host.Tests: ≥84 (69 + 15) ✅
- Phase 9 target IntegrationTests: ≥3 (1 existing + 2 new) ✅

### H. Open Questions — Clarified

Three architect questions in pre-build VERIFICATION.md remain valid:

1. **DispatchItem.Subscription field** — Deferred to builder with bounded scope (single field). Appropriate. ✅
2. **mqtt.receive boundary in EventPump** — Architect-frozen per PLAN-2.1 Task 1. No ambiguity. ✅
3. **Validator span parenting** — Integration test in PLAN-3.1 Task 3 serves as sentinel. Parenting enforced via nested `using` blocks. ✅

### Minor Notes (Non-Blocking)

1. Manual review checkpoint: PLAN-2.1 Task 1 line 105 requires inspection of EventId ranges (500–599). Standard practice. ✅
2. Builder discretion: PLAN-2.1 Task 2 defers Subscription field decision. Bounded scope. ✅
3. Negative test (PLAN-2.2 Task 3): Malformed endpoint test is manual smoke run. Standard. ✅

### Conclusion

All four plans are **ready for build execution**. No gaps in ROADMAP coverage, decision assignment, file disjointness, or acceptance criteria quality. Verifier recommends proceeding to Wave 1 (PLAN-1.1) immediately.
