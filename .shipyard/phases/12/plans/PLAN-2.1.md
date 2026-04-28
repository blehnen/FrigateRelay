---
phase: 12-parity-cutover
plan: 2.1
wave: 2
dependencies: [1.1, 1.2, 1.3, 1.4, 1.5]
must_haves:
  - docs/parity-window-checklist.md created — operator-facing run book for the 48h passive watch
  - Documents the exact appsettings.Local.json shape with DryRun=true on BlueIris and Pushover plus Logging:File:CompactJson=true
  - Documents the legacy-side CSV export format (timestamp, camera, label, action, outcome)
  - Documents the FrigateRelay-side log path (logs/frigaterelay-YYYYMMDD.log NDJSON)
  - Documents what the operator hands off to Wave 3 builders
files_touched:
  - docs/parity-window-checklist.md
tdd: false
risk: low
---

# Plan 2.1: Operator parity-window checklist (Wave 2 gate)

## Context

CONTEXT-12 D2 makes Wave 2 a discrete observation checkpoint. No automated execution; this plan produces a checklist file that the operator follows during the ~48h passive watch. The checklist exists as a Shipyard-tracked artifact so it shows up in `/shipyard:status` and so the literal 48h sleep is bracketed by a clean gate between Wave 1 (active build) and Wave 3 (active reconcile).

**The 48h passive watch happens out of session.** The operator runs the legacy `FrigateMQTTProcessingService` and FrigateRelay (with DryRun on) side-by-side against the production MQTT broker for ≥48 hours, collecting `(timestamp, camera, label, action, outcome)` tuples from both. After the window closes, the operator runs `/shipyard:resume` and Wave 3 builders consume the collected artifacts.

**Architect-discretion locked:**

- **File path:** `docs/parity-window-checklist.md`. Lives in `docs/` alongside the migration doc so a v1.0.0 user finds both together. NOT under `.shipyard/` (that's for Shipyard-internal artifacts; this is operator-facing).
- **Single-task plan.** Wave 2 has exactly ONE plan with ONE task. Per CONTEXT-12 calibration ("Wave 2 will likely be a single 1-task plan"). The plan exists primarily as a Shipyard checkpoint — the actual content is the markdown file.
- **No code changes, no tests.** This is pure documentation; build/test commands are not part of acceptance.

## Dependencies

- **Wave 1 must complete** (PLAN-1.1, 1.2, 1.3, 1.4, 1.5). The checklist references the DryRun config key (1.1, 1.2), the migration tool (1.3), the migration doc (1.4), and the NDJSON sink config (1.5). Wave 1 plans MUST land before this checklist is authored, or the referenced features will not exist.
- File-disjoint with all other plans (only this plan touches `docs/parity-window-checklist.md`).

## Tasks

### Task 1: Create `docs/parity-window-checklist.md`

**Files:**
- `docs/parity-window-checklist.md` (create)

**Action:** create

**Description:**

Author the checklist with the structure below. **Operator-facing tone**, not architect-facing — this is a run book the user follows.

Required sections (each a `##` heading):

1. **Purpose** — one paragraph: this checklist guides the ≥48h side-by-side parity window between legacy `FrigateMQTTProcessingService` and FrigateRelay. The legacy service operates normally (real BlueIris/Pushover triggers); FrigateRelay runs in DryRun mode (logging-only, zero blast radius per CONTEXT-12 D1).

2. **Pre-flight** — checkbox list:
   - [ ] Legacy `FrigateMQTTProcessingService` is running and healthy (note the version)
   - [ ] You have run `dotnet run --project tools/FrigateRelay.MigrateConf -c Release -- --input <legacy.conf> --output appsettings.Local.json` (PLAN-1.4 migration doc covers this)
   - [ ] You have edited the migrated `appsettings.Local.json` to set `BlueIris:TriggerUrlTemplate` (the only manually-required field per the migration doc)
   - [ ] You have set env vars `Pushover__AppToken` and `Pushover__UserKey`
   - [ ] You have added the parity-window flags to `appsettings.Local.json` (next section)

3. **Parity-window `appsettings.Local.json` overlay** — exact JSON snippet to merge into the migrated file:

   ```jsonc
   {
     "BlueIris":  { "DryRun": true },
     "Pushover":  { "DryRun": true },
     "Logging":   { "File": { "CompactJson": true } }
   }
   ```

   Plus a note: "After the parity window closes, REMOVE these flags before declaring v1.0.0 cutover."

4. **Bringup** — exact commands. Keep the FrigateRelay run command consistent with CLAUDE.md's documented command list:

   ```bash
   # Terminal A — legacy service (already running per pre-flight)

   # Terminal B — FrigateRelay in DryRun mode
   dotnet run --project src/FrigateRelay.Host -c Release
   # Should boot, subscribe to MQTT, and start logging to logs/frigaterelay-YYYYMMDD.log (NDJSON)
   ```

   Include a sanity-check tail command:
   ```bash
   tail -f logs/frigaterelay-*.log | grep -E '"(BlueIrisDryRun|PushoverDryRun)"'
   ```
   Note: with `CompactJson: true`, lines are NDJSON; the EventId Name appears as `"@i":"BlueIrisDryRun"` or as a property — the checklist explains the exact field name match (PLAN-1.5 Task 2 locked: `Camera`, `Label`, `EventId` are top-level NDJSON properties; the EventId.Name lives at `"@i"` per `CompactJsonFormatter` convention).

5. **Legacy-side CSV export** — the legacy service does NOT emit a CSV natively (CONTEXT-12 confirms this is operator territory). The checklist instructs the operator to produce a CSV with header `timestamp,camera,label,action,outcome`, one row per legacy trigger, by whatever means available (parsing legacy log files, querying the legacy DB if any, or manual collection). A worked example:

   ```csv
   timestamp,camera,label,action,outcome
   2026-04-29T12:34:56Z,DriveWayHD,person,BlueIris,success
   2026-04-29T12:34:56Z,DriveWayHD,person,Pushover,success
   ```

   Save to `parity-window/legacy-actions.csv` (operator's local working dir; not committed).

6. **Watch the window** — checklist:
   - [ ] Both services running for ≥48 hours
   - [ ] No process restarts (if either restarts, note timestamp; the parity report will exclude that gap)
   - [ ] FrigateRelay log file exists and grows: `ls -lh logs/frigaterelay-*.log`
   - [ ] Legacy log/CSV is being collected
   - [ ] (Optional) sanity check: `grep -c '"BlueIrisDryRun"' logs/frigaterelay-*.log` should grow over time

7. **After 48 hours** — close-out:
   - [ ] Stop FrigateRelay (Ctrl+C; expect graceful shutdown per CLAUDE.md)
   - [ ] DO NOT stop the legacy service (it remains the production service until v1.0.0 cutover)
   - [ ] Collect both artifacts:
     - `logs/frigaterelay-*.log` (NDJSON)
     - `parity-window/legacy-actions.csv`
   - [ ] Run `/shipyard:resume` to re-enter Phase 12 Wave 3
   - [ ] Wave 3 builders will reference these two file paths in PLAN-3.1 (reconciliation)

8. **What Wave 3 will produce** — sets expectation:
   - `docs/parity-report.md` (summary table, missed-alerts list, spurious-alerts list)
   - README migration section (PLAN-3.2)
   - `RELEASING.md` snippet with the manual `git tag v1.0.0 && git push --tags` command (CONTEXT-12 D7)

9. **Failure-mode guidance:**
   - If FrigateRelay throws during the window (any unhandled exception), note the timestamp and the exception type. The parity report MUST acknowledge any unplanned downtime.
   - If the legacy service throws, note it but do NOT stop FrigateRelay. The parity comparison still runs over the bilaterally-up portion.
   - If you cannot collect a complete legacy CSV, the parity report can still be produced from the FrigateRelay-only NDJSON; Wave 3 PLAN-3.1 builders will be told to fail-fast or partial-report.

**Acceptance Criteria:**
- `test -f docs/parity-window-checklist.md`
- `wc -l docs/parity-window-checklist.md` is at least 80 lines.
- `grep -q '## Pre-flight' docs/parity-window-checklist.md`
- `grep -q '"DryRun": true' docs/parity-window-checklist.md`
- `grep -q '"CompactJson": true' docs/parity-window-checklist.md`
- `grep -q 'Pushover__AppToken' docs/parity-window-checklist.md`
- `grep -q 'BlueIris:TriggerUrlTemplate' docs/parity-window-checklist.md`
- `grep -q 'parity-window/legacy-actions.csv' docs/parity-window-checklist.md`
- `grep -q '/shipyard:resume' docs/parity-window-checklist.md`
- `grep -nE '192\.168\.|10\.0\.0\.' docs/parity-window-checklist.md` returns zero matches.
- `.github/scripts/secret-scan.sh` exits 0.

## Verification

```bash
test -f docs/parity-window-checklist.md
wc -l docs/parity-window-checklist.md
grep -q '## Pre-flight' docs/parity-window-checklist.md
grep -q '"DryRun": true' docs/parity-window-checklist.md
grep -q '"CompactJson": true' docs/parity-window-checklist.md
grep -q '/shipyard:resume' docs/parity-window-checklist.md
grep -nE '192\.168\.|10\.0\.0\.' docs/parity-window-checklist.md && exit 1 || true
.github/scripts/secret-scan.sh
```
