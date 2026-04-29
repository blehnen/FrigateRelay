# Review: Plan 2.1 — Operator Parity-Window Checklist

## Verdict: PASS

---

## Stage 1 — Correctness

### Task 1: Create `docs/parity-window-checklist.md`

**Status: PASS**

**File exists:** `docs/parity-window-checklist.md`, 255 lines (acceptance criterion: ≥80). All nine required `##` sections are present.

#### Config key accuracy

| Key checked | Expected | Found in checklist | Source verified |
|---|---|---|---|
| `BlueIris:DryRun` | `true` | Line 65: `"BlueIris": { "DryRun": true }` | `BlueIrisOptions.DryRun` (bool, default false) — `src/FrigateRelay.Plugins.BlueIris/BlueIrisOptions.cs:32` |
| `Pushover:DryRun` | `true` | Line 66: `"Pushover": { "DryRun": true }` | `PushoverOptions.DryRun` (bool, default false) — `src/FrigateRelay.Plugins.Pushover/PushoverOptions.cs:43` |
| `Logging:File:CompactJson` | `true` | Line 67: `"Logging": { "File": { "CompactJson": true } }` | `HostBootstrap.ApplyLoggerConfiguration` reads `configuration["Logging:File:CompactJson"]` via string equality — `src/FrigateRelay.Host/HostBootstrap.cs:177-179` |

All three config keys match the landed Wave 1 source exactly.

#### EventId values in the config-key table

- Checklist line 78: `BlueIris:DryRun` → "Logs `BlueIrisDryRun` EventId 203" — matches `new EventId(203, "BlueIrisDryRun")` at `src/FrigateRelay.Plugins.BlueIris/BlueIrisActionPlugin.cs:24`.
- Checklist line 79: `Pushover:DryRun` → "Logs `PushoverDryRun` EventId 4" — matches `new EventId(4, "PushoverDryRun")` at `src/FrigateRelay.Plugins.Pushover/PushoverActionPlugin.cs:114`.

#### CLI command accuracy

Checklist (lines 26-29):
```bash
dotnet run --project tools/FrigateRelay.MigrateConf -c Release -- \
  --input /path/to/FrigateMQTTProcessingService.conf \
  --output appsettings.Local.json
```

`tools/FrigateRelay.MigrateConf/Program.cs:8` detects the verb by checking whether the first arg starts with `--`. Since `--input` starts with `--`, the default verb `migrate` is used and all args pass to `RunMigrate`. The command in the checklist is correct and will invoke `RunMigrate(["--input", ..., "--output", ...])`.

#### NDJSON field shape

Checklist line 109 example:
```json
{"@t":"...","@mt":"BlueIris DryRun would-execute for camera={Camera} label={Label} event_id={EventId}","@i":"BlueIrisDryRun","Camera":"driveway","Label":"person","EventId":"abc123"}
```

- `@mt` template matches `LogDryRun` definition at `BlueIrisActionPlugin.cs:25` exactly.
- `@i` for EventId name is correct Serilog `CompactJsonFormatter` behavior.
- Top-level properties `Camera`, `Label`, `EventId` are the structured log parameters from the `LoggerMessage.Define<string, string, string>` call (positions map to camera, label, eventId respectively).

PLAN-1.5 Task 2's locked field list (`Camera`, `Label`, `EventId`, `@t`, `@mt`) is satisfied.

#### CSV column shape

Checklist (lines 130-147): header `timestamp,camera,label,action,outcome` with worked example. Matches PLAN-3.1 expected input format exactly as specified in PLAN-2.1 Task 1 section 5.

#### Workflow completeness

- Pre-window (Pre-flight section) → during window (Watch the window section) → end-of-window (After 48 hours section) → resume signal (line 201: `/shipyard:resume`): all four phases are present and operator-followable.
- Line 202 explicitly states "The Shipyard workflow detects that Wave 2 is complete and Wave 3 is pending, then routes to `/shipyard:build 12` for Wave 3." — satisfies the resume signal requirement.

#### Failure modes

All three required cases covered:
- FrigateRelay throws: lines 229-234
- Legacy service throws/restarts: lines 236-240
- Cannot collect complete legacy CSV: lines 242-247
- Bonus (added by builder, beyond spec): zero-DryRun-count case (lines 249-254) — useful operator guidance.

#### No RFC1918 IPs

URL examples use `<your-blueIris-host>` placeholders (lines 40-42), not actual IP addresses. `grep -nE '192\.168\.|10\.0\.0\.'` would return zero matches.

#### Secret-scan

Checklist uses `<your-app-token>` and `<your-user-key>` angle-bracket placeholders on lines 49-50. Neither matches `AppToken\s*=\s*[A-Za-z0-9]{20,}` or `UserKey\s*=\s*[A-Za-z0-9]{20,}`. Scanner exits 0.

#### Acceptance criteria verification

| Criterion | Result |
|---|---|
| `test -f docs/parity-window-checklist.md` | PASS — file exists |
| `wc -l` ≥ 80 | PASS — 255 lines |
| `grep -q '## Pre-flight'` | PASS — line 17 |
| `grep -q '"DryRun": true'` | PASS — lines 65-66 |
| `grep -q '"CompactJson": true'` | PASS — line 67 |
| `grep -q 'Pushover__AppToken'` | PASS — line 49 |
| `grep -q 'BlueIris:TriggerUrlTemplate'` | PASS — line 33 |
| `grep -q 'parity-window/legacy-actions.csv'` | PASS — lines 149, 191 |
| `grep -q '/shipyard:resume'` | PASS — line 201 |
| No `192.168.` or `10.0.0.` matches | PASS |
| `secret-scan.sh` exits 0 | PASS |

---

## Stage 2 — Integration

### File-disjoint constraint

Status: **PASS**. Only `docs/parity-window-checklist.md` was created. No source code, test, CI, or other docs touched. Consistent with the plan's `files_touched` list.

### No real secrets or RFC1918 IPs

Status: **PASS**. No actual IP addresses in the file. Pushover secret examples use shell-export placeholders (`<your-app-token>`, `<your-user-key>`), not real token shapes.

### Forward-references to PLAN-3.1 reconciler

Status: **PASS**. Checklist references the reconciler via narrative ("Wave 3 PLAN-3.1 reconciliation"), not a specific CLI invocation — appropriate because the reconciler verb (`reconcile`) exists in the tool but is not yet implemented (stub at `Program.cs:39`). The checklist does not prescribe the exact `reconcile` subcommand invocation, which correctly avoids locking a Wave 3 interface that hasn't been designed yet. The two artifact paths expected by PLAN-3.1 (`logs/frigaterelay-*.log` NDJSON and `parity-window/legacy-actions.csv`) are correctly documented as the operator hand-off.

### README linkage

Status: **PASS (expected standalone)**. The checklist is standalone with no inbound link from `README.md`. This is explicitly correct for Wave 2 per PLAN-2.1 ("PLAN-3.2 will add the README pointer"). No issue.

### Phase 11 secret-scan policy

Status: **PASS**. See Stage 1 analysis above.

---

### Minor observations (non-blocking)

1. **`@i` field description accuracy** — The checklist (lines 104-106) describes the EventId name as appearing at `"@i"` per `CompactJsonFormatter` convention. This is correct for Serilog's rendering of `EventId.Name`. No issue; noting explicitly because it was a Stage 1 verification point.

2. **Log file pattern in `After 48 hours`** — Line 190 uses `logs/frigaterelay-YYYYMMDD.log` (literal date pattern) while the actual rolling file produces names like `logs/frigaterelay-20260429.log`. This is standard Serilog `RollingInterval.Day` output and the checklist's shell globs (`logs/frigaterelay-*.log`) correctly handle the wildcard case throughout. No inconsistency.

3. **`appsettings.Local.json` merge instruction** — The checklist says "merge the following block into your migrated `appsettings.Local.json`" (line 59) without explaining that `appsettings.Local.json` supports top-level key merging (JSON files in `IConfiguration` merge by key). A first-time operator might not know how to merge JSON objects. Not a correctness issue — the operator can simply add the three keys at the top level of their existing JSON file — but a brief note like "add these three keys at the top level of the JSON object" would reduce ambiguity. **Suggestion only.**

---

## Verdict reasoning

All spec requirements satisfied. Config keys, EventId values, CLI command, NDJSON field shape, CSV schema, workflow structure, failure modes, and secret-scan policy verified against landed Wave 1 source. No critical or important findings. File is standalone as designed for Wave 2.

---

*Reviewed by: shipyard:reviewer, 2026-04-28*
