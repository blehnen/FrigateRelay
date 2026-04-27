# Review: Plan 2.1

## Verdict: MINOR_ISSUES

## Findings

### Critical
None.

### Minor

- **`event.match` span ends before dedupe filtering** — `EventPump.cs` lines 101–110: the `matchActivity` `using` block closes immediately after `subscription_count_matched` is set, so the dedupe check (`_dedupe.TryEnter`) runs *outside* the span. Result: the attribute reports raw match count, not post-dedupe; the dedupe work is uncovered by any span.
  - Remediation: either move the span's close brace to encompass the dedupe loop and report post-dedupe count, or rename the attribute to `subscription_count_raw_matched` to clarify pre-dedupe semantics.

- **`actionActivity?.SetStatus(ActivityStatusCode.Ok, "ValidatorRejected")` on validator rejection is semantically off** — `ChannelActionDispatcher.cs:256`. OTel `Ok` means "completed successfully"; a validator rejection is a deliberate skip, not a success. Mirror the ID-6 fix and use `ActivityStatusCode.Unset` (no description) for the skip.
  - Remediation: change line 256 to `actionActivity?.SetStatus(ActivityStatusCode.Unset)`. Suppresses dashboard-Error appearance (correct) and conveys "neither error nor success" (correct).

- **`ActionsDispatched` increment is before `WriteAsync`** — `ChannelActionDispatcher.cs:142–157`. If the channel is completed during shutdown, the counter has already incremented but the item was never enqueued. Minor accuracy gap; not a functional bug. Either move the `.Add(1)` after `WriteAsync` returns, or update the comment to say "items offered to the channel writer."

- **`goto NextItem` pattern in `ConsumeAsync`** — `ChannelActionDispatcher.cs:257, 291`. Valid C# but unconventional. A `bool validationPassed` flag with `if (!validationPassed) continue;` would be more readable.

### Positive

- **All 5 spans correctly named and kinded:** `mqtt.receive` (Server), `event.match` (Internal), `dispatch.enqueue` (Producer), `action.<name>.execute` (Consumer, parented to `item.ParentContext` per D1), `validator.<name>.check` (Internal).
- **D8 attribute table fully implemented:** every span carries `event.id`; spans add their semantic dimensions (`camera`/`label`/`subscription`/`action`/`validator`/`verdict`/`outcome`) per the table.
- **D3 counter dimensions correct:** `events.received` carries `camera+label`; `events.matched` adds `subscription`; `actions.*` carry `subscription+action`; `validators.*` carry `subscription+action+validator`.
- **D9 honored:** `ErrorsUnhandled` is a single tagless `.Add(1)` call site in `EventPump.PumpAsync`'s outermost catch. Zero occurrences in `ChannelActionDispatcher`.
- **ID-6 fix verified:** `OperationCanceledException when ct.IsCancellationRequested` block now sets `ActivityStatusCode.Unset` (no description). Zero occurrences of `Error, "Cancelled"` in the file.
- **`"ActionDispatch"` legacy span name fully removed.**
- **`DispatchItem.Subscription`** added as positional record parameter; `IActionDispatcher.EnqueueAsync` updated; existing test stubs updated.
- **No new `public` types**, no `.Result`/`.Wait()`, no `ServicePointManager`, no hard-coded IPs.
- **No `[LoggerMessage]` source-generator** introduced — D6 honored.
- **Zero matches** for forbidden packages (`App.Metrics`, `OpenTracing`, `Jaeger.*`).
- **ISSUES.md** correctly moves ID-6 to Closed with commit reference (`06ff862`); ID-13/14/15 untouched.

## Summary
Critical: 0 | Minor: 4 | Positive: 11. APPROVE — both Important findings (event.match span scoping, validator-rejected status code) are non-blocking but worth addressing as a follow-up commit before phase finalization.
