# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Instructions
Always use Context7 MCP when I need library/API documentation or setup steps. Automatically resolve library IDs and retrieve docs without being asked.

## Project state

FrigateRelay is a **.NET 10 background service** that supersedes the legacy `FrigateMQTTProcessingService`. Implementation is complete through **Phase 10** (Docker + multi-arch release workflow). Phase 11 (this phase) adds OSS polish (LICENSE, README, plugin scaffold, plugin-author guide). Phase 12 is the parity-cutover gate before v1.0.0. Before writing or changing code, read:

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

All commands assume `FrigateRelay.sln` at repo root.

```bash
# Build (warnings-as-errors; must be clean on Windows and Linux)
dotnet build FrigateRelay.sln -c Release

# Tests — NOT via `dotnet test`. On .NET 10 SDK, `dotnet test` is blocked for
# Microsoft.Testing.Platform (the MSTest v3 runner) via the VSTest target
# (https://aka.ms/dotnet-test-mtp-error). Test projects have OutputType=Exe
# and are invoked as an exe via `dotnet run`:
dotnet run --project tests/FrigateRelay.Abstractions.Tests -c Release
dotnet run --project tests/FrigateRelay.Host.Tests -c Release

# Single test by class name (MSTest v4.2.1 / MTP filter)
dotnet run --project tests/FrigateRelay.Host.Tests -c Release -- --filter "PluginRegistrarRunnerTests"

# List transitive dependencies (note the argument order — the csproj path goes
# BEFORE the `package` verb on .NET 10; the pre-10 `--project <path>` form
# prints help and exits 0):
dotnet list src/FrigateRelay.Abstractions/FrigateRelay.Abstractions.csproj package --include-transitive

# Run the host locally (expects appsettings.Local.json or env-var overrides for secrets)
dotnet run --project src/FrigateRelay.Host -c Release

# Graceful shutdown smoke on Linux / WSL — `timeout --signal=SIGINT` does NOT
# reliably propagate to the Host exe through the `dotnet run` wrapper. Use
# pgrep + explicit kill -INT against the child exe pid:
#
#   dotnet run --project src/FrigateRelay.Host -c Release --no-build > /tmp/host.log 2>&1 &
#   sleep 3
#   kill -INT "$(pgrep -f 'FrigateRelay.Host/bin/Release/net10.0/FrigateRelay.Host$' | head -1)"
#   wait
#   # expect exit 0 and "Application is shutting down..." in /tmp/host.log

# Docker build (Phase 10+)
docker build -f docker/Dockerfile .
docker compose -f docker/docker-compose.example.yml up
```

Integration tests (Phase 4+) use Testcontainers.NET — Docker must be running.

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
- **`Subscriptions:N:Actions` accepts both shapes**: object form `[{ "Plugin": "BlueIris" }, { "Plugin": "Pushover", "SnapshotProvider": "Frigate" }]` and the legacy string-array shorthand `["BlueIris"]`. Phase 8 closed ID-12 by adding `ActionEntryTypeConverter`; `IConfiguration.Bind` now converts a scalar string into `new ActionEntry(name)` via the registered `[TypeConverter]`, while `JsonSerializer.Deserialize` continues to use `ActionEntryJsonConverter`. The two converters operate on disjoint code paths and both are needed.
- **Dedupe uses a scoped `IMemoryCache`**, keyed per-subscription on camera + object label. **Never** use `MemoryCache.Default` (the legacy code's mistake).
- **No secrets in committed `appsettings.json`.** Secret fields default to `""` and must be supplied via env vars or user-secrets. CI greps for `AppToken=…`, `UserKey=…`, api-key-shaped strings, and `192.168.x.x` — any match fails the build. Do not commit hard-coded IPs or hostnames, including in comments.
- **No hard-coded IPs/hostnames in source** — including in commented-out code. (The legacy code had `http://192.168.0.58:5001` in a commented block; that pattern is forbidden here.)
- **Observability stack is Microsoft.Extensions.Logging + Serilog sinks + OpenTelemetry (OTLP).** **Do not** introduce `App.Metrics`, `OpenTracing`, or `Jaeger.*` — they are explicitly excluded. `git grep -nE 'App\.Metrics|OpenTracing|Jaeger\.' src/` must be empty. Use `ILogger.LogError(ex, "message")` — not the `_logger.Error(ex.Message, ex)` anti-pattern from the legacy code.
- **Metrics/spans are named.** `ActivitySource "FrigateRelay"`, `Meter "FrigateRelay"`. Counters use the `frigaterelay.*` prefix (see ROADMAP Phase 9 for the exact list). Activity propagates across the channel hop via the `DispatchItem`.
- **`EventContext` is source-agnostic and immutable.** Frigate-specific types never leak past the `IEventSource` boundary.

## Conventions (discovered and locked in during Phase 1)

- **`[SetsRequiredMembers]` on ctors with `required init` properties.** When a class has both a constructor AND `required init` properties, mark the ctor `[System.Diagnostics.CodeAnalysis.SetsRequiredMembers]` so callers can use the ctor standalone. Without it, callers must use object-initializer syntax even when the ctor sets every required member — making the ctor effectively vestigial. Precedent: `PluginRegistrationContext.cs` in `FrigateRelay.Abstractions`.
- **Test log assertions use the shared `CapturingLogger<T> : ILogger<T>` from `tests/FrigateRelay.TestHelpers/`, not NSubstitute on `ILogger<T>`.** NSubstitute on the generic `Log<TState>(...)` method is fragile around `TState` matching (`Arg.Any<object>()` often silently fails to bind to the generic slot). A tiny capturing implementation that stores `(level, eventId, formatter(state, exception))` tuples produces cleaner tests and better failure messages with zero new deps. The helper lives in `FrigateRelay.TestHelpers` (Library project, no test runner deps); each test csproj references it via `<ProjectReference>` and gets the type via `global using FrigateRelay.TestHelpers;` in `Usings.cs`. Do NOT redefine a per-assembly copy — extracted out of 4 duplicates after Phase 6 (ID-11 closed).
- **`IActionPlugin.ExecuteAsync` takes three parameters: `EventContext`, `SnapshotContext`, `CancellationToken`** (since Phase 6, ARCH-D2). Plugins that don't consume snapshots (e.g. `BlueIrisActionPlugin`) accept the `SnapshotContext` parameter and ignore it — no compile-time signaling required. The dispatcher constructs `SnapshotContext` per dispatch from `ISnapshotResolver` + the per-action and per-subscription provider name tiers carried on `DispatchItem`. Plugins that DO consume snapshots call `snapshot.ResolveAsync(eventContext, ct)` — `default(SnapshotContext).ResolveAsync` short-circuits to `null` so plugins can be tested without DI'ing a resolver.
- **`<InternalsVisibleTo>` MSBuild item for test-only internals access.** Prefer the csproj item `<InternalsVisibleTo Include="FrigateRelay.X.Tests" />` over a source-level `[assembly: InternalsVisibleTo(...)]` attribute — the MSBuild form is declarative, removable, and keeps test boundaries in config rather than polluting the public-surface source tree. Precedent: `src/FrigateRelay.Host/FrigateRelay.Host.csproj`.
- **Test names use underscores (`Method_Condition_Expected`).** `CA1707` is silenced for `tests/**.cs` via `.editorconfig` — do not re-enable it per-project. This is the DAMP convention for tests and produces readable failure output.
- **Startup validation is collect-all, not throw-on-first** (Phase 8 D7). Each pass — `ProfileResolver.Resolve`, `StartupValidation.ValidateActions`, `ValidateSnapshotProviders`, `ValidateValidators` — appends to a shared `List<string> errors` instead of throwing. The single `StartupValidation.ValidateAll(IServiceProvider, HostSubscriptionsOptions)` entry point allocates the accumulator, runs every pass, then throws one aggregated `InvalidOperationException` whose message lists every misconfiguration on its own indented line. Operators see all problems at once instead of fixing one, restarting, and discovering the next. When adding a new startup invariant, follow the same pattern: take a `List<string> errors` parameter, call `errors.Add(...)`, never throw inside the pass.
- **NSubstitute on `internal` types requires `<InternalsVisibleTo Include="DynamicProxyGenAssembly2" />`.** Castle DynamicProxy (NSubstitute's runtime) needs internals access in addition to the test assembly. Adding only `<InternalsVisibleTo Include="FrigateRelay.X.Tests" />` produces NS2003 build errors when a test mocks an internalized type. Discovered during Phase 8 PLAN-1.1 visibility sweep when `IActionDispatcher` was internalized.

## Testing

- **MSTest v3** with **Microsoft.Testing.Platform** runner. Primary assertions via MSTest `Assert`.
- **FluentAssertions is pinned to 6.12.2** (Apache-2.0, pre-commercial). Do not upgrade to 7.x/8.x — license constraint (MIT-compatible deps only).
- **NSubstitute** for mocks. **Testcontainers.NET** for integration tests (Mosquitto). **WireMock.Net** for HTTP stubs (Blue Iris, Pushover, CodeProject.AI).
- Each phase in `ROADMAP.md` lists explicit test-count gates — treat them as acceptance criteria, not suggestions.

## CI

**Split architecture** (decision D1 from Phase 2 CONTEXT-2.md, mirrors DotNetWorkQueue's topology): GitHub Actions is the fast PR gate; Jenkins runs coverage. **Do not add coverage to `ci.yml`** — coverage lives in `Jenkinsfile` exclusively. A future agent adding `--coverage` flags to the GH workflow would silently duplicate Jenkins's job and break the intentional separation.

- `.github/workflows/ci.yml` — PR gate. Matrix `[ubuntu-latest, windows-latest]`. `actions/setup-dotnet@v4` with `global-json-file: global.json`. Build + tests only (`dotnet run --project tests/<project> -c Release --no-build`); **no coverage, no TRX, no artifact upload**. `shell: bash` on test steps for Windows Git Bash consistency. Concurrency group cancels obsolete runs on force-push.
- `.github/workflows/secret-scan.yml` — two jobs: `scan` greps the tree (excluding `.shipyard/`, `CLAUDE.md`, and the fixture file) for secret-shaped patterns; `tripwire-self-test` greps **only** `.github/secret-scan-fixture.txt` and fails if any pattern does NOT match — proves the regex set still detects the shapes it's supposed to. If you change a pattern in `.github/scripts/secret-scan.sh`, you MUST add a matching fixture line, or the tripwire will silently rot.
- `.github/dependabot.yml` — nuget + github-actions, weekly Monday. FluentAssertions hard-pinned (`ignore: versions [">= 7.0.0"]`) — license-critical, do not remove. MSBuild SDKs are not watched (we use `Microsoft.NET.Sdk`, no `msbuild-sdks` block).
- `Jenkinsfile` — coverage pipeline. Scripted. Docker agent `mcr.microsoft.com/dotnet/sdk:10.0` digest-pinned (current SHA committed in `Jenkinsfile`; bump manually per the inline comment — Dependabot `docker` ecosystem watches `docker/Dockerfile` only, NOT `Jenkinsfile`, intentionally decoupled). Workspace-local NuGet cache (`--packages .nuget-cache`). MTP coverage extension: `dotnet run --project tests/<project> -c Release --no-build -- --coverage --coverage-output-format cobertura --coverage-output coverage/<project>.cobertura.xml`. **The explicit `--coverage-output <path>` flag IS honored inside the SDK container** (verified Phase 2) — archive with `coverage/**/*.cobertura.xml`, no `TestResults/` glob needed.
- `.github/workflows/release.yml` — multi-arch GHCR release. Triggered on `push: tags: ['v*']`. Two-job pipeline: (1) `smoke` builds amd64 with `load: true`, runs Mosquitto sidecar + polls `/healthz` until 200 — hard-fail gate; (2) `push-multiarch` (depends on smoke) buildx-builds `linux/amd64,linux/arm64` and pushes to `ghcr.io/<owner>/frigaterelay:<semver>` + `:latest` + `:major`. Permissions are minimal (`contents: read`, `packages: write`); concurrency group `release-${{ github.ref }}` prevents overlapping releases on the same tag.

**When adding a new test project**: no CI changes required. Both `ci.yml` and `Jenkinsfile` delegate to `.github/scripts/run-tests.sh` (extracted Phase 3), which auto-discovers test projects via `find tests -maxdepth 2 -name '*Tests.csproj'`. Drop the new csproj at `tests/<Name>/<Name>.csproj` (matching the `*Tests.csproj` glob) and it runs in both CI and Jenkins automatically. The script handles `--coverage` (Jenkins), `--skip-integration` (PR-fast path), and propagates `--filter`-style passthrough args.

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