---
phase: frigate-mqtt-ingestion
plan: 1.2
wave: 1
dependencies: []
must_haves:
  - SubscriptionOptions record (Name, Camera, Label, Zone, CooldownSeconds) lives in src/FrigateRelay.Host/Configuration/
  - HostSubscriptionsOptions wrapper record with `IReadOnlyList<SubscriptionOptions> Subscriptions` to bind the top-level `Subscriptions` config section
  - SubscriptionMatcher.Match(EventContext, IReadOnlyList<SubscriptionOptions>) returning IReadOnlyList<SubscriptionOptions> of ALL matches (D1) — lives in src/FrigateRelay.Host/Matching/
  - DedupeCache.TryEnter(SubscriptionOptions, EventContext) backed by injected IMemoryCache, key = (SubscriptionName, Camera, Label), TTL = SubscriptionOptions.CooldownSeconds — lives in src/FrigateRelay.Host/Matching/
  - Stationary AND false_positive guard documented as the source's responsibility upstream of EventContext (D5 contract — applied by the FrigateMqtt plugin in PLAN-2.1)
  - Case-insensitive Camera + Label equality
  - Zone empty/null => match-any; non-empty => must equal one of EventContext.Zones (case-insensitive)
  - ">= 9 unit tests across matcher and dedupe (D1 multi-match, type=new always proceeds, zone-any, zone-no-match, zone-case-insensitive, camera-case-insensitive, dedupe hit, dedupe miss, dedupe TTL expiry)"
files_touched:
  - src/FrigateRelay.Host/Configuration/SubscriptionOptions.cs
  - src/FrigateRelay.Host/Configuration/HostSubscriptionsOptions.cs
  - src/FrigateRelay.Host/Matching/SubscriptionMatcher.cs
  - src/FrigateRelay.Host/Matching/DedupeCache.cs
  - src/FrigateRelay.Host/FrigateRelay.Host.csproj
  - tests/FrigateRelay.Host.Tests/SubscriptionMatcherTests.cs
  - tests/FrigateRelay.Host.Tests/DedupeCacheTests.cs
tdd: true
risk: low
---

# PLAN-1.2 — SubscriptionOptions, SubscriptionMatcher, DedupeCache (in Host)

## Context

Pure-logic siblings to PLAN-1.1; no I/O, no MQTT, no plugin code. **All three artifacts in this plan live in `FrigateRelay.Host`** — they describe host-level subscription semantics that any current or future `IEventSource` (Frigate MQTT today, HTTP webhook / Zigbee / etc. tomorrow) can be filtered through with no code change. This plan is parallel-safe with PLAN-1.1 (disjoint files, disjoint assemblies).

**Why Host-owned (rationale for the move from plugin to host).**
- `EventContext` exposes `Camera`, `Label`, and `Zones` as universal fields (Phase 1 contract). A matcher that reads only those fields is trivially reusable across sources — moving it into Host makes that reuse free instead of speculative.
- Subscription config is bound from the **top-level `Subscriptions`** section of `appsettings.json` (config-section question option (b)), not from `FrigateMqtt:Subscriptions`. This matches Phase 8's Profiles+Subscriptions composition shape.
- Plugin assemblies stay minimal: DTOs, wire parsing, MQTT client, registrar. Pure ingestion, no policy.
- No new types in `FrigateRelay.Abstractions` (hard constraint reaffirmed). `SubscriptionOptions`, `SubscriptionMatcher`, `DedupeCache`, and `HostSubscriptionsOptions` are all `public` (or `internal` where appropriate) in `FrigateRelay.Host`.

**OQ4 resolution (Zones aggregation).** Locked in: union all four zone arrays (`before.current_zones`, `before.entered_zones`, `after.current_zones`, `after.entered_zones`) into `EventContext.Zones` during projection (PLAN-2.1 owns the projection). The matcher consumes a single `EventContext.Zones` list and stays source-agnostic — it never sees `FrigateEvent`. This plan's matcher takes `EventContext` + `IReadOnlyList<SubscriptionOptions>` only.

**D1 + D5 enforcement.**
- **D1 (matcher):** Returns ALL matches (no break, no early exit). Implementation iterates every sub and collects into `List<SubscriptionOptions>`, returns `IReadOnlyList`.
- **D5 (guard):** Stationary + false_positive checks happen at the **source** — i.e. inside the FrigateMqtt plugin's projector, which strips skipped events before they ever land in the channel. The matcher in this plan only ever sees post-D5 events. This contract is documented in `SubscriptionMatcher`'s XML docs.

`DedupeCache` takes `IMemoryCache` via constructor (DI-friendly; PLAN-3.1 wires the registration in Host). Tests pass `new MemoryCache(new MemoryCacheOptions())` per CONTEXT-3 §D4. The Host csproj gains a `Microsoft.Extensions.Caching.Memory` package reference in this plan.

## Dependencies

- None within Phase 3 plan ordering. Reads `EventContext` from `FrigateRelay.Abstractions`. PLAN-1.1 is disjoint and parallel-safe.

## Tasks

<task id="1" files="src/FrigateRelay.Host/Configuration/SubscriptionOptions.cs, src/FrigateRelay.Host/Configuration/HostSubscriptionsOptions.cs, src/FrigateRelay.Host/FrigateRelay.Host.csproj, src/FrigateRelay.Host/Matching/SubscriptionMatcher.cs, tests/FrigateRelay.Host.Tests/SubscriptionMatcherTests.cs" tdd="true">
  <action>Add a `Microsoft.Extensions.Caching.Memory` PackageReference to `src/FrigateRelay.Host/FrigateRelay.Host.csproj` (used in task 2; declaring it now keeps the diff small).

Create two public records in `src/FrigateRelay.Host/Configuration/`:
  - `SubscriptionOptions { string Name, string Camera, string Label, string? Zone = null, int CooldownSeconds = 60 }` — XML docs explain matching semantics (case-insensitive equality for Camera/Label; Zone empty/null=any, non-empty=must-appear-in `EventContext.Zones`).
  - `HostSubscriptionsOptions { IReadOnlyList<SubscriptionOptions> Subscriptions = [] }` — bound from the top-level `Subscriptions` config section by `Program.cs` in PLAN-3.1. XML doc states "Bound from the top-level `Subscriptions` array in appsettings.json; host-level (not plugin-specific) so any IEventSource is filtered through these rules."

TDD: write 6 tests first in `tests/FrigateRelay.Host.Tests/SubscriptionMatcherTests.cs` — (1) single sub matching camera+label returns 1 result; (2) two subs both matching the same event return BOTH (D1 multi-match); (3) sub with `Camera="Front_Door"` matches `EventContext.Camera=="front_door"` (case-insensitive); (4) sub with `Zone=null` matches event with empty `Zones`; (5) sub with `Zone="driveway"` matches event whose `Zones` contains `"Driveway"` (case-insensitive); (6) sub with `Zone="porch"` does NOT match event whose `Zones` is `["driveway"]`.

Then implement `public static class SubscriptionMatcher` in `src/FrigateRelay.Host/Matching/SubscriptionMatcher.cs` exposing `IReadOnlyList<SubscriptionOptions> Match(EventContext context, IReadOnlyList<SubscriptionOptions> subs)`. Use `string.Equals(..., StringComparison.OrdinalIgnoreCase)`. Iterate every sub; collect matches into a `List<SubscriptionOptions>`; return `IReadOnlyList`. XML doc the contract: caller must apply D5 stationary/false_positive guard upstream (i.e. at the source).</action>
  <verify>dotnet build tests/FrigateRelay.Host.Tests -c Release && dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build</verify>
  <done>All 6 matcher tests pass; matcher never short-circuits on first match; `IReadOnlyList` is the return type; `SubscriptionOptions` and `HostSubscriptionsOptions` exist under `src/FrigateRelay.Host/Configuration/`; Host csproj references `Microsoft.Extensions.Caching.Memory`.</done>
</task>

<task id="2" files="src/FrigateRelay.Host/Matching/DedupeCache.cs, tests/FrigateRelay.Host.Tests/DedupeCacheTests.cs" tdd="true">
  <action>TDD: write 3 tests first in `tests/FrigateRelay.Host.Tests/DedupeCacheTests.cs` against `new MemoryCache(new MemoryCacheOptions())` — (1) first call to `TryEnter(sub, ctx)` returns true; (2) immediate second call with same sub.Name + ctx.Camera + ctx.Label returns false; (3) after the cache entry expires (sleep-based with a 1-second cooldown sub) the next `TryEnter` returns true again.

Then implement `public sealed class DedupeCache` in `src/FrigateRelay.Host/Matching/DedupeCache.cs` — constructor `DedupeCache(IMemoryCache cache)`, method `bool TryEnter(SubscriptionOptions sub, EventContext ctx)`. Key = `$"{sub.Name}|{ctx.Camera}|{ctx.Label}"` (lowercased on insert). On miss, insert with `AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(sub.CooldownSeconds)` and return true; on hit return false. XML docs explain the per-(sub, camera, label) bucket model and that callers should pre-filter via `SubscriptionMatcher`.</action>
  <verify>dotnet run --project tests/FrigateRelay.Host.Tests -c Release</verify>
  <done>All 3 dedupe tests pass; `git grep -n "MemoryCache\.Default" src/` returns zero matches; key is composed of (Name, Camera, Label) only — no event id, no timestamp.</done>
</task>

## Verification

```bash
cd /mnt/f/git/FrigateRelay
dotnet build src/FrigateRelay.Host -c Release
dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build
git grep -n "MemoryCache\.Default\|ServicePointManager\|\.Result\|\.Wait()" src/FrigateRelay.Host/ || echo "OK"
git grep -n "SubscriptionOptions\|SubscriptionMatcher\|DedupeCache" src/FrigateRelay.Sources.FrigateMqtt/ || echo "OK: no host-level types leaked into the plugin"
```

Expected: build passes; >= 9 tests added by this plan pass; both grep guards print "OK".
