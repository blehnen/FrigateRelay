---
phase: phase-8-profiles
plan: 1.1
wave: 1
dependencies: []
must_haves:
  - Visibility sweep (D8): nine types flipped to internal sealed in one pass
  - ProfileOptions type created internal from inception
  - SubscriptionOptions gains nullable Profile property
  - HostSubscriptionsOptions gains Profiles dictionary property
  - dotnet build remains warning-free post-sweep
files_touched:
  - src/FrigateRelay.Host/Configuration/SubscriptionOptions.cs
  - src/FrigateRelay.Host/Configuration/HostSubscriptionsOptions.cs
  - src/FrigateRelay.Host/Configuration/ProfileOptions.cs
  - src/FrigateRelay.Host/Configuration/ActionEntryJsonConverter.cs
  - src/FrigateRelay.Host/Snapshots/SnapshotResolverOptions.cs
  - src/FrigateRelay.Host/Dispatch/IActionDispatcher.cs
  - src/FrigateRelay.Host/Dispatch/DispatcherOptions.cs
  - src/FrigateRelay.Host/Matching/DedupeCache.cs
  - src/FrigateRelay.Host/Matching/SubscriptionMatcher.cs
  - src/FrigateRelay.Host/FrigateRelay.Host.csproj
tdd: false
risk: low
---

# Plan 1.1: Visibility Sweep + Profile/Subscription Option Surface

## Context

Phase 8 introduces named `Profiles` to the configuration schema (PROJECT.md S2) and closes ID-2, ID-10, and ID-12. Per **D8**, the nine option/dispatch/matching types currently held `public` solely because of CS0053 cascades from `SubscriptionOptions.Actions` (an `IReadOnlyList<ActionEntry>`) must move to `internal sealed` in a single atomic pass. The CS0053 risk noted in RESEARCH.md §10 means the sweep must be all-or-nothing — flipping a subset produces compile errors.

This plan also lands the new option-surface scaffolding (`ProfileOptions`, `SubscriptionOptions.Profile`, `HostSubscriptionsOptions.Profiles`) in the same pass — these new properties bind to the new top-level `"Profiles"` config key (RESEARCH.md §1) and per **D5** carry no other fields. `ActionEntry` is intentionally **not** modified here — it gains its `[TypeConverter]` attribute in PLAN-1.2 to keep file conflicts disjoint inside Wave 1.

`ActionEntry` itself stays its current visibility (`public`) for this plan — PLAN-1.2 owns that file. The CS0053 cascade still works because `SubscriptionOptions` becoming `internal` removes the constraint that forced `ActionEntry` to `public`; PLAN-1.2 then internalizes `ActionEntry` and `ActionEntryJsonConverter` together. Wave-1 ordering (1.1 before 1.2 — they share no files but depend on the sweep landing first conceptually) is enforced by the disjoint-file rule plus the visibility convention.

## Dependencies

None (Wave 1).

## Tasks

### Task 1: Flip eight types to `internal sealed` in one atomic pass
**Files:**
- `src/FrigateRelay.Host/Configuration/SubscriptionOptions.cs`
- `src/FrigateRelay.Host/Configuration/HostSubscriptionsOptions.cs`
- `src/FrigateRelay.Host/Snapshots/SnapshotResolverOptions.cs`
- `src/FrigateRelay.Host/Dispatch/IActionDispatcher.cs`
- `src/FrigateRelay.Host/Dispatch/DispatcherOptions.cs`
- `src/FrigateRelay.Host/Matching/DedupeCache.cs`
- `src/FrigateRelay.Host/Matching/SubscriptionMatcher.cs`
- `src/FrigateRelay.Host/FrigateRelay.Host.csproj` (verify only)

**Action:** modify
**TDD:** false (pure visibility refactor — no behavior change)

**Description:**
In a single atomic edit, change `public sealed record` / `public sealed class` / `public interface` declarations on each of the seven type files above to `internal sealed record` / `internal sealed class` / `internal interface`. `IActionDispatcher` is an interface and stays unsealed (`internal interface IActionDispatcher`). All other types use `internal sealed`. **Do not** modify `ActionEntry.cs` or `ActionEntryJsonConverter.cs` in this task — those move in Task 2 here, except `ActionEntry` itself which is owned by PLAN-1.2. Verify `<InternalsVisibleTo Include="FrigateRelay.Host.Tests" />` is already present in `src/FrigateRelay.Host/FrigateRelay.Host.csproj` (RESEARCH.md §10 + CLAUDE.md convention block — MSBuild item form, not source-level attribute). If missing, add it; do not change format. The PR-7 architectural invariant `IActionDispatcher` seam is preserved — only visibility changes, no signature edits.

**Acceptance Criteria:**
- `git grep -nE 'public (sealed )?(class|record|interface) (SubscriptionOptions|HostSubscriptionsOptions|SnapshotResolverOptions|IActionDispatcher|DispatcherOptions|DedupeCache|SubscriptionMatcher)\b' src/` returns zero matches.
- `git grep -nE 'internal (sealed )?(class|record|interface) (SubscriptionOptions|HostSubscriptionsOptions|SnapshotResolverOptions|IActionDispatcher|DispatcherOptions|DedupeCache|SubscriptionMatcher)\b' src/` returns exactly seven matches (one per type).
- `<InternalsVisibleTo Include="FrigateRelay.Host.Tests" />` is present in `src/FrigateRelay.Host/FrigateRelay.Host.csproj`.
- `dotnet build FrigateRelay.sln -c Release` exits 0 with zero warnings.

### Task 2: Internalize `ActionEntryJsonConverter` and add `ProfileOptions`
**Files:**
- `src/FrigateRelay.Host/Configuration/ActionEntryJsonConverter.cs`
- `src/FrigateRelay.Host/Configuration/ProfileOptions.cs` (new)

**Action:** modify + create
**TDD:** false (pure refactor + new type with no behavior beyond a property)

**Description:**
Flip `ActionEntryJsonConverter` from `public sealed class` to `internal sealed class`. Create the new `ProfileOptions.cs` containing exactly:

```csharp
namespace FrigateRelay.Host.Configuration;

internal sealed record ProfileOptions
{
    public IReadOnlyList<ActionEntry> Actions { get; init; } = Array.Empty<ActionEntry>();
}
```

Per **D5** the type carries `Actions` and **nothing else** — no `BasedOn`, no `DefaultSnapshotProvider`. Use `record` (not `class`) to mirror the `SubscriptionOptions` shape. The default-empty initializer matches the existing `SubscriptionOptions.Actions` default. `ActionEntry` itself stays its current visibility — PLAN-1.2 internalizes it alongside the `[TypeConverter]` attribute addition to keep file ownership disjoint.

**Acceptance Criteria:**
- `git grep -nE 'public sealed class ActionEntryJsonConverter' src/` returns zero matches.
- `git grep -nE 'internal sealed class ActionEntryJsonConverter' src/` returns one match.
- `src/FrigateRelay.Host/Configuration/ProfileOptions.cs` exists, declares `internal sealed record ProfileOptions`, contains a single `Actions` property, no other public/internal members.
- `dotnet build FrigateRelay.sln -c Release` exits 0 with zero warnings.

### Task 3: Add `Profile` and `Profiles` properties to subscription/host options
**Files:**
- `src/FrigateRelay.Host/Configuration/SubscriptionOptions.cs`
- `src/FrigateRelay.Host/Configuration/HostSubscriptionsOptions.cs`

**Action:** modify
**TDD:** false (additive properties; behavior wired in Wave 2 via the resolver)

**Description:**
Add `public string? Profile { get; init; }` to `SubscriptionOptions` (now `internal sealed record` post-Task 1). Add `public IReadOnlyDictionary<string, ProfileOptions> Profiles { get; init; } = new Dictionary<string, ProfileOptions>();` to `HostSubscriptionsOptions`. Both properties stay `public` on the now-`internal` types — public-on-internal is required for `IConfiguration.Bind` to set them via the binder's reflection path. Per **D1** the `Profile` and `Actions` fields on a subscription are mutually exclusive at runtime, but the type itself permits both — the validator (PLAN-2.1) enforces the mutex. Per **D4/D5** `Profiles` binds from the top-level `"Profiles"` config key (this happens automatically because `HostSubscriptionsOptions` is bound at the configuration root in `HostBootstrap.ConfigureServices`, RESEARCH.md §1).

Add a brief XML doc comment on `SubscriptionOptions.Profile` referencing the D1 mutex behavior, and on `HostSubscriptionsOptions.Profiles` referencing D5 (flat dictionary, no nesting).

**Acceptance Criteria:**
- `git grep -nE 'public string\? Profile' src/FrigateRelay.Host/Configuration/SubscriptionOptions.cs` returns one match.
- `git grep -nE 'public IReadOnlyDictionary<string, ProfileOptions> Profiles' src/FrigateRelay.Host/Configuration/HostSubscriptionsOptions.cs` returns one match.
- `dotnet build FrigateRelay.sln -c Release` exits 0 with zero warnings.
- `dotnet run --project tests/FrigateRelay.Host.Tests -c Release` — all existing tests pass (no regression from visibility/property additions).

## Verification

Run all from repo root:

- `dotnet build FrigateRelay.sln -c Release` — exit 0, zero warnings.
- `git grep -nE 'public (sealed )?(class|record|interface) (SubscriptionOptions|HostSubscriptionsOptions|SnapshotResolverOptions|IActionDispatcher|DispatcherOptions|DedupeCache|SubscriptionMatcher|ActionEntryJsonConverter)\b' src/` — empty.
- `git grep -nE 'public string\? Profile' src/FrigateRelay.Host/Configuration/SubscriptionOptions.cs` — exactly one match.
- `git grep -nE 'IReadOnlyDictionary<string, ProfileOptions>' src/FrigateRelay.Host/Configuration/HostSubscriptionsOptions.cs` — exactly one match.
- `dotnet run --project tests/FrigateRelay.Host.Tests -c Release` — all green.
