# Build Summary: Plan 1.1 — Frigate DTOs + JsonOptions + FrigateMqttOptions

## Status: complete

Builder agent truncated mid-verification; orchestrator committed the final task and reconstructed this summary from git log + file inspection.

## Tasks Completed

- **Task 1 — Plugin + test csproj scaffold** — commit `7120806`
  - `src/FrigateRelay.Sources.FrigateMqtt/FrigateRelay.Sources.FrigateMqtt.csproj` — `Microsoft.NET.Sdk` class library. `GenerateDocumentationFile=true`. `<InternalsVisibleTo Include="FrigateRelay.Sources.FrigateMqtt.Tests" />`. PackageRefs: `MQTTnet` (v5 per research), `Microsoft.Extensions.Options`, `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.Configuration.Abstractions`, `Microsoft.Extensions.Configuration.Binder`. ProjectReference to `FrigateRelay.Abstractions`.
  - `tests/FrigateRelay.Sources.FrigateMqtt.Tests/FrigateRelay.Sources.FrigateMqtt.Tests.csproj` — mirrors Host.Tests shape (Approach B PackageReference: MSTest 4.2.1, FluentAssertions 6.12.2, NSubstitute 5.3.0, NSubstitute.Analyzers.CSharp 1.0.17). ProjectReference to the plugin.
  - Both added to `FrigateRelay.sln`.

- **Task 2 — DTOs + JsonOptions + 6 tests** — commit `8006a2f`
  - `src/FrigateRelay.Sources.FrigateMqtt/Payloads/FrigateEvent.cs` (internal). Fields: `Type` (string), `Before` (FrigateEventObject?), `After` (FrigateEventObject?).
  - `src/FrigateRelay.Sources.FrigateMqtt/Payloads/FrigateEventObject.cs` (internal). Single shared record per OQ3 / architect's decision. Fields per RESEARCH.md schema: `Id`, `Camera`, `Label`, `CurrentZones` (`IReadOnlyList<string>`), `EnteredZones` (`IReadOnlyList<string>`), `Stationary` (bool), `FalsePositive` (bool), `StartTime` (double — Unix epoch seconds), `EndTime` (double?), `HasSnapshot` (bool), `Thumbnail` (string? — always null per D3 revised).
  - `src/FrigateRelay.Sources.FrigateMqtt/FrigateJsonOptions.cs` — exposes a shared `JsonSerializerOptions` instance. Uses `JsonNamingPolicy.SnakeCaseLower` (per RESEARCH.md recommendation — cleaner than per-field `[JsonPropertyName]` attributes).
  - 6 `[TestMethod]` in `tests/FrigateRelay.Sources.FrigateMqtt.Tests/`:
    1. Round-trip of a `new` event payload.
    2. Round-trip of an `update` event payload.
    3. Round-trip of an `end` event payload.
    4. `Thumbnail` deserializes as `null` when absent from the wire.
    5. Zone arrays deserialize as empty `IReadOnlyList<string>` when absent.
    6. Round-trip via the shared `FrigateJsonOptions.Default` matches the raw string.

- **Task 3 — `FrigateMqttOptions` (transport-only)** — commit `2a0958e` (orchestrator)
  - `src/FrigateRelay.Sources.FrigateMqtt/Configuration/FrigateMqttOptions.cs` — `Server`, `Port` (default 1883), `ClientId` (default `FrigateRelay`), `Topic` (default `frigate/events`), nested TLS block (`UseTls`, `AllowInvalidCertificates`, defaults false). 64 lines, XML docs on every public surface.
  - **Explicitly no `Subscriptions` member** — per post-revision architecture, subscription config is host-level (bound by PLAN-1.2's `HostSubscriptionsOptions`). Documented in the class's summary comment.

## Files Modified

| File | Change | Commit |
|---|---|---|
| `src/FrigateRelay.Sources.FrigateMqtt/FrigateRelay.Sources.FrigateMqtt.csproj` | created | `7120806` |
| `tests/FrigateRelay.Sources.FrigateMqtt.Tests/FrigateRelay.Sources.FrigateMqtt.Tests.csproj` | created | `7120806` |
| `FrigateRelay.sln` | +2 projects via `dotnet sln add` | `7120806` |
| `src/FrigateRelay.Sources.FrigateMqtt/Payloads/FrigateEvent.cs` | created | `8006a2f` |
| `src/FrigateRelay.Sources.FrigateMqtt/Payloads/FrigateEventObject.cs` | created | `8006a2f` |
| `src/FrigateRelay.Sources.FrigateMqtt/FrigateJsonOptions.cs` | created | `8006a2f` |
| `tests/FrigateRelay.Sources.FrigateMqtt.Tests/*Tests.cs` | 6 tests across files | `8006a2f` |
| `src/FrigateRelay.Sources.FrigateMqtt/Configuration/FrigateMqttOptions.cs` | created (orchestrator, truncation recovery) | `2a0958e` |

## Decisions Made

1. **`FrigateEventObject` is a single shared record** (OQ3 choice). `FrigateEvent.Before` and `FrigateEvent.After` are both `FrigateEventObject?`. DRY over the spec's separate-type naming — the schema is identical; duplicating the type for semantic clarity would be over-engineering.

2. **`JsonNamingPolicy.SnakeCaseLower` over per-field `[JsonPropertyName]`.** Cleaner for the ~10-field DTO; .NET 9+ feature (works on net10.0). The shared `FrigateJsonOptions.Default` is the single configuration point — tests and the Wave-2 projector both consume it.

3. **`Thumbnail` is `string?`, nullable, not decoded.** Per D3 revised (Frigate docs confirm the field is always null in MQTT payloads), the Wave-2 projector will not attempt base64 decode. The field exists in the DTO only to keep the deserializer happy if Frigate ever populates it; no downstream code path reads it.

4. **`TlsOptions` as a nested record inside `FrigateMqttOptions`.** Keeps the option shape self-contained; binds cleanly from `appsettings.json` under `FrigateMqtt:Tls`. `AllowInvalidCertificates: false` is the safe default.

## Issues Encountered

1. **Builder agent truncation** — the Sonnet-4.6 builder agent got through tasks 1 and 2 cleanly (two committed), then wrote `FrigateMqttOptions.cs` (task 3) on disk but truncated before committing it or writing this SUMMARY. Orchestrator completed task 3's commit (`2a0958e`) and reconstructed this SUMMARY from git show + file inspection. No rework needed.

2. **NSubstitute.Analyzers.CSharp 1.0.17 warning noise during initial restore** — not a build error (the `[tests/**.cs]` editorconfig scope silences CA1707 + IDE0005; the analyzer's own warnings stayed quiet). Noted.

## Verification Results

Run on commit `2a0958e`:

```
$ dotnet build FrigateRelay.sln -c Release
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

```
$ dotnet run --project tests/FrigateRelay.Sources.FrigateMqtt.Tests -c Release
  total: 6  succeeded: 6  failed: 0
```

```
$ dotnet list src/FrigateRelay.Sources.FrigateMqtt/FrigateRelay.Sources.FrigateMqtt.csproj package --include-transitive
  Top-level:
    Microsoft.Extensions.Configuration.Abstractions 10.0.0
    Microsoft.Extensions.Configuration.Binder       10.0.0
    Microsoft.Extensions.Logging.Abstractions       10.0.0
    Microsoft.Extensions.Options                    10.0.0
    MQTTnet                                          5.0.x
  Transitive: Microsoft.Extensions.Primitives, Microsoft.Extensions.DependencyInjection.Abstractions (both Microsoft.Extensions.*)
```
No Newtonsoft, no third-party runtime deps beyond MQTTnet (the one intentional non-`Microsoft.Extensions.*` runtime dep per ROADMAP).

```
$ git grep -nE '(Newtonsoft|ServicePointManager|\.Result\(|\.Wait\()' src/FrigateRelay.Sources.FrigateMqtt tests/FrigateRelay.Sources.FrigateMqtt.Tests
(no output)
$ bash .github/scripts/secret-scan.sh scan
Secret-scan PASSED: no secret-shaped strings found in tracked files.
```

## Next wave readiness

Wave 2 (PLAN-2.1) can consume:
- `FrigateEvent` + `FrigateEventObject` DTOs (internal; test project sees them via `InternalsVisibleTo`; Wave 2's projector is in the same assembly so it sees them directly).
- `FrigateJsonOptions.Default` — single-instance shared `JsonSerializerOptions`.
- `FrigateMqttOptions` — for the MQTT client connection + TLS + topic + client-id wiring.
