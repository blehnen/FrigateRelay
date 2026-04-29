---
phase: phase-8-profiles
plan: 3.1
wave: 3
dependencies: [2.1]
must_haves:
  - config/appsettings.Example.json reproduces the 9-subscription deployment in Profiles shape
  - tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf sourcing prompt + sanitization checklist
  - ConfigSizeParityTest hard-fails on missing fixture (D9), passes when JSON <= 60% of INI char count
  - Both fixture files copied to test output via CopyToOutputDirectory
files_touched:
  - config/appsettings.Example.json
  - tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf
  - tests/FrigateRelay.Host.Tests/Configuration/ConfigSizeParityTest.cs
  - tests/FrigateRelay.Host.Tests/FrigateRelay.Host.Tests.csproj
tdd: true
risk: low
---

# Plan 3.1: Example Config + Legacy Fixture + ConfigSizeParityTest

## Context

Phase 8's Success Criterion #2 (PROJECT.md, ROADMAP.md) — "configuration is meaningfully shorter than the legacy INI" — is operationalized in this plan as a CI gate. Per **D3** the parity test measures the **real** sanitized production INI, not a synthetic one. Per **D9** the test hard-fails when `tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf` is missing — no `Assert.Inconclusive` skip path. Per **D6** the user manually sanitizes their real conf using `.shipyard/phases/8/SANITIZATION-CHECKLIST.md` (delivered as a planning artifact alongside this plan, not a build task) and places the result at the fixture path before the test runs.

`config/appsettings.Example.json` is the JSON counterpart — same 9 subscriptions, same camera names, labels, zones, expressed in the new Profiles shape. Per CONTEXT-8 cross-cutting note 4, names must align so the comparison is meaningful. Per **D4/D5** the JSON uses 3-tier snapshot precedence and the flat-dictionary profile shape.

The path-resolution problem from RESEARCH.md §6 (test exe runs five `..` hops below repo root) is solved by adding both files to the test csproj as `<None CopyToOutputDirectory=PreserveNewest>` items. The test then reads them via `Path.Combine(AppContext.BaseDirectory, ...)`.

## Dependencies

- **PLAN-2.1** — needs the resolved profile pipeline to validate the example config can actually load (the parity test invokes the host's bind path + validator to prove the example is well-formed, not just shorter).

## Tasks

### Task 1: `appsettings.Example.json` + csproj wiring + builder fixture-sourcing prompt
**Files:**
- `config/appsettings.Example.json` (new)
- `tests/FrigateRelay.Host.Tests/FrigateRelay.Host.Tests.csproj` (modify — add two `<None>` items)
- `tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf` — **NOT created by the builder**; sourced by the user per `.shipyard/phases/8/SANITIZATION-CHECKLIST.md`

**Action:** create + modify + (user-supplied content)
**TDD:** false (fixture content; the test is in Task 2)

**Description:**
Create `config/appsettings.Example.json` reproducing the author's 9-subscription deployment in the Profiles shape. The JSON must:

- Use the new top-level `"Profiles"` dictionary (D5 — flat, no nesting).
- Each subscription declares `"Profile": "<name>"` OR `"Actions": [...]`, never both (D1).
- Reuse profile names where action-list shape repeats (this is the dedup the test measures).
- Use **only** the object form for `Actions` arrays inside profiles (CLAUDE.md ID-12 invariant note — until Phase 8 closes ID-12; the example is the canonical reference).
- Per-action `SnapshotProvider:` overrides where needed (D4).
- Subscription-level `DefaultSnapshotProvider:` where appropriate (D4 Tier 2).
- Global `Snapshots: { DefaultProviderName: "..." }` (D4 Tier 3).
- **Zero secrets** — `AppToken: ""`, `UserKey: ""`, with a JSON `// comment` (or top-level `_comment` key) noting these are env-var overrides (CLAUDE.md "no secrets" invariant).
- **Zero hard-coded IPs/hostnames** — use `example.local` or RFC 5737 prefixes (CLAUDE.md). All BlueIris/Frigate/Pushover URLs use `example.local:<port>` form.
- Camera names, labels, zones must match the user's real deployment (CONTEXT-8 note 4) so `legacy.conf` and this file describe the same system.

The builder must NOT invent the camera list — the camera names, count, and labels come from the user's `legacy.conf` after the user sanitizes it. Builder workflow:
1. Verify `tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf` exists.
2. If missing: STOP, halt the build with the exact prompt: `"legacy.conf fixture missing. Sanitize your real FrigateMQTTProcessingService.conf per .shipyard/phases/8/SANITIZATION-CHECKLIST.md and place the redacted result at tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf, then re-run."`
3. Once the file is present, read its `[SubscriptionSettings]` section names/labels/zones and produce `config/appsettings.Example.json` mirroring them under the Profiles shape.

Modify `tests/FrigateRelay.Host.Tests/FrigateRelay.Host.Tests.csproj` to add:

```xml
<ItemGroup>
  <None Update="Fixtures/legacy.conf">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
  <None Include="..\..\config\appsettings.Example.json" Link="Fixtures/appsettings.Example.json">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

The linked file pattern (RESEARCH.md §6 recommendation) avoids relative-path climbing in the test.

**Acceptance Criteria:**
- `config/appsettings.Example.json` exists and parses as valid JSON: `dotnet run --project tests/FrigateRelay.Host.Tests -c Release -- --filter-query "/*/*/ConfigSizeParityTest/*"` (Task 2) does not throw a `JsonException` when loading it.
- `tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf` exists (user-supplied; see SANITIZATION-CHECKLIST.md).
- `git grep -nE 'AppToken=[A-Za-z0-9]{20,}|UserKey=[A-Za-z0-9]{20,}|192\.168\.[0-9]+\.[0-9]+' config/ tests/FrigateRelay.Host.Tests/Fixtures/` returns zero matches.
- `git grep -nE 'AppToken|UserKey' config/appsettings.Example.json` shows only empty-string assignments.
- `dotnet build FrigateRelay.sln -c Release` exits 0; both fixture files copy to `tests/FrigateRelay.Host.Tests/bin/Release/net10.0/Fixtures/`.

### Task 2: `ConfigSizeParityTest`
**Files:**
- `tests/FrigateRelay.Host.Tests/Configuration/ConfigSizeParityTest.cs` (new)

**Action:** create
**TDD:** true (test written and runnable; passes only when the fixture exists and the JSON is short enough)

**Description:**
Create a single test method `Json_Is_At_Most_60_Percent_Of_Ini_Character_Count` (underscore-naming, CLAUDE.md). Behavior:

```csharp
var iniPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "legacy.conf");
var jsonPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "appsettings.Example.json");

if (!File.Exists(iniPath))
{
    Assert.Fail(
        "legacy.conf fixture missing at " + iniPath + ". " +
        "Sanitize your real FrigateMQTTProcessingService.conf per " +
        ".shipyard/phases/8/SANITIZATION-CHECKLIST.md and place the " +
        "redacted result at the path above. This test cannot run without it.");
}

var iniLength = File.ReadAllText(iniPath).Length;     // raw char count, no whitespace stripping (D3, D9)
var jsonLength = File.ReadAllText(jsonPath).Length;
var ratio = (double)jsonLength / iniLength;
ratio.Should().BeLessOrEqualTo(0.60,
    $"JSON ({jsonLength} chars) must be <= 60% of INI ({iniLength} chars); ratio was {ratio:P1}");
```

Per **D9**: no environment branch, no `Assert.Inconclusive`. The fail message must use the verbatim text above so the user recognizes it from the SANITIZATION-CHECKLIST.md cross-reference.

Per **D3**: char count is raw `File.ReadAllText().Length` — no `Trim`, no whitespace stripping, no normalization. Whitespace counts because INI's structural whitespace is part of what makes it verbose.

Optionally, also bind the example JSON via `IConfiguration.Bind` and run `ProfileResolver.Resolve` + `StartupValidation.ValidateAll` to prove the example is not just shorter but valid. This is a single additional sub-assertion in the same test method — if the example ever drifts out of valid shape, the parity gate catches it. Use a minimal `IServiceProvider` with stub `IActionPlugin` / `ISnapshotProvider` / `IValidationPlugin` keyed registrations matching the names in the example (BlueIris, Pushover, Frigate, CodeProjectAi).

**Acceptance Criteria:**
- File exists at `tests/FrigateRelay.Host.Tests/Configuration/ConfigSizeParityTest.cs`.
- When `legacy.conf` is absent: `dotnet run --project tests/FrigateRelay.Host.Tests -c Release -- --filter-query "/*/*/ConfigSizeParityTest/*"` exits non-zero with the verbatim D9 fail message in stdout.
- When `legacy.conf` is present and `appsettings.Example.json` is at most 60% of its char count: the test passes.
- The example config also binds and validates successfully (sub-assertion); a deliberately-broken example (typo a profile name) makes the test fail with the aggregated startup-validation message — confirms the example-validity sub-assertion fires.

### Task 3: Close-out — CLAUDE.md + ISSUES.md updates
**Files:**
- `CLAUDE.md` (modify — replace ID-12 invariant block, add D7 collect-all conventions block)
- `.shipyard/ISSUES.md` (modify — move ID-2, ID-10, ID-12 to Closed Issues)

**Action:** modify
**TDD:** false (documentation update)

**Description:**
**CLAUDE.md changes:**
1. In the invariants list, replace the current ID-12 block (the long paragraph beginning `"`Subscriptions:N:Actions` requires the object form"`) with a "since Phase 8" note: both forms now bind correctly via `ActionEntryTypeConverter`. Preserve the historical context as a one-line trailing sentence: `"Phase 8 closed ID-12 by adding ActionEntryTypeConverter; both string-array and object-array forms now bind via IConfiguration.Bind."`.
2. Add a new bullet to the "Conventions (discovered and locked in...)" block describing the collect-all validator pattern (D7): `"Startup validators accumulate errors into a shared List<string> and throw a single aggregated InvalidOperationException at the end. Operators see all misconfigurations at once, not piecemeal. Pattern lives in StartupValidation.ValidateAll. Phase 8 retrofitted ValidateActions/ValidateSnapshotProviders/ValidateValidators to this shape and added ProfileResolver."`.

**ISSUES.md changes:**
- Move ID-2, ID-10, ID-12 entries from "Open" to "Closed" sections (or wherever the file structures closure).
- Add a "Resolution" line on each citing the Phase 8 commit that closed it (the builder will fill in actual SHAs at commit time — leave a `<commit-sha>` placeholder).
- For ID-12: cite `ActionEntryTypeConverter` + the 3 unit tests + the example-config integration as the closing evidence.
- For ID-2 + ID-10: cite the visibility sweep (PLAN-1.1) — list the 9 types now `internal sealed`.

**Acceptance Criteria:**
- `git grep -nE 'silently produces an empty .Actions. list' CLAUDE.md` returns zero matches (the old warning is gone).
- `git grep -nE 'Phase 8 closed ID-12' CLAUDE.md` returns at least one match.
- `git grep -nE 'collect-all|ValidateAll' CLAUDE.md` returns at least one match (new conventions block).
- `git grep -nE 'ID-2|ID-10|ID-12' .shipyard/ISSUES.md` shows each appearing under a Closed/Resolved section.
- `dotnet build FrigateRelay.sln -c Release` still exits 0 (sanity — doc-only changes shouldn't break build, but verify).
- `dotnet run --project tests/FrigateRelay.Host.Tests -c Release` — full suite passes.

## Verification

- `dotnet build FrigateRelay.sln -c Release` — zero warnings.
- `dotnet run --project tests/FrigateRelay.Host.Tests -c Release` — full suite passes (>= 10 ProfileResolutionTests + 3 ActionEntryTypeConverterTests + 1 ConfigSizeParityTest + all pre-existing tests).
- `dotnet run --project tests/FrigateRelay.Host.Tests -c Release -- --filter-query "/*/*/ConfigSizeParityTest/*"` — passes (assuming `legacy.conf` is present).
- `git grep -nE 'AppToken=[A-Za-z0-9]{20,}|UserKey=[A-Za-z0-9]{20,}' tests/ config/` — empty.
- `git grep -nE '192\.168\.[0-9]+\.[0-9]+' tests/FrigateRelay.Host.Tests/Fixtures/ config/` — empty.
- `git grep -nE 'silently produces an empty .Actions. list' CLAUDE.md` — empty.
- `git grep -nE 'Phase 8 closed ID-12' CLAUDE.md` — at least one match.
- `bash .github/scripts/secret-scan.sh` — exits 0 (tripwire-clean across the new fixture and example config).
