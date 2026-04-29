---
phase: frigate-mqtt-ingestion
plan: 2.1
wave: 2
dependencies: [1.1, 1.2]
must_haves:
  - FrigateMqttEventSource implements IEventSource via Channel<EventContext>.ReadAllAsync(ct)
  - Plain IMqttClient (MQTTnet v5) + custom 5s reconnect loop driven by TryPingAsync; ApplicationMessageReceivedAsync handler bridges to channel writer
  - Per-client TLS via WithTlsOptions(...).WithCertificateValidationHandler when Tls.AllowInvalidCertificates == true; never ServicePointManager
  - D5 guard applied at projection-time (skip on type in {update,end} && (after.stationary || after.false_positive)) — these events never reach the channel
  - "EventContext.Zones = union of all four zone arrays (OQ4); SnapshotFetcher = _ => ValueTask.FromResult<byte[]?>(null) (D3 revised); RawPayload = UTF-8 string of PayloadSegment (D2)"
  - FrigateMqtt.PluginRegistrar : IPluginRegistrar registers IEventSource singleton, binds FrigateMqtt config section, and registers the MQTT-lifecycle IHostedService adapter — nothing else (no DedupeCache, no SubscriptionMatcher, no IMemoryCache; those live in Host)
  - ">= 6 new tests in this plan (projection: zone union, RawPayload roundtrip, SnapshotFetcher returns null, D5 skip on update+stationary, D5 skip on update+false_positive, type=new with stationary=true is NOT skipped)"
files_touched:
  - src/FrigateRelay.Sources.FrigateMqtt/FrigateMqttEventSource.cs
  - src/FrigateRelay.Sources.FrigateMqtt/EventContextProjector.cs
  - src/FrigateRelay.Sources.FrigateMqtt/PluginRegistrar.cs
  - tests/FrigateRelay.Sources.FrigateMqtt.Tests/EventContextProjectorTests.cs
  - tests/FrigateRelay.Sources.FrigateMqtt.Tests/FrigateMqttEventSourceTests.cs
tdd: true
risk: high
---

# PLAN-2.1 — FrigateMqttEventSource, Projector, PluginRegistrar

## Context

Wires the MQTT client lifecycle to a `Channel<EventContext>` and exposes it through `IEventSource.ReadEventsAsync(ct)`. This is the highest-risk plan in Phase 3 (CONTEXT-3 §"Risk High — MQTT lifecycle, reconnect, and cancellation-token wiring"). Resolves the following:

- **D2 RawPayload string** — UTF-8 decode `MqttApplicationMessage.PayloadSegment.Span` once per message; one allocation per message.
- **D3 revised** — `SnapshotFetcher = _ => ValueTask.FromResult<byte[]?>(null)`. No thumbnail decode. No HTTP.
- **D5 stationary + false_positive skip** — applied during projection, before channel write. `EventContextProjector.TryProject(FrigateEvent, string raw, out EventContext)` returns false on skip; the source simply does not write skipped events to the channel. This is the documented contract upstream of `SubscriptionMatcher` (which lives in Host per PLAN-1.2).
- **OQ4 zone aggregation** — `EventContext.Zones = before.current_zones ∪ before.entered_zones ∪ after.current_zones ∪ after.entered_zones`, deduplicated, materialised as `IReadOnlyList<string>`.
- **MQTT client (RESEARCH §"MQTTnet v5 API Cookbook")** — `MqttClientFactory.CreateMqttClient()` (no `ManagedMqttClient`), one client owned by the source, register `ApplicationMessageReceivedAsync` BEFORE first connect, custom reconnect loop using `TryPingAsync` + `Task.Delay(5s)`. Per-client TLS via `MqttClientOptionsBuilder.WithTlsOptions(o => o.WithCertificateValidationHandler(...))`.

**Plugin registrar scope (re-audited per the simplification decision).** The plugin registrar registers **only**:
1. The `FrigateMqtt` config section binding for `FrigateMqttOptions`.
2. `MqttClientFactory` as singleton.
3. `IMqttClient` as singleton via factory delegate.
4. `FrigateMqttEventSource` as singleton, also aliased as `IEventSource` (singleton aliasing).
5. A thin `IHostedService` adapter (private nested class) whose `StartAsync`/`StopAsync` forward to `FrigateMqttEventSource`.

**Explicitly NOT registered by the plugin** (these were in earlier drafts; removed in this revision):
- `IMemoryCache` — not needed in the plugin. `DedupeCache` lives in Host (PLAN-1.2) and the Host registrar wires its own `IMemoryCache`.
- `DedupeCache` — Host-owned (PLAN-1.2 / PLAN-3.1).
- `SubscriptionMatcher` — Host-owned, static, no registration anyway.
- Any `ISubscriptionProvider` / `IEventMatchSink` — these abstractions are dropped entirely (per user decision following the feasibility critique). `FrigateRelay.Abstractions` receives **zero** new types in Phase 3.

The `FrigateMqttEventSource` itself does NOT implement `IHostedService` directly (RESEARCH Option 1) — start/stop is driven by the registered hosted-service adapter. The source exposes `Task StartAsync(CancellationToken)` and `Task StopAsync(CancellationToken)` for the adapter to call. PLAN-3.1's `EventPump` only consumes events via `IEventSource.ReadEventsAsync`; it does not own the MQTT lifecycle.

## Dependencies

- PLAN-1.1 (DTOs, JsonOptions, FrigateMqttOptions)
- PLAN-1.2 (no direct production dependency — Host-owned types; plan-2.1's tests do not import them)

## Tasks

<task id="1" files="src/FrigateRelay.Sources.FrigateMqtt/EventContextProjector.cs, tests/FrigateRelay.Sources.FrigateMqtt.Tests/EventContextProjectorTests.cs" tdd="true">
  <action>TDD: add 7 tests in `EventContextProjectorTests.cs` covering — (1) `new` event with `before.current_zones=[]`, `before.entered_zones=[]`, `after.current_zones=["driveway"]`, `after.entered_zones=["driveway"]` projects to `Zones={"driveway"}` (deduplicated); (2) `new` event with disjoint zone sets across all four arrays projects to the union; (3) `RawPayload` round-trips the exact input UTF-8 string; (4) `SnapshotFetcher` invocation returns `null`; (5) `update` event with `after.stationary=true` returns `false` (skipped); (6) `update` event with `after.false_positive=true` returns `false` (skipped); (7) `new` event with `after.stationary=true` projects successfully (D5 only fires on update/end). Then implement `internal static class EventContextProjector` with `static bool TryProject(FrigateEvent evt, string rawPayload, out EventContext context)`. Apply D5 guard first; on pass, build the union zone list (`HashSet<string>` with `StringComparer.OrdinalIgnoreCase`), materialise to `IReadOnlyList<string>`, set `EventId=evt.After.Id`, `Camera=evt.After.Camera`, `Label=evt.After.Label`, `StartedAt=DateTimeOffset.UnixEpoch.AddSeconds(evt.After.StartTime)`, `RawPayload=rawPayload`, `SnapshotFetcher=static _ => ValueTask.FromResult<byte[]?>(null)`. XML doc the D5 contract.</action>
  <verify>dotnet run --project tests/FrigateRelay.Sources.FrigateMqtt.Tests -c Release</verify>
  <done>All 7 projector tests pass; D5 skip path returns false without touching `out context`; `git grep -n "ServicePointManager\|MemoryCache\.Default" src/` returns zero matches.</done>
</task>

<task id="2" files="src/FrigateRelay.Sources.FrigateMqtt/FrigateMqttEventSource.cs, tests/FrigateRelay.Sources.FrigateMqtt.Tests/FrigateMqttEventSourceTests.cs" tdd="true">
  <action>Implement `internal sealed class FrigateMqttEventSource : IEventSource, IAsyncDisposable`. Constructor: `(IMqttClient client, MqttClientFactory factory, IOptions<FrigateMqttOptions> options, ILogger<FrigateMqttEventSource> logger)` — accept the client + factory via DI so tests can substitute. Fields: `Channel<EventContext>` unbounded `SingleWriter=true`, `CancellationTokenSource _cts`. Public: `Name => "FrigateMqtt"`; `IAsyncEnumerable<EventContext> ReadEventsAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct)`; `Task StartAsync(CancellationToken ct)` registers `ApplicationMessageReceivedAsync` (decode UTF-8, deserialize via `FrigateJsonOptions.Default`, call `EventContextProjector.TryProject`, on true write to channel; on false do nothing), builds `MqttClientOptions` honouring `Tls.Enabled` + `Tls.AllowInvalidCertificates` (per-client `WithCertificateValidationHandler(_ => true)` ONLY when allow flag is set), starts the reconnect loop (`Task.Run`) which uses `TryPingAsync` -> `ConnectAsync` -> `factory.CreateSubscribeOptionsBuilder().WithTopicFilter(options.Topic, MqttQualityOfServiceLevel.AtMostOnce).Build()` -> `SubscribeAsync`, log "MQTT connected" on success and "MQTT connect failed; retrying" on exception, `Task.Delay(TimeSpan.FromSeconds(5), ct)` between iterations. `Task StopAsync(CancellationToken ct)` cancels `_cts`, calls `_channel.Writer.Complete()`, `await client.DisconnectAsync(...)`, logs "MQTT disconnected". Add 4 tests in `FrigateMqttEventSourceTests.cs` using NSubstitute to fake `IMqttClient`: (1) `StartAsync` registers a handler; (2) firing a synthetic `MqttApplicationMessageReceivedEventArgs` with a `new` payload produces one item via `ReadEventsAsync`; (3) firing an `update`+`stationary=true` payload produces zero items; (4) `StopAsync` completes the channel so `ReadAllAsync` exits cleanly within 1 second. Use `LoggerMessage.Define` for connection-state logs (allocation-free per CONTEXT-3 §"hot path"). All public/internal API gets XML docs. NO `.Result`, NO `.Wait()`, NO `ServicePointManager`.</action>
  <verify>dotnet run --project tests/FrigateRelay.Sources.FrigateMqtt.Tests -c Release && git grep -n "ServerCertificateValidationCallback\|ServicePointManager\|\.Result\|\.Wait()" src/FrigateRelay.Sources.FrigateMqtt/</verify>
  <done>4 source tests pass; `git grep` for banned APIs returns zero matches in the plugin tree; channel exits cleanly on `StopAsync` (test #4 completes &lt; 1s).</done>
</task>

<task id="3" files="src/FrigateRelay.Sources.FrigateMqtt/PluginRegistrar.cs" tdd="false">
  <action>Implement `public sealed class PluginRegistrar : IPluginRegistrar` in namespace `FrigateRelay.Sources.FrigateMqtt`. In `Register(PluginRegistrationContext context)`:
  1. Bind `FrigateMqttOptions` from `context.Configuration.GetSection("FrigateMqtt")`.
  2. Register `MqttClientFactory` as singleton.
  3. Register `IMqttClient` as singleton via factory delegate `sp => sp.GetRequiredService<MqttClientFactory>().CreateMqttClient()`.
  4. Register `FrigateMqttEventSource` as singleton.
  5. Alias the same instance as `IEventSource` via `services.AddSingleton<IEventSource>(sp => sp.GetRequiredService<FrigateMqttEventSource>())`.
  6. Register a thin `IHostedService` adapter (private nested `sealed class FrigateMqttHostedService : IHostedService`) whose `StartAsync`/`StopAsync` forward to `FrigateMqttEventSource`; register via `services.AddHostedService<FrigateMqttHostedService>()`.

**Explicitly do NOT register**: `IMemoryCache`, `DedupeCache`, `SubscriptionMatcher`, `ISubscriptionProvider`, `IEventMatchSink`. The first three live in Host; the last two do not exist (dropped per simplification decision).

All public types XML-doc'd.</action>
  <verify>dotnet build src/FrigateRelay.Sources.FrigateMqtt -c Release && grep -nE "AddKeyedSingleton|DedupeCache|SubscriptionMatcher|ISubscriptionProvider|IEventMatchSink|IMemoryCache" src/FrigateRelay.Sources.FrigateMqtt/PluginRegistrar.cs || echo "OK: registrar contains none of the forbidden registrations"</verify>
  <done>Plugin builds with zero warnings; the grep above prints "OK"; `IEventSource` resolves to the same instance as `FrigateMqttEventSource` (singleton aliasing); a hosted-service adapter is registered.</done>
</task>

## Verification

```bash
cd /mnt/f/git/FrigateRelay
dotnet build -c Release
dotnet run --project tests/FrigateRelay.Sources.FrigateMqtt.Tests -c Release --no-build
git grep -n "ServerCertificateValidationCallback" src/ || echo "OK: no global TLS callback"
git grep -n "MemoryCache\.Default\|ServicePointManager\|Newtonsoft\|\.Result\|\.Wait()" src/ || echo "OK: no banned APIs"
git grep -nE "ISubscriptionProvider|IEventMatchSink" src/FrigateRelay.Abstractions/ || echo "OK: Abstractions surface unchanged"
```

Expected: solution builds; combined plugin test count from PLAN-1.1 (6) + PLAN-2.1 (11) = 17 plugin-side tests; all three grep guards print "OK".
