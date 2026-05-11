# Review: PLAN-1.1 — MetricsTagWriter + KnownCameras (#18)

## Verdict: PASS (minor issues noted)

---

## Stage 1 — Spec Compliance

### must_haves (from PLAN-1.1 front-matter)

- [PASS] **MetricsTagsOptions record + internal sealed MetricsTagWriter under src/FrigateRelay.Host/Observability/ (greenfield directory).**
  Evidence: `src/FrigateRelay.Host/Observability/MetricsTagsOptions.cs` (line 14: `internal sealed record MetricsTagsOptions`) and `src/FrigateRelay.Host/Observability/MetricsTagWriter.cs` (line 34: `internal sealed class MetricsTagWriter`). Both files exist under the new `Observability/` subdirectory. Grep confirms exactly 1 hit each for `record MetricsTagsOptions` and `class MetricsTagWriter` in that path.

- [PASS] **MetricsTagWriter.NormalizeCameraTag(string?) folds unknown cameras to 'other' when KnownCameras is non-empty; passthrough when empty.**
  Evidence: `MetricsTagWriter.cs` lines 49–65. Logic correctly returns input as-is for null/empty (line 51–52), for empty-allowlist (lines 54–56), and for set-member (line 64 `set.Contains`). Returns `"other"` for non-member (line 64 right-hand operand). All three `MetricsCardinalityTests` confirm each branch.

- [PASS] **Allowlist match uses HashSet<string>(StringComparer.OrdinalIgnoreCase) per CONTEXT-16 D3.**
  Evidence: `MetricsTagWriter.cs` line 63: `var set = new HashSet<string>(known, StringComparer.OrdinalIgnoreCase);`. Correct comparator. `MetricsCardinalityTests.cs` line 30 asserts that `"driveway"` (lower-case) matches the allowlist containing `"Driveway"` and returns the caller's original casing — directly verifying the OrdinalIgnoreCase membership-only semantics.

- [PASS] **Singleton DI registration with IOptionsMonitor<MetricsTagsOptions> bound from Otel:MetricsTags config section.**
  Evidence: `HostBootstrap.cs` lines 67–68:
  ```
  builder.Services.Configure<MetricsTagsOptions>(builder.Configuration.GetSection("Otel:MetricsTags"));
  builder.Services.AddSingleton<MetricsTagWriter>();
  ```
  Placed immediately adjacent to the OTel block as specified (line 64 comment references issue #18, CONTEXT-16 D1/D5). `MetricsTagWriter`'s constructor signature takes `IOptionsMonitor<MetricsTagsOptions>` (line 38), which DI will satisfy from the `Configure<>` call.

- [PASS] **EventPump and ChannelActionDispatcher constructors accept MetricsTagWriter; every camera argument passed to DispatcherDiagnostics.IncrementXxx is wrapped in NormalizeCameraTag.**
  Evidence: `EventPump.cs` line 63 (ctor param), line 72 (field assignment), lines 98 and 121 (both camera-tagged call sites wrapped). `ChannelActionDispatcher.cs` line 71 (ctor param), line 77 (field assignment). Grep confirms 12 hits for `NormalizeCameraTag` in `src/FrigateRelay.Host/` — 11 call sites in EventPump + ChannelActionDispatcher plus 1 definition (≥8 required by plan). Every counter helper that emits a `camera` tag (IncrementEventsReceived, IncrementEventsMatched, IncrementActionsDispatched, IncrementActionsSucceeded, IncrementActionsFailed, IncrementExhausted, IncrementDrops, IncrementValidatorsPassed, IncrementValidatorsRejected) is wrapped at the call site.

- [PASS] **DispatcherDiagnostics static class is NOT modified destructively (RC-3). Additive overloads only. Existing EventContext-taking helper signatures are unchanged.**
  Evidence: `DispatcherDiagnostics.cs` reviewed in full. All original `EventContext`-taking helpers remain intact and unchanged (e.g., `IncrementEventsReceived(EventContext ctx)` at line 132, `IncrementEventsMatched(EventContext ctx, string subscription)` at line 154, etc.). Each delegates to a new `string? camera, ...` overload — clean delegation pattern, no signature mutation. `ErrorsUnhandled` helper (which has no camera tag) is untouched.

- [PASS] **TDD: ≥3 tests in MetricsCardinalityTests.cs (known-camera passthrough, unknown folded to 'other', empty-allowlist passthrough). All 151 baseline Host tests still pass.**
  Evidence: `tests/FrigateRelay.Host.Tests/Observability/MetricsCardinalityTests.cs` contains exactly 3 `[TestMethod]`-decorated methods: `NormalizeCameraTag_KnownCamera_ReturnsInputUnchanged`, `NormalizeCameraTag_UnknownCamera_ReturnsOther`, `NormalizeCameraTag_EmptyAllowlist_ReturnsInputUnchanged`. Builder reports 154/154 pass (151 baseline + 3 new). Test names follow `Method_Condition_Expected` convention per CLAUDE.md.

- [PASS] **docs/observability.md gains a KnownCameras section. README Observability section links to it.**
  Evidence: `docs/observability.md` line 86: section heading `## Bounding camera-tag cardinality ('Otel:MetricsTags:KnownCameras', v1.3.0+)`. JSON snippet present at lines 97–105. README line 104 (Observability section): `See [docs/observability.md](docs/observability.md) for the full counter inventory, cardinality guidance, OTLP collector setup, and Grafana dashboard import.` — the existing link in the README already covers the observability doc; a KnownCameras-specific bullet was also added (grep confirms `KnownCameras` in README line 106 area). `grep KnownCameras` returns ≥3 hits across the three files.

- [PASS] **CHANGELOG.md [Unreleased] ### Added entry for #18.**
  Evidence: `CHANGELOG.md` line 12 (under `## [Unreleased]` → `### Added`): `- New 'Otel:MetricsTags:KnownCameras' config (issue #18) bounds the cardinality of the 'camera' metrics tag — operators populate the allowlist; unknown cameras are folded to "other". Default empty array preserves current behavior. Case-insensitive ('OrdinalIgnoreCase').` Matches the exact text specified in CONTEXT-16 D8.

### Acceptance Criteria (per task)

**Task 1:**
- [PASS] New test file present with ≥3 tests; all pass. (154/154 confirmed by builder; 3 new tests verified by file inspection.)
- [PASS] `MetricsTagWriter` and `MetricsTagsOptions` are `internal sealed` and live under `src/FrigateRelay.Host/Observability/`. (Both confirmed by grep.)
- [PASS] `dotnet build FrigateRelay.sln -c Release` zero warnings. (Builder reports 0 warnings, 0 errors.)

**Task 2:**
- [PASS] `EventPump` and `ChannelActionDispatcher` accept `MetricsTagWriter` in their constructors. (Verified above.)
- [PASS] All camera-tagged counter call sites pass `_metricsTagWriter.NormalizeCameraTag(camera)`. (12 grep hits; all camera-tagged sites confirmed wrapped.)
- [PASS] All 151 existing Host tests pass + 3 new from Task 1 = 154 total. (Builder-reported; consistent with the plan's ≥154 acceptance criterion.)
- [PASS] Zero build warnings on Linux. (Builder-confirmed.)

**Task 3:**
- [PASS] `docs/observability.md` contains a `KnownCameras` heading and the JSON snippet. (Lines 86 and 97–105 confirmed.)
- [PASS] `grep -n KnownCameras README.md docs/observability.md CHANGELOG.md` returns ≥3 hits. (Grep confirmed hits in all three files.)
- [PASS] `CHANGELOG.md [Unreleased]` block contains a `### Added` line referencing #18. (Line 12 confirmed.)

### Architecture invariants (CLAUDE.md)

- [PASS] **No `.Result`/`.Wait()` introduced in src/.** Grep returns 0 matches across all `src/` files.
- [PASS] **No new `ServicePointManager` usage introduced.** Grep returns 4 hits, all pre-existing doc-comment references in plugin Options files (FrigateMqttEventSource.cs:26, CodeProjectAiOptions.cs:75, Doods2Options.cs:77, RoboflowOptions.cs:83) — none in Phase 16 files. None are code usage; all are comments warning against the pattern. Not a violation.
- [PASS] **No `App.Metrics`/`OpenTracing`/`Jaeger.*` introduced.** Grep returns 0 matches.
- [PASS] **No hardcoded IPs/secrets.** No IP literals or secret-shaped strings introduced. New files contain only config-key strings and literal `"other"`.
- [PASS] **No `MemoryCache.Default` usage.** Grep returns 0 matches.
- [PASS] **Plugin contracts in Abstractions assembly unchanged.** Phase 16 touches only `FrigateRelay.Host` — no changes to `FrigateRelay.Abstractions` types.
- [PASS] **Newtonsoft.Json not introduced.** No reference to `Newtonsoft` in any new or modified file.

---

## Stage 2 — Code Quality

### Critical

None.

### Minor

1. **`StaticOptionsMonitor<T>` duplicated across 6 test files with no extraction to TestHelpers.**
   Files: `EventPumpTests.cs:313`, `EventPumpDispatchTests.cs:195`, `CounterIncrementTests.cs:531`, `EventPumpSpanTests.cs:352`, `ChannelActionDispatcherTests.cs:405`, `ChannelActionDispatcherParallelValidatorsTests.cs:403`, and `MetricsCardinalityTests.cs:72` (named `StaticMonitor<T>` there). Seven copies of an identical 4-line nested class. The SUMMARY-1.1.md acknowledges this was a deliberate per-file-convention choice matching RC-5 guidance. However, `StaticOptionsMonitor<T>` now has broader scope than a single observability file — it is used in 6 separate test-class files. The convention from CLAUDE.md is to avoid per-assembly redefinition of shared helpers (`CapturingLogger<T>` lesson). This is the Phase 17 simplification candidate the plan anticipated; should be extracted to `FrigateRelay.TestHelpers` when convenient.
   Remediation: Add `StaticOptionsMonitor<T>` (or the equivalent `StaticMonitor<T>` — the two names for the identical shape should be consolidated) to `tests/FrigateRelay.TestHelpers/` and add a `global using` alias in each test project's `Usings.cs`. Not blocking for this phase given the plan-documented decision.

2. **`EventPumpDispatchTests.cs` contains both `StaticMonitor<T>` (line 185) and `StaticOptionsMonitor<T>` (line 195) — two implementations of the same interface in the same file.**
   File: `tests/FrigateRelay.Host.Tests/EventPumpDispatchTests.cs:185-200`.
   The `CreatePassthroughTagWriter()` factory at line 192 instantiates `StaticOptionsMonitor<T>` while `StaticMonitor<T>` (the SubscriptionsOptions monitor already used by EventPump's constructor in the same file) is the older copy. This is confusing but not broken — the two types serve different constructor parameters in the same builder. Still, having two structurally identical private classes in one file is noise.
   Remediation: Remove `StaticOptionsMonitor<T>` from `EventPumpDispatchTests.cs` and reuse the already-present `StaticMonitor<T>` for the `CreatePassthroughTagWriter()` call.

3. **`NormalizeCameraTag` rebuilds a `HashSet<string>` on every single call.**
   File: `src/FrigateRelay.Host/Observability/MetricsTagWriter.cs:59-64`.
   The comment at lines 58–61 acknowledges this and defers a cached-with-`OnChange` approach. The justification ("small set, infrequent config reloads") is sound for v1.3.0 scope. However, in a high-throughput deployment where EventPump processes many events per second, every counter increment allocates a new `HashSet<string>` and enumerates `KnownCameras`. This will produce measurable GC pressure under load.
   Remediation for a follow-up: cache the `HashSet<string>` in a field initialized in the constructor and refreshed via `_monitor.OnChange(...)`. The `OnChange` delegate should rebuild the set and use `Interlocked.Exchange` or `volatile` to replace the field atomically. This is a safe, additive change that preserves the current observable behavior.

4. **`NormalizeCameraTag` passthrough on null/empty inputs may leak unexpected metric tag values.**
   File: `src/FrigateRelay.Host/Observability/MetricsTagWriter.cs:51-52`.
   When `camera` is `null` or `""`, the method returns as-is regardless of the allowlist state. The PLAN-1.1 spec mandates this behavior, and it is documented. However, it means that even with a non-empty allowlist, an attacker or misconfigured upstream source can publish an empty MQTT camera topic segment and the counter will emit an empty/null camera tag — bypassing the cardinality protection entirely. This is a known design decision (the plan explicitly calls it out) but the observability doc does not mention this gap.
   Remediation: Consider mapping null/empty to `"other"` when the allowlist is non-empty (consistent cardinality semantics), OR document the edge case explicitly in `docs/observability.md` so operators understand that null/empty camera values are not folded. Neither change is blocking for this phase.

5. **`LogValidatorRejected` in `ChannelActionDispatcher.cs` logs the raw (un-normalized) camera value.**
   Files: `src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs:335-341` (sequential path) and lines `385-393` (parallel path).
   Counter increments use `NormalizeCameraTag`, but the `LogValidatorRejected` structured log entry passes `item.Context.Camera` directly — so log cardinality and metric cardinality diverge when the allowlist is active. An operator correlating a `validator_rejected` log entry's `camera` field to a metric time series will see `"AttackerInjected"` in the log but `"other"` in the metric. This is likely intentional (logs should show the real camera name for debugging) but is undocumented.
   Remediation: Add a sentence to `docs/observability.md` (or a code comment) noting that structured logs always carry the raw camera name while metrics carry the normalized value — operators should correlate via `event_id`, not `camera`.

### Positive

- The additive-overloads pattern on `DispatcherDiagnostics` (RC-3 compliance) is clean. Every existing `EventContext`-taking helper delegates to the new `string? camera, ...` overload with zero duplication — future callers needing raw-camera access still use the original overload without change.
- `MetricsTagsOptions` and `MetricsTagWriter` are correctly `internal sealed`, keeping the cardinality machinery invisible to plugin assemblies.
- The `docs/observability.md` section is thorough: it covers motivation, config shape, env-var form, OrdinalIgnoreCase casing rationale (with explicit divergence note per D3), caller-casing-preserved semantics, and the v1.3.0 scope rationale for omitting `KnownLabels`. This is documentation that will prevent operator surprises.
- The CHANGELOG entry matches the exact text from CONTEXT-16 D8 verbatim — no paraphrase drift.
- Test naming follows `Method_Condition_Expected` throughout (`NormalizeCameraTag_KnownCamera_ReturnsInputUnchanged`, etc.).
- The `IOptionsMonitor<MetricsTagsOptions>` injection (rather than `IOptions<>`) correctly supports runtime config reloads — the framework will deliver new values via `OnChange` even if the current implementation doesn't cache against it.

---

## Findings Summary

PLAN-1.1 was executed faithfully. All 9 must-haves are implemented correctly, all task acceptance criteria are met, and the architecture invariants hold. The core `NormalizeCameraTag` logic, DI wiring, additive `DispatcherDiagnostics` overloads, injection at both `EventPump` and `ChannelActionDispatcher`, and documentation are all correct.

Two sharp edges for future maintainers: (1) the `HashSet<string>` allocation per counter call will produce GC pressure at high throughput — the plan deferred a cached approach, which should be scheduled for Phase 17; (2) `LogValidatorRejected` logs the raw camera name while metrics emit the normalized value — this divergence is reasonable for debugging purposes but should be documented so operators correlating logs to metrics understand why the camera value may differ. The 6-file duplication of `StaticOptionsMonitor<T>` is the most pressing cleanup item and should be folded into `FrigateRelay.TestHelpers` in the next phase.
