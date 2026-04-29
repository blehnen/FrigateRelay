# CONVENTIONS.md

## Overview

The codebase follows consistent C# conventions throughout its 19 non-generated source files across 5 projects. Patterns are uniform enough to be deliberate: underscore-prefixed private fields, constructor-injected `ILogger`/`ITracer`, Serilog string-interpolated message templates, OpenTracing spans wrapping every I/O call, and intentionally retained commented-out code blocks as in-place documentation of abandoned design paths.

## Metrics

| Metric | Value |
|--------|-------|
| Non-generated .cs source files | 19 |
| `var` usages (non-generated) | 64 |
| TODO / FIXME / HACK comments | 0 |
| Projects (solution) | 5 |
| XML doc comment methods observed | 6 (on public methods in `ConfigurationLoader`, `FrigateMain`) |

---

## Findings

### Field and Property Naming

- **Private fields**: Underscore prefix + camelCase throughout. No exceptions observed across all classes sampled.
  - Evidence: `Source/FrigateMQTTMainLogic/Main.cs` lines 21–31 (`_logger`, `_client`, `_settings`, `_cameraEventTracker`, `_cameraLastEvent`, `_cameraQueue`, `_pushoverQueue`, `_tracer`)
  - Evidence: `Source/FrigateMQTTProcessingMain/FrigateMain.cs` lines 24–34 (`_cancelToken`, `_systemLogger`, `_locker`, `_appMetricsTaskScheduler`, `_metricsApi`, `_metricsQueue`, `_tracing`, `_main`)
  - Evidence: `Source/FrigateMQTTMainLogic/TriggerCamera.cs` lines 13–14 (`_tracer`, `_logger`)

- **Public properties**: PascalCase auto-properties. POCO classes use `{ get; set; }` uniformly.
  - Evidence: `Source/FrigateMQTTConfiguration/PushoverSettings.cs` lines 15–17 (`AppToken`, `UserKey`, `NotifySleepTime`)
  - Evidence: `Source/FrigateMQTTConfiguration/SubscriptionSettings.cs` lines 12–19

- **Constants**: `private const string` with PascalCase.
  - Evidence: `Source/FrigateMQTTMainLogic/Pushover.cs` line 19 (`QuoteChar`)

- **JSON deserialization DTOs** (internal to `Pushover`): snake_case property names to match the external API payload directly, no `[JsonProperty]` mapping attributes.
  - Evidence: `Source/FrigateMQTTMainLogic/Pushover.cs` lines 155–162 (`confidence`, `label`, `x_min`, `y_min`, etc.)

---

### `var` Usage

- `var` is used freely for local variables where the type is evident from the right-hand side (constructor calls, method returns). Explicit types are used for parameters and fields.
  - Evidence: `Source/FrigateMQTTMainLogic/Main.cs` lines 50–56 (`var options = new ManagedMqttClientOptionsBuilder()...`, `var filters = new List<MqttTopicFilter>...`)
  - Evidence: `Source/FrigateMQTTProcessingMain/FrigateMain.cs` lines 55–56 (`var rc = HostFactory.Run(...)`)

---

### XML Documentation Comments

- XML doc comments (`/// <summary>`) appear on public methods and classes in the lower-layer projects, but are absent from most `FrigateMQTTMainLogic` classes.
  - Evidence: `Source/FrigateMQTTConfiguration/ConfigurationLoader.cs` lines 7–8, 19–23, 38–41 (class and both public methods documented)
  - Evidence: `Source/FrigateMQTTProcessingMain/FrigateMain.cs` lines 20–21, 43–44, 56–57 (class, `Start()`, `Stop()` all documented)
  - Not present on: `Main`, `Pushover`, `TriggerCamera`, `WorkerQueue` in `FrigateMQTTMainLogic`

---

### Async / Await

- `async Task` and `async Task<bool>` are used for all I/O methods. No `async void`.
- `Task.WaitAll()` (blocking) is used inside the MQTT message handler to wait on enqueued work tasks — this is a synchronous wait inside an async context.
  - Evidence: `Source/FrigateMQTTMainLogic/Main.cs` lines 163, 198, 233
- `.Result` blocking access appears on `HttpResponseMessage.Content.ReadAsStringAsync()` in both `Pushover` and `TriggerCamera`.
  - Evidence: `Source/FrigateMQTTMainLogic/Pushover.cs` line 90; `Source/FrigateMQTTMainLogic/TriggerCamera.cs` line 40
- `FrigateMain.Start()` calls `_main.Run(...).Result` to block on the async startup, acceptable in a synchronous Topshelf `Start()` method.
  - Evidence: `Source/FrigateMQTTProcessingMain/FrigateMain.cs` line 66

---

### Locking Pattern

- A single `private readonly object _locker = new object();` guards the `Start()`/`Stop()` race in `FrigateMain`. The lock comment explicitly states its purpose.
  - Evidence: `Source/FrigateMQTTProcessingMain/FrigateMain.cs` line 27 (field), lines 48 and 85 (usage with inline comment `//prevent stop being called before start finishes`)

---

### Dependency Injection Style

- No DI container. Objects are constructed manually via `new` and dependencies passed through constructors.
- `ILogger` (Serilog) and `ITracer` (OpenTracing) are injected via constructor parameters at every level.
  - Evidence: `Source/FrigateMQTTMainLogic/Pushover.cs` lines 29–33 (constructor accepts `ILogger`, `ITracer`)
  - Evidence: `Source/FrigateMQTTMainLogic/TriggerCamera.cs` lines 17–21 (constructor accepts `ITracer`, `ILogger`)
  - Evidence: `Source/FrigateMQTTProcessingMain/FrigateMain.cs` lines 36–38 (constructor accepts `ILogger`)
- Handler classes (`Pushover`, `TriggerCamera`) are instantiated fresh per queue message dispatch, not cached as fields on `Main`.
  - Evidence: `Source/FrigateMQTTMainLogic/Main.cs` lines 277–279 (`var push = new Pushover(_logger, _tracer)`), lines 283–284 (`var camera = new TriggerCamera(_tracer, _logger)`)

---

### Static HttpClient Singleton Pattern

- Both `Pushover` and `TriggerCamera` declare `private static readonly HttpClient HttpClient` initialized in a static constructor. This is the correct pattern to avoid socket exhaustion.
  - Evidence: `Source/FrigateMQTTMainLogic/Pushover.cs` lines 20–27
  - Evidence: `Source/FrigateMQTTMainLogic/TriggerCamera.cs` lines 15–24

---

### Configuration Style (SharpConfig INI-to-POCO)

- `ConfigurationLoader` reads the INI file as a string and passes it to `SharpConfig.Configuration.LoadFromString()`. The result is stored in a `ConfigurationSettings` wrapper that exposes typed POCO properties.
  - Evidence: `Source/FrigateMQTTConfiguration/ConfigurationLoader.cs` lines 28–35
- POCO constructors set default values for optional fields.
  - Evidence: `Source/FrigateMQTTConfiguration/PushoverSettings.cs` lines 11–14 (`NotifySleepTime = 30` set in constructor)
- `SubscriptionSettings` has no constructor defaults — all fields are optional-by-absence in the INI.
  - Evidence: `Source/FrigateMQTTConfiguration/SubscriptionSettings.cs`
- `[Serializable]` attribute is applied to `SubscriptionSettings` (required by SharpConfig for section list reflection).
  - Evidence: `Source/FrigateMQTTConfiguration/SubscriptionSettings.cs` line 9

---

### Logging Style (Serilog)

- The injected `ILogger _logger` instance is used for most logging calls. However, `Log.Information()` / `Log.Debug()` (global static logger) is also used directly in `Main.cs`, requiring `Log.Logger` to be set at startup.
  - Evidence: `Source/FrigateMQTTMainLogic/Main.cs` lines 155, 160, 189, 195, 223–225, 259 (static `Log.*` calls)
  - Evidence: `Source/FrigateMQTTProcessingService/Program.cs` line 47 (`Log.Logger = _logger`)
- Message templates: **string interpolation** (`$"..."`) is used inside Serilog calls rather than structured message templates with `{Property}` placeholders. This loses structured logging benefits.
  - Evidence: `Source/FrigateMQTTMainLogic/Main.cs` line 70 (`_logger.Information($"Have sub for {sub.Name} {sub.Zone}")`)
  - Evidence: `Source/FrigateMQTTMainLogic/TriggerCamera.cs` line 28 (`_logger.Information($"Triggering Camera {location}")`)
- **Incorrect Serilog overload**: `_logger.Error(ex.Message, ex)` is used in the MQTT handler catch block. The correct Serilog overload for exception logging is `_logger.Error(ex, "message template")` — the exception must be the first argument. As written, `ex.Message` becomes the message template and `ex` is treated as a property value, which loses the full stack trace in structured sinks.
  - Evidence: `Source/FrigateMQTTMainLogic/Main.cs` line 269

---

### Tracing Style (OpenTracing / Jaeger)

- Every I/O operation is wrapped in an OpenTracing span using `_tracer.BuildSpan("OperationName").StartActive(finishSpanOnDispose: true)` inside a `using` block.
  - Evidence: `Source/FrigateMQTTMainLogic/Pushover.cs` line 38 (`PushOver_SendAsync` span)
  - Evidence: `Source/FrigateMQTTMainLogic/TriggerCamera.cs` line 29 (`TriggerCamera_SendAsync` span)
  - Evidence: `Source/FrigateMQTTMainLogic/Main.cs` line 130 (`Frigate_Event_{CameraName}_{type}` span with dynamic name)
- Span tags use `scope.Span.SetTag(key, value)` for metadata; errors use `scope.Span.Log(e.ToString())` + `Tags.Error.Set(scope.Span, true)`.
  - Evidence: `Source/FrigateMQTTMainLogic/Pushover.cs` lines 96–97; `Source/FrigateMQTTMainLogic/TriggerCamera.cs` lines 46–47
- When `tracesettings.json` is absent, `NoopTracerFactory.Create()` is returned, so all tracer calls are no-ops.
  - Evidence: `Source/FrigateMQTTProcessingMain/FrigateMain.cs` lines 104–105

---

### Error Handling Pattern

- I/O methods (`SendAsync` in `Pushover`, `TriggerCamera`) use try/catch to log the span error and then **re-throw** the exception — they do not swallow it.
  - Evidence: `Source/FrigateMQTTMainLogic/Pushover.cs` lines 94–99 (`catch ... throw`)
  - Evidence: `Source/FrigateMQTTMainLogic/TriggerCamera.cs` lines 44–49 (`catch ... throw`)
- The queue message handler (`ClientOnApplicationMessageReceivedAsync`) catches `Exception` broadly and logs it, **not** re-throwing — preventing queue worker crashes from propagating.
  - Evidence: `Source/FrigateMQTTMainLogic/Main.cs` lines 267–270
- `FrigateMain.Start()` / `Stop()` catch `Exception` and log at `Fatal` level without re-throwing, keeping Topshelf stable.
  - Evidence: `Source/FrigateMQTTProcessingMain/FrigateMain.cs` lines 72–75, 93–98
- Success/failure is communicated via `Task<bool>` return values from `Run()`, `Send()`, `SendAsync()` on `WorkerQueue`.
  - Evidence: `Source/FrigateMQTTMainLogic/Main.cs` line 33 (`Task<bool> Run`); `Source/FrigateMQTTMainLogic/Queue.cs` lines 97–107

---

### Commented-Out Code (Deliberate Convention)

- Large blocks of commented-out code are retained inline as documentation of intentionally disabled paths:
  - Frigate API snapshot image fetch (two alternative URLs) — replaced by Blue Iris image source.
    - Evidence: `Source/FrigateMQTTMainLogic/Pushover.cs` lines 45–58
  - CodeProject.AI second-pass confirmation call — entire method body is present but the invocation is commented out.
    - Evidence: `Source/FrigateMQTTMainLogic/Pushover.cs` lines 45–50 (call site), lines 103–141 (full method retained)
  - App.Metrics text file reporter configuration.
    - Evidence: `Source/FrigateMQTTProcessingMain/FrigateMain.cs` lines 131–135
  - DotNetWorkQueue tracer registration alternatives.
    - Evidence: `Source/FrigateMQTTMainLogic/Queue.cs` lines 64–66, 74–76
  - Payload logging line.
    - Evidence: `Source/FrigateMQTTMainLogic/Main.cs` line 93 (`//Log.Logger.Information(payload)`)
- This is an explicit in-code documentation strategy, not code hygiene debt. CLAUDE.md explicitly calls it out.

---

### Project Layout and Assembly Info

- Three legacy-style projects (`FrigateMQTTConfiguration`, `FrigateMQTTLogging`, `FrigateMQTTMainLogic`) use `Properties/AssemblyInfo.cs` per-project.
  - Evidence: `Source/FrigateMQTTConfiguration/Properties/AssemblyInfo.cs`, `Source/FrigateMQTTLogging/Properties/AssemblyInfo.cs`, `Source/FrigateMQTTMainLogic/Properties/AssemblyInfo.cs`
- Two SDK-style projects (`FrigateMQTTProcessingMain`, `FrigateMQTTProcessingService`) generate `AssemblyInfo.cs` into `obj/` at build time — no hand-authored `Properties/AssemblyInfo.cs`.
  - Evidence: `Source/FrigateMQTTProcessingMain/obj/Debug/net48/FrigateMQTTProcessingMain.AssemblyInfo.cs` (generated)
- No `SharedAssemblyInfo.cs` at the solution root was found. [Inferred] Version metadata is managed independently per project.

---

### Import Ordering

- `using` directives: BCL namespaces first, then third-party (alphabetical within each group), then project-local namespaces last. Not enforced by tooling — observed pattern only.
  - Evidence: `Source/FrigateMQTTMainLogic/Main.cs` lines 1–15 (DotNetWorkQueue, FrigateMQTTConfiguration, MQTTnet, OpenTracing, Serilog, then System.* namespaces)
  - Evidence: `Source/FrigateMQTTMainLogic/Pushover.cs` lines 1–13 (FrigateMQTTConfiguration, Newtonsoft.Json, OpenTracing, RestSharp, Serilog, then System.*)

---

## Summary Table

| Convention | Detail | Confidence |
|------------|--------|------------|
| Private field prefix | `_camelCase` throughout | Observed |
| Public property casing | PascalCase auto-properties | Observed |
| `var` usage | Free use where type is obvious | Observed |
| XML doc comments | On public members in lower-layer projects; absent in MainLogic | Observed |
| Async pattern | `async Task` / `async Task<bool>`; `.Result` blocking in two places | Observed |
| Lock idiom | `private readonly object _locker = new object()` | Observed |
| DI style | Manual constructor injection, no container | Observed |
| HttpClient | Static singleton per class via static constructor | Observed |
| Config | SharpConfig INI-to-POCO; defaults in POCO constructors | Observed |
| Logging | Serilog via injected `_logger` + global `Log.*`; string interpolation (not templates) | Observed |
| Serilog error overload | `_logger.Error(ex.Message, ex)` — arguments reversed from correct Serilog signature | Observed |
| Tracing | OpenTracing `BuildSpan(...).StartActive(finishSpanOnDispose: true)` around each I/O call | Observed |
| Error handling | Re-throw in I/O methods; swallow-and-log in queue handlers | Observed |
| Commented code | Intentionally retained as in-place design documentation | Observed |
| SharedAssemblyInfo | Not present; legacy projects use per-project AssemblyInfo.cs | Observed |

## Open Questions

- The `QuoteChar = @"\"` constant in `Pushover.cs` (line 19) is declared but never referenced in the file. Its intended use is unclear.
- Import ordering appears consistent but is not enforced by an `.editorconfig` or StyleCop ruleset — no such files were found in the repository.
