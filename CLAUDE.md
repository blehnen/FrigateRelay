# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Instructions
Always use Context7 MCP when I need library/API documentation or setup steps. Automatically resolve library IDs and retrieve docs without being asked.

## Project state

FrigateRelay is a **greenfield .NET 10 rewrite**, currently **pre-implementation**. Nothing but planning docs exists in-tree yet. Before writing or changing code, read:

- `.shipyard/PROJECT.md` — goals, non-goals, requirements, architecture decisions (authoritative).
- `.shipyard/ROADMAP.md` — 12-phase build order with per-phase deliverables and **verifiable** success criteria.
- `.shipyard/STATE.json` — current phase / position in the plan.
- `.shipyard/codebase/*.md` — these describe the **legacy `FrigateMQTTProcessingService`** (.NET Framework 4.8 / Topshelf / DotNetWorkQueue / SharpConfig INI). It is the **behavioral reference only** — no code, build system, or dependency is shared. Do not treat it as the current codebase.

The project is managed by the Shipyard workflow (`.shipyard/config.json`). Follow the roadmap phase currently in `STATE.json` — do not jump ahead.

## Target repo shape (from PROJECT.md)

```
src/
  FrigateRelay.Abstractions/            # IEventSource, IActionPlugin, IValidationPlugin,
                                        # ISnapshotProvider, EventContext, Verdict,
                                        # IPluginRegistrar, PluginRegistrationContext
  FrigateRelay.Host/                    # generic host, DI, dispatcher, BackgroundService
  FrigateRelay.Sources.FrigateMqtt/     # IEventSource impl
  FrigateRelay.Plugins.BlueIris/        # IActionPlugin + ISnapshotProvider (both)
  FrigateRelay.Plugins.Pushover/        # IActionPlugin
  FrigateRelay.Plugins.CodeProjectAi/   # IValidationPlugin
  FrigateRelay.Plugins.FrigateSnapshot/ # ISnapshotProvider
tests/                                  # per-project MSTest v3 suites + IntegrationTests
docker/                                 # Dockerfile + compose example
.github/workflows/                      # build.yml, release.yml (multi-arch GHCR)
```

## Commands

All commands assume `FrigateRelay.sln` at repo root (scaffolded in Phase 1).

```bash
# Build (warnings-as-errors; must be clean on Windows and Linux)
dotnet build FrigateRelay.sln -c Release

# Test (whole solution)
dotnet test -c Release --no-build

# Test a single project
dotnet test tests/FrigateRelay.Host.Tests -c Release

# Test a single test by name (MSTest v3 / Microsoft.Testing.Platform)
dotnet test tests/FrigateRelay.Host.Tests --filter "FullyQualifiedName~MqttToBlueIris_HappyPath"

# Run the host locally (expects appsettings.Local.json or env-var overrides for secrets)
dotnet run --project src/FrigateRelay.Host

# Docker build (Phase 10+)
docker build -f docker/Dockerfile .
docker compose -f docker/docker-compose.example.yml up
```

Integration tests use Testcontainers.NET — Docker must be running.

## Architecture invariants (non-negotiable)

These encode the PROJECT.md decisions. Violating any of them is a regression from the plan, not a stylistic choice.

- **.NET 10 only.** `Directory.Build.props` sets `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<LangVersion>latest</LangVersion>`. No multi-targeting, no polyfills.
- **Plugin contracts live in `FrigateRelay.Abstractions`.** The host depends on abstractions + DI only — **never** on concrete plugin types. Each plugin project exposes an `IPluginRegistrar` that registers its services. The abstractions assembly must not pull third-party runtime deps (only `Microsoft.Extensions.*`).
- **Plugin loading is build-time DI in v1 (decision A).** The contract must remain compatible with a future `AssemblyLoadContext`-based loader — additive, no rewrites.
- **Async pipeline is `Channel<T>` + `Microsoft.Extensions.Resilience` (Polly v8).** Retry defaults 3 / 6 / 9 seconds. An `IActionDispatcher` seam exists so a durable dispatcher can swap in later (P3 escape hatch).
- **No `.Result` / `.Wait()` in source.** `git grep -nE '\.(Result|Wait)\(' src/` must be empty.
- **TLS skipping is opt-in per-plugin only.** Scoped to a plugin-owned `HttpClient` via `SocketsHttpHandler.SslOptions.RemoteCertificateValidationCallback`, gated by an explicit `AllowInvalidCertificates: true` config flag. **Never** set a global `ServicePointManager.ServerCertificateValidationCallback`. `git grep ServicePointManager` must return zero matches in `src/`.
- **Validators are per-action, not global** (decision V3). A failing validator short-circuits **that action only** and emits a structured `validator_rejected` log with the action + validator name. Other actions in the same event fire independently.
- **Snapshot resolution order:** per-action override → per-subscription default → global `DefaultSnapshotProvider`. Keyed by provider name.
- **Config shape is Profiles + Subscriptions** (decision S2). Subscriptions reference a named profile **or** declare an inline action list. Startup must **fail fast** with a clear diagnostic on undefined-profile references or unknown plugin names.
- **Dedupe uses a scoped `IMemoryCache`**, keyed per-subscription on camera + object label. **Never** use `MemoryCache.Default` (the legacy code's mistake).
- **No secrets in committed `appsettings.json`.** Secret fields default to `""` and must be supplied via env vars or user-secrets. CI greps for `AppToken=…`, `UserKey=…`, api-key-shaped strings, and `192.168.x.x` — any match fails the build. Do not commit hard-coded IPs or hostnames, including in comments.
- **No hard-coded IPs/hostnames in source** — including in commented-out code. (The legacy code had `http://192.168.0.58:5001` in a commented block; that pattern is forbidden here.)
- **Observability stack is Microsoft.Extensions.Logging + Serilog sinks + OpenTelemetry (OTLP).** **Do not** introduce `App.Metrics`, `OpenTracing`, or `Jaeger.*` — they are explicitly excluded. `git grep -nE 'App\.Metrics|OpenTracing|Jaeger\.' src/` must be empty. Use `ILogger.LogError(ex, "message")` — not the `_logger.Error(ex.Message, ex)` anti-pattern from the legacy code.
- **Metrics/spans are named.** `ActivitySource "FrigateRelay"`, `Meter "FrigateRelay"`. Counters use the `frigaterelay.*` prefix (see ROADMAP Phase 9 for the exact list). Activity propagates across the channel hop via the `DispatchItem`.
- **`EventContext` is source-agnostic and immutable.** Frigate-specific types never leak past the `IEventSource` boundary.

## Testing

- **MSTest v3** with **Microsoft.Testing.Platform** runner. Primary assertions via MSTest `Assert`.
- **FluentAssertions is pinned to 6.12.2** (Apache-2.0, pre-commercial). Do not upgrade to 7.x/8.x — license constraint (MIT-compatible deps only).
- **NSubstitute** for mocks. **Testcontainers.NET** for integration tests (Mosquitto). **WireMock.Net** for HTTP stubs (Blue Iris, Pushover, CodeProject.AI).
- Each phase in `ROADMAP.md` lists explicit test-count gates — treat them as acceptance criteria, not suggestions.

## CI

- `.github/workflows/build.yml` — matrix: `windows-latest` + `ubuntu-latest`. Runs restore, build `-c Release`, test with coverage + trx. Patterned on the author's `F:\Git\DotNetWorkQueue\.github\workflows` (consult when editing).
- `.github/workflows/secret-scan.yml` — greps for secret shapes; any hit fails the job.
- `.github/workflows/release.yml` — on tag `v*`, builds multi-arch (`linux/amd64`, `linux/arm64`) and pushes to `ghcr.io/<owner>/frigaterelay`.

## Deliberately excluded

- **DotNetWorkQueue** — replaced by `Channel<T>` + `Microsoft.Extensions.Resilience`.
- **App.Metrics** — replaced by OpenTelemetry metrics.
- **OpenTracing / Jaeger client** — replaced by OpenTelemetry tracing (OTLP).
- **SharpConfig / INI** — replaced by `IConfiguration` layering (`appsettings.json` + env + user-secrets + optional `appsettings.Local.json`).
- **Topshelf / Windows Service hosting** — Docker-first. `sc.exe` on a self-contained binary is unsupported.
- **Newtonsoft.Json** — use `System.Text.Json`.
- **Hot-reload config, web UI, durable queue, runtime DLL plugin discovery** — out of scope for v1 (see PROJECT.md Non-Goals).

## When in doubt

PROJECT.md and ROADMAP.md are the source of truth. If a user request conflicts with them, surface the conflict rather than silently deviating.

## Code Quality
- Prefer correct, complete implementations over minimal ones.
- Use appropriate data structures and algorithms — don't brute-force what has a known better solution.
- When fixing a bug, fix the root cause, not the symptom.
- If something I asked for requires error handling or validation to work reliably, include it without asking.
- New and changed features should be covered by either unit or integration Tests