# Phase 16 — Context (Discussion Capture)

**Date:** 2026-05-07
**Phase:** 16 (v1.3.0 Minor Release)
**Source:** `/shipyard:plan 16` discussion-capture step.

## Operator confirmations

- **Phase 15 / v1.2.1 status:** SHIPPED 2026-05-07 (release.yml succeeded 8m42s; multi-arch GHCR images published). Phase 15-must-ship-before-Phase-16 dependency met.
- **v1.3.0 SemVer rationale:** New operator-visible config key (`Otel:MetricsTags:KnownCameras`) is the load-bearing reason for the minor bump. #22 (test refactor) and #30 (registrar style) are zero-shipping-impact and ride along.
- **Test-count baseline:** 151 Host tests as of v1.2.1 (the runner-reported count after Phase 15's +18 net-new). Phase 16 success criterion sharpens to "all 151 existing pass + N net-new for Phase 16".

## Design decisions (Phase 16-specific)

### D1 — #18 KnownCameras allowlist surface: **`MetricsTagWriter` as string-normalizer, injected at the callers** *(refined post-research)*

**Decision:** Introduce a new `internal sealed class MetricsTagWriter` under `src/FrigateRelay.Host/Observability/` (greenfield directory — no existing observability namespace per RESEARCH.md). The helper exposes `string NormalizeCameraTag(string? camera)` and is injected via constructor into the **callers** that emit `camera`-tagged counters: `EventPump` and `ChannelActionDispatcher` (which today call into `DispatcherDiagnostics`'s static helpers).

**Why injected at callers, not as a wrapper around `Counter<T>.Add`:** RESEARCH.md surfaced the critical constraint — **`DispatcherDiagnostics` is a `static class`** and 9 of 10 counters live there as static helper methods. Static classes can't take constructor injection. The cleanest alignment with the existing `TagList`-based static-helper pattern is for `MetricsTagWriter` to normalize the `camera` string value **at the caller**, before it's passed to the static `DispatcherDiagnostics.IncrementXxx(camera, ...)` calls.

**Behavior:**
- If `KnownCameras` is empty → return input unchanged (preserves current behavior for all current operators).
- If `KnownCameras` is non-empty → look up via `HashSet<string>(StringComparer.OrdinalIgnoreCase)` (D3 case-insensitive). Membership → return input unchanged. Non-membership → return literal `"other"`.
- The helper holds `IOptionsMonitor<MetricsTagsOptions>` (singleton DI registration in `HostBootstrap`) — picks up config rebinding if reload is ever wired without service restart.

**Rationale:** Single string-normalization seam enforced uniformly at every counter-emitting caller. A future caller adding a new `camera`-tagged counter has one helper to call; the static-class constraint on `DispatcherDiagnostics` is preserved (no rewrite of the v1.1 tag-matrix discipline). Matches the architect-recommended "central helper" intent while honoring the existing static-class structure.

**Surface impact:**
- `src/FrigateRelay.Host/Observability/MetricsTagWriter.cs` — new internal class.
- `src/FrigateRelay.Host/Observability/MetricsTagsOptions.cs` — new internal options record (per OQ-1 lean: co-located).
- `src/FrigateRelay.Host/EventPump.cs` — constructor gains `MetricsTagWriter` parameter; `camera` argument to `DispatcherDiagnostics.IncrementXxx` calls wrapped in `_metricsTagWriter.NormalizeCameraTag(...)`.
- `src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs` — same treatment.
- `src/FrigateRelay.Host/HostBootstrap.cs` — register `MetricsTagWriter` as singleton, bind `MetricsTagsOptions` from `Otel:MetricsTags` config section.
- `tests/FrigateRelay.Host.Tests/Observability/MetricsCardinalityTests.cs` — new file, ≥3 tests (known-camera passthrough, unknown-folded-to-other, empty-allowlist-disabled).

### D2 — #22 polling helper shape: **`WaitForEntriesAsync` on `CapturingLogger<T>` only** *(simplified post-research)*

**Decision:** Add `WaitForEntriesAsync(int count, TimeSpan timeout)` to `CapturingLogger<T>` in `tests/FrigateRelay.TestHelpers/`. Use it at all 4 fragility sites. No per-fixture MeterListener helper needed.

**Note on field name:** `CapturingLogger<T>` exposes captured records via the field **`Entries`** (not `Records` — confirmed by RESEARCH.md; the Phase 15 PLAN-1.1 build's "Records did not exist" surprise is the same root). The new method is named `WaitForEntriesAsync` to match the field.

**Rationale (updated post-research):** Originally specified as "hybrid" because some `Task.Delay` sites were assumed to wait on `MeterListener` measurement counts. RESEARCH.md confirmed **all 4 fragility sites are log-record polling** (the `CapturingLogger<EventPump>` and `CapturingLogger<ChannelActionDispatcher>` instances are already constructed at each call site, and the original sleeps were waiting for log emission, not metric flush). The hybrid alternative's per-fixture-MeterListener-helper is unnecessary; logger-only is the simpler fit.

**Surface impact:** Single new method on `CapturingLogger<T>`; 4 modified sites in `tests/FrigateRelay.Host.Tests/Observability/` (`EventPumpSpanTests.cs:285`, `CounterIncrementTests.cs:359`, `:393`, `:425` — line numbers updated from the ROADMAP's earlier estimate per RESEARCH.md). No fixture changes.

### D3 — #18 KnownCameras casing: **Case-insensitive (`StringComparer.OrdinalIgnoreCase`)**

**Decision:** `HashSet<string>(StringComparer.OrdinalIgnoreCase)`. Operator can write `"Driveway"` in `appsettings.Local.json`'s `KnownCameras` and Frigate can publish `"driveway"` — they match.

**Rationale (operator):** Optimizing for fewer "wait, why is my camera name in 'other'?" support incidents over surfacing operator config typos. Frigate camera names are sometimes case-inconsistent across the operator's pipeline (e.g. lowercase IDs in Frigate config, mixed-case in upstream tools), and the cardinality protection is the load-bearing goal — not enforcing strict naming. Case-insensitive matching delivers the cardinality bound without the friction of case-mismatch debugging.

**Note — divergent from project convention:** Subscription / profile / plugin / validator names elsewhere in FrigateRelay are case-sensitive (Phase 15 #19 didn't case-fold either). `KnownCameras` is intentionally divergent because the goal is operational protection, not config-correctness validation. Document this in the ROADMAP entry and `docs/observability.md` so the divergence is explicit, not surprising.

### D4 — #18 cardinality-tag scope: **Cameras-only** (label NOT included in v1.3.0)

**Decision:** `MetricsTagsOptions` has a single `KnownCameras` field. No `KnownLabels` field. The cardinality DoS issue (#18) listed both `camera` and `label` as attacker-influenceable, but v1.3.0 only addresses `camera`.

**Rationale:** Frigate `label` values are derived from the detector model (typically the COCO 80-class set) — the realized cardinality in production is small. `camera` is operator-named and free-form — higher cardinality risk. Cameras-only is defensible for v1.3.0; if a future operator hits label-cardinality issues, add `KnownLabels` in a v1.3.x or v1.4 follow-up. Captured here so the architect doesn't expand scope.

### D5 — #18 config key location: **`Otel:MetricsTags:KnownCameras: string[]`**

**Decision:** Confirm the config key name as specified in ROADMAP:
```json
{
  "Otel": {
    "MetricsTags": {
      "KnownCameras": ["Front", "Driveway", "Backyard"]
    }
  }
}
```
Default is empty array (`[]`); when empty, the camera tag passes through unchanged (preserves current behavior for all current operators). When populated, unknown camera values are folded to `"other"`.

**Env-var form:** `Otel__MetricsTags__KnownCameras__0=Front`, etc. (standard `IConfiguration.Bind` array syntax).

### D6 — #30 registrar registration order: **Atomic 3-file commit, no per-plugin split**

**Decision:** PLAN for #30 produces a single commit touching all three plugin registrar files (`FrigateRelay.Plugins.CodeProjectAi/PluginRegistrar.cs`, `FrigateRelay.Plugins.Roboflow/PluginRegistrar.cs`, `FrigateRelay.Plugins.Doods2/PluginRegistrar.cs`). Per the CodeRabbit recommendation captured in ID-30 — landing one in isolation creates lint-style drift across the trio.

The greppable invariant after this task: `git log --oneline -1 -- <three registrar paths>` shows a single commit covering all three.

**Optional in same commit:** backfill `CodeProjectAiPluginRegistrarTests` (5 tests, mirroring the Roboflow + DOODS2 precedent). **DOODS2 already has `Doods2PluginRegistrarTests.cs` with 5 tests as of Phase 14** — RESEARCH.md surfaced this; ISSUES.md ID-30 description ("CPAI + DOODS2 do not yet [have these tests]") is stale. Only **CPAI needs backfill** (5 tests), not 10. Recommended — the architect can bundle in PLAN-16.1.3 within the 3-task budget.

### D7 — Wave structure: **Single wave, parallel plans (3 plans)**

**Decision:** Phase 16 ships as one PR against `main`. The three deliverables (#18, #22, #30) touch disjoint files — no cross-plan file conflicts. Architect proposes one plan per issue, all in Wave 1.

**File ownership map:**
- PLAN-16.1.1 (#18) — owns `src/FrigateRelay.Host/Observability/MetricsTagWriter.cs` (new) + `MetricsTagsOptions.cs` (new) + edits to `DispatcherDiagnostics.cs` + `EventPump.cs` + new `MetricsCardinalityTests.cs`.
- PLAN-16.1.2 (#22) — owns `tests/FrigateRelay.TestHelpers/CapturingLogger.cs` (extension) + 4 modified test files in `tests/FrigateRelay.Host.Tests/Observability/`.
- PLAN-16.1.3 (#30) — owns 3 `PluginRegistrar.cs` files + optionally 2 new test files (`CodeProjectAiPluginRegistrarTests.cs`, `Doods2PluginRegistrarTests.cs`).

**CHANGELOG.md is the only shared file.** Per the Phase 15 lesson, sequential dispatch eliminates merge friction; the architect should plan accordingly.

### D8 — CHANGELOG section: **`### Added` for #18, `### Changed` for #30, `### Internal` for #22**

**Decision:**
- `#18` → `### Added` (operator-visible new config key).
- `#30` → `### Changed` (no operator-visible behavior change, but registrar shape unification — internal API consistency).
- `#22` → `### Internal` (test-only refactor; operators don't see this).

Mirrors the Phase 15 CHANGELOG style (Security/Fixed/Documentation sections).

### D9 — Tag-cut: **Manual operator-cut per CONTEXT-12 D7 + CONTEXT-15 D7 policy**

**Decision:** After PR merge, operator cuts `git tag v1.3.0` manually. `release.yml` smoke + push-multiarch GHCR pipeline auto-runs.

## Open questions (architect, decide at PLAN dispatch)

Most OQs resolved post-research. Remaining smaller-grained decisions:

- **OQ-3 — `WaitForEntriesAsync` polling interval.** Default e.g. 25ms? Configurable? Lean: 25ms internal default, no exposed knob. Total wait bounded by the caller-supplied `timeout`.
- **OQ-5 (NEW from RESEARCH) — Greppable invariant for #22 needs tightening.** ROADMAP success criterion `git grep -nE 'Task\.Delay' tests/FrigateRelay.Host.Tests/Observability/` will not return empty after Phase 16 — RESEARCH.md found 2 additional `Task.Delay(Timeout.Infinite, ct)` calls in fake `IEventSource` test stubs (structurally correct cancellation-token waits, NOT fragility sites). Architect must re-state the invariant as e.g. `git grep -nE 'Task\.Delay\([0-9]' tests/FrigateRelay.Host.Tests/Observability/` (matches numeric-delay calls only, not the `Timeout.Infinite, ct` cancellation-await pattern), and mirror the corrected pattern back into ROADMAP via a small post-build doc fix.

**Resolved during CONTEXT-16 post-research update:**
- OQ-1 (`MetricsTagsOptions` location): co-located with `MetricsTagWriter` under `src/FrigateRelay.Host/Observability/`.
- OQ-2 (`MetricsTagWriter` lifetime): singleton + `IOptionsMonitor`.
- OQ-4 (CPAI + DOODS2 backfill): only CPAI needs backfill; DOODS2 already has 5 tests from Phase 14.

## Dispatch ready

CONTEXT-16.md complete. Researcher dispatch (Step 4) follows.
