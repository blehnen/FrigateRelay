<p align="center">
  <img src="assets/logo.png" alt="FrigateRelay logo" width="128" height="128">
</p>

<h1 align="center">FrigateRelay</h1>

<p align="center">
  <a href="https://github.com/blehnen/FrigateRelay/actions/workflows/ci.yml"><img src="https://github.com/blehnen/FrigateRelay/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <a href="https://codecov.io/gh/blehnen/FrigateRelay"><img src="https://codecov.io/gh/blehnen/FrigateRelay/branch/main/graph/badge.svg" alt="codecov"></a>
</p>

FrigateRelay is a .NET 10 background service that listens to [Frigate NVR](https://frigate.video/) MQTT events and dispatches them to action plugins. Each action can sit behind its own pre-action validators, like a CodeProject.AI confidence check. If a validator fails, that action is skipped, but other actions on the same event still fire.

Plugins ship for [Blue Iris](https://blueirissoftware.com/) (trigger + snapshot), [Pushover](https://pushover.net/) (notification + snapshot), [CodeProject.AI](https://www.codeproject.com/ai/docs/) (validation), and Frigate snapshot.

## Quickstart (Docker)

**Prerequisites:** Docker with Compose plugin, a running Frigate NVR + MQTT broker.

```bash
git clone https://github.com/blehnen/FrigateRelay.git
cd frigaterelay
cp docker/.env.example .env
```

Edit `.env` and fill in your secrets:

```
BLUEIRIS__TRIGGERURLTEMPLATE=http://your-blueiris-host:81/admin?camera={camera}&trigger=1
PUSHOVER__APPTOKEN=your-app-token
PUSHOVER__USERKEY=your-user-key
```

Blue Iris auth is expressed inside the trigger URL template — either by IP-whitelisting the FrigateRelay container on the Blue Iris side (preferred; the template needs no credentials), or by appending `&user=<USER>&pw=<PW>` to the template. See `docker/.env.example` for the fully annotated example, including optional Frigate-snapshot and CodeProject.AI validator vars.

See `SECURITY.md`. Do not commit `.env` to source control.

Start the service:

```bash
docker compose -f docker/docker-compose.example.yml up
```

Verify readiness:

```bash
curl -i http://localhost:8080/healthz
# HTTP/1.1 200 OK  (or 503 if MQTT is unreachable; check FrigateMqtt__Server)
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

- **Profiles** are reusable action lists, referenced by name from subscriptions. Define once, use across many subscriptions.
- **Subscriptions** match events by camera name, object label, and optional zone. Each subscription uses a profile or declares its own inline action list.
- **`CameraShortName` (optional, per subscription):** an alternate camera identifier surfaced through the `{camera_shortname}` URL-template token. Use when the downstream system (typically Blue Iris) names the camera differently from the originating source — e.g. Frigate id `"driveway"` vs Blue Iris shortname `"DriveWayHD"`. Without it, BI's HTTP trigger API returns 200 OK on the wrong name but silently does nothing.
- **SnapshotProvider override:** `"SnapshotProvider": "Frigate"` on an action overrides the subscription default. Resolution order: per-action → per-subscription → global `DefaultSnapshotProvider`.
- **Validators** attach to specific action entries and gate that action independently.

## Validator engine status

FrigateRelay ships with one validator plugin today — **CodeProject.AI** — but **active CodeProject.AI development has stopped upstream**. The plugin's request shape (`POST /v1/vision/detection`) is also the API exposed by **[Blue Onyx](https://github.com/xnorpx/blue-onyx)**, so the existing plugin is the supported path to either backend.

### Supported backends (verified)

- **CodeProject.AI** — the historical default. Existing installs (current and older versions) keep working with no FrigateRelay change.
- **Blue Onyx** — verified working through the existing `FrigateRelay.Plugins.CodeProjectAi` plugin with **no code change**, only a config swap. Point your validator's `BaseUrl` at the Blue Onyx host and port:
  ```jsonc
  "Validators": {
    "Person": {
      "Type": "CodeProjectAi",          // plugin type — same plugin, different backend
      "BaseUrl": "http://blueonyx-host:32168",
      "MinConfidence": 0.5,
      "OnError": "FailClosed"
    }
  }
  ```
  **Performance caveat:** Blue Onyx supports NVIDIA GPU acceleration **only via its Windows EXE/service distribution**. The Docker image is CPU-only — slower than CPAI's CUDA-enabled Docker image on the same hardware. Acceptable for event-driven validation in the FrigateRelay pipeline (sub-real-time, one inference per matched event); operators with high event rates and no Windows GPU host should benchmark before swapping.

### Future backends (v1.1 roadmap)

- **Roboflow Inference / RF-DETR** (#13) — different detection model architecture; needs its own plugin.
- **DOODS2** (#14) — TFLite / TensorFlow / YOLOv5 detector hub; needs its own plugin.

The CPAI plugin is **not marked obsolete** — both CPAI users and Blue Onyx users depend on it. The deprecation is about the upstream CodeProject.AI *service*, not the plugin contract or the API shape.

## Migrating from FrigateMQTTProcessingService

If you are migrating from the author's earlier `FrigateMQTTProcessingService`
(.NET Framework 4.8 / Topshelf / SharpConfig INI) to FrigateRelay v1.0.0, the
project ships a one-shot conversion tool plus a field-by-field mapping doc:

- **Tool:** [`tools/FrigateRelay.MigrateConf/`](tools/FrigateRelay.MigrateConf/), a .NET 10 console app that reads the legacy `.conf` and writes a FrigateRelay-shaped `appsettings.Local.json`.
  ```bash
  dotnet run --project tools/FrigateRelay.MigrateConf -c Release -- \
    --input /path/to/FrigateMQTTProcessingService.conf \
    --output appsettings.Local.json
  ```
- **Field-by-field mapping:** [`docs/migration-from-frigatemqttprocessing.md`](docs/migration-from-frigatemqttprocessing.md). Covers `[ServerSettings]`, `[PushoverSettings]`, and `[SubscriptionSettings]`; documents the secrets you must supply via env vars (`Pushover__AppToken`, `Pushover__UserKey`); explains the deliberately-dropped per-subscription `Camera` URL field.
- **Side-by-side parity window (recommended):** [`docs/parity-window-checklist.md`](docs/parity-window-checklist.md), the 48-hour run book for verifying behavioral parity in DryRun mode before flipping to production.
- **Parity report:** [`docs/parity-report.md`](docs/parity-report.md), the reconciliation output the operator reviews before declaring cutover.
- **Release procedure:** [`RELEASING.md`](RELEASING.md), the manual `git tag v1.0.0` run book, including the pre-flight checklist and what `release.yml` does automatically after the tag push.

## Adding a new action plugin

Install the plugin scaffold template and generate a new project:

```bash
dotnet new install templates/FrigateRelay.Plugins.Template
dotnet new frigaterelay-plugin -n FrigateRelay.Plugins.MyPlugin -o src/FrigateRelay.Plugins.MyPlugin
```

See `docs/plugin-author-guide.md` for the full walkthrough: contract interfaces, options binding, the registrar pattern, test setup, and wiring into the host.

## Project status

`v1.0.0` shipped 2026-05-03 — see [`CHANGELOG.md`](CHANGELOG.md) and the [release page](https://github.com/blehnen/FrigateRelay/releases/tag/v1.0.0). Multi-arch images are published to `ghcr.io/blehnen/frigaterelay:1.0.0`, `:1`, and `:latest`. v1.0.1 is in progress (operator-reported bugs + docs cleanup); the v1.1 plan adds the alternative validator plugins listed under "Validator engine status" above.

## License

[MIT](LICENSE). Copyright 2026 Brian Lehnen.
