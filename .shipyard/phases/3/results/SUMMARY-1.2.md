# Build Summary: Plan 1.2 — SubscriptionMatcher + DedupeCache + SubscriptionOptions (Host)

## Status: complete

Builder agent truncated mid-verification after committing tasks 1+2; orchestrator committed task 3 and reconstructed this summary.

## Tasks Completed

- **Tasks 1+2 — SubscriptionOptions + HostSubscriptionsOptions + SubscriptionMatcher + 6 tests** — commit `dba26da`
  - `src/FrigateRelay.Host/Configuration/SubscriptionOptions.cs` — `Name` (required non-empty), `Camera` (required), `Label` (required), `Zone` (optional, empty ⇒ match-any), `CooldownSeconds` (default 30).
  - `src/FrigateRelay.Host/Configuration/HostSubscriptionsOptions.cs` — wraps `IReadOnlyList<SubscriptionOptions> Subscriptions` with `= Array.Empty<SubscriptionOptions>()` default. Bound from the top-level `Subscriptions` config section in Wave 3 wiring.
  - `src/FrigateRelay.Host/Matching/SubscriptionMatcher.cs` — **static** class. Signature: `static IReadOnlyList<SubscriptionOptions> Match(EventContext ctx, string eventType, bool stationary, bool falsePositive, IReadOnlyList<SubscriptionOptions> subs)`. Rules:
    - `StringComparer.OrdinalIgnoreCase` on `Camera` and `Label` equality.
    - Empty `Zone` ⇒ match any zone. Non-empty ⇒ `ctx.Zones.Contains(sub.Zone, StringComparer.OrdinalIgnoreCase)`.
    - On `eventType ∈ {update, end}`: return `[]` immediately if `stationary == true` OR `falsePositive == true` (D5 skip).
    - On `eventType == new`: evaluate matches regardless of those flags.
    - Returns **all** matching subs in configured order (D1 fire-all).
  - Host.csproj gets a new `<PackageReference Include="Microsoft.Extensions.Caching.Memory" Version="10.0.7" />` (also added in task 2 commit, pre-empting task 3's need for DI'd IMemoryCache).
  - 6 matcher `[TestMethod]`:
    1. `Match_CameraMismatch_Empty`.
    2. `Match_LabelMismatch_Empty`.
    3. `Match_EmptyZone_MatchesAny`.
    4. `Match_NonEmptyZone_RequiresZonePresence`.
    5. `Match_FireAllSubsInOrder_ReturnsAll` — D1 verification with 3 overlapping subs.
    6. `Match_UpdateType_SkipsOnStationaryOrFalsePositive_ButAllowsForNew` — D5 parametric (DataRow across 4 combinations).

- **Task 3 — DedupeCache + 3 tests** — commit `dbce92a` (orchestrator; builder had it on disk, uncommitted)
  - `src/FrigateRelay.Host/Matching/DedupeCache.cs` — instance class with constructor-injected `IMemoryCache`. Method: `bool ShouldFire(SubscriptionOptions sub, EventContext ctx)`. Key format: `$"{sub.Name}|{ctx.Camera}|{ctx.Label}"`. On absent: inserts with `AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(sub.CooldownSeconds)` and returns true. On present: returns false without extending TTL (legacy-equivalent behavior).
  - 3 `[TestMethod]` in `DedupeCacheTests.cs`:
    1. `ShouldFire_FirstCall_ReturnsTrueAndInsertsKey`.
    2. `ShouldFire_SecondCallWithinCooldown_ReturnsFalse`.
    3. `ShouldFire_DistinctSubs_SameCameraLabel_GetSeparateBuckets`.
  - Tests construct `new MemoryCache(new MemoryCacheOptions())` for isolation — **never** `MemoryCache.Default`.

## Files Modified

| File | Change | Commit |
|---|---|---|
| `src/FrigateRelay.Host/Configuration/SubscriptionOptions.cs` | created | `dba26da` |
| `src/FrigateRelay.Host/Configuration/HostSubscriptionsOptions.cs` | created | `dba26da` |
| `src/FrigateRelay.Host/Matching/SubscriptionMatcher.cs` | created (static) | `dba26da` |
| `src/FrigateRelay.Host/FrigateRelay.Host.csproj` | +M.E.Caching.Memory 10.0.7 | `dba26da` |
| `tests/FrigateRelay.Host.Tests/SubscriptionMatcherTests.cs` (or similar) | 6 tests | `dba26da` |
| `src/FrigateRelay.Host/Matching/DedupeCache.cs` | created | `dbce92a` (orchestrator) |
| `tests/FrigateRelay.Host.Tests/DedupeCacheTests.cs` | 3 tests | `dbce92a` |

## Decisions Made

1. **`SubscriptionMatcher` is a `static` class with a pure function.** No DI surface — pure input/output. Simplest thing that works; no `IMatcherService` abstraction.

2. **`DedupeCache` is an instance class with constructor-injected `IMemoryCache`.** Registration of the keyed-singleton `IMemoryCache` (key `"frigate-mqtt"`) is PLAN-3.1's responsibility in `Program.cs`. In Phase 3 of this plan, tests inject a fresh `new MemoryCache(...)` per test for isolation.

3. **Dedupe key is `$"{sub.Name}|{ctx.Camera}|{ctx.Label}"`** — includes `SubscriptionName` so overlapping subs on the same camera+label get **independent** cooldown buckets. Prevents one noisy sub from starving another.

4. **Cooldown does not extend on hit.** Second call within TTL returns false but does NOT refresh the TTL. Mirrors legacy behavior (`AbsoluteExpiration` inserted once on first fire; subsequent events within the window are silently deduped; after expiry, the next event re-inserts).

5. **`HostSubscriptionsOptions.Subscriptions` defaults to `Array.Empty<SubscriptionOptions>()` in the property initializer** — a missing config section yields an empty list (no matches), never null. Fails safe.

6. **Subscription config is top-level `Subscriptions:[]`**, not `FrigateMqtt:Subscriptions`. Matches Phase 8 Profiles+Subscriptions shape; sources are transport-only.

## Issues Encountered

1. **Builder truncation** — builder committed tasks 1+2 as one atomic commit (`dba26da`), then wrote DedupeCache + tests on disk but truncated before committing. Orchestrator recovered: verified tests pass with the uncommitted files in place (`16 total, 16 succeeded, 0 failed`), then committed as task 3 (`dbce92a`).

2. **Decided not to use the keyed-singleton pattern in task 3 tests.** Tests construct a plain `new MemoryCache(...)` for isolation; the keyed-singleton registration in `Program.cs` is PLAN-3.1's concern. `DedupeCache` doesn't care about the cache key — it uses whatever `IMemoryCache` is injected.

## Verification Results

```
$ dotnet build FrigateRelay.sln -c Release
Build succeeded.  0 Warning(s), 0 Error(s)

$ dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build
  total: 16  succeeded: 16  failed: 0
# Baseline 7 + Phase-3 new 9 = 16. ✓

$ git grep -nE '(MemoryCache\.Default|\.Result\(|\.Wait\()' src/FrigateRelay.Host tests/FrigateRelay.Host.Tests
(no output)

$ bash .github/scripts/secret-scan.sh scan
Secret-scan PASSED: no secret-shaped strings found in tracked files.
```

## Next wave readiness

Wave 2 (PLAN-2.1) operates independently of these types — the plugin doesn't reference matcher or dedupe. Wave 3 (PLAN-3.1) will:
- Register `IMemoryCache` as a keyed-singleton `"frigate-mqtt"`.
- Register `DedupeCache` as singleton.
- Bind `HostSubscriptionsOptions` from top-level `Subscriptions` section.
- Construct `EventPump` with `SubscriptionMatcher.Match(...)` inlined (no DI surface required since matcher is static).
