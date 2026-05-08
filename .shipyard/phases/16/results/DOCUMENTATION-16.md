# Phase 16 Documentation Review

**Date:** 2026-05-08
**Documenter:** documenter agent (claude-sonnet-4-6)
**Scope:** `pre-build-phase-16..HEAD` — 9 commits covering PLAN-1.1 (MetricsTagWriter + KnownCameras), PLAN-1.2 (WaitForEntriesAsync), PLAN-1.3 (registrar unification + CPAI backfill)

---

## Coverage assessment

- **Public API:** `MetricsTagWriter` and `MetricsTagsOptions` are both `internal sealed` — no public-API docs gap. `NormalizeCameraTag` and its XML doc comment are thorough (normalization rules, casing rationale, reference to `docs/observability.md`). `CapturingLogger<T>.WaitForEntriesAsync` is `public` and has an XML doc comment covering count semantics, timeout, cancellation, and `TimeoutException` — no gap.
- **CHANGELOG:** `[Unreleased]` block has `### Added` (#18), `### Internal` (#22), and `### Changed` (#30). Complete for the three plans.
- **Architecture docs:** `.shipyard/codebase/ARCHITECTURE.md` describes the *legacy* `FrigateMQTTProcessingService` (the behavioral-reference document per CLAUDE.md). It is not a living FrigateRelay architecture document and is not expected to track Phase 16 changes. No update needed.
- **Operator guidance:** `docs/observability.md` has a complete `KnownCameras` section. However, both REVIEW-1.1 (minor finding 5) and AUDIT-16 (advisory A1 and A2) flag two operator-facing gaps that are not yet documented — see Recommended findings below.
- **CLAUDE.md conventions:** Three new patterns emerged this phase (WaitForEntriesAsync/MeterListener test idiom, StaticOptionsMonitor extraction precedent, constructor parameter-order rule) that are not yet recorded. See Recommended findings.
- **Plugin-author guide:** `docs/plugin-author-guide.md` Rule 3 in `docs/observability.md` covers bounded-tag discipline. The guide itself (`docs/plugin-author-guide.md`) does not reference `MetricsTagWriter` — correctly so, because that type is `internal` to the host and plugin authors cannot and should not touch it. No gap.

---

## Findings

### Blocking

None. All public interfaces have XML doc comments. CHANGELOG entries are present. Operator-facing config documentation (`docs/observability.md` KnownCameras section) was shipped as Task 3 of PLAN-1.1.

---

### Recommended

**R1 — `docs/observability.md`: add log/metric camera divergence note**

- **File:** `docs/observability.md`, section "Bounding camera-tag cardinality (`Otel:MetricsTags:KnownCameras`, v1.3.0+)"
- **Issue:** When `KnownCameras` is non-empty, structured log entries (`LogMatchedEvent`, `LogValidatorRejected`) carry the raw camera value from `EventContext.Camera`, while counter tags carry the normalized value (`"other"` for unknown cameras). An operator who sees `frigaterelay.validators.rejected{camera="other"}` spike and tries to correlate against log lines by filtering on `camera` will find no matching entries under `"other"` — the log entry will show the real camera name.
- **Source:** REVIEW-1.1 minor finding 5 (`src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs:333,385`); AUDIT-16 advisory A1.
- **Suggested addition:** Append a short note at the end of the KnownCameras section, after the env-var block:

  > **Log vs metric correlation:** When `KnownCameras` is configured, structured log entries always carry the raw camera value from `EventContext.Camera`. Metric tags carry the normalized value (`"other"` for non-members). If you see a spike in `frigaterelay.validators.rejected{camera="other"}`, correlate against logs using the `subscription` or `event_id` fields rather than `camera` — the log line will show the actual camera name that was folded.

**R2 — `docs/observability.md`: add null/empty camera bypass note**

- **File:** `docs/observability.md`, section "Bounding camera-tag cardinality"
- **Issue:** `NormalizeCameraTag` passes `null`/empty camera values through unchanged even when the allowlist is non-empty (`MetricsTagWriter.cs:51-52`). This is by design (documented in the XML comment) but is not visible to operators. A Frigate source with a misconfigured or empty camera field will record metrics with a `null`/`""` camera tag regardless of the allowlist — operators may be confused when they see such values in dashboards despite having populated `KnownCameras`.
- **Source:** AUDIT-16 advisory A2; REVIEW-1.1 minor finding 4.
- **Suggested addition:** Add a sentence to the Behavior list in the KnownCameras section:

  > **Note:** `null` or empty camera values pass through unchanged in all cases (whether or not `KnownCameras` is set). If your Frigate source emits events with no camera name, those metrics will carry a null/empty tag. This is distinct from `"other"` — `"other"` only appears for non-empty, non-member values. If you observe null/empty-tagged metrics, check your Frigate source configuration.

**R3 — `CLAUDE.md`: add `WaitForEntriesAsync` / MeterListener test pattern to Conventions**

- **File:** `CLAUDE.md`, section "Conventions"
- **Issue:** The `WaitForEntriesAsync` helper (PLAN-1.2) is a new shared test pattern that future test authors need to know about. The existing `CapturingLogger<T>` convention entry doesn't mention the polling method. The authorized MeterListener fallback for metric-driven waits (the pattern in `CounterIncrementTests.RunDispatcherAsync`) is also new and undocumented.
- **Suggested addition** (new bullet after the `CapturingLogger<T>` bullet):

  > **`CapturingLogger<T>.WaitForEntriesAsync(count, timeout)` replaces fixed-time `Task.Delay` in async observability tests.** Call `await logger.WaitForEntriesAsync(1, TimeSpan.FromSeconds(2))` to wait until at least `count` log entries have been captured or timeout elapses. For code paths that emit no log message (e.g. the dispatcher success path), use a `MeterListener`-based `TaskCompletionSource` fallback that resolves on the first terminal counter measurement (`actions.succeeded`, `actions.failed`, `actions.exhausted`, `validators.rejected`). See `tests/FrigateRelay.Host.Tests/Observability/CounterIncrementTests.cs:RunDispatcherAsync` for the canonical MeterListener pattern. Do NOT introduce new numeric `Task.Delay([0-9]` sites in `tests/FrigateRelay.Host.Tests/Observability/` — the greppable invariant `git grep -nE 'Task\.Delay\([0-9]' tests/FrigateRelay.Host.Tests/Observability/` must return empty.

**R4 — `CLAUDE.md`: add constructor parameter-order rule to Conventions**

- **File:** `CLAUDE.md`, section "Conventions"
- **Issue:** PLAN-1.1 encountered a concrete breakage when a required `MetricsTagWriter` parameter was inserted at position 4 of `ChannelActionDispatcher`, pushing an existing optional `ISnapshotResolver?` to position 5. One positional test call site broke (CS1503). The lesson is actionable for future contributors adding required parameters to constructors that have existing optional trailing parameters.
- **Source:** SUMMARY-1.1 Decisions Made; SUMMARY-1.1 Lesson Seeds bullet 3.
- **Suggested addition** (new bullet in Conventions):

  > **New required constructor parameters go ahead of existing optional ones — use named arguments at all downstream call sites.** Inserting a required parameter before an existing optional parameter breaks any positional caller. When adding a required parameter to a constructor that already has an `ISnapshotResolver?` or similar nullable/optional trailing parameter: (1) place the new required param immediately before the optional one, (2) update all downstream call sites to use named arguments (e.g. `snapshotResolver: ...`), or (3) update positional callers to insert the new arg in the correct slot. Precedent: `ChannelActionDispatcher` gained `MetricsTagWriter` at position 4 in Phase 16, pushing `ISnapshotResolver?` to position 5.

**R5 — `CLAUDE.md`: record `StaticOptionsMonitor<T>` extraction as pending Phase 17 task**

- **File:** `CLAUDE.md`, section "Conventions" (or a note on `CapturingLogger<T>`)
- **Issue:** SIMPLIFICATION-16 (High) and REVIEW-1.1 (minor 1) both flag that `StaticOptionsMonitor<T>` is duplicated across 7 test files (11 definitions) and should be extracted to `FrigateRelay.TestHelpers` exactly as `CapturingLogger<T>` was extracted after Phase 6. The existing `CapturingLogger<T>` convention bullet says "Do NOT redefine a per-assembly copy" but does not mention `StaticOptionsMonitor<T>`. Until extraction lands, new test files will copy the pattern again.
- **Suggested addition** (append to the `CapturingLogger<T>` bullet or as a standalone bullet):

  > **`StaticOptionsMonitor<T>` is a pending Phase 17 extraction.** A 4-line `IOptionsMonitor<T>` stub (`CurrentValue`, `Get`, `OnChange → null`) is currently duplicated across 7 test files as either `StaticMonitor<T>` or `StaticOptionsMonitor<T>`. Do NOT define a new copy; instead reference the Phase 17 item to extract the canonical version to `tests/FrigateRelay.TestHelpers/StaticOptionsMonitor.cs`. Until that extraction lands, reuse an existing per-file copy within the same test class file rather than creating a new definition.

---

### Optional

**O1 — `docs/observability.md`: worked Prometheus example for KnownCameras**

A before/after Prometheus output block showing `frigaterelay_events_received_total` with 3 known-camera label values plus `camera="other"` after folding would make the cardinality bound visually concrete. Useful for new operators evaluating the feature. Low priority — the current text is clear enough without it.

**O2 — `docs/plugin-author-guide.md`: note that `MetricsTagWriter` is host-internal**

Rule 3 in `docs/observability.md` tells plugin authors to use bounded-tag discipline. A cross-reference note in `docs/plugin-author-guide.md` (Section 8 or a new Section 12 on Observability) could clarify that the host's `MetricsTagWriter` is `internal` to `FrigateRelay.Host` and that plugins that emit their own counters with a `camera` tag are responsible for their own cardinality bounding (e.g. by injecting their own options-driven allowlist). Very low priority — the information is derivable from the `internal sealed` declaration and existing Rule 3 text.

---

## Verdict: GAPS_NON_BLOCKING

All shipped documentation (operator-facing `docs/observability.md` section, README link, CHANGELOG entries, XML doc comments on new types) is complete and correct. The three recommended CLAUDE.md convention additions (R3, R4, R5) record phase-16 lessons for future contributors and prevent pattern drift. The two recommended `docs/observability.md` additions (R1, R2) address auditor and reviewer advisories (A1, A2) that are currently visible only in phase documents, not in the living operator documentation. None of the gaps are blocking for ship.
