# Simplification Report — Phase 9 (Observability)

**Phase:** 9 — Observability (Spans, Counters, Serilog, OTLP)
**Date:** 2026-04-27
**Files analyzed:** ~20 (4 new test files, 4 modified source files, 2 new source files, 1 integration test file, csproj changes, ISSUES.md)
**Findings:** 2 High · 3 Medium · 4 Low

---

## High Priority

### Magic delays in observability unit tests
- **Type:** Refactor
- **Effort:** Trivial
- **Locations:**
  - `tests/FrigateRelay.Host.Tests/Observability/EventPumpSpanTests.cs:285` — `Task.Delay(400)` inside `RunPumpAsync`
  - `tests/FrigateRelay.Host.Tests/Observability/CounterIncrementTests.cs:357` — `Task.Delay(400)` inside `RunPumpAsync`
  - `tests/FrigateRelay.Host.Tests/Observability/CounterIncrementTests.cs:420–422` — `shouldThrow ? 200 : 100` delay inside `RunDispatcherAsync`
- **Description:** Three call sites use fixed `Task.Delay` values to wait for the `Channel<T>` consumer to process items and export spans/counter measurements. Under CI load or slower machines, 400 ms may be insufficient; under fast machines the delay always burns wall-clock time. The `shouldThrow ? 200 : 100` variant encodes an implicit assumption that error paths are slower, which is fragile and undocumented.
- **Suggestion:** Replace each `await Task.Delay(N)` wait with a lightweight poll loop:
  ```csharp
  var sw = Stopwatch.StartNew();
  while (activities.Count == 0 && sw.Elapsed < TimeSpan.FromSeconds(5))
      await Task.Delay(20);
  ```
  For the counter variant, poll until `sink.Count >= expectedMinimum`. This eliminates the timing dependency while keeping tests self-contained (no added packages).
- **Impact:** Removes 3 hard-coded delay values; makes tests reliable under load; eliminates the semantically confusing `shouldThrow ? 200 : 100` branch.

---

### `CooldownSeconds = 0` silently faults the pump instead of meaning "no dedupe"
- **Type:** Refactor
- **Effort:** Moderate
- **Locations:**
  - `src/FrigateRelay.Host/Dispatch/DedupeCache.cs` — `TryEnter` passes `TimeSpan.FromSeconds(CooldownSeconds)` directly to `MemoryCache` as `AbsoluteExpirationRelativeToNow`
  - `tests/FrigateRelay.Host.Tests/Observability/EventPumpSpanTests.cs` — all fixtures set `CooldownSeconds = 1` to avoid the exception
  - `tests/FrigateRelay.Host.Tests/Observability/CounterIncrementTests.cs` — same workaround
  - `tests/FrigateRelay.IntegrationTests/Observability/TraceSpansCoverFullPipelineTests.cs` — same workaround
- **Description:** `MemoryCache` throws `ArgumentOutOfRangeException` when `AbsoluteExpirationRelativeToNow <= TimeSpan.Zero`. Setting `CooldownSeconds = 0` (the natural value for "disable deduplication in tests") therefore causes `PumpFaulted`, which silently swallows all spans after `event.match`. The workaround — forcing `CooldownSeconds = 1` in every test fixture — is a latent footgun: any future test that forgets this convention will produce confusing failures with no counter-or-span output and no clear error message. The fix belongs in the production code, not spread across three test files.
- **Suggestion:** In `DedupeCache.TryEnter`, add a guard before calling `MemoryCache.Set`:
  ```csharp
  if (CooldownSeconds <= 0) return true; // no dedupe — always allow
  ```
  This makes `CooldownSeconds = 0` a meaningful no-dedupe sentinel, allows test fixtures to express their intent clearly, and removes the constraint comment that must be remembered across test files.
- **Impact:** Removes 3 workaround comments/values across test fixtures; fixes a production edge case (operator sets `CooldownSeconds: 0` expecting "off" but gets a faulted pump); simplifies test fixture authoring going forward.

---

## Medium Priority

### `BuildListener` / `BuildTracer` / `RunPumpAsync` helpers duplicated across test files
- **Type:** Consolidate
- **Effort:** Moderate
- **Locations:**
  - `tests/FrigateRelay.Host.Tests/Observability/EventPumpSpanTests.cs` — `BuildTracer(...)`, `RunPumpAsync(...)`
  - `tests/FrigateRelay.Host.Tests/Observability/CounterIncrementTests.cs` — `BuildListener(...)`, `RunPumpAsync(...)`, `RunDispatcherAsync(...)`
  - `tests/FrigateRelay.IntegrationTests/Observability/TraceSpansCoverFullPipelineTests.cs` — inline `SimpleActivityExportProcessor` + `ConfigureOpenTelemetryTracerProvider` wiring
- **Description:** Two unit test files independently define `RunPumpAsync` helpers with near-identical structure: build a minimal `SubscriptionOptions`, wire a `FaultingSource` or real source, start the pump, wait for output. `BuildTracer` (span exporter wiring via `SimpleActivityExportProcessor`) and `BuildListener` (MeterListener setup with `lock`-guarded sink) are defined once each but will need to be replicated the moment a third test class needs OTel assertions. The Rule of Three is met at 2 occurrences for `RunPumpAsync`; the infrastructure pattern (tracer/listener setup) appears in both unit and integration test assemblies. Additionally, `tests/FrigateRelay.IntegrationTests/FrigateRelay.IntegrationTests.csproj` currently has no `ProjectReference` to `FrigateRelay.TestHelpers`, so the shared pattern cannot be adopted without first adding that reference.
- **Suggestion:** Phase 10 adds a new plugin (FrigateSnapshot); if any Phase 10 test needs OTel assertions, that triggers the Rule of Three and extraction becomes clearly warranted. At that point:
  1. Add `<ProjectReference Include="../../tests/FrigateRelay.TestHelpers/..." />` to `FrigateRelay.IntegrationTests.csproj`.
  2. Move `BuildTracer(ICollection<Activity>)`, `BuildListener(ICollection<(string name, long value, TagList tags)>)`, and the canonical `RunPumpAsync` body into `tests/FrigateRelay.TestHelpers/ObservabilityTestHelpers.cs`.
  3. Update call sites in `EventPumpSpanTests`, `CounterIncrementTests`, and `TraceSpansCoverFullPipelineTests`.
- **Impact:** Defers until Rule of Three is cleanly met. When extracted: ~60 lines consolidated; single canonical OTel test-wiring pattern; `TestHelpers` already in CI/Jenkins invocation scope.

---

### Validator-span parentage assertion missing from integration test
- **Type:** Refactor
- **Effort:** Trivial
- **Locations:**
  - `tests/FrigateRelay.IntegrationTests/Observability/TraceSpansCoverFullPipelineTests.cs:112–125`
- **Description:** `TraceSpans_CoverFullPipeline` asserts that action spans exist and share a `TraceId` with the root span, but does not assert that any `validator.codeprojectai.check` span exists or that its `ParentSpanId` matches the enclosing `action.pushover.execute` span. The plan (D8) and reviewer (REVIEW-3.1 Minor #2) both flag this gap. The test passes today because span existence is not asserted — a refactor that accidentally breaks validator-span emission would not be caught.
- **Suggestion:** After the existing action-span assertions, add:
  ```csharp
  var validatorSpan = allSpans.FirstOrDefault(a => a.DisplayName == "validator.codeprojectai.check");
  Assert.IsNotNull(validatorSpan, "validator.codeprojectai.check span must exist");
  Assert.AreEqual(pushoverSpan!.SpanId, validatorSpan!.ParentSpanId,
      "validator span must be a child of action.pushover.execute");
  ```
- **Impact:** 3 lines added; closes the D8 test-coverage gap identified in REVIEW-3.1; prevents silent regression if validator-span emission is broken.

---

### `Activity.Current?.Context ?? default` capture pattern not encapsulated
- **Type:** Refactor
- **Effort:** Trivial
- **Locations:**
  - `src/FrigateRelay.Host/EventPump.cs` — `EnqueueAsync` call site (producer-side context capture)
  - `src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs` — `EnqueueAsync` implementation (where the pattern was introduced per SUMMARY-1.1)
- **Description:** The pattern `Activity.Current?.Context ?? default` appears at the producer write site and is conceptually the "capture current trace context for channel propagation" idiom. It is only 2 occurrences (below Rule of Three for extraction) but the intent is non-obvious to a reader unfamiliar with OTel channel propagation. An inline comment is present per SUMMARY-1.1's description but a small named helper would make the intent clearer at no runtime cost.
- **Suggestion:** If a third producer site is added (e.g., a future `IEventSource` that enqueues directly), extract a one-liner extension method:
  ```csharp
  internal static class ActivityExtensions
  {
      internal static ActivityContext CaptureContext()
          => Activity.Current?.Context ?? default;
  }
  ```
  Until a third call site materializes, a single-line comment (`// capture trace context for channel propagation`) at each existing site is sufficient and cheaper than adding a new file.
- **Impact:** Currently: add a comment at 2 sites (trivial). On third occurrence: 5-line extension class, 2 call sites updated.

---

## Low Priority

- **`DispatcherDiagnostics.cs` — 10 identical `Meter.CreateCounter<long>(...)` initializers.** `src/FrigateRelay.Host/Dispatch/DispatcherDiagnostics.cs`. The repetition is idiomatic for the OTel API and each counter has distinct name/description/unit, so no extraction is warranted. Flag only: if a future phase adds more than 3 additional counters, a factory helper `static Counter<long> C(string name, string desc) => Meter.CreateCounter<long>(name, description: desc)` at the top of the class would reduce per-counter boilerplate from 3 lines to 1. Effort: trivial when triggered.

- **Duplicate ID-17 entry in `.shipyard/ISSUES.md`.** Lines ~237–255 contain a second ID-17 entry marked "Open" with the original env-var-fallback description. The first ID-17 entry (lines ~193–204) is marked CLOSED. REVIEW-3.1 flagged this. Append `*[CLOSED 2026-04-27]*` and a "Duplicate — see first ID-17 entry" note to the second entry. Effort: trivial housekeeping; no code change.

- **`OpenTelemetry.Exporter.InMemory` version 1.11.2 vs plan-spec 1.15.3.** `tests/FrigateRelay.Host.Tests/FrigateRelay.Host.Tests.csproj:26` and `tests/FrigateRelay.IntegrationTests/FrigateRelay.IntegrationTests.csproj:23`. The builder used 1.11.2 (latest available at the time) and worked around the missing `AddInMemoryExporter` overload with `SimpleActivityExportProcessor`. The workaround is correct. Either bump both test csprojs to 1.15.3 and use the simpler overload, or add a comment noting the version pin rationale. Effort: trivial version bump or comment.

- **`tests/FrigateRelay.IntegrationTests/FrigateRelay.IntegrationTests.csproj` has no `ProjectReference` to `FrigateRelay.TestHelpers`.** Noted in REVIEW-3.1 Suggestion #3. No current test in that project requires `CapturingLogger<T>` or other shared helpers, but adding the reference proactively mirrors `Host.Tests` and prevents a future PR adding a test that needs the helper from also having to add the reference. Effort: one csproj line.

---

## Summary

- **Duplication found:** 2 instances (`RunPumpAsync` in 2 test files; magic-delay pattern in 3 locations)
- **Dead code found:** 0 — no unused definitions detected; `DispatchItem.Activity` old shape cleanly removed
- **Complexity hotspots:** 0 — all new Phase 9 methods are well-scoped; no function exceeds 40 lines or 3 nesting levels
- **AI bloat patterns:** 1 — `shouldThrow ? 200 : 100` conditional delay encodes implicit timing assumptions; belongs in the magic-delay finding above
- **Estimated cleanup impact:** ~10 lines of magic delay replaced with polling; `CooldownSeconds = 0` guard is 2 lines of production code eliminating 3 workaround comments; validator-span assertion is 3 lines closing a D8 gap

## Recommendation

**Simplification recommended before Phase 10 begins for the two High-priority findings only.**

The `CooldownSeconds = 0` silent-fault is a latent footgun — every future observability test author must know to use `CooldownSeconds = 1` or face confusing pump-fault failures with no clear error. A 2-line production fix in `DedupeCache.TryEnter` is cheaper than the tribal knowledge it currently requires. The magic-delay replacement is mechanical and should be done in the same pass.

The Medium and Low findings are non-blocking. The test-helper extraction (Medium #1) should wait for the Rule of Three to be met cleanly in Phase 10. The validator-span assertion gap (Medium #2) is a 3-line add that can be done opportunistically. The ISSUES.md duplicate ID-17 entry (Low) is housekeeping that takes 30 seconds.
