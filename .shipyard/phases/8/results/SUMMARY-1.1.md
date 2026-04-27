# Build Summary: Plan 1.1

## Status: complete

## Tasks Completed

- **Task 1: Flip 7 host types to `internal sealed` in one atomic pass** — `SubscriptionOptions.cs`, `HostSubscriptionsOptions.cs`, `SnapshotResolverOptions.cs`, `IActionDispatcher.cs`, `DispatcherOptions.cs`, `DedupeCache.cs`, `SubscriptionMatcher.cs`, `FrigateRelay.Host.csproj`
- **Task 2: Internalize `ActionEntryJsonConverter` + create `ProfileOptions` record** — `ActionEntryJsonConverter.cs` (modified), `ProfileOptions.cs` (new)
- **Task 3: Add `Profile` and `Profiles` properties to subscription/host options** — `SubscriptionOptions.cs`, `HostSubscriptionsOptions.cs`

## Files Modified

- `src/FrigateRelay.Host/Configuration/SubscriptionOptions.cs` — `public sealed record` to `internal sealed record`; added nullable `Profile` property with D1 mutex XML doc
- `src/FrigateRelay.Host/Configuration/HostSubscriptionsOptions.cs` — `public sealed record` to `internal sealed record`; added `Profiles` flat dictionary property with D5 XML doc
- `src/FrigateRelay.Host/Configuration/ActionEntryJsonConverter.cs` — `public sealed class` to `internal sealed class`
- `src/FrigateRelay.Host/Configuration/ProfileOptions.cs` — new file; `internal sealed record` with single `Actions` property
- `src/FrigateRelay.Host/Snapshots/SnapshotResolverOptions.cs` — `public sealed record` to `internal sealed record`
- `src/FrigateRelay.Host/Dispatch/IActionDispatcher.cs` — `public interface` to `internal interface`
- `src/FrigateRelay.Host/Dispatch/DispatcherOptions.cs` — `public sealed record` to `internal sealed record`
- `src/FrigateRelay.Host/Matching/DedupeCache.cs` — `public sealed class` to `internal sealed class`
- `src/FrigateRelay.Host/Matching/SubscriptionMatcher.cs` — `public static class` to `internal static class`
- `src/FrigateRelay.Host/FrigateRelay.Host.csproj` — added `<InternalsVisibleTo Include="DynamicProxyGenAssembly2" />`

## Decisions Made

- **`DynamicProxyGenAssembly2` `InternalsVisibleTo` required (unlisted in plan).** Flipping `IActionDispatcher` to `internal` caused NS2003 build errors in `EventPumpDispatchTests.cs`. NSubstitute's Castle DynamicProxy runtime also needs access to internal types — not just the test assembly. Added `<InternalsVisibleTo Include="DynamicProxyGenAssembly2" />` in MSBuild item form per CLAUDE.md convention. Included in the Task 1 commit.
- **`SubscriptionMatcher` is `static`, not `sealed`.** Flip was `public static class` to `internal static class`. `static` classes are implicitly sealed so no `sealed` keyword was added. Plan's shorthand "internal sealed" language did not apply literally here.
- **`InternalsVisibleTo` for `FrigateRelay.IntegrationTests` already present** — left unchanged.

## Issues Encountered

- **NSubstitute NS2003 blocked the build after Task 1.** RESEARCH.md §10 and D8 noted "verify `InternalsVisibleTo` is present" but did not call out the `DynamicProxyGenAssembly2` requirement. Any future phase that internalizes a type currently mocked via NSubstitute should preemptively add this entry. Recommend adding to CLAUDE.md conventions alongside the existing `InternalsVisibleTo` guidance.
- **CS0053 cascade was a non-issue in practice.** Keeping `ActionEntry` public while making `SubscriptionOptions` internal (which holds `IReadOnlyList<ActionEntry>`) did not produce CS0053. The cascade concern applied to the scenario where both are flipped simultaneously; deferring `ActionEntry` to PLAN-1.2 is safe.
- **CRLF/LF warnings on all commits** — cosmetic, WSL2 + Windows repo with `core.autocrlf`. No action taken.

## Verification Results

**Baseline (pre-implementation):**
```
dotnet build FrigateRelay.sln -c Release  →  Build succeeded. 0 Warning(s) 0 Error(s)
dotnet run --project tests/FrigateRelay.Host.Tests -c Release  →  55 passed, 0 failed
```

**Post-all-tasks final sweep:**
```
git grep -nE 'public (sealed )?(class|record|interface) (SubscriptionOptions|HostSubscriptionsOptions|SnapshotResolverOptions|IActionDispatcher|DispatcherOptions|DedupeCache|SubscriptionMatcher|ActionEntryJsonConverter)\b' src/
  → exit 1, zero matches — PASS

git grep -nE 'public string\? Profile' src/FrigateRelay.Host/Configuration/SubscriptionOptions.cs
  → 1 match — PASS

git grep -nE 'public IReadOnlyDictionary<string, ProfileOptions> Profiles' src/FrigateRelay.Host/Configuration/HostSubscriptionsOptions.cs
  → 1 match — PASS

dotnet build FrigateRelay.sln -c Release  →  Build succeeded. 0 Warning(s) 0 Error(s)
dotnet run --project tests/FrigateRelay.Host.Tests -c Release  →  55 passed, 0 failed, 6s 535ms
```

**Commits:**
- `b5b87eb` — shipyard(phase-8): flip 7 host types to internal sealed + add DynamicProxyGenAssembly2 IVT
- `e622a39` — shipyard(phase-8): internalize ActionEntryJsonConverter + add ProfileOptions record
- `d2bc12a` — shipyard(phase-8): add Profile + Profiles properties to subscription/host options
