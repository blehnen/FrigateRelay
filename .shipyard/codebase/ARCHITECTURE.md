# ARCHITECTURE.md

## Overview

FrigateMQTTProcessing is a single-process Windows service that bridges a Frigate NVR's MQTT event stream to two downstream actions: an HTTP GET trigger to Blue Iris (camera alert) and a multipart POST to Pushover (push notification with a Blue Iris snapshot image). The service is structured as five strictly-layered .NET Framework 4.8 projects, hosted by Topshelf so the same binary can run as a console app or an installed Windows service. All event routing, deduplication, and fan-out logic lives in `FrigateMQTTMainLogic` — the other projects are infrastructure wrappers.

## Metrics

| Metric | Value |
|--------|-------|
| Projects | 5 |
| Entry point binary | `FrigateMQTTProcessingService.exe` |
| MQTT topics subscribed | 1 (`frigate/events`) |
| Work queues | 2 (`camera`, `pushover`) |
| Worker threads per queue | 2 |
| Retry delays (on any Exception) | 3 s, 6 s, 9 s |
| Pushover cooldown (configurable) | `PushoverSettings.NotifySleepTime` seconds (default 30) |
| MQTT reconnect delay | 5 s (hard-coded) |
| Topshelf service recovery restarts | 3 (at 0, 1, 2 days) |

## Findings

### Layer Diagram

```
FrigateMQTTProcessingService        ← exe; Topshelf host, logger bootstrap, TLS callback
        │  references
        ▼
FrigateMQTTProcessingMain           ← IServiceControl wrapper: Start/Stop, metrics+tracing setup
        │  references
        ▼
FrigateMQTTMainLogic                ← workhorse: MQTT client, event routing, queues, handlers
        │  references
        ├──► FrigateMQTTConfiguration   ← SharpConfig-backed POCO settings loader
        └──► FrigateMQTTLogging         ← Serilog configuration helper
```

Higher layers reference only lower layers; there are no upward or cross-sibling references.

- Evidence: `Source/FrigateMQTTProcessingService/Program.cs` — `using FrigateMQTTProcessingMain;`
- Evidence: `Source/FrigateMQTTProcessingMain/FrigateMain.cs` — `using FrigateMQTTMainLogic; using FrigateMQTTConfiguration;`
- Evidence: `Source/FrigateMQTTMainLogic/Main.cs` — `using FrigateMQTTConfiguration;`

### Naming Confusion: "ProcessingMain" vs "MainLogic"

`FrigateMQTTProcessingMain` is the *service lifecycle wrapper* — it implements `Start()`/`Stop()` and wires up metrics and tracing, but it does not process any events. The actual MQTT connection, subscription, event dispatch, and queue management all live in `FrigateMQTTMainLogic/Main.cs`. The naming is the inverse of what it implies.

- Evidence: `Source/FrigateMQTTProcessingMain/FrigateMain.cs` line 65 — `_main = new Main(); var started = _main.Run(...).Result;`
- Evidence: `Source/FrigateMQTTMainLogic/Main.cs` line 33 — `public async Task<bool> Run(ILogger logger, ConfigurationSettings settings, IMetrics metrics, ITracer tracing)`

### End-to-End Event Flow

**Step 1 — Bootstrap (Program.cs → FrigateMain.Start)**

`Program.cs` (lines 30–40) resolves the exe directory, loads `Configuration\logging.settings` via `FrigateMQTTLogging.LoggingSetup.Startup`, sets `Log.Logger` as the global Serilog instance, installs a global `ServerCertificateValidationCallback` that accepts all TLS certificates (line 22–23), and hands control to Topshelf which calls `FrigateMain.Start()`.

`FrigateMain.Start()` (`FrigateMQTTProcessingMain/FrigateMain.cs` lines 44–76) acquires `_locker`, creates a `CancellationTokenSource`, loads `ConfigurationLoader.LoadConfiguration()`, builds two `IMetrics` instances (API + queue), sets up an `ITracer` (Jaeger or Noop), then constructs and calls `Main.Run()`.

**Step 2 — Queue and MQTT Client Initialization (Main.Run)**

`Main.Run()` (`FrigateMQTTMainLogic/Main.cs` lines 33–81):
1. Instantiates `WorkerQueue<FrigateCameraMessage>` named `"camera"` wired to `ProcessActionCamera`.
2. Instantiates `WorkerQueue<FrigatePushOverMessage>` named `"pushover"` wired to `ProcessActionPushover`.
3. Starts both queues.
4. Builds a `ManagedMqttClient` with `ClientId = "FrigateProcessing"`, TCP server from `ServerSettings.Server`, auto-reconnect delay 5 s.
5. Subscribes to topic `frigate/events`.
6. Pre-populates `_cameraEventTracker` (a `ConcurrentDictionary<string,string>`) with one entry per configured subscription's `CameraName`.

**Step 3 — Receive and Deserialize**

`ClientOnApplicationMessageReceivedAsync` (`Main.cs` lines 83–273) fires for every MQTT message. It checks the topic is exactly `frigate/events`, then deserializes the UTF-8 JSON payload into `FrigateEvent` (which has `type` ∈ {`new`, `update`, `end`}, plus `before` and `after` snapshots of the tracked object).

- Evidence: `Source/FrigateMQTTMainLogic/Main.cs` line 94 — `JsonConvert.DeserializeObject<FrigateEvent>(payload)`
- Evidence: `Source/FrigateMQTTMainLogic/FrigateEvent.cs` — `FrigateEvent`, `FrigateEventBefore`, `FrigateEventAfter` POCOs

**Step 4 — Per-Subscription Match**

For each `SubscriptionSettings` in `_settings.Subscriptions`, the handler checks:
1. `sub.CameraName` equals `data.before.camera` (case-insensitive).
2. `sub.ObjectName` equals `data.before.label` (case-insensitive).
3. If `sub.Zone` is non-empty: zone must appear in any of `before.current_zones`, `before.entered_zones`, `after.current_zones`, or `after.entered_zones`.

The first matching subscription wins (`break` at line 237); remaining subscriptions are not evaluated.

- Evidence: `Source/FrigateMQTTMainLogic/Main.cs` lines 115–128

**Step 5 — Event Type Branching and Stationary Guard**

Within a matched subscription, dispatch branches on `data.type`:
- `"new"`: always enqueues to `_cameraQueue`; conditionally enqueues to `_pushoverQueue` (dedupe check below).
- `"update"` where `!data.after.stationary`: same fan-out as `"new"`.
- Any other type (i.e., `"end"`) where `!data.after.stationary`: same fan-out.
- If `data.after.stationary` is `true` on an `update` or `end`, no action is taken.

- Evidence: `Source/FrigateMQTTMainLogic/Main.cs` lines 134–233

**Step 6 — Deduplication (MemoryCache throttle)**

The camera trigger (`_cameraQueue`) fires on every qualifying event. The Pushover notification is throttled per `sub.Camera` (the Blue Iris trigger URL, not the Frigate camera id) using `MemoryCache.Default` (`_cameraLastEvent`):
- If the cache key for `sub.Camera` is absent: enqueue Pushover message, insert cache entry with `AbsoluteExpiration = DateTimeOffset.Now + NotifySleepTime seconds`.
- If the cache key exists: skip Pushover (log at Debug level).

Note: the cache key is `sub.Camera` (the HTTP trigger URL field from config), not `sub.CameraName`. Multiple subscriptions sharing the same `Camera` URL share one cooldown bucket.

- Evidence: `Source/FrigateMQTTMainLogic/Main.cs` lines 141–158 (`new` branch)
- Evidence: `Source/FrigateMQTTMainLogic/Main.cs` lines 175–191 (`update` branch)

**Step 7 — Downstream Handlers**

`TriggerCamera.SendAsync` (`FrigateMQTTMainLogic/TriggerCamera.cs`):
- Issues an `HttpClient.GetAsync` to `sub.Camera` (the Blue Iris HTTP trigger URL).
- Uses a static `HttpClient` instance (shared across all calls).
- Enforces TLS 1.2 via `ServicePointManager.SecurityProtocol`.
- Wraps the call in an OpenTracing span `"TriggerCamera_SendAsync"`.

`Pushover.SendAsync` (`FrigateMQTTMainLogic/Pushover.cs`):
- Fetches image bytes from `ServerSettings.BlueIrisImages + cameraShortName` via `ImageLoader.LoadImage`.
- Builds a `MultipartFormDataContent` with `token`, `user`, `message`, and `attachment` (image/jpg).
- POSTs to `https://api.pushover.net/1/messages.json`.
- Wraps in an OpenTracing span `"PushOver_SendAsync"`.
- Two alternative image sources are present but commented out: Frigate API snapshot (`/api/events/{id}/snapshot.jpg`) and thumbnail. A CodeProject.AI second-pass confirmation step is also fully implemented but commented out (hard-codes `http://192.168.0.58:5001`).

- Evidence: `Source/FrigateMQTTMainLogic/Pushover.cs` lines 45–57 (commented Frigate paths), lines 103–141 (commented CodeProject.AI method)

**Step 8 — WorkerQueue internals**

`WorkerQueue<T>` (`FrigateMQTTMainLogic/Queue.cs`) wraps `DotNetWorkQueue` with an in-memory transport (`MemoryMessageQueueInit`). Configuration:
- `WorkerCount = 2` per queue.
- Retry on any `Exception`: 3 s, 6 s, 9 s delays.
- Separate `QueueContainer` instances for producer and consumer, sharing a `ICreationScope` for the in-memory connection.
- Both `App.Metrics` and (partially commented) `ITracer` are registered in the DI container.

- Evidence: `Source/FrigateMQTTMainLogic/Queue.cs` lines 82–87

### Service Lifecycle

**Start sequence** (`FrigateMQTTProcessingMain/FrigateMain.cs` lines 44–76):
1. Acquire `_locker`.
2. Create `CancellationTokenSource`.
3. `ConfigurationLoader.LoadConfiguration()` — reads `FrigateMQTTProcessingService.conf` from CWD.
4. `SetupMetrics()` × 2 — builds App.Metrics with 3-second reporting scheduler.
5. `SetupTracing()` — loads `tracesettings.json` if present, else returns `NoopTracerFactory.Create()`.
6. `new Main()` + `Main.Run()` — blocks via `.Result` until MQTT client is connected and queues are started.
7. Release `_locker`.

**Stop sequence** (`FrigateMQTTProcessingMain/FrigateMain.cs` lines 81–100):
1. Acquire `_locker` (blocks until Start completes if called concurrently).
2. `_cancelToken.Cancel()` — signals `CancellationToken` propagated to queue worker handlers.
3. `_main.Dispose()`:
   a. Deregisters MQTT message handler.
   b. `_client.StopAsync().Wait(5000)` — 5-second graceful MQTT disconnect.
   c. `_client.Dispose()`.
   d. `_cameraQueue.Dispose()` — disposes producer, consumer, containers, creation scope.
   e. `_pushoverQueue.Dispose()`.
4. `_appMetricsTaskScheduler.Dispose()`.
5. `_cancelToken.Dispose()`.
6. Release `_locker`.

The `_locker` on both `Start` and `Stop` prevents a race where `Stop` could be called before `Start` finishes initializing `_main`.

- Evidence: `Source/FrigateMQTTProcessingMain/FrigateMain.cs` lines 27, 48, 83 (`_locker` usage)
- Evidence: `Source/FrigateMQTTMainLogic/Main.cs` lines 300–308 (`Dispose` implementation)

### Cross-Cutting Concerns

**Logging — Serilog + Seq**
- Configured from `Configuration\logging.settings` (key-value file, Serilog.Settings.KeyValuePairs format).
- `FrigateMQTTLogging.LoggingSetup` handles construction; result set as both injected `ILogger` and global `Log.Logger`.
- `Main.cs` uses both the injected `_logger` and the static `Log` global (mixed pattern).
- Evidence: `Source/FrigateMQTTProcessingService/Program.cs` lines 38–47

**Metrics — App.Metrics**
- Two separate `IMetrics` instances: `metricsAPI` and `metricsQueue`.
- Reporting scheduler fires every 3 seconds; the `ToTextFile` reporter is commented out, so no output is currently written despite the scheduler running.
- Evidence: `Source/FrigateMQTTProcessingMain/FrigateMain.cs` lines 130–136 (commented `ToTextFile`)

**Tracing — Jaeger / Noop**
- `SetupTracing()` checks for `tracesettings.json` in `AppDomain.CurrentDomain.BaseDirectory`; absent = `NoopTracerFactory.Create()`.
- Spans are created in `ClientOnApplicationMessageReceivedAsync`, `TriggerCamera.SendAsync`, and `Pushover.SendAsync`.
- `ITracer` registration in `WorkerQueue` DI is commented out for both producer and consumer containers.
- Evidence: `Source/FrigateMQTTProcessingMain/FrigateMain.cs` lines 102–113
- Evidence: `Source/FrigateMQTTMainLogic/Queue.cs` lines 64–76 (commented tracer registration)

**TLS**
- Global `ServicePointManager.ServerCertificateValidationCallback` set to `(_, _, _, _) => true` in `Program.cs` line 22. Intentional — accepts self-signed certs on local network devices (MQTT broker, Blue Iris, Frigate).
- `TriggerCamera` and `Pushover` also set `ServicePointManager.SecurityProtocol = Tls12` per-call (redundant with the global but harmless).

## Summary Table

| Item | Detail | Confidence |
|------|--------|------------|
| Architectural pattern | Layered monolith, single process | Observed |
| Host framework | Topshelf (console + Windows service) | Observed |
| MQTT client | MQTTnet ManagedMqttClient, auto-reconnect 5 s | Observed |
| Event topic | `frigate/events` (sole subscription) | Observed |
| Fan-out targets | Blue Iris HTTP GET + Pushover multipart POST | Observed |
| Queue mechanism | DotNetWorkQueue in-memory, 2 workers/queue, 3×retry | Observed |
| Deduplication | `MemoryCache.Default`, keyed by `sub.Camera`, TTL = `NotifySleepTime` | Observed |
| Stationary guard | `update`/`end` events skipped if `data.after.stationary == true` | Observed |
| First-match-wins | `break` after first subscription match; remaining subs not evaluated | Observed |
| Metrics output | App.Metrics scheduler running but `ToTextFile` reporter commented out | Observed |
| Tracing output | Jaeger if `tracesettings.json` present; Noop otherwise | Observed |
| CodeProject.AI | Fully implemented second-pass confirmation, entirely commented out | Observed |
| Naming inversion | `ProcessingMain` = wrapper; `MainLogic/Main.cs` = actual workhorse | Observed |

## Open Questions

- The `_cancelToken` created in `FrigateMain.Start()` is cancelled in `Stop()` but is not passed into `Main.Run()` or the queue workers — only `IWorkerNotification.WorkerStopping.StopWorkToken` (from DotNetWorkQueue internals) reaches handlers. Confirm whether graceful cancellation propagates correctly to in-flight `TriggerCamera`/`Pushover` HTTP calls.
- `SetupMetrics` has a logic bug: `Path.GetDirectoryName("Metrics\metricsAPI.txt")` returns `"Metrics"`, then the code calls `Directory.Exists` and only creates the directory if it *exists* (condition is inverted — should be `!Directory.Exists`). Metrics folder may never be auto-created. Confirm runtime behavior.
- With `ToTextFile` commented out, what (if anything) consumes the App.Metrics output? Confirm whether metrics are wired to a sink elsewhere or are purely in-memory.
- `_cameraEventTracker` (`ConcurrentDictionary<string,string>`) is populated in `Run()` and written in `ClientOnApplicationMessageReceivedAsync` (line 236) but is never read. Clarify intended purpose.
