# Migrating from FrigateMQTTProcessingService

This document guides operators through migrating from the legacy `FrigateMQTTProcessingService`
(`.NET Framework 4.8`, SharpConfig INI config) to `FrigateRelay` (`.NET 10`, `IConfiguration`
JSON layering). The legacy service used a single `FrigateMQTTProcessingService.conf` file with
three INI section types (`[ServerSettings]`, `[PushoverSettings]`, `[SubscriptionSettings]`).
FrigateRelay uses `appsettings.json` with a Profiles + Subscriptions shape (see `config/appsettings.Example.json`).
The `FrigateRelay.MigrateConf` tool (source: `tools/FrigateRelay.MigrateConf/`) automates the
structural conversion. Secrets must be supplied via environment variables — they are never written
to committed files.

## Prerequisites

Before running the migration tool you need:

- The legacy `.conf` file readable on disk (e.g. `FrigateMQTTProcessingService.conf`).
- `.NET 10 SDK` installed locally. Phase 12 ships source-built only; no standalone binary is
  distributed yet.
- Your BlueIris HTTP trigger URL template. The tool cannot auto-detect the parameterized form from
  per-subscription URLs — you must identify the base template and supply it manually after
  migration (see [The `Camera` field is not migrated](#the-camera-field-is-not-migrated) below).

## Running the tool

```bash
dotnet run --project tools/FrigateRelay.MigrateConf -c Release -- \
  --input /path/to/FrigateMQTTProcessingService.conf \
  --output /path/to/appsettings.Local.json
```

The default output filename is `appsettings.Local.json`. This file is `.gitignore`'d — secrets
written here are not committed. The committed `appsettings.json` carries no secrets per project
policy.

After the tool completes, open the generated `appsettings.Local.json` and:

1. Supply `Pushover__AppToken` and `Pushover__UserKey` via environment variables (see [Secrets](#secrets)).
2. Set `BlueIris:TriggerUrlTemplate` manually (see below).
3. Run the host to trigger startup validation (see [Validation gate](#validation-gate)).

## Field-by-field mapping

### `[ServerSettings]`

| Legacy INI key | New JSON path | Auto-migrated? | Notes |
|---|---|---|---|
| `server` | `FrigateMqtt:Server` | yes | MQTT broker hostname |
| `blueirisimages` | `BlueIris:SnapshotUrlTemplate` | yes | Tool appends `/{camera}` token to the base URL to form the template |
| `frigateapi` | `FrigateApi:BaseUrl` | yes | Frigate HTTP API base URL |
| *(any unrecognised key)* | — | warning logged | Tool prints unknown-key warnings to stderr; copy manually |

The legacy `[ServerSettings]` section appears exactly once. All keys are case-insensitive
(SharpConfig convention).

### `[PushoverSettings]`

| Legacy INI key | New JSON path | Auto-migrated? | Notes |
|---|---|---|---|
| `AppToken` | `Pushover:AppToken` | empty placeholder | **Must be supplied via `Pushover__AppToken` env var — never committed** |
| `UserKey` | `Pushover:UserKey` | empty placeholder | **Must be supplied via `Pushover__UserKey` env var** |
| `NotifySleepTime` | `Subscriptions:N:CooldownSeconds` | yes — applied to ALL subscriptions | Defaults to `30` seconds if absent in the INI |

The legacy `[PushoverSettings]` section appears exactly once. `NotifySleepTime` is a global
cooldown in the legacy service; the migrated output copies the value to every subscription entry
since FrigateRelay configures cooldown per subscription.

### `[SubscriptionSettings]`

The legacy service supports multiple `[SubscriptionSettings]` sections in sequence (SharpConfig
section-list pattern). Each section maps to one element in the `Subscriptions` array.

| Legacy INI key | New JSON path | Auto-migrated? | Notes |
|---|---|---|---|
| `Name` | `Subscriptions:N:Name` | yes | Human-readable subscription label used in logs |
| `CameraName` | `Subscriptions:N:Camera` | yes | Frigate camera name; used as the `{camera}` token source |
| `ObjectName` | `Subscriptions:N:Label` | yes | Detection label, e.g. `Person`, `Car` |
| `Zone` | `Subscriptions:N:Zone` | yes | Optional zone filter; omitted from output if absent or empty |
| `Camera` | — | **NO — see below** | Per-subscription BlueIris HTTP trigger URL (intentionally dropped) |
| `LocationName` | — | dropped | Display-only field; not used by FrigateRelay |
| `CameraShortName` | — | dropped | Display-only alias; not used by FrigateRelay |

The 1-to-1 / 9-to-9-element conversion: a single `[SubscriptionSettings]` block produces one
`Subscriptions` array element; nine blocks produce a nine-element array. The array index `N`
corresponds to declaration order in the INI file.

## The `Camera` field is not migrated

> **Action required from operator.**

In the legacy service, each `[SubscriptionSettings]` block carried a per-subscription `Camera`
key containing the full BlueIris HTTP trigger URL, including camera name, credentials, and any
query parameters:

```ini
[SubscriptionSettings]
CameraName = driveway
Camera = http://192.0.2.53:81/admin?trigger&camera=DriveWayHD&user=admin&pw=secret
```

FrigateRelay uses a single global `BlueIris:TriggerUrlTemplate` with a `{camera}` token. The
token is replaced at runtime with the `Subscriptions:N:Camera` value for each matching
subscription. This means you configure the URL once instead of once per subscription.

After migration, open `appsettings.Local.json` (or `appsettings.json` for non-secret portions)
and add or edit the `BlueIris` section:

```jsonc
"BlueIris": {
  "TriggerUrlTemplate": "http://192.0.2.53:81/admin?trigger&camera={camera}&user=admin&pw=secret",
  "SnapshotUrlTemplate": "http://192.0.2.53:81/image/{camera}"
}
```

Replace `192.0.2.53`, `admin`, and `secret` with your actual BlueIris host and credentials.
Keep credentials in `appsettings.Local.json` (gitignored) rather than `appsettings.json`.

The `{camera}` token in `TriggerUrlTemplate` is replaced with the `Camera` field of each
matching subscription at dispatch time (e.g. subscription with `Camera: driveway` triggers
`http://192.0.2.53:81/admin?trigger&camera=driveway&...`).

Note: in the legacy fixture the `Camera` query parameter value (`DriveWayHD`) comes from the
`CameraShortName` field, which is distinct from `CameraName` (`driveway`). FrigateRelay uses
`Subscriptions:N:Camera` (the Frigate camera name) as the token value. If your BlueIris camera
names differ from your Frigate camera names, you may need to set `Camera` in each subscription to
the BlueIris camera name rather than the Frigate camera name, or configure the template
accordingly.

## Secrets

The following values are secrets and must never appear in committed files.

| Secret | Environment variable | Notes |
|---|---|---|
| Pushover app token | `Pushover__AppToken` | Double underscore maps to `:` per `IConfiguration` env-var convention |
| Pushover user key | `Pushover__UserKey` | Same convention |
| BlueIris HTTP credentials | Embedded in `BlueIris:TriggerUrlTemplate` | Store the full template (including credentials) in `appsettings.Local.json` (gitignored), or use `BlueIris__TriggerUrlTemplate` env var |

To set env vars in a shell session:

```bash
export Pushover__AppToken=<your-app-token>
export Pushover__UserKey=<your-user-key>
```

In Docker, pass them via `--env` or the `environment:` block in `docker-compose.yml`.

The tool writes empty string placeholders for `Pushover:AppToken` and `Pushover:UserKey` in the
output JSON. The host's `IConfiguration` layering merges the env vars at startup and the
placeholders are overridden.

## Validation gate

After migration and before deploying, run the host locally:

```bash
dotnet run --project src/FrigateRelay.Host -c Release
```

The host's startup validation (`StartupValidation.ValidateAll`) runs before any MQTT connection
is opened. It collects every misconfiguration — undefined profile references, unknown plugin
names, missing required fields — and reports them all in a single aggregated error message.
Operators see every problem at once rather than discovering them one restart at a time.

Fix all reported issues, then restart. A clean startup (no error thrown) means the configuration
is structurally valid.

## Cross-references

- Migration tool source: `tools/FrigateRelay.MigrateConf/`
- Legacy INI fixture (sanitized, operator-provided): `tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf`
- Size-parity gate (JSON must be ≤ 60% of INI char count): `tests/FrigateRelay.Host.Tests/Configuration/ConfigSizeParityTest.cs`
- Example output shape: `config/appsettings.Example.json`
- Parity report (Wave 3): `docs/parity-report.md`
- Operator parity-window checklist (Wave 2): `docs/parity-window-checklist.md`
