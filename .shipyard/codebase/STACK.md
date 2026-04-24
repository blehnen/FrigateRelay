# STACK.md

## Overview

FrigateMQTTProcessing is a .NET Framework 4.8 Windows service built across five projects in a single Visual Studio solution. The solution uses a mixed csproj format: two projects (`FrigateMQTTProcessingService`, `FrigateMQTTProcessingMain`) use SDK-style csproj with `<PackageReference>`, while three projects (`FrigateMQTTMainLogic`, `FrigateMQTTConfiguration`, `FrigateMQTTLogging`) use legacy `ToolsVersion="15.0"` csproj with `packages.config` — requiring dual maintenance of `.csproj` `<Reference HintPath>` entries and `packages.config` entries simultaneously when adding dependencies to those three projects.

## Metrics

| Metric | Value |
|--------|-------|
| Target framework | net48 (.NET Framework 4.8) |
| Projects in solution | 5 |
| SDK-style csproj projects | 2 (`FrigateMQTTProcessingService`, `FrigateMQTTProcessingMain`) |
| Legacy csproj + packages.config projects | 3 (`FrigateMQTTMainLogic`, `FrigateMQTTConfiguration`, `FrigateMQTTLogging`) |
| Total NuGet packages (FrigateMQTTMainLogic alone) | 96 entries in packages.config (majority are transitive BCL polyfills) |
| Build system | MSBuild via `msbuild` CLI or Visual Studio |
| Package manager | NuGet (mixed PackageReference + packages.config) |
| Output binary | `FrigateMQTTProcessingService.exe` (Topshelf exe) |

## Findings

### Language and Runtime

- **Language**: C# targeting .NET Framework 4.8
  - Evidence: `Source/FrigateMQTTProcessingService/FrigateMQTTProcessingService.csproj` line 4: `<TargetFramework>net48</TargetFramework>`
  - Evidence: `Source/FrigateMQTTMainLogic/FrigateMQTTMainLogic.csproj` line 12: `<TargetFrameworkVersion>v4.8</TargetFrameworkVersion>`
- **Runtime requirement**: Windows only — service installs via Topshelf as a Windows Service running as LocalSystem
  - Evidence: `Source/FrigateMQTTProcessingService/Program.cs` line 63: `x.RunAsLocalSystem()`

### Build Tools

- **Build**: MSBuild (legacy + SDK-style mixed). Command documented in CLAUDE.md:
  ```
  msbuild Source/FrigateMQTTProcessingService.sln /t:Restore
  msbuild Source/FrigateMQTTProcessingService.sln /p:Configuration=Release
  ```
- **Output path**: `Source/FrigateMQTTProcessingService/bin/{Debug,Release}/net48/FrigateMQTTProcessingService.exe`
  - Evidence: `CLAUDE.md` (build section)
- **No CI configuration**: no `.github/workflows/`, no Azure Pipelines YAML, no Makefile observed.

### Mixed csproj Format — Important Constraint

Three of five projects still use the legacy csproj format and `packages.config`. When adding a NuGet dependency to any of these three projects, **both** the `<Reference HintPath>` entry in `.csproj` and the `<package>` entry in `packages.config` must be updated manually. SDK-style `<PackageReference>` cannot be used in those projects without a project-format migration.

| Project | csproj Format | Package Style |
|---------|--------------|---------------|
| `FrigateMQTTProcessingService` | SDK-style | `<PackageReference>` |
| `FrigateMQTTProcessingMain` | SDK-style | `<PackageReference>` |
| `FrigateMQTTMainLogic` | Legacy ToolsVersion=15.0 | `packages.config` |
| `FrigateMQTTConfiguration` | Legacy ToolsVersion=15.0 | `packages.config` |
| `FrigateMQTTLogging` | Legacy ToolsVersion=15.0 | `packages.config` |

Evidence: `Source/FrigateMQTTMainLogic/FrigateMQTTMainLogic.csproj` line 2 vs `Source/FrigateMQTTProcessingService/FrigateMQTTProcessingService.csproj` line 1.

### Service Host

- **Topshelf** `4.3.0` — enables the same exe to run as a Windows console app (for debugging) or as an installable Windows service. Install/uninstall via `FrigateMQTTProcessingService.exe install|uninstall|start|stop`.
  - Evidence: `Source/FrigateMQTTProcessingService/FrigateMQTTProcessingService.csproj` lines 13-14
  - Evidence: `Source/FrigateMQTTProcessingService/Program.cs` lines 55-80
- **Topshelf.Serilog** `4.3.0` — wires Serilog into Topshelf's internal logging.
  - Evidence: `Source/FrigateMQTTProcessingService/FrigateMQTTProcessingService.csproj` line 14
- **Service recovery**: configured to restart on crash, up to 3 attempts (0, 1, 2 minutes delay), reset period 1 day.
  - Evidence: `Source/FrigateMQTTProcessingService/Program.cs` lines 67-73

### MQTT Client

- **MQTTnet** `4.1.4.563` — core MQTT protocol library.
  - Evidence: `Source/FrigateMQTTMainLogic/packages.config` line 30
- **MQTTnet.Extensions.ManagedClient** `4.1.4.563` — wraps `MqttClient` with auto-reconnect (5-second reconnect delay, hard-coded).
  - Evidence: `Source/FrigateMQTTMainLogic/packages.config` line 31
  - Evidence: `Source/FrigateMQTTMainLogic/Main.cs` line 52: `WithAutoReconnectDelay(TimeSpan.FromSeconds(5))`

### Work Queues

- **DotNetWorkQueue** `0.6.8` — in-process async worker queue. Two instances: `"camera"` queue and `"pushover"` queue. Retry policy of 3/6/9 seconds on any `Exception`.
  - Evidence: `Source/FrigateMQTTMainLogic/packages.config` lines 10-11
  - Evidence: `Source/FrigateMQTTMainLogic/Main.cs` lines 42-46
- **DotNetWorkQueue.AppMetrics** `0.6.8` — integrates DotNetWorkQueue with App.Metrics for queue instrumentation.
  - Evidence: `Source/FrigateMQTTMainLogic/packages.config` line 11

### Logging

- **Serilog** `2.12.0` — structured logging framework used across all five projects.
  - Evidence: `Source/FrigateMQTTLogging/packages.config` line 2; `Source/FrigateMQTTMainLogic/packages.config` line 40
- **Serilog.Sinks.File** `5.0.0` — rolling file sink.
  - Evidence: `Source/FrigateMQTTLogging/packages.config` line 7
- **Serilog.Sinks.Seq** `5.2.2` — structured log sink to a Seq server (URL configured in `logging.settings`).
  - Evidence: `Source/FrigateMQTTLogging/packages.config` line 9
- **Serilog.Sinks.Console** `4.1.0` — console output (useful in debug/console mode).
  - Evidence: `Source/FrigateMQTTLogging/packages.config` line 5
- **Serilog.Enrichers.Environment** `2.2.0` — enriches log events with machine/environment name.
  - Evidence: `Source/FrigateMQTTLogging/packages.config` line 3
- **Serilog.Formatting.Compact** `1.1.0` — compact JSON formatter.
  - Evidence: `Source/FrigateMQTTLogging/packages.config` line 4
- **Serilog.Sinks.PeriodicBatching** `3.1.0` — batching infrastructure for sinks.
  - Evidence: `Source/FrigateMQTTLogging/packages.config` line 8
- **Configuration style**: flat `key=value` file (`Configuration/logging.settings`), parsed via `Serilog.Settings.KeyValuePairs` — not `appsettings.json`. Template at `Configs/logging.settings`.
  - Evidence: `CLAUDE.md` (runtime configuration section)

### Tracing

- **Jaeger** `1.0.3` — Jaeger OpenTracing client. Only activated when `tracesettings.json` exists in the exe directory; falls back to `NoopTracerFactory.Create()` when absent.
  - Evidence: `Source/FrigateMQTTProcessingMain/FrigateMQTTProcessingMain.csproj` line 9
  - Evidence: `Source/FrigateMQTTProcessingMain/FrigateMain.cs` lines 103-113
- **OpenTracing** `0.12.1` — OpenTracing API (ITracer, ISpan abstractions used throughout MainLogic).
  - Evidence: `Source/FrigateMQTTMainLogic/packages.config` line 36
- **OpenTelemetry** `1.3.2` + **OpenTelemetry.Api** `1.3.2` — present as transitive dependencies via DotNetWorkQueue; not directly used in application code.
  - Evidence: `Source/FrigateMQTTMainLogic/packages.config` lines 34-35

### Metrics

- **App.Metrics** `4.3.0` (core + abstractions + concurrency + formatters) — in-process metrics. Two metric roots: `metricsAPI` and `metricsQueue`, each writing to `Metrics\metricsAPI.txt` and `Metrics\metricsQueue.txt` respectively. Scheduler fires every 3 seconds. The text-file reporter is currently commented out in code; metrics are collected but not persisted at runtime.
  - Evidence: `Source/FrigateMQTTMainLogic/packages.config` lines 3-8
  - Evidence: `Source/FrigateMQTTProcessingMain/FrigateMain.cs` lines 57-59, 122-145 (reporter commented out at lines 131-136)

### HTTP Client

- **RestSharp** `108.0.3` — used exclusively in the commented-out `CodeProjectConfirms` method in `Pushover.cs`. Not active at runtime in current state.
  - Evidence: `Source/FrigateMQTTMainLogic/packages.config` line 39
  - Evidence: `Source/FrigateMQTTMainLogic/Pushover.cs` line 111
- **System.Net.Http.HttpClient** (BCL) — used for the active Pushover POST and Blue Iris image fetch. Static singleton instances in `Pushover` and `TriggerCamera`.
  - Evidence: `Source/FrigateMQTTMainLogic/Pushover.cs` lines 21-22, 82
  - Evidence: `Source/FrigateMQTTMainLogic/TriggerCamera.cs` lines 16-17, 36

### Configuration

- **SharpConfig** `3.2.9.1` — INI-style config parser. Reads `FrigateMQTTProcessingService.conf` into typed POCOs (`ServerSettings`, `PushoverSettings`, `SubscriptionSettings`).
  - Evidence: `Source/FrigateMQTTConfiguration/packages.config` line 2
  - Evidence: `Source/FrigateMQTTConfiguration/FrigateMQTTConfiguration.csproj` line 34
- **Microsoft.Extensions.Configuration** `7.0.0` + **Microsoft.Extensions.Configuration.Json** — used only for loading `tracesettings.json` for Jaeger. Not the primary config system.
  - Evidence: `Source/FrigateMQTTProcessingMain/FrigateMQTTProcessingMain.csproj` lines 10-11

### Dependency Injection

- **SimpleInjector** `5.4.1` — DI container present as a dependency in `FrigateMQTTMainLogic`.
  - Evidence: `Source/FrigateMQTTMainLogic/packages.config` line 41
- [Inferred] SimpleInjector does not appear to be wired up as the primary DI container in the active code paths reviewed; dependencies are constructed manually (e.g., `new Pushover(...)`, `new TriggerCamera(...)`). SimpleInjector may be a DotNetWorkQueue internal dependency.

### Caching / Throttling

- **Microsoft.Extensions.Caching.Memory** `7.0.0` — `IMemoryCache` used for per-camera Pushover cooldown. Also `System.Runtime.Caching.MemoryCache` (BCL) is used directly in `Main.cs`.
  - Evidence: `Source/FrigateMQTTMainLogic/packages.config` lines 15-16
  - Evidence: `Source/FrigateMQTTMainLogic/Main.cs` lines 26, 39
- **Polly** `7.2.3` + **Polly.Caching.Memory** `3.0.2` — resilience library; present in packages, likely used within DotNetWorkQueue retry infrastructure.
  - Evidence: `Source/FrigateMQTTMainLogic/packages.config` lines 37-38

### JSON Serialization

- **Newtonsoft.Json** `13.0.2` — used in `Main.cs` to deserialize incoming MQTT `frigate/events` payloads into `FrigateEvent`.
  - Evidence: `Source/FrigateMQTTMainLogic/packages.config` line 33
  - Evidence: `Source/FrigateMQTTMainLogic/Main.cs` line 94
- **System.Text.Json** `7.0.1` — present as a package; not observed in active application code (may be a transitive dependency).
  - Evidence: `Source/FrigateMQTTMainLogic/packages.config` line 87

### Miscellaneous

- **CliWrap** `3.6.0` — CLI process-wrapping library; present in `FrigateMQTTMainLogic/packages.config` but not observed in active source files reviewed.
  - Evidence: `Source/FrigateMQTTMainLogic/packages.config` line 9

## Summary Table

| Item | Detail | Confidence |
|------|--------|------------|
| Runtime | .NET Framework 4.8, Windows only | Observed |
| Service host | Topshelf 4.3.0 | Observed |
| MQTT client | MQTTnet 4.1.4.563 + ManagedClient extension | Observed |
| Work queues | DotNetWorkQueue 0.6.8 (in-process) | Observed |
| Logging | Serilog 2.12.0, key-value config file | Observed |
| Log sinks | File, Console, Seq 5.2.2 | Observed |
| Tracing | Jaeger 1.0.3 / OpenTracing 0.12.1, optional | Observed |
| Metrics | App.Metrics 4.3.0, text-file reporter commented out | Observed |
| HTTP (active) | BCL HttpClient (static singletons) | Observed |
| HTTP (inactive) | RestSharp 108.0.3 (CodeProject.AI path, commented out) | Observed |
| Config | SharpConfig 3.2.9.1 INI + JSON file for Jaeger | Observed |
| DI container | SimpleInjector 5.4.1 (likely DotNetWorkQueue internal) | Inferred |
| JSON | Newtonsoft.Json 13.0.2 (active), System.Text.Json 7.0.1 (transitive) | Observed |
| Build format | Mixed SDK-style (2 projects) + legacy csproj/packages.config (3 projects) | Observed |
| No CI/CD | No pipeline config found | Observed |

## Open Questions

- Is `CliWrap` actually used anywhere in source not reviewed (e.g., a helper script runner), or is it a leftover dependency?
- Is `SimpleInjector` wired up anywhere beyond being a DotNetWorkQueue transitive requirement?
- The App.Metrics text-file reporter is commented out — is metrics output actually being captured anywhere in production?
- No `Serilog.Settings.KeyValuePairs` package appears in any `packages.config`; how exactly is `logging.settings` parsed? [Needs investigation of `FrigateMQTTLogging/LoggingSetup.cs`]
