# REVIEW-1.1: BlueIris DryRun Flag

**Commit:** 68774d7
**Plan:** PLAN-1.1
**Reviewer:** reviewer-1-1
**Date:** 2026-04-28

---

## Stage 1 — Correctness

### DryRun default = false
PASS. `BlueIrisOptions.DryRun` is a `bool` property with no explicit initializer — C# defaults to `false`. Confirmed in `BlueIrisOptions.cs`.

### Early-return path (DryRun=true)
PASS.
- Guard at top of `ExecuteAsync`: `if (_options.DryRun) { LogDryRun(...); return; }` — no HttpClient call.
- `LogDryRun` uses `LoggerMessage.Define<string, string, string>` (source-gen pattern, CA1848 compliant).
- EventId: `new EventId(203, "BlueIrisDryRun")` — correct.
- Return is a bare `return` (void path), so the method returns `Task.CompletedTask` implicitly from the `async Task` signature. Clean.

### Dispatcher counters still tick
PASS. Looking at `ChannelActionDispatcher.ConsumeAsync`:
```
await plugin.ExecuteAsync(item.Context, shared, ct).ConfigureAwait(false);
actionActivity?.SetTag("outcome", "success");
DispatcherDiagnostics.ActionsSucceeded.Add(1, actionTags);
```
`ExecuteAsync` returns normally when `DryRun=true` (no exception thrown, no special return value). The dispatcher's success path is unconditional after the `await` — `ActionsSucceeded` increments. The `actions.executed.total` (dispatched) counter is incremented at `EnqueueAsync` time, before the channel hop. Both counters tick correctly on DryRun.

### Tests — 2 new test methods
PASS.
- `ExecuteAsync_DryRunTrue_DoesNotCallHttpClientAndLogsWouldExecute` — uses `InvocationCountingHandler`, asserts `handler.SendInvocations == 0` and log entry `Id.Name == "BlueIrisDryRun"` contains camera/label/eventId. Correct.
- `ExecuteAsync_DryRunFalse_CallsHttpClientAsBefore` — uses `StubHandler(HttpStatusCode.OK)`, asserts `handler.SendInvocations == 1` and no `BlueIrisDryRun` log entry. Correctly validates the default (false) path.
- Both use `CapturingLogger<T>` from `FrigateRelay.TestHelpers` per CLAUDE.md convention.
- Test names use `Method_Condition_Expected` underscore convention — correct.

---

## Stage 2 — Integration

### File scope (file-disjoint)
PASS. `git show 68774d7 --stat` shows exactly 3 files:
- `src/FrigateRelay.Plugins.BlueIris/BlueIrisActionPlugin.cs`
- `src/FrigateRelay.Plugins.BlueIris/BlueIrisOptions.cs`
- `tests/FrigateRelay.Plugins.BlueIris.Tests/BlueIrisActionPluginTests.cs`

No overlap with PLAN-1.2 (Pushover), 1.3 (tools/tests), 1.4 (docs), 1.5 (Host). Fully disjoint.

### EventId 203 uniqueness
PASS. `grep -rn "EventId.*= *203" src/` returns exactly one match:
`src/FrigateRelay.Plugins.BlueIris/BlueIrisActionPlugin.cs` — the new `LogDryRun` definition. No collision.

### LoggerMessage source-gen pattern
PASS. Uses `LoggerMessage.Define<string, string, string>` with `static readonly Action<ILogger, ...>` field — matches CLAUDE.md CA1848 requirement.

### No committed secrets / hard-coded IPs
PASS. Test uses `http://example.invalid/...` (RFC 2606 invalid TLD). No real IPs, no credentials.

---

## Findings

| # | Severity | Finding |
|---|----------|---------|
| — | — | No issues found |

---

## Verdict

**PASS**

All correctness criteria met. File scope is clean. EventId 203 is unique. LoggerMessage pattern is correct. Tests cover both DryRun=true and DryRun=false paths. Dispatcher success counters unaffected.
