---
phase: phase-4-action-dispatcher-blueiris
plan: 1.2
wave: 1
dependencies: []
must_haves:
  - FrigateRelay.Plugins.BlueIris csproj scaffold
  - BlueIrisOptions (TriggerUrlTemplate required, AllowInvalidCertificates, RequestTimeout, QueueCapacity)
  - BlueIrisUrlTemplate (internal sealed partial; GeneratedRegex; FrozenSet allowlist; fail-fast on unknown placeholders)
  - FrigateRelay.Plugins.BlueIris.Tests csproj scaffold
  - URL template parse + resolve unit tests (allowlist enforcement, URL-encoding, fail-fast)
files_touched:
  - src/FrigateRelay.Plugins.BlueIris/FrigateRelay.Plugins.BlueIris.csproj
  - src/FrigateRelay.Plugins.BlueIris/BlueIrisOptions.cs
  - src/FrigateRelay.Plugins.BlueIris/BlueIrisUrlTemplate.cs
  - tests/FrigateRelay.Plugins.BlueIris.Tests/FrigateRelay.Plugins.BlueIris.Tests.csproj
  - tests/FrigateRelay.Plugins.BlueIris.Tests/BlueIrisUrlTemplateTests.cs
  - FrigateRelay.sln
tdd: true
risk: low
---

# Plan 1.2: BlueIris plugin scaffold + URL template

## Context

Stands up the `FrigateRelay.Plugins.BlueIris` project, defines its options record, and implements + tests the URL template parser/resolver (CONTEXT-4 D3 + RESEARCH §7). No `BlueIrisActionPlugin` and no registrar in this plan — those land in PLAN-2.2 once the dispatcher contract from PLAN-1.1 is stable. The work in this plan is fully disjoint from PLAN-1.1's files, so the two plans run in parallel safely.

This plan owns ROADMAP deliverable 4 (partial — options + templater only) and the URL-template portion of deliverable 9.

**Architect decisions resolved inline:**
- **Q1 from RESEARCH §10 — RESOLVED: drop `{score}` from the allowlist.** Reading `src/FrigateRelay.Abstractions/EventContext.cs` confirms `EventContext` carries `EventId`, `Camera`, `Label`, `Zones`, `StartedAt`, `RawPayload`, `SnapshotFetcher` — **no `Score` property**. Adding `Score` would force a cross-phase edit to Abstractions and to `EventContextProjector` (Phase 3) for a feature CONTEXT-4 lists but no user has explicitly asked for. Final allowlist: `{camera}`, `{label}`, `{event_id}`, `{zone}`. This narrows D3's table from 5 placeholders to 4. If a user requests `{score}` later, adding it is additive (extend Abstractions, extend the FrozenSet, extend the switch).
- **Plugin-test project layout — RESOLVED: new `tests/FrigateRelay.Plugins.BlueIris.Tests/` project**, matching the Phase-3 precedent of one test project per source project. Keeps assemblies aligned and lets `<InternalsVisibleTo Include="FrigateRelay.Plugins.BlueIris.Tests" />` stay tightly scoped.

## Dependencies

None (Wave 1).

## Files touched

- `src/FrigateRelay.Plugins.BlueIris/FrigateRelay.Plugins.BlueIris.csproj` (create)
- `src/FrigateRelay.Plugins.BlueIris/BlueIrisOptions.cs` (create)
- `src/FrigateRelay.Plugins.BlueIris/BlueIrisUrlTemplate.cs` (create)
- `tests/FrigateRelay.Plugins.BlueIris.Tests/FrigateRelay.Plugins.BlueIris.Tests.csproj` (create)
- `tests/FrigateRelay.Plugins.BlueIris.Tests/BlueIrisUrlTemplateTests.cs` (create)
- `FrigateRelay.sln` (modify — `dotnet sln add` both new projects)

## Tasks

### Task 1: Scaffold the BlueIris plugin csproj + BlueIrisOptions
**Files:** `src/FrigateRelay.Plugins.BlueIris/FrigateRelay.Plugins.BlueIris.csproj`, `src/FrigateRelay.Plugins.BlueIris/BlueIrisOptions.cs`, `FrigateRelay.sln`
**Action:** create
**Description:**

Create `FrigateRelay.Plugins.BlueIris.csproj` mirroring the Phase-3 `FrigateRelay.Sources.FrigateMqtt` shape:
- `<Sdk>Microsoft.NET.Sdk</Sdk>` (inherits `Directory.Build.props`).
- `<TargetFramework>net10.0</TargetFramework>`.
- `<ProjectReference Include="..\FrigateRelay.Abstractions\FrigateRelay.Abstractions.csproj" />`.
- **No** reference to `FrigateRelay.Host` (plugin must not depend on host — CLAUDE.md invariant: host depends on abstractions only).
- `<InternalsVisibleTo Include="FrigateRelay.Plugins.BlueIris.Tests" />` MSBuild item form (NOT the assembly attribute).

Create `BlueIrisOptions` as a `public sealed record`:

```csharp
namespace FrigateRelay.Plugins.BlueIris;

public sealed record BlueIrisOptions
{
    /// <summary>Trigger URL template with allowlisted placeholders {camera} {label} {event_id} {zone} (CONTEXT-4 D3).</summary>
    public required string TriggerUrlTemplate { get; init; }

    /// <summary>When true, the BlueIris HttpClient skips TLS validation. Per-plugin opt-in only (CLAUDE.md invariant). Default false.</summary>
    public bool AllowInvalidCertificates { get; init; } = false;

    /// <summary>HttpClient timeout for the trigger request. Default 10s.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Optional override for the dispatcher channel capacity. When null, dispatcher default (256) is used.</summary>
    public int? QueueCapacity { get; init; } = null;
}
```

Note `required` with no `[SetsRequiredMembers]` ctor — record default ctor + object initializer is fine here (no convenience ctor needed).

Add both projects to the solution: `dotnet sln FrigateRelay.sln add src/FrigateRelay.Plugins.BlueIris/FrigateRelay.Plugins.BlueIris.csproj`.

**Acceptance Criteria:**
- `src/FrigateRelay.Plugins.BlueIris/FrigateRelay.Plugins.BlueIris.csproj` builds in isolation: `dotnet build src/FrigateRelay.Plugins.BlueIris/FrigateRelay.Plugins.BlueIris.csproj -c Release` returns exit 0 with zero warnings.
- `BlueIrisOptions` is a `public sealed record` with the four properties above; `TriggerUrlTemplate` is `required`.
- `<InternalsVisibleTo Include="FrigateRelay.Plugins.BlueIris.Tests" />` appears in the csproj (verify with `grep` on the file).
- `git grep -n "FrigateRelay.Plugins.BlueIris" FrigateRelay.sln` returns at least one match.

### Task 2: Implement BlueIrisUrlTemplate (parser + resolver)
**Files:** `src/FrigateRelay.Plugins.BlueIris/BlueIrisUrlTemplate.cs`
**Action:** create
**Description:**

Per RESEARCH §7, but **with `{score}` removed from the allowlist** (Q1 resolution above):

```csharp
namespace FrigateRelay.Plugins.BlueIris;

internal sealed partial class BlueIrisUrlTemplate
{
    [GeneratedRegex(@"\{(?<name>[a-z_]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    private static readonly FrozenSet<string> AllowedTokens =
        new[] { "camera", "label", "event_id", "zone" }.ToFrozenSet(StringComparer.Ordinal);

    private readonly string _template;

    private BlueIrisUrlTemplate(string template) => _template = template;

    /// <summary>
    /// Parses the template and validates that every {placeholder} is in the allowlist.
    /// Throws <see cref="ArgumentException"/> with a diagnostic listing the offending token
    /// AND the full allowed set. Caller (registrar) wraps in OptionsValidationException via
    /// .Validate(...).ValidateOnStart() so the host fails fast at startup (PROJECT.md S2).
    /// </summary>
    public static BlueIrisUrlTemplate Parse(string template)
    {
        if (string.IsNullOrWhiteSpace(template))
            throw new ArgumentException("BlueIris.TriggerUrlTemplate must not be null or whitespace.", nameof(template));

        foreach (Match m in TokenRegex().Matches(template))
        {
            var name = m.Groups["name"].Value;
            if (!AllowedTokens.Contains(name))
                throw new ArgumentException(
                    $"BlueIris.TriggerUrlTemplate contains unknown placeholder '{{{name}}}'. " +
                    $"Allowed placeholders: {{camera}}, {{label}}, {{event_id}}, {{zone}}.",
                    nameof(template));
        }
        return new BlueIrisUrlTemplate(template);
    }

    public string Resolve(EventContext ctx)
    {
        return TokenRegex().Replace(_template, m => m.Groups["name"].Value switch
        {
            "camera"   => Uri.EscapeDataString(ctx.Camera),
            "label"    => Uri.EscapeDataString(ctx.Label),
            "event_id" => Uri.EscapeDataString(ctx.EventId),
            "zone"     => Uri.EscapeDataString(ctx.Zones.Count > 0 ? ctx.Zones[0] : ""),
            _          => m.Value, // unreachable — Parse() guards
        });
    }
}
```

Mark `internal sealed partial` (partial is required for `[GeneratedRegex]`). The class is plugin-private — not exposed via `public`. Phase 5's `BlueIrisSnapshot` lives in this same assembly and reuses this type.

**Acceptance Criteria:**
- File compiles cleanly under `<Nullable>enable</Nullable>` and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.
- The FrozenSet contains exactly four members: `camera`, `label`, `event_id`, `zone`. **Confirm `score` is NOT present.**
- `BlueIrisUrlTemplate.Parse("https://x/{camera}?l={label}&e={event_id}&z={zone}")` returns a non-null instance.
- `BlueIrisUrlTemplate.Parse("https://x/{score}")` throws `ArgumentException` whose message contains `'{score}'` AND `Allowed placeholders:`.
- `Resolve` produces URL-encoded substitutions (e.g., a camera named `front door` becomes `front%20door`).
- The class is `internal sealed partial` and uses `[GeneratedRegex]` (NOT `new Regex(...)` — source-generated regex per .NET 7+ best practice).

### Task 3: BlueIrisUrlTemplate unit tests
**Files:** `tests/FrigateRelay.Plugins.BlueIris.Tests/FrigateRelay.Plugins.BlueIris.Tests.csproj`, `tests/FrigateRelay.Plugins.BlueIris.Tests/BlueIrisUrlTemplateTests.cs`
**Action:** create + test
**Description:**

Create the test csproj mirroring the Phase-3 `FrigateRelay.Sources.FrigateMqtt.Tests` shape:
- `<Sdk>Microsoft.NET.Sdk</Sdk>`, `<TargetFramework>net10.0</TargetFramework>`, `<OutputType>Exe</OutputType>`.
- MSTest v3 + Microsoft.Testing.Platform package refs.
- `FluentAssertions` pinned to `6.12.2`.
- `<ProjectReference Include="..\..\src\FrigateRelay.Plugins.BlueIris\FrigateRelay.Plugins.BlueIris.csproj" />`.
- `<ProjectReference Include="..\..\src\FrigateRelay.Abstractions\FrigateRelay.Abstractions.csproj" />`.

Add to solution: `dotnet sln FrigateRelay.sln add tests/FrigateRelay.Plugins.BlueIris.Tests/FrigateRelay.Plugins.BlueIris.Tests.csproj`.

Tests in `BlueIrisUrlTemplateTests.cs` (test names use underscores):

1. **`Parse_WithKnownPlaceholders_ReturnsInstance`** — `{camera}`, `{label}`, `{event_id}`, `{zone}` all parse without throwing.
2. **`Parse_WithUnknownPlaceholder_ThrowsArgumentException_WithDiagnosticMessage`** — `"https://x/{nope}"` throws; exception `Message` contains `'{nope}'` AND `Allowed placeholders:`.
3. **`Parse_WithScorePlaceholder_ThrowsBecauseScoreIsNotInAllowlist`** — `"https://x/{score}"` throws (encodes the Q1 deferral; if a future agent adds `Score` to `EventContext`, this test must be updated AND `score` added to the FrozenSet).
4. **`Parse_WithEmptyTemplate_Throws`** — null and whitespace both throw.
5. **`Resolve_UrlEncodesValues`** — given `EventContext { Camera = "front door", Label = "person", EventId = "ev-1", Zones = ["drive way"] }`, the template `"x?c={camera}&l={label}&e={event_id}&z={zone}"` resolves to `"x?c=front%20door&l=person&e=ev-1&z=drive%20way"`.
6. **`Resolve_WithEmptyZones_SubstitutesEmptyString`** — given `Zones = []`, `{zone}` becomes empty string (per D3 + RESEARCH §7).
7. **`Resolve_WithMultipleZones_UsesFirstZone`** — given `Zones = ["a", "b"]`, `{zone}` becomes `a` (first-zone v1 behavior per D3).

For test EventContext fixtures, build via object initializer (`EventContext` has `required init` properties + no `[SetsRequiredMembers]` ctor):

```csharp
private static EventContext NewCtx(string camera = "front", string label = "person",
    string eventId = "ev-1", IReadOnlyList<string>? zones = null) => new()
{
    EventId = eventId, Camera = camera, Label = label,
    Zones = zones ?? Array.Empty<string>(),
    StartedAt = DateTimeOffset.UnixEpoch,
    RawPayload = "{}",
    SnapshotFetcher = static _ => ValueTask.FromResult<byte[]?>(null),
};
```

Use FluentAssertions `.Should().Throw<ArgumentException>().WithMessage("*{nope}*")` etc.

**Acceptance Criteria:**
- 7 test methods named exactly as specified above; all pass via `dotnet run --project tests/FrigateRelay.Plugins.BlueIris.Tests -c Release -- --filter-query "/*/*/BlueIrisUrlTemplateTests/*"`.
- Test #3 explicitly asserts the `{score}` failure mode (encodes the Q1 architectural decision so any future regressor must consciously reverse it).
- `git grep -n "score" src/FrigateRelay.Plugins.BlueIris/BlueIrisUrlTemplate.cs` returns ZERO matches (the deferral is honored — no leftover code mentioning score).
- `git grep -nE '\.(Result|Wait)\(' tests/FrigateRelay.Plugins.BlueIris.Tests/` returns zero matches.
- FluentAssertions package reference is exactly `Version="6.12.2"`.

## Verification

```bash
dotnet build FrigateRelay.sln -c Release
dotnet run --project tests/FrigateRelay.Plugins.BlueIris.Tests -c Release -- --filter-query "/*/*/BlueIrisUrlTemplateTests/*"
grep -E 'Version="6\.12\.2"' tests/FrigateRelay.Plugins.BlueIris.Tests/FrigateRelay.Plugins.BlueIris.Tests.csproj
git grep -n "score" src/FrigateRelay.Plugins.BlueIris/
git grep -nE '\.(Result|Wait)\(' src/ tests/
```

Expected: build clean, 7 tests pass, FluentAssertions pin verified, no `score` references in BlueIris source, no `.Result/.Wait` matches.

## Notes for the builder

- `[GeneratedRegex]` requires `partial` on the class. `RegexOptions.CultureInvariant` matches RESEARCH §7's exact spec; do NOT add `RegexOptions.IgnoreCase` — placeholder names are lowercase by convention and the regex character class `[a-z_]+` already enforces that.
- `FrozenSet` is in `System.Collections.Frozen` (BCL since .NET 8; available in .NET 10).
- The exception thrown by `Parse` is `ArgumentException`. The registrar in PLAN-2.2 wraps the call inside `services.AddOptions<BlueIrisOptions>().Validate(...).ValidateOnStart()` so the failure surfaces as `OptionsValidationException` at host start — fail-fast per S2.
- Q1 resolution (drop `{score}`) is deliberately encoded in test #3 so the verifier and any future agent see the choice rather than guess at it. If `Score` is later added to `EventContext`, both the FrozenSet AND test #3 must be updated in the same commit.
- CI scripts auto-discover `tests/FrigateRelay.Plugins.BlueIris.Tests/` via the `find tests/*.Tests/*.Tests.csproj` glob in `.github/scripts/run-tests.sh` — no CI edits needed for this plan.
