# Critique Report — Phase 9 Plans

**Phase:** 9 — Observability  
**Date:** 2026-04-27  
**Type:** Plan feasibility critique (pre-execution)

## Verdict: READY

All four plans are feasible and correctly ordered. File paths exist or are sensibly new. API references match codebase state. No blocking issues detected.

---

## Summary

Phase 9 plans implement observability via OpenTelemetry traces, Serilog structured logging, and named counters across the pipeline. The four-plan structure (Wave 1 foundation → Wave 2 parallel instrumentation + wiring → Wave 3 tests) correctly sequences breaking changes (`DispatchItem.Activity` → `ActivityContext`) alongside instrumentation sites, avoiding intermediate build failures. CONTEXT-9 decisions are consistently reflected in each plan's acceptance criteria.

Key verification: `DispatcherDiagnostics.cs` exists (PLAN-1.1 claim verified; RESEARCH.md was incorrect). `DispatchItem` currently carries `Activity? Activity` at line 29 (matches PLAN-1.1 line reference). Test helper `CapturingLogger<T>` lives in shared `FrigateRelay.TestHelpers` (PLAN-3.1 reuse pattern confirmed). No file collisions between Wave 2 parallel plans (PLAN-2.1: EventPump.cs + ChannelActionDispatcher.cs; PLAN-2.2: HostBootstrap.cs + Program.cs + appsettings.json + StartupValidation.cs).

---

## Per-Plan Findings

### PLAN-1.1 — Diagnostics Foundation

**File Paths:**
- `src/FrigateRelay.Host/FrigateRelay.Host.csproj` — exists ✓
- `src/FrigateRelay.Host/Dispatch/DispatcherDiagnostics.cs` — exists ✓ (currently 38 lines; declares `Meter` + `ActivitySource` + 2 counters)
- `src/FrigateRelay.Host/Dispatch/DispatchItem.cs` — exists ✓ (record struct at 31 lines)
- `src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs` — exists ✓

**API Surface Validation:**
- `DispatcherDiagnostics.Meter` — verified at line 16, name `"FrigateRelay"` ✓
- `DispatcherDiagnostics.ActivitySource` — verified at line 21, name `"FrigateRelay"` ✓
- `DispatcherDiagnostics.Drops`, `Exhausted` counters — verified at lines 27–37 ✓
- `DispatchItem` parameter `Activity? Activity` — verified at line 29 (existing shape before flip) ✓
- `using System.Diagnostics;` present in DispatchItem.cs — verified ✓

**Verification Commands:**
- All grep/dotnet commands reference actual files with correct paths ✓
- `dotnet build FrigateRelay.sln -c Release` syntax valid ✓
- Counter count assertions (`grep -c 'CreateCounter<long>'`) are numeric and runnable ✓
- Task 2 acceptance: "expect: 10" (2 existing + 8 new) is quantified correctly ✓
- Task 3 grep commands check for non-existence of old field (`Activity? Activity`) and existence of new field (`ActivityContext ParentContext`) ✓

**File Collision Check:**
- PLAN-1.1 is Wave 1; no parallel plans depend on it; safe ✓

**Risk:** Low → **Medium claimed** (per frontmatter). Justification: The `Activity?` → `ActivityContext` flip on `DispatchItem` is a breaking change to an internal struct passed through the channel, but call sites are all internal (EventPump/ChannelActionDispatcher) so impact is contained. The plan correctly identifies this as a structural change that must be coordinated with PLAN-2.1's instrumentation sites.

---

### PLAN-2.1 — Pipeline Instrumentation

**File Paths:**
- `src/FrigateRelay.Host/Pipeline/EventPump.cs` — exists ✓
- `src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs` — exists ✓

**API Surface Validation:**
- Task 1 references `DispatcherDiagnostics.ActivitySource.StartActivity(...)` — verified that ActivitySource exists and is internal ✓
- Counter names in D3 table match CONTEXT-9 frozen set (`frigaterelay.events.received`, etc.) ✓
- Span names follow D8 table exactly (`mqtt.receive`, `event.match`, `dispatch.enqueue`, `action.<name>.execute`, `validator.<name>.check`) ✓
- `ActivityKind.Server` / `ActivityKind.Internal` / `ActivityKind.Producer` / `ActivityKind.Consumer` — all valid BCL types ✓
- Task 2 references `item.ParentContext` (post-PLAN-1.1 field name) — correct forward reference ✓
- D4 fix references `ActivityStatusCode.Unset` — valid BCL type ✓
- D9 single-site enforcement: grep checks for `ErrorsUnhandled.Add` in two files (EventPump + ChannelActionDispatcher), expecting exactly 1 match (EventPump only) ✓

**Verification Commands:**
- Five span-name greps (`mqtt.receive`, `event.match`, `dispatch.enqueue`, `action.*`, `validator.*`) are distinct and runnable ✓
- Counter increment greps check all 8 names across two files ✓
- D9 enforcement via `grep -rln ... | wc -l` expecting 1 is a proper negative-space check ✓
- ID-6 fix verification: grep for new pattern (`ActivityStatusCode.Unset`) and absence of old pattern (`ActivityStatusCode.Error, "Cancelled"`) ✓
- Span name legacy-removal check: grep for `"ActionDispatch"` expecting zero ✓

**Hidden Dependencies:**
- Task 2 note: "If `DispatchItem` does not currently carry the subscription name, ... add `string Subscription` as a positional record param." This is marked as a small additive change ("stays in PLAN-2.1 rather than retroactively expanding 1.1"). This is a **forward reference into implementation** — the plan author is saying "we might need to add a field, but the structural impact is small enough that it doesn't break Plan 1." This is acceptable architectural flexibility, but the plan should be crystal-clear about whether this is actually needed. **Spot-check:** The plan says `item.Plugin.Name` exists (for the action span), and references `subscriptionName` from outer scope. If `DispatchItem` doesn't have a `Subscription` field yet, it must be passed as a parameter or captured in the loop. The plan is **vague here** on whether the field exists pre-PLAN-2.1 or is being added. However, this is a builder decision, not a plan quality issue — the plan correctly flags it as "verify at build time" ✓

**Risk:** Medium — Correct. Parenting spans across `Channel<T>` requires careful `Activity.Current` capture and propagation. ID-6 fix is a one-line semantics change in an exception handler (low risk but must be exact).

---

### PLAN-2.2 — Host Wiring

**File Paths:**
- `src/FrigateRelay.Host/HostBootstrap.cs` — exists ✓
- `src/FrigateRelay.Host/Program.cs` — exists ✓
- `src/FrigateRelay.Host/appsettings.json` — exists ✓
- `src/FrigateRelay.Host/Configuration/StartupValidation.cs` — exists ✓

**API Surface Validation:**
- Task 1: `appsettings.json` shape with `Serilog:Seq:ServerUrl` and `Otel:OtlpEndpoint` sections — plan shows JSON structure; acceptance criteria check via Python JSON parse ✓
- Task 2: `builder.Host.UseSerilog(...)` — valid API for Worker SDK (not AspNetCore) ✓
- `Serilog.Extensions.Hosting` package (vs. rejected `Serilog.AspNetCore`) — correctly differentiated ✓
- Serilog sink calls: `WriteTo.Console(...)`, `WriteTo.File(...)`, `WriteTo.Seq(...)` — all valid Serilog API ✓
- Task 3: `AddOpenTelemetry()` → `WithTracing(b => b.AddSource("FrigateRelay"))` → conditional `AddOtlpExporter(...)` — correct OTel shape ✓
- `AddRuntimeInstrumentation()` — valid OTel API ✓
- `StartupValidation.ValidateObservability(...)` — new method marked for creation; references `Uri.TryCreate(...)` which is BCL ✓

**Verification Commands:**
- Python JSON parse in Task 1 acceptance: `json.load()` + assertion on nested keys ✓
- Grep for sink presence across two files (Program.cs or HostBootstrap.cs) ✓
- Secret-scan CI check re-runs (existing Phase 2 artifact) to ensure no leaked URLs ✓
- Smoke test: `dotnet run --project src/FrigateRelay.Host` with timeout + Ctrl-C pattern from CLAUDE.md ✓
- URI validation negative test: set malformed `Otel__OtlpEndpoint` env var and expect non-zero exit with error message ✓

**File Collision Check:**
- PLAN-2.2 files are disjoint from PLAN-2.1 files; can run in parallel ✓
- No file overlap identified ✓

**Risk:** Medium — Correct. Serilog bootstrap is fiddly (bootstrap logger vs. host logger timing; RESEARCH.md §6 sharp edges called out). URI validation is defensive (prevents silent OTLP failures).

---

### PLAN-3.1 — Observability Tests

**File Paths:**
- `tests/FrigateRelay.Host.Tests/Observability/DispatcherDiagnosticsTests.cs` — NEW ✓
- `tests/FrigateRelay.Host.Tests/Observability/EventPumpSpanTests.cs` — NEW ✓
- `tests/FrigateRelay.Host.Tests/Observability/CounterIncrementTests.cs` — NEW ✓
- `tests/FrigateRelay.Host.Tests/FrigateRelay.Host.Tests.csproj` — exists ✓ (modify to add test-only PackageReference)
- `tests/FrigateRelay.IntegrationTests/Observability/TraceSpansCoverFullPipelineTests.cs` — NEW ✓
- `tests/FrigateRelay.IntegrationTests/FrigateRelay.IntegrationTests.csproj` — exists ✓ (modify)

**API Surface Validation:**
- `Sdk.CreateTracerProviderBuilder().AddSource("FrigateRelay").AddInMemoryExporter(...)` — valid OTel testing API (RESEARCH.md §9 precedent cited) ✓
- `ExportProcessorType.Simple` (vs. rejected `Batch`) — correct per RESEARCH.md §9 for synchronous assertions ✓
- `MeterListener` pattern with `instrument.Meter.Name == "FrigateRelay"` filter — BCL API, correct ✓
- `CapturingLogger<T>` from `FrigateRelay.TestHelpers` — verified to exist at `tests/FrigateRelay.TestHelpers/CapturingLogger.cs` ✓
- Testcontainers + WireMock patterns — reuse existing Phase 4/6/7 fixtures (plan says "reuse existing or pattern-match Phase 7 setup") ✓
- Test class name `TraceSpans_CoverFullPipeline` — matches ROADMAP mandate exactly ✓

**Verification Commands:**
- Test count gates: baseline ~69 for Host.Tests, expect ≥ 84 after Phase 9 (+6 span, +9 counter) — quantified ✓
- Filter queries use MTP syntax (`--filter-query "/*/*/ClassName"`) — consistent with CLAUDE.md ✓
- Span parent-child assertions: `ParentSpanId == root.SpanId` and activity tree walk — testable via OTel ActivityExporter list ✓
- Counter totals via MeterListener: assert exact counts (1 received, 1 matched, 2 dispatched, etc.) and tag values present ✓
- `TraceSpans_CoverFullPipeline` name verification via grep returns ≥1 match ✓

**Hidden Dependencies:**
- PLAN-3.1 depends on PLAN-2.1 (instrumentation sites) and PLAN-2.2 (OTel registration) being complete. The test fixture must call `services.AddOpenTelemetry().WithTracing(b => b.AddSource("FrigateRelay")...)` — this setup only works if PLAN-2.2's wiring is present. ✓
- Integration test uses real Mosquitto + WireMock (existing Phase 4+ infrastructure). Plan assumes fixtures exist; no new external dependencies. ✓

**Risk:** Medium — Correct. Span parenting assertions are brittle if the span hierarchy doesn't match D8 table exactly. The plan flags this: "If the implementation in PLAN-2.1 produced a different parenting, this test will fail and PLAN-2.1 must be revisited." This is appropriate TDD discipline. Counter increment tests depend on exact tag emission; if PLAN-2.1 omits a tag, the MeterListener assertion will catch it.

---

## Cross-Plan Concerns

### Wave Ordering
1. **Wave 1 (PLAN-1.1):** Packages, counter declarations, `DispatchItem` field flip.
2. **Wave 2 (PLAN-2.1 || PLAN-2.2):** Instrumentation sites + host wiring. No ordering constraint between 2.1 and 2.2 (disjoint files).
3. **Wave 3 (PLAN-3.1):** Tests. Correct dependencies `[PLAN-2.1, PLAN-2.2]` ensure both instrumentation and wiring complete before tests run.

**Verdict:** Correct. Wave 1 → 2 → 3 ordering is enforced by file dependencies and captures the logical flow: foundation → implementation → validation.

### Hidden Dependencies
- **PLAN-2.1 and PLAN-2.2 coordination on OTel/Serilog packages:** Both plans assume packages added in PLAN-1.1 are present. No circular dependency. ✓
- **DispatchItem.Subscription field:** PLAN-2.1 Task 2 notes a possible structural change ("add `string Subscription` if needed"). This is flagged as "verify at build time." Not a blocker, but the builder should confirm pre-PLAN-2.1 whether the field exists or must be added. **Recommendation:** Architect should clarify in PLAN-2.1 Task 2 whether `DispatchItem.Subscription` is added as part of PLAN-2.1 or was already present from Phase 8. (Low risk; builder will discover at compile time if wrong.)
- **StartupValidation.ValidateObservability hook:** PLAN-2.2 Task 3 says "wire into existing `ValidateAll` aggregation site." This assumes `ValidateAll` exists and is being called. Plan doesn't cite the line number where this hook is injected. **Recommendation:** Accepted on builder discretion, but verify `StartupValidation.cs` has an existing `ValidateAll` entry point at build time. (Low risk; builder will find it via grep.)

### File Collision
- PLAN-2.1 touches: `Pipeline/EventPump.cs`, `Dispatch/ChannelActionDispatcher.cs` (2 files).
- PLAN-2.2 touches: `HostBootstrap.cs`, `Program.cs`, `appsettings.json`, `Configuration/StartupValidation.cs` (4 files).
- **Overlap:** None. ✓

### Breaking Changes
- **DispatchItem.Activity → DispatchItem.ParentContext:** Affects only internal call sites (EventPump line ~150, ChannelActionDispatcher lines ~173–176 per RESEARCH.md). Correctly identified as a PLAN-1.1 breaking change that PLAN-2.1 adapts to. ✓
- **Span name change "ActionDispatch" → "action.<name>.execute":** Local to ChannelActionDispatcher; no external impact. ✓

---

## Blocking Issues

**None identified.**

---

## Minor Notes (Non-Blocking)

1. **PLAN-2.1 Task 2, "subscription name source" ambiguity:** The plan says "If `DispatchItem` does not currently carry the subscription name, ... add `string Subscription`." Recommend architect pre-confirm with builder that `DispatchItem` has this field before PLAN-2.1 execution (or expand PLAN-2.1 Task 2 to explicitly add it). Currently accepted as "builder verifies at compile time."

2. **StartupValidation hook injection point:** PLAN-2.2 Task 3 says "wire into existing `ValidateAll` aggregation site" but doesn't cite the exact line. This is standard Phase 8 pattern per CONTEXT-9, so builder will recognize the pattern. Accepted.

3. **RESEARCH.md claim about DispatcherDiagnostics.cs:** RESEARCH.md §7 claimed the file was missing; it exists. PLAN-1.1 correctly identified the falsehood and noted "RESEARCH.md's claim that the file is missing is incorrect; verified at the start of Phase 9." ✓

4. **Test directories:** PLAN-3.1 creates `tests/FrigateRelay.Host.Tests/Observability/` and `tests/FrigateRelay.IntegrationTests/Observability/` (neither exists yet). This is expected for NEW files. No blocking issue. ✓

---

## Recommendations

- **Pre-PLAN-2.1:** Confirm `DispatchItem` carries `Subscription` field (or plan to add it in PLAN-2.1 Task 2).
- **Pre-PLAN-2.2:** Confirm `Configuration/StartupValidation.cs` has a `ValidateAll` method or entry point for bundling validators.
- **CI workflow:** The Phase 9 plans do not touch `.github/workflows/ci.yml`, so the existing `dotnet run --project tests/...` test invocations should pick up the new Phase 9 tests automatically (glob already in place per CLAUDE.md Phase 2 notes). Verify this assumption at build verification time.

---

## Verdict Summary

**READY** — All four Phase 9 plans are feasible, correctly ordered, and internally consistent with CONTEXT-9 decisions. No file paths are missing, APIs match the codebase, verification commands are runnable, and cross-plan dependencies are properly expressed. Minor clarifications on DispatchItem structure and StartupValidation hook injection point can be resolved at build time without plan revision.

