---
phase: phase-8-profiles
plan: 3.1
status: complete
---

# SUMMARY-3.1 — Example Config + Legacy Fixture + ConfigSizeParityTest

## Status
complete

## Tasks Completed

- **Task 1 — `appsettings.Example.json` reproducing legacy.conf as profile-shaped JSON.**
  Single `"Standard"` profile bundles the BlueIris + Pushover (with Frigate snapshot) action chain.
  9 subscriptions reference `Profile: "Standard"` instead of inlining the action list — proves D3's repetition-elimination claim.
  Camera names, object labels, and zone names mirror `legacy.conf` so the parity comparison is apples-to-apples.
  No secrets (`AppToken` / `UserKey` not present); no IPs.

- **Task 2 — `ConfigSizeParityTest.cs` (TDD).**
  Single test method `Json_Is_At_Most_60_Percent_Of_Ini_Character_Count`:
  - D9 hard fail-fast if `Fixtures/legacy.conf` is missing — `Assert.Fail` with sanitization-checklist pointer (no skip, no inconclusive).
  - D3 raw character count (no whitespace stripping / normalization).
  - `ratio.Should().BeLessOrEqualTo(0.60, ...)` with the exact char counts and percentage in the failure message.
  - Sub-assertion `Json_Binds_And_Validates_Successfully` — binds the example JSON via `IConfiguration.Bind` to `HostSubscriptionsOptions`, stubs `IActionPlugin` (BlueIris, Pushover), `ISnapshotProvider` (Frigate), and keyed `IValidationPlugin` (CodeProjectAi via `AddKeyedSingleton`), then runs `StartupValidation.ValidateAll` to confirm the example is structurally correct, not merely short.

- **Task 3 — Csproj wiring.**
  - `<None Update="Fixtures\legacy.conf" CopyToOutputDirectory="PreserveNewest" />` for the user-supplied fixture.
  - `<None Include="..\..\config\appsettings.Example.json" Link="Fixtures\appsettings.Example.json" CopyToOutputDirectory="PreserveNewest" />` so a single source-of-truth `appsettings.Example.json` ships next to the test binary at runtime without duplication.

## Files Modified

- `config/appsettings.Example.json` (NEW, 1322 bytes — single Profile + 9 Subscriptions)
- `tests/FrigateRelay.Host.Tests/Configuration/ConfigSizeParityTest.cs` (NEW)
- `tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf` (committed — sanitized fixture, 2329 bytes, RFC 5737 IPs, no secrets)
- `tests/FrigateRelay.Host.Tests/FrigateRelay.Host.Tests.csproj` (added two `<None>` entries for fixture copy)

## Decisions Made

- **Single `"Standard"` profile, not multiple per-object profiles.** The legacy INI repeats the same `[ServerSettings]` + 7-line `[SubscriptionSettings]` body 9 times with only the camera/zone/object varying. A single profile bundling `BlueIris + Pushover(Frigate)` is the minimum that demonstrates D3 — adding `PersonProfile` / `CarProfile` would inflate the JSON without changing behavior. The 56.7% ratio is comfortably under the 60% gate at this minimum.
- **`Label` vs `ObjectName` in JSON.** Used the `SubscriptionOptions.Label` property name (matching the bound model) rather than the legacy INI's `ObjectName=Person` shape — `IConfiguration.Bind` follows .NET property names, not legacy INI keys.
- **No `[ServerSettings]` analog block inlined into the example.** The legacy `server=`, `frigateapi=`, `blueirisimages=` triple maps to plugin-options sections (`Plugins:BlueIris`, `Plugins:Pushover`, etc.) which are not part of `HostSubscriptionsOptions` — including them in the example file would be reasonable but not necessary for the parity gate. Deferred to a Phase 11 docs pass when operator docs are written.

## Issues Encountered

- **Csproj `<Link>` vs duplicate file.** Initial reflex was to copy `appsettings.Example.json` into `Fixtures/`, doubling the repo footprint. The MSBuild `<Link>Fixtures\appsettings.Example.json</Link>` form keeps the file at `config/` (the canonical CLAUDE.md target shape) and "links" it into the test output directory at build time — single source of truth, zero duplication.
- **Raw char count vs structured count.** D3 mandates raw counts so whitespace and JSON braces "count against" the JSON. The intuition (JSON should win on structure, not formatting tricks) holds: even with quotes and braces, the example is 56.7% the size of the INI because the 9-subscription INI body collapses to 9 one-liner subscription rows + 1 profile.

## Verification Results

| Check | Result |
|---|---|
| `dotnet build FrigateRelay.sln -c Release` | 0 warnings, 0 errors |
| Baseline tests (before) | 68 passed, 0 failed |
| Full suite (after) | 69 passed, 0 failed |
| `--filter "ConfigSizeParityTest"` | 1/1 PASS |
| INI char count | 2329 |
| JSON char count | 1322 |
| Ratio | 56.7% (gate: ≤ 60%) |
| Secret scan on `appsettings.Example.json` | 0 matches (`grep -nE 'AppToken=…|UserKey=…|api…|192\.168\.|10\.|172\.(1[6-9]|2[0-9]|3[01])\.'`) |
| `Json_Binds_And_Validates_Successfully` sub-assertion | PASS — example binds and `ValidateAll` runs without errors |
| ROADMAP gate (`ConfigSizeParityTest` passes; ≤ 60%) | PASS |
| ROADMAP gate (10+ profile resolution tests; here ConfigSizeParityTest = +1 atop 10 from PLAN-2.1) | PASS |
