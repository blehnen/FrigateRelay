# Review: PLAN-1.2 — WaitForEntriesAsync (#22)

## Verdict: PASS

---

## Stage 1 — Spec Compliance

### Must-Have 1: `WaitForEntriesAsync(int count, TimeSpan timeout, CancellationToken ct = default)` added to `CapturingLogger<T>`
- Status: PASS
- Evidence: `tests/FrigateRelay.TestHelpers/CapturingLogger.cs:27` — exact signature `public async Task WaitForEntriesAsync(int count, TimeSpan timeout, CancellationToken ct = default)` is present. `git grep -n 'public async Task WaitForEntriesAsync' tests/FrigateRelay.TestHelpers/` returns exactly 1 hit (verified by Grep).

### Must-Have 2: Polls `Entries.Count >= count` at 25ms intervals; throws `TimeoutException` on expiry
- Status: PASS
- Evidence: `CapturingLogger.cs:7` — `private const int PollIntervalMs = 25;`. Loop body at lines 30–36: `while (Entries.Count < count)` → deadline check → `throw new TimeoutException(...)` → `await Task.Delay(TimeSpan.FromMilliseconds(PollIntervalMs), ct)`. Polling interval is a private const (no exposed knob, per OQ-3). TimeoutException message includes expected count, actual count, and timeout.

### Must-Have 3: All 4 fragility sites replaced
- Status: PASS with plan-authorized deviation
- Evidence: Three sites use `WaitForEntriesAsync` directly:
  - `EventPumpSpanTests.cs:286` — `await logger.WaitForEntriesAsync(1, TimeSpan.FromSeconds(2))`
  - `CounterIncrementTests.cs:361` — `await logger.WaitForEntriesAsync(1, TimeSpan.FromSeconds(2))`
  - `CounterIncrementTests.cs:395` — `await logger.WaitForEntriesAsync(1, TimeSpan.FromSeconds(2))`
- The 4th site (`RunDispatcherAsync`) uses the plan-authorized MeterListener `TaskCompletionSource` fallback at lines 428–451 in `CounterIncrementTests.cs`. PLAN-1.2 Task 2 Step 4 explicitly authorizes this fallback because the dispatcher success path emits no log message. The fallback is fully documented inline with a multi-line comment citing issue #22 and PLAN-1.2.

### Must-Have 4: Tightened greppable invariant — `git grep -nE 'Task\.Delay\([0-9]' tests/FrigateRelay.Host.Tests/Observability/` returns empty
- Status: PASS
- Evidence: Grep executed against that directory returns no matches.

### Must-Have 5: `Task.Delay(Timeout.Infinite, ct)` sites preserved (sanity check)
- Status: PASS
- Evidence: Grep returns exactly 2 hits:
  - `EventPumpSpanTests.cs:338` — `FakeSource.ReadEventsAsync`
  - `CounterIncrementTests.cs:507` — `BatchSource.ReadEventsAsync`

### Must-Have 6: CHANGELOG `[Unreleased]` `### Internal` entry with both lines
- Status: PASS
- Evidence: `CHANGELOG.md` lines 15–17. First line documents the helper switch and names all four terminal states in the MeterListener fallback. Second line documents the ROADMAP invariant correction with the exact tightened grep command.

### Task 1 AC: field remains `Entries`, no `Records` references
- Status: PASS
- Evidence: Grep for `Records` in `CapturingLogger.cs` returns no matches.

### Task 2 AC: `WaitForEntriesAsync` exactly 3 call sites in observability directory
- Status: PASS (with note)
- Evidence: Grep returns 3 actual call sites + 1 comment reference. The plan's "exactly 4 hits" AC was written assuming no fallback; the SUMMARY documents the resolution (3 logger calls + 1 authorized MeterListener fallback = 4 deterministic waits). This is plan-conformant per the authorized deviation.

### Task 2 AC: `validators.rejected` in MeterListener terminal set
- Status: PASS
- Evidence: `CounterIncrementTests.cs:429–434` — `terminalNames` HashSet contains `"frigaterelay.actions.succeeded"`, `"frigaterelay.actions.failed"`, `"frigaterelay.actions.exhausted"`, and `"frigaterelay.validators.rejected"`. The validator-short-circuit path (`Verdict.Fail`) emits `validators.rejected` and never increments any `actions.*` counter; omitting it would have caused `ValidatorsRejected_Tags_SubscriptionActionValidator_OnFail` to time out. The SUMMARY documents this was caught pre-commit.

### CLAUDE.md invariants
- **No `.Result`/`.Wait()`:** Grep clean on `CapturingLogger.cs`. No blocking calls introduced.
- **Field name `Entries`:** Confirmed, no `Records` confusion.
- **No Newtonsoft.Json, no ServicePointManager, no global TLS bypass:** Not applicable to test-helper changes; no new imports introduced.
- **`WaitAsync` on `TaskCompletionSource.Task` (line 451):** Correct BCL usage — `Task.WaitAsync(TimeSpan, CancellationToken)` is the non-blocking async timeout; no `.Wait()` or `.Result` anti-pattern.

---

## Stage 2 — Code Quality

### Critical
None.

### Minor

- **`Entries` list has no thread-safety guarantee** — `CapturingLogger.cs:9`, `List<LogEntry>` is not synchronized. The `Log<TState>` method (line 13) writes to `Entries` from whatever thread the logger is called on; `WaitForEntriesAsync` reads `Entries.Count` from the polling thread. On .NET, `List<T>.Count` is not guaranteed to be a single atomic read on all architectures, and concurrent `Add` + `Count` reads on `List<T>` are undefined behavior. The existing tests pass because the logging thread and the polling thread are in practice the same async continuation context, but this is a latent hazard.
  - Remediation: change `public List<LogEntry> Entries { get; } = new();` to `public System.Collections.Concurrent.ConcurrentBag<LogEntry>` — or keep `List<T>` but add a `lock (_lock)` in `Log<TState>` and `WaitForEntriesAsync`. `ConcurrentBag` does not preserve insertion order but `Count` is safe; if order matters for test assertions, use a `lock`. The `BuildListener` helper in `CounterIncrementTests.cs` already uses `lock (sink)` on its own list (line 278) — this is the right pattern to mirror.

- **`RunPumpAsync` waits for count=1 but some callers receive multi-event inputs** — `CounterIncrementTests.cs:361`, `EventPumpSpanTests.cs:286`. `EventsMatched_Increments_PerMatchedSubscription` passes 2 subscriptions that both match; after the first log entry the test cancels and asserts 2 `events.matched` increments. If the pump processes the single event and emits both the `LogMatchedEvent` (sub_A) and `LogMatchedEvent` (sub_B) log entries in rapid succession before `WaitForEntriesAsync(1, ...)` returns, there is no race. But if the pump emits only 1 log entry for the event (e.g. a single `LogPumpStopped` on the cancellation path), the metric assertions on `events.matched` could fire before the counter increments are flushed. In practice this is fine because `listener.RecordObservableInstruments()` is called after `RunPumpAsync` returns (pump has fully stopped), but the wait count (1) does not semantically gate "all processing for this event is done." Harmless under current implementation; note for future maintainers.
  - Remediation: document in the helper's XML comment that count=1 is used as a "pump has started emitting" signal, not a "pump has finished all processing" gate. No code change required — the pump stop (`await pump.StopAsync`) provides the real synchronization barrier; the logger wait only prevents the immediate `cts.CancelAsync()` from racing with the pump startup.

### Positive

- **TimeoutException message is exemplary** (`CapturingLogger.cs:33–34`): `"WaitForEntriesAsync: expected {count} log entries but only {Entries.Count} were recorded within {timeout}."` — includes all three diagnostic fields. CI failures will be immediately actionable.
- **MeterListener fallback is correctly documented** (lines 421–427 in `CounterIncrementTests.cs`): the inline comment cites the issue number, the plan step, and enumerates all terminal states with their semantic meaning. Future maintainers can understand the design without reading the phase docs.
- **`validators.rejected` inclusion caught pre-commit** by the full test run. Demonstrates the "always run tests before staging" discipline from CLAUDE.md and SUMMARY lesson seeds. No regression reached the reviewer.
- **`PollIntervalMs` as private const** (line 7, `CapturingLogger.cs`): correct per OQ-3 lean-default — no API surface exposed, no future temptation to tune per-callsite.
- **Consistent `TimeSpan.FromSeconds(2)` timeout across all 4 waits**: uniform, generous relative to the 25ms poll interval, conservative relative to the 5s outer `CancellationTokenSource`. Well-chosen defaults.

---

## Findings Summary

The implementation satisfies every spec requirement. The single plan-authorized deviation (MeterListener fallback for `RunDispatcherAsync`) is correctly applied, fully documented, and includes the `validators.rejected` terminal state needed for the validator-short-circuit test path. The only non-trivial code quality concern is a latent thread-safety hazard on `CapturingLogger<T>.Entries` (`List<T>` written and read across threads without synchronization). The existing `BuildListener` in `CounterIncrementTests` already uses `lock(sink)` on its own list, making the fix obvious. All other findings are documentation/minor robustness notes with no code risk.

<!-- context: turns=10, compressed=no, task_complete=yes -->
