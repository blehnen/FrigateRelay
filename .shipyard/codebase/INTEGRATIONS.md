# INTEGRATIONS.md

## Overview

This service integrates with five external systems: an MQTT broker (inbound events from Frigate NVR), Blue Iris NVR (HTTP trigger + snapshot image fetch), Pushover REST API (push notification delivery), Jaeger (optional distributed tracing), and Seq (optional structured log aggregation). A sixth integration — CodeProject.AI for secondary object-detection confirmation — exists in the codebase but is entirely commented out. All outbound HTTP connections share a globally disabled TLS certificate validation callback set at process startup.

## Metrics

| Metric | Value |
|--------|-------|
| Active external integrations | 4 (MQTT broker, Blue Iris, Pushover, Seq/Jaeger optional) |
| Commented-out integrations | 1 (CodeProject.AI) |
| Hard-coded IP addresses | 1 (`http://192.168.0.58:5001` for CodeProject.AI) |
| TLS validation | Disabled globally for all connections (accepts all certs) |
| MQTT topics subscribed | 1 (`frigate/events`) |

## Findings

### Global TLS Bypass

- **ServerCertificateValidationCallback set to always return `true`** at process startup. This affects all `HttpClient`, `WebRequest`, and MQTT TLS connections made by the process — there is no scoping to specific hosts.
  - Evidence: `Source/FrigateMQTTProcessingService/Program.cs` lines 21-23:
    ```csharp
    ServicePointManager.ServerCertificateValidationCallback +=
        (sender, cert, chain, sslPolicyErrors) => true;
    ```
  - Rationale documented in CLAUDE.md: intentional to accept self-signed certificates on LAN-hosted MQTT/Frigate/BlueIris hosts.

---

### 1. MQTT Broker (Frigate NVR event source)

- **Protocol**: MQTT over TCP. No TLS configuration observed in the `MqttClientOptionsBuilder` — connection is plaintext by default unless the broker forces TLS (which the global callback above would bypass anyway).
- **Broker address**: Configured at runtime via `[ServerSettings] Server` in `FrigateMQTTProcessingService.conf`. Not hard-coded.
  - Evidence: `Source/FrigateMQTTMainLogic/Main.cs` line 55: `.WithTcpServer(settings.ServerSettings.Server)`
- **Client ID**: `"FrigateProcessing"` (hard-coded).
  - Evidence: `Source/FrigateMQTTMainLogic/Main.cs` line 53: `.WithClientId("FrigateProcessing")`
- **Topic subscribed**: `frigate/events` (single topic, hard-coded).
  - Evidence: `Source/FrigateMQTTMainLogic/Main.cs` line 65: `new MqttTopicFilterBuilder().WithTopic("frigate/events").Build()`
- **Auto-reconnect delay**: 5 seconds (hard-coded).
  - Evidence: `Source/FrigateMQTTMainLogic/Main.cs` line 52: `WithAutoReconnectDelay(TimeSpan.FromSeconds(5))`
- **Payload format**: JSON, deserialized via `Newtonsoft.Json` into `FrigateEvent` (fields: `type`, `before`, `after`; each snapshot has `camera`, `label`, `id`, `current_zones`, `entered_zones`, `stationary`).
  - Evidence: `Source/FrigateMQTTMainLogic/Main.cs` line 94
  - Evidence: `Source/FrigateMQTTMainLogic/FrigateEvent.cs` (file listed in csproj compile items)
- **Event types handled**: `new`, `update` (non-stationary only), and any other type treated as `end` (non-stationary only).
  - Evidence: `Source/FrigateMQTTMainLogic/Main.cs` lines 134, 165, 200

---

### 2. Blue Iris NVR

Blue Iris is used in two distinct ways: as a camera trigger target and as a snapshot image source.

#### 2a. Camera Trigger (HTTP GET)

- **Transport**: HTTP GET via `System.Net.Http.HttpClient` (static singleton).
- **URL**: The full trigger URL is stored per-subscription as `[SubscriptionSettings] Camera` in `FrigateMQTTProcessingService.conf`. The value is passed verbatim to `HttpClient.GetAsync`.
  - Evidence: `Source/FrigateMQTTMainLogic/TriggerCamera.cs` line 36: `HttpClient.GetAsync(location, token)`
  - Evidence: `Source/FrigateMQTTMainLogic/Main.cs` line 285: `camera.SendAsync(arg1.Body.Sub.Camera, ...)`
- **TLS**: TLS 1.2 forced per-call via `ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12`, but the global callback means any certificate is accepted.
  - Evidence: `Source/FrigateMQTTMainLogic/TriggerCamera.cs` line 34
- **Error handling**: Non-2xx response throws `ApplicationException`. Exception is logged and re-thrown; DotNetWorkQueue retry policy handles retries (3/6/9 seconds).

#### 2b. Snapshot Image Fetch (HTTP GET for Pushover attachment)

- **URL pattern**: `ServerSettings.BlueIrisImages + cameraShortName` — the base URL prefix is configured in `[ServerSettings] BlueIrisImages`; the per-subscription `CameraShortName` is appended to form the full image URL.
  - Evidence: `Source/FrigateMQTTMainLogic/Pushover.cs` line 60: `var imageLocation = serverSettings.BlueIrisImages + cameraShortName`
- **Transport**: `ImageLoader.LoadImage(new Uri(imageLocation))` — returns `byte[]`.
  - Evidence: `Source/FrigateMQTTMainLogic/Pushover.cs` line 64
- **Purpose**: Image bytes are attached to the Pushover notification. Blue Iris is preferred over Frigate's own snapshot API because deployment-specific Frigate snapshot quality is lower (see CLAUDE.md).

---

### 3. Pushover (Push Notification)

- **Endpoint**: `https://api.pushover.net/1/messages.json` (hard-coded).
  - Evidence: `Source/FrigateMQTTMainLogic/Pushover.cs` line 82
- **Transport**: `System.Net.Http.HttpClient.PostAsync` with `multipart/form-data` body.
- **Auth**: `token` (app token) and `user` (user key) are form fields populated from `[PushoverSettings] AppToken` and `UserKey` in `FrigateMQTTProcessingService.conf`.
  - Evidence: `Source/FrigateMQTTMainLogic/Pushover.cs` lines 71-72
- **Payload fields**:
  - `token` — Pushover app token
  - `user` — Pushover user key
  - `message` — formatted as `"<ObjectName> found on <LocationName>"`
  - `attachment` — JPEG image bytes fetched from Blue Iris, sent as `image/jpg` named `image.jpg`
  - Evidence: `Source/FrigateMQTTMainLogic/Pushover.cs` lines 71-78
- **TLS**: TLS 1.2 forced per-call; global callback accepts all certs.
  - Evidence: `Source/FrigateMQTTMainLogic/Pushover.cs` line 68
- **Throttle**: Per-camera cooldown governed by `[PushoverSettings] NotifySleepTime` (seconds, default 30). Implemented via `System.Runtime.Caching.MemoryCache` keyed on `sub.Camera`.
  - Evidence: `Source/FrigateMQTTMainLogic/Main.cs` lines 141-154
- **Static HttpClient**: `Pushover` uses a static `HttpClient` instance (initialized once in a static constructor). This is correct for connection reuse but means the `ServerCertificateValidationCallback` set at startup applies.
  - Evidence: `Source/FrigateMQTTMainLogic/Pushover.cs` lines 21-27

#### Commented-out Frigate snapshot paths

Two alternate image sources exist in `Pushover.SendAsync` but are commented out — they would fetch the snapshot or thumbnail directly from the Frigate API using `ServerSettings.FrigateApi`:
```csharp
// serverSettings.FrigateApi + "/api/events/" + eventId + "/snapshot.jpg?bbox=1"
// serverSettings.FrigateApi + "/api/events/" + eventId + "/thumbnail.jpg"
```
Evidence: `Source/FrigateMQTTMainLogic/Pushover.cs` lines 52-58.

---

### 4. Jaeger (Distributed Tracing — Optional)

- **Client**: Jaeger .NET client `1.0.3`, via `Jaeger.Configuration.FromIConfiguration`.
- **Activation**: Only when `tracesettings.json` is present in the exe directory. Without it, a `NoopTracer` is used and all `_tracer.BuildSpan(...)` calls are no-ops.
  - Evidence: `Source/FrigateMQTTProcessingMain/FrigateMain.cs` lines 103-113
- **Config section**: `tracesettings.json` must contain a `Jaeger` section compatible with `Jaeger.Configuration.FromIConfiguration`.
  - Evidence: `Source/FrigateMQTTProcessingMain/FrigateMain.cs` line 110: `.GetSection("Jaeger")`
- **Instrumented spans**: `Frigate_Event_<CameraName>_<type>` (per event match), `TriggerCamera_SendAsync`, `PushOver_SendAsync`.
  - Evidence: `Source/FrigateMQTTMainLogic/Main.cs` line 130; `Source/FrigateMQTTMainLogic/TriggerCamera.cs` line 29; `Source/FrigateMQTTMainLogic/Pushover.cs` line 38

---

### 5. Seq (Structured Log Aggregation — Optional)

- **Sink**: `Serilog.Sinks.Seq` `5.2.2`.
- **Configuration**: Seq server URL is read from `Configuration/logging.settings` (flat key=value format). No URL is hard-coded in source.
  - Evidence: `Source/FrigateMQTTLogging/packages.config` line 9
  - Evidence: CLAUDE.md: "Configs/logging.settings contains the previous author's local paths and Seq URL and should be treated as a template"
- **Activation**: Seq sink is only configured if `logging.settings` is present; fallback is a minimal startup logger.
  - Evidence: `Source/FrigateMQTTProcessingService/Program.cs` lines 38-40

---

### 6. CodeProject.AI (COMMENTED OUT — NOT ACTIVE)

- **Status**: Entire code path is disabled. The method `CodeProjectConfirms` in `Pushover.cs` exists but is never called; its call site in `SendAsync` is commented out.
  - Evidence: `Source/FrigateMQTTMainLogic/Pushover.cs` lines 45-50 (call site), lines 103-141 (method body)
- **Endpoint**: Hard-coded `http://192.168.0.58:5001` — a local LAN IP with no configuration hook.
  - Evidence: `Source/FrigateMQTTMainLogic/Pushover.cs` line 111: `new RestClient(new Uri("http://192.168.0.58:5001"))`
- **API path**: `POST v1/vision/detection` with a `multipart/form-data` image field `image`.
  - Evidence: `Source/FrigateMQTTMainLogic/Pushover.cs` lines 113-115
- **Purpose**: Would have performed second-pass object-detection confirmation (person/car/truck at >50% confidence) before sending Pushover, using a Frigate snapshot fetched via `FrigateApi`. CLAUDE.md notes this should be moved to `ServerSettings` if re-enabled.
- **HTTP client**: Uses `RestSharp 108.0.3` (not the BCL `HttpClient` used by the active paths).

---

## Summary Table

| Integration | Direction | Protocol | URL / Address | Config Source | Active |
|-------------|-----------|----------|---------------|---------------|--------|
| MQTT Broker (Frigate) | Inbound subscribe | MQTT/TCP | `[ServerSettings] Server` | `.conf` file | Yes |
| Blue Iris trigger | Outbound GET | HTTP(S) | `[SubscriptionSettings] Camera` | `.conf` file | Yes |
| Blue Iris snapshot | Outbound GET | HTTP(S) | `[ServerSettings] BlueIrisImages` + `CameraShortName` | `.conf` file | Yes |
| Pushover API | Outbound POST | HTTPS | `https://api.pushover.net/1/messages.json` | Hard-coded URL, credentials in `.conf` | Yes |
| Jaeger tracing | Outbound | UDP/HTTP (Jaeger agent) | From `tracesettings.json` | JSON file | Optional |
| Seq logging | Outbound | HTTP | From `logging.settings` | Key-value file | Optional |
| CodeProject.AI | Outbound POST | HTTP | `http://192.168.0.58:5001` (hard-coded) | None | No (commented out) |
| Frigate snapshot API | Outbound GET | HTTP(S) | `[ServerSettings] FrigateApi` + `/api/events/{id}/snapshot.jpg` | `.conf` file | No (commented out) |

## Open Questions

- What MQTT port and authentication (username/password, TLS) does the target broker require? No auth fields are configured in the `MqttClientOptionsBuilder` — if the broker requires credentials, they are missing.
- What is the exact format of `tracesettings.json`? No example or schema is checked into the repo.
- `FrigateApi` is listed as a `[ServerSettings]` field in CLAUDE.md but is only used in commented-out code paths. Is it still required in the `.conf` file for the service to start without error?
