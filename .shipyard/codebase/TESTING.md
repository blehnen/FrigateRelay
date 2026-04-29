# TESTING.md

## Overview

There is no test project in this codebase. No unit tests, integration tests, or test infrastructure of any kind exist. Validation is performed entirely through manual runtime observation: running the service binary as a console application and inspecting Serilog/Seq output. This is the complete testing story as the code stands.

## Metrics

| Metric | Value |
|--------|-------|
| Test projects | 0 |
| Test files (.cs) | 0 |
| xUnit / NUnit / MSTest references | None found |
| CI configuration files | None found |
| Code coverage tooling | None |
| Solution projects total | 5 (all production) |

---

## Findings

### No Test Project Exists

- The solution file `Source/FrigateMQTTProcessingService.sln` contains 5 projects, all production code. No test project is present.
  - Evidence: `Source/FrigateMQTTProcessingService.sln` — projects are `FrigateMQTTConfiguration`, `FrigateMQTTLogging`, `FrigateMQTTMainLogic`, `FrigateMQTTProcessingMain`, `FrigateMQTTProcessingService`
- No `packages.config` or `.csproj` file references xUnit, NUnit, MSTest, FluentAssertions, Moq, NSubstitute, or any other testing library.
- CLAUDE.md explicitly states: "No unit or integration tests. No CI configuration. No Dockerfile."
  - Evidence: `CLAUDE.md` line 93

---

### No CI Configuration

- No `.github/`, `.gitlab-ci.yml`, `azure-pipelines.yml`, `Jenkinsfile`, or equivalent CI artifact exists in the repository root or anywhere in the tree.
- Deployment is manual: `Topshelf install` after copying configuration files to the binary output directory.
  - Evidence: `CLAUDE.md` lines 93–94

---

### Manual Validation Path

The documented and only validation path is:

1. **Console mode**: Run `FrigateMQTTProcessingService.exe` directly (no subcommand). The `#if DEBUG` branch in `Program.cs` controls Serilog debug-level verbosity.
   - Evidence: `Source/FrigateMQTTProcessingService/Program.cs` lines 25–28
2. **Log observation**: Serilog outputs to file and optionally to a Seq server, configured via `Configuration/logging.settings`. The global `Log.Logger` is set at startup so all modules share one sink.
   - Evidence: `Source/FrigateMQTTProcessingService/Program.cs` line 47 (`Log.Logger = _logger`)
3. **Service mode validation**: Install as a Windows service with `FrigateMQTTProcessingService.exe install`, then `start`. Topshelf maps `Start()`/`Stop()` lifecycle.
   - Evidence: `Source/FrigateMQTTProcessingService/Program.cs` lines 55–80 (Topshelf `HostFactory.Run` configuration)
4. **Tracing**: If `tracesettings.json` is present, Jaeger spans can be observed in a local Jaeger UI for I/O call timing and error tags.
   - Evidence: `Source/FrigateMQTTProcessingMain/FrigateMain.cs` lines 102–113
5. **Metrics**: App.Metrics writes to `Metrics/metricsAPI.txt` and `Metrics/metricsQueue.txt` (text reporter is currently commented out, so files may be empty in practice).
   - Evidence: `Source/FrigateMQTTProcessingMain/FrigateMain.cs` lines 131–135 (commented-out reporter)

---

### Testability Assessment

- **`Main` class** (`FrigateMQTTMainLogic/Main.cs`): `Run()` accepts `ILogger`, `ConfigurationSettings`, `IMetrics`, and `ITracer` by parameter — these are injectable interfaces. However, `Main` internally calls `new MqttFactory().CreateManagedMqttClient()` and `new WorkerQueue<T>(...)` without abstraction, making MQTT and queue behavior hard to substitute without refactoring.
  - Evidence: `Source/FrigateMQTTMainLogic/Main.cs` lines 42–58
- **`Pushover` and `TriggerCamera`**: Both use a `private static readonly HttpClient` initialized in a static constructor. The `HttpClient` is not injectable, making HTTP calls untestable without a real network or a shim like `HttpMessageHandler` substitution.
  - Evidence: `Source/FrigateMQTTMainLogic/Pushover.cs` lines 20–27; `Source/FrigateMQTTMainLogic/TriggerCamera.cs` lines 15–24
- **`ConfigurationLoader`**: Accepts an optional `configurationFileFolder` path, which enables file-based configuration to be pointed at a test fixture directory. [Inferred] This is the most readily testable seam in the codebase.
  - Evidence: `Source/FrigateMQTTConfiguration/ConfigurationLoader.cs` line 25
- **CLAUDE.md instruction**: The quality guidelines section states "New and changed features should be covered by either unit or integration Tests" — establishing intent that testing should accompany future changes, even though no test infrastructure currently exists.
  - Evidence: `CLAUDE.md` lines 98–102

---

## Summary Table

| Item | Detail | Confidence |
|------|--------|------------|
| Test framework | None | Observed |
| Test runner | None | Observed |
| CI system | None | Observed |
| Coverage tooling | None | Observed |
| Validation method | Manual console run + Serilog/Seq observation | Observed |
| Service validation | Topshelf install/start/stop commands | Observed |
| HttpClient testability | Static singleton — not injectable | Observed |
| ILogger / ITracer testability | Constructor-injectable interfaces | Observed |
| MQTT client testability | `new MqttFactory()` called internally — not injectable | Observed |
| Author intent | CLAUDE.md states new features should have tests | Observed |

## Open Questions

- The App.Metrics text file reporter is commented out in `FrigateMain.SetupMetrics()`. It is unclear whether metrics output was ever working or if a different reporter sink is intended for production.
- No test fixture configuration files (e.g., a sample `.conf`) exist alongside the `Configs/logging.settings` template — any future integration test would need to create these.
