# Simplification Report — Phase 8

**Phase:** Phase 8 — Profiles in Configuration
**Date:** 2026-04-27
**Files analyzed:** 11 source + test files across PLAN-1.1, PLAN-1.2, PLAN-2.1, PLAN-3.1
**Findings:** 2 high, 2 medium, 3 low

---

## High Priority

### 1. Dead `ValidationPlugin` helper in `ProfileResolutionTests`

- **Type:** Remove
- **Effort:** Trivial
- **Location:** `tests/FrigateRelay.Host.Tests/Configuration/ProfileResolutionTests.cs:44-49`
- **Description:** The private static `ValidationPlugin(string key)` method is defined but never called by any of the 10 tests in that file. Tests 8 and 10 exercise validator paths by passing validator key strings directly as arguments to `ActionEntry`, not via this helper. Already noted in REVIEW-2.1 and survived into the final commit unchanged.
- **Suggestion:** Delete lines 44-49 of `ProfileResolutionTests.cs`. The build and all tests will continue to pass; `CA1822` or a trimmer would eventually flag it anyway in a stricter config.
- **Impact:** 6 lines removed, eliminates misleading implication that the helper is part of the test contract.

---

### 2. `HostSubscriptionsOptions.Snapshots` property is orphaned — dual binding with no reader

- **Type:** Remove
- **Effort:** Trivial
- **Location:** `src/FrigateRelay.Host/Configuration/HostSubscriptionsOptions.cs:33-35`
- **Description:** `HostSubscriptionsOptions` declares `public SnapshotResolverOptions Snapshots { get; init; } = new();`. This causes the `Snapshots` config section to bind into *both* this embedded property *and* into the separately-registered `IOptions<SnapshotResolverOptions>` (wired at `HostBootstrap.cs:56-59` via `.Bind(builder.Configuration.GetSection("Snapshots"))`). No production code reads `HostSubscriptionsOptions.Snapshots` — `ValidateAll` reads `services.GetService<IOptions<SnapshotResolverOptions>>()?.Value` (`StartupValidation.cs:42`), and `SnapshotResolver` takes `IOptions<SnapshotResolverOptions>` from DI. The embedded property is silently populated but never consulted, creating a false impression that it is authoritative.
- **Suggestion:** Delete the `Snapshots` property from `HostSubscriptionsOptions`. `SnapshotResolverOptions` already has its own dedicated DI registration with `ValidateOnStart()` — that is the correct owner. If any future code needs `SnapshotResolverOptions` values, it should inject `IOptions<SnapshotResolverOptions>` directly, not navigate via `HostSubscriptionsOptions`.
- **Impact:** 5 lines removed, eliminates a latent confusion where two different bindings of the same config section appear to coexist, and removes a misleading unused property from the authoritative options type.

---

## Medium Priority

### 3. Three independent `ISnapshotProvider` stub factories — Rule of Three triggered

- **Type:** Consolidate
- **Effort:** Trivial
- **Locations:**
  - `tests/FrigateRelay.Host.Tests/Configuration/ProfileResolutionTests.cs:38-43` — `SnapshotProvider(string name)`
  - `tests/FrigateRelay.Host.Tests/Configuration/ConfigSizeParityTest.cs:97-101` — `SnapshotProvider(string name)`
  - `tests/FrigateRelay.Host.Tests/Configuration/StartupValidationSnapshotTests.cs:11-16` — `Provider(string name)` (identical body, different method name)
- **Description:** All three are identically structured: `Substitute.For<ISnapshotProvider>(); p.Name.Returns(name); return p;`. The Rule of Three applies — three independent implementations of the same 4-line factory. `ActionPlugin(string)` appears in two files (ProfileResolutionTests + ConfigSizeParityTest) and just misses the rule threshold, but can be co-located in the same move.
- **Suggestion:** Add a static `StubFactory` class (or extension methods) to `tests/FrigateRelay.TestHelpers/` with `MakeSnapshotProvider(string name)` and `MakeActionPlugin(string name)` methods. This aligns with the existing `CapturingLogger<T>` precedent for shared test infrastructure (ID-11, closed). Update the three call sites. `StubPlugin` in `SubscriptionActionWiringTests` is a concrete `IActionPlugin` implementation (not NSubstitute), so it stays local.
- **Impact:** ~15 lines consolidated, single location for NSubstitute stub construction conventions across Host tests.

---

### 4. `ValidateValidators` forced materialization is unnecessary

- **Type:** Refactor
- **Effort:** Trivial
- **Location:** `src/FrigateRelay.Host/StartupValidation.cs:141`
- **Description:** Line 141 reads `var subList = subscriptions as IList<SubscriptionOptions> ?? subscriptions.ToList();` to enable index-variable access for error messages. The only caller in production is `ValidateAll`, which passes the `IReadOnlyList<SubscriptionOptions>` returned directly by `ProfileResolver.Resolve` — that is already an `IList<>`, so the `.ToList()` branch never executes in production. In tests, `ValidateValidators` is called directly with arrays (which implement `IList<>`), so again `.ToList()` never fires. The materialisation guard exists for a theoretical `IEnumerable<>` caller that does not exist.
- **Suggestion:** Change the parameter type of `ValidateValidators` from `IEnumerable<SubscriptionOptions>` to `IReadOnlyList<SubscriptionOptions>` to match its actual callers. The index-based loop then indexes directly without the cast guard, and the defensive `.ToList()` is deleted. All three other `Validate*` methods already accept `IEnumerable<>` because they do not need indices — this one is the exception and should be typed accordingly.
- **Impact:** 1 line removed, parameter type more accurately reflects usage, no production-path `.ToList()` allocation.

---

## Low Priority

- **Convention drift on commit prefixes (PLAN-2.1 commits):** Commits `4e1c683` (`test(host): add ProfileResolutionTests`) and `c9a0b4a` (`feat(host): ProfileResolver + collect-all ValidateAll`) use `feat(host):`/`test(host):` instead of the project-standard `shipyard(phase-8):` prefix. Already noted in REVIEW-2.1. No functional impact.

- **`ConfigSizeParityTest` dual-concern test:** The single test method `Json_Is_At_Most_60_Percent_Of_Ini_Character_Count` performs a size gate and calls a private sub-assertion that does a full bind + validate. The two concerns could be separate `[TestMethod]`s with clearer failure messages. This is a stylistic choice; the current form is defensible — a structurally invalid file would produce a misleading "too large" failure only if the JSON is *also* oversized.

- **`ValidateAll` defensive `?.Value` on `IOptions<SnapshotResolverOptions>` (`StartupValidation.cs:42`):** `services.GetService<IOptions<SnapshotResolverOptions>>()?.Value` uses null-tolerant service resolution even though `HostBootstrap.ConfigureServices` always registers `SnapshotResolverOptions`. The `?.Value` guard exists because integration tests calling `ValidateAll` directly may not register the snapshot options. If finding #2 is resolved by removing the `Snapshots` property and `ValidateAll` is updated to require `IOptions<SnapshotResolverOptions>` via `GetRequiredService`, the production path becomes unambiguous. Worth addressing only after finding #2 is resolved.

---

## Already-Clean Observations

- `ProfileResolver.Resolve` is clean: correct early-continue pattern, no unnecessary allocations, `OrderBy` only on the error path. `IReadOnlyList<>` return type is appropriate.
- `ActionEntryTypeConverter` (PLAN-1.2) is minimal and correct: 3 tests, no over-engineering, coexists cleanly with `[JsonConverter]` on disjoint code paths.
- `HostBootstrap.ValidateStartup` correctly simplified to a single `ValidateAll` call.
- No orphaned `DefaultProfile` or `ProfileMode` fields in `HostSubscriptionsOptions`. Only properties are `Profiles`, `Subscriptions`, and the orphaned `Snapshots` (finding #2).
- `<None Update="Fixtures\legacy.conf">` in the test csproj is the correct MSBuild form for an in-tree file.
- The `DynamicProxyGenAssembly2` `InternalsVisibleTo` entry is correctly placed in MSBuild item form per CLAUDE.md conventions.

---

## Summary

- **Duplication found:** 3 instances of identical `ISnapshotProvider` stub factory across 3 files (Rule of Three met)
- **Dead code found:** 1 unused method (`ValidationPlugin` in `ProfileResolutionTests`)
- **Orphaned production property:** 1 (`HostSubscriptionsOptions.Snapshots` — bound but never read)
- **Complexity hotspots:** 0 functions exceeding thresholds
- **AI bloat patterns:** 1 (unnecessary defensive materialization in `ValidateValidators`)
- **Estimated cleanup impact:** ~25 lines removable, 1 orphaned property eliminated, 1 test helper consolidation opportunity

## Recommendation

**Two findings warrant prompt action before Phase 9 begins:**

1. The dead `ValidationPlugin` helper (High #1) is a trivial delete — should be removed in the first commit of Phase 9 or as a standalone cleanup commit.

2. The orphaned `HostSubscriptionsOptions.Snapshots` property (High #2) is a latent correctness trap. Any future code author navigating `HostSubscriptionsOptions` will find a `Snapshots` property and assume it is authoritative — it is not. Removing the property and updating `ValidateAll` to use `GetRequiredService<IOptions<SnapshotResolverOptions>>()` (eliminating the `?.Value` guard, Low finding) makes the ownership unambiguous before Phase 9 adds more snapshot-adjacent code.

The stub factory consolidation (Medium #3) and `ValidateValidators` parameter tightening (Medium #4) are clean-up items that can be batched into a single Phase 9 prep commit without risk.
