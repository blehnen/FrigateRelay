# REVIEW-3.2 — PLAN-3.2: README migration + RELEASING.md + CHANGELOG `[Unreleased]` Phase 12

**Reviewer:** reviewer-3-2
**Date:** 2026-04-28
**Commits:** `bb556c5` (README), `ed325d6` (RELEASING.md), `39c9677` (CHANGELOG)
**Status:** PASS

---

## Stage 1 — Correctness

### Task 1 — README.md migration section (bb556c5)

- [x] Section present at line 87 — heading `## Migrating from FrigateMQTTProcessingService`
- [x] All links resolve on disk:
  - `tools/FrigateRelay.MigrateConf/` — EXISTS (contains Program.cs, Reconciler.cs, IniReader.cs, etc.)
  - `docs/migration-from-frigatemqttprocessing.md` — EXISTS
  - `docs/parity-window-checklist.md` — EXISTS
  - `docs/parity-report.md` — EXISTS
  - `RELEASING.md` — EXISTS
- [x] CLI invocation correct: `dotnet run --project tools/FrigateRelay.MigrateConf -c Release -- --input <path> --output <path>` — no spurious `-- migrate` verb (the verb is the default, correctly implicit)
- [x] Placement correct: between `## Configuration` (line 40) and `## Adding a new action plugin` (line 104)

### Task 2 — RELEASING.md (ed325d6)

- [x] Pre-flight checklist present (9-item checklist under `## Pre-release checklist`): parity report zero-missed, parity window closed, all tests green, DryRun flags removed, CHANGELOG promoted
- [x] Tag commands exact per CONTEXT-12 D7:
  - `git tag -a v1.0.0 -m "v1.0.0 — initial public release"` ✓
  - `git push origin v1.0.0` ✓
- [x] Phase 10 release.yml mentioned: "Pushing the `v1.0.0` tag triggers `.github/workflows/release.yml` (added in Phase 10)"
- [x] CHANGELOG promotion step present as explicit Step 1 with manual-edit instructions
- [x] Rollback procedure present (Step 4 + `## Rollback` section): tag delete + GHCR image removal + cut v1.0.1 guidance

### Task 3 — CHANGELOG.md `[Unreleased]` Phase 12 (39c9677)

- [x] Phase 12 entry at lines 10–27, above Phase 11 entry at line 30 — ordering correct
- [x] Format mirrors prior phase entries: `### Phase NN — Title (date)` + `#### Added` / `#### Changed` subsections
- [x] Coverage complete:
  - BlueIris/Pushover DryRun flags ✓
  - MigrateConf tool ✓
  - reconcile subcommand ✓
  - NDJSON sink (`Logging:File:CompactJson`) ✓
  - migration doc ✓
  - parity-window checklist ✓
  - parity-report template ✓
  - RELEASING.md ✓
  - README section ✓
- [x] CRITICAL: only `## [Unreleased]` heading present — `grep -n "^## \[" CHANGELOG.md` returns only line 8 `## [Unreleased]`. No `## [1.0.0]` heading exists.

---

## Stage 2 — Integration

- [x] File-disjoint with PLAN-3.1: `bb556c5` touches only `README.md`; `ed325d6` touches only `RELEASING.md`; `39c9677` touches only `CHANGELOG.md`. No overlap with `tools/`, `tests/`, `docs/parity-report.md`, `docs/parity-window-checklist.md`, or `src/`.
- [x] No real secrets, no RFC1918 IPs: grep for `192.168`, `10.x`, `172.16-31.x`, `mailto:`, `AppToken=`, `UserKey=` across all three touched files — zero matches.
- [x] Forward references to PLAN-3.1 artifacts stable: `tools/FrigateRelay.MigrateConf/Reconciler.cs` and `docs/parity-report.md` both exist on disk (PLAN-3.1 commit `1d18b31` landed).
- [x] Secret-scan policy clean: no new patterns introduced; existing `.github/scripts/secret-scan.sh` exclusions unchanged.

---

## Findings

**Blockers:** 0
**Warnings:** 0
**Notes:** 1

- **NOTE (non-blocking):** `RELEASING.md` Step 4 uses `<owner>` placeholder in `docker pull ghcr.io/<owner>/frigaterelay:1.0.0` — this is intentional operator-substitution guidance (same pattern as Phase 11's README). Consistent with project convention; not a concern.

---

## Verdict

**PASS** — all 17 checks green, zero blockers, zero warnings. PLAN-3.2 may land.
