# Build Summary: PLAN-1.1 — MetricsTagWriter + KnownCameras (#18)

## Status: complete

## Tasks Completed

- **Task 1: Add MetricsTagsOptions + MetricsTagWriter (TDD red phase first)** — complete
  - Files: `src/FrigateRelay.Host/Observability/MetricsTagWriter.cs` (new), `src/FrigateRelay.Host/Observability/MetricsTagsOptions.cs` (new), `src/FrigateRelay.Host/HostBootstrap.cs` (modified), `tests/FrigateRelay.Host.Tests/Observability/MetricsCardinalityTests.cs` (new — 3 tests)
  - Commit: `c5ecc44 shipyard(phase-16): add MetricsTagWriter + MetricsTagsOptions for camera allowlist (#18)`
- **Task 2: Inject MetricsTagWriter at counter call sites** — complete
  - Files: `EventPump.cs`, `Dispatch/ChannelActionDispatcher.cs`, `Dispatch/DispatcherDiagnostics.cs` (additive overloads only — RC-3 preserved), 6 test files updated for the new constructor parameter
  - Commit: `6c99f59 shipyard(phase-16): inject MetricsTagWriter at counter call sites (#18)`
- **Task 3: Documentation + CHANGELOG** — complete (split across two commits due to a tool-state hiccup on the first commit; no functional impact)
  - Files: `docs/observability.md`, `README.md`, `CHANGELOG.md`
  - Commits: `ee61224 shipyard(phase-16): document Otel:MetricsTags:KnownCameras (#18)` and `1567e7a shipyard(phase-16): CHANGELOG entry for KnownCameras (#18)`

## Files Modified

- `src/FrigateRelay.Host/Observability/MetricsTagWriter.cs` (new) — `internal sealed class`, holds `IOptionsMonitor<MetricsTagsOptions>`, `NormalizeCameraTag(string?)` returns input unchanged for null/empty/empty-allowlist/member, returns literal `"other"` for non-member.
- `src/FrigateRelay.Host/Observability/MetricsTagsOptions.cs` (new) — `internal sealed record` with `KnownCameras = Array.Empty<string>()` default.
- `src/FrigateRelay.Host/HostBootstrap.cs` — bound `Otel:MetricsTags` config section, registered `MetricsTagWriter` as singleton.
- `src/FrigateRelay.Host/EventPump.cs` — constructor gains `MetricsTagWriter`; counter call sites pass `_metricsTagWriter.NormalizeCameraTag(camera)`.
- `src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs` — same treatment across all camera-tagged counter helpers.
- `src/FrigateRelay.Host/Dispatch/DispatcherDiagnostics.cs` — additive overloads accepting an explicit `string camera` argument; existing `EventContext`-taking helpers delegate to them. Static class shape preserved (RC-3).
- 6 test files updated to pass a passthrough `MetricsTagWriter` (empty-allowlist `StaticOptionsMonitor<MetricsTagsOptions>`): `EventPumpTests.cs`, `EventPumpDispatchTests.cs`, `Observability/EventPumpSpanTests.cs`, `Observability/CounterIncrementTests.cs`, `Dispatch/ChannelActionDispatcherTests.cs`, `Dispatch/ChannelActionDispatcherParallelValidatorsTests.cs`.
- `tests/FrigateRelay.Host.Tests/Observability/MetricsCardinalityTests.cs` (new) — 3 tests covering known-camera passthrough, unknown-folded-to-`"other"`, empty-allowlist passthrough.
- `docs/observability.md` — new section "Bounding camera-tag cardinality (`Otel:MetricsTags:KnownCameras`, v1.3.0+)" with config example, env-var form, OrdinalIgnoreCase rationale, scope rationale.
- `README.md` — one-line bullet in the Observability section linking to the new docs section.
- `CHANGELOG.md` — `[Unreleased]` `### Added` entry per CONTEXT-16 D8.

## Decisions Made

- **Constructor parameter order on `ChannelActionDispatcher`:** placed `MetricsTagWriter` at position 4 (required, before the optional `ISnapshotResolver?`). This pushed `ISnapshotResolver?` to position 5. One pre-existing test site (`ChannelActionDispatcherTests.cs:398`) passed `ISnapshotResolver` positionally in slot 4 — fixed by inserting the passthrough writer ahead of it. All other call sites used named/builder helpers and were trivially updated.
- **Per-file `CreatePassthroughTagWriter()` + `StaticOptionsMonitor<T>` helper:** the `StaticOptionsMonitor<T>` pattern from the Observability test files (per RESEARCH.md RC-5) is implemented as a private nested class. Reused that exact shape per-file rather than extracting to TestHelpers — matches the existing convention. 6 files now carry the same 4-line nested class plus a 2-line factory.
- **Task 3 split into two commits:** an editor tool-state issue caused the CHANGELOG.md edit to error out of the first docs commit. Per the operator's git policy ("Prefer to create a new commit rather than amending"), the CHANGELOG entry landed as a follow-up `1567e7a`. No functional impact; both commits are clearly tagged for #18.

## Issues Encountered

- **Builder agent hit tool-budget caps twice mid-task** (Phase 9 lesson recurring). Resumed via SendMessage and ultimately the orchestrator finished the last 2 test-site fixes inline — `Dispatch/ChannelActionDispatcherTests.cs:398` (CS1503: positional ISnapshotResolver collided with new MetricsTagWriter slot) and `Dispatch/ChannelActionDispatcherParallelValidatorsTests.cs:396`. Both touched the `BuildDispatcher` helpers, with the per-file `StaticOptionsMonitor<T>` + `CreatePassthroughTagWriter()` pattern matching what the agent had landed elsewhere.
- **Builder reported its sandbox blocked `dotnet`.** Orchestrator ran the build/test commands and fed the build errors back via SendMessage. The agent worked from the CS error list to fix the first 11 of the 13 sites; the last 2 were finished inline. Pattern noted for future phases.
- **Pre-existing `ServicePointManager` documentation comments** in `CodeProjectAi/Doods2/Roboflow Options.cs` files. PLAN-1.1's verification expected `git grep ServicePointManager src/` to be empty, but these are doc comments warning plugin authors NOT to use the API — they reference the forbidden type to enforce the rule, not violate it. Pre-existing as of `pre-build-phase-16` tag (v1.2.1 baseline). Not introduced by Phase 16; not a regression.

## Verification Results

- `dotnet build FrigateRelay.sln -c Release`: **0 warnings, 0 errors** (Linux + WSL2 verified).
- `dotnet run --project tests/FrigateRelay.Host.Tests -c Release`: **154/154 pass** (151 baseline + 3 new MetricsCardinalityTests). Duration 13.4s.
- `git grep -n 'NormalizeCameraTag' src/FrigateRelay.Host/`: **12 hits** (≥8 required by PLAN-1.1). One per camera-tagged counter helper invocation in EventPump + ChannelActionDispatcher, plus the helper's own definition in MetricsTagWriter.cs.
- `git grep -nE 'Otel:MetricsTags|KnownCameras' src/ docs/ README.md CHANGELOG.md`: **19 hits** across HostBootstrap.cs, MetricsCardinalityTests.cs, MetricsTagsOptions.cs, MetricsTagWriter.cs, observability.md, README.md, CHANGELOG.md. Verified.
- `git grep -n 'class MetricsTagWriter' src/FrigateRelay.Host/Observability/`: **1 hit** (line 34 of MetricsTagWriter.cs). Exactly as required.
- `git grep -n 'record MetricsTagsOptions' src/FrigateRelay.Host/Observability/`: **1 hit** (line 14 of MetricsTagsOptions.cs). Exactly as required.
- `git grep -nE 'App\.Metrics|OpenTracing|Jaeger\.' src/`: **empty**. PROJECT.md non-goal preserved.
- `git grep -nE '\.(Result|Wait)\(' src/`: **empty**. Async-only invariant preserved.
- `git grep ServicePointManager src/`: 3 hits, all pre-existing doc comments; see Issues Encountered.

## Lesson Seeds

- **Builder tool-budget caps recur on TDD-heavy tasks with cumulative API surface changes** (also seen Phase 9 OTel work). When a plan touches both production and ~6 test fixtures, expect 2-3 SendMessage resumptions or plan to land the last few sites inline.
- **`internal sealed` + `[SetsRequiredMembers]` + `IOptionsMonitor<T>` is a clean shape for caller-side cardinality normalization.** Avoided having to break `DispatcherDiagnostics`'s static-class shape by injecting at the callers and adding additive overloads downstream.
- **Constructor parameter order matters for positional-arg callers.** Inserting a required parameter before an existing optional one breaks any positional caller. Use named arguments when the surrounding test style allows; otherwise insert the new arg ahead and accept the migration cost.
- **`Microsoft.Extensions.Options.IOptionsMonitor<T>` fully qualified** worked around an unexpected name-resolution quirk where the `using Microsoft.Extensions.Options;` directive didn't resolve in nested-class scope. Cheaper than reorganizing the using set per-file.
- **The PLAN-1.1 ServicePointManager invariant is over-strict.** It assumes zero source-tree references, but doc comments explaining the *anti-pattern* legitimately reference the forbidden type. The CI gate (if added later) should grep for actual usage — e.g. `ServicePointManager.ServerCertificateValidationCallback\s*=` — not the bare type name.

<!-- context: turns=15+13+orchestrator-inline, compressed=no, task_complete=yes -->
