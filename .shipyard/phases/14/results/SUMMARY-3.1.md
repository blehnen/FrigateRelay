# Build Summary: Plan 3.1

## Status: complete

## Tasks Completed

- **Task 1: Extend `ActionEntry` record + update both converters + 6 binding tests** — complete — files: `src/FrigateRelay.Host/Configuration/ActionEntry.cs`, `ActionEntryJsonConverter.cs`, `ActionEntryTypeConverter.cs`, plus new `ParallelValidatorsBindingTests.cs`. Commit `3e50c83`.
- **Task 2: Extend `DispatchItem` + `IActionDispatcher` + `ChannelActionDispatcher`** — complete. Commit `9f64d69`.
- **Task 3: Update `EventPump` to propagate `ActionEntry.ParallelValidators` → `DispatchItem.ParallelValidators`** + fix 4 test stub class signatures across the test suite. Commit `9f64d69` (combined with Task 2 since both required the interface signature change).

## Files Modified

### Source

- `src/FrigateRelay.Host/Configuration/ActionEntry.cs` (modified) — added `bool ParallelValidators = false` as the 4th positional record parameter. Default `false` preserves backward compat (CONTEXT-14 D5).
- `src/FrigateRelay.Host/Configuration/ActionEntryJsonConverter.cs` (modified) — JsonConverter now reads/writes the new `ParallelValidators` field. Default `false` when missing.
- `src/FrigateRelay.Host/Configuration/ActionEntryTypeConverter.cs` (modified) — TypeConverter's string-to-record path constructs `new ActionEntry(name)` (with positional defaults including `ParallelValidators=false`); no special handling needed.
- `src/FrigateRelay.Host/Dispatch/DispatchItem.cs` (modified) — added `bool ParallelValidators = false` as the 8th positional record-struct parameter with XML doc-comment explaining the consumer (`ChannelActionDispatcher.ConsumeAsync`) and CONTEXT-14 D6 reference.
- `src/FrigateRelay.Host/Dispatch/IActionDispatcher.cs` (modified) — added `bool parallelValidators = false` parameter to `EnqueueAsync` between `subscriptionDefaultSnapshotProvider` and `ct`. XML doc references CONTEXT-14 D6 + the source field on `ActionEntry`.
- `src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs` (modified) — `EnqueueAsync` accepts and forwards the new param into `new DispatchItem(...)` via named arg `ParallelValidators: parallelValidators`.
- `src/FrigateRelay.Host/EventPump.cs:148` — `dispatcher.EnqueueAsync` call now passes `parallelValidators: entry.ParallelValidators` as a named arg (alongside `ct`).

### Tests

- `tests/FrigateRelay.Host.Tests/Configuration/ParallelValidatorsBindingTests.cs` (new, from `3e50c83`) — 6 tests proving the round-trip:
  1. `Bind_ActionEntry_ObjectFormWithParallelValidatorsTrue_BindsCorrectly`
  2. `Bind_ActionEntry_ObjectFormWithoutParallelValidators_DefaultsToFalse`
  3. `Bind_ActionEntry_StringShorthandForm_ParallelValidatorsDefaultsToFalse`
  4. `Deserialize_ActionEntry_JsonWithParallelValidatorsTrue_ProducesCorrectRecord`
  5. `Deserialize_ActionEntry_JsonWithoutParallelValidators_DefaultsToFalse`
  6. `Deserialize_ActionEntry_StringForm_ParallelValidatorsDefaultsToFalse`

  Tests 1-3 exercise the TypeConverter path (`IConfiguration.Bind`); tests 4-6 exercise the JsonConverter path (`JsonSerializer.Deserialize`). Both converter paths produce identical defaults — backward compat invariant.

- `tests/FrigateRelay.Host.Tests/EventPumpTests.cs` — 2 internal stub classes (`NoOpDispatcher`, `CapturingDispatcher`) updated to match the new `IActionDispatcher.EnqueueAsync` signature. `CapturingDispatcher` now also captures `parallelValidators` per-call (`List<bool> CapturedParallelValidators`) so future tests can assert pass-through.
- `tests/FrigateRelay.Host.Tests/EventPumpDispatchTests.cs` — 4 NSubstitute `Received(1).EnqueueAsync(...)` assertion chains gained `Arg.Any<bool>()` between the `subscriptionDefaultSnapshotProvider` arg and the `ct` arg.
- `tests/FrigateRelay.Host.Tests/Observability/EventPumpSpanTests.cs` — `NoOpDispatcher` stub signature updated.
- `tests/FrigateRelay.Host.Tests/Observability/CounterIncrementTests.cs` — `NoOpDispatcher` stub signature updated.

## Decisions Made

- **Field placement: trailing positional record param.** `ActionEntry` and `DispatchItem` both grew a new positional parameter at the END of the record's parameter list (with default `false`). This keeps every existing `new ActionEntry(...)` / `new DispatchItem(...)` call site working without modification — important because Phase 14 has 30+ test sites.
- **Interface seam: optional default-valued param on `IActionDispatcher.EnqueueAsync`.** Adding the parameter with `= false` default keeps existing callers source-compatible without rebuilding. Future durable-dispatcher implementations on the same seam see a stable shape.
- **EventPump pass-through is named-arg style.** `parallelValidators: entry.ParallelValidators` rather than positional. Matches the existing `ct` pass-through style in the same call.
- **Test stub `CapturingDispatcher` now also captures `parallelValidators`.** Adds `List<bool> CapturedParallelValidators` so PLAN-3.2's tests can assert that the right value reached the dispatcher seam without rewriting the harness.
- **No new `[InternalsVisibleTo]` entries** needed. The new tests are in the existing `FrigateRelay.Host.Tests` project which already has access.

## Issues Encountered

- **Test stub classes implementing `IActionDispatcher` directly broke the build.** 4 internal sealed classes (`NoOpDispatcher` × 3, `CapturingDispatcher` × 1) had hand-rolled `EnqueueAsync` signatures that didn't grow with the interface. Errors were CS0535 ("does not implement interface member"). Builder agent flagged the issue and stopped before fixing them; orchestrator finished the cleanup. **Lesson seed:** when extending an internal interface like `IActionDispatcher`, always grep for `: IActionDispatcher` to find hand-rolled implementations that won't auto-update like NSubstitute mocks would.
- **NSubstitute `Received(1).EnqueueAsync(...)` calls also needed updating.** The analyzer raised NS5000 ("Unused received check") because the call argument list didn't match any overload. NSubstitute does NOT silently fill default arguments on these `Received` chains — every positional must be explicit. Fixed via 4 `Arg.Any<bool>()` insertions in `EventPumpDispatchTests.cs`.
- **Builder agent stopped before commit.** Builder was about to check `EventPumpDispatchTests.cs` for the `Received()` issue when its turn budget ran out. Same pattern as Wave 2 PLAN-2.1. Orchestrator committed Tasks 2+3 + finished the test fixes.

## Verification Results

- `dotnet build FrigateRelay.sln -c Release` — **0 warnings, 0 errors** (13.7s elapsed).
- `bash .github/scripts/run-tests.sh --skip-integration` — **273/273 passing, 0 failures**. Test count rose 267 → 273 (+6 new ActionEntry binding tests).
- `git grep -n 'ParallelValidators' src/FrigateRelay.Host/Configuration/ActionEntry.cs` — present.
- `git grep -n 'ParallelValidators' src/FrigateRelay.Host/Dispatch/DispatchItem.cs` — present.
- `git grep -n 'parallelValidators' src/FrigateRelay.Host/EventPump.cs` — present at line 149.
- `git grep -nE 'Grpc\.' src/` — empty ✓ (DOODS2 reversal still holds).
- `git grep -nE 'App\.Metrics|OpenTracing|Jaeger\.' src/` — empty ✓.
- `git grep -nE '\.(Result|Wait)\(' src/` — empty ✓.
- `git grep -n 'ActionEntryTypeConverter' src/FrigateRelay.Host/Configuration/ActionEntry.cs` — decoration still present (TypeConverter path intact).
- `git grep -n 'ActionEntryJsonConverter' src/FrigateRelay.Host/Configuration/ActionEntry.cs` — decoration still present (JsonConverter path intact).
- 2 atomic commits on `feature/23-parallel-validators`: `3e50c83` (Task 1: ActionEntry + converters + 6 tests), `9f64d69` (Tasks 2+3: DispatchItem + IActionDispatcher + ChannelActionDispatcher + EventPump + 4 test stub fixes).

## Next: PLAN-3.2 (dispatcher branch + 6 unit tests) and PLAN-3.3 (integration test + CHANGELOG)

PLAN-3.2 builder targets `src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs:200-246` — adds the `if (item.ParallelValidators)` branch that swaps the existing sequential `foreach` for `Task.WhenAll`. Per CONTEXT-14 D6: strict-AND aggregation, no first-reject short-circuit, each rejecting validator emits its own counter (OQ-4: no aggregate counter).

Plus 6 unit tests in `tests/FrigateRelay.Host.Tests/Dispatch/`:
1. Sequential default unchanged (regression test for backward compat)
2. Parallel happy: all validators allow → action executes
3. Parallel any-reject: at least one rejects → action does NOT execute, all validators DID run (proves no short-circuit)
4. Parallel any-timeout: any validator times out → FailClosed, action does NOT execute
5. Parallel cancellation: host-shutdown propagates `OperationCanceledException` (caught by `ChannelActionDispatcher.cs:259` outer catch — see PLAN-3.2 for the chain explanation)
6. Per-validator `validators.rejected` counter still emits per-validator in parallel mode (no aggregate counter — OQ-4)

The `CapturingDispatcher` test stub already has `CapturedParallelValidators` from Task 2's update, so unit tests can assert pass-through trivially.

## Notes for the PLAN-3.1 reviewer

- The 6 binding tests prove both converter paths (`IConfiguration.Bind` via `ActionEntryTypeConverter` AND `JsonSerializer.Deserialize` via `ActionEntryJsonConverter`) produce the same defaults and propagate `ParallelValidators=true` correctly.
- Backward compat invariant: existing v1.0/v1.1 `appsettings.json` files using `Actions: ["BlueIris"]` string-shorthand or `Actions: [{"Plugin":"BlueIris"}]` object-form WITHOUT `ParallelValidators` continue to bind as `ActionEntry { Plugin="BlueIris", ParallelValidators=false }`. Tests 3 and 6 prove this explicitly.
- The new `bool` parameter is positioned at the END of `IActionDispatcher.EnqueueAsync` (with `= false` default) BEFORE the `CancellationToken ct = default`. This is the C# convention: required positional → optional value-typed → cancellation token last. Matches existing `EnqueueAsync` shape.
