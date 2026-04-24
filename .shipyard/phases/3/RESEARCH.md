# Research: Phase 3 — Frigate MQTT Ingestion and EventContext Projection

## Context

FrigateRelay targets net10.0, SDK 10.0.107. Phase 1 delivered `FrigateRelay.Abstractions`
(`IEventSource`, `EventContext`, `IPluginRegistrar`, `PluginRegistrationContext`) and
`FrigateRelay.Host` (`PluginRegistrarRunner.RunAll` with an empty registrar list). Phase 3
adds `src/FrigateRelay.Sources.FrigateMqtt/` — the first concrete `IEventSource`.

Key constraints from PROJECT.md and CONTEXT-3.md:
- MQTTnet v5; no global `ServicePointManager`; per-client TLS callback only.
- `System.Text.Json` only; no Newtonsoft.Json.
- Scoped `IMemoryCache` per plugin; never `MemoryCache.Default`.
- `IAsyncEnumerable<EventContext>` pulled from a `Channel<EventContext>` bridge.
- D1: fire ALL matching subscriptions; D2: `RawPayload` stays `string`;
  D3: base64 thumbnail from payload when present; D4: no Testcontainers in Phase 3.

---

## MQTTnet v5 API Cookbook

### Version

**MQTTnet 5.1.0.1559** — published 2026-02-04 on NuGet. No dependencies.
Targets net8.0 and net10.0. Zero dependencies (BCL only).

Source: https://www.nuget.org/packages/MQTTnet (accessed 2026-04-24)

### Critical Breaking Change vs v4

`ManagedMqttClient` (the auto-reconnect wrapper from v4) is **completely removed** in v5.
There is no `MQTTnet.Extensions.ManagedClient` package. The `MqttFactory` class is split
into `MqttClientFactory` and `MqttServerFactory`. `WithAutoReconnectDelay` on
`ManagedMqttClientOptionsBuilder` no longer exists.

Source: https://github.com/dotnet/MQTTnet/wiki/Upgrading-guide (accessed 2026-04-24)

The CONTEXT-3.md implementation notes reference "ManagedMqttClient with auto-reconnect" —
this must be re-read as **a custom reconnect loop** using the plain `IMqttClient` with a
timer-based ping monitor. The ROADMAP's "5s reconnect delay" target is still achievable;
it moves from a framework option into application code.

### Namespaces (v5)

```
MQTTnet                          // MqttClientFactory, IMqttClient
MQTTnet.Client                   // MqttClientOptionsBuilder, MqttClientConnectResult
MQTTnet.Packets                  // MqttTopicFilterBuilder
MQTTnet.Protocol                 // MqttQualityOfServiceLevel
```

### Minimal Subscribe + Receive + Reconnect + TLS + Disposal

```csharp
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

var factory = new MqttClientFactory();
using var client = factory.CreateMqttClient();

// Build options once; reuse on every reconnect attempt
var options = new MqttClientOptionsBuilder()
    .WithTcpServer("192.168.1.10", 1883)
    .WithClientId("frigate-relay")
    .WithTlsOptions(o =>
    {
        // Per-client TLS callback — never ServicePointManager
        o.WithCertificateValidationHandler(_ => allowInvalidCerts);
    })
    .Build();

// Register message handler BEFORE connecting
client.ApplicationMessageReceivedAsync += async e =>
{
    ReadOnlyMemory<byte> payload = e.ApplicationMessage.PayloadSegment;
    string raw = Encoding.UTF8.GetString(payload.Span);
    // hand raw to Channel<EventContext> writer
    await writer.WriteAsync(ProjectToContext(raw), ct);
};

// Reconnect loop — timer-based (recommended; event-based has deadlock risk)
_ = Task.Run(async () =>
{
    while (!ct.IsCancellationRequested)
    {
        try
        {
            if (!await client.TryPingAsync(ct))
            {
                await client.ConnectAsync(options, ct);
                // Re-subscribe after reconnect
                var subOptions = factory.CreateSubscribeOptionsBuilder()
                    .WithTopicFilter("frigate/events",
                        MqttQualityOfServiceLevel.AtMostOnce)
                    .Build();
                await client.SubscribeAsync(subOptions, ct);
                logger.LogInformation("MQTT connected");
            }
        }
        catch (OperationCanceledException) { break; }
        catch (Exception ex) { logger.LogWarning(ex, "MQTT connect failed; retrying"); }
        finally
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
        }
    }
}, ct);

// Teardown: signal writer complete, then disconnect
writer.Complete();
await client.DisconnectAsync(
    new MqttClientDisconnectOptionsBuilder().Build(), CancellationToken.None);
logger.LogInformation("MQTT disconnected");
```

**Exact TLS property path:**
`MqttClientOptionsBuilder.WithTlsOptions(Action<MqttClientTlsOptionsBuilder>)` →
`MqttClientTlsOptionsBuilder.WithCertificateValidationHandler(
    Func<MqttClientCertificateValidationEventArgs, bool> handler)`

The handler receives `MqttClientCertificateValidationEventArgs` with `.Certificate`,
`.Chain`, and `.SslPolicyErrors`. Return `true` to bypass validation
(only when `AllowInvalidCertificates: true` in config). This is strictly per-client;
no global callback is touched.

Source: https://github.com/dotnet/MQTTnet/blob/master/Samples/Client/Client_Connection_Samples.cs

### Subscription Options Builder

```csharp
var subOptions = factory.CreateSubscribeOptionsBuilder()
    .WithTopicFilter("frigate/events", MqttQualityOfServiceLevel.AtMostOnce)
    .Build();
await client.SubscribeAsync(subOptions, ct);
```

`mqttFactory.CreateSubscribeOptionsBuilder()` is the factory helper; alternatively
construct `MqttClientSubscribeOptions` directly.

### Payload Access

```csharp
// e.ApplicationMessage.PayloadSegment is ReadOnlyMemory<byte> in v5
string raw = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment.Span);
```

The `Payload` property (byte[]) also exists but `PayloadSegment` avoids an extra copy.

---

## Frigate `frigate/events` Payload Schema

### Critical Finding: `thumbnail` is Always `null` in MQTT Messages

The official Frigate documentation (2026-04-24 snapshot) explicitly lists
`thumbnail: null` with the note "Always null in published messages." Snapshots are
published separately to camera-specific MQTT topics as JPEG frames, not embedded in
`frigate/events`.

Source: https://docs.frigate.video/integrations/mqtt/ (accessed 2026-04-24)

**Impact on D3:** `EventContext.SnapshotFetcher` cannot decode a base64 thumbnail from the
MQTT payload because the field is always null. D3 must be re-scoped: when `thumbnail` is
null, `SnapshotFetcher` should return `ValueTask.FromResult<byte[]?>(null)`.
The base64 decode path becomes dead code at Phase 3. This is an **open question** for the
architect — see Open Questions section.

### Type Values

| `type` | Meaning |
|--------|---------|
| `"new"` | First message when the object is no longer a false_positive |
| `"update"` | Better snapshot found, zone change, or frame update |
| `"end"` | Final message; `end_time` is now set |

### `before` / `after` Field Mapping

| Wire name (snake_case) | .NET type | Nullable | Notes |
|------------------------|-----------|----------|-------|
| `id` | `string` | No | Stable across all messages for the same detection |
| `camera` | `string` | No | Matches camera name in Frigate config |
| `label` | `string` | No | e.g. `"person"`, `"car"` |
| `sub_label` | `string?` | Yes | Recognized name/attribute |
| `score` | `double` | No | 0.0–1.0 confidence |
| `top_score` | `double` | No | Highest confidence across frames |
| `start_time` | `double` | No | Unix epoch **seconds** as float (e.g. `1714000000.123`) |
| `end_time` | `double?` | Yes | Null until `type == "end"` |
| `stationary` | `bool` | No | True when object movement has stopped |
| `active` | `bool` | No | Inverse of `stationary` |
| `false_positive` | `bool` | No | Still true on some edge-case updates |
| `current_zones` | `string[]` | No (empty array) | Active zone names |
| `entered_zones` | `string[]` | No (empty array) | All zones entered during lifetime |
| `has_snapshot` | `bool` | No | True when Frigate has saved a snapshot |
| `has_clip` | `bool` | No | True when a video clip exists |
| `thumbnail` | `string?` | Yes | **Always null** in published messages |
| `frame_time` | `double` | No | Unix epoch seconds of the captured frame |
| `box` | `int[]` | No | `[x1, y1, x2, y2]` bounding box pixels |
| `area` | `int` | No | Bounding box pixel area |
| `region` | `int[]` | No | `[x1, y1, x2, y2]` analysis region |
| `motionless_count` | `int` | No | Frames without position change |
| `position_changes` | `int` | No | Movement transitions |
| `attributes` | `object` | No | Top-scoring attributes dict |
| `current_attributes` | `object[]` | No | Frame-specific attribute details |

Timestamp convention: `start_time` and `end_time` are Unix epoch **seconds** expressed as
a JSON `number` (floating point double). Convert with
`DateTimeOffset.FromUnixTimeMilliseconds((long)(value * 1000))` or
`DateTimeOffset.UnixEpoch.AddSeconds(value)`.

Source: https://docs.frigate.video/integrations/mqtt/ (accessed 2026-04-24)

### Annotated JSON Sample — `new` Event

```json
{
  "type": "new",
  "before": {
    "id": "1714000001.123456-abc123",
    "camera": "front_door",
    "label": "person",
    "sub_label": null,
    "score": 0.84,
    "top_score": 0.84,
    "start_time": 1714000001.123,
    "end_time": null,
    "stationary": false,
    "active": true,
    "false_positive": false,
    "current_zones": [],
    "entered_zones": [],
    "has_snapshot": false,
    "has_clip": false,
    "thumbnail": null,
    "frame_time": 1714000001.1,
    "box": [100, 200, 300, 500],
    "area": 40000,
    "region": [0, 0, 1920, 1080],
    "motionless_count": 0,
    "position_changes": 0,
    "attributes": {},
    "current_attributes": []
  },
  "after": {
    "id": "1714000001.123456-abc123",
    "camera": "front_door",
    "label": "person",
    "sub_label": null,
    "score": 0.91,
    "top_score": 0.91,
    "start_time": 1714000001.123,
    "end_time": null,
    "stationary": false,
    "active": true,
    "false_positive": false,
    "current_zones": ["driveway"],
    "entered_zones": ["driveway"],
    "has_snapshot": true,
    "has_clip": false,
    "thumbnail": null,
    "frame_time": 1714000001.2,
    "box": [105, 205, 305, 505],
    "area": 40000,
    "region": [0, 0, 1920, 1080],
    "motionless_count": 0,
    "position_changes": 1,
    "attributes": {},
    "current_attributes": []
  }
}
```

### `update` with `stationary: true` (skip trigger)

```json
{
  "type": "update",
  "before": { "id": "...", "stationary": false, ... },
  "after":  { "id": "...", "stationary": true,  ... }
}
```

When `type` is `"update"` or `"end"` and `after.stationary == true`, the
`SubscriptionMatcher` must skip the event (stationary guard from CONTEXT-3.md).

---

## DTO Template

### Recommended Approach: `JsonNamingPolicy.SnakeCaseLower`

`JsonNamingPolicy.SnakeCaseLower` is available since .NET 8 and forward into .NET 10.
It eliminates per-property `[JsonPropertyName]` attributes for snake_case wire names.
Use a single shared `JsonSerializerOptions` instance.

```csharp
internal static class FrigateJsonOptions
{
    internal static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
```

Source: https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsonnamingpolicy.snakecaselower?view=net-8.0

### DTO Shape

DTOs are `internal` to `FrigateRelay.Sources.FrigateMqtt`; they never cross the
`IEventSource` boundary. `InternalsVisibleTo` exposes them to the test project.

```csharp
// Internal — never leaks into EventContext
internal sealed record FrigateEvent
{
    public required string Type { get; init; }          // "new" | "update" | "end"
    public required FrigateEventObject Before { get; init; }
    public required FrigateEventObject After { get; init; }
}

internal sealed record FrigateEventObject
{
    public required string Id { get; init; }
    public required string Camera { get; init; }
    public required string Label { get; init; }
    public string? SubLabel { get; init; }
    public double StartTime { get; init; }
    public double? EndTime { get; init; }
    public bool Stationary { get; init; }
    public bool FalsePositive { get; init; }
    public IReadOnlyList<string> CurrentZones { get; init; } = [];
    public IReadOnlyList<string> EnteredZones { get; init; } = [];
    public bool HasSnapshot { get; init; }
    public string? Thumbnail { get; init; }   // always null per Frigate docs
}
```

**Note on naming:** CONTEXT-3.md uses `FrigateEventBefore`/`FrigateEventAfter` as class
names; the before/after objects share an identical schema so a single `FrigateEventObject`
record avoids duplication. The architect should decide whether to name them separately
(matching CONTEXT-3.md) or merge. Either is correct.

**`[JsonPropertyName]` fallback:** Only needed for fields whose C# name after
SnakeCaseLower transformation would differ from the wire name (e.g. `SubLabel` →
`sub_label` is correct; no annotation needed). The architect should verify
`FalsePositive` → `false_positive` transforms correctly (it does with SnakeCaseLower).

### `EventContext` Projection from `FrigateEvent`

```csharp
new EventContext
{
    EventId      = evt.After.Id,
    Camera       = evt.After.Camera,
    Label        = evt.After.Label,
    Zones        = evt.After.CurrentZones
                       .Union(evt.After.EnteredZones)
                       .Union(evt.Before.CurrentZones)
                       .Union(evt.Before.EnteredZones)
                       .ToList(),
    StartedAt    = DateTimeOffset.UnixEpoch.AddSeconds(evt.After.StartTime),
    RawPayload   = rawJsonString,
    SnapshotFetcher = _ => ValueTask.FromResult<byte[]?>(null),
                    // D3: thumbnail always null in MQTT; fallback to null
}
```

**Zones aggregation:** The subscription matcher checks any of the four zone arrays per
CONTEXT-3.md. Projecting all four into `EventContext.Zones` via Union means the matcher
only needs one list. Alternatively, keep four separate arrays in the DTO and evaluate
them independently in `SubscriptionMatcher` — either approach works; the union is simpler
but loses which-array-triggered information (not needed in Phase 3).

---

## `Channel<T>` Bridging Pattern

### Recommended: Unbounded Channel

Workload is "dozens of events per minute" (PROJECT.md). At this rate, an unbounded
channel is appropriate — bounded channels with `FullMode.DropWrite` would silently lose
events. Back-pressure is not a concern at this volume.

```csharp
internal sealed class FrigateMqttEventSource : IEventSource
{
    private readonly Channel<EventContext> _channel =
        Channel.CreateUnbounded<EventContext>(
            new UnboundedChannelOptions { SingleWriter = true });

    public string Name => "FrigateMqtt";

    public IAsyncEnumerable<EventContext> ReadEventsAsync(CancellationToken ct)
        => _channel.Reader.ReadAllAsync(ct);

    // Called from ApplicationMessageReceivedAsync (MQTT push → channel write)
    private async ValueTask HandleMessageAsync(string raw, CancellationToken ct)
    {
        var evt = JsonSerializer.Deserialize<FrigateEvent>(raw, FrigateJsonOptions.Default);
        if (evt is null) return;

        var context = Project(evt, raw);
        await _channel.Writer.WriteAsync(context, ct);
    }

    // On shutdown:
    internal void CompleteChannel() => _channel.Writer.Complete();
}
```

### `ReadAllAsync` Termination Semantics

`ChannelReader<T>.ReadAllAsync(ct)` (introduced .NET Core 3.0) returns an
`IAsyncEnumerable<T>` using the pattern:
`while (await WaitToReadAsync(ct)) while (TryRead(out T item)) yield return item;`

When `Writer.Complete()` is called, `WaitToReadAsync` returns `false` once the channel
is drained, and the enumeration terminates normally (no exception). The host's
`ReadEventsAsync` loop exits cleanly — satisfying the graceful-shutdown criterion.

If the `CancellationToken` fires before `Writer.Complete()`, `ReadAllAsync` throws
`OperationCanceledException`, which the host's background service should catch and treat
as normal shutdown.

Source: https://learn.microsoft.com/en-us/dotnet/api/system.threading.channels.channelreader-1.readallasync?view=net-10.0

---

## Scoped `IMemoryCache` — Idiomatic .NET 10 Pattern

### The Problem

`services.AddMemoryCache()` registers a **singleton** `IMemoryCache`. If called by both
the host and the FrigateMqtt plugin, all callers share the same cache instance. The
`DedupeCache` key space collides across plugins, violating CLAUDE.md's invariant.

### Option A — Keyed Singleton (.NET 8+ built-in)

```csharp
// In FrigateMqttPluginRegistrar.Register():
context.Services.AddKeyedSingleton<IMemoryCache>(
    "FrigateMqtt",
    (_, _) => new MemoryCache(new MemoryCacheOptions()));

// In DedupeCache constructor:
public DedupeCache([FromKeyedServices("FrigateMqtt")] IMemoryCache cache, ...)
```

Requires `Microsoft.Extensions.Caching.Memory` which is already a transitive dep of
`Microsoft.Extensions.Hosting`. Uses the .NET 8 keyed services feature, fully supported
on .NET 10.

Source: https://andrewlock.net/exploring-the-dotnet-8-preview-keyed-services-dependency-injection-support/

### Option B — Private Instance Constructed in Registrar Body

```csharp
// In FrigateMqttPluginRegistrar.Register():
var cache = new MemoryCache(new MemoryCacheOptions());
context.Services.AddSingleton<IMemoryCache>(cache);   // NOT recommended — still shared
// Better: register DedupeCache directly with the cache embedded
context.Services.AddSingleton(_ => new DedupeCache(
    new MemoryCache(new MemoryCacheOptions()), options));
```

This hides `IMemoryCache` inside `DedupeCache`'s constructor, never registering it
under `IMemoryCache` in DI at all, avoiding any collision with host-level cache.

### Recommendation: Option A (Keyed Singleton)

Option A is the idiomatic .NET 10 pattern. It keeps `DedupeCache` constructor-injectable
and testable (tests pass `new MemoryCache(new MemoryCacheOptions())` directly per D4).
Option B works but embeds a `new MemoryCache` allocation inside the registrar — less
transparent. Note: `[FromKeyedServices]` requires `Microsoft.Extensions.DependencyInjection`
which the host already pulls in.

---

## Host-Side Wiring — `EventPump` vs Source-as-`IHostedService`

### Option 1 — Separate `EventPump : BackgroundService`

```
IHostedService: EventPump
  → resolves IEventSource from DI
  → await foreach (var ctx in source.ReadEventsAsync(ct))
       → SubscriptionMatcher.Match(ctx)
       → DedupeCache.TryEnter(sub, ctx)
       → logger.LogInformation("Matched event...")
```

### Option 2 — Source Implements `IHostedService`

`FrigateMqttEventSource : IEventSource, IHostedService` — `StartAsync` connects
the MQTT client; `StopAsync` calls `CompleteChannel()` + `DisconnectAsync`.

### Recommendation: Option 1 (Separate `EventPump`)

**Rationale:** `IEventSource` is defined in `FrigateRelay.Abstractions` which must not
reference `Microsoft.Extensions.Hosting` (CLAUDE.md: abstractions reference only
`Microsoft.Extensions.*`). The concrete `FrigateMqttEventSource` lives in the plugin
project which *can* reference Hosting, but mixing the lifecycle concern into the data
source blurs the boundary between "produces events" and "manages its own startup/shutdown."
An `EventPump` hosted service keeps event consumption logic in `FrigateRelay.Host`
(where it belongs as pipeline infrastructure) and keeps the source testable without
a hosted service lifecycle. The pump itself is trivial — a `BackgroundService` with
one `await foreach` loop — and its shutdown path is clean: the pump's `ExecuteAsync`
exits when the channel completes.

---

## Frigate Ingestion Pitfalls

- **`thumbnail` is always `null` in `frigate/events` MQTT messages.** The field exists in
  the schema but is never populated. Do not attempt to base64-decode it. Snapshots are
  available via the Frigate HTTP API (`/api/events/{id}/snapshot.jpg`) — Phase 5 scope.
  Source: https://docs.frigate.video/integrations/mqtt/

- **Multiple messages per event ID.** Frigate publishes a new message when it finds a
  better snapshot, when zones change, or on any frame update. Consumers see `type=update`
  many times for the same `id`. The dedupe cache handles this; do not assume one message
  per detection.

- **`"end"` can arrive before `"new"` is fully processed** in high-throughput scenarios,
  though uncommon at home-lab scale. The stationary guard applies to both `update` and
  `end`. Processing order within the channel is FIFO (unbounded single-writer), so
  within a single session this is deterministic.

- **`"new"` event `has_snapshot: false` is normal.** The snapshot is often not yet
  written when the first MQTT message fires. Do not gate matching on `has_snapshot`.

- **`frigate/events` messages are NOT retained** (MQTT retain flag not set by Frigate).
  If the client subscribes after an event fires, it will not receive that event.
  No replay on reconnect. Missed events during disconnect are permanently lost — this
  is by design and acceptable per PROJECT.md non-goals (no durable queue in v1).

- **Snapshot event IDs can differ from MQTT event IDs** in some edge cases (#17156).
  The MQTT `after.id` is authoritative for the dedupe key.

- **False-positive updates.** Frigate may publish an `update` where `false_positive=true`;
  these should be skipped (the object was reclassified as a false positive mid-track).
  The legacy code does not handle this explicitly; Phase 3 may wish to filter them.
  This is a **Decision Required** item — see Open Questions.

- **MQTT broker retained messages on other topics.** On initial connect, the broker may
  deliver retained messages for wildcard subscriptions. Since Phase 3 subscribes to the
  exact topic `frigate/events` and Frigate does not retain that topic, this is not a
  concern in practice.

- **QoS 0 vs QoS 1.** The legacy code used QoS 0 (AtMostOnce). At home-lab scales this
  is fine. Using QoS 1 (AtLeastOnce) risks duplicate delivery — the dedupe cache handles
  that too, but the `SubscribeAsync` call should explicitly request QoS 0 to match legacy
  behavior unless there is a reason to change.

Source: https://docs.frigate.video/integrations/mqtt/,
        https://github.com/blakeblackshear/frigate/issues/820,
        https://github.com/blakeblackshear/frigate/discussions/17156

---

## `InternalsVisibleTo` Precedent

`FrigateRelay.Host.csproj` already uses the MSBuild form:
```xml
<ItemGroup>
  <InternalsVisibleTo Include="FrigateRelay.Host.Tests" />
</ItemGroup>
```
Apply the same pattern in `FrigateRelay.Sources.FrigateMqtt.csproj`:
```xml
<InternalsVisibleTo Include="FrigateRelay.Sources.FrigateMqtt.Tests" />
```
This keeps DTOs and internal classes (`FrigateEvent`, `FrigateEventObject`, `DedupeCache`,
`SubscriptionMatcher`) accessible to the test project without polluting the public surface.
Source: `src/FrigateRelay.Host/FrigateRelay.Host.csproj` (observed pattern)

---

## Open Questions

1. **D3 is a no-op at Phase 3.** The `thumbnail` field in `frigate/events` is always null
   per current Frigate documentation. CONTEXT-3.md Decision D3 ("extract base64 thumbnail
   from MQTT payload when present") is based on an assumption that does not hold.
   **Decision Required:** Should D3 be officially closed as "thumbnail is always null;
   SnapshotFetcher returns null"? Or does the architect want to keep the decode path as
   dead code against the possibility of a future Frigate version populating the field?
   Recommendation: close D3 and document the field as null; Phase 5 is the correct place
   for HTTP-based snapshot fetching.

2. **False-positive filtering.** The legacy code does not explicitly skip events where
   `after.false_positive == true` on `update`/`end` messages. Should Phase 3 add this
   guard? Adding it is low-risk and reduces spurious matches; omitting it maintains exact
   behavioral parity with the legacy code. **Decision Required.**

3. **`FrigateEventBefore` / `FrigateEventAfter` vs `FrigateEventObject`.**
   CONTEXT-3.md names them separately; the schema is identical. The architect should
   decide: two identically-shaped records (matching the spec's naming), or one shared
   record (DRY). No functional difference.

4. **Zones aggregation strategy.** `EventContext.Zones` is `IReadOnlyList<string>`.
   The `SubscriptionMatcher` needs to check across all four zone arrays. Two approaches:
   (a) Union all four into `Zones` during projection (single list, simple matcher);
   (b) Project only `after.current_zones` into `Zones` and let the matcher receive the
   full `FrigateEvent` alongside `EventContext` (couples matcher to Frigate DTOs, violates
   source-agnostic boundary). Option (a) is recommended but loses which-zone-triggered
   provenance. **Decision Required** if per-zone-array differentiation is ever needed.

5. **ManagedMqttClient reference in CONTEXT-3.md / ROADMAP.md.** Both documents say
   "MQTTnet v5 ManagedMqttClient." That class does not exist in v5. The architect should
   update the plan to reflect a plain `IMqttClient` + custom reconnect loop.
   Functionally equivalent; the 5-second reconnect interval from ROADMAP is preserved
   via `Task.Delay(TimeSpan.FromSeconds(5))` in the ping-monitor loop.

---

## Sources

1. https://www.nuget.org/packages/MQTTnet — version 5.1.0.1559, published 2026-02-04
2. https://github.com/dotnet/MQTTnet/wiki/Upgrading-guide — v4→v5 breaking changes
3. https://github.com/dotnet/MQTTnet/blob/master/Samples/Client/Client_Connection_Samples.cs — TLS + reconnect samples
4. https://github.com/dotnet/MQTTnet/blob/master/Samples/Client/Client_Subscribe_Samples.cs — subscribe + ApplicationMessageReceivedAsync
5. https://docs.frigate.video/integrations/mqtt/ — frigate/events schema, thumbnail=null statement
6. https://github.com/blakeblackshear/frigate/issues/820 — thumbnail never populated community report
7. https://github.com/blakeblackshear/frigate/discussions/17156 — snapshot/event ID mismatch
8. https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsonnamingpolicy.snakecaselower?view=net-8.0 — SnakeCaseLower availability (.NET 8+)
9. https://learn.microsoft.com/en-us/dotnet/api/system.threading.channels.channelreader-1.readallasync?view=net-10.0 — ReadAllAsync semantics
10. https://learn.microsoft.com/en-us/dotnet/core/extensions/channels — Channel<T> patterns
11. https://andrewlock.net/exploring-the-dotnet-8-preview-keyed-services-dependency-injection-support/ — keyed services DI pattern
12. `src/FrigateRelay.Abstractions/IEventSource.cs` — contract surface (observed)
13. `src/FrigateRelay.Abstractions/EventContext.cs` — contract surface (observed)
14. `src/FrigateRelay.Host/PluginRegistrarRunner.cs` — registrar runner pattern (observed)
15. `.shipyard/phases/3/CONTEXT-3.md` — phase decisions D1–D4
16. `.shipyard/PROJECT.md` — NFRs, constraints
17. `.shipyard/codebase/ARCHITECTURE.md` — legacy event flow (behavioral reference only)
18. `.shipyard/codebase/STACK.md` — legacy stack (MQTTnet 4.1, Newtonsoft.Json — confirmed not reused)
