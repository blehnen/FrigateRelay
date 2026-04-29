# Build Summary: Plan 2.1 — Pipeline Instrumentation (Spans, Counters, ID-6 Fix)

## Status: complete

Build clean (0 warnings / 0 errors). 64/69 Host.Tests pass at PLAN-2.1 close — 5 failures are PLAN-2.2-induced regressions (not introduced by this plan); see Issues Encountered.

## Tasks Completed

| Task | Description | Commit |
|---|---|---|
| Task 1 | EventPump instrumentation: `mqtt.receive` / `event.match` / `dispatch.enqueue` spans + counters + `ErrorsUnhandled` + `DispatchItem.Subscription` field + `IActionDispatcher` signature update + test fixes | `06ff862` |
| Task 2 | ChannelActionDispatcher: `action.<name>.execute` + `validator.<name>.check` spans + `ActionsSucceeded`/`ActionsFailed`/`ValidatorsPassed`/`ValidatorsRejected` counters + ID-6 fix | `06ff862` (combined with Task 1 — tightly coupled, identical surface) |
| Task 3 | Close ID-6 in ISSUES.md | `e0ec830` |

## Files Modified

- `src/FrigateRelay.Host/EventPump.cs` — `mqtt.receive` (Server), `event.match` (Internal), `dispatch.enqueue` (Producer) spans with D8 attributes; `EventsReceived`, `EventsMatched`, `ActionsDispatched` counters; `ErrorsUnhandled.Add(1)` at single top-level catch (D9).
- `src/FrigateRelay.Host/Dispatch/DispatchItem.cs` — added `string Subscription = ""` positional record parameter.
- `src/FrigateRelay.Host/Dispatch/IActionDispatcher.cs` — added `string subscription = ""` parameter to `EnqueueAsync`.
- `src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs` — `EnqueueAsync`: `ActionsDispatched` + passes `Subscription` to `DispatchItem`; `ConsumeAsync`: `"ActionDispatch"` renamed to `action.<name>.execute`, validator spans, all 4 outcome counters, ID-6 fix.
- `src/FrigateRelay.Host/StartupValidation.cs` — removed stale `using Microsoft.Extensions.Configuration` (IDE0005 from parallel PLAN-2.2 wave; unblocks warnings-as-errors build).
- `tests/FrigateRelay.Host.Tests/EventPumpTests.cs` — updated `NoOpDispatcher.EnqueueAsync` stub to new 7-param signature.
- `tests/FrigateRelay.Host.Tests/EventPumpDispatchTests.cs` — 3 NSubstitute `Received` assertions updated for new `subscription` parameter.
- `.shipyard/ISSUES.md` — ID-6 moved to Closed with commit reference.

## Decisions Made

- **`event.source` tag uses `source.Name`** — `EventContext` has no `SourceName` property (D8 table listed it incorrectly). `IEventSource.Name` is the correct semantic value available in `PumpAsync` scope.
- **`dispatch.enqueue` Producer span lives in EventPump only.** Consumer side (`ChannelActionDispatcher`) starts the `action.<name>.execute` Consumer span parented to `item.ParentContext` — correct OTel producer/consumer model. No duplicate span.
- **`ActionsDispatched` incremented in `EnqueueAsync`** (not attempt-time): counts items actually written to the channel (after the plugin-registered guard), consistent with where `Drops` fires.
- **ID-6 fix wording:** `actionActivity?.SetStatus(ActivityStatusCode.Unset)` — no description string, no `RecordException`. The `when (ct.IsCancellationRequested)` filter on the catch already isolates graceful shutdown; no additional condition check inside the handler.
- **`errors.unhandled` placement (D9):** exactly one `ErrorsUnhandled.Add(1)` in `EventPump.PumpAsync` outermost catch. Zero in `ChannelActionDispatcher` — retry-exhausted path increments `ActionsFailed` + `Exhausted` instead.

## Issues Encountered

- **PLAN-2.2 parallel-wave build conflicts (two instances):**
  - `StartupValidation.cs` had `using Microsoft.Extensions.Configuration` flagged IDE0005 — the `using` was redundant under SDK global usings; removed to unblock warnings-as-errors. Mechanical one-line fix, no logic change.
  - `HostBootstrap.cs` CA1305/IDE0005 errors blocked my first build attempt — resolved when PLAN-2.2 committed their Serilog wiring (`d6da64d`, `c82dd83`).
- **PLAN-2.2 test regressions (5 pre-existing failures, not introduced by this plan):** `ProfileResolutionTests` (4) + `Json_Is_At_Most_60_Percent_Of_Ini_Character_Count` (1) fail because `StartupValidation.ValidateAll` now calls `services.GetRequiredService<IConfiguration>()` (added by PLAN-2.2) but test fixtures don't register `IConfiguration`. Exception: `"No service for type 'Microsoft.Extensions.Configuration.IConfiguration' has been registered."` PLAN-2.2 owner needs to either (a) register an `IConfigurationRoot` in the affected test fixtures, or (b) switch `GetRequiredService` → `GetService` to make `IConfiguration` optional (consistent with how `IOptions<SnapshotResolverOptions>` is handled at line 47 of StartupValidation.cs).

## Verification Results

- Build: clean at every commit (0 warnings, 0 errors).
- `mqtt.receive`, `event.match`, `dispatch.enqueue` — 1 match each in `EventPump.cs`.
- `action.<name>.execute` via `spanName` local — 1 match in `ChannelActionDispatcher.cs`.
- `validator.<name>.check` via `validatorSpanName` local — 1 match in `ChannelActionDispatcher.cs`.
- `"ActionDispatch"` — 0 occurrences (legacy name removed).
- All 8 counters increment at correct sites; `ErrorsUnhandled` is single-site (D9).
- `ActivityStatusCode.Unset` present in `ChannelActionDispatcher.cs`; `ActivityStatusCode.Error, "Cancelled"` — 0 occurrences (ID-6 closed).
- No new `public` types. No `.Result`/`.Wait()`. No `ServicePointManager`. No forbidden packages.
- ID-13, ID-14, ID-15 in ISSUES.md untouched.
- ID-6 status flipped to **Closed** with commit `06ff862` reference.
