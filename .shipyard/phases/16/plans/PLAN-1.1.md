---
phase: 16-v1.3.0-minor
plan: 1.1
wave: 1
dependencies: []
must_haves:
  - "MetricsTagsOptions record + internal sealed MetricsTagWriter under src/FrigateRelay.Host/Observability/ (greenfield directory)."
  - "MetricsTagWriter.NormalizeCameraTag(string?) folds unknown cameras to 'other' when KnownCameras is non-empty; passthrough when empty (preserves current behavior)."
  - "Allowlist match uses HashSet<string>(StringComparer.OrdinalIgnoreCase) per CONTEXT-16 D3."
  - "Singleton DI registration with IOptionsMonitor<MetricsTagsOptions> bound from Otel:MetricsTags config section."
  - "EventPump and ChannelActionDispatcher constructors accept MetricsTagWriter; every camera argument passed to DispatcherDiagnostics.IncrementXxx is wrapped in NormalizeCameraTag."
  - "DispatcherDiagnostics static class is NOT modified (RC-3) — normalization happens at the callers."
  - "TDD: ≥3 tests in tests/FrigateRelay.Host.Tests/Observability/MetricsCardinalityTests.cs (known-camera passthrough, unknown folded to 'other', empty-allowlist passthrough). All 151 baseline Host tests still pass."
  - "docs/observability.md gains an Otel:MetricsTags:KnownCameras section; README Observability section links to it."
  - "CHANGELOG.md [Unreleased] ### Added entry for #18."
files_touched:
  - src/FrigateRelay.Host/Observability/MetricsTagWriter.cs
  - src/FrigateRelay.Host/Observability/MetricsTagsOptions.cs
  - src/FrigateRelay.Host/EventPump.cs
  - src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs
  - src/FrigateRelay.Host/HostBootstrap.cs
  - tests/FrigateRelay.Host.Tests/Observability/MetricsCardinalityTests.cs
  - tests/FrigateRelay.Host.Tests/Observability/CounterIncrementTests.cs
  - tests/FrigateRelay.Host.Tests/Observability/EventPumpSpanTests.cs
  - docs/observability.md
  - README.md
  - CHANGELOG.md
tdd: true
risk: medium
---

# Plan 1.1: MetricsTagWriter + KnownCameras allowlist (#18)

## Context

Issue #18 adds a cardinality bound on the `camera` metrics tag — operators populate `Otel:MetricsTags:KnownCameras: string[]` and any camera value not in the allowlist is folded to literal `"other"` before reaching `Counter<long>.Add`. This is the **load-bearing reason for the v1.3.0 minor SemVer bump** (CONTEXT-16). Per CONTEXT-16 D1 (refined post-research), `MetricsTagWriter` is a string-normalizer injected at the **callers** (`EventPump` + `ChannelActionDispatcher`) — it cannot wrap `Counter<T>.Add` because `DispatcherDiagnostics` is a `static class` (RESEARCH.md RC-3). D3 mandates `OrdinalIgnoreCase`; D4 scopes to cameras-only (no `KnownLabels` in v1.3.0); D5 fixes the config key shape; D8 puts the CHANGELOG entry under `### Added`.

## Dependencies

None — Wave 1 root. Disjoint files from PLAN-1.2 and PLAN-1.3 except CHANGELOG.md (sequential dispatch in build phase eliminates merge friction per Phase 15 lesson).

## Tasks

### Task 1: Add MetricsTagsOptions + MetricsTagWriter (TDD red phase first)
**Files:**
- `src/FrigateRelay.Host/Observability/MetricsTagsOptions.cs` (new)
- `src/FrigateRelay.Host/Observability/MetricsTagWriter.cs` (new)
- `src/FrigateRelay.Host/HostBootstrap.cs` (modify)
- `tests/FrigateRelay.Host.Tests/Observability/MetricsCardinalityTests.cs` (new)

**Action:** create + modify (TDD)

**Description:**
1. Write the failing tests **first** in `MetricsCardinalityTests.cs`. Mirror the pattern from `tests/FrigateRelay.Host.Tests/Observability/CounterTagMatrixTests.cs` (helper-level, not pipeline-level). Three tests minimum:
   - `NormalizeCameraTag_KnownCamera_ReturnsInputUnchanged` — `KnownCameras = ["Driveway"]`, input `"Driveway"` → returns `"Driveway"`. Also assert `"driveway"` (lower) → returns `"driveway"` (preserves caller casing on match per OrdinalIgnoreCase membership-only semantics).
   - `NormalizeCameraTag_UnknownCamera_ReturnsOther` — `KnownCameras = ["Driveway"]`, input `"AttackerInjected"` → returns `"other"`.
   - `NormalizeCameraTag_EmptyAllowlist_ReturnsInputUnchanged` — `KnownCameras = []`, input `"AnythingAtAll"` → returns `"AnythingAtAll"`.
   Construct the writer with a `StaticOptionsMonitor<MetricsTagsOptions>` stub (the pattern already exists in the Observability test files per RESEARCH.md RC-5 — reuse rather than reinvent).
2. `MetricsTagsOptions.cs`: `internal sealed record MetricsTagsOptions { public string[] KnownCameras { get; init; } = Array.Empty<string>(); }`. Co-located with the writer per OQ-1 resolution.
3. `MetricsTagWriter.cs`: `internal sealed class MetricsTagWriter`. Constructor takes `IOptionsMonitor<MetricsTagsOptions>`. Build the `HashSet<string>(StringComparer.OrdinalIgnoreCase)` lazily on each call against `monitor.CurrentValue.KnownCameras` (or cache + `OnChange` — implementer's choice; spec is "picks up rebinding"). `NormalizeCameraTag(string? camera)`: null/empty → return input as-is; allowlist empty → return input as-is; allowlist non-empty + member → return input as-is; allowlist non-empty + non-member → return literal `"other"`.
4. `HostBootstrap.cs`: in `ConfigureServices`, immediately adjacent to the existing OTel block (line ~44 per RESEARCH.md), add:
   - `services.Configure<MetricsTagsOptions>(config.GetSection("Otel:MetricsTags"));`
   - `services.AddSingleton<MetricsTagWriter>();`

**TDD:** true

**Acceptance Criteria:**
- New test file present with ≥3 tests; all pass.
- `MetricsTagWriter` and `MetricsTagsOptions` are `internal sealed` and live under the new `src/FrigateRelay.Host/Observability/` directory.
- `dotnet build FrigateRelay.sln -c Release` zero warnings.

### Task 2: Inject MetricsTagWriter into EventPump + ChannelActionDispatcher and normalize at every camera-emitting site
**Files:**
- `src/FrigateRelay.Host/EventPump.cs`
- `src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs`
- `tests/FrigateRelay.Host.Tests/Observability/CounterIncrementTests.cs`
- `tests/FrigateRelay.Host.Tests/Observability/EventPumpSpanTests.cs`

**Action:** modify

**Description:**
1. Add `MetricsTagWriter metricsTagWriter` to `EventPump`'s constructor; store in field; at every call into `DispatcherDiagnostics.IncrementEventsReceived(...)` and `DispatcherDiagnostics.IncrementEventsMatched(...)` (RESEARCH.md camera-tag write surface table), construct a normalized `EventContext` for the helper call **or** introduce a thin local pattern: pass `_tagWriter.NormalizeCameraTag(ctx.Camera)` and adjust the call shape minimally. Implementer's choice between (a) overloading the helper to accept a pre-normalized camera string or (b) creating a normalized `EventContext` local. **Preferred:** the static helpers already read `ctx.Camera`/`item.Context.Camera` directly — the cheapest change is to add overloads on `DispatcherDiagnostics` that accept an explicit `string camera` argument and have the existing `EventContext`-taking methods delegate to them. The new code path passes the normalized value; the test surface (`CounterTagMatrixTests`) is unchanged because it exercises the same end-state `TagList`.
   - **NOTE:** This adjustment to `DispatcherDiagnostics` is **additive overloads only** — do NOT modify the existing static helper signatures (RC-3 holds: no signature breakage).
2. Same treatment for `ChannelActionDispatcher`: constructor gains `MetricsTagWriter`; every call into `IncrementActionsDispatched/Succeeded/Failed/ValidatorsPassed/ValidatorsRejected/Drops/Exhausted` is normalized at the call site.
3. Update test helpers in `CounterIncrementTests.cs` and `EventPumpSpanTests.cs` (~10 builder methods per RC-5) that `new` `EventPump`/`ChannelActionDispatcher` directly. Inject a passthrough `MetricsTagWriter` (empty-allowlist `StaticOptionsMonitor<MetricsTagsOptions>`). Existing tests continue to pass unchanged (empty allowlist == passthrough).
4. **RC-4 sanity:** before adding the new constructor parameter, verify the baseline 151 tests pass at HEAD with `dotnet run --project tests/FrigateRelay.Host.Tests -c Release`. The `RunDispatcherAsync` call site at line ~419 is uncertain (RESEARCH.md uncertainty flag) — if the file fails to compile at HEAD, that is a pre-existing condition not introduced by this plan; report and stop.

**TDD:** false (refactor under existing test coverage — Task 1 added the new behavioral coverage; this task wires it through and must not regress the 151 baseline)

**Acceptance Criteria:**
- `EventPump` and `ChannelActionDispatcher` accept `MetricsTagWriter` in their constructors.
- All camera-tagged counter call sites pass `_metricsTagWriter.NormalizeCameraTag(camera)` (greppable: `git grep -n 'NormalizeCameraTag' src/FrigateRelay.Host/` returns ≥8 hits — one per camera-tagged helper invocation).
- All 151 existing Host tests pass + 3 new from Task 1 = 154 minimum. Use "all existing pass + N net-new pass" framing per the Phase 15 test-count lesson rather than a hard "151 + 3" gate.
- `dotnet build FrigateRelay.sln -c Release` zero warnings on Linux + Windows (CI matrix gate).

### Task 3: Documentation + CHANGELOG
**Files:**
- `docs/observability.md`
- `README.md`
- `CHANGELOG.md`

**Action:** modify (docs)

**Description:**
1. `docs/observability.md`: add a new section titled `Bounding Camera Tag Cardinality (`Otel:MetricsTags:KnownCameras`)`. Cover: motivation (defense against camera-name explosion / attacker-influenced tag values), config shape (the JSON snippet from CONTEXT-16 D5), env-var form (`Otel__MetricsTags__KnownCameras__0=Front`), case-insensitive matching note (D3 — and the explicit divergence from project convention per D3's last paragraph), default behavior when empty (passthrough — preserves current behavior). Mirror the Phase 15 GAP-2/GAP-3 documentation commit pattern from PR #45.
2. `README.md`: in the existing "Observability" section, add a one-line bullet linking to the new `docs/observability.md` section.
3. `CHANGELOG.md` `[Unreleased]` `### Added` entry per CONTEXT-16 D8: `- New \`Otel:MetricsTags:KnownCameras\` config (issue #18) bounds the cardinality of the \`camera\` metrics tag — operators populate the allowlist; unknown cameras are folded to \`"other"\`. Default empty array preserves current behavior. Case-insensitive (\`OrdinalIgnoreCase\`).`

**TDD:** false

**Acceptance Criteria:**
- `docs/observability.md` contains a `KnownCameras` heading and the JSON snippet.
- `grep -n KnownCameras README.md docs/observability.md CHANGELOG.md` returns ≥3 hits across the three files.
- `CHANGELOG.md` `[Unreleased]` block contains an `### Added` line referencing #18.

## Verification

```bash
# Build clean
dotnet build FrigateRelay.sln -c Release

# New cardinality tests pass
dotnet run --project tests/FrigateRelay.Host.Tests -c Release -- --filter "MetricsCardinalityTests"

# Baseline + new pass
dotnet run --project tests/FrigateRelay.Host.Tests -c Release

# Greppable invariants
git grep -n 'NormalizeCameraTag' src/FrigateRelay.Host/   # >=8 hits at counter call sites
git grep -nE 'Otel:MetricsTags|KnownCameras' src/ docs/ README.md CHANGELOG.md
git grep -n 'class MetricsTagWriter' src/FrigateRelay.Host/Observability/   # exactly 1 hit
git grep -n 'record MetricsTagsOptions' src/FrigateRelay.Host/Observability/   # exactly 1 hit

# PROJECT.md invariants still hold
git grep -nE 'App\.Metrics|OpenTracing|Jaeger\.' src/   # empty
git grep -nE '\.(Result|Wait)\(' src/                   # empty
git grep ServicePointManager src/                        # empty
```

## Notes

- **CHANGELOG.md is shared with PLAN-1.2 and PLAN-1.3.** Sequential dispatch in build phase eliminates merge friction (Phase 15 lesson).
- **Do not modify `DispatcherDiagnostics` signatures** — only add overloads if needed (RC-3). The static class shape is intentional v1.1 architecture.
- **Do not introduce `KnownLabels`** — D4 scopes v1.3.0 to cameras-only.
