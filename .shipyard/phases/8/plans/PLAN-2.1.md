---
phase: phase-8-profiles
plan: 2.1
wave: 2
dependencies: [1.1, 1.2]
must_haves:
  - Profile expansion produces resolved subscription list before downstream validators run
  - Existing Phase 4-7 startup validators retrofit to collect-all (D7)
  - Single aggregated InvalidOperationException thrown at end of validation
  - >= 10 ProfileResolutionTests covering D1 mutex, undefined refs, plugin/validator/provider unknowns, multi-error aggregation
files_touched:
  - src/FrigateRelay.Host/StartupValidation.cs
  - src/FrigateRelay.Host/HostBootstrap.cs
  - src/FrigateRelay.Host/Configuration/ProfileResolver.cs
  - tests/FrigateRelay.Host.Tests/Configuration/ProfileResolutionTests.cs
  - tests/FrigateRelay.Host.Tests/Startup/StartupValidationTests.cs
tdd: true
risk: low
---

# Plan 2.1: Profile Expansion + Collect-All Validator Retrofit

## Context

Per **D1**, every subscription must declare exactly one of `Profile:` or `Actions:` (XOR — neither is also an error). Per **D7**, the existing `StartupValidation.ValidateActions` / `ValidateSnapshotProviders` / `ValidateValidators` passes (all currently fail-on-first per RESEARCH.md §2) are retrofit so each pass writes into a shared `List<string> errors` accumulator and a single aggregated `InvalidOperationException` is thrown at the very end. Operators with three misconfigurations see all three at once.

Profile expansion runs **before** the existing validators (RESEARCH.md §2 "key finding"): the resolver produces a fully-resolved `IReadOnlyList<SubscriptionOptions>` where each subscription's `Actions` is the final effective list (profile expanded). Downstream code (`EventPump`, `IActionDispatcher`, `SnapshotResolver`) sees only the resolved list and stays unchanged (RESEARCH.md §3, §4). Per **D4** the 3-tier snapshot precedence is preserved: profile actions can carry per-action `SnapshotProvider:` overrides; the subscription's `DefaultSnapshotProvider:` (Tier 2) and the global default (Tier 3) are unchanged; profiles themselves carry no provider field (D5).

This plan retrofits all existing validators in one pass (D7 explicitly requires consistency across the validation surface — cherry-picked retrofit was rejected) and lands `ProfileResolutionTests` (≥10 tests, ROADMAP Phase 8 success criterion).

## Dependencies

- **PLAN-1.1** — needs `ProfileOptions`, `SubscriptionOptions.Profile`, `HostSubscriptionsOptions.Profiles`, internalized types.
- **PLAN-1.2** — needs `[TypeConverter]` so profile actions specified in JSON-array form bind correctly during the parity test fixture loads (and in any test using string-form `Actions`).

## Tasks

### Task 1: Profile resolver + collect-all retrofit (production code)
**Files:**
- `src/FrigateRelay.Host/Configuration/ProfileResolver.cs` (new)
- `src/FrigateRelay.Host/StartupValidation.cs` (modify — collect-all retrofit)
- `src/FrigateRelay.Host/HostBootstrap.cs` (modify — wire resolver before existing validators)

**Action:** create + modify
**TDD:** true (test classes from Tasks 2-3 are written ahead; production code lands here to turn them green)

**Description:**
Create `internal static class ProfileResolver` exposing one method:

```csharp
internal static IReadOnlyList<SubscriptionOptions> Resolve(
    HostSubscriptionsOptions options,
    List<string> errors);
```

Behavior (per D1, D5, D7):
- For each subscription in `options.Subscriptions`:
  - If `Profile is not null` AND `Actions.Count > 0` → append `"Subscription '{name}' may declare either 'Profile' or 'Actions', not both."` to `errors`; skip emitting a resolved entry.
  - If `Profile is null` AND `Actions.Count == 0` → append `"Subscription '{name}' must declare either 'Profile' or 'Actions'."`; skip.
  - If `Profile is not null` AND `Actions.Count == 0`:
    - If `options.Profiles` does not contain the key → append `"Subscription '{name}' references undefined profile '{profile}'. Defined profiles: [a, b, c]."`; skip.
    - Else → emit a `SubscriptionOptions` clone with `Actions = options.Profiles[name].Actions` and `Profile = null` (the resolved record makes the source ambiguity disappear).
  - Else (inline `Actions`) → emit the subscription unchanged.
- Returns the resolved list. **Never throws** — error reporting goes through the accumulator.

Modify `StartupValidation.cs`:
- Change all three `Validate*` methods from `void` returning + `throw` on first error to accept a `List<string> errors` parameter and `errors.Add(...)` instead of `throw`. Each pass iterates the **resolved** subscription list, not the raw `options.Subscriptions`.
- Add a new top-level entry point `internal static void ValidateAll(IServiceProvider services, HostSubscriptionsOptions options)` that:
  1. Allocates `var errors = new List<string>();`
  2. Calls `ProfileResolver.Resolve(options, errors)` and captures `resolved`.
  3. Calls `ValidateActions(services, resolved, errors)`, `ValidateSnapshotProviders(services, resolved, errors)`, `ValidateValidators(services, resolved, errors)`.
  4. If `errors.Count > 0`, throws `new InvalidOperationException("Startup configuration invalid:\n  - " + string.Join("\n  - ", errors));`.

Modify `HostBootstrap.ValidateStartup`: replace the three sequential `StartupValidation.Validate*` calls with a single `StartupValidation.ValidateAll(services, options)` call. Pass the bound `HostSubscriptionsOptions` from `IOptions<HostSubscriptionsOptions>.Value`.

Document the new collect-all contract with a brief XML doc comment on `ValidateAll`. The error message format follows the Phase 7 convention — sentence-ending period, named entities, registered-names list (RESEARCH.md §2).

**Acceptance Criteria:**
- `src/FrigateRelay.Host/Configuration/ProfileResolver.cs` exists with the signature above; declared `internal static class`.
- `git grep -nE 'throw new InvalidOperationException' src/FrigateRelay.Host/StartupValidation.cs` returns exactly one match (the aggregated throw at the end of `ValidateAll`).
- `dotnet build FrigateRelay.sln -c Release` exits 0 with zero warnings.
- `dotnet run --project tests/FrigateRelay.Host.Tests -c Release` — full suite passes (Tasks 2 and 3 tests now green, existing `StartupValidationTests` updated to assert substring presence in the aggregated message — see Task 3).

### Task 2: `ProfileResolutionTests` — ≥10 tests
**Files:**
- `tests/FrigateRelay.Host.Tests/Configuration/ProfileResolutionTests.cs` (new)

**Action:** create
**TDD:** true (written before Task 1 production code)

**Description:**
Create `ProfileResolutionTests` under `tests/FrigateRelay.Host.Tests/Configuration/`. Use `CapturingLogger<T>` from `FrigateRelay.TestHelpers` if logging assertions are needed; otherwise plain MSTest + FluentAssertions 6.12.2. Tests build a `HostSubscriptionsOptions` either by direct object init or by binding an in-memory JSON config (via `ConfigurationBuilder().AddJsonStream(...)`), call `ProfileResolver.Resolve(options, errors)`, and inspect both the returned list and the `errors` accumulator. Tests covering plugin/validator/provider name validation invoke the full `StartupValidation.ValidateAll` against a stub `IServiceProvider` (use `ServiceCollection().BuildServiceProvider()` with the relevant keyed singletons or empty for "unknown" cases) and assert the resulting `InvalidOperationException.Message` contains the expected substrings.

Required tests (≥10 — all using underscore naming per CLAUDE.md):

1. `Resolve_ProfileOnlySubscription_UsesProfileActions` — sub with `Profile: "Standard"` and no `Actions` → resolved entry has `Actions == Profiles["Standard"].Actions`.
2. `Resolve_InlineOnlySubscription_UsesInlineActions` — sub with `Actions: [BlueIris]` and no `Profile` → resolved entry preserves the inline actions.
3. `Resolve_MixedSubscriptions_ResolveIndependently` — three subs (one profile-only, one inline-only, one profile-only with a different profile) all resolve correctly in one pass.
4. `Resolve_BothProfileAndActionsSet_ReportsMutexError` — sub with both fields → `errors` contains `"Subscription '<name>' may declare either 'Profile' or 'Actions', not both."`.
5. `Resolve_NeitherProfileNorActions_ReportsMissingError` — sub with neither → `errors` contains `"Subscription '<name>' must declare either 'Profile' or 'Actions'."`.
6. `Resolve_UndefinedProfileReference_ReportsUndefinedError` — sub references `Profile: "Ghost"` not in `Profiles` → `errors` contains `"references undefined profile 'Ghost'"`.
7. `ValidateAll_ProfileActionUnknownPlugin_ReportsPluginError` — profile with `{Plugin: "Bogus"}`, no `IActionPlugin` keyed `"Bogus"` registered → aggregated exception message contains `"unknown action plugin 'Bogus'"`.
8. `ValidateAll_ProfileActionUnknownValidator_ReportsValidatorError` — profile with `Validators: ["Bogus"]` but no keyed `IValidationPlugin` registered → exception message contains the validator name and registered-names list (Phase 7 message shape).
9. `ValidateAll_ProfileActionUnknownSnapshotProvider_ReportsProviderError` — profile with `SnapshotProvider: "Bogus"` and no matching `ISnapshotProvider` → exception message contains the provider name.
10. `ValidateAll_MultipleErrors_AggregatesAllInOneException` — config with one missing-profile error, one unknown-plugin error, and one unknown-validator error → exactly one `InvalidOperationException` is thrown; its `Message` starts with `"Startup configuration invalid:"` and contains all three error fragments (use `.Should().Contain(...)` thrice).

**Acceptance Criteria:**
- File exists; `dotnet run --project tests/FrigateRelay.Host.Tests -c Release -- --filter-query "/*/*/ProfileResolutionTests/*"` discovers ≥10 tests.
- Initial run (before Task 1 production code lands): all 10 fail with compile or assertion errors confirming the red phase.
- Final run (after Task 1): all 10 pass.

### Task 3: Update existing `StartupValidationTests` to match collect-all message shape
**Files:**
- `tests/FrigateRelay.Host.Tests/Startup/StartupValidationTests.cs`

**Action:** modify
**TDD:** true (existing tests must be migrated to the new assertion shape — D7 explicitly notes this)

**Description:**
Existing Phase 7 tests assert on the exact single-error exception message (e.g. `Assert.AreEqual("Validator 'X' is not registered.", ex.Message)`). Update each affected test to assert the aggregated multi-line message **contains** the original error fragment as a substring — e.g. `ex.Message.Should().Contain("Validator 'X' is not registered.")`. The test names and structure are preserved; only the assertion mode changes.

D7 spec: "Update existing tests that asserted on the single-error message to assert on substring presence within the aggregated message (or update to match the new multi-line shape)." Substring containment is the cleaner option — the aggregate prefix `"Startup configuration invalid:\n  - "` is invariant.

For any test that exercises a code path no longer reachable (e.g. a "first error short-circuits the second pass"-style test), rewrite it to assert that two seeded errors both appear in the aggregated message — proving collect-all works rather than mocking the old fail-fast behavior.

**Acceptance Criteria:**
- `dotnet run --project tests/FrigateRelay.Host.Tests -c Release -- --filter-query "/*/*/StartupValidationTests/*"` — all existing tests pass against the new aggregated-message shape.
- `git grep -nE 'AreEqual\(".*is not registered\."' tests/FrigateRelay.Host.Tests/Startup/StartupValidationTests.cs` returns zero matches (no exact-equality assertions on validator messages remain).
- Full suite green: `dotnet run --project tests/FrigateRelay.Host.Tests -c Release`.

## Verification

- `dotnet build FrigateRelay.sln -c Release` — zero warnings.
- `dotnet run --project tests/FrigateRelay.Host.Tests -c Release -- --filter-query "/*/*/ProfileResolutionTests/*"` — 10+ pass, 0 fail.
- `dotnet run --project tests/FrigateRelay.Host.Tests -c Release -- --filter-query "/*/*/StartupValidationTests/*"` — all pass against new aggregated-message shape.
- `dotnet run --project tests/FrigateRelay.Host.Tests -c Release` — full suite passes.
- `git grep -nE 'throw new InvalidOperationException' src/FrigateRelay.Host/StartupValidation.cs` — exactly one match (the aggregated throw).
- `git grep -nE 'throw new InvalidOperationException' src/FrigateRelay.Host/Configuration/ProfileResolver.cs` — empty (resolver never throws).
