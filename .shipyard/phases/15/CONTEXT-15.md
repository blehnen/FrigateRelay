# Phase 15 — Context (Discussion Capture)

**Date:** 2026-05-07
**Phase:** 15 (v1.2.1 Hardening Patch)
**Source:** `/shipyard:plan 15` discussion-capture step.

## Operator confirmations

- **Phase 14 / v1.2.0 status:** v1.2.0 tag exists (`git tag -l 'v1.2*'` → `v1.2.0`); operator deployment confirmed live (Driveway profile running CPAI + DOODS2 ParallelValidators). ROADMAP Phase 14 status corrected from `[NOT STARTED]` to `[COMPLETE 2026-05-07]` as part of this planning session. Phase 15 is unblocked.
- **Test-count baseline:** 291 tests (post-Phase 14, per the v1.2.0 release commit). Phase 15 success criterion sharpens to "291 + N net-new tests".

## Design decisions (Phase 15-specific)

### D1 — #19 name-allowlist regex strictness: **Permissive printable**

**Decision:** `ValidateNames` enforces `^[A-Za-z0-9_. -]+$` (alphanumeric + space + dot + dash + underscore). Anything else — including CRLF, null bytes, ASCII control chars, unicode punctuation, slashes, colons — is rejected.

**Rationale:** The design doc's strict `^[A-Za-z0-9_-]+$` would reject every one of the 9 spaced subscription names in the committed `config/appsettings.Example.json` (e.g. `"DriveWay Person"`, `"Front Door Porch"`) AND break the operator's live deployment, which uses the same shape. The CWE-117 mitigation requires only that CRLF/null/control chars cannot reach a log line or span name; preserving spaces/dots/hyphens has no security cost. Permissive-printable is the smallest change that closes the structured-logging injection surface without forcing an operator config migration.

**Side effects:**
- Non-ASCII names (accented characters, kanji) are still rejected. Acceptable for v1.x; revisit if an operator opens an issue.
- Forward slashes (`/`), colons (`:`), and at-signs (`@`) are rejected — these are unusual in subscription/plugin/validator names but worth flagging in the diagnostic message so operators understand why their config failed.

### D2 — #19 scope: **All four name kinds**

**Decision:** The permissive-printable regex applies uniformly to subscription, profile, plugin, and validator names. Single rule, single mental model for operators.

**Rationale:** The architect-presented alternative (skip subscriptions, enforce only on the structural DI-key kinds) would split the rule across kinds and complicate the operator-facing diagnostic. Since the chosen regex (D1) already preserves spaces, the cost of applying it to subscriptions is zero — every existing subscription name passes — while the consistency benefit is real.

### D3 — Wave structure: **Single wave, parallel plans**

**Decision:** Phase 15 ships as one PR against `main`. Batch 1 (validation/log hardening, #13/#14/#19/#20) and Batch 2 (CI/operator hygiene, #8/#15/#24/#25/#26/#27) are parallel plans within a single wave — no cross-batch dependencies.

**Rationale:** Per ROADMAP Phase 15 "PR sequencing (decided)" and the design doc's "Cross-phase notes" section. Confirmed unchanged at planning time. Parallel waves would add plan-orchestration overhead with no shipping benefit since both batches go out on one tag.

### D4 — #13 sanitization helper shape: **`internal static` helper inside `StartupValidation.cs`** *(updated 2026-05-07 post-research)*

**Decision:** An `internal static` method `Sanitize(string?)` in `StartupValidation.cs` strips/escapes `\n` and `\r` from operator-controlled values before they are interpolated into `errors.Add(...)` strings. No new public type; no extension method; no dedicated `SanitizeHelper` class.

**Rationale:** Originally specified as `private static`; updated to `internal static` after `RESEARCH.md` surfaced 3 additional `errors.Add` call sites in `ProfileResolver.cs` (separate file in the same assembly) that interpolate operator-controlled `sub.Name` and `profileName`. `private` would leave those sites unsanitized; `internal` keeps the helper invisible outside `FrigateRelay.Host` (assembly boundary unchanged) while reaching every cross-file call site that needs it. Single-assembly visibility, multi-file accessibility — minimum surface that fixes #13 completely.

### D5 — #27 platform-detection seam: **`Func<bool>? isWindows = null` parameter**

**Decision:** `ValidateSerilogPath` (or its new helper that handles Windows-path rejection) accepts an optional `Func<bool>? isWindows = null` parameter. Default — when caller passes `null` or omits — resolves to `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)`. Tests inject `() => true` or `() => false` to exercise both branches cross-platform without `[Conditional...]` attributes.

**Rationale:** A simple `Func<bool>` keeps the test-only seam minimal. Introducing an `IPlatformDetector` interface would mean DI registration, mock framework boilerplate, and a new file for what is effectively a single bool. Conditional test attributes (`[ConditionalFact]`, `[OSPlatform...]`) actually skip on the wrong platform rather than asserting cross-platform behavior — they would weaken the test coverage. The `Func<bool>` keeps tests deterministic and CI-runnable on both Linux and Windows agents.

### D6 — CHANGELOG: **`[Unreleased]` → `[1.2.1]` promotion at release commit**

**Decision:** Every plan that closes an issue ID adds a 1-line entry to `CHANGELOG.md` `[Unreleased]` section. The release commit promotes `[Unreleased]` → `[1.2.1]` in one targeted edit. Same convention as v1.0.x → v1.1.0 → v1.2.0 transitions.

**Rationale:** Standing convention; no novel decision here. Captured to short-circuit the architect from reinventing it.

### D7 — Tag-cut: **Manual operator-cut per CONTEXT-12 D7 policy**

**Decision:** After PR merge, the operator (Brian) cuts `git tag v1.2.1` manually. `release.yml` smoke + push-multiarch GHCR pipeline auto-runs on the tag push.

**Rationale:** Standing convention; same as v1.2.0. No planner work item for the tag itself — it's an operator step listed in the success criteria, not a build task.

## Open questions deferred to future phases

- **Unicode name support** — non-ASCII subscription/profile/plugin/validator names. Currently rejected by D1's regex. Revisit if an operator opens an issue. Not a Phase 15 scope.
- **Operator config migration tooling** — if a future regex-tightening forces operators to rename subscriptions, the `tools/FrigateRelay.MigrateConf` CLI is the natural home for the migration helper. Not a Phase 15 scope; D1 was chosen specifically to avoid forcing this.

## Dispatch ready

CONTEXT-15.md complete. Researcher dispatch (Step 4) follows.
