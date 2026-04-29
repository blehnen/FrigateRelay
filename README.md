# FrigateRelay

[![CI](https://github.com/blehnen/FrigateRelay/actions/workflows/ci.yml/badge.svg)](https://github.com/blehnen/FrigateRelay/actions/workflows/ci.yml)
[![codecov](https://codecov.io/gh/blehnen/FrigateRelay/branch/main/graph/badge.svg)](https://codecov.io/gh/blehnen/FrigateRelay)

FrigateRelay is a .NET 10 background service that consumes MQTT events from [Frigate NVR](https://frigate.video/) and fans them out to a pluggable set of notification and automation targets. Each action can have independent pre-action validators (e.g. CodeProject.AI confidence checks) that short-circuit only that action, leaving others unaffected. Plugins ship for [Blue Iris](https://blueirissoftware.com/) (trigger + snapshot), [Pushover](https://pushover.net/) (notification + snapshot), [CodeProject.AI](https://www.codeproject.com/ai/docs/) (validation), and Frigate snapshot.

## Quickstart (Docker)

**Prerequisites:** Docker with Compose plugin, a running Frigate NVR + MQTT broker.

```bash
git clone https://github.com/blehnen/FrigateRelay.git
cd frigaterelay
cp docker/.env.example .env
```

Edit `.env` and fill in your secrets:

```
BLUEIRIS__BASEURL=http://your-blueiris-host:81
BLUEIRIS__USERNAME=your-username
BLUEIRIS__PASSWORD=your-password
PUSHOVER__APITOKEN=your-api-token
PUSHOVER__USERKEY=your-user-key
```

See `SECURITY.md` — do not commit `.env` to source control.

Start the service:

```bash
docker compose -f docker/docker-compose.example.yml up
```

Verify readiness:

```bash
curl -i http://localhost:8080/healthz
# HTTP/1.1 200 OK  (or 503 if MQTT is unreachable — check FrigateMqtt__Server)
```

## Configuration

Configuration uses standard .NET `IConfiguration` layering:

```
appsettings.json  ←  environment variables  ←  user-secrets (dev)  ←  appsettings.Local.json
```

For Docker, override connection details via environment variables. The double-underscore convention maps to nested JSON keys:

```
FrigateMqtt__Server=mosquitto        # MQTT broker hostname (Docker service name)
FrigateMqtt__Port=1883
ASPNETCORE_ENVIRONMENT=Docker        # suppresses file-sink logging inside the container
```

A full example config lives at `config/appsettings.Example.json`. A minimal excerpt showing one profile and one subscription:

```json
{
  "Profiles": {
    "Standard": {
      "Actions": [
        { "Plugin": "BlueIris" },
        { "Plugin": "Pushover", "SnapshotProvider": "Frigate" }
      ]
    }
  },
  "Subscriptions": [
    {
      "Name": "Driveway Person",
      "Camera": "DriveWayHD",
      "Label": "Person",
      "Zone": "driveway_zone",
      "Profile": "Standard"
    }
  ]
}
```

**Key concepts:**

- **Profiles** — reusable action lists referenced by name from subscriptions. Define once, use across many subscriptions.
- **Subscriptions** — match events by camera name, object label, and optional zone. Each subscription uses a profile or declares its own inline action list.
- **SnapshotProvider override** — `"SnapshotProvider": "Frigate"` on an action overrides the subscription default. Resolution order: per-action → per-subscription → global `DefaultSnapshotProvider`.
- **Validators** — attach to specific action entries to gate that action independently.

## Migrating from FrigateMQTTProcessingService

If you are migrating from the author's earlier `FrigateMQTTProcessingService`
(.NET Framework 4.8 / Topshelf / SharpConfig INI) to FrigateRelay v1.0.0, the
project ships a one-shot conversion tool plus a field-by-field mapping doc:

- **Tool:** [`tools/FrigateRelay.MigrateConf/`](tools/FrigateRelay.MigrateConf/) — a .NET 10 console app that reads the legacy `.conf` and writes a FrigateRelay-shaped `appsettings.Local.json`.
  ```bash
  dotnet run --project tools/FrigateRelay.MigrateConf -c Release -- \
    --input /path/to/FrigateMQTTProcessingService.conf \
    --output appsettings.Local.json
  ```
- **Field-by-field mapping:** [`docs/migration-from-frigatemqttprocessing.md`](docs/migration-from-frigatemqttprocessing.md) — covers `[ServerSettings]`, `[PushoverSettings]`, `[SubscriptionSettings]` blocks; documents the secrets you must supply via env vars (`Pushover__AppToken`, `Pushover__UserKey`); explains the deliberately-dropped per-subscription `Camera` URL field.
- **Side-by-side parity window (recommended):** [`docs/parity-window-checklist.md`](docs/parity-window-checklist.md) — the 48-hour run book for verifying behavioral parity in DryRun mode before flipping to production.
- **Parity report:** [`docs/parity-report.md`](docs/parity-report.md) — the reconciliation output the operator reviews before declaring cutover.
- **Release procedure:** [`RELEASING.md`](RELEASING.md) — the manual `git tag v1.0.0` run book, including the pre-flight checklist and what `release.yml` does automatically after the tag push.

## Adding a new action plugin

Install the plugin scaffold template and generate a new project:

```bash
dotnet new install templates/FrigateRelay.Plugins.Template
dotnet new frigaterelay-plugin -n FrigateRelay.Plugins.MyPlugin -o src/FrigateRelay.Plugins.MyPlugin
```

See `docs/plugin-author-guide.md` for the full walkthrough — contract interfaces, options binding, registrar pattern, test setup, and wiring into the host.

## Project status

Pre-1.0; Phase 11 is the OSS-polish gate before v1.0.0 cutover. See `.shipyard/ROADMAP.md` for the build plan and `CHANGELOG.md` for history.

## License

[MIT](LICENSE) — Copyright 2026 Brian Lehnen.
