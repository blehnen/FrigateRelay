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
BLUEIRIS__TRIGGERURLTEMPLATE=http://your-blueiris-host:81/admin?camera={camera_shortname}&trigger=1
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
- **Name validation (v1.2.1+):** subscription names, profile keys, plugin names, and validator instance keys must match `^[A-Za-z0-9_. -]+$` — alphanumerics, space, dot, dash, and underscore are accepted. Slashes, colons, at-signs, control characters, and CRLF are rejected at startup with a structured diagnostic. Spaced names like `"DriveWay Person"` and `"Front Door"` continue to bind cleanly. Empty or whitespace-only plugin names also fail fast at the converter boundary with a `FormatException` — supply a non-empty name in either the string-shorthand (`"BlueIris"`) or object form (`{"Plugin": "BlueIris"}`).

## Observability

FrigateRelay emits OpenTelemetry metrics and traces under `Meter "FrigateRelay"` and `ActivitySource "FrigateRelay"`, plus Serilog logs to console and (optionally) Seq. The 10 counters carry structured tags (camera, subscription, action, etc.) bounded by your Frigate + subscription config.

See [docs/observability.md](docs/observability.md) for the full counter inventory, cardinality guidance, OTLP collector setup, and Grafana dashboard import. A reference compose stack lives in [docker/observability/](docker/observability/).

- **Bounding camera-tag cardinality (v1.3.0+):** populate `Otel:MetricsTags:KnownCameras: string[]` to fold any unknown camera value to the literal `"other"` before it reaches the metrics SDK — defends against attacker-influenced or misconfigured camera names that would otherwise create unbounded time series. Default is empty (passthrough). Case-insensitive (`OrdinalIgnoreCase`). See [docs/observability.md#bounding-camera-tag-cardinality-otelmetricstagsknowncameras-v130](docs/observability.md#bounding-camera-tag-cardinality-otelmetricstagsknowncameras-v130) for the full config shape and rationale.

## Validator engine status

FrigateRelay ships three validator plugins as of v1.2.0: **CodeProject.AI** (also serves [Blue Onyx](https://github.com/xnorpx/blue-onyx) — same wire format), **Roboflow Inference**, and **DOODS2**. Multiple validators can be attached per action, and v1.2.0 adds an opt-in `ParallelValidators: true` flag that runs them concurrently with strict-AND aggregation (see CHANGELOG for details).

**About CodeProject.AI:** active upstream development has stopped, but the plugin is **not deprecated** — the request shape (`POST /v1/vision/detection`) is also the API exposed by Blue Onyx, so existing CPAI users and Blue Onyx users both depend on it. The deprecation concerns the upstream CodeProject.AI *service*, not the plugin contract.

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
- **Roboflow Inference** (added v1.2.0, #13) — self-hosted [Roboflow Inference server](https://github.com/roboflow/inference) with any Roboflow-hosted or custom-trained model (e.g. RF-DETR). One validator instance per model — declare multiple instances to gate different actions on different models.
  ```jsonc
  "Validators": {
    "roboflow_persons": {
      "Type": "Roboflow",
      "BaseUrl": "http://roboflow-host:9001",
      "ModelId": "rfdetr-base/1",       // include the version suffix
      "MinConfidence": 0.5,
      "AllowedLabels": ["person"],
      "OnError": "FailClosed"
    }
  }
  ```
- **DOODS2** (added v1.2.0, #14) — self-hosted [DOODS2 v2](https://github.com/snowzach/doods2) detector hub (TFLite / TensorFlow / PyTorch backends, all with the COCO 80-label set). HTTP-only — gRPC was dropped upstream in the v2 Python rewrite.
  ```jsonc
  "Validators": {
    "doods2_driveway": {
      "Type": "Doods2",
      "BaseUrl": "http://doods2-host:8080",
      "DetectorName": "default",        // "default" | "tensorflow" | "pytorch"
      "MinConfidence": 0.5,
      "AllowedLabels": ["person", "car"],
      "OnError": "FailClosed"
    }
  }
  ```

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

`v1.2.0` is the current release — adds the Roboflow Inference (#13) and DOODS2 (#14) validator plugins listed under "Validator engine status" above, the per-action `ParallelValidators: true` opt-in (#23), and a `ChannelActionDispatcher` graceful-shutdown hardening fix. See [`CHANGELOG.md`](CHANGELOG.md) for the full per-version history (`v1.0.0` → `v1.0.3` → `v1.1.0` → `v1.2.0`) and the [releases page](https://github.com/blehnen/FrigateRelay/releases) for tagged artifacts. Multi-arch images are published to `ghcr.io/blehnen/frigaterelay:<semver>`, `:<major>`, and `:latest`.

## License

[MIT](LICENSE). Copyright 2026 Brian Lehnen.
