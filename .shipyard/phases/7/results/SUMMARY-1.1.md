# Build Summary: Plan 7.1.1 — `IValidationPlugin` sig + `SnapshotContext.PreResolved` + dispatcher validator chain

## Status: complete

## Tasks Completed

- **Task 1** — `IValidationPlugin.ValidateAsync` extended with `SnapshotContext snapshot` parameter (CONTEXT-7 D1 lock-in). Updated XML doc; ID-12-aware comment in `<remarks>`. Commit `12ee767`.
- **Task 2** — `SnapshotContext.cs` gains a second constructor `SnapshotContext(SnapshotResult? preResolved)` + `_hasPreResolved` flag + branched `ResolveAsync` per RESEARCH §5 Option A. Two TDD tests (`ResolveAsync_PreResolved_*`) added to `SnapshotContextTests.cs`. Commit `29adaab`.
- **Task 3** — `ChannelActionDispatcher.ConsumeAsync` rewired: when `item.Validators.Count > 0`, pre-resolves snapshot ONCE via the resolver-backed `SnapshotContext`, wraps result via `SnapshotContext(SnapshotResult?)`, iterates validators with first-fail short-circuit logging structured `validator_rejected` at Warning (EventId 20). New `LogValidatorRejected` `LoggerMessage.Define` carries event_id, camera, label, action, validator, reason (CONTEXT-7 D7). Validator chain runs ABOVE the action's Polly retry pipeline. No-validators path unchanged (BlueIris-only subscriptions still pay zero snapshot fetch cost). Two TDD tests in `ChannelActionDispatcherTests.cs`. Commit `c4ba938`.

## Files Modified

- `src/FrigateRelay.Abstractions/IValidationPlugin.cs` — signature extension + remarks block.
- `src/FrigateRelay.Abstractions/SnapshotContext.cs` — added `SnapshotContext(SnapshotResult?)` ctor + `_hasPreResolved` flag + branch in `ResolveAsync`.
- `src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs` — added `LogValidatorRejected` LoggerMessage + validator chain + pre-resolve branch in `ConsumeAsync`.
- `tests/FrigateRelay.Abstractions.Tests/SnapshotContextTests.cs` — +2 tests (PreResolved cached, PreResolved null).
- `tests/FrigateRelay.Host.Tests/Dispatch/ChannelActionDispatcherTests.cs` — +2 tests + 4 new helpers (`RecordingPlugin`, `StubValidator`, `SnapshotReadingValidator`, `SnapshotReadingPlugin`, `CountingResolver`); extended `BuildDispatcher` to accept optional `ISnapshotResolver`.

## Decisions Made

- Used `goto NextItem;` to skip the action invocation cleanly when a validator fails. Considered an extracted helper method or a `bool` flag — `goto` to a labeled statement at the end of the foreach body is idiomatic for the "skip the rest of this iteration after a multi-line setup" case and keeps the existing try/catch structure intact (the goto exits the try cleanly; no catch fires).
- `_hasPreResolved` is a boolean alongside `_preResolved` so that an explicitly-null pre-resolved snapshot is distinguishable from `default(SnapshotContext)` (where neither field is set). Both produce the same observable behavior today (`ResolveAsync` returns null), but the flag prevents future resolver-fallback logic from accidentally firing when an explicit null was intended.
- `EventId(20, "ValidatorRejected")` chosen for the structured log to keep distinct from existing dispatcher EventIds 10 (DispatchItemDropped), 11 (PluginNotRegistered), and 101 (DispatchRetryExhausted).

## Issues Encountered

- **Builder agent failed on Bash permission.** Subagent dispatched via the Agent tool was denied Bash for both running baseline tests and committing — same issue applied to the parallel PLAN-1.2 builder. Pattern is identical to the orchestrator-finishes-truncation pattern from Phases 1/3/5/6. Orchestrator took the work over inline.
- **CS1734 on initial XML comment.** First version of `IValidationPlugin.cs` used `<paramref name="snapshot"/>` in the interface-level `<remarks>` block — `paramref` requires a parameter scope (interface has no parameters). Switched to `<c>snapshot</c>` text reference. One round-trip cost ~5 minutes.
- **CA1861 in PLAN-1.2 builder's test file.** Builder used `new[] { "strict-person", "lax-vehicle" }` repeatedly; analyzer required hoisting to a `static readonly string[]` field. Trivial fix; no design impact.

## Verification Results

- `dotnet build FrigateRelay.sln -c Release` — **0 warnings, 0 errors**.
- All test projects via `.github/scripts/run-tests.sh`:
  - `FrigateRelay.Abstractions.Tests` — 25/25 (was 23, +2)
  - `FrigateRelay.Host.Tests` — 52/52 (was 48, +2 from PLAN-1.2 + +2 from PLAN-1.1)
  - `FrigateRelay.IntegrationTests` — 2/2
  - `FrigateRelay.Plugins.BlueIris.Tests` — 17/17
  - `FrigateRelay.Plugins.FrigateSnapshot.Tests` — 6/6
  - `FrigateRelay.Plugins.Pushover.Tests` — 10/10
  - `FrigateRelay.Sources.FrigateMqtt.Tests` — 18/18
  - **Total: 130/130 across all suites** (Phase 6 baseline 124, +6 new in Wave 1).
- Architecture invariant greps — clean (no `.Result`/`.Wait`, no `ServicePointManager`).

## Lesson seeding (for `/shipyard:ship`)

- **Subagent Bash-permission denial is now a steady-state pattern in this session.** Both builder agents (PLAN-1.1 and PLAN-1.2) failed before any commit because their Bash tool was denied. The orchestrator-finishes-inline pattern handles it, but the cycle wastes ~30s of agent dispatch time per failure. If agent permissions can be elevated for the build phase, the dispatch model would work as designed. Otherwise, future phases should consider dispatching agents to Read/Edit ONLY and have the orchestrator run all bash-required steps.
- **`<paramref>` in interface-level docstrings is a CS1734 trap.** `<paramref>` requires a parameter scope — works on `<param>` and `<returns>` blocks of methods, NOT on interface-level `<summary>` or `<remarks>`. Use `<c>name</c>` for prose references at the type level.
- **`goto label` to a labeled statement at the end of a foreach body** is the cleanest pattern for "skip the rest of this iteration after a multi-line try block." The alternative (`continue;` from inside try) doesn't work because the try has matching catch blocks that would fire on cancellation; the labeled statement after the catch blocks lets us exit the try cleanly without engaging catch handlers.