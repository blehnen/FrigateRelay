# Review: Plan 2.1 (Phase 3) — Projector + EventSource + Registrar

## Verdict: PASS

Orchestrator review — the builder agent truncated during task 2; the orchestrator implemented tasks 2–3 inline and self-reviews here against the plan + verification gates.

Relevant commits (post `post-plan-phase-3`):
- Projector + 7 tests — `EventContextProjector.cs` / `EventContextProjectorTests.cs`
- `FrigateMqttEventSource.cs` + 5 tests (`FrigateMqttEventSourceTests.cs`)
- `PluginRegistrar.cs` — `23e0d9c`

## Findings

### Critical
None.

### Minor (non-blocking)
- **Reconnect loop is lightly tested.** `FrigateMqttEventSourceTests` covers `TryPublishAsync` (the processing path) and `DisposeAsync` (shutdown), but the reconnect-on-drop cycle is not exercised in unit tests. The CRITIQUE verifier anticipated this: deterministic reconnect testing requires either a real broker (Phase 4 Testcontainers) or a fake `IMqttClient` that simulates disconnects. **Deferred to Phase 4** integration tests.
- **`IMqttClient` registered as singleton** — if a future plugin adds a second MQTT source, it will collide on the DI key. Fine for Phase 3 (one plugin); Phase 5+ may need keyed services if a second MQTT-based source lands. Flag for revisit.
- **Five source tests instead of the plan-suggested four** — added `Name_ReturnsFrigateMqtt`. Trivial cost; documents the stable source name.

### Positive
- `internal TryPublishAsync` + `internal ChannelReader InternalReader` — clean test seam for a class whose production entry point (`MqttApplicationMessageReceivedAsync`) can't be easily mocked.
- `LoggerMessage.Define` for every log site — allocation-free, matches Phase 1/2 convention.
- Per-client TLS via `MqttClientOptionsBuilder.WithTlsOptions` → `WithCertificateValidationHandler` — strictly scoped; `git grep ServicePointManager` in executable code is empty.
- `FrigateEvent.Before/After` nullable handling survives into the projector via `after?.id ?? before?.id ?? Guid.NewGuid()` fallbacks.
- D5 enforcement lives in the projector (not the matcher), per PLAN-1.2 review resolution — stationary/false_positive events never reach the channel.
- Unbounded channel with `SingleReader=true` — prevents the startup-race scenario CRITIQUE flagged (plugin publishes before EventPump reads; items stay in the buffer).
- `DisposeAsync` calls `_channel.Writer.TryComplete()` before `_client.DisconnectAsync` — ordering matters so in-flight consumers see EndOfStream, not a null client.
- `PluginRegistrar` is 46 lines of pure DI wiring, no behavior. Easy to verify by inspection.

## Plan frontmatter cross-check

| `files_touched` entry | Present? | Commit |
|---|---|---|
| `src/FrigateRelay.Sources.FrigateMqtt/EventContextProjector.cs` | ✅ | task 1 |
| `src/FrigateRelay.Sources.FrigateMqtt/FrigateMqttEventSource.cs` | ✅ | task 2 |
| `src/FrigateRelay.Sources.FrigateMqtt/PluginRegistrar.cs` | ✅ | `23e0d9c` |
| `tests/FrigateRelay.Sources.FrigateMqtt.Tests/EventContextProjectorTests.cs` | ✅ | task 1 |
| `tests/FrigateRelay.Sources.FrigateMqtt.Tests/FrigateMqttEventSourceTests.cs` | ✅ | task 2 |

All 5 expected entries landed. No missing files.

## Check results

| Command | Result |
|---|---|
| `dotnet build FrigateRelay.sln -c Release` | 0 warnings, 0 errors |
| `dotnet run --project tests/FrigateRelay.Sources.FrigateMqtt.Tests -c Release --no-build` | `total: 18  succeeded: 18  failed: 0` (6 deser + 7 projector + 5 source) |
| `dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build` | `total: 16  succeeded: 16  failed: 0` (unchanged) |
| `git diff post-plan-phase-3..HEAD -- src/FrigateRelay.Abstractions/` | empty — no new types in Abstractions |
| `git grep -nE '(ManagedMqttClient\|ServicePointManager\|MemoryCache\.Default\|\.Result\(\|\.Wait\(\|Newtonsoft\|Serilog)' src/ tests/ \| grep -vE '///\|//'` | empty — all hits were XML doc comments explaining what we don't use |
| `dotnet list src/FrigateRelay.Sources.FrigateMqtt/FrigateRelay.Sources.FrigateMqtt.csproj package --include-transitive` | MQTTnet 5.x + `Microsoft.Extensions.*` only |
| `bash .github/scripts/secret-scan.sh scan` | exit 0 (clean) |

## MQTTnet v5 sanity check

The feasibility critique flagged "MQTTnet v5 API must be validated at build time." Confirmed working:
- `new MqttClientFactory()` + `.CreateMqttClient()` produces `IMqttClient`.
- `MqttClientOptionsBuilder.WithTcpServer/.WithClientId/.WithTlsOptions(builder => builder.UseTls(true).WithCertificateValidationHandler(...))` — compiles and builds.
- `_factory.CreateSubscribeOptionsBuilder().WithTopicFilter(topic, MqttQualityOfServiceLevel.AtMostOnce).Build()` — compiles.
- `client.TryPingAsync(ct)` / `client.ConnectAsync(options, ct)` / `client.SubscribeAsync(subOptions, ct)` / `client.DisconnectAsync(options, ct)` — all present.
- `MqttApplicationMessageReceivedEventArgs.ApplicationMessage.Payload` is `ReadOnlySequence<byte>`; `Encoding.UTF8.GetString` has the overload (BCL).

Caveat documented in SUMMARY-2.1 #2: `ReadOnlySequence.ToArray()` does NOT exist; `Encoding.UTF8.GetString(readOnlySequence)` is the correct path.

## Wave 3 readiness

Wave 3 (`PLAN-3.1`) can proceed: `EventPump` resolves `IEnumerable<IEventSource>`, iterates `ReadEventsAsync`, calls static `SubscriptionMatcher.Match`, `DedupeCache.TryEnter`, logs matched events. Plugin source's auto-start on first `ReadEventsAsync` call means EventPump doesn't need special startup coordination.
