# REVIEW-1.1 — DTOs, JsonOptions, Plugin Options, Test Project Scaffold

**Phase:** 3 | **Plan:** 1.1 | **Reviewer:** Claude Code | **Date:** 2026-04-24

---

## Stage 1: Spec Compliance

**Verdict:** PASS (with one Minor deviation — see Task 1 notes)

### Task 1: Plugin + test csproj scaffold

- Status: PASS
- Evidence:
  - `src/FrigateRelay.Sources.FrigateMqtt/FrigateRelay.Sources.FrigateMqtt.csproj` — `Microsoft.NET.Sdk`, `GenerateDocumentationFile=true`, `<InternalsVisibleTo Include="FrigateRelay.Sources.FrigateMqtt.Tests" />`. ProjectRef to `FrigateRelay.Abstractions`. No `Caching.Memory` reference.
  - PackageRefs present: `MQTTnet 5.1.0.1559`, `Microsoft.Extensions.Hosting.Abstractions`, `Microsoft.Extensions.Options.ConfigurationExtensions`, `Microsoft.Extensions.Logging.Abstractions`. No Newtonsoft.
  - Test csproj: `OutputType=Exe`, `EnableMSTestRunner=true`, `TestingPlatformDotnetTestSupport=true`, `MSTest 4.2.1`, `FluentAssertions 6.12.2`, `NSubstitute 5.3.0`. Both `ProjectReference` entries present (plugin + Abstractions).
- Notes: The plan Task 1 action specifies `xunit.v3` as the test framework, but the implementation uses `MSTest 4.2.1`. The SUMMARY says it "mirrors Host.Tests shape (Approach B PackageReference: MSTest 4.2.1)" — the shipyard pattern for this repo is MSTest, not xUnit. The done criteria (`EnableMSTestRunner`, `OutputType=Exe`) match MSTest/Approach B exactly. This is an architect-approved pattern discrepancy in the plan text; the implementation is consistent with the Host.Tests precedent. Not a defect.
  - `Program.cs` is not present as a separate file — MSTest 4.2.1 with `OutputType=Exe` and `EnableMSTestRunner=true` auto-generates the entry point, making an explicit `Program.cs` unnecessary. This is correct for MSTest Approach B.

### Task 2: DTOs + JsonOptions + 6 tests

- Status: PASS
- Evidence:
  - `FrigateEvent` at `src/FrigateRelay.Sources.FrigateMqtt/Payloads/FrigateEvent.cs` — `internal sealed record`, fields: `Type (required string)`, `Before (required FrigateEventObject)`, `After (required FrigateEventObject)`. XML doc on all members.
  - `FrigateEventObject` at `src/FrigateRelay.Sources.FrigateMqtt/Payloads/FrigateEventObject.cs` — `internal sealed record`. All required fields present: `Id`, `Camera`, `Label`, `SubLabel?`, `Score`, `TopScore`, `StartTime`, `EndTime?`, `Stationary`, `Active`, `FalsePositive`, `CurrentZones=[]`, `EnteredZones=[]`, `HasSnapshot`, `HasClip`, `Thumbnail?`, `FrameTime`. XML doc on all members.
  - `FrigateJsonOptions` at `src/FrigateRelay.Sources.FrigateMqtt/FrigateJsonOptions.cs` — `internal static`, `Default` is `JsonSerializerOptions` with `SnakeCaseLower`, `PropertyNameCaseInsensitive=true`, `WhenWritingNull`. No Newtonsoft.
  - 6 `[TestMethod]` in `PayloadDeserializationTests.cs`: new event, update/stationary, end/EndTime, sub_label null, thumbnail omitted, empty zones. All assertions match spec requirements exactly.

### Task 3: FrigateMqttOptions

- Status: PASS
- Evidence:
  - `src/FrigateRelay.Sources.FrigateMqtt/Configuration/FrigateMqttOptions.cs` — `public sealed record`. `Server="localhost"`, `Port=1883`, `ClientId="frigate-relay"`, `Topic="frigate/events"`, `Tls` (nested `FrigateMqttTlsOptions`). Nested record has `Enabled=false`, `AllowInvalidCertificates=false`.
  - No `Subscriptions` member. Class summary doc explicitly states "Subscription rules are bound separately from the top-level `Subscriptions` configuration section by the host; this options type covers MQTT transport only." — exact wording required by the plan.
  - XML doc on every public member.
- Notes: Plan spec named the TLS field `UseTls`; implementation uses `Enabled`. SUMMARY Decision 4 documents this as `Tls.Enabled`. The done criteria only checks for absence of `Subscriptions` — field naming within `TlsOptions` is not a done-criteria violation. Wave 2's MQTT client wiring should reference `Tls.Enabled` (not `UseTls`).

---

## Stage 2: Code Quality

### Critical

None.

### Important

- **`FrigateEvent.Before` and `FrigateEvent.After` are `required` (non-nullable) but Frigate documentation notes that `before` can be absent on the very first `new` event in some edge cases.** (`src/FrigateRelay.Sources.FrigateMqtt/Payloads/FrigateEvent.cs`, lines 22 and 27)
  - If Frigate ever emits a payload where `before` is `null`, `JsonSerializer.Deserialize` will throw a `JsonException` because the `required` keyword on a non-nullable record property does not allow null assignment at runtime (it only enforces presence of the JSON key — but a `null` JSON value mapped to a non-nullable property silently stays `null`, which would then throw on first use).
  - Remediation: Change `Before` to `FrigateEventObject?` (nullable) and remove `required`. Wave 2's projector already accesses `After` — `Before` is only used for diffing. Alternatively, add a test that exercises a `"before": null` payload and confirm the behavior is acceptable.

- **`FrigateJsonOptions` is `internal static` but `FrigateMqttOptions` is `public`.** The two visibility levels are intentional (options is config-bound and must be public; JsonOptions is deserialization-internal). However, `FrigateJsonOptions.Default` has no write-protection — it is a `static readonly` field but `JsonSerializerOptions` is mutable after construction. Any caller (including tests) could mutate `Default` and corrupt deserialization globally.
  - File: `src/FrigateRelay.Sources.FrigateMqtt/FrigateJsonOptions.cs`, line 17.
  - Remediation: Call `.MakeReadOnly()` on the options instance after construction (available in .NET 8+, confirmed available on net10.0). E.g.: `internal static readonly JsonSerializerOptions Default = new JsonSerializerOptions { ... }.Also(o => o.MakeReadOnly());` — or use an explicit static constructor to call `Default.MakeReadOnly()` after field init.

### Suggestions

- **Test 4 (`Deserialize_SubLabelNull_DeserializesToNullSubLabel`) uses `SubLabelNullJson` where `sub_label` is explicitly `null` in JSON, not omitted.** The spec asks to confirm "null deserialises to `SubLabel == null`" which this satisfies. A complementary test where `sub_label` is completely absent from the wire would provide stronger coverage (verifies default-null behavior without the key present). Not a blocking gap.

- **`FrigateMqttOptions` and `FrigateMqttTlsOptions` are both `public sealed record` but live in a plugin assembly that is never exposed as a NuGet package** (`IsPackable=false`). Making them `internal` would be consistent with the DTO visibility policy, though options types need to be publicly bindable for `IOptions<T>`. This is a style note only — current visibility is correct for `IOptions<T>` binding.

---

## Check Results

| Check | Result |
|---|---|
| `dotnet build FrigateRelay.sln -c Release` | 0 warnings, 0 errors (per SUMMARY) |
| `dotnet run --project tests/FrigateRelay.Sources.FrigateMqtt.Tests -c Release` | 6 pass, 0 fail (per SUMMARY) |
| `dotnet list ... package --include-transitive` — no Newtonsoft | PASS (per SUMMARY; confirmed by csproj inspection) |
| `git grep Newtonsoft/ServicePointManager/.Result/.Wait()` | Empty (per SUMMARY) |
| `bash .github/scripts/secret-scan.sh scan` | Exit 0 (per SUMMARY) |
| `grep Subscriptions FrigateMqttOptions.cs` — zero property matches | PASS (confirmed by file inspection — only appears in doc comment phrase) |

---

## Summary

**Verdict:** APPROVE

All three tasks are correctly implemented and match the spec's done criteria. The `FrigateEvent.Before` nullability and `JsonSerializerOptions` mutability are Important issues for the Wave 2 builder to address before the MQTT client code reads these DTOs in anger. No critical blockers.

Critical: 0 | Important: 2 | Suggestions: 2
