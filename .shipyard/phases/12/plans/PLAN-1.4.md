---
phase: 12-parity-cutover
plan: 1.4
wave: 1
dependencies: []
must_haves:
  - docs/migration-from-frigatemqttprocessing.md created
  - Field-by-field mapping table covering [ServerSettings], [PushoverSettings], [SubscriptionSettings]
  - References tools/FrigateRelay.MigrateConf/ usage with concrete --input/--output example
  - Documents the deliberately-dropped [SubscriptionSettings].Camera per-subscription URL
  - No hard-coded RFC 1918 IPs; uses RFC 5737 documentation IPs (192.0.2.x) per RESEARCH §8
files_touched:
  - docs/migration-from-frigatemqttprocessing.md
tdd: false
risk: low
---

# Plan 1.4: `docs/migration-from-frigatemqttprocessing.md` field-by-field mapping doc

## Context

ROADMAP Phase 12 mandates an INI → `appsettings.json` field-by-field mapping document. RESEARCH §1 enumerates the keys; RESEARCH §8 confirms the existing `tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf` is the canonical example shape (1 ServerSettings + 1 PushoverSettings + 9 SubscriptionSettings). This plan creates the operator-facing markdown doc that pairs with the MigrateConf tool (PLAN-1.3) — operators read this doc to understand what the tool does and what they need to supply manually (secrets, BlueIris trigger URL template).

**Architect-discretion locked:**

- **Doc lives at `docs/`** (operator-facing, ships with v1.0.0), NOT under `.shipyard/`. Future operators who clone the repo see it. The Wave 3 README update (PLAN-3.2) links to it.
- **No new architecture/operations/config-reference docs** (D4 — explicitly forbidden).
- **Sample IPs in the doc are RFC 5737** (`192.0.2.x`) per the existing fixture convention. The secret-scan rejects RFC 1918 (`192.168.x.x`, `10.x.x.x`); RFC 5737 is documentation-safe.
- **Document the dropped field:** the legacy `[SubscriptionSettings].Camera` (BlueIris HTTP trigger URL) is intentionally NOT migrated per-subscription — the new shape uses a single `BlueIris:TriggerUrlTemplate` with `{camera}` token. This is a behavioral change the operator MUST understand; the doc has a dedicated callout.

## Dependencies

- None (Wave 1 plan, parallel-safe).
- File-disjoint: this plan touches ONLY `docs/migration-from-frigatemqttprocessing.md`.
- Soft-coupling note: PLAN-1.3 produces the tool referenced by this doc. The doc references `tools/FrigateRelay.MigrateConf/` paths and the `--input` / `--output` CLI shape, both of which are LOCKED in PLAN-1.3 — the doc author does not need PLAN-1.3 to be complete to write the doc, but the file paths and CLI shape MUST match PLAN-1.3 exactly.

## Tasks

### Task 1: Create `docs/migration-from-frigatemqttprocessing.md`

**Files:**
- `docs/migration-from-frigatemqttprocessing.md` (create)

**Action:** create

**Description:**

Author the doc with the structure below. **Do NOT** copy any real `.conf` content; use the sanitized RFC 5737 fixture shape from `tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf` as the example.

Required top-level sections:

1. **Overview** — one paragraph explaining: legacy `FrigateMQTTProcessingService` used a SharpConfig INI (`[ServerSettings]`/`[PushoverSettings]`/`[SubscriptionSettings]`); FrigateRelay uses `IConfiguration` JSON layering (Profiles + Subscriptions per CONTEXT-8 S2). The MigrateConf tool automates the structural migration; the operator supplies secrets via env vars.

2. **Prerequisites** — operator must have:
   - The legacy `.conf` file readable on disk
   - .NET 10 SDK installed locally (or the prebuilt `FrigateRelay.MigrateConf` binary if shipped — Phase 12 ships source-built only)
   - Knowledge of their BlueIris trigger URL template (auto-detection from per-subscription URLs is out of scope)

3. **Running the tool** — exact commands:
   ```bash
   dotnet run --project tools/FrigateRelay.MigrateConf -c Release -- \
     --input /path/to/FrigateMQTTProcessingService.conf \
     --output /path/to/appsettings.Local.json
   ```
   Note the default output filename `appsettings.Local.json` — secrets land here, the file is `.gitignore`'d. The committed `appsettings.json` carries no secrets per CLAUDE.md.

4. **Field-by-field mapping** — three subsections, one per legacy section type, each a markdown table of (Legacy key, New JSON path, Migrated automatically, Notes):

   **`[ServerSettings]`**

   | Legacy key | New JSON path | Auto-migrated? | Notes |
   |---|---|---|---|
   | `Server` | `FrigateMqtt:Server` | yes | MQTT broker hostname |
   | `BlueIrisImages` | `BlueIris:SnapshotUrlTemplate` | yes | Tool appends `/{camera}` token to the base URL |
   | (any unmapped key) | — | warning logged | Tool prints unknown-key warnings; operator copies manually |

   **`[PushoverSettings]`**

   | Legacy key | New JSON path | Auto-migrated? | Notes |
   |---|---|---|---|
   | `AppToken` | `Pushover:AppToken` | empty placeholder | **Operator MUST supply via `Pushover__AppToken` env var per CLAUDE.md** |
   | `UserKey` | `Pushover:UserKey` | empty placeholder | **Operator MUST supply via `Pushover__UserKey` env var** |
   | `NotifySleepTime` | `Subscriptions:N:CooldownSeconds` | yes (applied to ALL subscriptions) | Default 30s if absent |

   **`[SubscriptionSettings]`** (one block per subscription)

   | Legacy key | New JSON path | Auto-migrated? | Notes |
   |---|---|---|---|
   | `Name` | `Subscriptions:N:Name` | yes | |
   | `CameraName` | `Subscriptions:N:Camera` | yes | Frigate camera name (template token source) |
   | `ObjectName` | `Subscriptions:N:Label` | yes | e.g. `Person`, `Car` |
   | `Zone` | `Subscriptions:N:Zone` | yes | Optional zone filter |
   | `Camera` | — | **NO — see below** | Per-subscription BlueIris trigger URL |
   | `LocationName`, `CameraShortName` | — | dropped | Legacy ergonomics; not used by FrigateRelay |

5. **The `[SubscriptionSettings].Camera` field is intentionally NOT migrated** — explanatory subsection. Legacy carried per-subscription HTTP trigger URLs; FrigateRelay uses a global `BlueIris:TriggerUrlTemplate` with the `{camera}` token. Operator action required: edit the migrated `appsettings.Local.json` to set `BlueIris:TriggerUrlTemplate` once. Show before/after using RFC 5737 IPs:

   Legacy:
   ```ini
   [SubscriptionSettings]
   CameraName = DriveWayHD
   Camera = http://192.0.2.50:81/admin?trigger&camera=DriveWayHD&user=...&pw=...
   ```

   New (one-time edit by operator):
   ```jsonc
   "BlueIris": {
     "TriggerUrlTemplate": "http://192.0.2.50:81/admin?trigger&camera={camera}&user=...&pw=..."
   }
   ```

6. **Secrets** — explicit table of env var names:

   | Secret | Env var | Notes |
   |---|---|---|
   | Pushover app token | `Pushover__AppToken` | double underscore = `:` per IConfiguration env-var convention |
   | Pushover user key | `Pushover__UserKey` | |
   | BlueIris HTTP creds | embedded in `BlueIris:TriggerUrlTemplate` (URL-encoded) | Or use `appsettings.Local.json` (gitignored) |

7. **Validation gate** — final paragraph: after migration, the operator runs `dotnet run --project src/FrigateRelay.Host -c Release` (with env vars set). The host's startup validation (`StartupValidation.ValidateAll`) reports every misconfiguration in one aggregated error message (CLAUDE.md collect-all convention) — operator fixes all at once, restarts.

8. **Cross-references**:
   - Tool source: `tools/FrigateRelay.MigrateConf/`
   - Fixture example: `tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf`
   - Phase 8 size-parity gate: `tests/FrigateRelay.Host.Tests/Configuration/ConfigSizeParityTest.cs`
   - Reconciliation doc: `docs/parity-report.md` (added in Wave 3 PLAN-3.1)
   - Operator parity-window checklist: `docs/parity-window-checklist.md` (Wave 2 PLAN-2.1)

**Acceptance Criteria:**
- `test -f docs/migration-from-frigatemqttprocessing.md`
- `wc -l docs/migration-from-frigatemqttprocessing.md` is at least 80 lines (substantive content, not a stub).
- `grep -q '\[ServerSettings\]' docs/migration-from-frigatemqttprocessing.md`
- `grep -q '\[PushoverSettings\]' docs/migration-from-frigatemqttprocessing.md`
- `grep -q '\[SubscriptionSettings\]' docs/migration-from-frigatemqttprocessing.md`
- `grep -q 'tools/FrigateRelay.MigrateConf' docs/migration-from-frigatemqttprocessing.md`
- `grep -q 'Pushover__AppToken' docs/migration-from-frigatemqttprocessing.md`
- `grep -q 'BlueIris:TriggerUrlTemplate' docs/migration-from-frigatemqttprocessing.md`
- `grep -nE '192\.168\.|10\.0\.0\.' docs/migration-from-frigatemqttprocessing.md` returns zero matches (no RFC 1918).
- `grep -nE 'AppToken=[A-Za-z0-9]{20,}|UserKey=[A-Za-z0-9]{20,}' docs/migration-from-frigatemqttprocessing.md` returns zero matches.

## Verification

```bash
test -f docs/migration-from-frigatemqttprocessing.md
wc -l docs/migration-from-frigatemqttprocessing.md
grep -q '\[ServerSettings\]' docs/migration-from-frigatemqttprocessing.md
grep -q 'tools/FrigateRelay.MigrateConf' docs/migration-from-frigatemqttprocessing.md
grep -q 'Pushover__AppToken' docs/migration-from-frigatemqttprocessing.md
grep -q 'BlueIris:TriggerUrlTemplate' docs/migration-from-frigatemqttprocessing.md
grep -nE '192\.168\.|10\.0\.0\.' docs/migration-from-frigatemqttprocessing.md && exit 1 || true
.github/scripts/secret-scan.sh
```
