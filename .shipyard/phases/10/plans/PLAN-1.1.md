---
phase: 10-docker-release
plan: 1.1
wave: 1
dependencies: []
must_haves:
  - Host pivots to WebApplication.CreateBuilder with Microsoft.NET.Sdk.Web
  - /healthz endpoint returns 503 until MQTT connects + ApplicationStarted fires, 200 thereafter
  - IMqttConnectionStatus singleton updated by FrigateMqttEventSource
  - Integration test asserts the 503 -> 200 transition
files_touched:
  - src/FrigateRelay.Host/FrigateRelay.Host.csproj  # owns: SDK attribute + PackageReference block ONLY
  - src/FrigateRelay.Host/Program.cs
  - src/FrigateRelay.Host/HostBootstrap.cs
  - src/FrigateRelay.Host/Health/IMqttConnectionStatus.cs
  - src/FrigateRelay.Host/Health/MqttConnectionStatus.cs
  - src/FrigateRelay.Host/Health/MqttHealthCheck.cs
  - src/FrigateRelay.Host/Health/HealthzResponseWriter.cs
  - src/FrigateRelay.Sources.FrigateMqtt/FrigateMqttEventSource.cs
  - src/FrigateRelay.Sources.FrigateMqtt/PluginRegistrar.cs
  - tests/FrigateRelay.Host.Tests/Health/MqttHealthCheckTests.cs
  - tests/FrigateRelay.IntegrationTests/HealthzReadinessTests.cs
tdd: true
risk: high
---

# Plan 1.1: Host pivot to WebApplication + /healthz readiness endpoint

## Context

Implements CONTEXT-10 D2 (`/healthz` minimal-API transport) + D4 (single ready-state endpoint, 200 only when MQTT connected AND host past `ApplicationStarted`). This is the highest-risk task in Phase 10 because it changes the SDK from `Microsoft.NET.Sdk.Worker` to `Microsoft.NET.Sdk.Web` and changes the `HostBootstrap.ConfigureServices` signature from `HostApplicationBuilder` to `WebApplicationBuilder`. Researcher recommended option (a) — full pivot to `WebApplication.CreateBuilder` — and architect concurs: `WebApplication` runs on the same generic-host infrastructure, so all existing `IHostedService` registrations (`EventPump`, `ChannelActionDispatcher`) are unchanged, and `MapHealthChecks` is the standard machine-consumed health probe.

The MQTT connection-state surface uses a new `IMqttConnectionStatus` singleton (researcher A3 option 1) updated by `FrigateMqttEventSource` inside its existing reconnect loop — keeps the health-check layer decoupled from the source.

`StartupValidation.ValidateAll` already pulls `IConfiguration` via `services.GetService<IConfiguration>()`, so no validation-pass signature changes needed for this plan (PLAN-1.2 will plug the new Serilog-path pass into the same Pass 0 slot).

## Dependencies

None (Wave 1).

**File-section ownership note (cross-plan):** This plan owns the `<Project Sdk="...">` attribute and the `<PackageReference>` block in `FrigateRelay.Host.csproj`. PLAN-1.3 owns the `<PropertyGroup>` publish flags and `<None Include="appsettings.Docker.json"/>` content items. The two plans MUST NOT touch each other's sections; the builder will merge sequentially within Wave 1.

## Tasks

### Task 1: Add IMqttConnectionStatus + wire FrigateMqttEventSource updates

**Files:**
- `src/FrigateRelay.Host/Health/IMqttConnectionStatus.cs` (create)
- `src/FrigateRelay.Host/Health/MqttConnectionStatus.cs` (create)
- `src/FrigateRelay.Sources.FrigateMqtt/FrigateMqttEventSource.cs` (modify — inject `IMqttConnectionStatus`, call `SetConnected(true)` after successful `_client.ConnectAsync`, `SetConnected(false)` on disconnect / in `DisposeAsync`)
- `src/FrigateRelay.Sources.FrigateMqtt/PluginRegistrar.cs` (modify — register `IMqttConnectionStatus` -> `MqttConnectionStatus` as singleton)
- `tests/FrigateRelay.Host.Tests/Health/MqttHealthCheckTests.cs` (create — covers status flag transitions in isolation via the concrete `MqttConnectionStatus`)

**Action:** create + modify

**Description:**
Define `IMqttConnectionStatus` with a single `bool IsConnected { get; }` member. Implement `MqttConnectionStatus` with thread-safe (`Volatile.Read`/`Volatile.Write` over a `bool` field, OR `Interlocked` + `int`) `SetConnected(bool)`. Register as singleton in the FrigateMqtt `PluginRegistrar`. Inject into `FrigateMqttEventSource` ctor; call `SetConnected(true)` immediately after the existing successful `_client.ConnectAsync(...)` call in the reconnect loop and `SetConnected(false)` in the disconnect path / `DisposeAsync`. Write tests against `MqttConnectionStatus` directly (no DI, no mocks) covering: default = false; SetConnected(true) -> IsConnected true; SetConnected(false) -> IsConnected false; concurrent read/write does not throw.

**Acceptance Criteria:**
- `test -f src/FrigateRelay.Host/Health/IMqttConnectionStatus.cs && test -f src/FrigateRelay.Host/Health/MqttConnectionStatus.cs`
- `grep -n 'IMqttConnectionStatus' src/FrigateRelay.Sources.FrigateMqtt/FrigateMqttEventSource.cs` returns at least one ctor-injection line and two `SetConnected(` call sites.
- `grep -n 'IMqttConnectionStatus' src/FrigateRelay.Sources.FrigateMqtt/PluginRegistrar.cs` returns one `AddSingleton` registration.
- `dotnet build FrigateRelay.sln -c Release` exits 0.
- `dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build -- --filter-query "/*/*/MqttConnectionStatusTests/*"` exits 0 with all tests passing.

### Task 2: Pivot Host SDK to Web + add WebApplication wiring + MapHealthChecks

**Files:**
- `src/FrigateRelay.Host/FrigateRelay.Host.csproj` (modify — change `<Project Sdk="Microsoft.NET.Sdk.Worker">` to `<Project Sdk="Microsoft.NET.Sdk.Web">`; do NOT touch `<PropertyGroup>` publish flags or any new `<ItemGroup><None>` blocks — those belong to PLAN-1.3)
- `src/FrigateRelay.Host/Program.cs` (modify — switch `Host.CreateApplicationBuilder(args)` to `WebApplication.CreateBuilder(args)`; preserve `appsettings.Local.json` overlay; preserve `HostBootstrap.ConfigureServices(builder)` and `HostBootstrap.ValidateStartup(app.Services)` call order; add `app.MapHealthChecks("/healthz", new HealthCheckOptions { ResponseWriter = HealthzResponseWriter.WriteAsync })` after Build)
- `src/FrigateRelay.Host/HostBootstrap.cs` (modify — change `ConfigureServices(HostApplicationBuilder builder)` parameter type to `WebApplicationBuilder builder`; add `builder.Services.AddHealthChecks().AddCheck<MqttHealthCheck>("mqtt-and-startup")`)
- `src/FrigateRelay.Host/Health/MqttHealthCheck.cs` (create — `internal sealed class MqttHealthCheck : IHealthCheck`; ctor injects `IMqttConnectionStatus` + `IHostApplicationLifetime`; returns `HealthCheckResult.Unhealthy` with `data` dict of `{started, mqttConnected}` until both true, then `Healthy`)
- `src/FrigateRelay.Host/Health/HealthzResponseWriter.cs` (create — serializes `HealthReport` to JSON via `System.Text.Json` with status + per-check status + per-check data; sets `Content-Type: application/json`)

**Action:** modify + create

**Description:**
The pivot is mechanical: `WebApplicationBuilder` exposes `.Services`, `.Configuration`, `.Logging` with the same shape as `HostApplicationBuilder`, so the body of `ConfigureServices` is unchanged except for the new `AddHealthChecks().AddCheck<MqttHealthCheck>(...)` line. The `MqttHealthCheck` queries `_lifetime.ApplicationStarted.IsCancellationRequested` (true once host past `StartAsync` of every `IHostedService`) AND `_status.IsConnected`; emits `Unhealthy` with a `data` dictionary listing `{started, mqttConnected}` when either is false. The `HealthzResponseWriter` returns short JSON: `{ "status": "Healthy"|"Unhealthy", "checks": [{ "name": "...", "status": "...", "data": {...} }] }`. Do NOT add `Microsoft.AspNetCore.Diagnostics.HealthChecks.UI` or any UI package (CONTEXT-10 D2 forbids).

Kestrel listen URL is configured via `ASPNETCORE_URLS` env var (CONTEXT-10 default `http://+:8080`). Do NOT hard-code the port in code.

If integration tests directly call `HostBootstrap.ConfigureServices(HostApplicationBuilder ...)`, the builder MUST update those callsites to use `WebApplication.CreateBuilder(...)` as well. Search before making the signature change.

**Acceptance Criteria:**
- `grep -n 'Microsoft.NET.Sdk.Web' src/FrigateRelay.Host/FrigateRelay.Host.csproj` returns exactly one match.
- `grep -n 'Microsoft.NET.Sdk.Worker' src/FrigateRelay.Host/FrigateRelay.Host.csproj` returns zero matches.
- `grep -n 'WebApplication.CreateBuilder' src/FrigateRelay.Host/Program.cs` returns one match.
- `grep -n 'MapHealthChecks' src/FrigateRelay.Host/Program.cs` returns one match referencing `"/healthz"`.
- `grep -nE 'HealthChecks\.UI|TcpListener' src/FrigateRelay.Host/` returns zero matches.
- `test -f src/FrigateRelay.Host/Health/MqttHealthCheck.cs && test -f src/FrigateRelay.Host/Health/HealthzResponseWriter.cs`
- `dotnet build FrigateRelay.sln -c Release` exits 0 with zero warnings (warnings-as-errors enforced).

### Task 3: Integration test asserting 503 -> 200 readiness transition

**Files:**
- `tests/FrigateRelay.IntegrationTests/HealthzReadinessTests.cs` (create)

**Action:** test

**Description:**
Integration test that:
1. Starts a Mosquitto Testcontainer (existing pattern — see `MqttToValidatorTests` for the broker setup recipe).
2. Builds a `WebApplication` host pointed at the broker, on a free ephemeral port (`ASPNETCORE_URLS=http://127.0.0.1:0`; capture the bound port from `IServer` features after `app.StartAsync`).
3. Asserts: the FIRST `GET /healthz` issued before MQTT subscribe completes returns `503` with body containing `mqttConnected:false` OR `started:false`.
4. Polls `/healthz` for up to 10 seconds; asserts a `200` response is observed with body status `Healthy`.
5. Stops the broker; asserts a subsequent `/healthz` poll returns `503` again within 10 seconds (confirms `IMqttConnectionStatus` flips on disconnect).

Do NOT use `Task.Delay` polling without a timeout assertion. Use existing `WaitForAsync` / poll-with-timeout helpers if available, otherwise inline a `for` loop with explicit max-iteration + `Assert.Fail` on timeout.

**Acceptance Criteria:**
- `test -f tests/FrigateRelay.IntegrationTests/HealthzReadinessTests.cs`
- `dotnet run --project tests/FrigateRelay.IntegrationTests -c Release --no-build -- --filter-query "/*/*/HealthzReadinessTests/*"` exits 0 (Docker must be running locally).
- The test contains at least one `Assert` against HTTP `503` AND at least one against HTTP `200`.
- `grep -n 'TcpListener\|ServicePointManager' tests/FrigateRelay.IntegrationTests/HealthzReadinessTests.cs` returns zero.

## Verification

Run from repo root:

```bash
# Compile + test gates
dotnet build FrigateRelay.sln -c Release
dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build
dotnet run --project tests/FrigateRelay.IntegrationTests -c Release --no-build -- --filter-query "/*/*/HealthzReadinessTests/*"

# Architectural invariants
git grep -n 'TcpListener' src/ && exit 1 || true
git grep -n 'HealthChecks\.UI' src/ && exit 1 || true
git grep -n 'Microsoft.NET.Sdk.Worker' src/FrigateRelay.Host/FrigateRelay.Host.csproj && exit 1 || true
git grep -nE '\.(Result|Wait)\(' src/ && exit 1 || true

# /healthz wired
grep -q 'MapHealthChecks("/healthz"' src/FrigateRelay.Host/Program.cs
```
