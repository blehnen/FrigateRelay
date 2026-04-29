---
phase: 11-oss-polish
plan: 1.1
wave: 1
dependencies: []
must_haves:
  - Validator_ShortCircuits_OnlyAttachedAction passes (or is explicitly [Ignore]'d with new ISSUES.md ID)
  - TraceSpans_CoverFullPipeline passes (or is explicitly [Ignore]'d with new ISSUES.md ID)
  - End-state test count is documented green or known-issue
  - Wave 2 doc plans gate on this plan landing
files_touched:
  - tests/FrigateRelay.IntegrationTests/MqttToValidatorTests.cs
  - tests/FrigateRelay.IntegrationTests/Observability/  # one .cs file (architect to read filename before edit)
  - tests/FrigateRelay.IntegrationTests/Fixtures/CapturingLoggerProvider.cs  # may need rewire to Serilog sink
  - .shipyard/ISSUES.md  # only on escape-hatch
tdd: false
risk: medium
---

# Plan 1.1: Phase 9 integration test triage (CONTEXT-11 D7 gate)

## Context

CONTEXT-11 D7 makes Wave 1 a test-triage gate. Phase 10 closeout confirmed `192/194` tests pass; the two regressions are:

1. `Validator_ShortCircuits_OnlyAttachedAction` (`tests/FrigateRelay.IntegrationTests/MqttToValidatorTests.cs`) — RESEARCH.md sec 6 high-confidence root cause: the test registers a `CapturingLoggerProvider` via `builder.Services.AddSingleton<ILoggerProvider>(...)` AFTER `HostBootstrap.ConfigureServices`, but `AddSerilog` clears providers and replaces the pipeline with a single Serilog provider. The DI-singleton-registered `ILoggerProvider` never sees log entries. The WireMock action assertions likely pass; only the `captureProvider.Entries.Where(e => e.EventId.Name == "ValidatorRejected")` assertion fails. **Shallow fix.**

2. `TraceSpans_CoverFullPipeline` — RESEARCH.md sec 6 unconfirmed file path; researcher flagged the test file in `tests/FrigateRelay.IntegrationTests/Observability/` was not located. Builder's first action in Task 2 is to read the directory listing and the file. Hypothesis is either a similar capture-wiring issue (in-memory exporter not registered against the active `TracerProvider`) OR an `Activity` propagation timing across the channel hop. Depth not yet known.

D7 budget: **1 retry per test**. If a fix attempt fails, escape-hatch is `[Ignore("...")]` plus a fresh ISSUES.md entry, so Wave 2 doc work can proceed.

## Dependencies

None (Wave 1).

**Wave-2 gate:** Every Wave 2 plan declares `dependencies: [1.1]`. Acceptance for closing Wave 1 is one of:
- All test projects pass green: `bash .github/scripts/run-tests.sh` exits 0 with no `[Ignore]` markers added.
- Same command exits 0 with `[Ignore("ID-N — see ISSUES.md")]` on the unresolved test(s) AND a corresponding new ISSUES.md entry.

## Tasks

### Task 1: Fix `Validator_ShortCircuits_OnlyAttachedAction` log capture

**Files:**
- `tests/FrigateRelay.IntegrationTests/Fixtures/CapturingLoggerProvider.cs` (read first; modify if it must be re-wired into Serilog instead of `ILoggerProvider`)
- `tests/FrigateRelay.IntegrationTests/MqttToValidatorTests.cs` (modify the `BuildHostAsync` registration site that adds the capturing provider)

**Action:** modify

**Description:**
Reproduce the failure first: `dotnet build FrigateRelay.sln -c Release` then `dotnet run --project tests/FrigateRelay.IntegrationTests -c Release --no-build -- --filter "Validator_ShortCircuits_OnlyAttachedAction"`. Confirm the assertion that fails is the `rejectedEntries.Should().HaveCount(1, ...)` line (or the WireMock assertions are fine).

**Fix path A (preferred, minimal):** Re-wire the capture so it survives Serilog's pipeline takeover. Two equivalent patterns:

1. **Logger-builder injection.** In `BuildHostAsync` (or wherever Serilog is wired), add the capturing provider via `builder.Logging.AddProvider(captureProvider)` AFTER the `AddSerilog` call rather than via `Services.AddSingleton<ILoggerProvider>`. `AddProvider` on the `ILoggingBuilder` registers post-Serilog because `ILoggingBuilder` order is preserved while `AddSerilog` clears earlier providers, not later ones.

2. **Serilog sink form.** Add a tiny `CapturingSerilogSink : ILogEventSink` adjacent to `CapturingLoggerProvider` and wire it into `loggerConfiguration.WriteTo.Sink(captureSink)`. The sink translates Serilog `LogEvent` to the existing `Entry` shape so test assertions are unchanged. Heavier; only choose if (1) doesn't work because Serilog clears later providers too.

The test's existing assertions (`e.EventId.Name == "ValidatorRejected"` + message contains `"Pushover"` + `"strict-person"`) MUST remain unchanged — the dispatcher emits the structured log; this fix only restores capture, not assertion shape.

**Acceptance Criteria:**
- `dotnet run --project tests/FrigateRelay.IntegrationTests -c Release --no-build -- --filter "Validator_ShortCircuits_OnlyAttachedAction"` exits 0.
- `grep -n 'AddSingleton<ILoggerProvider>' tests/FrigateRelay.IntegrationTests/MqttToValidatorTests.cs` returns either zero matches (replaced) or matches that are accompanied by an explicit `builder.Logging.AddProvider(...)` line.
- The existing assertion lines (`rejectedEntries.Should().HaveCount(1, ...)`, `Should().Contain("Pushover")`, `Should().Contain("strict-person")`) are textually unchanged from before the fix — `git diff` shows only the registration-site lines moved.
- No new `[Ignore]` attribute on the test method.
- D7 budget consumed: ≤1 retry. If both Fix A patterns fail, escape-hatch via Task 3.

### Task 2: Fix `TraceSpans_CoverFullPipeline`

**Files:**
- `tests/FrigateRelay.IntegrationTests/Observability/<filename>.cs` — **builder MUST first run `ls tests/FrigateRelay.IntegrationTests/Observability/` and read the file before editing**. RESEARCH.md uncertainty flag #1 explicitly defers this lookup to the builder.

**Action:** modify

**Description:**
Step 1: identify the test file. Step 2: reproduce the failure with `dotnet run --project tests/FrigateRelay.IntegrationTests -c Release --no-build -- --filter "TraceSpans_CoverFullPipeline"` and capture the assertion failure shape. Step 3: classify into one of three categories:

- **Category A (shallow, ≤30 min):** In-memory exporter wiring race (exporter registered against a `TracerProvider` whose disposal raced the test, or activities not flushed before the assertion). Fix by adding `tracerProvider.ForceFlush(timeoutMilliseconds: 5000)` before reading `_exportedActivities`, OR by polling the exporter's snapshot up to a deadline (similar shape to PLAN-1.1's TraceSpans-equivalent in Phase 10's HealthzReadiness pattern).

- **Category B (moderate, ≤2 hours):** `Activity` propagation across `Channel<T>` hop. The producer span (`mqtt.receive` or `event.match`) closes before the consumer (`action.<name>.execute`) reads the `DispatchItem`, so the child has a null parent. Verify by inspecting `DispatchItem` to confirm it carries an `ActivityContext` (Phase 9 ARCH); fix is either `Activity.Current = Activity.Start(parentContext, ...)` in the consumer OR wrap the consumer in an explicit `ActivitySource.StartActivity(name, kind, parentContext)`. CLAUDE.md "Activity propagates across the channel hop via the `DispatchItem`" is the contract — the fix restores it, doesn't change it.

- **Category C (deep, >2 hours):** Anything else. Stop, escape-hatch via Task 3.

D7 budget: **1 retry total**. If Category A fix attempt does not produce green, do NOT attempt Category B in the same plan; escape-hatch.

The 4-child-spans-under-one-root assertion shape from ROADMAP Phase 9 success criteria MUST remain unchanged — the fix restores propagation/flush, not assertions.

**Acceptance Criteria:**
- `ls tests/FrigateRelay.IntegrationTests/Observability/` listed in builder's notes (uncertainty flag closure).
- `dotnet run --project tests/FrigateRelay.IntegrationTests -c Release --no-build -- --filter "TraceSpans_CoverFullPipeline"` exits 0.
- The "one root span + 4 child spans" assertion lines are textually unchanged.
- No new `[Ignore]` attribute on the test method.
- D7 budget consumed: ≤1 retry.

### Task 3: Escape-hatch path (only if Task 1 OR Task 2 cannot be fixed within retry budget)

**Files (only created if escape-hatch is taken):**
- The relevant test file (`MqttToValidatorTests.cs` and/or the TraceSpans test) — add `[Ignore("ID-NN — see .shipyard/ISSUES.md")]` immediately above the `[TestMethod]` attribute.
- `.shipyard/ISSUES.md` — append a new entry per ignored test.

**Action:** modify (conditional)

**Description:**
This task ONLY executes if Task 1's or Task 2's retry budget is exhausted. Otherwise it is skipped (mark `n/a` in the SUMMARY).

For each test that cannot be fixed:

1. Add `[Ignore("ID-NN — Phase 9 regression: <one-line cause hypothesis>; see .shipyard/ISSUES.md")]` immediately above the `[TestMethod]` attribute. Use the next free ISSUES.md ID (Phase 10 closed at ID-27 → start at ID-28).

2. Append an ISSUES.md entry following the established format:

```markdown
### ID-NN: <Test name> Phase 9 regression — Phase 11 escape-hatch

**Source:** Phase 11 PLAN-1.1 builder (test triage retry budget exhausted, 2026-04-28)
**Severity:** Medium (test-coverage regression, not production)
**Status:** Open

**Description:**
<one-paragraph cause hypothesis from triage attempt>

**Reproduction:**
`dotnet run --project tests/FrigateRelay.IntegrationTests -c Release --no-build -- --filter "<test-name>"`

**Mitigation attempts during PLAN-1.1:**
<bullets — what was tried, what failed>

**Reactivation triggers:**
- Phase 12 cutover (parity tests must be green before v1.0.0 tag).
- Any builder pass touching the relevant area (`src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs` for ValidatorRejected logging, or `src/FrigateRelay.Host/Observability/` for span propagation).
```

**Acceptance Criteria:**
- If escape-hatch taken: `grep -n '\[Ignore("ID-' tests/FrigateRelay.IntegrationTests/` returns one line per ignored test.
- If escape-hatch taken: `grep -n '^### ID-' .shipyard/ISSUES.md | tail -3` shows the new entry/entries with sequential IDs starting from ID-28.
- If escape-hatch NOT taken: SUMMARY records "Task 3: not exercised — Tasks 1 and 2 both passed".

## Verification

Run from repo root:

```bash
# 0. Solution still builds clean
dotnet build FrigateRelay.sln -c Release

# 1. Both target tests (or their [Ignore]-skipped form) — full integration suite green
dotnet run --project tests/FrigateRelay.IntegrationTests -c Release --no-build

# 2. Full repo test gate (Wave-2 entry condition)
bash .github/scripts/run-tests.sh

# 3. CONTEXT-11 D7 acceptance — 194/194 OR (192/192 + 2 [Ignore]'d with tracking IDs)
# Manual visual check on test output: count "Passed" lines, count "Skipped" lines.

# 4. If escape-hatch taken
grep -nE '\[Ignore\("ID-(2[8-9]|[3-9][0-9])' tests/FrigateRelay.IntegrationTests/
grep -nE '^### ID-(2[8-9]|[3-9][0-9]):' .shipyard/ISSUES.md
```

Wave 2 plans are blocked until verification step 2 exits 0.
