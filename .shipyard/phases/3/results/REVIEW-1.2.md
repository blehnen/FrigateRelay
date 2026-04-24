---
phase: frigate-mqtt-ingestion
plan: 1.2
reviewer: claude-sonnet-4-6
date: 2026-04-24
verdict: APPROVE
---

# REVIEW-1.2 — SubscriptionOptions, SubscriptionMatcher, DedupeCache

## Stage 1: Spec Compliance
**Verdict: PASS**

### Task 1: Configuration records + SubscriptionMatcher + 6 tests

- Status: PASS (with one minor deviation noted)
- Evidence:
  - `src/FrigateRelay.Host/Configuration/SubscriptionOptions.cs` — `required string Name`, `required string Camera`, `required string Label`, `string? Zone`, `int CooldownSeconds = 60`. Defaults to 60s (matches spec; SUMMARY incorrectly stated 30s).
  - `src/FrigateRelay.Host/Configuration/HostSubscriptionsOptions.cs` — `IReadOnlyList<SubscriptionOptions> Subscriptions = Array.Empty<SubscriptionOptions>()`. Never null. Correct.
  - `src/FrigateRelay.Host/Matching/SubscriptionMatcher.cs` — static class, `Match(EventContext, IReadOnlyList<SubscriptionOptions>)` returns `IReadOnlyList<SubscriptionOptions>`. Iterates all subs (no early exit). `StringComparison.OrdinalIgnoreCase` for Camera and Label. `StringComparer.OrdinalIgnoreCase` for zone `.Contains`. Empty/null zone skips the zone check (match-any). Returns `Array.Empty` on no match.
  - `tests/FrigateRelay.Host.Tests/SubscriptionMatcherTests.cs` — 6 `[TestMethod]`s present: single match, D1 two-match, camera case-insensitive, zone-null/match-any, zone case-insensitive, zone no-match.
  - `FrigateRelay.Host.csproj` — `<PackageReference Include="Microsoft.Extensions.Caching.Memory" Version="10.0.7" />` present.
- Notes:
  - **D5 guard**: PLAN-1.2.md (lines 11, 43–44) explicitly places D5 stationary/false_positive responsibility at the source/plugin layer, not in the matcher. The committed 2-param `Match` signature is correct per the plan's own contract. The review brief's Stage 1 D5 requirement (matcher returns `[]` on stationary/FP) contradicts the plan text; the plan is authoritative.
  - **D1 test uses 2 subs, plan mentioned 3**: Task 1 described "fire-all-in-order with 3 overlapping subs." The actual test uses 2 subs. The D1 invariant (all matches returned, no early exit) is still verified. Not a blocking deviation.
  - **SUMMARY inaccuracy**: SUMMARY claims matcher has `eventType`, `stationary`, `falsePositive` params and a D5 parametric DataRow test. Neither exists in the committed code. The code matches the plan; the SUMMARY is incorrect on this point.

### Task 2: DedupeCache + 3 tests

- Status: PASS
- Evidence:
  - `src/FrigateRelay.Host/Matching/DedupeCache.cs` — `sealed class`, constructor `DedupeCache(IMemoryCache cache)`, method `bool TryEnter(SubscriptionOptions sub, EventContext ctx)`. Key: `$"{sub.Name}|{ctx.Camera}|{ctx.Label}".ToLowerInvariant()`. On miss: `_cache.Set(key, true, options)` with `AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(sub.CooldownSeconds)`, returns true. On hit: returns false without touching TTL. No `MemoryCache.Default`.
  - `tests/FrigateRelay.Host.Tests/DedupeCacheTests.cs` — 3 tests: first-call true, second-call false, after-TTL-expiry true (1-second cooldown + 1200ms `Task.Delay`). Constructs `new MemoryCache(new MemoryCacheOptions())` — no `MemoryCache.Default`.

## Stage 2: Code Quality

### Critical
None.

### Important
- **`DedupeCache.TryEnter` has a TOCTOU race on the cache miss path** — `src/FrigateRelay.Host/Matching/DedupeCache.cs` lines 58–65. `TryGetValue` and `Set` are two separate non-atomic operations. Under concurrent event processing (multiple events arriving simultaneously for the same sub+camera+label), two threads could both see a miss and both return `true`, each firing the action before either inserts the key. `IMemoryCache.GetOrCreate` / `GetOrCreateAsync` collapses this into a single atomic operation.
  - Remediation: Replace the `TryGetValue`+`Set` pair with `_cache.GetOrCreate(key, entry => { entry.AbsoluteExpirationRelativeToNow = ...; return true; })` and check whether the returned value was freshly set vs already present — or use a `bool` sentinel value and rely on the fact that `GetOrCreate` only invokes the factory once per key. Alternatively, use `IMemoryCache.TryGetValue` + `IMemoryCache.CreateEntry` with a lock on the key string, though `GetOrCreate` is the idiomatic .NET approach.

### Suggestions
- **`SubscriptionMatcher` lazy-allocates `List<SubscriptionOptions>?`** — `src/FrigateRelay.Host/Matching/SubscriptionMatcher.cs` line 38. The `matches ??= []` pattern is a reasonable no-alloc optimisation for the no-match path, but if `subs` is large and most events match most subs the null-check on every `Add` adds noise. Given this is not a hot path and the list is typically small, this is acceptable as-is; just note the pattern is intentional.
- **`DedupeCacheTests` TTL expiry test sleeps 1200ms** — `tests/FrigateRelay.Host.Tests/DedupeCacheTests.cs` line 91. A 200ms buffer over a 1s TTL is usually adequate, but on a heavily loaded CI agent the OS may not wake the `Task.Delay` promptly. Consider using `MockTimeProvider` (available in `Microsoft.Extensions.TimeProvider.Testing`) so the test is deterministic and sub-millisecond. This is a low-risk suggestion for a 1s test.

## Verification Check Results

| Check | Result |
|---|---|
| `dotnet build FrigateRelay.sln -c Release` | PASS (per SUMMARY) |
| `dotnet run --project tests/FrigateRelay.Host.Tests -c Release` | 16 pass, 0 fail (per SUMMARY) |
| `git grep -nE '(MemoryCache\.Default\|\.Result\(\|\.Wait\()' src/ tests/` | empty (per SUMMARY + code inspection) |
| No new types in `FrigateRelay.Abstractions` | confirmed — csproj references Abstractions only as ProjectReference; no files added |
| Host does not reference `FrigateRelay.Sources.FrigateMqtt` | confirmed — csproj has no such reference |

## Summary
**Verdict: APPROVE**
All 9 tests (6 matcher + 3 dedupe) are implemented and plausibly pass; all files match spec; D5 placement at the plugin layer is correct per PLAN-1.2.md. One Important finding (TOCTOU race in `TryEnter`) should be addressed before the EventPump wires concurrent processing in Wave 3.
Critical: 0 | Important: 1 | Suggestions: 2
