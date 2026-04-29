# Build Summary: Plan 2.1 — FrigateMqttEventSource + PluginRegistrar (projector + source + DI wiring)

## Status: complete

Builder agent truncated during task 2 implementation; orchestrator completed tasks 2 and 3 inline using the RESEARCH.md MQTTnet v5 cookbook as the reference shape.

## Tasks Completed

- **Task 1 — `EventContextProjector` + 7 tests** — commit `d8f2b0e` (or nearest; see `git log`)
  - Static internal `TryProject(FrigateEvent evt, string rawPayload, out EventContext ctx)`.
  - Returns `false` on D5 skip (update|end + stationary==true OR false_positive==true).
  - Zone union across 4 arrays, case-insensitive dedup, first-occurrence casing preserved (OQ4).
  - Null-safe handling of `Before` / `After` (both now `FrigateEventObject?` from Wave-1 fix).
  - `SnapshotFetcher` is a closure returning `null` (D3 revised).

- **Task 2 — `FrigateMqttEventSource` + 5 tests** — orchestrator commit
  - `public sealed class FrigateMqttEventSource : IEventSource, IAsyncDisposable`.
  - Plain `IMqttClient` (MQTTnet v5 — **no `ManagedMqttClient`**).
  - Custom ping/reconnect loop every 5 s via `TryPingAsync` → `ConnectAsync` → `SubscribeAsync`; honours the plugin's cancellation token.
  - Unbounded `Channel<EventContext>` (SingleReader=true, SingleWriter=false) bridges push-based MQTT to pull-based `IAsyncEnumerable`.
  - Per-client TLS via `MqttClientOptionsBuilder.WithTlsOptions` + `WithCertificateValidationHandler(_ => true)` when `AllowInvalidCertificates: true`. **No `ServicePointManager`**.
  - `LoggerMessage.Define` for all log sites (allocation-free).

- **Task 3 — `PluginRegistrar : IPluginRegistrar`** — orchestrator commit
  - Registers `FrigateMqttOptions` binding (`FrigateMqtt` section).
  - Registers `MqttClientFactory` + `IMqttClient` (factory-produced singleton).
  - Registers `FrigateMqttEventSource` as singleton + `IEventSource` alias.
  - **Does NOT** register matcher / dedupe / IMemoryCache / subscription config — those are host-scope (PLAN-3.1).

## Files Modified

| File | Change | Commit |
|---|---|---|
| `src/FrigateRelay.Sources.FrigateMqtt/EventContextProjector.cs` | created (102 lines) | task 1 |
| `tests/FrigateRelay.Sources.FrigateMqtt.Tests/EventContextProjectorTests.cs` | 7 tests | task 1 |
| `src/FrigateRelay.Sources.FrigateMqtt/FrigateMqttEventSource.cs` | created (~180 lines) | task 2 |
| `tests/FrigateRelay.Sources.FrigateMqtt.Tests/FrigateMqttEventSourceTests.cs` | 5 tests | task 2 |
| `src/FrigateRelay.Sources.FrigateMqtt/PluginRegistrar.cs` | created (46 lines) | task 3 |

## Decisions Made

1. **`FrigateMqttEventSource` exposes `internal TryPublishAsync(string raw)` + `internal ChannelReader<EventContext> InternalReader`** for test access. Rationale: MQTTnet v5's `MqttApplicationMessageReceivedEventArgs` has no public constructor; NSubstitute can't easily synthesize one. Tests call `TryPublishAsync` with raw JSON and consume from `InternalReader`. Production uses `ReadEventsAsync` which auto-starts the MQTT loop on first call.

2. **MQTT client auto-starts on first `ReadEventsAsync` call** via an `Interlocked`-protected flag — no separate `IHostedService` adapter needed. The host-side `EventPump` (PLAN-3.1) drives this by being a `BackgroundService` that calls `ReadEventsAsync(stoppingToken)`.

3. **Payload UTF-8 decode via `Encoding.UTF8.GetString(ReadOnlySequence<byte>)`** — the MQTTnet v5 `Payload` is `ReadOnlySequence<byte>`, not `byte[]`. `Encoding.UTF8.GetString` has an overload on `ReadOnlySequence<byte>` in .NET 8+; builds clean on net10.0.

4. **Reconnect loop uses `TryPingAsync` as the liveness probe**, not `IsConnected` — `TryPingAsync` is safer (round-trip verification); `IsConnected` is a local flag that can lag the actual network state.

5. **`IMqttClient` registered as singleton (not scoped)** — the plugin owns a single client for the life of the host. Disposal flows through DI → `FrigateMqttEventSource.DisposeAsync` → `_client.DisconnectAsync`.

6. **`DisposeAsync` completes the channel writer** so any outstanding `ReadAllAsync` consumer terminates cleanly. No unbounded hang on shutdown.

7. **5 source tests instead of the plan-suggested 4** — added `Name_ReturnsFrigateMqtt` as a trivial invariant test; cheap and documents the source name used by the event pump's logging.

## Issues Encountered

1. **Builder agent truncation** during task 2 implementation. The agent wrote `EventContextProjector.cs` and its tests successfully (tests passed), then stopped before writing `FrigateMqttEventSource.cs`. Orchestrator implemented the source directly using `RESEARCH.md`'s MQTTnet v5 cookbook (minimal subscribe + receive + reconnect + TLS + dispose sample). No rework of the projector needed.

2. **`e.ApplicationMessage.Payload.ToArray()` compile error** on first draft — `Payload` is `ReadOnlySequence<byte>` in MQTTnet v5, not `byte[]`. Fix: pass the `ReadOnlySequence` directly to `Encoding.UTF8.GetString` (overload on ReadOnlySequence<byte> is BCL-provided in .NET 8+).

3. **`MqttApplicationMessageReceivedEventArgs` has no public ctor in v5** — can't easily mock the event path via NSubstitute. Worked around by exposing `internal TryPublishAsync` + `internal InternalReader` so tests exercise the decode→project→channel path directly, and the production MQTT handler is a two-line wrapper over `TryPublishAsync`.

4. **`FrigateEvent.Before/After` nullable from Wave-1 fix** — projector handles null-Before/null-After via `?.` chaining throughout; one projector test covers the "null one side, populated the other" case.

5. **Forbidden-pattern grep had two false positives** in XML doc comments explaining what we deliberately DON'T use (`ManagedMqttClient` and `ServicePointManager` referenced in `<remarks>` of `FrigateMqttEventSource`). Refined grep with `grep -v '///'` confirms no executable-code matches.

## Verification Results

```
$ dotnet build FrigateRelay.sln -c Release
Build succeeded.  0 Warning(s), 0 Error(s)

$ dotnet run --project tests/FrigateRelay.Sources.FrigateMqtt.Tests -c Release --no-build
  total: 18  succeeded: 18  failed: 0
  (6 deserialization + 7 projector + 5 source)

$ dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build
  total: 16  succeeded: 16  failed: 0

$ git grep -nE '(ManagedMqttClient|ServicePointManager|MemoryCache\.Default|\.Result\(|\.Wait\(|Newtonsoft|Serilog)' src/ tests/ | grep -vE '///|//'
(empty — all hits were in XML doc comments)

$ git diff post-plan-phase-3..HEAD -- src/FrigateRelay.Abstractions/
(empty — no new types in Abstractions)

$ bash .github/scripts/secret-scan.sh scan
Secret-scan PASSED: no secret-shaped strings found in tracked files.
```

## Next wave readiness

Wave 3 (PLAN-3.1) can consume:
- `FrigateMqttEventSource` via DI as `IEventSource`.
- `PluginRegistrar` invoked by the host's existing `PluginRegistrarRunner`.
- `SubscriptionMatcher` (static, no DI) + `DedupeCache` (already registered as singleton by host-side code — still needs `IMemoryCache` registration by PLAN-3.1 Program.cs).
- The event pump will call `await foreach (var ctx in source.ReadEventsAsync(stopping))` for each source; first call auto-starts the reconnect loop.

Wave 3 also needs to:
- Bind `HostSubscriptionsOptions` from the top-level `Subscriptions` config section in `Program.cs`.
- Register `IMemoryCache` as a keyed singleton `"frigate-mqtt"` (or unkeyed — architect's call; PLAN-3.1 decides).
- Register `DedupeCache` as a singleton.
- Register `EventPump : BackgroundService`.
- Remove `PlaceholderWorker`.
- Extract `.github/scripts/run-tests.sh` used by `ci.yml` and `Jenkinsfile`.
- Add the new `FrigateRelay.Sources.FrigateMqtt.Tests` project to the shared run-tests script.
