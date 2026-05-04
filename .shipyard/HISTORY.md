# Shipyard History

## 2026-05-04 — Phase 13 Wave 3 built + PR #40 opened (final v1.1 PR)

- **Scope:** PR for issue #34 (`BlueIrisUrlTemplate` collapse + canonical-set drift test). Final PR in the v1.1 trio.
- **Branch:** `refactor/34-blueiris-template` off `f4c9c73` (post-#39 main). Pushed; PR #40 opened.
- **Plan built:** PLAN-3.1 (4 tasks, one over the 3-task limit; architect justified Task 4 as one-line CHANGELOG bookkeeping). Reviewer verdict PASS.
- **Commits on `refactor/34-blueiris-template`** (4):
  - `004dfb3` — `refactor(blueiris): collapse BlueIrisUrlTemplate to thin wrapper around EventTokenTemplate`. 58 → 24 lines. Single source of truth for `AllowedTokens`, regex, and unknown-placeholder error wording.
  - `f7c32ba` — `test(abstractions): add EventTokenTemplate_AllowedTokens_Canonical drift guard`. Hardcoded canonical set; catches both additions and removals.
  - `940b24a` — `docs(changelog): add [Unreleased] entry for BlueIrisUrlTemplate collapse (Issue #34)`.
  - `f824bd2` — `review: clarify drift-test comment to cover removals as well as additions` (PLAN-3.1 review suggestion).
- **Cumulative diff:** 3 src/test files, +33/-46 net (collapse shrank `BlueIrisUrlTemplate` more than the new test added).
- **Verification:** `dotnet build` 0 warnings/errors; full suite 241/241 across 8 projects (was 240 from PR #39 = +1 from canonical-set test); the 11 existing `BlueIrisUrlTemplateTests` pass unchanged (RESEARCH.md prediction confirmed: wildcard error-message assertions survive the new caller-name format).
- **Decisions confirmed:**
  - **D6** drift test uses an explicit hardcoded `private static readonly FrozenSet<string> _canonicalTokens` field (`{ camera, camera_shortname, label, event_id, zone }`). NOT a self-comparison.
  - **`BlueIrisSnapshotUrlTemplate` untouched** per the issue's explicit guidance — DI marker only.
  - **`BlueIrisActionPlugin.cs` call sites unaffected** — `Parse(string)` signature preserved.
- **Reviewer findings landed this PR:** 0 Critical / 0 Important / 2 Suggestions. 1 addressed inline (test-comment scope tightened to mention removals); 1 deferred (SUMMARY-3.1 prose says `record` while implementation is `class` — historical artifact, no source impact).
- **Closes the v1.0.2→v1.0.3 P0 root cause structurally.** Adding a future token (e.g., `{score}`) now requires editing exactly one allowlist; the canonical-set test fails CI on drift.
- **Next step:** merge PR #40, then cut `v1.1.0` per `RELEASING.md`. CHANGELOG `[Unreleased]` already aggregates all three PRs' bullets.

## 2026-05-04 — Phase 13 Wave 2 built + PR #39 opened

- **Scope:** PR for issue #36 (`docs/observability.md` + Grafana dashboard JSON + `docker/observability/` reference compose stacks + root `Makefile` `verify-observability` target + `RELEASING.md` retitle + drift test + CHANGELOG).
- **Branch:** `feat/36-observability-docs` off `b110bb3` (post-#38 main). Pushed; PR #39 opened.
- **Plans built:** PLAN-2.1, PLAN-2.2, PLAN-2.3 — all reviewers PASS.
- **Commits on `feat/36-observability-docs`** (12, ~1101 lines):
  - PLAN-2.1: `b52aa3e` (`docs/observability.md`), `383f7c8` (Grafana dashboard), `4361cbe` (README section). Plus `8734a52` review fix (OTel env-var casing + native SDK form).
  - PLAN-2.2: `1419491` (metrics compose + collector + prom configs), `2703a5c` (Seq compose), `67db72b` (Makefile). Plus `3152d48` review fix (trap signals + 2 doc nits).
  - PLAN-2.3: `c5fb95b` (drift test), `e2323b0` (RELEASING retitle + verify-observability bullet), `7521a23` (CHANGELOG). Plus `0364e47` review fix (RELEASING body de-versioning + 2 doc/test nits).
- **Verification:** `dotnet build` 0 warnings/errors; `run-tests.sh --skip-integration` **240/240** across 8 projects (was 239 from PR #38 baseline = +1 from `CounterInventoryDriftTests`); `make -n verify-observability` parses cleanly.
- **Decisions to highlight (all per CONTEXT-13):**
  - **D2** drift test uses reflection over `DispatcherDiagnostics`'s `Counter<long>` fields (excludes `Meter` and `ActivitySource` correctly via `FieldType` filter); doc parser locates `docs/observability.md` via marker-file walk from `AppContext.BaseDirectory` (resilient to SDK path-segment changes).
  - **D7** two compose files preserved — metrics stack and Seq stack with distinct project names so they can run independently.
  - **OQ-4** RELEASING.md retitled to version-agnostic; the body line that still hard-coded `v1.0.0` was de-versioned in the same PR.
- **Reviewer findings landed this PR:** 1 Important + 7 Minor/Suggestion across 3 reviewers (PLAN-2.1: 1 Important + 1 Suggestion; PLAN-2.2: 3 Minor; PLAN-2.3: 1 Important + 2 Suggestions). All addressed inline before push — no deferred fixes.
- **Drift-test guarantee:** adding a counter to `DispatcherDiagnostics` without updating `docs/observability.md` (or vice versa) now fails CI. The PR #38 `event_id`-grep gate is preserved; this test extends drift detection to the cumulative inventory.
- **Next step:** await PR #39 merge, then branch `refactor/34-blueiris-template` off updated main for Wave 3 (PR for issue #34).

## 2026-05-04 — PR #38 merged + Wave 2 staged

- **PR #38 merged** to origin/main as merge-commit `b110bb3`. CodeRabbit's two stale-text findings (ROADMAP "12 phases" and CounterIncrementTests Test 8 section header) were addressed in commit `4f2b874` before merge.
- **Local main rebased** onto origin/main — local shipyard wave-1-complete metadata commit replayed as `5dbd4a4` (was `decbaa5`). One unrelated upstream change picked up: Dependabot mstest bump (`085283b`/`e01701a`).
- **Wave 1 worktree removed** (`.worktrees/feat-35-counter-tags`), local branch `feat/35-counter-tags` deleted.
- **Wave 2 worktree branched:** `.worktrees/feat-36-observability-docs` on `feat/36-observability-docs` off `origin/main` (`b110bb3`). Baseline verified — `dotnet build FrigateRelay.sln -c Release` 0 warnings/errors, `run-tests.sh --skip-integration` 239/239 across 8 projects. Same as merged-main baseline.
- **Next step:** dispatch Wave 2 builders — PLAN-2.1 (docs/observability.md + Grafana dashboard JSON + README), PLAN-2.2 (docker/observability/ compose stacks + Makefile), PLAN-2.3 (drift test + RELEASING.md + CHANGELOG). PLAN-2.3's drift test parses PLAN-2.1's markdown so 2.3 should land after 2.1 (or in the same diff with shared test fixtures).

## 2026-05-04 — Phase 13 Wave 1 built (`/shipyard:build 13` — PR #35 counter tags)

- **Scope:** Wave 1 only on isolated worktree `.worktrees/feat-35-counter-tags` (branch `feat/35-counter-tags`), per the brainstorm decision to ship Phase 13 as 3 sequential PRs.
- **Plans built:** PLAN-1.1 (helper-method refactor on `DispatcherDiagnostics`), PLAN-1.2 (10 `MeterListener` tag-presence tests + CHANGELOG). Both PASS reviews.
- **Commits on `feat/35-counter-tags`:** 6 atomic commits from `b0fc774`:
  - `14ec2f5` add 10 helper methods + per-counter XML doc-comments
  - `d53c4a1` migrate EventPump increment sites
  - `07479ba` migrate ChannelActionDispatcher increment sites
  - `07832b2` cross-phase fix: `ErrorsUnhandled_*_Untagged` → `_TaggedWithComponent` (Phase 9 D9 → Phase 13 #35)
  - `cafaee0` `CounterTagMatrixTests.cs` (10 tests, one per counter, with `event_id` absence tripwire)
  - `6928164` CHANGELOG `[Unreleased]` entry
- **Cumulative diff:** 4 src/test files, +201/-64 lines + new 320-line test file.
- **Verification:** `dotnet build` 0 warnings/errors, `run-tests.sh --skip-integration` 239/239 across 8 projects (up from 229 baseline = +10 from new tests). `git grep '"event_id"' src/FrigateRelay.Host/Dispatch/` empty (CONTEXT-13 hard rule).
- **Cross-phase decision logged:** Phase 9's deliberate "intentionally tagless `errors.unhandled`" intent is intentionally overruled by Phase 13 issue #35's `component` tag. Operator confirmed at build time.
- **Builder ergonomics:** Both PLAN-1.1 and PLAN-1.2 builder agents stalled before completing the closeout (build verification + commit + SUMMARY) — orchestrator finished inline. Commits are clean. Pattern noted for future-Phase builder dispatch.
- **Issue filed (deferred):** `ID-29` — eviction-callback log captures stale `plugin.Name` from loop variable. Pre-existing closure-capture pattern surfaced by PLAN-1.1's refactor; one-line follow-up fix. Counter tags themselves are correct.
- **Post-phase gates intentionally deferred to end-of-Phase 13.** Wave-level reviewers PASSed both plans; the auditor/simplifier/documenter make more sense once all three PRs (Wave 1+2+3) have landed and the cumulative diff is reviewable as a unit.
- **Next step:** push `feat/35-counter-tags` and open PR #35; after merge, branch `feat/36-observability-docs` for Wave 2.

## 2026-05-04 — Phase 13 planned (`/shipyard:plan 13` — v1.1 Observability + Cleanup)

- **Discussion capture (CONTEXT-13.md):** 4 design decisions resolved interactively (D1 helper-method API for tags, D2 reflection-based drift test, D3 direct-shell Makefile, D4 update tests for #34 error-message contract). Then 5 OQs surfaced by RESEARCH.md addendum (D5 events.received tag matrix amended — drop `subscription`, D6 explicit canonical-set drift test post-collapse, D7 two compose files in docker/observability/, OQ-2 auto-resolved, OQ-4 RELEASING.md retitle, D8 actual test count is 11 not 23).
- **Researcher (sonnet):** required one continuation SendMessage to write the file. RESEARCH.md = 336 lines. Documented every counter call site with file:line refs, OTLP wiring (gRPC port 4317), DispatcherDiagnostics shape (static class, no DI), DispatchItem/EventContext field inventory, existing MeterListener test patterns at `tests/FrigateRelay.Host.Tests/Observability/CounterIncrementTests.cs`, full template-class shapes for #34, all call sites of BlueIrisUrlTemplate.
- **Architect (opus):** generated 6 plan files / 18 tasks across 3 waves on the first dispatch. PLAN-3.1 has 4 tasks (justified inline — Task 4 is one-line CHANGELOG). All 8 CONTEXT-13 decisions honored. Wave 1 plans 1.1+1.2 file-disjoint (parallel-safe within PR working branch); Wave 2 plans 2.1+2.2+2.3 file-disjoint.
- **Verifier — completeness (sonnet):** verdict **PASS_WITH_NOTES**. All 10 ROADMAP success criteria + all 9 CONTEXT-13 decisions covered. 3 caveats: Gap-1 (Makefile scope vs SC-7 wording — operator pre-condition) and Gap-2 (PLAN-2.2 must_haves missing config files) addressed inline by orchestrator with surgical edits to PLAN-2.2 and PLAN-2.3. Gap-3 (PLAN-3.1 4 tasks) accepted with architect's inline justification.
- **Verifier — critique (sonnet):** required one continuation SendMessage to write the file. CRITIQUE.md = 9.1KB. Verdict **READY** — all 6 plans feasible as written, file paths verified, API surface match, verification commands runnable, cross-plan dependencies acyclic.
- **Inline edits this session:** PLAN-2.2 must_haves now lists otel-collector-config.yaml + prometheus.yml; PLAN-2.3 RELEASING.md task wording amended to include operator pre-condition for SC-7 counter-sample confirmation (Makefile per D3 verifies stack health only — operator manually confirms `frigaterelay_*_total` series in `:9090/graph` to honor SC-7 intent without expanding Makefile scope).
- **Native tasks:** 6 created via TaskCreate (one per plan). Status: pending.
- **State:** phase=13, status=planned, position="Ready for /shipyard:build 13".
- **Next step:** `/shipyard:build 13` (or optionally `/shipyard:worktree create phase-13-v1.1` for isolation first).

## 2026-05-04 — v1.1 scope captured (`/shipyard:brainstorm` — post-v1.0 issue triage)

- **Trigger:** user invoked `/shipyard:brainstorm` to plan against the 6 open GitHub issues at https://github.com/blehnen/FrigateRelay/issues.
- **Scope decisions (interactive Q&A):**
  - **Q1 PROJECT.md handling** → Update existing (append "Post-v1.0 Scope — v1.1" section, preserve v1.0 history).
  - **Q2 release split** → Option B: tight v1.1 = #34 + #35 + #36 (refactor + observability story); v1.2 picks up #13 + #14 + #23 (new validators + parallel mode).
  - **Q3 PR sequencing within v1.1** → Option A: three sequential PRs (#35 first, #36 second, #34 any slot — independent).
  - **Q4 docker-compose recipes (#36)** → Option C: compose files under `docker/observability/` + `make verify-observability` Makefile target + one line in `RELEASING.md` for pre-tag-push manual smoke. No new CI job.
  - **Q5 roadmap handling** → Append Phase 13 to existing ROADMAP.md (preserve Phases 1–12 verbatim).
- **Artifacts written:**
  - `PROJECT.md` — appended "## Post-v1.0 Scope — v1.1 (observability + structural cleanup)" with Goals / In scope / Out of scope (v1.2 deferral) / PR sequencing / verification gates / success criteria.
  - `ROADMAP.md` — appended "## Phase 13 — v1.1 Observability + Cleanup `[NOT STARTED]`" matching Phase 11/12 heading style. 60 lines added (374 → 433). New "Phase 13 open questions" subsection under existing Questions Appendix surfaces 4 deferred-decision items (per-counter tag selection, `make verify-observability` shell-vs-script, Grafana version pin, #34 error-message contract loosening).
  - `STATE.json` — restored from HEAD (working tree had a stale Phase 4 snapshot from a prior corrupted write) and forward-rolled to Phase 13 / status ready. SHA256 recomputed.
- **Architect dispatch:** `shipyard:architect` (opus per `model_routing.architecture`) appended Phase 13 in a single shot. No revision cycles required — user approved on first presentation.
- **Out of scope (deferred to v1.2):** #13 (Roboflow Inference / RF-DETR), #14 (DOODS2), #23 (parallel validator execution). v1.2 narrative locked in: more inference engines + the parallel-AND mode that uses them.
- **Next step:** `/shipyard:plan 13` to decompose Phase 13 into per-PR plans.

## 2026-04-28 — Phase 12 built (`/shipyard:build 12` — Parity Cutover, final phase before v1.0.0)

- **Layout executed:** 3 waves / 8 plans / 18 tasks / ~14 implementation commits + 2 fix-ups + 1 closeout. Wave 1 (5 parallel team-mode `shipyard-build-phase-12-wave-1`) → Wave 2 (1 plan operator-checklist, agent mode) → Wave 3 (2 parallel team-mode `shipyard-build-phase-12-wave-3`). 48h passive-observation gate explicitly **continued in-session per user choice** — Wave 3 builds the reconcile tooling against synthetic NDJSON+CSV fixtures; the actual parity report is a TEMPLATE the operator fills before tagging v1.0.0.
- **Wave 1 — 5 parallel doc/tool/code plans (team mode):** PLAN-1.1 BlueIris DryRun (commit `68774d7`, EventId 203, 19/19 plugin tests +2 new), PLAN-1.2 Pushover DryRun (commit `4705f12`, EventId 4, 12/12 +2 new), PLAN-1.3 `tools/FrigateRelay.MigrateConf/` C# console tool with **hand-rolled IniReader** (commits `dccb210`+`491c1f8`, 4 round-trip tests, preserves all 9 repeated `[SubscriptionSettings]` blocks — `Microsoft.Extensions.Configuration.Ini` would have collapsed them), PLAN-1.4 `docs/migration-from-frigatemqttprocessing.md` (commit `8bebf47`, 174 lines, RFC 5737 documentation IPs), PLAN-1.5 NDJSON Serilog audit-log sink with `Logging:File:CompactJson` opt-in (commit `543e639`, `HostBootstrap.ApplyLoggerConfiguration` extracted as internal static for testability, 103/103 Host tests +2 new). All 5 reviewers PASS. Wave 1 fix-up commit `4de0499` addressed REVIEW-1.3 advisories (test method rename `BelowSixty`→`BelowSeventy`, CLAUDE.md staleness on test-project CI integration — Phase 3's run-tests.sh extraction made the hard-coded list paragraph obsolete) plus a heads-up note inside PLAN-3.1 frontmatter about REVIEW-1.5 finding F-1 (CompactJson `@i` is a Murmur3 hash, NOT an action name).
- **Wave 2 — operator parity-window checklist (agent mode, 1 plan):** PLAN-2.1 produced `docs/parity-window-checklist.md` (commit `1d16ce5`, 254 lines, 9 sections covering pre-flight / overlay config / bringup / CSV export / watch / close-out / Wave 3 expectations / failure modes). Reviewer PASS with 1 minor non-blocking suggestion (JSON-merge instruction wording).
- **Wave 3 — reconcile + release prep (team mode, 2 parallel):** PLAN-3.1 reconcile subcommand + parity-report.md template (commit `1d18b31`, +6 ReconcilerTests for 10/10 in MigrateConf.Tests). Builder correctly resolved REVIEW-1.5 F-1 by reading the actual NDJSON shape: discriminator is `@mt` (message template prefix `"BlueIris DryRun"` / `"Pushover DryRun"`), NOT `@i`. PLAN-3.2 README migration section + RELEASING.md + CHANGELOG `[Unreleased]` Phase 12 entry (commits `bb556c5` + `ed325d6` + `39c9677`). Both reviewers PASS, 0 critical findings.
- **Post-build pipeline:**
  - **Verifier:** verdict **COMPLETE** (`VERIFICATION-12.md`). Build clean (0 errors / 0 warnings). 208/208 tests across 9 projects (+10 net new from Wave 1, +6 from Wave 3). All 6 ROADMAP Phase 12 success criteria fully met (migration tool runs, output passes ConfigSizeParityTest, README migration section, parity-window checklist + report template, RELEASING.md). All 7 CONTEXT-12 D-decisions honored.
  - **Auditor:** verdict **PASS_WITH_NOTES** / Low risk (`AUDIT-12.md`). 0 critical / 0 important / 3 advisory. **A1 (proposed ID-28)** MigrateConf path-traversal — applied inline this session (commit `4d4db81`) — added `Path.GetFullPath` canonicalization in `RunMigrate`+`RunReconcile`. ID-28 marked CLOSED. **A2** carry-over of ID-19 (DryRun log-payload contains MQTT-sourced camera/label values; existing log-injection concern, no new surface). **A3** informational (RFC 5737 documentation IPs in migration doc are correct, not RFC 1918). CLAUDE.md invariants 4/4 PASS.
  - **Simplifier:** **0 High / 0 Medium / 2 Low** (`SIMPLIFICATION-12.md`). Patterns avoided positives: hand-rolled IniReader is justified (M.E.C.Ini section-collapse), DryRun copy-paste between BlueIris/Pushover is per-plugin EventId clarity not bloat, MigrateConf 2-verb dispatch fine without CommandLineParser library.
  - **Documenter:** verdict **NEEDS_DOCS** with 1 blocker — `docs/parity-window-checklist.md` lines 105-109 told operators to grep `@i` for `BlueIrisDryRun`/`PushoverDryRun`. Same F-1 mistake as PLAN-3.1's original spec, baked into the operator-facing checklist. **Applied inline** (commit `4d4db81`) — updated checklist to use `@mt` prefix matching aligned with the reconciler's actual implementation. NDJSON sample line corrected to show `@i` as a hex hash and the `@mt` template prefix as the action discriminator.
- **Issues closed this phase:** **ID-28** (MigrateConf path canonicalization — opened by AUDIT-12 A1 and closed by inline fix-up commit `4d4db81` in the same closeout session).
- **Issues opened this phase:** ID-28 (closed same-session — see above).
- **Lessons-learned drafts (for `/shipyard:ship`):**
  - **Skeleton-first prompt structure now fully proven across 3 phases.** Every Phase 12 agent that received "write skeleton FIRST" guidance produced a usable artifact even when truncating mid-investigation. The pattern works.
  - **F-1 (`@i` is a Murmur3 hash) caught in two places.** REVIEW-1.5 caught it in PLAN-3.1 (architect's spec); orchestrator added a heads-up to PLAN-3.1 frontmatter; Wave 3 builder correctly used `@mt` instead. But Wave 2's operator checklist had the SAME wrong assumption and shipped with it; Phase 12 documenter caught it post-build. **Lesson:** when a finding is caught in one plan's review, the orchestrator should grep the rest of the phase artifacts (including operator docs already landed) for the same pattern, not just propagate to one downstream plan.
  - **In-session continuation of Wave 2's 48h passive observation works.** User chose to continue immediately rather than pause-and-resume after the real window. Wave 3 builds against synthetic fixtures; the operator fills the parity-report template in their own time before running `git tag v1.0.0` per RELEASING.md. Preserves Shipyard's automation envelope without coupling it to wall-clock observation time.
  - **Hand-rolled IniReader was the right call (twice).** Architect mandated it in PLAN-1.3; builder respected it. The 9-section preservation test caught what M.E.C.Ini would have silently dropped. Had the architect not pre-decided, the builder might have reached for the canonical .NET API and shipped a silent data loss.
  - **CHANGELOG-during-phase pattern works.** PLAN-3.2 Task 3 added the Phase 12 entry to `[Unreleased]` proactively rather than retroactively. Phase 11's documenter caught Phase 11 missing-from-CHANGELOG; Phase 12 didn't repeat the gap.
  - **Team-mode for ≥3 parallel plans, agent-mode for 1-plan waves.** This phase used team mode for Wave 1 (5 plans) and Wave 3 (2 plans), agent mode for Wave 2 (1 plan). Both worked. The TeamCreate→spawn→shutdown→TeamDelete cycle adds ~5-10 tool calls of overhead which pays off only when there's parallelism to gain.
  - **Auditor-proposed ISSUES IDs can land closed-same-session when fix is trivial.** ID-28 was a 4-line `Path.GetFullPath` addition. Cleaner to ship v1.0.0 with the hardening than carry the deferred debt. Pattern: when auditor's "Effort: Trivial" + Phase is final-before-release, fix inline.
  - **Documenter still finds blockers in operator docs.** Phase 11 documenter found the missing-CHANGELOG; Phase 12 documenter found the wrong-`@i` reference in the operator checklist. Documenter agent is earning its keep — not just generating new docs but verifying the consistency of the docs already written across the phase.
  - **D7 manual v1.0.0 tag is the right boundary.** Agent never touched `git tag v1.0.0`; RELEASING.md hands the operator the exact commands. Once the operator runs the parity window for real (per Wave 2 checklist), reads the parity report, then runs the tag commands, release.yml auto-builds + pushes the multi-arch GHCR images. Clean operator-controlled release boundary.
- Checkpoint tags: `pre-build-phase-12` (created at start of build), `post-build-phase-12` (created at end of this session).
- **All 12 ROADMAP phases now COMPLETE.** Project is `/shipyard:ship`-ready pending the operator's parity window + manual `git tag v1.0.0` + `git push origin v1.0.0`.

## 2026-04-28 — Phase 12 planned (`/shipyard:plan 12` — Parity Cutover, final phase before v1.0.0)

- **Discussion capture (CONTEXT-12.md):** 7 user-locked decisions across 2 AskUserQuestion rounds:
  - **D1** Parity-window risk posture = **logging-only**. FrigateRelay does NOT trigger real BlueIris/Pushover during the ≥48h window; legacy service hits production normally; reconciliation reads FrigateRelay's structured "would-execute" log entries.
  - **D2** **3 waves**: Wave 1 active prep, Wave 2 single operator-checklist plan (the 48h passive watch), Wave 3 reconcile + release prep. Wave 2 is a discrete plan (not a no-op task) so it shows up cleanly in `/shipyard:status` and the `post-build-phase-12` checkpoint.
  - **D3** Migration script = **C# console tool**, NOT Python. User explicitly diverged from ROADMAP's Python suggestion to get compile-time schema awareness against `IConfiguration` types.
  - **D4** **Narrow scope** — no docs sprint absorption. ID-9's deferred operator/architecture/config-reference docs stay deferred beyond v1.0.0.
  - **D5** DryRun mechanism = **per-action `DryRun: true` config flag in existing plugins** (BlueIris + Pushover). Validators/snapshot providers out of scope.
  - **D6** `tools/FrigateRelay.MigrateConf/` Exe csproj + companion `tests/FrigateRelay.MigrateConf.Tests/` test csproj — both in `FrigateRelay.sln`, exercised by ci.yml.
  - **D7** v1.0.0 release tag = **manual operator step**. Wave 3 produces `RELEASING.md` snippet; operator runs `git tag v1.0.0 && git push --tags` after reading the parity report. release.yml auto-builds + pushes multi-arch GHCR images.
- **Researcher** (sonnet, 2 dispatches: original truncated, SendMessage resume completed) wrote 291-line `RESEARCH.md` covering: legacy INI structure (1× `[ServerSettings]` + 1× `[PushoverSettings]` + 9× repeated `[SubscriptionSettings]`), `ConfigSizeParityTest` semantics (60% character ratio, hard-fails without legacy.conf), action-plugin csproj patterns, dispatcher counter behavior preservation, Serilog audit-log mechanism options, release prerequisites, tools/ directory non-existence (Phase 12 introduces it). **Researcher false-positive corrected by orchestrator inline:** researcher claimed `legacy.conf` did not exist (citing wrong path under `Configuration/Fixtures/`); actual file is at `tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf` (88 lines, 11 sections — confirmed by direct `cat`). RESEARCH.md Section 8 now leads with an "ORCHESTRATOR CORRECTION" block so the architect didn't act on the false positive.
- **Architect** (opus, 1 dispatch) produced **8 plans across 3 waves (18 tasks total)** + `VERIFICATION.md` — successfully wrote VERIFICATION skeleton FIRST (lessons-learned from Phase 11 where architect lost it):
  - **Wave 1 — active prep (5 parallel, file-disjoint):**
    - PLAN-1.1 (3 tasks) — BlueIris DryRun flag (`BlueIrisActionOptions.DryRun`, plugin short-circuit, unit tests)
    - PLAN-1.2 (3 tasks) — Pushover DryRun (mirror of 1.1 for Pushover)
    - PLAN-1.3 (3 tasks) — `tools/FrigateRelay.MigrateConf/` (csproj + Program.cs + hand-rolled `IniReader` because `Microsoft.Extensions.Configuration.Ini` collapses repeated `[SubscriptionSettings]`) + `tests/FrigateRelay.MigrateConf.Tests/` round-trip against legacy.conf asserting `ConfigSizeParityTest` still passes — 494 lines, largest plan.
    - PLAN-1.4 (1 task) — `docs/migration-from-frigatemqttprocessing.md` field-by-field mapping table.
    - PLAN-1.5 (3 tasks) — Serilog NDJSON audit-log sink, opt-in via `Logging:File:CompactJson`, refactored `HostBootstrap.BuildLoggerConfiguration` for testability.
  - **Wave 2 — operator checkpoint (1 plan, gates Wave 3):**
    - PLAN-2.1 (1 task) — operator parity-window checklist file. The literal 48h wait happens out-of-session; user resumes after window closes.
  - **Wave 3 — reconcile + release prep (2 parallel, file-disjoint):**
    - PLAN-3.1 (3 tasks) — reconciliation tooling (added as a `reconcile` subcommand of MigrateConf, reading legacy CSV + FrigateRelay's NDJSON; produces `docs/parity-report.md` template). Soft-coupled to PLAN-1.5's NDJSON field shape (`Camera`, `Label`, `EventId`, `@i`).
    - PLAN-3.2 (3 tasks) — README migration-section update + `RELEASING.md` snippet + CHANGELOG `[Unreleased]` Phase 12 entry (proactive, addressing Phase 11 documenter's retroactive-CHANGELOG lesson). Promotion to `[1.0.0]` is operator step in RELEASING.md, not in the plan.
- **Architect did NOT** add operator-reference docs, architecture diagrams, or config-reference docs (D4 narrow scope — ID-9 stays deferred beyond v1.0.0). Did not pre-write deliverable content (kept plans descriptive, not prescriptive of output).
- **Plan-quality verifier (Step 6):** **PASS**. 11/11 ROADMAP deliverables mapped to concrete plan/task with runnable acceptance criteria. All 7 CONTEXT-12 D-decisions honored. ≤3 tasks/plan satisfied (3+3+3+1+3+1+3+3 = 20 — plan-quality verifier reported "21 tasks" but per-plan task counts above sum to 20; minor discrepancy noted but not material). Wave 1 file-disjoint confirmed. 0 CLAUDE.md invariant violations. 15 net new tests planned (target ≥3-5; +300% over baseline). Architect's self-VERIFICATION.md spot-checked at 5 entries; no mismatches. All 4 architect-flagged risks resolved as acceptable. Verifier wrote `VERIFICATION_PLAN_QUALITY.md` (separate from architect's `VERIFICATION.md`, same Phase 9/10/11 precedent).
- **Critique (Step 6a, feasibility stress test):** **READY** (NOT REVISE/CAUTION). 0 blocking defects. 3 documented cautions (CSV header flexibility, NDJSON field-shape soft-coupling, `legacy.conf` fixture location correction adopted from RESEARCH.md). 3 informational notes. File paths verified to exist (`legacy.conf` at `tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf`, 2329 bytes). API surfaces match (`IActionPlugin.ExecuteAsync(EventContext, SnapshotContext, CancellationToken)` 3-param shape per Phase 6 ARCH-D2). All 4 architect-flagged risks resolved.
- **Test count baseline:** 192 passing today. Phase 12 target: ≥207/207 passing post-Wave-1 (15 net new from MigrateConf round-trip, DryRun unit tests for BlueIris + Pushover, NDJSON sink tests, reconcile subcommand tests). +Wave-3 tests likely ≥3 more.
- **Lessons-learned drafts:**
  - **Skeleton-first prompt structure now established.** Phase 11's pattern (write deliverable skeleton FIRST, fill incrementally) carried over; Phase 12 architect successfully wrote VERIFICATION.md skeleton before plans, avoiding Phase 11's truncation-loses-VERIFICATION trap. Plan-quality verifier and feasibility critique BOTH wrote skeletons first. Pattern is solid; should be promoted into the Shipyard agent prompt templates.
  - **Researcher false-positives recur across phases.** Phase 10 (IMqttConnectionStatus XML docs), Phase 11 (template covers IActionPlugin only), Phase 12 (legacy.conf exists). Pattern: researcher reasons from spec/intent rather than reading actual files. Mitigation that's working: orchestrator verifies any "doesn't exist" or "is missing" claim before letting it influence downstream agents. Should bake into researcher prompts: "Before claiming a file doesn't exist, run `ls` or `find` on the parent directory and quote the listing".
  - **529 / monthly rate-limit interrupts are now tractable.** Phase 11 hit a "monthly usage limit" mid-Wave-3 reviewer; this Phase 12 session got a 529 overload notification from the user. `/shipyard:resume` + `/shipyard:plan` pattern handles both cleanly without rework. Resume detects what's missing and routes back to the right command.
  - **Hand-rolled IniReader was the right call.** `Microsoft.Extensions.Configuration.Ini` collapses repeated section keys (later wins) which would silently drop 8 of 9 `[SubscriptionSettings]` blocks. Architect caught this in PLAN-1.3 and mandated a hand-rolled parser. Mitigation pattern: when the canonical .NET API has subtle behavior that breaks domain semantics, the architect should explicitly document WHY a custom implementation is required.
  - **CHANGELOG-for-this-phase-as-a-task is now a Phase 12 plan task.** Phase 11 documenter retroactively flagged that Phase 11 was missing from `[Unreleased]`; Phase 12 PLAN-3.2 Task 3 builds it in. Pattern adopted.
  - **Wave 2 as a single operator-checklist plan is novel.** Most prior phases had only "active build" waves. The 48h passive-wait pattern (operator collects data out-of-session, then resumes) is new. Worth watching whether the build command's wave-iteration logic handles it cleanly when the wave's verification depends on operator-collected artifacts that may not exist.
- Checkpoint tag: `post-plan-phase-12` (created at end of this session).

## 2026-04-28 — Phase 11 built (`/shipyard:build 11` — Open-Source Polish)

- **Layout executed:** 3 waves / 6 plans / 18 tasks / ~17 implementation commits + 2 fix-up commits + 1 closeout. Wave 1 (gate, PLAN-1.1) sequential; Wave 2 (PLAN-2.1/2.2/2.3/2.4) team-mode 4-parallel via `shipyard-build-phase-11-wave-2`; Wave 3 (PLAN-3.1) sequential.
- **Wave 1 — test triage gate:** `Validator_ShortCircuits_OnlyAttachedAction` was failing because Phase 9's `Services.AddSingleton<ILoggerProvider>` workaround stopped working after Phase 10's `Microsoft.NET.Sdk.Web` pivot — `SerilogLoggerFactory` doesn't consult DI-registered `ILoggerProvider` instances regardless of registration order. Builder pivoted to a `CapturingSerilogSink : ILogEventSink` registered via a second `AddSerilog` call (commit `dd84185`). `TraceSpans_CoverFullPipeline` was a 1-line assertion correction: span name uses validator instance key (`validator.strict-person.check`), not plugin type (commit `157bc01`). Both Phase 9 regressions fixed; **192/192 tests passing**. Reviewer PASS (0 critical, 2 important non-blocking notes).
- **Wave 2 — 4 parallel doc/scaffold/CI plans (team mode):** TeamCreate `shipyard-build-phase-11-wave-2`, 4 builders + 4 reviewers all in same team, SendMessage shutdown + TeamDelete after gate. PLAN-2.1 (LICENSE MIT/Brian Lehnen/2026, SECURITY.md GitHub private vuln reporting, CHANGELOG.md retroactive Phases 1-10), PLAN-2.2 (README.md + CONTRIBUTING.md + CLAUDE.md staleness fix — 2 lines: project-state + Jenkinsfile description), PLAN-2.3 (`templates/FrigateRelay.Plugins.Template/` dotnet-new template with shortName `frigaterelay-plugin`, smoke-verified by builder running `dotnet new install` + `dotnet new` + `dotnet build`), PLAN-2.4 (`.github/ISSUE_TEMPLATE/{bug_report,feature_request,config}.yml` + `pull_request_template.md` + `.github/workflows/docs.yml` with 3 conditional jobs). All 4 reviewers PASS. Slug placeholder fix-up (`<owner>/frigaterelay` → `blehnen/FrigateRelay` confirmed via `git remote -v`) + `--filter-query` → `--filter` in CONTRIBUTING.md applied as orchestrator commit `f4b2439`.
- **Wave 3 — plugin-author-guide + samples (sequential):** PLAN-3.1 across 3 commits + a SUMMARY commit. `samples/FrigateRelay.Samples.PluginGuide/` with 7 source files demonstrating all 4 contracts (`IActionPlugin`, `IValidationPlugin`, `ISnapshotProvider`, `IPluginRegistrar`), wired into `FrigateRelay.sln`. `docs/plugin-author-guide.md` 11-section tutorial with 5 verbatim-copied code fences. `.github/scripts/check-doc-samples.sh` (bash + Python heredoc) extracts annotated `csharp filename=` fences and byte-compares against samples/, emitting unified diffs on drift. `docs.yml` extended with third `doc-samples-rot` job. Reviewer PASS (0 critical / 0 important / 3 minor suggestions).
- **Builder/reviewer truncation pattern persists.** Wave 3 builder hit budget after Task 1 (samples + sln); orchestrator committed Task 1 inline and dispatched a fresh builder for Tasks 2-3 with explicit "skeleton-first" guidance — that builder completed all of Tasks 2 and 3 cleanly. Wave 1 builder did the same (1 dispatch + 1 SendMessage resume). Wave 2 builders (4 parallel) all completed cleanly. Wave 3 reviewer hit a *monthly-usage rate-limit* error mid-build (separate from truncation); resumed cleanly after `/shipyard:resume` once the limit cleared.
- **Post-build pipeline:**
  - **Verifier:** verdict **COMPLETE** (`VERIFICATION-11.md`). Build clean (0 errors / 0 warnings under warnings-as-errors). 192/192 tests across 8 projects. All 6 ROADMAP success criteria fully met (scaffold smoke verified locally; doc-rot script returns exit 0 with 5 fences ok; README boilerplate-clean; CHANGELOG covers 10 retroactive phases). All 8 CONTEXT-11 D-decisions honored.
  - **Auditor:** verdict **PASS_WITH_NOTES** / Low risk (`AUDIT-11.md`). 0 critical / 0 important / 3 advisory. **A1** check-doc-samples.sh accepted unsanitized fence filenames (CWE-22 path-traversal in CI-runner FS, negligible risk; pull_request-scoped); **applied inline this session** (commit `fa51afc`) — added textual reject for `..`/leading-slash/NUL + resolved-path containment check. **A2** docs.yml tag-pinned actions extending existing ID-24 — no new finding. **A3** samples sln isolation positive note. CLAUDE.md invariants spot-check: 4/4 PASS.
  - **Simplifier:** 0 High / 0 Medium / 3 Low (`SIMPLIFICATION-11.md`). All Lows are Rule-of-Three deferral notes (CapturingSerilogSink dedup if a second test class adopts it; potential samples ↔ template content extraction if both files grow; etc.). Phase clean overall.
  - **Documenter:** verdict **ACCEPTABLE** / DEFER_TO_DOCS_SPRINT (`DOCUMENTATION-11.md`). 1 small gap flagged: Phase 11's own changes were missing from CHANGELOG `[Unreleased]` (it was retroactively populated for Phases 1-10 only). **Applied inline this session** (commit `fa51afc`) — added a Phase 11 section listing all Added / Fixed / Changed items. Template currently covers IActionPlugin only (not Validation/Snapshot) — noted for Phase 12 if desired.
- **Issues closed this phase:** **ID-4** (`--filter-query` staleness — closed by PLAN-2.2 Task 3 + Wave 2 fix-up `f4b2439` updating CONTRIBUTING.md to `--filter`).
- **Issues opened this phase:** none new (auditor advisories rolled into existing ID-24 + addressed inline).
- **Lessons-learned drafts (for `/shipyard:ship`):**
  - **Skeleton-first prompt structure prevents truncation losses.** Every Phase 11 agent prompt began with "Write the deliverable file skeleton FIRST, then fill incrementally." Result: even when agents truncated mid-investigation, the file existed with structured content. Compare to Phase 8/9 builders that wrote SUMMARY at the end and lost everything when they ran out of budget. The Wave 3 builder pattern (Task 1 commit, then fresh agent for Tasks 2-3) was the cleanest way to handle "truncation between tasks" — small sub-dispatches succeed cleanly even when full-plan dispatches don't.
  - **Phase 9 fix degradation was silent.** The `ILoggerProvider`-after-ConfigureServices workaround that closed Phase 9's exact same Validator_ShortCircuits failure stopped working when Phase 10 pivoted from `Microsoft.NET.Sdk.Worker` to `Microsoft.NET.Sdk.Web`. No test enforced the capture mechanism's correctness — the test simply started failing. The new fix (`Serilog.ILogEventSink` registered via second `AddSerilog`) is structurally more robust because it integrates at Serilog's pipeline rather than relying on factory-replacement order. Lesson: any test using DI-registered `ILoggerProvider` for log capture under Serilog should pivot to `ILogEventSink` proactively.
  - **Web SDK pivot's blast radius extended into test infrastructure.** Phase 10 audit caught the production-side surface change (Kestrel listener, `/healthz` endpoint), but the integration tests' log-capture path also broke. Phase 9-style fixes that ride on `Microsoft.Extensions.Logging`'s `ILoggerProvider` chain should be marked as "Serilog-fragile" in CLAUDE.md.
  - **Team mode worked as advertised for Wave 2.** TeamCreate / 4 builders / 4 reviewers / SendMessage shutdown / TeamDelete cycled cleanly. The 4 builders ran in true parallel (start-to-finish wall time was the slowest single builder, not the sum). Trade-off: ~5-10 extra orchestration tool calls per wave compared to plain Agent run_in_background. Worth it for ≥3 parallel builders; not worth it for 1.
  - **Rate-limit ("monthly usage limit") interrupted the Wave 3 reviewer mid-flight.** `/shipyard:resume` correctly detected all 4 post-build deliverables were missing and routed back to `/shipyard:build 11` for completion. Pattern works without modification.
  - **Documenter false-positives carry forward across phases.** Documenter-11 noted "template currently covers IActionPlugin only" — the same observation Phase 10 documenter had for IMqttConnectionStatus. Pattern: documenter reasons from intent rather than reading source. Not a blocker; just recurring noise.
  - **CHANGELOG entries should be added DURING the phase, not retroactively.** Phase 11 retroactively populated Phases 1-10 (correct), but the documenter caught that Phase 11 ITSELF was missing from `[Unreleased]`. Future phases should include CHANGELOG-entry-for-this-phase as a task in the architect's plans.
  - **Skeleton-first prompt structure also helps reviewers/verifiers/auditors.** Verifier wrote skeleton then SendMessage-resumed to fill TBDs; documenter and simplifier same pattern. The cost of a SendMessage resume is ~30s; the cost of starting over from scratch is ~2-3 minutes plus context loss.
- Checkpoint tags: `pre-build-phase-11` (created at start of build), `post-build-phase-11` (created at end of this session).

## 2026-04-28 — Phase 11 planned (`/shipyard:plan 11` — Open-Source Polish)

- **Discussion capture (CONTEXT-11.md):** 8 user-locked decisions across 2 AskUserQuestion rounds:
  - **D1** LICENSE = MIT, holder `Brian Lehnen`, year 2026.
  - **D2** Plugin scaffold via **`dotnet new` template** (NOT copy-and-rename); short-name `frigaterelay-plugin`. Diverges from ROADMAP's `sed`-rename wording — explicitly chosen for polished UX.
  - **D3** Phase 11 absorbs **4 adjacent items**: Phase 9 integration test triage, CLAUDE.md staleness fixes flagged in DOCUMENTATION-10, retroactive CHANGELOG.md (Phase 1–10), SECURITY.md.
  - **D4** New workflow file `.github/workflows/docs.yml` (NOT extending `ci.yml`); preserves the Phase 2 D1 split-CI architecture.
  - **D5** dotnet-new short-name = `frigaterelay-plugin`.
  - **D6** SECURITY.md uses GitHub private vulnerability reporting (NO mailto exposure).
  - **D7** Wave 1 = test triage; doc waves START in Wave 2 or later, gated on green test suite. Budget 1 retry; escape-hatch to `[Ignore]` + new ISSUES ID if root cause is too deep.
  - **D8** `samples/FrigateRelay.Samples.PluginGuide/` is in `FrigateRelay.sln` (warnings-as-errors discipline + IDE intellisense), but built only by `docs.yml` (not `ci.yml`).
- **Researcher** (sonnet, 2 dispatches: original + 1 SendMessage resume) wrote 469-line `RESEARCH.md` covering: existing-surface inventory (every Phase 11 deliverable confirmed missing), plugin contract surface table, BlueIris/Pushover csproj layouts, dotnet new template format with Microsoft Learn citations, CLAUDE.md staleness item locations, Phase 9 test failure analysis (`Validator_ShortCircuits` traced to Serilog clobbering DI-registered `CapturingLoggerProvider` — same Phase 9 lessons-learned pattern; `TraceSpans` test file path not located in spot-check, deferred to builder), CI surface for `docs.yml`, CHANGELOG source material, SECURITY.md template, doc-sample copy options. 4 explicit "Uncertainty Flags" surfaced for the architect.
- **Architect** (opus, 1 dispatch + 1 SendMessage resume to write final VERIFICATION.md) produced **6 plans across 3 waves (18 tasks total)** + pre-build VERIFICATION.md:
  - **Wave 1 (gate, D7):** PLAN-1.1 — test triage (3 tasks: fix `Validator_ShortCircuits_OnlyAttachedAction`, fix `TraceSpans_CoverFullPipeline`, conditional `[Ignore]` escape-hatch).
  - **Wave 2 (4 parallel, file-disjoint):** PLAN-2.1 root docs (LICENSE/SECURITY/CHANGELOG); PLAN-2.2 README + CONTRIBUTING + CLAUDE.md staleness fixes; PLAN-2.3 dotnet-new template (`templates/FrigateRelay.Plugins.Template/` with `template.json` + plugin csproj + test csproj — 8 files); PLAN-2.4 GitHub issue/PR templates + `docs.yml` (with conditional samples-build job that no-ops until Wave 3).
  - **Wave 3 (sequential):** PLAN-3.1 — `docs/plugin-author-guide.md` (tutorial-first, architect-discretion call) + `samples/FrigateRelay.Samples.PluginGuide/` + sln wiring + doc-rot check script (extends `docs.yml`'s third job — sequential touch on the workflow file PLAN-2.4 created).
- **Architect did NOT** add CODE_OF_CONDUCT, CODEOWNERS, badges, or screenshots — explicitly out of scope per CONTEXT-11.
- **Plan-quality verifier (Step 6):** **PASS**. All 13 Phase 11 deliverables (ROADMAP + D3 expansion) have explicit plan/task mapping with runnable acceptance criteria. All 8 CONTEXT-11 decisions honored. ≤3 tasks/plan satisfied (3+3+3+3+3+3 = 18). Wave 2 file-disjoint confirmed. 100% of acceptance criteria are concrete bash/Python commands. Architect's self-VERIFICATION.md spot-checked at 5 entries — no mismatches. All 5 architect-flagged risks resolved as acceptable. Verifier wrote `VERIFICATION_PLAN_QUALITY.md` (separate from architect's `VERIFICATION.md`, same Phase 9/10 precedent).
- **Critique (Step 6a, feasibility stress test):** **CAUTION** (NOT REVISE). 0 blocking defects. 3 documented builder-time lookups required: (1) `TraceSpans_CoverFullPipeline` test file path (PLAN-1.1 Task 2 — escape-hatch covers not-found case); (2) `CapturingLoggerProvider.cs` fixture exact path (PLAN-1.1 Task 1 — trivial relocation if elsewhere); (3) GitHub owner/repo slug from `git remote -v` (PLAN-2.1, PLAN-2.4 — placeholder fallback documented). All within architect's risk-list; no surprise hidden deps.
- **Test count baseline:** 192 passing today + 2 failing (`Validator_ShortCircuits_OnlyAttachedAction`, `TraceSpans_CoverFullPipeline` — phase-9 regressions). Phase 11 target: **194/194 passing** post-Wave-1 (or 192/192 + 2 documented-known-issue with new ISSUES IDs if D7 escape-hatch fires). Net new tests this phase: ≥1 (template-bundled `ExampleActionPluginTests` from PLAN-2.3) + samples project test if PLAN-3.1 designs one.
- **Lessons-learned drafts:**
  - **Researcher truncated mid-write again** (same pattern as Phase 9 RESEARCH.md). Resume via SendMessage with explicit "stop investigating, write file" guidance worked but cost ~30s of duplicate context-loading. Mitigation candidate: prompt-template the researcher to write skeleton first.
  - **Architect truncated immediately AFTER finishing the 6 plans, before writing VERIFICATION.md**. SendMessage resume completed it cleanly — but pattern persists. The plan-files-then-VERIFICATION.md ordering may itself be the trap; reverse to VERIFICATION-first-as-skeleton might help.
  - **Plan-quality verifier ran clean in one pass** (no resume needed). Critique verifier truncated mid-CLAUDE.md-check, resumed via SendMessage. Strong indication that single-file deliverables under ~100 lines complete in one tool budget; everything else needs a write-first-investigate-later prompt structure.
  - **Documenter false-positives carry over:** RESEARCH.md noted `IMqttConnectionStatus` lacks XML docs (it doesn't — Phase 10 DOCUMENTATION-10 had the same false alarm; researcher likely reasoned from the PLAN-1.1 spec rather than reading the file). The architect didn't act on the claim — good. Future researchers should always Read source before claiming documentation gaps.
  - **Phase 11 net scope is bigger than ROADMAP literal text** — D3 absorbed 4 items adding ~4 deliverables and PLAN-1.1 (test triage) is unknown-depth work. Estimated 3–4 hour ROADMAP figure may be optimistic; realistic 5–6 hours.
  - **Wave 2 forward-references are acceptable when plans declare them.** PLAN-2.4's `scaffold-smoke` job depends on PLAN-2.3's template existing, and the workflow conditional handles the gap. Architect documented this in the dependency text; both verifiers accepted it without escalation.
- Checkpoint tag: `post-plan-phase-11`.

## 2026-04-28 — Phase 10 built (`/shipyard:build 10` post-build pipeline)

- **Build state on entry:** all 5 plans (PLAN-1.1, 1.2, 1.3, 2.1, 2.2) had completed builds + reviewer PASS in the prior session, plus 4 fix-up commits already on top (`14166c2`, `161dc47`, `ddd3528`, `3b87641`, `1c3eaaa`). This session ran Step 5 onward (verifier → auditor + simplifier + documenter → state finalize) — Step 4 was skipped because all SUMMARY/REVIEW files already existed with PASS verdicts. STATE.json was stale (`phase=0`, "Phase 4 build complete...") and HISTORY had no phase-10 build entry; both corrected here.
- **Post-build verifier:** verdict **COMPLETE_WITH_GAPS** (`VERIFICATION-10.md`). Build clean (0 errors / 0 warnings under warnings-as-errors). **192/194 tests passing** across 8 test projects: Abstractions 25, Host 101, FrigateMqtt 18, BlueIris 17, Pushover 10, CodeProjectAi 8, FrigateSnapshot 6, IntegrationTests 5/7. Net-new tests this phase: ValidateSerilogPath (9 cases), MqttConnectionStatus (4 cases), HealthzReadinessTests (1 integration). 3/4 ROADMAP success criteria fully met; 1 blocked: `docker build -f docker/Dockerfile .` image-size check could not run (no Docker daemon in WSL session) — deferred to first real `v0.0.0-rc1` tag push.
- **Auditor:** verdict **PASS_WITH_NOTES** / Low risk (`AUDIT-10.md`). 0 critical / 0 important / 4 advisory items proposed and adopted as **ID-24** (release.yml actions tag-pinned not SHA-pinned, supply-chain hardening), **ID-25** (mosquitto-smoke.conf warning prominence), **ID-26** (compose example exposes 8080 on all host interfaces), **ID-27** (ValidateSerilogPath does not block Windows-style paths — accepted Linux-target gap). All four are Low/deferred. CLAUDE.md invariants spot-check: 6/6 PASS (`.Result/.Wait`, `ServicePointManager`, `App.Metrics`/`OpenTracing`/`Jaeger.`, RFC1918 IPs, secret shapes, TLS-skip scoping). Cross-component STRIDE pre-analysis included: SDK Worker→Web pivot adds Kestrel surface, but `Program.cs` registers exactly one route (`/healthz`); no auth-relevant endpoints accidentally exposed.
- **Simplifier:** 0 High / 2 Medium / 3 Low (`SIMPLIFICATION-10.md`). Both Mediums applied inline this session: (1) trimmed verbose `<remarks>` block on `internal HealthzResponseWriter` (~11 LoC saved); (2) dropped redundant `healthcheck:` block from `docker-compose.example.yml` (~6 LoC saved — Compose inherits the image's HEALTHCHECK directive). Lows deferred: `release.yml` 24-line header comment, `appsettings.Docker.json` `_comment` key, Jenkinsfile pre-Phase-4 docker.sock scaffolding. Notable "patterns avoided" call-outs: `Volatile.Read/Write` in `MqttConnectionStatus` is correct (cross-thread single-writer/single-reader signal — don't simplify); `IMqttConnectionStatus` in `Abstractions` is justified by circular-dep avoidance, not pollution.
- **Documenter:** verdict **ACCEPTABLE / DEFER_TO_DOCS_SPRINT** (`DOCUMENTATION-10.md`). Public API: only `IMqttConnectionStatus` is `public` (architecturally forced by the Sources↔Host dependency direction, documented in SUMMARY-1.1); all other phase-10 types are `internal`. Three immediate doc fixes recommended; two applied inline this session: (1) `--filter-query` → `--filter "ClassName"` in CLAUDE.md (closes **ID-4**); (2) refreshed `.github/workflows/release.yml` description in CLAUDE.md CI section (replaced "Not present yet" line with two-job structure description). Third fix (XML `<summary>` on `IMqttConnectionStatus`) was a documenter false alarm — the file already has `<summary>` on the interface plus both members; verified by direct read before editing. README/architecture/plugin-guide/CHANGELOG/CONTRIBUTING all deferred to Phase 12 docs sprint per established ID-9 pattern.
- **Issues closed this phase:** ID-21 (Serilog file sink path validation — closed by PLAN-1.2, verified at HISTORY entry time), ID-23 (file sink active in container B4 deviation — closed by PLAN-2.1, verified), ID-4 (`--filter-query` staleness — closed this session by the documenter-recommended fix). **Issues opened this phase:** ID-24, ID-25, ID-26, ID-27 (all auditor advisories, Low/deferred).
- **Stale documentation noticed but NOT fixed this session (left for Phase 11/12 docs sprint):** Jenkinsfile description in CLAUDE.md still says "tag-pinned — digest pin + Dependabot `docker` ecosystem deferred to Phase 10" — both deferrals were in fact addressed by Phase 10 fix-ups (`1c3eaaa` digest pin, `3b87641` dependabot docker), so the line is stale. Also CLAUDE.md "Project state" section still says "currently pre-implementation" which is wrong post-phase-10. Both flagged in DOCUMENTATION-10.md for future cleanup.
- **Phase verification:** COMPLETE_WITH_GAPS overall. The two failing integration tests (`Validator_ShortCircuits_OnlyAttachedAction`, `TraceSpans_CoverFullPipeline`) are pre-existing Phase 9 regressions — both touch OTel observability code (span parenting, log capture wiring), not Phase 10's Docker/healthz/Serilog-path changes. Phase 9 ROADMAP success criteria recorded "+2 integration tests pass"; that's now broken outside this phase's scope. **Recommend:** triage in Phase 11 hardening sprint, not as a Phase 10 gap-fill.
- **Lessons-learned drafts (for `/shipyard:ship`):**
  - **Post-build agents over-investigate before writing.** All four Phase-10 closeout agents (verifier, auditor, simplifier, documenter) hit their tool budget mid-investigation without writing their deliverable. Same pattern as Phase 8/9 builders. Each required one SendMessage resume with explicit "stop investigating, write the file now" guidance. Persistent mitigation candidate: bake "write skeleton FIRST, then fill" into the agent prompts as Step 0; or have orchestrator pre-write a stub the agent fills.
  - **Documenter false-positive on missing XML docs.** Recommended adding `<summary>` to `IMqttConnectionStatus` and its 2 members; direct file-read showed all three docs already present (probably written during PLAN-1.1 build, not visible to documenter unless it Read the file rather than reasoning from the plan spec). Pattern: trust-but-verify on documenter "missing X" claims — a single Read can save a wasted edit.
  - **Phase 10 stale state on resume was three-deep.** STATE.json was at `phase=0` / Phase 4 wording, no `Phase 10 built` HISTORY entry, AND `pre-build-phase-10` checkpoint already existed from before. The `/shipyard:build 10` resume path correctly detected all 5 SUMMARYs + PASS reviews and skipped Step 4 entirely. Suggests a `resume-detect-completed-plans` step is implicitly already working; worth promoting to an explicit detection note in the build command.
  - **Web SDK pivot's blast radius was contained.** PLAN-1.1 swapped `Microsoft.NET.Sdk.Worker` → `Microsoft.NET.Sdk.Web` plus `WebApplication.CreateBuilder`. The auditor confirmed only one endpoint (`/healthz`) registers and Kestrel binds only via `ASPNETCORE_URLS`. Net dependencies in `Host.csproj`: 3 packages REMOVED (now transitive via Web SDK), 0 new packages added. The SDK pivot effectively shrank the explicit dep surface while expanding capability — uncommon outcome.
  - **Compose `healthcheck` block is redundant when image has HEALTHCHECK.** Catch this in future Docker phases — Compose silently inherits the image directive unless explicitly overridden. The simplifier's drop of the duplicate block also eliminates a maintenance drift trap.
  - **`Volatile.Read/Write` over `int` for cross-thread bool.** The simplifier's "patterns avoided" note documents this for future drift: `MqttConnectionStatus` uses int-backed Volatile for the single-writer / single-reader connection signal. Looks like premature optimization at first glance — actually the correct minimal primitive (`lock` is heavier, `Interlocked` works but is less readable, plain `bool` would be a data race).
- Checkpoint tags: `pre-build-phase-10` (already existed from earlier session); `post-build-phase-10` (created at the end of this session).

## 2026-04-28 — Phase 10 build resumed for post-build pipeline (`/shipyard:resume` → `/shipyard:build 10`)

- All 5 plans (PLAN-1.1, 1.2, 1.3, 2.1, 2.2) had completed builds + reviewer PASS verdicts before the prior session ended; 4 fix-up commits also already on top (`14166c2`, `161dc47`, `ddd3528`, `3b87641`, `1c3eaaa`).
- Closeout never ran in the prior session: `STATE.json` was stale at `phase=0` / "Phase 4 build complete..." (last touched 2026-04-25), no `Phase 10 built` HISTORY entry, and none of the post-build gate artifacts (post-build VERIFICATION-10, AUDIT-10, SIMPLIFICATION-10, DOCUMENTATION-10) existed.
- This resume fast-forwards to Step 5 of `/shipyard:build` — Step 4 (build/review) is skipped because all plans already have PASS reviews. STATE bumped to `phase=10` / `status=building` / position notes the post-build pipeline is in progress.
- `pre-build-phase-10` checkpoint already exists; not recreated.

## 2026-04-27 — Phase 10 planned (Docker + Multi-Arch Release)

- 5 plans across 2 waves, 14 tasks total. All plans ≤ 3 tasks; all acceptance criteria runnable from repo root.
  - **Wave 1 (parallel-safe; file-disjoint with explicit `Host.csproj` section partition):**
    - PLAN-1.1 (3 tasks) — Host pivot from `Microsoft.NET.Sdk.Worker` to `Microsoft.NET.Sdk.Web` + `WebApplication.CreateBuilder`; new `IMqttConnectionStatus`/`MqttConnectionStatus` driven by `FrigateMqttEventSource._client.IsConnected`; `MqttHealthCheck` + `StartupHealthCheck` composed into single `/healthz`; integration test asserts 503→200 transition. **Risk: HIGH** (SDK pivot).
    - PLAN-1.2 (3 tasks) — `ValidateSerilogPath` startup pass following Phase 8 D7 collect-all pattern; rejects `..` traversal, off-allowlist absolute paths, UNC; closes **ID-21**.
    - PLAN-1.3 (3 tasks) — Publish flags (`SelfContained=true`, `PublishTrimmed=false`, `PublishAot=false`) added to `Host.csproj` `<PropertyGroup>` only; `appsettings.Docker.json` (Console-only Serilog — B4); `appsettings.Smoke.json` (minimal config validated against `ValidateActions` empty-actions tolerance); `.dockerignore`.
  - **Wave 2 (depends on all of Wave 1):**
    - PLAN-2.1 (3 tasks) — Multi-stage `docker/Dockerfile` on `runtime-deps:10.0-alpine` (digest-pinned, debian-slim fallback documented inline); non-root `USER 10001`; `HEALTHCHECK` via `wget --spider`; `docker/docker-compose.example.yml` (FR-only per D6); `.env.example`; `docker/mosquitto-smoke.conf` for PLAN-2.2 smoke. Image budget ≤ 120 MB (ROADMAP).
    - PLAN-2.2 (3 tasks) — New `.github/workflows/release.yml` on tag `v*` (setup-qemu-action@v3 + setup-buildx-action@v3 + login-action@v3 + metadata-action@v5); amd64 build → Mosquitto-sidecar smoke (HARD-FAIL on /healthz != 200) → multi-arch buildx push to GHCR; `Jenkinsfile` SDK base digest-pinned; `docker:` block added to `dependabot.yml`. **Risk: HIGH**.
- **Researcher** (sonnet) — initial run truncated mid-investigation; resumed via SendMessage with explicit "write file as final action" budget directive. Final RESEARCH.md cites `Program.cs`/`HostBootstrap.cs`/`FrigateMqttEventSource.cs`/`StartupValidation.cs` line numbers + Microsoft Learn URLs for ASP.NET Core HealthChecks + GHA buildx multi-platform docs + the OTel-on-Alpine known-issue context.
- **Architect** (opus) — produced all 5 plan files first pass. Resolved researcher's 5 blockers: (R1) `StartupValidation.cs` confirmed at `src/FrigateRelay.Host/StartupValidation.cs` with `ValidateAll(IServiceProvider, HostSubscriptionsOptions)` already pulling `IConfiguration`; (R2) Mosquitto smoke uses bind-mounted `docker/mosquitto-smoke.conf`, not bundled `mosquitto-no-auth.conf`, for self-documentation parity with compose; (R3) digest pins fetched live by builder, NOT frozen in plans; (R4) `ValidateActions` only errors on unknown plugin names, empty `Actions: []` is fine for smoke config; (R5) full pivot to `WebApplication.CreateBuilder` (researcher's option a), single task.
- **Verifier — Step 6** (haiku): **PASS**. All 9 ROADMAP success criteria + 10 CONTEXT-10 decisions covered; 5 plans / ≤3 tasks each / wave ordering correct / `Host.csproj` section partition documented in both PLAN-1.1 and PLAN-1.3.
- **Verifier — Step 6a critique** (haiku): **READY**. Six dimensions clean. Confirmed `_client.IsConnected` exists at `FrigateMqttEventSource.cs:216`; zero integration-test callsites for `HostBootstrap.ConfigureServices` (so `WebApplicationBuilder` swap is local); `ValidateActions` empty-actions tolerance matches the smoke config assumption. PLAN-1.1 flagged CAUTION (10 files, SDK pivot) but bounded by acceptance criteria.
- Open issues: only ID-21 closes this phase. ID-1/3/4/5/7/8/9/13/14/15/18/19/20/22 stay deferred.
- STATE → phase=10, status=planned. Native tasks #2–#6 created with Wave 2 (#5, #6) blocked-by Wave 1 (#2, #3, #4).

## 2026-04-27 — Phase 10 planning kicked off (Docker + Multi-Arch Release)

- Discussion-capture pass produced `.shipyard/phases/10/CONTEXT-10.md` with 6 explicit decisions + 4 bundled adjacent items.
  - **D1** Base image: **Alpine** (`runtime-deps:10.0-alpine`), with documented debian-slim fallback path inline in Dockerfile.
  - **D2** `/healthz` transport: **ASP.NET Core minimal API** (`AddHealthChecks`/`MapHealthChecks`).
  - **D3** Publish: **self-contained, untrimmed** (trim/AOT explicitly deferred — MQTTnet/OTel/Serilog reflection is hostile to both).
  - **D4** `/healthz` semantics: single endpoint, ready-state — 200 only when MQTT connected AND all `IHostedService` started.
  - **D5** Release-time smoke: `docker run` + `/healthz` GET against a Mosquitto sidecar in the release workflow; fail release if not 200. Smoke runs on amd64 only (ARM64 via QEMU is built but not smoked).
  - **D6** Compose example scope: **FrigateRelay only** (no bundled Mosquitto/WireMock); secrets via `.env`.
  - **B1** ID-21 mitigation (Serilog file sink path validation, CWE-22) bundled — pairs with non-root `USER` directive.
  - **B2** Dependabot `docker` ecosystem added — pairs with B3.
  - **B3** Tag + sha256 digest pin of base image (Dockerfile + Jenkinsfile SDK base).
  - **B4** Container-friendly logging — Console-only sink in `appsettings.Docker.json`; rolling file off by default in container.
- Defaulted (NOT asked) per ROADMAP/PROJECT.md: linux/amd64+arm64 via setup-qemu+buildx, GHCR public, `:semver`+`:latest`+`:major` tags, ≤120 MB image budget, non-root UID 10001, `linux-musl-x64`/`linux-musl-arm64` RIDs, `wget --spider` for `HEALTHCHECK` (Alpine ships wget, not curl).
- Out of scope reaffirmed: Helm/k8s manifests, Prometheus pull endpoint, cosign/sigstore, SBOM, trim/AOT, hot-reload, in-image broker. Open issues other than ID-21 (ID-13/14/15/18/19/20/22) stay deferred.
- STATE → phase=10, status=planning. Researcher dispatch next.

## 2026-04-26 — Phase 8 planned (Profiles in Configuration)

- 4 plans across 3 waves, 11 tasks total. All plans ≤ 3 tasks; all acceptance criteria measurable.
  - **Wave 1 (parallel-disjoint files, sequential commit order):**
    - PLAN-1.1 (3 tasks) — Visibility sweep (9 types → `internal`) + `ProfileOptions` + `SubscriptionOptions.Profile` + `HostSubscriptionsOptions.Profiles`. Closes ID-2 + ID-10.
    - PLAN-1.2 (2 tasks) — `ActionEntryTypeConverter` + tests + `[TypeConverter]` on `ActionEntry` + visibility flip on `ActionEntry`. Closes ID-12. **Commit-order dependency on PLAN-1.1 (CS0053 cascade) — must commit second OR be squashed into a single commit with PLAN-1.1.**
  - **Wave 2:**
    - PLAN-2.1 (3 tasks) — `ProfileResolver` + collect-all retrofit of all Phase 4–7 startup validators (D7) + ≥10 `ProfileResolutionTests` covering D1 mutex, undefined refs, and aggregated error reporting.
  - **Wave 3:**
    - PLAN-3.1 (3 tasks) — `appsettings.Example.json` (9-subscription user deployment in Profiles shape) + `legacy.conf` user-supplied prompt + `ConfigSizeParityTest` (hard-fails on missing fixture per D9) + CLAUDE.md ID-12-block update + ISSUES.md closure for ID-2/ID-10/ID-12.
- Coverage verifier: **PASS** — 5/5 ROADMAP deliverables, 9/9 D-decisions, 3/3 issue closures, 14 new tests vs ≥14 gate.
- Plan critique verifier: **READY** — all file paths exist or are properly scheduled, all API surfaces match real codebase shape, all verification commands runnable.
- One critique note (mitigated inline): PLAN-1.2 frontmatter and Dependencies section now explicitly document the commit-order constraint on PLAN-1.1. Builder must land PLAN-1.1 first OR squash both Wave 1 plans into one commit.
- Architect deviations from strict CONTEXT-8 reading (verifier judged all 4 as **strengthens**):
  - `ActionEntry` visibility flip moved into PLAN-1.2 (with TypeConverter) for cleaner file ownership.
  - `ProfileResolver` returns a new resolved list rather than mutating in place.
  - `ConfigSizeParityTest` adds an optional bind-and-validate sub-assertion.
  - `appsettings.Example.json` linked into test csproj via `<None Include=... Link=...>` to dodge relative-path issues.
- Planning artifacts:
  - `.shipyard/phases/8/CONTEXT-8.md` — 9 binding decisions D1–D9.
  - `.shipyard/phases/8/RESEARCH.md` — researcher's investigation.
  - `.shipyard/phases/8/plans/PLAN-{1.1,1.2,2.1,3.1}.md`.
  - `.shipyard/phases/8/SANITIZATION-CHECKLIST.md` — user redaction checklist for `legacy.conf`.
  - `.shipyard/phases/8/VERIFICATION.md` — coverage verdict.
  - `.shipyard/phases/8/CRITIQUE.md` — feasibility verdict.

## 2026-04-26 — Phase 8 planning kicked off (Profiles in Configuration)

- Discussion capture (CONTEXT-8.md) complete. 6 decisions locked:
  - **D1** Profile + inline = mutually exclusive (fail-fast if both, fail-fast if neither).
  - **D2** ID-12 (legacy `Actions: ["BlueIris"]` string-form silently dropped) fixed in Phase 8 via `ActionEntryTypeConverter`.
  - **D3** `ConfigSizeParityTest` measures the real (sanitized) production INI, not a synthetic.
  - **D4** Snapshot precedence unchanged — 3-tier (per-action → per-subscription → global). Profiles do NOT introduce a 4th tier.
  - **D5** Profiles are flat dictionary; no `BasedOn` / nested composition.
  - **D6** Sanitized INI fixture is user-provided with auditable `SANITIZATION-CHECKLIST.md`; build pauses if missing — no auto-sanitization regex pass.
- Cross-cutting note for architect: consider folding ID-2/ID-10 (visibility sweep on `ActionEntry`, `SubscriptionOptions`, `IActionDispatcher`, etc.) into Phase 8 since the same files are being modified — flag if scope feels stretched.
- Native task scaffolding: 6 tasks created (#1 Discussion, #2 Researcher, #3 Architect, #4 Verifier-coverage, #5 Verifier-critique, #6 Commit+checkpoint). #1 complete.

## 2026-04-24 — Project initialized

- Phase: 1
- Status: ready
- Message: Project initialized
- Settings: interactive mode, manual git, detailed review, security audit on, simplification on, IaC auto, docs gen on, codebase docs at `.shipyard/codebase`, default model routing, auto context tier.
- Detected as **brownfield** (existing .NET Framework 4.8 source in `Source/`).
- Repository is **not** a git repository at init time — no commit created.

## 2026-04-24 — Codebase mapped (all 4 focus areas)

- 4 mapper agents dispatched in parallel (agent mode).
- Files written to `.shipyard/codebase/`:
  - `STACK.md` (12.6 KB) — languages, runtime, NuGet deps by purpose
  - `INTEGRATIONS.md` (11.3 KB) — MQTT, Blue Iris, Pushover, Jaeger, Seq, (dead) CodeProject.AI; TLS bypass called out
  - `ARCHITECTURE.md` (14.1 KB) — 5-project layering, end-to-end event flow, lifecycle
  - `STRUCTURE.md` (in ARCHITECTURE.md bundle) — on-disk map
  - `CONVENTIONS.md` (14.2 KB) — style patterns including the Serilog wrong-overload bug at `Main.cs:269`
  - `TESTING.md` (5.6 KB) — no tests exist; manual validation only
  - `CONCERNS.md` (16.4 KB) — 24 concerns; top 3: TLS bypass, plaintext Pushover creds, no tests/no git
- Bonus finding: inverted directory guard in `SetupMetrics` at `FrigateMain.cs:118-119`.
- Mapper noted `SharedAssemblyInfo.cs` presence discrepancy vs CLAUDE.md — worth spot-checking.

## 2026-04-24 — Brainstorm complete, PROJECT.md + ROADMAP.md written

- 8 Socratic questions resolved the greenfield design:
  - Plugin loading: **A3** (ship A, design for B / DLL-drop loader).
  - Validator scope: **V3** (per-action).
  - Config shape: **S2** (Profiles + subscriptions).
  - Async pipeline: **P1** (Channel + Polly) with **P3** escape hatch (`IActionDispatcher` seam).
  - Input: **I2** (`IEventSource` abstraction, Frigate-only v1).
  - Snapshots: `ISnapshotProvider` as its own plugin type with per-action override.
  - Observability: M.E.L. + Serilog sinks + OpenTelemetry OTLP (drop OpenTracing + App.Metrics).
  - Test stack: MSTest v3 + MSTest `Assert` + FluentAssertions 6.12.2 (pre-commercial, pinned) + NSubstitute + Testcontainers.NET.
  - License: MIT.
  - Name: **FrigateRelay**.
- Positioning: differentiates from `0x2142/frigate-notify` via first-class BlueIris support (action + snapshot source).
- `PROJECT.md` written with all 9 decision rows locked in a summary table.
- `shipyard:architect` agent dispatched; produced `ROADMAP.md` with 12 phases, dependency-ordered, risk front-loaded, every success criterion verifiable by the author alone.
- Revision round 1 applied: Phase 11 onboarding criterion swapped from external-volunteer timed dry-run to a `scaffold-smoke` CI job; Phase 12 estimate clarified to separate ~6h active work from ~48h passive observation.
- Git strategy is **manual**; repo is not a git repo. No commit created.
- Deferred decisions surfaced (documented in ROADMAP Questions appendix): Alpine vs Debian-slim base image (decided in Phase 10); `/healthz` transport (minimal API vs raw TCP, decided in Phase 10).
- Open workflow question: FrigateRelay greenfield will live in a separate folder (likely `/mnt/f/git/FrigateRelay/`). The physical target location will be decided at the first `/shipyard:plan` step.

## 2026-04-24 — CLAUDE.md written (`/init`)

- Root `CLAUDE.md` created, derived from `.shipyard/PROJECT.md` + `ROADMAP.md`. Captures architecture invariants, planned repo shape, commands, testing stack, and the "deliberately excluded" list (DotNetWorkQueue, App.Metrics, OpenTracing, SharpConfig, Topshelf, Newtonsoft.Json).
- User appended a "Project Instructions" note pinning Context7 MCP for library/API docs lookups.

## 2026-04-24 — Phase 1 planned (`/shipyard:plan 1`)

- Working directory: `/mnt/f/git/FrigateRelay/` (git repo, branch `Initcheckin`). Physical target location resolved.
- Discussion capture: 4 decisions recorded in `.shipyard/phases/1/CONTEXT-1.md`:
  - **D1** — `PluginRegistrationContext` carries `IServiceCollection` + `IConfiguration`.
  - **D2** — Phase 1 uses M.E.L. console provider only (Serilog + OTel deferred to Phase 9).
  - **D3** — `Verdict` uses static factories (`Pass()` / `Pass(score)` / `Fail(reason)`) with non-public ctor.
  - **D4** — `global.json` pins `10.0.203` with `rollForward: latestFeature`.
- Researcher agent produced `.shipyard/phases/1/RESEARCH.md`: .NET 10.0.203 SDK (2026-04-21), MSTest 4.2.1 (2026-04-07), FluentAssertions 6.12.2 (license-safe, works on net10.0), NSubstitute 5.3.0. Flagged 4 open questions for the architect.
- Architect resolved all 4 open questions inline in the plans:
  - MSTest `PackageReference` (not `MSTest.Sdk` project SDK) — Dependabot can update PackageReferences but not `msbuild-sdks`.
  - `TreatWarningsAsErrors` applied globally incl. tests; per-project `<NoWarn>` is the escape valve.
  - `appsettings.Local.json` copied via explicit `<Content CopyToOutputDirectory="PreserveNewest" Condition="Exists(...)">`.
  - `UserSecretsId` hard-coded to a stable GUID (`9a7f6e02-3c8b-4d2e-9b17-afb4c6e03a10`) for contributor consistency.
- 3 plans written across 3 waves (linear chain, 1 plan per wave):
  - **Wave 1 — PLAN-1.1** Repo Tooling and Empty Solution.
  - **Wave 2 — PLAN-2.1** `FrigateRelay.Abstractions` and Contract-Shape Tests.
  - **Wave 3 — PLAN-3.1** `FrigateRelay.Host`, Registrar Loop, and Host Tests.
- Verifier (spec-compliance): **READY** — all Phase 1 ROADMAP deliverables owned by exactly one plan, 4 CONTEXT decisions honored, 13 planned tests exceed the ≥6 gate. Report: `.shipyard/phases/1/VERIFICATION.md`.
- Verifier (feasibility critique): **READY** — no cross-wave forward references, verify commands valid on Linux + Windows, three minor risks flagged (NSubstitute.Analyzers + WAE warnings, `appsettings.Local.json` copy behavior, Windows SIGINT parity) all with documented mitigations. Report: `.shipyard/phases/1/CRITIQUE.md`.
- Zero revision cycles needed.
- [2026-04-24T18:10:36Z] Session ended during build (may need /shipyard:resume)

## 2026-04-24 — Phase 1 built (`/shipyard:build 1`)

- **Waves executed sequentially** — all plans 1 plan per wave (linear chain).
- **Wave 1 (PLAN-1.1)** — builder agent (Sonnet 4.6) delivered 3 atomic commits (`b480f12` → `5de3227`) clean. Reviewer: **PASS**. SDK pin corrected from fabricated `10.0.203` to real `10.0.100` + `rollForward: latestFeature` (user installed `10.0.107` via `apt`; original RESEARCH.md claim was wrong). Caveats captured: `dotnet new sln --format sln` required vs new `.slnx` default; empty-solution NuGet warning expected until Wave 2.
- **Wave 2 (PLAN-2.1)** — builder truncated mid-task-3; orchestrator finished inline. 3 commits. Reviewer: **PASS**. Ripples surfaced: `[SetsRequiredMembers]` required on `PluginRegistrationContext` ctor; `.NET 10 dotnet test` blocked against MTP (use `dotnet run --project` against `OutputType=Exe` test exe); `dotnet list` flag order changed; test-project `[tests/**.cs]` editorconfig suppression for CA1707 + IDE0005.
- **Wave 3 (PLAN-3.1)** — builder truncated after task 1; orchestrator finished task 2 but initially missed `PlaceholderWorkerTests.cs`. Reviewer caught gap → orchestrator gap-fix at `ef68446` added the missing test file (2 tests) + 3 explicit `Microsoft.Extensions.*` PackageReferences. Re-review: **PASS**. Phase 1 total: **17 tests pass**.
- **Step 5 Phase verification** — COMPLETE. All 12 closeout criteria met.
- **Step 5a Security audit** — PASS / Low risk. 0 critical/high/medium, 1 low (`.gitignore` `**/` prefix — applied), 2 info (CI deferred to Phase 2; FluentAssertions 6.12.2 license pin intended).
- **Step 5b Simplification** — 1 medium (bootstrap `LoggerFactory` in `Program.cs` over-ceremony) — **applied**; 2 low (single-use `LoggerMessage.Define`, `CapturingLogger<T>` private-nested) — deferred.
- **Step 5c Documentation** — 3 medium CLAUDE.md gaps — **all applied**: `.NET 10 dotnet test` caveat + new `## Conventions` section capturing `[SetsRequiredMembers]`, `CapturingLogger<T>`, `<InternalsVisibleTo>` MSBuild item, test-name underscore convention.
- **Lessons-learned draft** (captured in SUMMARY files for ship-time):
  - SDK-version research for unreleased feature bands is unreliable — cross-check `builds.dotnet.microsoft.com` release-metadata JSON before pinning.
  - Verification must grep each plan's `files_touched:` frontmatter against `git log --name-only`, not just the ROADMAP test-count gate (the orchestrator shipped Wave 3 initially missing a whole test file because the phase-wide test count was already clear).
  - Builder agents truncate on 30+ tool-use runs; consider a "checkpoint after each task + commit" style to make resumption cheap.
  - WSL `timeout --signal=SIGINT` does not propagate through `dotnet run`; use `pgrep | kill -INT` or publish self-contained.
- Checkpoint tags: `pre-build-phase-1`, `post-build-phase-1`.

## 2026-04-24 — Phase 2 planned (`/shipyard:plan 2`)

- **5 decisions captured** in `CONTEXT-2.md`:
  - D1 — Mirror DotNetWorkQueue's CI topology (GH Actions `ci.yml` + `secret-scan.yml` + `dependabot.yml` for fast PR gating; `Jenkinsfile` for coverage on self-hosted Jenkins). User-confirmed after surveying DNWQ's actual 68-line `ci.yml` / 385-line `Jenkinsfile` split at `F:\Git\DotNetWorkQueue`.
  - D2 — Test invocation is `dotnet run --project tests/<project> -c Release` in GH, same + `-- --coverage --coverage-output-format cobertura` on Jenkins (.NET 10 SDK blocks `dotnet test` against MTP).
  - D3 — Defer graceful-shutdown smoke to Phase 4 Testcontainers integration tests. GH matrix Windows parity for a SIGINT dance isn't worth the complexity.
  - D4 — Secret-scan tripwire has a self-test job against a committed fixture (`.github/secret-scan-fixture.txt`). Ongoing regex-drift detection without manual one-shot poison-branch verification.
  - D5 — Dependabot covers `nuget` + `github-actions` ecosystems (not `docker` — Phase 10).
- **Researcher** produced `RESEARCH.md` with MTP code-coverage CLI flags, `setup-dotnet@v4` + `global-json-file` cookbook, Dependabot v2 schema template, scripted Jenkinsfile skeleton adapted from DNWQ, 7-pattern secret-scan regex set with FP risk assessment, fixture draft, DNWQ-copy/don't-copy list. Flagged 6 open questions for the architect.
- **Architect** resolved all 6 open questions inline and produced 4 plans across 3 waves:
  - Wave 1: `PLAN-1.1` Dependabot; `PLAN-1.2` secret-scan workflow + fixture + self-test. Parallel-safe.
  - Wave 2: `PLAN-2.1` GH Actions `ci.yml` (matrix ubuntu+windows, setup-dotnet@v4, build + `dotnet run` per test project, no coverage).
  - Wave 3: `PLAN-3.1` `Jenkinsfile` (scripted, Docker `sdk:10.0`, MTP cobertura per project, workspace-local NuGet cache, modern Coverage plugin `recordCoverage`).
- **Verifier (spec-compliance)**: **READY** — all ROADMAP Phase 2 deliverables owned, all 5 decisions honored, all plan structural rules met.
- **Verifier (feasibility critique)**: **READY** with one caveat carried forward to PLAN-3.1 builder: MSTest 4.2.1's MTP code-coverage extension writes output XML to `TestResults/coverage/*.cobertura.xml` subdirectories of each test project's assembly output path — NOT the `--coverage-output` path the researcher initially assumed. Jenkinsfile archive glob needs to be `tests/**/TestResults/coverage/**/*.cobertura.xml` or a post-test copy step. Critic actually ran the MTP CLI on this machine to confirm.
- Zero revision cycles needed.
- Checkpoint tag: `post-plan-phase-2`.

## 2026-04-24 — Phase 2 built (`/shipyard:build 2`)

- **Wave 1 (parallel, PLAN-1.1 + PLAN-1.2)** — 5 commits (`3172d9f`, `01c051f`, `fea2d9a`, `dec0494`). PLAN-1.1 (Dependabot): builder PASS clean. PLAN-1.2 (secret-scan): builder committed all 3 tasks; reviewer flagged MINOR — the `\x27` escape in Pattern 4's character class was unsupported in ERE, silently making the regex match `\`, `x`, `2`, `7` instead of a single quote. Fixed in `579126e` using bash's `["'"'"']?` escape idiom; added a second fixture line to exercise both quote branches.
- **Wave 2 (PLAN-2.1 GH CI)** — commit `13e3ea2`. Reviewer PASS. Matrix ubuntu+windows, `dotnet run --project` per test project (NOT `dotnet test`), `shell: bash` on test steps for Windows consistency, no coverage flags (coverage is Jenkins-side per D1).
- **Wave 3 (PLAN-3.1 Jenkinsfile)** — commit `3070ac6`. Reviewer PASS. **Important finding**: the feasibility CRITIQUE had warned `--coverage-output <path>` might not be honored by MSTest 4.2.1's MTP coverage extension on .NET 10 (based on WSL-host observation, files landed in `TestResults/` subdirs). Builder's Docker simulation against `mcr.microsoft.com/dotnet/sdk:10.0` produced 11,609 bytes of cobertura XML at exactly the explicit path — the caveat was WSL-host-specific, not container behavior. Archive glob simplified to `coverage/**/*.cobertura.xml`.
- **Step 5 Verification** — COMPLETE. All 3 ROADMAP success criteria met (via structural proxies pre-merge). Build clean, 17 tests pass, scope discipline honoured (no Dockerfile, no release.yml).
- **Step 5a Auditor** — PASS / Medium risk. 1 MEDIUM (Docker image tag-pinned, not digest — deferred to Phase 10 when Dependabot docker ecosystem lands); 1 LOW (RFC-1918 regex over-broad — deferred until it actually bites a real README example); all INFO items confirm no new secrets, no cache-poisoning vector, no third-party actions, no ReDoS risk.
- **Step 5b Simplifier** — 1 MEDIUM (test-project list duplicated across `ci.yml` + `Jenkinsfile`) deferred to Phase 3 per Rule of Three; 2 LOW (stale Dependabot comment + Jenkinsfile no-op top-level post block) **applied** — ~15 LoC delta, no behavior change.
- **Step 5c Documenter** — 2 MEDIUM (CLAUDE.md missing the "CI-coverage-split" rule and the secret-scan self-test mechanism) **applied**. Rewrote CLAUDE.md's `## CI` section: explains the D1 split, documents the self-test tripwire, notes `--coverage-output` is honored in the SDK container, flags the hard-coded test-project list as a Phase 3 consideration with the Rule of Three reasoning.
- **Lessons-learned drafts** (for `/shipyard:ship`):
  - **Regex authoring in bash single-quoted strings needs care**: `\x27` is valid in PCRE but NOT in ERE. When mixing bash quoting + regex escape, always exercise BOTH branches in the fixture. If a character class doesn't get matched by fixtures, the tripwire can rot silently.
  - **Container vs host environment diverges**: a feasibility caveat observed on the WSL host (MTP coverage writes to `TestResults/` subdir) did not reproduce in `mcr.microsoft.com/dotnet/sdk:10.0`. Always re-verify platform-sensitive behavior in the actual CI container, not on the dev box.
  - **DotNetWorkQueue's CI split is a good template** (GH for PR gate, Jenkins for coverage). The structural pattern survives the .NET 8 → .NET 10 transition; only the test-invocation shape (`dotnet run` not `dotnet test`) needs adapting.
  - **Reviewer agent does not self-persist REVIEW-*.md files**; the orchestrator must write them from the inline report. Consider adjusting the reviewer agent's system prompt in Shipyard config (flagged in Phase 1 history, still unresolved).
- Checkpoint tags: `pre-build-phase-2`, `post-build-phase-2`.

## 2026-04-24 — Phase 3 planned (`/shipyard:plan 3`)

- **Discussion capture** produced CONTEXT-3.md with 5 decisions, **updated mid-planning** after researcher findings:
  - **D1** — Fire ALL matching subscriptions (deviates from legacy first-match-wins; flagged for Phase 12 parity docs).
  - **D2** — Keep `string RawPayload` (no contract change — YAGNI).
  - **D3** — **Revised** from "extract base64 thumbnail" to **no-op returning null**. Researcher found Frigate's `thumbnail` in `frigate/events` is always null per official docs; thumbnails are on separate per-camera MQTT topics. `SnapshotFetcher` on `EventContext` is flagged for simplifier review at Phase 5 (may be removed from contract).
  - **D4** — Defer Testcontainers to Phase 4; Phase 3 is unit tests only.
  - **D5** — **Added** a `false_positive` skip alongside the stationary guard. Small deviation from legacy; flagged for Phase 12 docs.
  - Correction: `ManagedMqttClient` doesn't exist in MQTTnet v5 — use plain `IMqttClient` + custom 5s reconnect loop.
- **Researcher** produced RESEARCH.md (638 lines): MQTTnet v5 cookbook (correct v5 APIs, TLS via `WithTlsOptions`), Frigate payload schema with annotated samples, DTO templates, `Channel<T>` push→pull pattern, .NET 10 keyed-singleton `IMemoryCache` (Option A), EventPump `BackgroundService` recommendation. Flagged 5 open questions (D3 reality check, false-positive filter, DTO record shape, zones aggregation, ManagedMqttClient correction).
- **Architect** produced 4 plans; flagged self as potentially speculative about two new interfaces.
- **Spec verifier**: PASS. **Critique verifier**: CAUTION — two new abstractions `ISubscriptionProvider` + `IEventMatchSink` in `FrigateRelay.Abstractions` judged as YAGNI over-abstraction.
- **User chose revision cycle.** Architect rewrote all 4 plans:
  - `FrigateRelay.Abstractions` receives **zero new types** — the assembly's surface does not widen in Phase 3.
  - `SubscriptionMatcher`, `DedupeCache`, `SubscriptionOptions`, `HostSubscriptionsOptions` moved from plugin to `src/FrigateRelay.Host/Matching/` and `src/FrigateRelay.Host/Configuration/`. Matcher + dedupe are universal across any future `IEventSource` (camera/label/zone are on `EventContext` top-level).
  - `FrigateMqttOptions` becomes transport-only (no `Subscriptions` member).
  - `Subscriptions` config binds from a **top-level** section (matches Phase 8 Profiles+Subscriptions shape).
  - `EventPump` takes 4 DI deps: `IEnumerable<IEventSource>`, `DedupeCache`, `IOptions<HostSubscriptionsOptions>`, `ILogger<EventPump>`. Calls static `SubscriptionMatcher` directly.
- **Post-revision re-verification**: both verdicts READY. Residual concerns are mechanical (MQTTnet v5 method-signature validation at Wave 2 start; startup race documented as mitigated by unbounded channel).
- **Test count plan**: PLAN-1.1=6, PLAN-1.2=9, PLAN-2.1=11, PLAN-3.1=2 → 28 total (≥15 gate, +87% cushion).
- Architect also consolidates CI: extracts `.github/scripts/run-tests.sh` used by both `ci.yml` and `Jenkinsfile` — addresses the Phase 2 advisory about hard-coded test-project lists now that Phase 3 adds a third test project.
- Other architect decisions: single shared `FrigateEventObject` record (DRY per OQ3), union all four zone arrays into `EventContext.Zones` during projection (OQ4), `PlaceholderWorker` removed in favor of `EventPump`.
- Checkpoint tag: `post-plan-phase-3`.

## 2026-04-24 — Phase 3 built (`/shipyard:build 3`)

- **Wave 1 (parallel)** — PLAN-1.1 Frigate DTOs (6 tests) + PLAN-1.2 matcher/dedupe in Host (9 tests). Both reviewed with Important findings applied in-place:
  - PLAN-1.1 R1: `FrigateEvent.Before/After` required-non-nullable → defensive nullable; tests updated to `evt!.After!.X` pattern.
  - PLAN-1.1 R2: `FrigateJsonOptions.Default` sealed via `MakeReadOnly(populateMissingResolver: true)` (plain `MakeReadOnly()` throws on .NET 10 without a TypeInfoResolver).
  - PLAN-1.2 R1: `DedupeCache.TryEnter` TOCTOU race → single-lock serialisation.
- **Wave 2 (PLAN-2.1)** — EventContextProjector (7 tests), FrigateMqttEventSource (5 tests), PluginRegistrar. Plain `IMqttClient` + custom 5s reconnect loop (MQTTnet v5 has no ManagedMqttClient). Channel<EventContext> unbounded bridge. Per-client TLS via `WithTlsOptions`.
- **Wave 3 (PLAN-3.1)** — EventPump `BackgroundService` wiring `IEnumerable<IEventSource>`, DedupeCache, HostSubscriptionsOptions, Program.cs rewrite, PlaceholderWorker removal, `.github/scripts/run-tests.sh` consolidation (2 Host tests).
- **Two real bugs found and fixed during Phase 3 build**:
  - `PluginRegistrarRunner.RunAll` was moved AFTER `builder.Build()` by Phase 1's simplification. Registrars mutate `builder.Services`, which has no effect on an already-built provider. With Phase 1's empty registrar list this was latent; Phase 3's real registrar would have silently dropped plugin registration. **Fixed**: moved back to pre-Build with a minimal bootstrap LoggerFactory; inline comment warns future contributors.
  - `FrigateMqttEventSource.DisposeAsync` threw `ObjectDisposedException` during host shutdown because the linked CTS was cancelled against an already-disposed source token. **Fixed**: `Interlocked.Exchange` idempotency guard + targeted `catch (ObjectDisposedException)` wrappers. Exposed by the graceful-shutdown smoke test, not by any unit test.
- **Builder agent truncation** was pervasive in Phase 3 (Wave 1 ×2, Wave 2 ×1, Wave 3 partially). Orchestrator completed each task inline. Two Important reviewer findings were applied by the orchestrator. All 13 Phase-3 commits are atomic; git history is clean.
- **44 tests pass** (10 Abstractions + 16 Host + 18 Sources.FrigateMqtt). Phase-3 new tests: 29 (≥15 gate; +93% cushion).
- **Graceful shutdown smoke (no broker)**: exit 0; log shows "Application is shutting down..." → "Event pump stopped for source=FrigateMqtt" → "MQTT disconnected".
- **CI shared `run-tests.sh` discharges Phase-2 advisory.** Script auto-discovers test projects; a future test project needs zero workflow edit. Canonical-path fallback copy handles the MTP `--coverage-output` WSL/container divergence.
- **Phase 3 gate results**:
  - Verifier: **COMPLETE** — all ROADMAP criteria met, all D1–D5 honored, zero Abstractions diff.
  - Auditor: **PASS / Low** — 5 advisory notes (MQTT creds later, unbounded channel acceptable, CancellationToken.None usage intentional, NuGet lockfile suggestion, no CVEs in MQTTnet 5.1.0.1559).
  - Simplifier: 3 Medium + 3 Low. User deferred both Med items (DisposeAsync triple catches, HostSubscriptionsOptions wrapper) — the wrapper is intentionally preserved for Phase 8 expansion.
  - Documenter: 3 HIGH + 2 MEDIUM + 1 LOW CLAUDE.md gaps. User deferred all to **Phase 11 OSS polish**. SUMMARY-3.1 captures the facts for ship-time lessons extraction.
- **Lessons-learned drafts** (for `/shipyard:ship`):
  - Phase-1 simplifications can be latent bugs — the `RunAll`-after-`Build()` trap was invisible until Phase 3 added a real registrar. Inline comments about ordering invariants are cheap insurance.
  - MQTT ecosystem moves fast — ROADMAP wrote "`ManagedMqttClient`" assuming MQTTnet v4; v5 removed it. Always cross-check ROADMAP product references at phase start.
  - Environment divergence (WSL vs SDK container): `--coverage-output` is honored in one and not the other. Scripts that paper over this become the right shape.
  - Graceful shutdown paths need real smoke tests — the `ObjectDisposedException` in `DisposeAsync` would never have been caught by unit tests; it took a `pgrep | kill -INT` end-to-end run to surface.
- Checkpoint tags: `pre-build-phase-3`, `post-build-phase-3`.

## 2026-04-25 — Phase 4 planning started (`/shipyard:plan 4`)

- Discussion capture (`CONTEXT-4.md`) recorded **7 decisions** before research dispatch:
  - **D1** — Channel topology: per `IActionPlugin` (not shared, not per-(sub, action)). 2 consumer tasks per channel.
  - **D2** — Subscription→action wiring: new `Actions: ["BlueIris"]` array on `SubscriptionOptions`. Empty = no fire (fail-safe). Unknown name = startup fail-fast (matches PROJECT.md S2).
  - **D3** — BlueIris URL template: `{placeholder}` syntax with fixed allowlist (`{camera}`, `{label}`, `{event_id}`, `{score}`, `{zone}`). Unknown placeholder = startup fail-fast. Values URL-encoded.
  - **D4** — Validators: empty `IReadOnlyList<IValidationPlugin>` parameter on dispatcher NOW (smooths Phase-7 diff; behavior identical to "no validators" in v1).
  - **D5** — Default channel capacity: 256 (configurable per plugin via `BlueIrisOptions.QueueCapacity`). `BoundedChannelFullMode.DropOldest`. `SingleWriter = true` (EventPump is sole producer).
  - **D6** — Drop telemetry: BOTH `frigaterelay.dispatch.drops` Meter counter (tagged `action`) AND `LogWarning` carrying event_id + action + capacity. Roadmap mandates both.
  - **D7** — Polly v8: `AddResilienceHandler` on the named `HttpClient` (Microsoft-blessed pattern, NOT inline `ResiliencePipelineBuilder` in dispatcher). `HttpRetryStrategyOptions.DelayGenerator` returns 3/6/9-second fixed delays. Per-plugin TLS opt-in via `ConfigurePrimaryHttpMessageHandler` + `SocketsHttpHandler.SslOptions.RemoteCertificateValidationCallback`, gated by `AllowInvalidCertificates` flag.
- Phase 4 directory scaffolded (`plans/`, `results/`).
- Next: researcher agent dispatch (M.E.Resilience HttpRetryStrategyOptions surface, Testcontainers Mosquitto, WireMock.Net stub patterns, IHttpClientFactory + per-plugin TLS handler, Channel<T> drop-oldest semantics).

## 2026-04-25 — Phase 4 planned (`/shipyard:plan 4`)

- **Researcher agent truncated twice** at ~35-tool-use cap without writing `RESEARCH.md` (same pattern as Phase 1 builder Wave 2/3 truncations). Orchestrator completed inline using Microsoft Learn MCP + Context7 MCP — same fallback pattern that finished prior phases.
- **Three high-value research findings corrected CONTEXT-4.md drafts:**
  - **`Channel.CreateBounded<T>` has a built-in `itemDropped: Action<T>?` callback** — no `TryWrite` wrapper needed. Drop telemetry is one closure capture instead of a synchronisation-prone polling pattern.
  - **Polly v8 `DelayGenerator.AttemptNumber` is zero-indexed for the first retry** — formula `3 * (AttemptNumber + 1)` produces 3/6/9s exactly. Documented in PLAN-2.1 Task 2 as a regression test.
  - **CI scripts auto-discover via `find tests/*.Tests/*.Tests.csproj`** — adding `tests/FrigateRelay.IntegrationTests/` requires zero `run-tests.sh` / `Jenkinsfile` edits. Only the GH Actions Windows leg needs a `--skip-integration` flag (Testcontainers cannot run Linux containers on `windows-latest`).
- **Architect** (opus) wrote 6 plan files in 18 tool uses (clean, no truncation). Resolved both RESEARCH.md open questions inline:
  - **Q1**: Read `EventContext.cs`, found no `Score` property → dropped `{score}` from D3 allowlist. Final allowlist is `{camera}, {label}, {event_id}, {zone}`. Encoded as regression test in PLAN-1.2 Task 3 (`Parse_WithScorePlaceholder_ThrowsBecauseScoreIsNotInAllowlist`).
  - **Q2**: Confirmed `frigaterelay.dispatch.exhausted` (tagged `action`) for retry-exhaustion telemetry, emitted from the consumer `catch` block in PLAN-2.1 Task 1. Distinct from queue-overflow `frigaterelay.dispatch.drops`.
- **Wave structure (6 plans, 18 tasks):**
  - **Wave 1** (parallel): PLAN-1.1 IActionDispatcher + DispatchItem + ChannelActionDispatcher skeleton; PLAN-1.2 BlueIris csproj + BlueIrisOptions + BlueIrisUrlTemplate.
  - **Wave 2** (parallel): PLAN-2.1 dispatcher consumer body + Polly retries + retry-exhaustion telemetry; PLAN-2.2 BlueIrisActionPlugin + registrar (HttpClient + AddResilienceHandler + per-plugin TLS).
  - **Wave 3** (parallel): PLAN-3.1 SubscriptionOptions.Actions[] + EventPump dispatch wiring + Program.cs registrar + startup fail-fast on unknown action names; PLAN-3.2 IntegrationTests project + MqttToBlueIris_HappyPath (Testcontainers + WireMock) + CI Windows-skip flag + Jenkinsfile doc-comment.
- **Verifier (spec compliance)**: **READY** — all 13 ROADMAP deliverables owned, all 7 D1–D7 decisions honored, all CLAUDE.md invariants enforced via grep verification commands. 2 non-blocking caveats: PLAN-3.1 should gate BlueIris registrar on `Configuration.GetSection("BlueIris").Exists()`; PLAN-3.2's HostBootstrap extraction is a small ordering ripple if 3.1 ships first.
- **Verifier (feasibility critique)**: **READY** — all modify-target files exist, no same-wave forward refs, ≥6 dispatcher tests + 1 integration test gates met. Top mitigated risks: Polly AttemptNumber off-by-one (test-encoded), Testcontainers on Windows runner (`--skip-integration` flag), parallel sln edits (standard git merge).
- Zero revision cycles needed.
- **Cross-cutting decisions captured for builder:**
  - `IActionDispatcher` lives in `src/FrigateRelay.Host/Dispatch/`, NOT `Abstractions` (host-internal seam; plugins consume via DI).
  - `DispatchItem` is `readonly record struct` with EventContext + IActionPlugin + IReadOnlyList<IValidationPlugin> + Activity? (cheap enqueue, no GC pressure).
  - `ChannelActionDispatcher` implements `IHostedService` directly (NOT `BackgroundService`) — channel construction in `StartAsync`, drain in `StopAsync` after `Writer.Complete()`.
  - Per-plugin queue capacity is read host-side from `BlueIris:QueueCapacity`, NOT inside the BlueIris plugin assembly (keeps `FrigateRelay.Plugins.BlueIris` free of any `FrigateRelay.Host` reference).
  - `tests/FrigateRelay.Plugins.BlueIris.Tests/` is a NEW test project (matches per-source-project precedent from Phase 3).
- Next: `/shipyard:build 4`.

- [2026-04-25T17:36:33Z] Phase 4: Building phase 4 (building)
- [2026-04-25T17:44:02Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T17:55:00Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T17:55:31Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T17:59:17Z] Phase 4: Phase 4 wave 1 partial: PLAN-1.2 complete, PLAN-1.1 Tasks 1+2 committed (713065a, ed9e9a9), Task 3 (3 unit tests) + SUMMARY-1.1.md pending. Wave 2+3 unstarted. Restart session to enable agent teams (CLAUDE_CODE_EXPERIMENTAL_AGENT_TEAMS=1) then resume via /shipyard:build 4. (paused)
- [2026-04-25T18:02:21Z] Phase ?: Phase 4 build resumed (team mode): completing wave 1 remainder (PLAN-1.1 task 3) (building)
- [2026-04-25T18:04:59Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T18:08:26Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T18:09:02Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T18:14:04Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T18:19:43Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T18:22:39Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T18:22:46Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T18:24:00Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T19:10:05Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T19:11:20Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T19:14:37Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T19:18:48Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T19:19:57Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T19:44:53Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T19:46:20Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T19:50:53Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T19:54:25Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T19:56:38Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T19:57:19Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T19:59:34Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T19:59:45Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T20:23:28Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T20:38:32Z] Phase ?: Phase 4 build complete. 71/71 tests passing. 8 gaps in ISSUES.md, 1 resolved (ID-5). (complete_with_gaps)
- [2026-04-25T20:47:30Z] Phase ?: Planning phase 5 (Snapshot Providers): CONTEXT-5 captured, dispatching researcher (planning)

## 2026-04-26 — Phase 5 planned (`/shipyard:plan 5`)

- Resumed mid-planning. STATE.json was stale ("dispatching researcher") but research, 5 plans, and CRITIQUE.md were already written 2026-04-25 — verdict: **CAUTION** (6 items, no critical issues).
- User chose **targeted revision cycle** over full re-plan or accept-and-build.
- **Architect revision** (1 cycle, 11 tool uses, surgical edits to 5 plan files):
  - PLAN-1.1: object-initializer for `SnapshotRequest` (no positional ctor exists); builder note added.
  - PLAN-1.2: `files_touched` now includes 5 test files that compile-break on `Actions: IReadOnlyList<string> → IReadOnlyList<ActionEntry>` migration; `StartupValidation.ValidateActions` iteration update made explicit (`entry.Plugin`); `dependencies: [1.1]` frontmatter + header note locking Wave 1 sequential execution; folded fixture updates into Task 2 (no 4th task).
  - PLAN-2.1: committed to wrapper-record DI strategy (`BlueIrisSnapshotUrlTemplate(BlueIrisUrlTemplate Template)`) over keyed services — concrete registrar code snippet provided; constructor injection updated; verification grep updated.
  - PLAN-2.2: removed `Jenkinsfile` from `files_touched` and Task 3 Step 4 — `run-tests.sh` auto-discovers via `find tests/*.Tests/*.Tests.csproj` glob; `git diff -- Jenkinsfile` empty added as acceptance criterion.
  - PLAN-3.1: `tests/FrigateRelay.Host.Tests/FrigateRelay.Host.Tests.csproj` added to `files_touched` with exact `<ProjectReference>` entries for BlueIris + FrigateSnapshot plugins; registrar invocation pattern clarified (`registrars.Add` + `PluginRegistrarRunner.RunAll`).
- **Verifier re-run**: **READY**. All 6 cautions fixed and verified (CRITIQUE.md overwritten). No new issues introduced by the revisions.
- Final wave/test layout: 3 waves, 5 plans, 29 planned tests (≥10 gate, +190% cushion).
- Planning interruption observation: STATE.json was stale by 2 ROADMAP-pipeline steps (verifier had run but state-write didn't fire). Worth investigating shipyard hook reliability — but not a Phase 5 concern.
- Next: `/shipyard:build 5`.

- [2026-04-26T00:10:00Z] Phase 5: Build started (agent mode, sequential Wave 1 then parallel Wave 2 then Wave 3) (building)
- [2026-04-26T14:10:28Z] Session ended during build (may need /shipyard:resume)
- [2026-04-26T14:47:18Z] Session ended during build (may need /shipyard:resume)
- [2026-04-26T14:47:55Z] Session ended during build (may need /shipyard:resume)
- [2026-04-26T14:48:48Z] Session ended during build (may need /shipyard:resume)

## 2026-04-26 — Phase 5 built (`/shipyard:build 5`)

- **12 commits** across 3 waves: PLAN-1.1 (3 commits, 7 SnapshotResolver tests), PLAN-1.2 (2 commits, 9 ActionEntry/StartupValidationSnapshot tests + 5 fixture migrations), PLAN-2.1 (3 commits, 4 BlueIrisSnapshotProvider tests), PLAN-2.2 (2 commits, 6 FrigateSnapshotProvider tests + new plugin assembly), PLAN-3.1 (2 commits, 3 SnapshotResolutionEndToEnd tests + HostBootstrap wiring + ProjectReferences). Cleanup commit `26e8fc2` resolves the REVIEW-3.1 Critical.
- **100/100 tests pass** (99 unit + 1 integration). Build clean, 0 warnings.
- **Builder truncation pattern continued** — every wave's builder agent truncated at ~30-40 tool uses. Orchestrator finished each plan inline. Per-task atomic commits made resumption cheap. Reviewer agent ALSO doesn't self-persist REVIEW-*.md files (Phase 1 lesson still unresolved); orchestrator transcribed inline reports for REVIEW-1.1/1.2 only — REVIEW-2.1/2.2/3.1 captured in this entry.
- **Critical fix (inline)**: REVIEW-3.1 found `StartupValidation.ValidateSnapshotProviders` was dead code — defined in PLAN-1.2 but never called from `HostBootstrap.ValidateStartup`. Fixed in commit `26e8fc2`.
- **Real Phase-4 → Phase-5 regression discovered**: `IConfiguration.Bind` for `IReadOnlyList<ActionEntry>` does NOT fire `[JsonConverter]`. The plan's promise of back-compat for legacy `appsettings.json` `"Actions": ["BlueIris"]` shape was incorrect. Surfaced when integration test failed with "found 0" trigger fires. Fixture migrated to object form. Tracked as **ID-12**. Operator upgrade implication: existing Phase-4 deployments with string-array shape silently lose action firing.
- **Architectural cascade**: `ActionEntry`, `ActionEntryJsonConverter`, `SnapshotResolverOptions` raised from `internal` to `public` (CS0053 from public `SubscriptionOptions`/`HostSubscriptionsOptions`). Tracked as **ID-10** for the future ID-2 internalization sweep.
- **Phase verification**: COMPLETE_WITH_GAPS. All 7 ROADMAP deliverables met; D1–D4 honored; CLAUDE.md invariant greps clean.
- **Security audit**: PASS (Low). 0 critical, 0 important, 2 advisory (`EventId` not URL-encoded in FrigateSnapshot path — benign for UUIDs; `BaseUrl` no URI format validation — fail-fast violation).
- **Simplifier**: 5 actionable findings, all trivial (4 are reviewer Important items: dead `IOptions<BlueIrisOptions>` injection, hard-coded port 19999, duplicate `EventId(3)`, 62 LoC dead test scaffolding). Recommended as one `chore(phase-5): cleanup` commit before Phase 6.
- **Documenter**: 5 CLAUDE.md gap edits proposed; deferred to Phase 11 docs sprint per Phase-3 user decision.
- **Reviewer verdicts**: 1.1 PASS, 1.2 PASS, 2.1 REQUEST_CHANGES (3 important / 0 critical), 2.2 APPROVE (2 important / 0 critical), 3.1 REQUEST_CHANGES → resolved (1 critical fixed inline).
- **Lessons-learned drafts** (for `/shipyard:ship`):
  - **`[JsonConverter]` ≠ Configuration binding**: `Microsoft.Extensions.Configuration.Binder` does not call System.Text.Json. Plans promising dual-form binding via `JsonConverter` are wrong. Use `TypeConverter`, `IConfigureOptions`, or a custom binder for scalar-or-object polymorphism in `IConfiguration`.
  - **Accessibility cascade is a real planning hazard**: CS0053 forces consumers to track outer-type modifiers. Architect should grep `public.*<NewType>` consumers before locking the new type's accessibility.
  - **Reviewer agent doesn't self-persist** (since Phase 1). Orchestrator must transcribe inline reports — or the REVIEW files just don't land on disk.
  - **Builder truncation is a steady-state cost** (~30-40 tool uses). Per-task atomic commits + clear failure protocols make every truncation cheap to recover from.
- Checkpoint tags: `pre-build-phase-5`, `post-build-phase-5`.

## 2026-04-26 — Phase 5 cleanup pass (`chore(phase-5)`)

- Single commit `5f90d2c` applies all 5 simplifier findings + 2 auditor advisory items + the PLAN-2.1 reviewer log typo. Net **-60 LoC** across 5 files (16 insertions / 76 deletions). 100/100 tests still passing.
- **Source fixes**: dead `IOptions<BlueIrisOptions>` ctor param dropped; `bluiris_->blueiris_` log typo; `EventId` URL-encoded in `FrigateSnapshotProvider` (auditor A1); `ProviderName` literal → `Name` property; distinct `EventId(4)` for `_snapshotFailedMessage`; `[Url]` DataAnnotation on `FrigateSnapshotOptions.BaseUrl` (auditor A2).
- **Test fixes**: hard-coded port 19999 → `TcpListener` ephemeral port; 62 LoC of dead `BuildProvider`/`OptionsMutator`/`ApplyOverrides` scaffolding deleted from FrigateSnapshotProviderTests.
- **Deferred** (out of scope): PLAN-2.1 reviewer Important #3 (`PluginRegistrar` raw `IConfiguration` read — track for ID-2 sweep), PLAN-2.2 reviewer Important #2 (`IncludeBoundingBox` OR-merge — needs a dedicated test, not a fix).

## 2026-04-26 — Phase 6 planned (`/shipyard:plan 6`)

- **Discussion capture (CONTEXT-6.md)**: 4 user decisions (D1–D4) + 6 inherited (D5–D10). Configurable `MessageTemplate` with default; text-only when no snapshot; global `Priority` default; new `MqttToBothActionsTests` class.
- **Researcher** (no truncation, 17 tool uses): produced `RESEARCH.md` (~420 lines) with Pushover API cookbook, `MultipartFormDataContent` recipe, Polly retry semantics for non-idempotent endpoints. Recommended **template extraction** to Abstractions (`EventTokenTemplate`) since Rule of Three is crossed (BlueIris trigger + BlueIris snapshot + Pushover message). Recommended **Option D for snapshot resolver call site**: `SnapshotContext` readonly struct passed via extended `IActionPlugin.ExecuteAsync` signature.
- **Architect** (clean, 7 tool uses): locked **ARCH-D1 = (b)** `Resolve(EventContext, bool urlEncode = true)` — single method with default. **ARCH-D2 = Option D** confirmed. **ARCH-D3** — `Title` defaults to `null` (Pushover renders user's app name). 4 plans / 3 waves / 24 new tests (8 + 5 + 10 + 1 integration).
- **Verifier (compliance)**: **READY**. All 7 ROADMAP deliverables owned. All 10 CONTEXT decisions honored. Test count exceeds gate by 200%.
- **Verifier (critique)**: **READY** with 2 cautions:
  1. **PLAN-1.2 blast radius audit**: FrigateSnapshot and FrigateMqtt test projects need spot-check for direct `IActionPlugin.ExecuteAsync` call sites (verifier confirmed none exist before truncating; cross-checked clean).
  2. **PLAN-3.1 secret-scan tripwire**: `tests/` is NOT excluded in `.github/scripts/secret-scan.sh`. Builder must use short fake credentials (<20 chars) to pass the regex.
  3. **PLAN-2.1/3.1 BaseAddress seam** (architect-flagged): integration test needs `PushoverOptions.BaseAddress` override for WireMock redirect; should land in PLAN-2.1 task 2 if discovered, otherwise PLAN-3.1 inline.
- **Wave shape**:
  - Wave 1 (parallel-safe): PLAN-1.1 `EventTokenTemplate` extraction + BlueIris migration; PLAN-1.2 `SnapshotContext` plumbing through `IActionPlugin.ExecuteAsync` + DispatchItem.
  - Wave 2: PLAN-2.1 — new `FrigateRelay.Plugins.Pushover` project with multipart POST, snapshot attachment, 10 unit tests.
  - Wave 3: PLAN-3.1 — HostBootstrap conditional registrar + `MqttToBothActionsTests` integration test.
- Zero revision cycles needed. Checkpoint tag: `post-plan-phase-6`.
- [2026-04-26T16:14:28Z] Session ended during build (may need /shipyard:resume)
- [2026-04-26T16:15:38Z] Session ended during build (may need /shipyard:resume)
- [2026-04-26T16:15:41Z] Session ended during build (may need /shipyard:resume)
- [2026-04-26T16:15:55Z] Session ended during build (may need /shipyard:resume)

## 2026-04-26 — Phase 6 built (`/shipyard:build 6`)

- **5 commits** across 3 waves: Wave 1 (combined commit `f859251` after 2 builder truncations — `EventTokenTemplate` extraction + `SnapshotContext` struct + `IActionPlugin.ExecuteAsync` signature change + dispatcher plumbing + 13 tests); Wave 2 (`607687b` Pushover scaffold + 4 startup-validation tests, `dff4828` `PushoverActionPlugin` multipart POST + 6 behavioral tests); Wave 3 (`dd160ef` HostBootstrap registrar + QueueCapacity merge, `b2eef39` `MqttToBothActionsTests` integration + Pushover BaseAddress seam).
- **124/124 tests** (122 unit + 2 integration). Build clean, 0 warnings.
- **6 builder truncations** at the steady ~30-40 tool-use boundary. Orchestrator finished each plan inline: 6 stub plugin signature updates (CS0535 cascade), DispatchItem/IActionDispatcher/EventPump plumbing, full `PushoverActionPlugin.ExecuteAsync` implementation, multipart-quoting + WireMock-binary fixture fixes, BaseAddress seam wiring.
- **Critical findings**: 0. Reviewer files weren't written for Wave 2/3 (chronic agent issue from Phase 5).
- **Phase verification**: COMPLETE. All 7 ROADMAP deliverables met. All 10 CONTEXT decisions + 3 ARCH decisions honored.
- **Security audit**: PASS (Low). 0 critical/important. 1 advisory: Polly `AddResilienceHandler` doesn't have an explicit `ShouldHandle` 4xx-skip predicate — relies on `Microsoft.Extensions.Http.Resilience` defaults; recommend adding for documentation value.
- **Simplifier**: 2 High (CapturingLogger<T> 4-copy → shared TestHelpers project; HostBootstrap per-plugin QueueCapacity if-let → loop), 2 Medium (BlueIrisUrlTemplate intentional duplication — needs doc comment; StubResolver Rule of Two), 2 Low (BuildFastRetryProvider dead code; HostBootstrap registrar conditionals approaching Rule of Three at Phase 7).
- **Documenter**: 2 actionable for CLAUDE.md (IActionPlugin.ExecuteAsync 3-param + accept-and-ignore convention; ID-12 object-form Actions warning); 3 deferred to Phase 11.
- **Lessons-learned drafts**:
  - **Interface signature changes cascade through stub/test plugins**. CS0535 errors are mechanical but tool-budget-eating. Future `IActionPlugin` changes should be accompanied by an automated stub-update grep.
  - **`MultipartFormDataContent.name=` is unquoted in .NET 10 default** (was quoted in older versions). Tests asserting `name="token"` form fail; use `name=token`.
  - **WireMock returns null `Body` (string) for binary multipart**. Use `BodyAsBytes` + UTF-8 decode.
  - **Pushover returns HTTP 200 with `{"status":0}` for app-level rejections** (e.g. bad token). Plugin must parse body, not just trust HTTP status.
  - **`HttpClient.BaseAddress` is required** when plugin uses relative URIs. Both production registrar AND test helpers must set it from options.
- Checkpoint tags: `pre-build-phase-6`, `post-build-phase-6`.

## 2026-04-26 — Phase 7 planned (`/shipyard:plan 7`)

- **Discussion capture (CONTEXT-7.md)** — 5 user-locked decisions D1–D5 + 8 architect lock-ins D6–D13:
  - **D1** — Extend `IValidationPlugin.ValidateAsync` to take `SnapshotContext` (mirrors Phase 6 ARCH-D2 for `IActionPlugin.ExecuteAsync`). One snapshot resolved per dispatch, shared between validator chain and action.
  - **D2** — **Top-level `Validators` dict + `ActionEntry.Validators` keyed references**. Each named instance carries `Type` discriminator + per-instance options (e.g. `"strict-person": { "Type": "CodeProjectAi", "MinConfidence": 0.7, "AllowedLabels": ["person"], ... }`). Heavier schema than originally proposed; anticipates Phase 8 Profiles cleanly. Per-label confidence dict therefore unnecessary — operators express per-label tuning by creating multiple named instances.
  - **D3** (resolved by D2) — Per-instance scalar `MinConfidence` + `AllowedLabels` only. No nested per-label confidence dict.
  - **D4** — **Configurable per-instance `OnError: { FailClosed, FailOpen }`**, default FailClosed. **No Polly retry handler** on validator HttpClient (asymmetric with BlueIris/Pushover plugins which DO retry — explicit comment + CLAUDE.md update).
  - **D5** — Defer bbox `ZoneOfInterest` to a later phase. v1 = `MinConfidence` + `AllowedLabels` only (Phase 3 already does subscription-level zone matching).
- **Researcher** truncated at 22 tool uses without writing — orchestrator finished inline using Microsoft Learn MCP for keyed-services + named-options patterns. CodeProject.AI docs fetch hit a cert verification error; legacy reference (`Source/FrigateMQTTMainLogic/Pushover.cs:103-141`) is the canonical API-shape source for v1 (`POST /v1/vision/detection`, multipart `image`, JSON `predictions: [{label, confidence, x_min, ...}]`). RESEARCH.md resolved all 6 CONTEXT-7 open questions:
  - .NET 10 keyed-validator-instance pattern: `AddOptions<T>(name).Bind(...)` + `AddKeyedSingleton<IValidationPlugin>(name, factory)` with `IOptionsMonitor<T>.Get(name)` retrieval. Each plugin registrar enumerates `Configuration.GetSection("Validators").GetChildren()` and filters by `Type == "{ownType}"`.
  - **`SnapshotContext` does NOT cache `ResolveAsync`** — calling twice hits the resolver twice. Architect lock-in: add a second `SnapshotContext(SnapshotResult? preResolved)` constructor + `_hasPreResolved` flag so the dispatcher can resolve once when validators are present and pass the cached result through the chain. ~10 LoC delta. Optional for BlueIris-only subscriptions (no fetch when no validator + no snapshot-consuming action).
  - **Zero existing `IValidationPlugin` test stubs** — only the empty-list call sites (8 in `ChannelActionDispatcherTests`, 4 in `EventPumpDispatchTests`, 1 in `EventPumpTests`) — none invoke `ValidateAsync`, so the new `SnapshotContext` parameter has zero migration surface in existing tests.
  - **`ActionEntryJsonConverter` extension is straightforward** — add `Validators` field to public record + private DTO + Read projection + Write conditional emit. ID-12 (`IConfiguration.Bind` ≠ `[JsonConverter]`) does NOT need a fix in Phase 7: the binder handles `IReadOnlyList<string>?` via primary-constructor positional binding. Deferred fix remains a Phase 11 OSS-polish item.
- **Architect** (opus, 7 tool uses, no truncation) wrote 4 plan files + a phase-level VERIFICATION.md draft:
  - **Wave 1 (parallel-safe)**: `PLAN-1.1` `IValidationPlugin` signature + `SnapshotContext.PreResolved` ctor + `ChannelActionDispatcher.ConsumeAsync` validator-chain wiring (3 tasks, TDD); `PLAN-1.2` `ActionEntry.Validators` field + `ActionEntryJsonConverter` extension (2 tasks).
  - **Wave 2**: `PLAN-2.1` new `FrigateRelay.Plugins.CodeProjectAi/` project (csproj + ID-3 explicit TargetFramework + InternalsVisibleTo MSBuild item) + `CodeProjectAiValidator` (multipart POST, decision rule, OnError FailClosed/FailOpen catch ordering: `OperationCanceledException when ct.IsCancellationRequested` first, then `TaskCanceledException` timeout, then `HttpRequestException` unavailable) + `PluginRegistrar` enumerating top-level `Validators` and registering keyed instances (no `AddResilienceHandler` per D4) + 8 unit tests (3 tasks, TDD).
  - **Wave 3**: `PLAN-3.1` `EventPump` validator-key resolution via `IServiceProvider.GetRequiredKeyedService<IValidationPlugin>(key)` + `StartupValidation.ValidateValidators` wired into `HostBootstrap.ValidateStartup` (loud Phase-5 dead-code regression mode warning + explicit `git grep` acceptance criterion) + 2 integration tests `Validator_ShortCircuits_OnlyAttachedAction` + `Validator_Pass_BothActionsFire` (3 tasks).
- **Verifier (spec compliance)**: **READY**. All 13 CONTEXT-7 decisions D1–D13 implemented across the 4 plans. CI auto-discovery (`run-tests.sh` glob) handles the new `FrigateRelay.Plugins.CodeProjectAi.Tests` project with zero workflow edits. One minor frontmatter gap: `FrigateRelay.Host.csproj` not in PLAN-3.1 `files_touched` despite Task 3 prose requiring the `<ProjectReference>` edit — fixed inline.
- **Verifier (feasibility critique)**: **CAUTION → READY (post-orchestrator fixes)**. Three concrete plan-text errors found:
  1. PLAN-1.1 dispatcher pseudo-code used `_resolver` but actual field is `_snapshotResolver` (the SnapshotContext.cs field IS `_resolver` — different file; fixed with explanatory comment).
  2. PLAN-3.1 referenced `src/FrigateRelay.Host/Configuration/StartupValidation.cs` but actual path is `src/FrigateRelay.Host/StartupValidation.cs` (no `Configuration/` subdir for the source file; the **test** file IS in `tests/FrigateRelay.Host.Tests/Configuration/StartupValidationTests.cs` which the plan correctly references).
  3. PLAN-3.1 referenced precedent file `MqttToActionsTests.cs` but actual is `MqttToBothActionsTests.cs`.
  All 3 fixed inline (no architect revision cycle dispatched — surgical string substitutions, not design changes). CRITIQUE.md updated with a Resolution log preserving the original CAUTION findings for audit-trail traceability.
- **Test-count target**: 19 tests (12 unit in `CodeProjectAiValidatorTests` + 3 startup-validation + 2 in `ChannelActionDispatcherTests` additive + 2 in `SnapshotContextTests` additive + 2 integration). ROADMAP gate is 10. +90% cushion.
- **Plan-level open questions resolved by architect** (per RESEARCH §7): registrar enumerates `IConfigurationSection.GetChildren()` directly (no central `ValidatorInstanceOptions` strongly-typed bind); response DTOs internal to plugin; test class names locked; validator chain runs ABOVE Polly `ResiliencePipeline.ExecuteAsync` so it bypasses retry; `IServiceProviderIsKeyedService` available in .NET 10 for clean fail-fast checks.
- **Cross-cutting**: ID-12 (`IConfiguration.Bind` ≠ `[JsonConverter]`) explicitly out of scope in PLAN-1.2 + PLAN-3.1; D4 asymmetric-retry stance loud-commented in PLAN-2.1 Task 3 registrar code AND noted for CLAUDE.md update in PLAN-3.1 Task 3; Phase 5 review-3.1 dead-code lesson explicit in PLAN-3.1 Task 2 Context block.
- Checkpoint tag: `post-plan-phase-7`.
- Next: `/shipyard:build 7`.
- [2026-04-26T20:56:56Z] Session ended during build (may need /shipyard:resume)

## 2026-04-26 — Phase 7 built (`/shipyard:build 7`)

- **10 atomic per-task commits** between `pre-build-phase-7` and `acc3de4` + 1 phase-close artifact commit (`5d578da`):
  - Wave 1 (PLAN-1.1 + PLAN-1.2 in parallel):
    - `12ee767` — IValidationPlugin gains SnapshotContext param.
    - `29adaab` — SnapshotContext.PreResolved ctor + 2 tests.
    - `c4ba938` — dispatcher validator chain + snapshot share + 2 tests.
    - `b021f3c` — ActionEntry gains optional Validators field.
    - `f6996d2` — ActionEntryJsonConverter handles Validators + 2 tests.
  - Wave 2 (PLAN-2.1):
    - `072961c` — CodeProjectAi plugin project + validator + registrar (tasks 1+3 bundled per orchestrator-finishes pattern).
    - `be28f4c` — CodeProjectAiValidator 8 unit tests (task 2 TDD).
  - Wave 3 (PLAN-3.1):
    - `8f55f8a` — EventPump resolves keyed validators per ActionEntry.
    - `da120c1` — StartupValidation.ValidateValidators + HostBootstrap wiring + CodeProjectAi registrar.
    - `acc3de4` — MqttToValidatorTests integration tests (Validator_ShortCircuits_OnlyAttachedAction + Validator_Pass_BothActionsFire).
- **143/143 tests pass** (Phase 6 baseline 124, +19 new). Build clean (0 warnings). Integration tests pass with Docker-backed Mosquitto.
- **Both subagent builders failed on Bash permission** (Wave 1 PLAN-1.1 and PLAN-1.2 builders). Orchestrator finished both inline using the same pattern as Phases 1/3/5/6 truncation recovery. PLAN-1.2 builder did manage Read/Edit before failing, leaving 67 LoC of correct edits on disk that the orchestrator validated and committed atomically.
- **Phase verification**: COMPLETE. All ROADMAP-listed Phase 7 deliverables met or exceeded. All 13 CONTEXT-7 decisions D1-D13 honored. All architecture invariants hold (no Result/Wait, no ServicePointManager, no excluded libs, no hardcoded IPs in src/ or tests/).
- **Security audit**: PASS / Low risk. 0 critical/high/medium. 3 informational notes (NOTE-1 scoped TLS bypass matching BlueIris/Pushover precedent, NOTE-2 fail-fast config validation, NOTE-3 SSRF surface unchanged from existing plugins). All matching Phase 4-6 patterns; no remediation required.
- **Simplifier**: 3 Low findings — all deferred:
  - L1 `CapturingLoggerProvider` in MqttToValidatorTests.cs (Rule of Two — defer until third integration test needs cross-category log capture).
  - L2 OnError/timeout/unavailable catch-block ordering pattern across BlueIris/Pushover/CodeProjectAi (Rule of Three technically met but bodies differ; document the pattern in CLAUDE.md instead of extracting code).
  - L3 EventPump validator-resolution `keys.Select(...).ToArray()` allocation per dispatch (defer to Phase 9 perf pass; modern JIT may elide).
- **Documenter**: 4 actionable CLAUDE.md gaps + 1 Phase 11 plugin-author-guide note — all 5 deferred to Phase 11 OSS-polish docs sprint per the established Phase 5+6 pattern. Captured in DOCUMENTATION-7.md for ship-time pickup:
  - CLAUDE-1 (HIGH) validator/action retry asymmetry doc.
  - CLAUDE-2 (HIGH) keyed-validator-instance pattern doc.
  - CLAUDE-3 (HIGH) `partial class` requirement for `[LoggerMessage]` source-gen.
  - CLAUDE-4 (MEDIUM) SnapshotContext.PreResolved sharing path invariant.
  - CLAUDE-5 (LOW) plugin-author-guide IValidationPlugin samples for Phase 11.
- **Real Phase 7 build issues caught and fixed inline**:
  - CS1734 on `<paramref name="snapshot"/>` in interface-level XML doc — `paramref` requires parameter scope; fixed with `<c>snapshot</c>` text reference.
  - CS0260 on `partial class Log` nested in non-partial outer class — added `partial` to outer `CodeProjectAiValidator`.
  - CA5359 on always-true cert callback — scoped #pragma matching BlueIris precedent.
  - CA1861 on `new[] { ... }` literal arrays in test methods — hoisted to `static readonly string[]`.
  - IDE0005 on unused `Microsoft.Extensions.DependencyInjection` usings — removed.
  - CS8417 on `await using var app` for `IHost` — IHost is IDisposable not IAsyncDisposable; switched to `using var` matching Phase 6 precedent.
- **Lessons-learned drafts** (for `/shipyard:ship`):
  - **Subagent Bash-permission denial is the steady-state pattern in this session.** Both Wave 1 builders failed identically. The orchestrator-finishes-inline pattern handles it without quality loss but loses ~30s per failed dispatch. Either elevate subagent permissions OR restructure agents to Read/Edit-only with orchestrator running all bash steps.
  - **`[LoggerMessage]` source-gen requires `partial` up the nesting chain.** The CS0260 trap will catch any future plugin author who picks the modern attribute style over `LoggerMessage.Define<...>` static fields. Worth a CLAUDE.md `## Conventions` note (Phase 11 docs sprint will add).
  - **`SnapshotContext` was struct-based without resolver caching.** The new `PreResolved` ctor (10 LoC) is the simplest way to share one fetch across validator + action without restructuring the type to a class.
  - **Phase 5 review-3.1 dead-code regression mode is real and recurring.** Loud inline comment + explicit `git grep` acceptance criterion at the wire-up site is the lightweight countermeasure that keeps catching it.
  - **`run-tests.sh` auto-discovery via `find tests -maxdepth 2`** absorbed the new CodeProjectAi test project with zero workflow edits. Phase 3's extraction (initially flagged as Rule-of-Two violation by Phase 2 simplifier) keeps paying off.
  - **`IHost` is `IDisposable` not `IAsyncDisposable`** — surprising for modern hosting; `using var app` is the correct pattern.
- Checkpoint tags: `pre-build-phase-7`, `post-build-phase-7`.
- [2026-04-27T13:38:18Z] Session ended during build (may need /shipyard:resume)
- [2026-04-27T13:52:29Z] Session ended during build (may need /shipyard:resume)
- [2026-04-27T16:17:35Z] Session ended during build (may need /shipyard:resume)
- [2026-04-27T16:20:53Z] Session ended during build (may need /shipyard:resume)
- [2026-04-27T16:21:51Z] Session ended during build (may need /shipyard:resume)

## 2026-04-27 — Phase 8 built (`/shipyard:build 8`)

- **8 commits** across 3 waves + 3 orchestrator-driven cleanup commits:
  - Wave 1 (sequential, CS0053-safe order): `b5b87eb` (flip 7 host types internal + DynamicProxyGenAssembly2 IVT), `e622a39` (internalize ActionEntryJsonConverter + ProfileOptions), `d2bc12a` (Profile/Profiles properties), `a880bac` (ProfileOptions XML docs + SUMMARY-1.1), `4357fd6` (ID-12 red TDD), `6264154` (ActionEntryTypeConverter green, internalize ActionEntry), `544516e` (close ID-2/10/12 in ISSUES + REVIEW-1.2 + SUMMARY-1.2).
  - Wave 2: `4e1c683` / `c9a0b4a` / `200182c` (ProfileResolver + ValidateAll collect-all retrofit + 10 ProfileResolutionTests), `e340770` (orchestrator fix — restore IOptions<SnapshotResolverOptions> threading flagged by REVIEW-2.1).
  - Wave 3: `c945c40` (appsettings.Example.json + ConfigSizeParityTest + sanitized legacy.conf + csproj wiring), `85dac72` (CLAUDE.md updates per PLAN-3.1 Task 3 + REVIEW-3.1 — Task 3 originally omitted, fixed inline by orchestrator).
- **69/69 tests** (was 55 pre-Phase-8). Build clean, 0 warnings.
- **2 builder truncations** at the steady ~30-40 tool-use boundary; both required `SendMessage` resumption to write SUMMARY files. PLAN-1.1 builder also hit a `.shipyard/` write-block — orchestrator wrote SUMMARY-1.1 from dumped content. Pattern: builders work the code cleanly but stall before the final artifact write.
- **Critical findings (per phase reviewer):** 1. REVIEW-3.1 caught PLAN-3.1 Task 3 (CLAUDE.md updates) entirely omitted by builder; orchestrator applied the 3 required edits inline (replace stale ID-12 paragraph, add D7 collect-all bullet, add NSubstitute DynamicProxyGenAssembly2 bullet) and committed `85dac72`.
- **Phase verification:** COMPLETE. All 3 ROADMAP success criteria pass (ConfigSizeParityTest 56.7%, 10 ProfileResolutionTests, undefined-profile fail-fast wording matches). All 9 CONTEXT-8 decisions D1–D9 honored. All 3 issues closed correctly (ID-2 b5b87eb, ID-10 b5b87eb+e622a39+6264154, ID-12 6264154).
- **Security audit:** PASS (Low). 0 critical / 0 important / 3 advisory: N1 newline-sanitization in error messages (CWE-117 log spoofing, attacker already owns config — negligible), N2 empty/whitespace plugin name accepted by ActionEntryTypeConverter (clean fail-fast in ValidateActions), N3 secret-scan.sh covers RFC 1918 class C only. All 3 deferred and tracked as ID-13/14/15.
- **Simplifier:** 2 High (dead `ValidationPlugin` helper in ProfileResolutionTests; orphaned `HostSubscriptionsOptions.Snapshots` property — bound but never read), 2 Medium (3x ISnapshotProvider stub factory triplication; `ValidateValidators` unnecessary materialization guard), 3 Low. **Both High fixed inline** in commit (next): drop dead helper + delete orphaned property. Medium and Low deferred to Phase 9 prep.
- **Documenter:** ACCEPTABLE — no public-API leakage (visibility sweep makes the entire host config / dispatch / matching surface internal). All 3 new internal types carry XML docs. CLAUDE.md gained 2 conventions bullets (collect-all + DynamicProxyGenAssembly2). ID-9 partial-activation recommended (operator `_comment` keys in appsettings.Example.json) but **deferred to Phase 11/12** docs pass per user direction.
- **Convention drift noted:** PLAN-2.1 builder used `feat(host):`/`test(host):` commit prefixes instead of `shipyard(phase-8):`. Flagged in REVIEW-2.1 + SIMPLIFICATION-8 Low; left as-is in history.
- **Lessons-learned drafts:**
  - **Builders stall before final SUMMARY writes.** PLAN-1.1 + 2.1 + 3.1 all reached green-state code but stopped before writing `.shipyard/phases/N/results/SUMMARY-W.P.md`. Pattern is a tool-budget cap right at the deliverable. Mitigation: have orchestrator write SUMMARY from agent's dumped content, or pre-write a stub the builder updates.
  - **`.shipyard/` writes are sometimes blocked for subagents.** Permission scope is unclear — SendMessage resumption fixed it for some files (REVIEW-2.1) but not others (SUMMARY-1.1). Reliable path: builders dump content; orchestrator writes the file.
  - **`HostSubscriptionsOptions.Snapshots` was a phantom property.** Both the `Snapshots` config section AND `IOptions<SnapshotResolverOptions>` got bound into the host; no production code read the `HostSubscriptionsOptions.Snapshots` member, only the DI-registered `IOptions<>`. Removing the property eliminated the latent ambiguity. Audit checklist for future option records: every `init` property must have at least one reader.
  - **Visibility sweep is best done all-at-once.** Phase 5 introduced ID-2 + ID-10 because internalizing `IActionDispatcher` alone caused CS0053 cascade through `SubscriptionOptions.Actions`, forcing the cascade types to be raised back to public. Phase 8 PLAN-1.1 sweeping seven types in one atomic pass made the change feasible, and PLAN-1.2 internalized `ActionEntry` afterwards as a clean follow-up.
  - **`DynamicProxyGenAssembly2` is required for NSubstitute on internalized types.** Adding only the test-assembly `[InternalsVisibleTo]` is insufficient — Castle DynamicProxy itself needs internals access. NS2003 build errors on internalized types are the symptom. Documented in CLAUDE.md.
  - **MSTest v4.2.1 uses `--filter`, not `--filter-query`.** Confirms ID-4 staleness; CLAUDE.md flag references should be updated when ID-4 is closed.


## 2026-04-27 — Phase 9 planned (`/shipyard:plan 9`)

- **Discussion capture (CONTEXT-9.md):** 9 user-locked decisions:
  - **D1** ActivityContext struct (not Activity?) on DispatchItem for cross-channel propagation.
  - **D2** OTel registers but no exporter when OTEL_EXPORTER_OTLP_ENDPOINT is unset (silent no-op; tests use in-memory exporter).
  - **D3** counter tags: subscription/action/validator/camera/label per counter; errors.unhandled untagged.
  - **D4** ID-6 (OperationCanceledException → ActivityStatusCode.Error during shutdown) bundled into Phase 9 dispatcher instrumentation.
  - **D5** test split: unit in Host.Tests/Observability/, integration TraceSpans_CoverFullPipeline in IntegrationTests/Observability/ (Mosquitto + WireMock + in-memory exporter).
  - **D6** keep hand-rolled Action<ILogger,...> delegates; do NOT migrate to [LoggerMessage].
  - **D7** Serilog Seq sink: included now, conditionally registered when Serilog:Seq:ServerUrl is set.
  - **D8** span attribute table seeded; architect finalized in PLAN-2.1.
  - **D9** errors.unhandled increment site is single top-level catch in EventPump + ChannelActionDispatcher; per-plugin failures already counted by actions.failed.
- **Researcher** (sonnet, 25 tool uses then resumed) wrote 717-line RESEARCH.md covering existing instrumentation surface, pipeline shape, OTel + Serilog package landscape (versions confirmed 2026-04-27), counter cardinality, MeterListener test pattern, in-memory exporter wiring. Note: researcher claimed `DispatcherDiagnostics.cs` was missing — verified FALSE; file exists. Architect noted the discrepancy and trusted only the API-shape findings.
- **Architect** (opus, 12 tool uses, no truncation) wrote 4 plans + pre-build VERIFICATION.md:
  - **Wave 1 (foundation):** PLAN-1.1 — csproj package refs (8 OTel + 5 Serilog), DispatcherDiagnostics counter surface extension (8 D3 counters with tag dimensions), DispatchItem.Activity? → ActivityContext flip per D1. 3 tasks, low risk, no TDD.
  - **Wave 2 (parallel-safe instrumentation + wiring):** PLAN-2.1 — 5 spans with D8 attribute table, counter increments with TagList, ID-6 fix at ChannelActionDispatcher.cs:~238 (D4), errors.unhandled untagged single site (D9). PLAN-2.2 — UseSerilog Worker SDK, conditional Seq (D7), AddOpenTelemetry with conditional OTLP (D2), appsettings.json Serilog/Otel sections, StartupValidation.ValidateObservability fail-fast on bad URI. Disjoint file sets.
  - **Wave 3 (TDD):** PLAN-3.1 — unit span/counter tests in Host.Tests/Observability/ + integration TraceSpans_CoverFullPipeline + counter-set integration in IntegrationTests/Observability/. Target 69→≥84 Host.Tests, +2 integration.
- **Architect did NOT rename** DispatcherDiagnostics → FrigateRelayDiagnostics (RESEARCH.md §7 suggested) — rejected as churn-only since the existing class name is wired throughout the dispatcher.
- **Verifier (Step 6, plan quality):** PASS. All 5 ROADMAP deliverables, all 9 D-decisions, all 4 plans (≤3 tasks each, valid frontmatter, disjoint Wave 2 files, runnable acceptance commands). Verifier wrote `.shipyard/phases/9/VERIFICATION_PLAN_QUALITY.md` (separate file — append-target was the architect's VERIFICATION.md).
- **Critique (Step 6a, feasibility stress test):** READY. File-existence claims spot-checked: `DispatcherDiagnostics.cs` exists (PLAN-1.1 correct, RESEARCH.md was wrong), `DispatchItem.Activity?` confirmed at line 29, ID-6 line range plausible, all NEW test directories correctly marked. 2 low-risk pre-build flags: confirm DispatchItem carries Subscription field, confirm ValidateAll exists in StartupValidation.cs. Both will surface immediately at build time.
- **Test count baseline:** 69 (post-Phase-8). Phase 9 net new: ≥17 (6 span shape + 9 counter MeterListener + 2 integration).
- **Lessons-learned drafts:**
  - **Researcher hit tool budget mid-work without writing.** First attempt stopped at "Let me search for files by listing the directory structure properly." Pattern: 25 tool uses on doc + code lookup, but no synthesis budget left for the deliverable. Future researchers should write incrementally; orchestrator can resume but the second pass loses some accumulated context.
  - **RESEARCH.md inaccuracy: claimed DispatcherDiagnostics.cs was missing.** Wasn't, build was green. Architect verified before relying on the claim. Pattern: if a researcher claim contradicts your green-build observation, verify the claim, not the build.
  - **Plan-quality verifier wrote to a separate file** (`VERIFICATION_PLAN_QUALITY.md`) instead of appending to the architect's VERIFICATION.md. Either approach is fine; clearer file split is arguably better.
- Checkpoint tag: `post-plan-phase-9`.

- [2026-04-27T18:40:43Z] Session ended during build (may need /shipyard:resume)
- [2026-04-27T18:44:45Z] Session ended during build (may need /shipyard:resume)
- [2026-04-27T18:46:31Z] Session ended during build (may need /shipyard:resume)
- [2026-04-27T18:53:44Z] Session ended during build (may need /shipyard:resume)
- [2026-04-27T18:57:16Z] Session ended during build (may need /shipyard:resume)
- [2026-04-27T18:58:46Z] Session ended during build (may need /shipyard:resume)
- [2026-04-27T19:14:59Z] Session ended during build (may need /shipyard:resume)
- [2026-04-27T19:24:26Z] Session ended during build (may need /shipyard:resume)
- [2026-04-27T19:30:46Z] Session ended during build (may need /shipyard:resume)
- [2026-04-27T19:43:30Z] Session ended during build (may need /shipyard:resume)
- [2026-04-27T19:46:51Z] Session ended during build (may need /shipyard:resume)
- [2026-04-27T19:47:39Z] Session ended during build (may need /shipyard:resume)
- [2026-04-27T19:48:36Z] Session ended during build (may need /shipyard:resume)
- [2026-04-27T19:49:09Z] Session ended during build (may need /shipyard:resume)

## 2026-04-27 — Phase 9 built (`/shipyard:build 9`)

- **Wave 1 (foundation, sequential):** PLAN-1.1 across 3 commits (`32704a6` packages, `277ef64` counter surface, `26f6c2a` DispatchItem ActivityContext flip). 8 OTel + 5 Serilog packages added. DispatcherDiagnostics now has 10 counters. Build green, 69/69 tests preserved.
- **Wave 2 (parallel, disjoint files):** PLAN-2.1 (`06ff862` instrumentation, `e0ec830` ID-6 ISSUES close) and PLAN-2.2 (`c82dd83` config, `d6da64d` Serilog, `9fe5274` OTel, `c7ee4d1` ValidateObservability). Both reviewers APPROVE. **Parallel-build conflict twice during the wave**: PLAN-2.1's first build attempt failed with PLAN-2.2's in-progress CA1305/IDE0005 errors; PLAN-2.2's `ValidateAll` `GetRequiredService<IConfiguration>` regression broke 5 of PLAN-2.1's tests until orchestrator switched to `GetService<>?.Value` (commit `e340770`). Lesson: warnings-as-errors makes parallel-wave builds brittle even with disjoint files; the build is shared workspace.
- **Wave 3 (TDD tests):** PLAN-3.1 across 4 commits. 4 unit test files (DispatcherDiagnostics 3, EventPumpSpan 4, ValidateObservability 3 + ID-16 closure, CounterIncrement 9) + 1 integration test file (TraceSpansCoverFullPipeline 2). Host.Tests 69 → 88 (+19; target ≥84 exceeded by 4). 2 new integration tests pass. **Wave 2 regression surfaced**: `MqttToValidatorTests.Validator_ShortCircuits_OnlyAttachedAction` failed because PLAN-2.2's `AddSerilog` clears `builder.Logging` providers, dropping the test's `CapturingLoggerProvider`. Orchestrator inline fix moved capture-provider registration to `builder.Services.AddSingleton<ILoggerProvider>` AFTER `ConfigureServices` (commit `794a893`).
- **Builder stalls:** Phase 9 builders hit tool-budget caps mid-task more often than Phase 8 — PLAN-1.1, PLAN-2.1, PLAN-2.2, PLAN-3.1 all required SendMessage resumption at least once. PLAN-3.1 builder needed three resumptions. Pattern: cumulative complexity of OTel/Serilog API surface plus concurrent test-file generation taxed the budget. Mitigation that worked: dump SUMMARY content in agent's final result; orchestrator writes the file.
- **Issues closed this phase:** ID-6 (OperationCanceledException → ActivityStatusCode.Error during shutdown — fixed in PLAN-2.1 commit `06ff862`); ID-16 (`ValidateObservability` had no unit tests — closed in PLAN-3.1 commit `9dfdb83`); ID-17 (env-var fallback validation — orchestrator inline fix `a661d03`).
- **Issues opened this phase:** ID-18 (cardinality DOS), ID-19 (span tag injection), ID-20 (URI scheme), ID-21 (file-sink path) from auditor advisories; ID-22 (test polling improvement) from simplifier — all Low/deferred.
- **Phase verification:** COMPLETE. All 3 ROADMAP success criteria met. All 9 D-decisions honored (D1 ActivityContext, D2 OTel-without-exporter, D3 counter tags, D4 ID-6, D5 test split, D6 hand-rolled delegates, D7 Seq conditional, D8 span attributes, D9 errors.unhandled untagged).
- **Security audit:** PASS_WITH_NOTES (Low). 0 critical/important; 4 advisory (A1–A4 = ID-18–21) all deferred per user direction.
- **Simplifier:** 2 High applied (CooldownSeconds=0 dedupe guard inline; Task.Delay polling deferred as ID-22 after API-mismatch on `CapturingLogger<T>.Records` property). Validator-span parentage assertion added to `TraceSpansCoverFullPipelineTests` per Med #2.
- **Documenter:** ACCEPTABLE — no public API leakage; recommends architecture/CLAUDE.md additions for span tree + counter table (deferred to Phase 11/12 docs pass per ID-9).
- **Lessons-learned drafts:**
  - **Parallel-wave build hazard under warnings-as-errors.** Even with disjoint file sets, both builders share the workspace; one builder's incomplete code can fail the other's `dotnet build`. Mitigation: the second-to-commit builder must `dotnet build` clean before proceeding, ideally on the merged tree state.
  - **`AddSerilog` clobbers `builder.Logging.AddProvider` registrations.** Worker SDK Serilog wiring replaces the logging-provider pipeline. Test fixtures that need a `CapturingLoggerProvider` must register via `builder.Services.AddSingleton<ILoggerProvider>` AFTER `ConfigureServices` runs.
  - **`MemoryCache.AbsoluteExpirationRelativeToNow` rejects `TimeSpan.Zero`.** `DedupeCache.TryEnter` was vulnerable when callers passed `CooldownSeconds = 0`. Phase 9 added a guard treating `<= 0` as "no dedupe". Test fixtures previously had to use `cooldownSeconds = 1` minimum as a workaround.
  - **`ActivityContext` struct field on channel items is the right pattern.** Lightweight 16-byte struct (TraceId/SpanId/Flags/State); `default` value yields a root span on the consumer side. Avoids GC pressure and `Activity` lifetime coupling.
  - **`HostApplicationBuilder.Host` doesn't exist in .NET 10 Worker SDK.** Use `builder.Services.AddSerilog((services, lc) => ...)` from `Serilog.Extensions.Hosting`, NOT `builder.Host.UseSerilog`.
  - **OpenTelemetry InMemoryExporter v1.11.2 vs v1.15.3 API divergence.** v1.11.2's `AddInMemoryExporter(ICollection<Activity>)` lacks the options overload; use `new InMemoryExporter<Activity>(list)` + `AddProcessor(new SimpleActivityExportProcessor(exporter))` instead.
- Checkpoint tags: `pre-build-phase-9`, `post-build-phase-9`.

- [2026-04-28T13:58:59Z] Session ended during build (may need /shipyard:resume)
- [2026-04-28T14:00:30Z] Session ended during build (may need /shipyard:resume)
- [2026-04-28T15:53:51Z] Session ended during build (may need /shipyard:resume)
- [2026-04-28T16:08:42Z] Session ended during build (may need /shipyard:resume)
- [2026-04-28T16:16:23Z] Session ended during build (may need /shipyard:resume)
- [2026-04-28T16:19:11Z] Session ended during build (may need /shipyard:resume)
- [2026-04-28T16:25:07Z] Session ended during build (may need /shipyard:resume)
- [2026-04-28T17:51:21Z] Session ended during build (may need /shipyard:resume)
- [2026-04-28T18:03:01Z] Session ended during build (may need /shipyard:resume)
- [2026-04-28T18:05:54Z] Session ended during build (may need /shipyard:resume)
- [2026-04-28T18:06:27Z] Session ended during build (may need /shipyard:resume)
- [2026-04-28T18:06:34Z] Session ended during build (may need /shipyard:resume)
- [2026-04-28T19:31:26Z] Session ended during build (may need /shipyard:resume)
- [2026-04-28T19:33:30Z] Session ended during build (may need /shipyard:resume)
- [2026-04-28T19:35:17Z] Session ended during build (may need /shipyard:resume)
- [2026-04-28T19:38:33Z] Session ended during build (may need /shipyard:resume)
- [2026-04-28T19:45:50Z] Session ended during build (may need /shipyard:resume)
- [2026-04-28T19:46:43Z] Session ended during build (may need /shipyard:resume)
- [2026-04-28T19:47:21Z] Session ended during build (may need /shipyard:resume)
- [2026-04-28T19:48:58Z] Session ended during build (may need /shipyard:resume)
- [2026-04-28T20:09:58Z] Session ended during build (may need /shipyard:resume)
- [2026-04-28T20:15:48Z] Session ended during build (may need /shipyard:resume)
- [2026-04-28T20:17:16Z] Session ended during build (may need /shipyard:resume)
- [2026-04-28T20:34:28Z] Session ended during build (may need /shipyard:resume)
- [2026-04-28T20:44:58Z] Session ended during build (may need /shipyard:resume)
- [2026-04-28T20:49:22Z] Session ended during build (may need /shipyard:resume)
- [2026-04-28T20:49:51Z] Session ended during build (may need /shipyard:resume)
