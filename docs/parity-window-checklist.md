# Parity-Window Checklist

## Purpose

This checklist guides you through the ≥48-hour side-by-side parity window between the legacy
`FrigateMQTTProcessingService` and FrigateRelay. During the window, the legacy service continues
to operate normally — it sends real BlueIris triggers and Pushover notifications to production
targets. FrigateRelay runs alongside it in DryRun mode: it subscribes to the same MQTT broker,
evaluates every event, and logs every action it *would* have taken at structured-log level (Info)
— without calling any external API. Zero blast radius if FrigateRelay misbehaves (CONTEXT-12 D1).

After ≥48 hours you collect two artifacts — the FrigateRelay NDJSON log file and a legacy-side
CSV of actual triggers — and hand them to Wave 3 builders for reconciliation.

---

## Pre-flight

Complete every item before starting the timer.

- [ ] Legacy `FrigateMQTTProcessingService` is running and healthy. Note the version:
      `version: _______________`
- [ ] You have run the migration tool to produce a local `appsettings.Local.json`:

  ```bash
  dotnet run --project tools/FrigateRelay.MigrateConf -c Release -- \
    --input /path/to/FrigateMQTTProcessingService.conf \
    --output appsettings.Local.json
  ```

  See `docs/migration-from-frigatemqttprocessing.md` for the full field-by-field mapping.

- [ ] You have manually set `BlueIris:TriggerUrlTemplate` in `appsettings.Local.json`. The
      migration tool cannot infer this value from the per-subscription `Camera` URLs in the INI
      file — it is the **only field that requires manual operator action** post-migration. Example
      shape (use your actual BlueIris host and credentials, stored in the gitignored local file):

  ```jsonc
  "BlueIris": {
    "TriggerUrlTemplate": "http://<your-blueIris-host>:<port>/admin?trigger&camera={camera}&user=<user>&pw=<password>",
    "SnapshotUrlTemplate": "http://<your-blueIris-host>:<port>/image/{camera}"
  }
  ```

- [ ] You have set the Pushover secret environment variables in your shell or Docker compose
      environment before starting FrigateRelay:

  ```bash
  export Pushover__AppToken=<your-app-token>
  export Pushover__UserKey=<your-user-key>
  ```

- [ ] You have added the parity-window overlay flags to `appsettings.Local.json` (next section).

---

## Parity-window `appsettings.Local.json` overlay

Merge the following block into your migrated `appsettings.Local.json`. These three flags put
both action plugins into logging-only mode and enable the structured NDJSON log format required
by the Wave 3 reconciler.

```jsonc
{
  "BlueIris":  { "DryRun": true },
  "Pushover":  { "DryRun": true },
  "Logging":   { "File": { "CompactJson": true } }
}
```

**After the parity window closes, REMOVE these flags before declaring v1.0.0 cutover.**
Leaving `DryRun: true` in a production deployment silently suppresses all real actions.

Config-key reference (Wave 1 sources):

| JSON path | Source | Effect when `true` |
|---|---|---|
| `BlueIris:DryRun` | `BlueIrisOptions.DryRun` (PLAN-1.1) | Logs `BlueIrisDryRun` EventId 203 instead of HTTP trigger |
| `Pushover:DryRun` | `PushoverOptions.DryRun` (PLAN-1.2) | Logs `PushoverDryRun` EventId 4 instead of API call |
| `Logging:File:CompactJson` | `HostBootstrap.cs` (PLAN-1.5) | Writes NDJSON to `logs/frigaterelay-YYYYMMDD.log` |

---

## Bringup

```bash
# Terminal A — legacy service (already running per pre-flight; leave it alone)

# Terminal B — FrigateRelay in DryRun mode
dotnet run --project src/FrigateRelay.Host -c Release
```

FrigateRelay should:
1. Pass startup validation and print no errors.
2. Connect to the MQTT broker.
3. Start writing structured log lines to `logs/frigaterelay-YYYYMMDD.log` (NDJSON, one JSON
   object per line, daily rolling file).

Sanity-check tail (run in Terminal C):

```bash
tail -f logs/frigaterelay-*.log | grep -E '"BlueIris DryRun would-execute"|"Pushover DryRun would-execute"'
```

With `CompactJson: true` the file is NDJSON. **`Serilog.Formatting.Compact.CompactJsonFormatter`
renders `@i` as a hex Murmur3 hash of the message template — NOT the EventId name.** Use the
`@mt` (message template) field for action discrimination instead. Each DryRun line looks like:

```json
{"@t":"2026-04-29T12:34:56.000Z","@mt":"BlueIris DryRun would-execute for camera={Camera} label={Label} event_id={EventId}","@i":"a1b2c3d4","Camera":"driveway","Label":"person","EventId":"abc123"}
```

The reconcile subcommand (`tools/FrigateRelay.MigrateConf reconcile`) keys on `@mt.StartsWith("BlueIris DryRun")`
and `@mt.StartsWith("Pushover DryRun")` — your grep commands should match those exact prefix strings.
If you see lines starting with `"BlueIris DryRun"` or `"Pushover DryRun"` in `@mt` each time a
Frigate event fires, the parity window is running correctly.

Verify the log file exists and is growing:

```bash
ls -lh logs/frigaterelay-*.log
```

---

## Legacy-side CSV export

The legacy `FrigateMQTTProcessingService` does not emit a structured CSV natively. You must
produce one yourself from its log files, database, or event records. The Wave 3 reconciler
(PLAN-3.1) expects the following format:

```
timestamp,camera,label,action,outcome
```

- `timestamp` — ISO 8601 UTC, e.g. `2026-04-29T12:34:56Z`
- `camera` — Frigate camera name as it appears in subscriptions (e.g. `driveway`)
- `label` — detection label (e.g. `person`, `car`)
- `action` — `BlueIris` or `Pushover`
- `outcome` — `success`, `failed`, or `skipped` (use `skipped` for cooldown suppression)

Worked example:

```csv
timestamp,camera,label,action,outcome
2026-04-29T12:34:56Z,driveway,person,BlueIris,success
2026-04-29T12:34:56Z,driveway,person,Pushover,success
2026-04-29T12:35:10Z,driveway,person,BlueIris,skipped
2026-04-29T12:35:10Z,driveway,person,Pushover,skipped
```

Save the file to `parity-window/legacy-actions.csv` in your local working directory. This path
is NOT committed — it is a local operator artifact handed to Wave 3.

```bash
mkdir -p parity-window
# ... populate parity-window/legacy-actions.csv from legacy logs
```

---

## Watch the window

Let both services run for at least 48 hours. Check these items periodically:

- [ ] Both services have been running for ≥48 hours
- [ ] No unplanned restarts — if either service restarted, note the timestamp:
      `restart(s): _______________`
  (The parity report will exclude the gap; see Failure-mode guidance.)
- [ ] FrigateRelay log file exists and is growing:

  ```bash
  ls -lh logs/frigaterelay-*.log
  ```

- [ ] Legacy CSV is being collected continuously (populate after each observed trigger, or batch
      from legacy logs at the end of the window)
- [ ] (Optional ongoing sanity) Count grows over time:

  ```bash
  grep -c '"BlueIrisDryRun"' logs/frigaterelay-*.log
  ```

---

## After 48 hours

- [ ] Stop FrigateRelay gracefully (Ctrl+C in Terminal B; expect `Application is shutting
      down...` and exit 0)
- [ ] **Do NOT stop the legacy service** — it remains the production service until v1.0.0
      cutover. The parity window ending does not mean cutover has happened.
- [ ] Collect both artifacts and confirm they are readable:
  - `logs/frigaterelay-YYYYMMDD.log` (one or more daily NDJSON files)
  - `parity-window/legacy-actions.csv`
- [ ] Verify the NDJSON log is non-empty and contains DryRun lines:

  ```bash
  grep -c '"BlueIrisDryRun"\|"PushoverDryRun"' logs/frigaterelay-*.log
  ```

  A count greater than zero means FrigateRelay observed and would-have-acted on at least one
  event. If the count is zero, check that MQTT connectivity was stable before closing the window.

- [ ] Run `/shipyard:resume` to re-enter Phase 12. The Shipyard workflow detects that Wave 2 is
      complete and Wave 3 is pending, then routes to `/shipyard:build 12` for Wave 3.
- [ ] Wave 3 builders will reference these two file paths in PLAN-3.1 (reconciliation). Have the
      absolute paths ready:
  - FrigateRelay log: `<repo-root>/logs/frigaterelay-*.log`
  - Legacy CSV: `<local-dir>/parity-window/legacy-actions.csv`

---

## What Wave 3 will produce

After you run `/shipyard:resume`, Wave 3 builders will produce the following before v1.0.0 is
tagged:

| Deliverable | Location | Description |
|---|---|---|
| Parity report | `docs/parity-report.md` | Summary table, missed-alert list, spurious-alert list, cooldown-match verification |
| README migration section | `README.md` | Links migration doc + tool |
| Release prep | `RELEASING.md` | Exact commands for `git tag v1.0.0 && git push --tags` (per CONTEXT-12 D7) |

The parity report is the gate. Wave 3 declares success when: zero missed alerts, zero spurious
alerts, and cooldown behaviour matches across the ≥48h window. Any discrepancy must be explained
as an intentional improvement or fixed before the tag is pushed.

---

## Failure-mode guidance

### FrigateRelay throws during the window

Note the timestamp and the exception type from the log. The parity report acknowledges any
unplanned downtime gap. If FrigateRelay restarts cleanly and DryRun log lines resume, the gap is
bounded and the window can still close. If the exception is persistent, investigate before
continuing (do not extend the window with a broken FrigateRelay instance — start fresh).

### Legacy service throws or restarts

Note the timestamp but do NOT stop FrigateRelay. The parity comparison runs over the
bilaterally-up portion only. As long as both services were up for a contiguous ≥48h block, the
window is valid.

### Cannot collect a complete legacy CSV

If legacy log coverage is partial, note what period is missing. The Wave 3 PLAN-3.1 reconciler
will fail fast or produce a partial report indicating the coverage gap. A partial report still
has diagnostic value for the DryRun EventIds that DID fire. Document the gap in the CSV with a
comment row or a separate `parity-window/legacy-gap.txt` note file.

### FrigateRelay log is empty or DryRun count is zero

Possible causes: MQTT connectivity lost at start, wrong broker address, subscriptions not
matching event labels. Check FrigateRelay startup logs for MQTT connect/subscribe confirmation
and check that `Logging:File:CompactJson` is `true` in the active `appsettings.Local.json`.
A zero-DryRun log is not a valid parity window — restart from the bringup step.
