---
phase: phase-9-observability
plan: 2.1
wave: 2
dependencies: [PLAN-1.1]
must_haves:
  - mqtt.receive, event.match, dispatch.enqueue, action.<name>.execute, validator.<name>.check spans emit with the D8 attribute set
  - All eight Phase 9 counters increment at the correct sites with the D3 tag dimensions
  - ID-6 closed: graceful shutdown sets ActivityStatusCode.Unset, never Error
  - errors.unhandled increments only at top-level pump catch (D9 — single tagless series)
files_touched:
  - src/FrigateRelay.Host/Pipeline/EventPump.cs
  - src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs
tdd: false
risk: medium
---

# Plan 2.1: Pipeline Instrumentation — Spans, Counters, ID-6 Fix

## Context

Implements CONTEXT-9 D1 (channel-hop tracing), D3 (counter tag dimensions), D4 (ID-6 OperationCanceled status fix), D8 (span attribute table — finalized below), and D9 (errors.unhandled at single top-level catch only). All work is in two files; no new files.

## D8 — Finalized span attribute table

| Span | Kind | Attributes |
|---|---|---|
| `mqtt.receive` | `ActivityKind.Server` | `event.id` (EventContext.EventId), `event.source` (EventContext.SourceName) |
| `event.match` | `ActivityKind.Internal` | `event.id`, `camera`, `label`, `subscription_count_matched` (int — number of subscriptions that matched) |
| `dispatch.enqueue` | `ActivityKind.Producer` | `event.id`, `subscription`, `action_count` (int — number of actions enqueued for this subscription) |
| `action.<name>.execute` | `ActivityKind.Consumer` | `event.id`, `action`, `subscription`, `outcome` (`success`\|`failure`\|`validator_rejected`) |
| `validator.<name>.check` | `ActivityKind.Internal` | `event.id`, `validator`, `action`, `subscription`, `verdict` (`pass`\|`fail`), `reason` (string — verdict.Reason, only when verdict=`fail`) |

Notes:
- The `<name>` placeholder is the lowercased plugin name (`action.blueiris.execute`, `validator.codeprojectai.check`). Build the span name string via `$"action.{plugin.Name.ToLowerInvariant()}.execute"`.
- `outcome` is set on the action span before `Activity.Stop()` — `success` on normal return, `failure` in the catch block (alongside existing `actions.failed` counter increment), `validator_rejected` when a validator short-circuits the action (alongside existing `validator_rejected` log per Phase 7).
- Every span gets `event.id` so Tempo/Jaeger can join-by-correlation per CONTEXT-9 §D8 rationale.

## Dependencies

PLAN-1.1 — needs the counter declarations (`EventsReceived` etc.) and the `ParentContext` field on `DispatchItem`.

## Tasks

### Task 1: Instrument EventPump.PumpAsync with mqtt.receive, event.match, dispatch.enqueue spans + counters
**Files:** `src/FrigateRelay.Host/Pipeline/EventPump.cs`
**Action:** modify
**Description:**

For each `EventContext` yielded by `await foreach context in source.ReadEventsAsync(ct)`:

1. **`mqtt.receive` span** — wraps the entire iteration body for one event.
   ```csharp
   using var receiveActivity = DispatcherDiagnostics.ActivitySource.StartActivity(
       "mqtt.receive", ActivityKind.Server);
   receiveActivity?.SetTag("event.id", context.EventId);
   receiveActivity?.SetTag("event.source", context.SourceName);
   ```
   Increment `DispatcherDiagnostics.EventsReceived.Add(1, new TagList { {"camera", context.Camera}, {"label", context.Label} })` immediately after starting the span.

2. **`event.match` span** — wraps the `SubscriptionMatcher.Match(...)` + `DedupeCache.TryEnter(...)` block.
   ```csharp
   using (var matchActivity = DispatcherDiagnostics.ActivitySource.StartActivity(
       "event.match", ActivityKind.Internal))
   {
       matchActivity?.SetTag("event.id", context.EventId);
       matchActivity?.SetTag("camera", context.Camera);
       matchActivity?.SetTag("label", context.Label);
       var matched = SubscriptionMatcher.Match(...);
       matchActivity?.SetTag("subscription_count_matched", matched.Count);
       // existing dedupe + iteration logic
   }
   ```
   For each matched subscription that passes dedupe, increment `EventsMatched.Add(1, ...)` with tags `camera`, `label`, `subscription` (from `sub.Name`).

3. **`dispatch.enqueue` span** — wraps the per-subscription loop that calls `_dispatcher.EnqueueAsync(...)` for each action.
   ```csharp
   using (var enqueueActivity = DispatcherDiagnostics.ActivitySource.StartActivity(
       "dispatch.enqueue", ActivityKind.Producer))
   {
       enqueueActivity?.SetTag("event.id", context.EventId);
       enqueueActivity?.SetTag("subscription", sub.Name);
       enqueueActivity?.SetTag("action_count", sub.Actions.Count);
       foreach (var actionEntry in sub.Actions) { /* existing EnqueueAsync */ }
   }
   ```
   For each `EnqueueAsync` call, increment `ActionsDispatched.Add(1, ...)` with tags `subscription`, `action`.

4. **D9 — `errors.unhandled` at single top-level catch.**
   In `PumpAsync`'s outermost `catch (Exception ex)` block (the one that emits the existing `LogPumpFaulted` Action<ILogger,...> delegate), add ONE line BEFORE the existing log call:
   ```csharp
   DispatcherDiagnostics.ErrorsUnhandled.Add(1);
   ```
   No tags — D3 mandates this is a single tagless series. Do NOT add this counter increment anywhere else in the pipeline.

5. **Logging delegates.** Any new diagnostic log lines you add for span lifecycle (e.g. Debug-level "matched N subscriptions" if useful) MUST use the hand-rolled `Action<ILogger,...>` delegate pattern (D6) with EventIds in the 500–599 range to avoid collision with existing IDs (max existing observed: 101). If existing log lines in `EventPump.cs` already cover the lifecycle adequately, do not add new ones — D6 says match the existing style; it does not mandate new logs.

**Acceptance Criteria:**
- `grep -n 'StartActivity("mqtt.receive"' src/FrigateRelay.Host/Pipeline/EventPump.cs` returns one match.
- `grep -n 'StartActivity("event.match"' src/FrigateRelay.Host/Pipeline/EventPump.cs` returns one match.
- `grep -n 'StartActivity("dispatch.enqueue"' src/FrigateRelay.Host/Pipeline/EventPump.cs` returns one match.
- `grep -nE 'EventsReceived\.Add|EventsMatched\.Add|ActionsDispatched\.Add' src/FrigateRelay.Host/Pipeline/EventPump.cs` returns at least three matches.
- `grep -n 'ErrorsUnhandled.Add' src/FrigateRelay.Host/Pipeline/EventPump.cs` returns exactly one match (D9 single-site).
- `grep -n 'ErrorsUnhandled.Add' src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs` returns ZERO matches (D9 — not in dispatcher).
- `grep -nE 'LoggerMessage\.Define' src/FrigateRelay.Host/Pipeline/EventPump.cs` shows any new delegates use EventIds in 500–599 range (manual review check).
- `dotnet build FrigateRelay.sln -c Release` clean.

### Task 2: Instrument ChannelActionDispatcher.ConsumeAsync with action+validator spans, counters, ID-6 fix
**Files:** `src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs`
**Action:** modify
**Description:**

1. **Rename existing dispatch span.** The existing `StartActivity("ActionDispatch", ...)` call at RESEARCH.md §3 line 173–176 is REPLACED at this point. Rename the span to `$"action.{item.Plugin.Name.ToLowerInvariant()}.execute"` and change `ActivityKind.Internal` → `ActivityKind.Consumer` (already partially done in PLAN-1.1 for the kind; this task completes the rename and tag set):
   ```csharp
   var spanName = $"action.{item.Plugin.Name.ToLowerInvariant()}.execute";
   using var actionActivity = DispatcherDiagnostics.ActivitySource.StartActivity(
       spanName, ActivityKind.Consumer, parentContext: item.ParentContext);
   actionActivity?.SetTag("event.id", item.Context.EventId);
   actionActivity?.SetTag("action", item.Plugin.Name);
   actionActivity?.SetTag("subscription", /* SubscriptionName carried on DispatchItem or via outer scope */);
   ```
   **Note on `subscription` tag source.** If `DispatchItem` does not currently carry the subscription name, the producer side (`ChannelActionDispatcher.EnqueueAsync` parameter list, or `EventPump` caller) must pass it. Verify at build time. If a new field is needed, add `string Subscription` as a positional record param on `DispatchItem` (this is a small additive change — not breaking, since all call sites are internal). Update PLAN-1.1's verification matrix mentally; the structural impact is small enough that it stays in PLAN-2.1 rather than retroactively expanding 1.1.

2. **Validator span per validator iteration.** Inside the existing validator loop:
   ```csharp
   foreach (var validator in item.Validators)
   {
       var validatorSpanName = $"validator.{validator.Name.ToLowerInvariant()}.check";
       using var vActivity = DispatcherDiagnostics.ActivitySource.StartActivity(
           validatorSpanName, ActivityKind.Internal);
       vActivity?.SetTag("event.id", item.Context.EventId);
       vActivity?.SetTag("validator", validator.Name);
       vActivity?.SetTag("action", item.Plugin.Name);
       vActivity?.SetTag("subscription", subscriptionName);

       var verdict = await validator.ValidateAsync(item.Context, ct);
       vActivity?.SetTag("verdict", verdict.Passed ? "pass" : "fail");
       if (!verdict.Passed)
           vActivity?.SetTag("reason", verdict.Reason);

       var validatorTags = new TagList {
           {"subscription", subscriptionName},
           {"action", item.Plugin.Name},
           {"validator", validator.Name}
       };
       if (verdict.Passed)
           DispatcherDiagnostics.ValidatorsPassed.Add(1, validatorTags);
       else
       {
           DispatcherDiagnostics.ValidatorsRejected.Add(1, validatorTags);
           actionActivity?.SetTag("outcome", "validator_rejected");
           // existing validator_rejected log + skip-action logic
           return; // or break per existing flow
       }
   }
   ```

3. **Action outcome counter increments.**
   - On normal `plugin.ExecuteAsync` return: `actionActivity?.SetTag("outcome", "success");` and `DispatcherDiagnostics.ActionsSucceeded.Add(1, new TagList { {"subscription", subscriptionName}, {"action", item.Plugin.Name} });`.
   - In the existing retry-exhausted catch (RESEARCH.md §7 cites line 241): add `actionActivity?.SetTag("outcome", "failure");` and `DispatcherDiagnostics.ActionsFailed.Add(1, sameTags);`. Keep the existing `Exhausted.Add(1)` call.

4. **D4 — ID-6 fix.** At RESEARCH.md §4 lines 235–239:
   ```csharp
   catch (OperationCanceledException) when (ct.IsCancellationRequested)
   {
       actionActivity?.SetStatus(ActivityStatusCode.Unset);  // was: Error, "Cancelled"
       return;
   }
   ```
   Per D4 — graceful shutdown is `Unset`, never `Error`. Do NOT add a SetStatus call to the non-graceful `OperationCanceledException` path (if any) — keep current behavior there.

5. **D9 reminder — no `ErrorsUnhandled.Add` in this file.** The retry-exhausted path increments `ActionsFailed` (expected) and `Exhausted` (expected). `ErrorsUnhandled` is reserved for unexpected pipeline escapes (PumpAsync top-level catch only).

**Acceptance Criteria:**
- `grep -nE 'StartActivity\(\$\"action\.' src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs` returns one match (interpolated action span name).
- `grep -nE 'StartActivity\(validatorSpanName|StartActivity\(\$\"validator\.' src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs` returns one match.
- `grep -n '"ActionDispatch"' src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs` returns zero (legacy name removed).
- `grep -nE 'ActionsSucceeded\.Add|ActionsFailed\.Add|ValidatorsPassed\.Add|ValidatorsRejected\.Add' src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs` returns at least four matches.
- `grep -n 'ActivityStatusCode.Unset' src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs` returns at least one match (ID-6 fix).
- `grep -n 'ActivityStatusCode.Error, "Cancelled"' src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs` returns zero (legacy ID-6 bug gone).
- `grep -n 'ErrorsUnhandled' src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs` returns zero (D9).
- `dotnet build FrigateRelay.sln -c Release` clean.
- Existing dispatcher tests still pass: `dotnet run --project tests/FrigateRelay.Host.Tests -c Release` zero failures.

### Task 3: ISSUES.md — close ID-6
**Files:** `.shipyard/ISSUES.md`
**Action:** modify
**Description:**
After Task 2 lands, transition ID-6's `Status:` field to `Closed (Phase 9 PLAN-2.1 task 2 — ChannelActionDispatcher.cs ID-6 fix)`. Add a one-line note under ID-6 referencing the file:line of the fix (the line where `ActivityStatusCode.Unset` now appears).

Do NOT touch ID-13 / ID-14 / ID-15 (out of scope per CONTEXT-9 "Out of scope" section).

**Acceptance Criteria:**
- `grep -A 3 '^### ID-6' .shipyard/ISSUES.md | grep -i 'closed'` returns at least one match.
- `grep -A 3 '^### ID-13\|^### ID-14\|^### ID-15' .shipyard/ISSUES.md | grep -ic 'closed'` returns zero matches (untouched).

## Verification

```bash
cd /mnt/f/git/frigaterelay

# Build clean
dotnet build FrigateRelay.sln -c Release 2>&1 | tail -5

# Five span names present
grep -hnE 'StartActivity\("mqtt\.receive"|StartActivity\("event\.match"|StartActivity\("dispatch\.enqueue"|StartActivity\(\$"action\.|StartActivity\(\$"validator\.' \
  src/FrigateRelay.Host/Pipeline/EventPump.cs \
  src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs

# Counter call sites — full set covered
grep -hnE '(EventsReceived|EventsMatched|ActionsDispatched|ActionsSucceeded|ActionsFailed|ValidatorsPassed|ValidatorsRejected|ErrorsUnhandled)\.Add' \
  src/FrigateRelay.Host/Pipeline/EventPump.cs \
  src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs

# ErrorsUnhandled is single-site (D9)
grep -rln 'ErrorsUnhandled.Add' src/ | wc -l
# expect: 1

# ID-6 fixed
grep -n 'ActivityStatusCode.Unset' src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs
grep -n 'ActivityStatusCode.Error, "Cancelled"' src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs
# expect: first matches, second zero

# Phase 8 visibility invariant holds
grep -E '^public ' src/FrigateRelay.Host/Pipeline/EventPump.cs src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs
# expect: zero

# Excluded packages still excluded
git grep -nE 'App\.Metrics|OpenTracing|Jaeger\.' src/
# expect: zero

# Existing test suites green
dotnet run --project tests/FrigateRelay.Host.Tests -c Release 2>&1 | tail -3
```
