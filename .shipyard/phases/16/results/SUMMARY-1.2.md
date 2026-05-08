# Build Summary: PLAN-1.2 — WaitForEntriesAsync (#22)

## Status: complete

## Tasks Completed

- **Task 1: Add WaitForEntriesAsync to CapturingLogger<T>** — complete
  - File: `tests/FrigateRelay.TestHelpers/CapturingLogger.cs`
  - Commit: `49d39e4 shipyard(phase-16): add WaitForEntriesAsync helper to CapturingLogger (#22)`

- **Task 2: Replace 4 Task.Delay sites + CHANGELOG** — complete
  - Files: `tests/FrigateRelay.Host.Tests/Observability/CounterIncrementTests.cs`, `tests/FrigateRelay.Host.Tests/Observability/EventPumpSpanTests.cs`, `CHANGELOG.md`
  - Commit: `37f97ce shipyard(phase-16): replace Task.Delay polling with WaitForEntriesAsync (#22)`

## Files Modified

- `tests/FrigateRelay.TestHelpers/CapturingLogger.cs` — added `public async Task WaitForEntriesAsync(int count, TimeSpan timeout, CancellationToken ct = default)`. Polls `Entries.Count >= count` at 25ms intervals (CONTEXT-16 OQ-3 internal const, no exposed knob). Throws `TimeoutException` with descriptive message on deadline. `OperationCanceledException` propagates naturally from `Task.Delay`.
- `tests/FrigateRelay.Host.Tests/Observability/EventPumpSpanTests.cs` — replaced numeric `Task.Delay(...)` in `RunPumpAsync` helper with `await logger.WaitForEntriesAsync(1, TimeSpan.FromSeconds(2))`.
- `tests/FrigateRelay.Host.Tests/Observability/CounterIncrementTests.cs` — replaced 3 numeric `Task.Delay(...)` sites:
  - `RunPumpAsync` helper → `WaitForEntriesAsync(1, 2s)`
  - `RunPumpAsyncWithSource` helper (FaultingSource path) → `WaitForEntriesAsync(1, 2s)` for the `LogPumpFaulted` Error-level emission
  - `RunDispatcherAsync` helper → **MeterListener-based `TaskCompletionSource` fallback** (PLAN-1.2 D2 explicitly authorized this fallback for the dispatcher-success site since it emits no log message). Terminal-metric set covers all three terminal states: `actions.succeeded`, `actions.failed`, `actions.exhausted`, `validators.rejected`.
- `CHANGELOG.md` — `[Unreleased]` `### Internal` block added with both lines (helper switch + invariant correction) per CONTEXT-16 D8.

## Decisions Made

- **`RunDispatcherAsync` uses the authorized MeterListener fallback.** PLAN-1.2 Task 2 Step 4 explicitly authorized this fallback for the dispatcher-success site (the success path emits no log message). The helper attaches a `MeterListener` to the four terminal counter names and resolves a `TaskCompletionSource<bool>` on the first measurement.
- **Terminal-metric set extended with `validators.rejected`.** Initial implementation used `actions.{succeeded,failed,exhausted}` only — but the validator-short-circuit path (`Verdict.Fail`) emits `validators.rejected` and never increments any `actions.*` counter, so a validator-rejected test (`ValidatorsRejected_Tags_SubscriptionActionValidator_OnFail`) timed out. Adding `validators.rejected` to the terminal set covers all three terminal states (success / failure / validator-short-circuit). Caught by the test suite, fixed inline before commit.
- **Three logger waits + one MeterListener fallback = 4 deterministic waits total** (the plan's "exactly 4 WaitForEntriesAsync hits in observability tests" acceptance criterion is satisfied in spirit — 3 via the helper, 1 via the authorized fallback. Documented in the commit message and the CHANGELOG.)
- **Builder agent investigated an EnqueueAsync `parallelValidators` parameter concern** mid-Task-2 but the build was already green; the orchestrator picked up the work, confirmed the existing call shape compiles and runs at HEAD, and didn't add the parameter (it's not required for these test paths and it's not in the plan's scope).

## Issues Encountered

- **Builder agent hit tool-budget cap mid-Task-2** (Phase 9 / Phase 16 pattern recurring). Resumed via SendMessage; orchestrator finished the last 2 sites + the bug-fix inline.
- **Validator-rejected test failure on first run.** First commit attempt would have shipped a regression — the MeterListener terminal set excluded `validators.rejected`, so `ValidatorsRejected_Tags_SubscriptionActionValidator_OnFail` timed out. Caught at orchestrator's pre-commit verification step (running `dotnet run --project tests/FrigateRelay.Host.Tests -c Release` before staging the commit), fixed inline by extending the terminal-name set.
- **The plan's "exactly 4 WaitForEntriesAsync hits" acceptance criterion conflicted with the explicit MeterListener fallback authorization** for the dispatcher-success site. Resolved by counting "deterministic waits" rather than "WaitForEntriesAsync calls": 3 logger waits + 1 MeterListener fallback = 4 deterministic waits.

## Verification Results

- `dotnet build FrigateRelay.sln -c Release`: **0 warnings, 0 errors**.
- `dotnet run --project tests/FrigateRelay.Host.Tests -c Release`: **154/154 pass** (no test-count change — the plan didn't add new tests, it refactored existing ones). Duration 7.5s (vs ~13s pre-refactor — faster because the deterministic waits short-circuit on the first matching log entry instead of waiting the full 400ms).
- `git grep -nE 'Task\.Delay\([0-9]' tests/FrigateRelay.Host.Tests/Observability/`: **empty**. The tightened invariant is met.
- `git grep -nE 'Task\.Delay\(Timeout\.Infinite' tests/FrigateRelay.Host.Tests/Observability/`: **2 hits**. The 2 cancellation-await sites in `BatchSource` and `FakeSource` IEventSource stubs are preserved as structurally correct.
- `git grep -nE 'WaitForEntriesAsync\(' tests/FrigateRelay.Host.Tests/Observability/`: **3 hits**. The 4th deterministic wait is the authorized MeterListener fallback in `RunDispatcherAsync` (commented inline at the call site).
- `git grep -n 'public async Task WaitForEntriesAsync' tests/FrigateRelay.TestHelpers/`: **1 hit** (the declaration).
- CHANGELOG `[Unreleased]` `### Internal` block contains both lines (helper switch + invariant correction).

## Lesson Seeds

- **Pre-commit `dotnet run` test sweep is load-bearing.** Builder reported "build succeeded, ready to commit" after the helper extraction, but the test suite caught the missed `validators.rejected` terminal state. Lesson: always run the full test suite before staging the test-touching commit; trust no agent's "looks good" without the test output.
- **MeterListener fallback's terminal-metric set must enumerate every terminal state, not just the happy path.** The validator-short-circuit branch is a real terminal state. Future "wait until done" patterns: ask "if every code branch terminates, would my wait condition fire?" before declaring the wait deterministic.
- **`CapturingLogger<T>.WaitForEntriesAsync` is the right abstraction for log-emission polling**, but it doesn't cover metric-emission polling. The split (logger-helper for log-driven waits, MeterListener for metric-driven waits) is the right shape. A future `CapturingMeter` helper could unify these into a single `WaitForMeasurementsAsync` for the metric-only sites — Phase 17 candidate.
- **Test refactors are 2x faster than the originals after the switch** (~7.5s vs ~13s). Fixed-time `Task.Delay` polling pays the worst case every time; deterministic waits short-circuit. Real CI-time savings at scale.

<!-- context: turns=35+orchestrator-inline, compressed=no, task_complete=yes -->
