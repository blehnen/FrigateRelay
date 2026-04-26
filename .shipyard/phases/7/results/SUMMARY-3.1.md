# Build Summary: Plan 7.3.1 — `EventPump` validator-resolution + `StartupValidation.ValidateValidators` + integration tests

## Status: complete

## Tasks Completed

- **Task 1** — `EventPump.DispatchAsync` resolves `ActionEntry.Validators` keys via `IServiceProvider.GetRequiredKeyedService<IValidationPlugin>(key)` at dispatch time, replacing the `Array.Empty<IValidationPlugin>()` placeholder. New `IServiceProvider _services` ctor param. 3 EventPump test ctor sites updated (2 in `EventPumpTests.cs` use a tiny inline `EmptyServiceProvider` stub since NSubstitute isn't imported there; 1 in `EventPumpDispatchTests.cs` uses `Substitute.For<IServiceProvider>()`). Commit `8f55f8a`.
- **Task 2** — `StartupValidation.ValidateValidators(IEnumerable<SubscriptionOptions>, IServiceProvider)` mirrors `ValidateActions` / `ValidateSnapshotProviders`. Uses `services.GetKeyedService<IValidationPlugin>(key)` — null result covers both undefined-key AND unknown-Type cases (since the per-plugin registrar's `Type` filter silently skips unknown values). Wired into `HostBootstrap.ValidateStartup` after the snapshot check (the **Phase 5 review-3.1 dead-code lesson** is loud-commented at the call site). 3 new tests in `StartupValidationValidatorsTests.cs` cover both error paths and the happy path. Commit `da120c1` (bundled with Task 3 production wiring — see below).
- **Task 3 (production wiring)** — `HostBootstrap.ConfigureServices` conditionally adds the `FrigateRelay.Plugins.CodeProjectAi.PluginRegistrar` when the top-level `Validators` config section is present (matches BlueIris/Pushover/FrigateSnapshot pattern). `FrigateRelay.Host.csproj` gains a `ProjectReference` to the new plugin. Bundled into commit `da120c1` because both edits are co-located in `HostBootstrap.cs` and inseparable on the file.
- **Task 3 (integration tests)** — `tests/FrigateRelay.IntegrationTests/MqttToValidatorTests.cs` with 2 end-to-end tests: `Validator_ShortCircuits_OnlyAttachedAction` (BlueIris fires, Pushover skipped, `validator_rejected` log emitted) and `Validator_Pass_BothActionsFire` (both fire, no rejection log). Each test spins up Mosquitto via `MosquittoFixture` + 4 WireMock servers (BlueIris, Pushover, FrigateSnapshot, CodeProject.AI) + a per-test `CapturingLoggerProvider` for log inspection. Commit `acc3de4`.

## Files Modified

- `src/FrigateRelay.Host/EventPump.cs` — IServiceProvider injection + validator-key resolution at dispatch.
- `src/FrigateRelay.Host/StartupValidation.cs` — added `ValidateValidators` method.
- `src/FrigateRelay.Host/HostBootstrap.cs` — `ValidateStartup` calls `ValidateValidators`; `ConfigureServices` conditionally registers CodeProjectAi.
- `src/FrigateRelay.Host/FrigateRelay.Host.csproj` — added `ProjectReference` to CodeProjectAi plugin.
- `tests/FrigateRelay.Host.Tests/EventPumpTests.cs` — 2 ctor sites updated; new `EmptyServiceProvider` stub.
- `tests/FrigateRelay.Host.Tests/EventPumpDispatchTests.cs` — 1 ctor site updated.
- `tests/FrigateRelay.Host.Tests/Configuration/StartupValidationValidatorsTests.cs` (new) — 3 tests.
- `tests/FrigateRelay.IntegrationTests/MqttToValidatorTests.cs` (new) — 2 tests + helper plumbing.

## Decisions Made

- **`ValidateValidators` signature `(IEnumerable<SubscriptionOptions>, IServiceProvider)`** — matches the **existing `ValidateActions` / `ValidateSnapshotProviders` shape** rather than the plan's suggested `(IServiceProvider, IOptions<HostSubscriptionsOptions>)`. Keeps the static-helper API consistent across the file and preserves test isolation (you don't need to spin up an `IOptions<>` wrapper to call it).
- **`EmptyServiceProvider` inline stub** in `EventPumpTests.cs` rather than importing NSubstitute. The file didn't already use NSubstitute and adding the import for a single throwaway mock would have triggered IDE0005 on warmer test runs. A 4-line nested class is local and self-explanatory.
- **`CapturingLoggerProvider` for integration log inspection.** Phase 6's tests didn't need log capture (asserted only on WireMock requests). Phase 7 must verify the structured `validator_rejected` log entry, so the integration test registers a per-test `ILoggerProvider` via `builder.Logging.AddProvider`. ConcurrentBag for thread-safety since the dispatcher runs on consumer tasks.
- **Snapshot tier via `Snapshots:DefaultProviderName = "Frigate"`** rather than per-action override. Confirms the dispatcher's pre-resolve-once path: ONE GET to the FrigateSnapshot WireMock server even though both validator and action call `snapshot.ResolveAsync`.
- **`AllowedLabels:0 = "person"` config syntax** for the integration test (string array via index keys, matching the pattern Phase 6 uses for `Subscriptions:0:Actions:0:Plugin`).

## Issues Encountered

- **`await using var app` failed on `IHost`** — `IHost` only implements `IDisposable` (not `IAsyncDisposable`). Phase 6's integration tests use plain `using var app = builder.Build()`. Switched to `using var`.
- **Unnecessary `using Microsoft.Extensions.DependencyInjection;` in production code** triggered IDE0005 — the `GetRequiredKeyedService<T>` extension is implicitly available via global usings or a transitive namespace; explicit using was redundant. Removed and confirmed by build clean.
- **`new[] { ... }` literal arrays inside test methods** can trigger CA1861 in tests too; fortunately the new tests already used inline collection-expressions `["..."]`.
- **`ValidateValidators` test file naming** — followed `StartupValidationSnapshotTests.cs` precedent (named after the validation method) rather than expanding a generic `StartupValidationTests.cs`. Keeps each file under ~100 lines.

## Verification Results

- `dotnet build FrigateRelay.sln -c Release` — **0 warnings, 0 errors**.
- All test projects via `.github/scripts/run-tests.sh`:
  - `FrigateRelay.Abstractions.Tests` — 25/25
  - `FrigateRelay.Host.Tests` — 55/55 (was 52, +3 ValidateValidators)
  - `FrigateRelay.IntegrationTests` — 4/4 (was 2, +2 MqttToValidatorTests)
  - `FrigateRelay.Plugins.BlueIris.Tests` — 17/17
  - `FrigateRelay.Plugins.CodeProjectAi.Tests` — 8/8 (NEW project)
  - `FrigateRelay.Plugins.FrigateSnapshot.Tests` — 6/6
  - `FrigateRelay.Plugins.Pushover.Tests` — 10/10
  - `FrigateRelay.Sources.FrigateMqtt.Tests` — 18/18
  - **Total: 143/143 across all suites** (Phase 6 baseline 124, Phase 7 added 19 = 13 unit + 3 startup-validation + 2 integration + 1 dispatcher-share-snapshot test).
- ROADMAP gate: ≥ 8 unit + 1 integration. Achieved: 16 unit (8 CodeProjectAi + 3 startup + 2 dispatcher + 2 SnapshotContext + 1 ActionEntryJsonConverter that's directly Phase-7-coupled out of 2) + 2 integration = **+80% cushion over gate**.
- Both integration tests confirm:
  - The dispatcher's pre-resolve-once path: **WireMock FrigateSnapshot received exactly 1 GET per dispatch** (validator and action share the snapshot).
  - The `validator_rejected` structured log emits `event_id`, `camera`, `label`, `action`, `validator`, `reason` per CONTEXT-7 D7.
  - Other actions in the same event fire independently when one is short-circuited (PROJECT.md V3).

## Lesson seeding (for `/shipyard:ship`)

- **Phase 5 review-3.1 dead-code regression mode is real and recurring.** Without an explicit "is this method called from the bootstrap chain?" check, it's easy to define a startup validator and forget to wire it. The PLAN-3.1 commit message + the inline comment in `HostBootstrap.ValidateStartup` both reference Phase 5 as the reason for the explicit reminder — operators reading the file later will see the rationale.
- **Per-test `ILoggerProvider` is the right log-capture tool for integration tests.** `CapturingLogger<T>` (the unit-test helper) is keyed per-T; integration tests need cross-category capture. The new `CapturingLoggerProvider` pattern (~30 LoC) is a candidate for promotion to `FrigateRelay.TestHelpers` if a third integration test needs it. **Rule of Two — flag for Phase 8 simplifier**, do not extract yet.
- **`IHost` is `IDisposable`, not `IAsyncDisposable`** — surprising, since most modern hosting infrastructure uses async dispose. `using var app` is the correct pattern; `await using` requires explicit `IAsyncDisposable` impl.
- **`Snapshots:DefaultProviderName` is the right tier for integration tests** — exercises the whole resolver chain without per-action override clutter.