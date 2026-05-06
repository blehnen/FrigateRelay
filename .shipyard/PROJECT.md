# FrigateRelay

## Description

FrigateRelay is a .NET 10 background service that consumes MQTT events from [Frigate NVR](https://frigate.video/) and fans them out to a pluggable set of notification and automation targets — with optional pre-action validators (e.g. CodeProject.AI confidence checks) that can short-circuit per action.

The existence case: an open-source Frigate → [Blue Iris](https://blueirissoftware.com/) bridge. Existing alternatives in this space (notably [`0x2142/frigate-notify`](https://github.com/0x2142/frigate-notify)) are good at sending notifications but do not trigger Blue Iris cameras. FrigateRelay makes Blue Iris a first-class action plugin **and** a first-class snapshot source, and treats notifications, snapshots, and validators as symmetric plugins under one host.

The project supersedes the author's existing personal `FrigateMQTTProcessingService` (.NET Framework 4.8 / Topshelf / DotNetWorkQueue) — same functional behavior, modernized runtime, cleaner architecture, Docker-first deployment, open-source-ready code.

## Goals

1. Relay Frigate MQTT `frigate/events` to configurable downstream actions without losing any of the existing author's production feature set (Blue Iris trigger + Pushover with Blue Iris snapshot).
2. Ship a clean, readable, reviewable OSS codebase that's comfortable to contribute to — not a personal-use script with the serial numbers filed off.
3. Define a plugin contract today that a future runtime-DLL-load discovery path can be layered onto **without rewriting plugins** (ship A, design for B).
4. Support both Frigate and Blue Iris as snapshot sources, with per-action override.
5. Enable CodeProject.AI-style validators that gate *specific* actions independently — not a global all-or-nothing gate.
6. Ship as a Docker image via GitHub Actions, with a clean `docker pull` install path and multi-arch support (linux/amd64 + linux/arm64).

## Non-Goals (v1)

- Persistent / durable event queue. Events are ephemeral; restarts may drop in-flight items. A durable dispatcher is a future phase (behind the `IActionDispatcher` seam).
- Runtime third-party plugin discovery (the DLL-drop / `AssemblyLoadContext` path). The *contract* is designed to accommodate it; the *loader* is not shipped in v1.
- Hot-reloadable config. Restart on config change.
- Web UI / dashboard. Configuration is declarative JSON/env-vars.
- Multi-input ingestion. Only Frigate MQTT in v1; the `IEventSource` abstraction supports future sources without breaking the plugin contract.
- Cloud-managed deploy (Kubernetes Helm charts, Terraform, operators). `docker run` + `docker-compose` reference is sufficient.
- Publishing NuGet packages for the abstractions. They live in the repo; promotion to NuGet is a later phase.
- Windows Service / Topshelf hosting. Docker-first. `sc.exe` on a self-contained binary is a user option but not an officially supported path.

## Requirements

### Functional — Host pipeline

- Subscribe to an MQTT broker with configurable host/port/client-id/topic. Default topic `frigate/events`.
- Deserialize Frigate events and project them into a source-agnostic `EventContext` (camera, label, zone, event id, start time, raw-payload accessor, snapshot-fetcher delegate).
- Match each event against configured subscriptions on camera name, object label, and optional zone (zone empty ⇒ match any zone).
- Dedupe matched events with a per-subscription cooldown keyed by camera + object using an in-memory cache.
- Dispatch matching events to the subscription's ordered action list.
- Each action independently evaluates its attached validators; a failing validator short-circuits **that action only**. Other actions in the same subscription fire normally.
- Graceful startup/shutdown with proper `IHostedService` / `CancellationToken` propagation through channels and HTTP handlers.

### Functional — Configuration

- Standard .NET `IConfiguration` layering: `appsettings.json` ← env vars ← user-secrets (dev) ← optional `appsettings.Local.json` override.
- Subscriptions resolve action lists via named `Profiles` to eliminate the 9×-repetition the author's production INI has today. A subscription may use a profile by name *or* declare its own ad-hoc action list.
- Plugin configuration (base URLs, secrets, default behavior) is **separate** from subscription configuration (which camera, which Pushover location label).
- Secrets never appear in a committed `appsettings.json`. All secret fields accept `""` as default and must be overridden via env vars or secret files at deploy time.
- Validators attach to specific action entries — not to subscriptions globally.
- Snapshot provider resolution: per-action override → per-subscription default → global `DefaultSnapshotProvider`.

### Functional — Plugins shipped in v1

- **Event source:** `FrigateMqttEventSource` implements `IEventSource`.
- **Action plugins:**
  - `BlueIris` — HTTP GET to a configured trigger URL with per-subscription camera name.
  - `Pushover` — multipart POST to `api.pushover.net/1/messages.json` with attached snapshot image.
- **Validator plugin:**
  - `CodeProjectAi` — POSTs a snapshot to a CodeProject.AI instance, returns pass/fail based on configured labels + minimum confidence + optional zone-of-interest bbox filter.
- **Snapshot providers:**
  - `BlueIrisSnapshot` — current-frame fetch from Blue Iris `/image/<CameraShortName>`.
  - `FrigateSnapshot` — Frigate API `/api/events/<id>/snapshot.jpg` (configurable `bbox` overlay) or `/thumbnail.jpg`.

### Functional — Extensibility contract

- Abstractions (`IEventSource`, `IActionPlugin`, `IValidationPlugin`, `ISnapshotProvider`, `EventContext`, `Verdict`, `PluginRegistrationContext`) live in a dedicated `FrigateRelay.Abstractions` project.
- Host depends only on abstractions + DI composition; **never** on concrete plugin types.
- Each plugin project exposes an `IPluginRegistrar` (or equivalent) that registers its services into the host's DI container.
- The host's plugin registration pipeline is factored so a future `FrigateRelay.Host.PluginLoader` (using `AssemblyLoadContext` against a `plugins/` folder) is **additive** — no rewrites, no breaking changes to plugin authors.

## Non-Functional Requirements

- **Runtime:** .NET 10. SDK-style projects. Nullable reference types enabled. C# language version latest. Warnings-as-errors for the shipped build.
- **Dependencies:** Microsoft.Extensions.* wherever possible. Third-party only for domain-specific needs: `MQTTnet` v5, OpenTelemetry exporters. **Explicitly excluded:** DotNetWorkQueue, App.Metrics, OpenTracing.
- **Reliability:** `System.Threading.Channels.Channel<T>` for per-action FIFO buffering. `Microsoft.Extensions.Resilience` (Polly v8+) for retry policies (3/6/9s defaults matching existing behavior). An `IActionDispatcher` abstraction exists so a durable dispatcher (P3 escape hatch — DotNetWorkQueue-backed, SQLite, etc.) can be added later without touching action plugins.
- **Observability:**
  - Logs: `Microsoft.Extensions.Logging` structured logging + Serilog sinks (console, rolling file, optional Seq).
  - Metrics + traces: OpenTelemetry with OTLP exporter, endpoint configurable via standard OTel env vars. Drop OpenTracing and App.Metrics.
  - No inline `Pushover.cs`-style `_logger.Error(ex.Message, ex)` anti-pattern — fixed in the new code.
- **Testing:** MSTest v3 with Microsoft.Testing.Platform runner. Primary assertions via MSTest `Assert`. FluentAssertions pinned to **6.12.2** (pre-commercial, Apache-2.0-licensed) available for readability-heavy tests. NSubstitute for mocks. Testcontainers.NET for integration tests that need a real Mosquitto broker.
- **Security:**
  - No committed secrets. CI has a `git grep` check that fails the build on known secret patterns.
  - **No global** `ServicePointManager.ServerCertificateValidationCallback` bypass. TLS-skipping must be opt-in per-plugin, scoped to a plugin-owned `HttpClient` via `SocketsHttpHandler.SslOptions.RemoteCertificateValidationCallback`, and gated by an explicit config flag (`AllowInvalidCertificates: true`).
  - No hard-coded IPs or hostnames anywhere in source — including commented-out code paths.
- **Performance:** Workload is small (dozens of events/minute at most). No explicit perf targets beyond "no memory leaks, no unbounded channel growth, GC allocations per event in the low-KB range."
- **Packaging:** Docker image is the primary distribution artifact. Base image: `mcr.microsoft.com/dotnet/runtime-deps` (Alpine or Debian-slim — locked in during the plan phase). Self-contained publish for small image size. Multi-arch via `docker buildx`.
- **CI:** GitHub Actions, patterned on the author's `DotNetWorkQueue` workflows. Runs on Ubuntu + Windows. Builds, tests, uploads coverage. Release tag triggers a multi-arch Docker build and push to GHCR (`ghcr.io/<owner>/frigaterelay`).

## Success Criteria

1. The author can replace their current `FrigateMQTTProcessingService` production deployment with a FrigateRelay Docker container and observe identical real-world behavior — same alerts fire, same cooldowns apply, same images attached.
2. The `appsettings.json` for the author's 9-subscription setup is meaningfully shorter than the existing INI (target: ≤ 60% the character count of today's `FrigateMQTTProcessingService.conf`).
3. An MSTest suite runs green in GitHub Actions CI on every push. Integration tests cover MQTT event round-trip through the full pipeline.
4. A Docker image is built, tagged, and pushed to GHCR on every release tag. Multi-arch (amd64 + arm64).
5. A reader unfamiliar with the project can read `README.md`, scaffold a new action plugin from a documented template, and see their plugin handling events locally in under one hour.
6. No secrets exist in the committed repo. `git grep -E 'AppToken|UserKey|api[a-z0-9]{20,}'` returns zero matches across all branches.
7. No global TLS-validation bypass anywhere in the codebase. Per-plugin opt-in only.

## Constraints

- **Timeline:** No external deadline. Hobby-scale; phases are sized for single-sitting focus (~2–6 hours each).
- **Budget:** Zero. No paid services required. All tooling must have free tiers or be fully self-hostable.
- **Target framework:** .NET 10 only. No multi-targeting, no polyfills, no legacy compatibility.
- **OS target:** Linux-first (Docker). Dev loop green on Windows; runtime on Linux. Windows Service hosting is explicitly not a supported scenario.
- **License:** MIT. All runtime dependencies must be MIT-compatible. No GPL, no commercial-only packages (the reason FluentAssertions is pinned to 6.12.2 and not upgraded to 8.x).
- **Competing landscape:** `0x2142/frigate-notify` (Go) is the nearest peer project. FrigateRelay differentiates on: (a) first-class Blue Iris support as an action *and* a snapshot source; (b) per-action validators; (c) pluggable snapshot providers.
- **Supersedes:** `FrigateMQTTProcessingService` (this repo's current code) is the reference implementation, not a dependency. FrigateRelay is a greenfield rewrite; no code is shared, though behavior must match.

## Architecture summary (digested from brainstorming)

| Decision | Choice | Escape hatch |
|---|---|---|
| Plugin loading | **A**: build-time project refs + DI | **B**: `AssemblyLoadContext` loader as a future project — contract ready |
| Validator scope | **V3**: per-action validators | — |
| Config shape | **S2**: Profiles + subscriptions that reference them | — |
| Async pipeline | **P1**: `Channel<T>` + `Microsoft.Extensions.Resilience` | **P3**: `IActionDispatcher` seam for a future durable dispatcher |
| Input | **I2**: `IEventSource` abstraction, Frigate as sole v1 impl | Second input = new plugin project |
| Snapshots | `ISnapshotProvider` plugin, per-action override | — |
| Observability | Microsoft.Extensions.Logging + Serilog sinks; OpenTelemetry (OTLP) for metrics + traces | — |
| Tests | MSTest v3 + Assert + FluentAssertions 6.12.2 + NSubstitute + Testcontainers.NET | — |
| License | MIT | — |
| Packaging | Multi-arch Docker image via GitHub Actions → GHCR | — |

## Repo shape (target)

```
FrigateRelay/
├── src/
│   ├── FrigateRelay.Abstractions/         # public plugin contracts
│   ├── FrigateRelay.Host/                 # generic host, DI, pipeline, BackgroundService
│   ├── FrigateRelay.Sources.FrigateMqtt/  # IEventSource impl
│   ├── FrigateRelay.Plugins.BlueIris/     # IActionPlugin + ISnapshotProvider
│   ├── FrigateRelay.Plugins.Pushover/     # IActionPlugin
│   ├── FrigateRelay.Plugins.CodeProjectAi/# IValidationPlugin
│   └── FrigateRelay.Plugins.FrigateSnapshot/  # ISnapshotProvider
├── tests/
│   ├── FrigateRelay.Host.Tests/
│   ├── FrigateRelay.Plugins.*.Tests/
│   └── FrigateRelay.IntegrationTests/     # Testcontainers Mosquitto
├── docker/
│   ├── Dockerfile
│   └── docker-compose.example.yml
├── .github/workflows/
│   ├── build.yml     # patterned on DotNetWorkQueue workflows
│   └── release.yml   # multi-arch docker build + GHCR push
├── LICENSE           # MIT
├── README.md
└── FrigateRelay.sln
```

## Post-v1.0 Scope — v1.1 (observability + structural cleanup)

v1.0.x is GA (v1.0.0 shipped 2026-05-03; v1.0.3 patched the `{camera_shortname}` allowlist drift). v1.1 closes the structural follow-up to that P0 and ships a complete observability story so operators can build dashboards out of the box.

### Goals (v1.1)

1. **Tag the meters.** Every counter on `Meter "FrigateRelay"` carries the structured tags an operator needs to pivot a dashboard by camera, subscription, action, validator, or component. Aggregate-only counters become per-camera-actionable.
2. **Document and demonstrate observability end-to-end.** A new `docs/observability.md`, a working `docs/grafana/frigaterelay-dashboard.json`, and a `docker/observability/` reference stack so operators starting from nothing can stand up FrigateRelay → OTel Collector → Prometheus + Grafana with one `docker compose up`.
3. **Eliminate the BlueIris template / EventTokenTemplate allowlist drift** that caused the v1.0.2→v1.0.3 P0, by collapsing `BlueIrisUrlTemplate` into a thin wrapper around `EventTokenTemplate`.

### In scope (v1.1)

- **#34** — refactor `BlueIrisUrlTemplate` to delegate to `EventTokenTemplate`. Single source of truth for `AllowedTokens`. New test asserts `BlueIrisUrlTemplate.AllowedTokens.SetEquals(EventTokenTemplate.AllowedTokens)` so future drift fails CI.
- **#35** — add structured tags to all 10 counters in `DispatcherDiagnostics.cs` per the inventory in the issue (`subscription`, `camera`, `label`, `action`, `validator`, `reason`, `component` — never `event_id`). New `MeterListener`-based unit tests assert tag presence per counter. Per-counter XML doc-comments document the tag set + cardinality rule.
- **#36** — `docs/observability.md` (counter inventory, OTLP export config, end-to-end recipes, cardinality rules for plugin authors), `docs/grafana/frigaterelay-dashboard.json`, `docker/observability/` compose files (OTel Collector → Prometheus + Grafana, plus a Seq stack), `make verify-observability` target, README "Observability" section linking to the new doc, one line in `RELEASING.md` for pre-release manual smoke.

### Out of scope (deferred to v1.2)

- **#13** — Roboflow Inference validator (RF-DETR).
- **#14** — DOODS2 validator (TFLite / TF / YOLOv5 hub).
- **#23** — optional parallel validator execution with all-pass aggregation.

v1.2 narrative: more inference engines + the parallel-AND mode that uses them. #23 is structurally unblocked once #13/#14 ship the additional engines that make multi-engine smoke testing useful.

### v1.1 PR sequencing

Three sequential PRs against `main`:

1. **#35 first.** Tags must land before the dashboard panels can pivot by them; otherwise the v1.1 dashboard would only work at system-aggregate and need an immediate follow-up.
2. **#36 second.** Depends on #35's tags. Bundles the doc, the dashboard JSON, the compose files, and the README + RELEASING glue in one diff so reviewers see the full observability story together.
3. **#34 in any slot** — independent of the other two; can land before, between, or after the observability pair. Pure refactor, no behavior change.

CHANGELOG classifies #35 as additive (semver minor): metric names persist, only the series cardinality grows; aggregate Prometheus queries that don't filter on tags continue to return the same totals.

### v1.1 verification gates

- `dotnet build FrigateRelay.sln -c Release` zero warnings (warnings-as-errors, both Linux and Windows).
- All existing tests pass; new `MeterListener` tests assert tag inventory per counter; new `BlueIrisUrlTemplate` allowlist-equality test passes.
- `git grep '"event_id"' src/FrigateRelay.Host/Dispatch/` returns empty.
- `git grep -nE 'App\.Metrics|OpenTracing|Jaeger\.' src/` still empty (architectural invariant unchanged).
- `make verify-observability` succeeds on the maintainer's machine before tag push: `docker compose up` the reference stack, scrape FrigateRelay, assert at least one tagged counter sample lands in Prometheus, dashboard imports cleanly into vanilla Grafana.
- `RELEASING.md` updated with the new pre-release smoke step.

### v1.1 success criteria

- A new operator can go from zero to a working Grafana dashboard in under fifteen minutes following `docs/observability.md` + the `docker/observability/` stack.
- Adding a future token (e.g. `{score}`) requires editing `EventTokenTemplate.AllowedTokens` only — the BlueIris-side allowlist no longer exists.
- A future contributor adding a new counter to `DispatcherDiagnostics` cannot ship without choosing a tag set, because the per-counter doc-comment template makes it the obvious next field to fill in.

## Post-v1.1 Scope — v1.2 (more inference engines + parallel-AND validation)

v1.1.0 shipped 2026-05-04 with full observability tagging and the `BlueIrisUrlTemplate` consolidation; the post-release ID-29 hotfix (eviction-callback log staleness) is queued under `[Unreleased]` for the next tag. v1.2 is the deferred-from-v1.1 scope from #13/#14/#23: two additional self-hosted inference engines as `IValidationPlugin` implementations, plus a parallel-AND mode that makes multi-engine validation a first-class option.

### Goals (v1.2)

1. **Two new self-hosted validators.** Roboflow Inference (RF-DETR headline) and DOODS2 (TFLite / TF / YOLOv5 detector hub) ship as separate plugin projects following the existing `IValidationPlugin` + `IPluginRegistrar` pattern established by `FrigateRelay.Plugins.CodeProjectAi`.
2. **Parallel-AND validator execution as a per-action opt-in.** Today validators run sequentially per-action (decision V3). v1.2 adds a per-action `ParallelValidators: true` flag — when set, all validators in `ActionEntry.Validators` run concurrently and the aggregate decision is strict AND (every validator must `Verdict.Allow` for the action to fire). Default remains sequential for backward compatibility.
3. **Demonstrate the multi-engine story end-to-end.** Integration tests cover at least one action protected by ≥ 2 validators in parallel, proving the new mode operationally.

### In scope (v1.2)

- **#13 — Roboflow Inference validator** (`FrigateRelay.Plugins.Roboflow`).
  - Self-hosted Roboflow Inference only (e.g. `http://roboflow:9001`). No support for the Roboflow Hosted Cloud API in v1.2 — matches the project's infra-friendly stance.
  - Per-instance `ModelId` config (e.g. `rfdetr-base`); operators declare multiple validator instances (e.g. `roboflow_persons`, `roboflow_vehicles`) if they need different models per camera.
  - Same config shape as CPAI: `Validators:<name>:Type: "Roboflow"`, plus `BaseUrl`, `ModelId`, `MinConfidence`, `AllowedLabels`, `OnError`, `Timeout`.
  - Transport: HTTP via `HttpClient` (typed client per `IPluginRegistrar`); WireMock-driven unit tests; Testcontainers integration test if `roboflow/inference` image is available, otherwise WireMock-only with a documented manual-smoke recipe.

- **#14 — DOODS2 validator** (`FrigateRelay.Plugins.Doods2`).
  - **HTTP only** (CONTEXT-14 D4 reversed 2026-05-06). DOODS2 v2 (the Python rewrite at `snowzach/doods2`) deliberately dropped gRPC support per upstream README; the original-Go-server gRPC scope was reverted during PLAN-2.1 build. Operators on the legacy Go `snowzach/doods` server should use that project's gRPC client; this plugin targets v2.
  - HTTP path: `POST /detect` with base64-encoded image + JSON detections back. WireMock-driven unit tests.
  - Same `MinConfidence` / `AllowedLabels` / `OnError` / `Timeout` knobs as CPAI/Roboflow. Plus `DetectorName` (default/tensorflow/pytorch) per CONTEXT-14.

- **#23 — Per-action `ParallelValidators: true`** in `ActionEntry`.
  - Default `false`; sequential behavior unchanged for existing config.
  - When `true`: validators run concurrently via `Task.WhenAll`; each validator's own `Timeout` applies; aggregate fails closed if any validator times out (matching the existing per-validator `OnError: FailClosed` semantics — "parallel" changes scheduling, not failure semantics).
  - Aggregation: strict AND. Each rejecting validator still emits its own `validators.rejected` counter for per-validator dashboard visibility (no behavioral change to the counter tag matrix from v1.1).
  - First reject does **not** short-circuit other in-flight validators — operators get full per-validator visibility on every dispatch. Documented as a deliberate cost-of-information tradeoff (and intentionally simpler than the cancellation-token plumbing the alternative would require).

### Out of scope (deferred to v1.3+)

- **Vote-based aggregation.** v1.2's parallel mode is strict AND only. Future work could add `RequireVotes: N-of-M` for soft consensus.
- **Roboflow Hosted Cloud API.** Adds an auth surface and quota error handling v1.2 does not need.
- **Per-action validator config override.** All validators referenced by an `ActionEntry` use the validator instance's own config; no per-action `ModelId` override knob.
- **First-result-wins cancellation.** Considered and deliberately rejected — the cancellation plumbing is a non-trivial surface and the per-validator visibility benefit is real.

### v1.2 PR sequencing

Three sequential PRs against `main`:

1. **#13 first** — Roboflow Inference plugin. Smaller surface than #14 (HTTP-only); establishes the second-validator pattern that #14 follows.
2. **#14 second** — DOODS2 plugin (HTTP only — gRPC scope reverted; see #14 description above). Builds on #13's pattern; no new dep families.
3. **#23 last** — parallel-AND opt-in. Lands after both #13 and #14 are in so its integration tests can exercise three validator types (CPAI + Roboflow + DOODS2) in a single AND chain — proves the design holds beyond the toy CPAI-only case.

CHANGELOG classifies all three as additive (semver minor). #23's `ParallelValidators` defaults to `false`, so existing `ActionEntry` configs are unaffected.

### v1.2 verification gates

- `dotnet build FrigateRelay.sln -c Release` zero warnings (warnings-as-errors, both Linux and Windows). New plugin projects compile clean.
- No gRPC anywhere (D4 reversed): `git grep -nE 'Grpc\.' src/` returns empty across the entire repo; no csproj declares Grpc.* packages.
- All existing tests pass.
- New unit tests per validator: at minimum allow / reject / timeout / OnError-FailClosed / OnError-FailOpen / cancellation, driven by WireMock (HTTP-only — DOODS2 gRPC scope reverted).
- New integration test demonstrating ≥ 2 validators running in parallel under a single `ActionEntry` with `ParallelValidators: true`. WireMock or Testcontainers as available; the CPAI + Roboflow combination is the smallest meaningful coverage.
- `git grep -nE 'App\.Metrics|OpenTracing|Jaeger\.' src/` still empty (architectural invariant unchanged).
- `git grep -nE 'Grpc\.' src/` returns empty across the entire repo (D4 reversed; no gRPC anywhere).

### v1.2 success criteria

- An operator can declare `Validators: ["cpai", "roboflow", "doods2"]` with `ParallelValidators: true` on a single action and see all three validators contribute decisions in production logs and counters.
- Adding a hypothetical fourth validator follows the #13/#14 pattern: a new plugin project + `IPluginRegistrar` registration, no host changes required.
- Existing v1.0/v1.1 deployments upgrade to v1.2 with no config changes — sequential validation remains the default. Operators opting into parallel mode flip exactly one boolean per action.
