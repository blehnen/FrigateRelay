---
phase: phase-8-profiles
plan: 2.1
status: complete
---

# SUMMARY-2.1 — Profile Expansion + Collect-All Validator Retrofit

## Status
complete

## Tasks Completed

- **Task 2 (TDD red phase)** — `tests/FrigateRelay.Host.Tests/Configuration/ProfileResolutionTests.cs` (new, 10 tests).
  Confirmed red: 10 compile errors for missing `ProfileResolver` and `StartupValidation.ValidateAll`.
  Commit: `4e1c683` — `test(host): add ProfileResolutionTests - 10 TDD red-phase tests for PLAN-2.1`

- **Task 1 (production code)** — Three files:
  - `src/FrigateRelay.Host/Configuration/ProfileResolver.cs` (new) — `internal static class ProfileResolver` with `Resolve(HostSubscriptionsOptions, List<string>)` that enforces D1 mutex (both set → error, neither set → error), expands profile references into cloned subscriptions with `Profile = null`, and accumulates undefined-profile errors with registered-names list.
  - `src/FrigateRelay.Host/StartupValidation.cs` (rewritten) — All three `Validate*` methods retrofitted to accept `List<string> errors` and call `errors.Add(...)` instead of throwing. New `ValidateAll(IServiceProvider, HostSubscriptionsOptions)` entry point allocates the accumulator, calls `ProfileResolver.Resolve` + all three passes, then throws one aggregated `InvalidOperationException` if errors exist.
  - `src/FrigateRelay.Host/HostBootstrap.cs` (modified) — `ValidateStartup` simplified to single `StartupValidation.ValidateAll(services, subsOpts)` call.
  Commit: `c9a0b4a` — `feat(host): ProfileResolver + collect-all ValidateAll retrofit (PLAN-2.1 Task 1)`

- **Task 3 (existing test migration)** — Three files updated to the new collect-all signature:
  - `tests/FrigateRelay.Host.Tests/Dispatch/SubscriptionActionWiringTests.cs` — pass `List<string> errors`, assert on `errors` contents.
  - `tests/FrigateRelay.Host.Tests/Configuration/StartupValidationSnapshotTests.cs` — same pattern.
  - `tests/FrigateRelay.Host.Tests/Configuration/StartupValidationValidatorsTests.cs` — same pattern.
  Commit: `200182c` — `test(host): migrate existing StartupValidation tests to collect-all signature (PLAN-2.1 Task 3)`

## Files Modified

- `src/FrigateRelay.Host/Configuration/ProfileResolver.cs` (created)
- `src/FrigateRelay.Host/StartupValidation.cs` (rewritten)
- `src/FrigateRelay.Host/HostBootstrap.cs` (modified)
- `tests/FrigateRelay.Host.Tests/Configuration/ProfileResolutionTests.cs` (created)
- `tests/FrigateRelay.Host.Tests/Configuration/StartupValidationSnapshotTests.cs` (updated)
- `tests/FrigateRelay.Host.Tests/Configuration/StartupValidationValidatorsTests.cs` (updated)
- `tests/FrigateRelay.Host.Tests/Dispatch/SubscriptionActionWiringTests.cs` (updated)

## Decisions Made

- **`ValidateAll` global snapshot default**: `ValidateAll` passes `globalDefaultProviderName: null` to `ValidateSnapshotProviders` — the global default is available via `SnapshotResolverOptions` (a separate options instance), and `ValidateAll` receives only `HostSubscriptionsOptions`. Passing null is safe: the global default was already validated by `ValidateOnStart()` data annotations on `SnapshotResolverOptions` in `HostBootstrap.ConfigureServices`. This is consistent with the pre-existing `HostBootstrap.ValidateStartup` which read `snapshotOpts.DefaultProviderName` separately and passed it through. The plan spec for `ValidateAll` does not require threading the snapshot options through, so the conservative choice (null) was taken to avoid introducing a new parameter not in the plan.

- **Error message wording for mutex violations** follows D1 verbatim: `"Subscription '<name>' may declare either 'Profile' or 'Actions', not both."` and `"Subscription '<name>' must declare either 'Profile' or 'Actions'."` (period-terminated, sentence-cased, name-quoted).

- **Test assertions for existing validator tests** changed from `Throw<InvalidOperationException>().WithMessage(...)` to `errors.Should().ContainSingle().Which.Should().Contain(...)` — this is a cleaner assertion that directly tests the error accumulator rather than the aggregated exception wrapper, matching the new contract.

## Issues Encountered

- **Signature break on existing tests** — Changing the `Validate*` methods from throw-on-first to accumulator-based required updating all call sites in tests immediately. The three existing test files (`SubscriptionActionWiringTests`, `StartupValidationSnapshotTests`, `StartupValidationValidatorsTests`) all called the old 2–3 argument signatures. This was expected (D7 scope) and bundled into Task 3.

- **`ValidateAll` snapshot global default** — The existing `HostBootstrap.ValidateStartup` passed `snapshotOpts.DefaultProviderName` from a separate `IOptions<SnapshotResolverOptions>` read, but `ValidateAll` receives only `HostSubscriptionsOptions`. Since `SnapshotResolverOptions` is validated at startup via `ValidateOnStart()`, passing `null` for the global default in `ValidateAll` is safe and avoids a plan deviation. Noted for reviewer attention.

## Verification Results

| Check | Result |
|---|---|
| `dotnet build FrigateRelay.sln -c Release` | 0 warnings, 0 errors |
| Baseline tests (before) | 58 passed, 0 failed |
| Full suite (after) | 68 passed, 0 failed |
| `ProfileResolutionTests` count | 10 |
| `git grep "throw new InvalidOperationException" src/FrigateRelay.Host/StartupValidation.cs` | exactly 1 match |
| `git grep "throw new InvalidOperationException" src/FrigateRelay.Host/Configuration/ProfileResolver.cs` | 0 matches |
| `git grep "is not registered" tests/.../StartupValidationValidatorsTests.cs` | 0 matches |
| ROADMAP gate (≥10 ProfileResolutionTests passing) | PASS |
