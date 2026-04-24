---
phase: frigate-mqtt-ingestion
plan: 1.1
wave: 1
dependencies: []
must_haves:
  - New plugin csproj at src/FrigateRelay.Sources.FrigateMqtt/ targets net10.0 with InternalsVisibleTo for the Tests project
  - Internal FrigateEvent + shared FrigateEventObject record DTOs (snake_case via JsonNamingPolicy.SnakeCaseLower)
  - Static FrigateJsonOptions.Default singleton (case-insensitive, snake_case lower, ignore-null on write)
  - FrigateMqttOptions (Server, Port, ClientId, Topic, Tls.Enabled, Tls.AllowInvalidCertificates) record only — no Subscriptions field on the plugin's options type
  - Test project FrigateRelay.Sources.FrigateMqtt.Tests using the shipyard test runner pattern (no dotnet test)
  - ">= 6 deserialization tests in this plan (new/update/end events + snake_case round-trip + missing optional + zone-array shape)"
files_touched:
  - src/FrigateRelay.Sources.FrigateMqtt/FrigateRelay.Sources.FrigateMqtt.csproj
  - src/FrigateRelay.Sources.FrigateMqtt/FrigateJsonOptions.cs
  - src/FrigateRelay.Sources.FrigateMqtt/Payloads/FrigateEvent.cs
  - src/FrigateRelay.Sources.FrigateMqtt/Payloads/FrigateEventObject.cs
  - src/FrigateRelay.Sources.FrigateMqtt/Configuration/FrigateMqttOptions.cs
  - tests/FrigateRelay.Sources.FrigateMqtt.Tests/FrigateRelay.Sources.FrigateMqtt.Tests.csproj
  - tests/FrigateRelay.Sources.FrigateMqtt.Tests/Program.cs
  - tests/FrigateRelay.Sources.FrigateMqtt.Tests/PayloadDeserializationTests.cs
  - FrigateRelay.sln
tdd: true
risk: low
---

# PLAN-1.1 — DTOs, JsonOptions, Plugin Options, Test Project Scaffold

## Context

This plan stands up the `FrigateRelay.Sources.FrigateMqtt` plugin project skeleton plus its sibling test project, and lands the pure-data parts: payload DTOs, JSON options, and the plugin's own `FrigateMqttOptions`. It is a Wave 1 sibling of PLAN-1.2 — they touch disjoint files so they can run in parallel.

**Subscription config ownership (NEW — locked in this revision).** `SubscriptionOptions` does **not** live in the plugin. Subscriptions are a host-level concern: they describe what the host cares about across any current or future `IEventSource` (Frigate MQTT today, HTTP webhooks / Zigbee / etc. later). Therefore:

- `SubscriptionOptions` is created in PLAN-1.2 under `src/FrigateRelay.Host/Configuration/`, not here.
- `FrigateMqttOptions` in this plan has **no `Subscriptions` property**. Its fields are limited to MQTT transport: `Server`, `Port`, `ClientId`, `Topic`, `Tls`.
- The plugin section in `appsettings.json` is `FrigateMqtt`. The host section for subscription rules is the top-level `Subscriptions` array (config option (b) in the prompt). This matches Phase 8's Profiles+Subscriptions shape.

**OQ3 resolution (DTO records).** Use a single shared `FrigateEventObject` record for both `before` and `after` (DRY); the schema is identical per RESEARCH §"`before` / `after` Field Mapping". CONTEXT-3.md's reference to `FrigateEventBefore`/`FrigateEventAfter` is satisfied by `FrigateEvent.Before` / `FrigateEvent.After` properties of type `FrigateEventObject` — same concept, less duplication.

**OQ5 (ManagedMqttClient).** Confirmed dead in MQTTnet v5; this plan does not touch the MQTT client (PLAN-2.1 owns it) so no impact here beyond the plugin's `MQTTnet` PackageReference, which uses 5.1.0.1559.

DTOs are `internal`. The plugin csproj declares `<InternalsVisibleTo Include="FrigateRelay.Sources.FrigateMqtt.Tests" />` (per RESEARCH §"InternalsVisibleTo Precedent"). All public types in the plugin csproj carry XML doc comments — Directory.Build.props enforces WAE.

**Hard constraint reaffirmed.** `FrigateRelay.Abstractions` receives **zero** new types in Phase 3. This plan touches only the plugin assembly and the plugin test assembly.

## Dependencies

- None within Phase 3. Depends on Phase 1 (`FrigateRelay.Abstractions`, `FrigateRelay.Host`) and Phase 2 contracts (`EventContext`, `IEventSource`, `IPluginRegistrar`).

## Tasks

<task id="1" files="src/FrigateRelay.Sources.FrigateMqtt/FrigateRelay.Sources.FrigateMqtt.csproj, tests/FrigateRelay.Sources.FrigateMqtt.Tests/FrigateRelay.Sources.FrigateMqtt.Tests.csproj, tests/FrigateRelay.Sources.FrigateMqtt.Tests/Program.cs, FrigateRelay.sln" tdd="false">
  <action>Create the plugin csproj at `src/FrigateRelay.Sources.FrigateMqtt/FrigateRelay.Sources.FrigateMqtt.csproj` targeting net10.0 with `<ItemGroup><InternalsVisibleTo Include="FrigateRelay.Sources.FrigateMqtt.Tests" /></ItemGroup>`. PackageReferences: `MQTTnet` 5.1.0.1559, `Microsoft.Extensions.Hosting.Abstractions`, `Microsoft.Extensions.Options.ConfigurationExtensions`, `Microsoft.Extensions.Logging.Abstractions`. (Note: `Microsoft.Extensions.Caching.Memory` is intentionally NOT referenced from the plugin — DedupeCache lives in Host now.) ProjectReference: `FrigateRelay.Abstractions`. Create the test csproj at `tests/FrigateRelay.Sources.FrigateMqtt.Tests/` mirroring `FrigateRelay.Host.Tests` shape — `<OutputType>Exe</OutputType>`, ProjectReference to the plugin and to `FrigateRelay.Abstractions`, packages: `Microsoft.NET.Test.Sdk`, `xunit.v3`, `NSubstitute`, `FluentAssertions`, `Microsoft.Extensions.Logging.Abstractions`. Add a `Program.cs` that delegates to xUnit v3's auto-generated entry point (match `FrigateRelay.Host.Tests` exactly). Add both projects to `FrigateRelay.sln` via `dotnet sln add` (use Bash; no `dotnet test`).</action>
  <verify>dotnet build src/FrigateRelay.Sources.FrigateMqtt/FrigateRelay.Sources.FrigateMqtt.csproj -c Release && dotnet build tests/FrigateRelay.Sources.FrigateMqtt.Tests/FrigateRelay.Sources.FrigateMqtt.Tests.csproj -c Release</verify>
  <done>Both projects compile in Release with zero warnings; both appear in `dotnet sln list`; the plugin csproj contains the `InternalsVisibleTo` element verbatim; `grep -n "Caching.Memory" src/FrigateRelay.Sources.FrigateMqtt/FrigateRelay.Sources.FrigateMqtt.csproj` returns zero matches.</done>
</task>

<task id="2" files="src/FrigateRelay.Sources.FrigateMqtt/FrigateJsonOptions.cs, src/FrigateRelay.Sources.FrigateMqtt/Payloads/FrigateEvent.cs, src/FrigateRelay.Sources.FrigateMqtt/Payloads/FrigateEventObject.cs" tdd="true">
  <action>TDD: first add 6 tests in `tests/FrigateRelay.Sources.FrigateMqtt.Tests/PayloadDeserializationTests.cs` covering — (1) deserialize the `new` event sample from RESEARCH.md and assert `Type=="new"`, `After.Camera=="front_door"`, `After.Label=="person"`, `After.CurrentZones==["driveway"]`, `After.Stationary==false`, `After.FalsePositive==false`; (2) deserialize an `update` event and assert `After.Stationary==true` round-trips; (3) deserialize an `end` event and assert `After.EndTime` is non-null; (4) confirm `sub_label` null deserialises to `SubLabel == null`; (5) confirm omitted optional `thumbnail` deserialises to null; (6) confirm `current_zones: []` deserialises to a non-null empty `IReadOnlyList<string>`. Then implement `FrigateJsonOptions.Default` (`PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower`, `PropertyNameCaseInsensitive = true`, `DefaultIgnoreCondition = WhenWritingNull`) and the two internal sealed records `FrigateEvent { Type, Before, After }` and `FrigateEventObject { Id, Camera, Label, SubLabel?, Score, TopScore, StartTime, EndTime?, Stationary, Active, FalsePositive, CurrentZones=[], EnteredZones=[], HasSnapshot, Thumbnail?, FrameTime }` using `required` on non-nullable refs. All public types get `<summary>` XML docs.</action>
  <verify>dotnet build tests/FrigateRelay.Sources.FrigateMqtt.Tests -c Release && dotnet run --project tests/FrigateRelay.Sources.FrigateMqtt.Tests -c Release --no-build</verify>
  <done>All 6 deserialization tests pass; build is warning-free; running `git grep -n "Newtonsoft" src/FrigateRelay.Sources.FrigateMqtt/` returns zero matches.</done>
</task>

<task id="3" files="src/FrigateRelay.Sources.FrigateMqtt/Configuration/FrigateMqttOptions.cs" tdd="false">
  <action>Create `FrigateMqttOptions` (public record) with: `Server` (string, default `"localhost"`), `Port` (int, default `1883`), `ClientId` (string, default `"frigate-relay"`), `Topic` (string, default `"frigate/events"`), and `Tls` (nested `FrigateMqttTlsOptions { bool Enabled = false; bool AllowInvalidCertificates = false }`). **Do NOT add a `Subscriptions` member here** — subscriptions are host-level (PLAN-1.2 / PLAN-3.1). All members get XML doc comments. The XML doc on `FrigateMqttOptions` explicitly states "Subscription rules are bound separately from the top-level `Subscriptions` configuration section by the host; this options type covers MQTT transport only."</action>
  <verify>dotnet build src/FrigateRelay.Sources.FrigateMqtt -c Release && grep -n "Subscriptions" src/FrigateRelay.Sources.FrigateMqtt/Configuration/FrigateMqttOptions.cs</verify>
  <done>`FrigateMqttOptions` compiles with zero warnings; XML docs are present on all public members; `grep` for `Subscriptions` in `FrigateMqttOptions.cs` returns zero matches (apart from the doc-comment phrase, which is acceptable but should be `Subscriptions configuration section` — verify intent manually).</done>
</task>

## Verification

```bash
cd /mnt/f/git/FrigateRelay
dotnet build -c Release
dotnet run --project tests/FrigateRelay.Sources.FrigateMqtt.Tests -c Release --no-build
git grep -n "Newtonsoft\|MemoryCache\.Default\|ServicePointManager\|\.Result\|\.Wait()" src/FrigateRelay.Sources.FrigateMqtt/ || echo "OK: no banned APIs"
git grep -n "SubscriptionOptions" src/FrigateRelay.Sources.FrigateMqtt/ || echo "OK: no host-level types leaked into the plugin"
```

Expected: solution builds with zero warnings; >= 6 deserialization tests pass; both grep guards print "OK".
