---
plan: 1.4
task: 1
status: completed
---

# SUMMARY: PLAN-1.4 — docs/migration-from-frigatemqttprocessing.md

## Status: COMPLETED

## Tasks

- [x] Task 1: Create `docs/migration-from-frigatemqttprocessing.md`

## Acceptance criteria

- File exists: PASS
- Line count (>=80): 174 lines PASS
- [ServerSettings] present: PASS
- [PushoverSettings] present: PASS
- [SubscriptionSettings] present: PASS
- tools/FrigateRelay.MigrateConf reference: PASS
- Pushover__AppToken documented: PASS
- BlueIris:TriggerUrlTemplate documented: PASS
- No RFC 1918 IPs: PASS
- No secret values leaked: PASS

## Key decisions

- Used RFC 5737 IPs (192.0.2.x) throughout, matching the committed fixture
- Documented dropped fields: `Camera` (per-subscription BlueIris URL → global template), `LocationName`, `CameraShortName`
- Documented the CameraShortName vs CameraName distinction (BlueIris camera name vs Frigate camera name) as an operator note
- `frigateapi` key mapped to `FrigateApi:BaseUrl` (present in fixture, not listed in RESEARCH §1 — added as observed from fixture)
- NotifySleepTime fan-out behaviour (global → per-subscription copy) explicitly documented
