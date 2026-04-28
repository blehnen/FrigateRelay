---
plan: PLAN-1.4
commit: 8bebf47
file: docs/migration-from-frigatemqttprocessing.md
reviewer: reviewer-1-4
status: PASS
---

# REVIEW-1.4 — docs/migration-from-frigatemqttprocessing.md

## Stage 1 — Correctness

### Field-by-field tables
- [x] `[ServerSettings]` table present (lines 35-42): `server`, `blueirisimages`, `frigateapi`, unknown-key row
- [x] `[PushoverSettings]` table present (lines 48-52): `AppToken`, `UserKey`, `NotifySleepTime`
- [x] `[SubscriptionSettings]` table present (lines 58-68): `Name`, `CameraName`, `ObjectName`, `Zone`, `Camera` (dropped), `LocationName`/`CameraShortName` (dropped)

### Tool reference
- [x] `tools/FrigateRelay.MigrateConf` referenced with exact `--input`/`--output` CLI shape (lines 26-29)
- [x] `tools/FrigateRelay.MigrateConf/` directory confirmed to exist at `tools/FrigateRelay.MigrateConf/`

### Secret handling
- [x] `AppToken` and `UserKey` written as empty placeholders in output JSON; doc explicitly states they MUST be supplied via `Pushover__AppToken` / `Pushover__UserKey` env vars (lines 49-51, 108-117)
- [x] Default output is `appsettings.Local.json` (gitignored); doc states committed `appsettings.json` carries no secrets (lines 31-33)

### BlueIris trigger URL template
- [x] Dedicated section "The `Camera` field is not migrated" (lines 74-100) with before/after example; operator action required callout present

### frigateapi= mapping
- [x] `frigateapi=` confirmed at line 3 of `tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf`: `frigateapi = http://192.0.2.209:5000/`
- [x] Doc maps it to `FrigateApi:BaseUrl` in `[ServerSettings]` table (line 38): correct path

### CameraShortName vs CameraName distinction
- [x] Doc notes the distinction at lines 92-97: "in the legacy fixture the `Camera` query parameter value (`DriveWayHD`) comes from the `CameraShortName` field, which is distinct from `CameraName` (`driveway`). FrigateRelay uses `Subscriptions:N:Camera` (the Frigate camera name) as the token value."
- [x] Fixture confirms the pattern: `CameraShortName = DriveWayHD` / `CameraName = driveway` appear in every subscription block — values differ (mixed-case BlueIris name vs lowercase Frigate name)
- [x] Operator is told: "If your BlueIris camera names differ from your Frigate camera names, you may need to set `Camera` in each subscription to the BlueIris camera name" — correct guidance

### NotifySleepTime fan-out
- [x] Table row states: "applied to ALL subscriptions" with note "Defaults to 30 seconds if absent in the INI" (line 51)
- [x] Prose below table confirms: "The migrated output copies the value to every subscription entry since FrigateRelay configures cooldown per subscription." (lines 53-55)

## Stage 2 — Integration

### No RFC 1918 IPs
- [x] `grep -nE '192\.168|^10\.|172\.(1[6-9]|2[0-9]|3[01])\.'` — zero matches. All example IPs use RFC 5737 `192.0.2.x` range (192.0.2.53 in the BlueIris example section).

### No real secrets
- [x] No `AppToken=<value>`, `UserKey=<value>`, or api-key-shaped strings. Placeholders shown as `<your-app-token>` / `<your-user-key>`.

### No boilerplate
- [x] No "File Transfer Server" or other reference-project text.

### Forward-reference stability
- [x] `tools/FrigateRelay.MigrateConf/` directory exists — forward ref is valid.
- [x] Cross-references to `docs/parity-report.md` (PLAN-3.1, Wave 3) and `docs/parity-window-checklist.md` (Wave 2) are forward-refs to future plans — no issue, doc correctly labels them as future waves.

### Phase 11 ROADMAP success criterion
- [x] No boilerplate. Content is substantive and project-specific.

## Findings

**No defects found.**

Minor observation (not blocking): The `[ServerSettings]` table key is written as lowercase `server` / `blueirisimages` / `frigateapi` in the doc, which matches the legacy fixture exactly (SharpConfig is case-insensitive). The table note "All keys are case-insensitive (SharpConfig convention)" is present — adequate.

## Verdict

**PASS** — 0 blocking findings, 0 advisory findings. All must-have checklist items satisfied.
