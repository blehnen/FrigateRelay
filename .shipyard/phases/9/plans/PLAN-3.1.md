---
phase: phase-9-observability
plan: 3.1
wave: 3
dependencies: [PLAN-2.1, PLAN-2.2]
must_haves:
  - Unit tests assert ActivitySource + Meter registration and counter increments via MeterListener
  - Unit tests assert each Phase 9 span emits its D8-finalized attribute set
  - Integration test TraceSpans_CoverFullPipeline asserts root mqtt.receive span has 4 expected children with shared TraceId
  - Integration test asserts the D3 counter increments for one matched event dispatched to two actions (one validated, one not)
files_touched:
  - tests/FrigateRelay.Host.Tests/Observability/DispatcherDiagnosticsTests.cs
  - tests/FrigateRelay.Host.Tests/Observability/EventPumpSpanTests.cs
  - tests/FrigateRelay.Host.Tests/Observability/CounterIncrementTests.cs
  - tests/FrigateRelay.Host.Tests/FrigateRelay.Host.Tests.csproj
  - tests/FrigateRelay.IntegrationTests/Observability/TraceSpansCoverFullPipelineTests.cs
  - tests/FrigateRelay.IntegrationTests/FrigateRelay.IntegrationTests.csproj
tdd: true
risk: medium
---

# Plan 3.1: Observability Tests — Unit Spans/Counters + Integration Pipeline Trace

## Context

Implements CONTEXT-9 D5 test split: unit tests for ActivitySource/Meter registration and span/counter shape live in `tests/FrigateRelay.Host.Tests/Observability/`; a single end-to-end span-tree test using real Mosquitto + WireMock lives in `tests/FrigateRelay.IntegrationTests/Observability/`.

Closes ROADMAP Phase 9 success criteria 1 (`TraceSpans_CoverFullPipeline`) and 2 (counter-increment assertions for one event → two actions → one validator).

TDD discipline (per architect requirement): Tests are authored RED first. Builder commits the red test commit, then commits the green implementation in PLAN-2.1/2.2 (which already shipped in Wave 2 — so for this plan, tests should land green-on-first-build IF Wave 2 was implemented correctly; if anything is wrong, tests will surface the gap immediately, which is the goal). The TDD value here is in detecting Wave 2 regressions, not in red-driving brand new code.

## Dependencies

- PLAN-2.1 — needs spans + counter increments to be present in `EventPump.cs` and `ChannelActionDispatcher.cs`.
- PLAN-2.2 — needs `AddSource("FrigateRelay")` + `AddMeter("FrigateRelay")` registration so OTel pipelines see the activities.

## Tasks

### Task 1: Unit tests — DispatcherDiagnostics surface + EventPump span attributes
**Files:**
- `tests/FrigateRelay.Host.Tests/Observability/DispatcherDiagnosticsTests.cs` (NEW)
- `tests/FrigateRelay.Host.Tests/Observability/EventPumpSpanTests.cs` (NEW)
- `tests/FrigateRelay.Host.Tests/FrigateRelay.Host.Tests.csproj` (modify — add `OpenTelemetry.Exporter.InMemory` 1.15.3 PackageReference)

**Action:** create + modify

**Description:**

Add `<PackageReference Include="OpenTelemetry.Exporter.InMemory" Version="1.15.3" />` to `FrigateRelay.Host.Tests.csproj` (test-only dep — never in `src/`).

**`DispatcherDiagnosticsTests.cs`** — pure surface assertions, no SUT execution:
- `Meter_Name_IsFrigateRelay` — reflect `DispatcherDiagnostics.Meter.Name` equals `"FrigateRelay"`.
- `ActivitySource_Name_IsFrigateRelay` — reflect `DispatcherDiagnostics.ActivitySource.Name` equals `"FrigateRelay"`.
- `AllExpectedCountersDeclared` — for each of the 10 names (`frigaterelay.events.received`, `frigaterelay.events.matched`, `frigaterelay.actions.dispatched`, `frigaterelay.actions.succeeded`, `frigaterelay.actions.failed`, `frigaterelay.validators.passed`, `frigaterelay.validators.rejected`, `frigaterelay.errors.unhandled`, `frigaterelay.dispatch.drops`, `frigaterelay.dispatch.exhausted`), use `MeterListener.InstrumentPublished` to confirm each name is published when an instrument-publication trigger runs. Practical pattern: run `MeterListener.Start()`, then trigger publication by referencing each `Counter<long>` field once; assert the captured set of instrument names equals the expected set.

**`EventPumpSpanTests.cs`** — span attribute shape assertions per D8 table:
- `MqttReceiveSpan_HasEventIdAndSourceTags` — drive `EventPump.PumpAsync` with an `IEventSource` stub yielding one canned `EventContext`; capture activities via `Sdk.CreateTracerProviderBuilder().AddSource("FrigateRelay").AddInMemoryExporter(list, o => o.ExportProcessorType = ExportProcessorType.Simple).Build()`; assert one activity with `DisplayName == "mqtt.receive"`, `Kind == ActivityKind.Server`, tags `event.id` and `event.source` set to the stub's values.
- `EventMatchSpan_TagsCameraLabelAndMatchCount` — same setup, plus `SubscriptionMatcher` returning 2 matches; assert one `event.match` activity with `event.id`, `camera`, `label`, `subscription_count_matched=2`.
- `DispatchEnqueueSpan_TagsSubscriptionAndActionCount` — assert one `dispatch.enqueue` activity with `subscription`, `action_count`, `event.id`, `Kind == ActivityKind.Producer`.
- `MqttReceive_IsParentOf_EventMatch_AndDispatchEnqueue` — assert `mqtt.receive.SpanId == event.match.ParentSpanId == dispatch.enqueue.ParentSpanId`.

Critical pattern (RESEARCH.md §9): use `ExportProcessorType.Simple` — `Batch` (default) buffers asynchronously and breaks synchronous assertions.

Use the shared `CapturingLogger<T>` from `tests/FrigateRelay.TestHelpers/` (CLAUDE.md convention: do NOT redefine).

**Acceptance Criteria:**
- `dotnet build FrigateRelay.sln -c Release` clean.
- `dotnet run --project tests/FrigateRelay.Host.Tests -c Release` — net new passing tests ≥ 6 across the two new files.
- `grep -n 'OpenTelemetry.Exporter.InMemory' src/` returns zero (test-only dep, NOT in production csprojs).
- `grep -n 'ExportProcessorType.Simple' tests/FrigateRelay.Host.Tests/Observability/` returns at least one match (RESEARCH.md §9 pattern).

### Task 2: Unit tests — Counter increments via MeterListener
**Files:** `tests/FrigateRelay.Host.Tests/Observability/CounterIncrementTests.cs` (NEW)
**Action:** create

**Description:**

Per RESEARCH.md §8: `MeterListener` is in the BCL — no NuGet package required.

Tests cover the D3 tag dimensions:
- `EventsReceived_Increments_WithCameraAndLabelTags` — drive one event through `EventPump.PumpAsync` (with `MeterListener` capturing `frigaterelay.*` measurements); assert exactly one measurement on `frigaterelay.events.received` with value 1, tag `camera = <stub camera>`, tag `label = <stub label>`.
- `EventsMatched_Increments_PerMatchedSubscription` — stub `SubscriptionMatcher` returning 2 matches; assert two measurements on `frigaterelay.events.matched`, each tagged `subscription`, `camera`, `label`.
- `ActionsDispatched_Increments_PerEnqueueAsyncCall` — stub returns 1 sub × 2 actions; assert two measurements on `frigaterelay.actions.dispatched` tagged `subscription`, `action`.
- `ActionsSucceeded_Tags_SubscriptionAndAction_OnNormalReturn` — drive `ChannelActionDispatcher.ConsumeAsync` with a passing `IActionPlugin` stub; assert one measurement on `frigaterelay.actions.succeeded`.
- `ActionsFailed_Tags_SubscriptionAndAction_OnRetryExhaustion` — drive ConsumeAsync with a stub that always throws; assert one measurement on `frigaterelay.actions.failed`.
- `ValidatorsPassed_Tags_SubscriptionActionValidator` — passing validator stub; assert measurement tags include all three.
- `ValidatorsRejected_Tags_SubscriptionActionValidator_OnFail` — failing validator stub; assert measurement and assert action plugin was NEVER called (Phase 7 short-circuit semantics still hold).
- `ErrorsUnhandled_Increments_OnPumpFault_Once_Untagged` — throw from `IEventSource.ReadEventsAsync`; assert exactly one measurement on `frigaterelay.errors.unhandled` with NO tags (D3).
- `ErrorsUnhandled_DoesNotIncrement_OnRetryExhaustion` — drive a retry-exhaust path; assert ZERO measurements on `frigaterelay.errors.unhandled` (D9 — reserved for unexpected escapes only).

Use the `MeterListener` pattern from RESEARCH.md §8 verbatim. Filter by `instrument.Meter.Name == "FrigateRelay"` so unrelated `dotnet.*` runtime instrumentation noise is excluded.

**Acceptance Criteria:**
- `dotnet run --project tests/FrigateRelay.Host.Tests -c Release` — net new passing tests ≥ 9.
- `dotnet run --project tests/FrigateRelay.Host.Tests -c Release -- --filter-query "/*/*/CounterIncrementTests/ErrorsUnhandled_DoesNotIncrement_OnRetryExhaustion"` returns the single test passing — proves D9 single-site discipline.
- All tests use the shared `CapturingLogger<T>` from `FrigateRelay.TestHelpers` (no NSubstitute on `ILogger<T>` per CLAUDE.md convention).

### Task 3: Integration test — TraceSpans_CoverFullPipeline + counter set
**Files:**
- `tests/FrigateRelay.IntegrationTests/Observability/TraceSpansCoverFullPipelineTests.cs` (NEW)
- `tests/FrigateRelay.IntegrationTests/FrigateRelay.IntegrationTests.csproj` (modify — add `OpenTelemetry.Exporter.InMemory` 1.15.3 PackageReference)

**Action:** create + modify

**Description:**

End-to-end test using the existing Phase 4/6/7 fixture pattern (Testcontainers Mosquitto + WireMock for BlueIris/Pushover/CodeProject.AI). Reuse the shared fixture if one exists in `IntegrationTests/`; otherwise pattern-match the Phase 7 `Validator_ShortCircuits_OnlyAttachedAction` setup.

**Test 1 — `TraceSpans_CoverFullPipeline`** (closes ROADMAP success criterion 1):

Setup:
- One subscription matching the published Frigate event.
- Two actions: BlueIris (no validator) + Pushover (with CodeProject.AI validator that returns `pass`).
- Test host registers OTel via `services.AddOpenTelemetry().WithTracing(b => b.AddSource("FrigateRelay").AddInMemoryExporter(activities, o => o.ExportProcessorType = ExportProcessorType.Simple))`.

Act:
- Publish one Frigate-shaped MQTT message.
- Wait for both downstream WireMock stubs to record their hits (timeout ≤ 30s per Phase 4 SLO).

Assert:
- Exactly one activity with `DisplayName == "mqtt.receive"` (call this `root`).
- All other captured `frigaterelay.*` activities share `root.TraceId`.
- Children of `root` (where `ParentSpanId == root.SpanId`) include exactly one `event.match` and exactly one `dispatch.enqueue`.
- Two `action.<name>.execute` spans exist (`action.blueiris.execute`, `action.pushover.execute`) with `ParentSpanId` matching the captured `dispatch.enqueue` span (`ActivityKind.Consumer` per D1).
- One `validator.codeprojectai.check` span exists as a child of `action.pushover.execute` (`ParentSpanId` chain: `mqtt.receive` → `dispatch.enqueue` → `action.pushover.execute` → `validator.codeprojectai.check`). Note: the actual parenting may differ depending on whether the validator span is parented to the action span or to the dispatch span; the FROZEN parenting per CONTEXT-9 §D8 + this plan is parent = `action.<name>.execute`. If the implementation in PLAN-2.1 produced a different parenting, this test will fail and PLAN-2.1 must be revisited (do not paper over the discrepancy by relaxing the assertion).
- Every span carries the D8-mandated `event.id` attribute (use `activities.All(a => a.Tags.Any(t => t.Key == "event.id"))`).

**Test 2 — `Counters_Increment_PerD3_TagDimensions`** (closes ROADMAP success criterion 2):

Setup: same as Test 1, but with a `MeterListener` capturing `FrigateRelay` instrument measurements.

Act: publish one event (matches one subscription with two actions, one validated).

Assert (per ROADMAP "Metric test" criterion):
- `frigaterelay.events.received` total = 1.
- `frigaterelay.events.matched` total = 1.
- `frigaterelay.actions.dispatched` total = 2.
- `frigaterelay.actions.succeeded` total = 2.
- `frigaterelay.validators.passed` total = 1.
- `frigaterelay.validators.rejected` total = 0.
- `frigaterelay.errors.unhandled` total = 0.
- Tag values present and correct on each: subscription name, action name (BlueIris / Pushover), validator name (CodeProjectAi), camera, label.

**Acceptance Criteria:**
- `dotnet build FrigateRelay.sln -c Release` clean.
- `dotnet run --project tests/FrigateRelay.IntegrationTests -c Release` — net new passing tests ≥ 2; total runtime under 30 seconds (Phase 4 SLO carries forward).
- `grep -n 'TraceSpans_CoverFullPipeline' tests/FrigateRelay.IntegrationTests/Observability/TraceSpansCoverFullPipelineTests.cs` returns at least one match.
- `grep -n 'OpenTelemetry.Exporter.InMemory' src/` returns zero (test-only).
- Phase 9 ROADMAP success criterion 3 holds: `git grep -nE 'App\.Metrics|OpenTracing|Jaeger\.' src/` returns zero.

## Verification

```bash
cd /mnt/f/git/frigaterelay

# Build clean
dotnet build FrigateRelay.sln -c Release 2>&1 | tail -5

# Unit tests pass (Host.Tests baseline 69 → expect ≥ 84 after Phase 9: +6 span shape, +9 counter)
dotnet run --project tests/FrigateRelay.Host.Tests -c Release 2>&1 | tail -10

# Specific Phase 9 unit suites
dotnet run --project tests/FrigateRelay.Host.Tests -c Release -- --filter-query "/*/*/DispatcherDiagnosticsTests"
dotnet run --project tests/FrigateRelay.Host.Tests -c Release -- --filter-query "/*/*/EventPumpSpanTests"
dotnet run --project tests/FrigateRelay.Host.Tests -c Release -- --filter-query "/*/*/CounterIncrementTests"

# Integration tests pass (existing 1+ from Phase 7 + 2 new from Phase 9)
dotnet run --project tests/FrigateRelay.IntegrationTests -c Release 2>&1 | tail -10

# Specific full-pipeline trace test
dotnet run --project tests/FrigateRelay.IntegrationTests -c Release -- --filter-query "/*/*/TraceSpansCoverFullPipelineTests/TraceSpans_CoverFullPipeline"

# ROADMAP Phase 9 success criterion 3
git grep -nE 'App\.Metrics|OpenTracing|Jaeger\.' src/
# expect: zero

# Test-only dep is NOT in src/
grep -rn 'OpenTelemetry.Exporter.InMemory' src/
# expect: zero

# CapturingLogger<T> shared (CLAUDE.md convention)
grep -rn 'class CapturingLogger' tests/FrigateRelay.Host.Tests/Observability/ tests/FrigateRelay.IntegrationTests/Observability/
# expect: zero (must reuse FrigateRelay.TestHelpers.CapturingLogger<T>)
```
