# Verification — Phase 12 (Pre-Build Plan-Quality)

**Phase:** 12 — Parity Cutover (final phase before v1.0.0)
**Date:** 2026-04-28
**Type:** plan-review (pre-execution quality gate)

This document maps each Phase 12 ROADMAP deliverable + each CONTEXT-12 user
decision to the plan/task that satisfies it, and is the verifier's
spot-check substrate. Architect writes this skeleton up front (so partial
truncation still produces a usable artifact) and back-fills the actual
mappings once plans are landed.

## ROADMAP Phase 12 Deliverable Coverage

| Deliverable | Plan / Task | Verify how |
| --- | --- | --- |
| Side-by-side deployment via DryRun (logging-only per D1) | PLAN-1.1 (BlueIris) + PLAN-1.2 (Pushover) | `grep DryRun src/FrigateRelay.Plugins.BlueIris/BlueIrisOptions.cs`; `grep DryRun src/FrigateRelay.Plugins.Pushover/PushoverOptions.cs`; new unit tests assert `would-execute` log path; no outbound HTTP call. |
| `docs/migration-from-frigatemqttprocessing.md` field-by-field mapping | PLAN-1.4 Task 1 | `test -f docs/migration-from-frigatemqttprocessing.md` and table headings (`grep -q '\[ServerSettings\]'`, `\[PushoverSettings\]`, `\[SubscriptionSettings\]`). |
| C# migration tool `tools/FrigateRelay.MigrateConf/` | PLAN-1.3 Task 1+2 | `dotnet build tools/FrigateRelay.MigrateConf -c Release` clean; `dotnet run --project tools/FrigateRelay.MigrateConf -c Release -- --input tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf --output /tmp/out.json` produces valid JSON; tool csproj is in `FrigateRelay.sln`. |
| MigrateConf output passes `ConfigSizeParityTest` (Phase 8) | PLAN-1.3 Task 3 (companion test) | `dotnet run --project tests/FrigateRelay.MigrateConf.Tests -c Release` green; the test runs the tool against the fixture and asserts the same `<= 0.60` size ratio + `StartupValidation.ValidateAll` binding. |
| Parity-CSV export mechanism (Serilog NDJSON file sink — Option A) | PLAN-1.5 Task 1+2 | `grep CompactJsonFormatter src/FrigateRelay.Host/HostBootstrap.cs`; new opt-in config key under `Logging:File:CompactJson`; default flips ON only when `DryRun: true` is detected, OR explicit config flag (architect picks: explicit config flag, no behavioral inference). |
| Operator parity-window checklist (Wave 2 gate) | PLAN-2.1 Task 1 | `test -f docs/parity-window-checklist.md`; checklist documents start commands, log-collection paths, what to capture, and the 48h hand-off. |
| Reconciliation tooling (`migrate-conf reconcile` subcommand) | PLAN-3.1 Task 1+2 | `dotnet run --project tools/FrigateRelay.MigrateConf -c Release -- reconcile --legacy <csv> --frigaterelay <ndjson> --output <md>` produces a `parity-report.md`; companion test exercises a synthetic NDJSON+CSV pair. |
| `docs/parity-report.md` template | PLAN-3.1 Task 3 | `test -f docs/parity-report.md`; template stub lists summary table headers + missed/spurious sections. |
| README migration-section update | PLAN-3.2 Task 1 | `grep -q 'Migrating from FrigateMQTTProcessingService' README.md`; the new section links `docs/migration-from-frigatemqttprocessing.md` AND `tools/FrigateRelay.MigrateConf/`. |
| `RELEASING.md` snippet + CHANGELOG `[Unreleased]` Phase 12 entry | PLAN-3.2 Task 2+3 | `test -f RELEASING.md`; `git tag v1.0.0 && git push --tags` exact command present; `grep -q 'Phase 12' CHANGELOG.md` under `[Unreleased]`. |
| `v1.0.0` tag (manual per D7) | PLAN-3.2 documents the operator command; tag itself is NOT an agent action | n/a — operator runs it after parity-report sign-off; `release.yml` (Phase 10) auto-builds GHCR images on the tag push. |

## CONTEXT-12 Decision Coverage

| D# | Decision | Plan / Task | Notes |
| --- | --- | --- | --- |
| D1 | Parity posture = logging-only (no real BlueIris/Pushover from FrigateRelay) | PLAN-1.1, PLAN-1.2 (DryRun in actions); PLAN-2.1 (checklist requires `DryRun: true` in operator config) | Validators / snapshot providers explicitly out of scope (D5 corollary). |
| D2 | 3 waves with explicit operator-checkpoint Wave 2 | Wave 1 = 5 plans (DryRun×2 OR ×1, MigrateConf, MigrateConf-Tests, doc, parity-export); Wave 2 = 1 plan; Wave 3 = 2 plans | Wave 2 gates Wave 3; Wave 3 builders read real operator output. |
| D3 | Migration script = C# console tool (NOT Python) | PLAN-1.3 (tool); PLAN-1.4 (doc references the tool, not a Python script) | `Microsoft.Extensions.Configuration.Ini` + custom multi-section handling. |
| D4 | Narrow scope — no architecture/operations/config-reference docs | All plans avoid creating `docs/architecture.md`, `docs/configuration-reference.md`, `docs/operations-guide.md` | ID-9 stays deferred. |
| D5 | DryRun mechanism = per-action `DryRun: true` in BlueIris + Pushover only | PLAN-1.1, PLAN-1.2 | Validators (CodeProjectAi) and snapshot providers (FrigateSnapshot) untouched. |
| D6 | `tools/FrigateRelay.MigrateConf/` Exe + `tests/FrigateRelay.MigrateConf.Tests/` Exe; both in sln | PLAN-1.3 (tool csproj + sln add); PLAN-1.3 Task 3 (test csproj + sln add) | `run-tests.sh` glob auto-discovers the new test project. |
| D7 | v1.0.0 tag is manual operator step | PLAN-3.2 Task 2 (RELEASING.md) | `release.yml` from Phase 10 triggers on `v*`. |

## Wave dependency graph

```
Wave 1 (parallel, file-disjoint):
  PLAN-1.1 — BlueIris DryRun (src/FrigateRelay.Plugins.BlueIris/** + tests/FrigateRelay.Plugins.BlueIris.Tests/**)
  PLAN-1.2 — Pushover DryRun (src/FrigateRelay.Plugins.Pushover/** + tests/FrigateRelay.Plugins.Pushover.Tests/**)
  PLAN-1.3 — MigrateConf tool + tests (tools/FrigateRelay.MigrateConf/** + tests/FrigateRelay.MigrateConf.Tests/** + FrigateRelay.sln)
  PLAN-1.4 — Migration doc (docs/migration-from-frigatemqttprocessing.md only)
  PLAN-1.5 — Parity NDJSON sink (src/FrigateRelay.Host/HostBootstrap.cs + src/FrigateRelay.Host/FrigateRelay.Host.csproj + appsettings.Example.json doc)
        │
        ▼
Wave 2 (single plan, gates Wave 3):
  PLAN-2.1 — Operator parity-window checklist (docs/parity-window-checklist.md only)
        │
        ▼ (after operator collects 48h of NDJSON + legacy CSV out-of-session)
        │
Wave 3 (parallel, file-disjoint):
  PLAN-3.1 — Reconcile subcommand + parity-report.md template (tools/FrigateRelay.MigrateConf/** reconcile path + tests/FrigateRelay.MigrateConf.Tests/** reconcile tests + docs/parity-report.md)
  PLAN-3.2 — README migration section + RELEASING.md + CHANGELOG entry (README.md + RELEASING.md + CHANGELOG.md)
```

**File-disjoint check (Wave 1):**
- 1.1 owns `src/FrigateRelay.Plugins.BlueIris/**` + its tests dir.
- 1.2 owns `src/FrigateRelay.Plugins.Pushover/**` + its tests dir.
- 1.3 owns `tools/FrigateRelay.MigrateConf/**` + `tests/FrigateRelay.MigrateConf.Tests/**` + `FrigateRelay.sln`.
- 1.4 owns `docs/migration-from-frigatemqttprocessing.md`.
- 1.5 owns `src/FrigateRelay.Host/HostBootstrap.cs` + `src/FrigateRelay.Host/FrigateRelay.Host.csproj`.

No two Wave 1 plans share a file. SAFE to parallelize.

**File-disjoint check (Wave 3):**
- 3.1 owns `tools/FrigateRelay.MigrateConf/**` (reconcile-only additions; touches Program.cs subcommand router) + `tests/FrigateRelay.MigrateConf.Tests/**` + `docs/parity-report.md`.
- 3.2 owns `README.md` + `RELEASING.md` + `CHANGELOG.md`.

3.1 returns to MigrateConf Program.cs which Wave 1 PLAN-1.3 also wrote — but they are in different waves (Wave 3 strictly after Wave 1), so no concurrency conflict. SAFE.

## Risk + sequencing notes

- **Highest risk: PLAN-1.3 (MigrateConf tool).** Repeated `[SubscriptionSettings]` headers are SharpConfig-specific and `Microsoft.Extensions.Configuration.Ini` does NOT handle them out of the box (last writer wins on duplicate sections). The plan calls out a hand-rolled INI section enumerator; builder must implement and test for the 9-subscription fixture explicitly. Mitigation: round-trip test against `tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf` (exists, 9 subscriptions). If round-trip yields fewer than 9 subscriptions in JSON output, test fails fast.
- **Medium risk: PLAN-1.5 (parity NDJSON sink).** Touches `HostBootstrap.cs` shared by every deployment. Mitigation: gate the NDJSON formatter behind an opt-in config key (`Logging:File:CompactJson: true`) so the default text format remains for production users; only the parity operator flips it on.
- **Low risk: PLAN-1.1, 1.2, 1.4, 2.1, 3.2.** Localized changes, doc files, or pure additive options.
- **PLAN-3.1 reconcile depends on PLAN-1.5 NDJSON shape.** Wave-3 plan must reference Wave-1 PLAN-1.5's `event_id`, `Camera`, `Label`, `Action`, `Outcome` field names so the reconciler parses them correctly. Captured in PLAN-3.1's Dependencies section.
- **Wave 2 dead-time.** Wave 2's only task creates the operator checklist; the actual 48h passive watch happens out of session. Wave 3 builders MUST verify the operator-supplied artifacts exist (`logs/audit.ndjson`, `legacy-actions.csv`) before producing the parity report; if absent, fail fast with a clear message rather than fabricating output.

## Open architect-discretion items resolved

- **Plugin DryRun rollout layout:** TWO plans (PLAN-1.1 BlueIris, PLAN-1.2 Pushover). Two plans = better parallelism, file-disjoint by csproj, ≤3 tasks each. The single-plan alternative would have crowded 4-5 tasks (record + class + 2 plugin classes + 2 test classes) and violated the ≤3-tasks-per-plan rule.
- **Parity-CSV export mechanism:** Option A (Serilog `CompactJsonFormatter` on existing file sink, opt-in config flag). Per RESEARCH §5 recommendation. Lowest surface area; reconciler reads NDJSON.
- **Reconciliation tooling location:** `migrate-conf reconcile` subcommand of the existing MigrateConf tool (not a separate csproj, not a bash/Python script). Bias toward subcommand per CONTEXT-12 architect-discretion guidance; minimizes new csproj surface.
- **Operator-checklist file path:** `docs/parity-window-checklist.md` (operator-facing). Lives next to the migration doc so a v1.0.0 user finds both together.
- **MigrateConf default output filename:** `appsettings.Local.json` (secrets live there per CLAUDE.md).
- **Phase 12 CHANGELOG entry:** added in Wave 3 PLAN-3.2 Task 3 proactively (Phase 11's lessons-learned about retroactive CHANGELOG).

## Plan quality gate (verifier checklist)

For each plan file `PLAN-{W}.{P}.md`:
- [ ] YAML frontmatter present with `phase`, `plan`, `wave`, `dependencies`, `must_haves`, `files_touched`, `tdd`, `risk`.
- [ ] ≤3 tasks per plan.
- [ ] Each task has Files / Action / Description / Acceptance Criteria.
- [ ] Acceptance Criteria are runnable shell commands (no prose).
- [ ] No two plans within the same wave list the same path in `files_touched`.
- [ ] Verification block at end of each plan with reproducible command sequence.
- [ ] No plan creates `docs/architecture.md`, `docs/configuration-reference.md`, or `docs/operations-guide.md` (D4).
- [ ] No plan references SharpConfig.
- [ ] No plan introduces RFC 1918 IPs or hard-coded secrets.
