---
phase: 16-v1.3.0-minor
plan: 1.2
wave: 1
dependencies: []
must_haves:
  - "WaitForEntriesAsync(int count, TimeSpan timeout, CancellationToken ct = default) added to CapturingLogger<T> in tests/FrigateRelay.TestHelpers/CapturingLogger.cs."
  - "Polls Entries.Count >= count at 25ms intervals (per CONTEXT-16 OQ-3); throws TimeoutException on expiry."
  - "All 4 fragility sites (EventPumpSpanTests:285, CounterIncrementTests:359, :393, :425) replaced with await logger.WaitForEntriesAsync(...)."
  - "Tightened greppable invariant: `git grep -nE 'Task\\.Delay\\([0-9]' tests/FrigateRelay.Host.Tests/Observability/` returns empty (matches numeric delays only; the 2 Task.Delay(Timeout.Infinite, ct) cancellation-await sites in fake IEventSource stubs survive correctly)."
  - "CHANGELOG.md [Unreleased] ### Internal entry for #22 noting the corrected invariant."
files_touched:
  - tests/FrigateRelay.TestHelpers/CapturingLogger.cs
  - tests/FrigateRelay.Host.Tests/Observability/EventPumpSpanTests.cs
  - tests/FrigateRelay.Host.Tests/Observability/CounterIncrementTests.cs
  - CHANGELOG.md
tdd: false
risk: low
---

# Plan 1.2: Replace Task.Delay polling with WaitForEntriesAsync (#22)

## Context

Issue #22 cleans up 4 fixed-time `Task.Delay(...)` calls in `tests/FrigateRelay.Host.Tests/Observability/` that wait for log emission from `EventPump` / `ChannelActionDispatcher`. They are timing-fragile under CI load. Per CONTEXT-16 D2 (simplified post-research), all 4 sites are **log-record polling** (RESEARCH.md confirms `CapturingLogger<T>` instances are constructed in the same test helpers; the sleeps were waiting for log emission, not metric flush). The fix: a single helper on `CapturingLogger<T>` named `WaitForEntriesAsync` (matching the actual field name `Entries` per RESEARCH.md — not `Records`). OQ-5 / RC-1: the ROADMAP-stated greppable invariant `Task\.Delay` returning empty is **not achievable** without removing the structurally-correct `Task.Delay(Timeout.Infinite, ct)` cancellation-await pattern in fake `IEventSource` stubs (lines 481 + 337). The architect tightens the invariant to numeric delays only and notes the correction in the CHANGELOG. D8 places the entry under `### Internal`.

## Dependencies

None — Wave 1 root. File-disjoint from PLAN-1.1 and PLAN-1.3 except CHANGELOG.md (sequential build dispatch).

## Tasks

### Task 1: Add WaitForEntriesAsync to CapturingLogger<T>
**Files:**
- `tests/FrigateRelay.TestHelpers/CapturingLogger.cs`

**Action:** modify (extension)

**Description:**
1. Add a new public method:
   - Signature: `public async Task WaitForEntriesAsync(int count, TimeSpan timeout, CancellationToken ct = default)`.
   - Implementation: capture `DateTime.UtcNow + timeout` deadline; loop while `Entries.Count < count`; on each iteration check `DateTime.UtcNow >= deadline` and throw `TimeoutException` with a descriptive message including expected vs actual count and the timeout; otherwise `await Task.Delay(TimeSpan.FromMilliseconds(25), ct)` (OQ-3).
   - Cancellation: respect `ct` — propagate `OperationCanceledException` naturally from `Task.Delay`.
2. Keep the polling interval as an internal const (no exposed knob per OQ-3 lean-default).
3. **Self-test (optional but recommended):** if `tests/FrigateRelay.TestHelpers.Tests/` exists, add 2 tests: one that completes when `Entries.Count` is incremented in-flight, one that throws `TimeoutException` when the count is never reached. If no such project exists, add the helper-self-test inside `tests/FrigateRelay.Host.Tests/Observability/` as a private fixture or co-locate with one of the touched test files (low-risk option). Implementer's choice; the load-bearing acceptance is that the 4 site replacements pass deterministically under CI.

**TDD:** false (helper is exercised by its 4 callers in Task 2; an explicit self-test is recommended but not gated)

**Acceptance Criteria:**
- `git grep -n 'public async Task WaitForEntriesAsync' tests/FrigateRelay.TestHelpers/` returns exactly 1 hit.
- `git grep -n ' Records' tests/FrigateRelay.TestHelpers/CapturingLogger.cs` returns no false-positive references — the field remains `Entries`.
- `dotnet build FrigateRelay.sln -c Release` zero warnings.

### Task 2: Replace 4 Task.Delay sites + CHANGELOG
**Files:**
- `tests/FrigateRelay.Host.Tests/Observability/EventPumpSpanTests.cs` (line 285 region)
- `tests/FrigateRelay.Host.Tests/Observability/CounterIncrementTests.cs` (line 359, 393, 425 regions)
- `CHANGELOG.md`

**Action:** modify

**Description:**
1. **Line 285 (EventPumpSpanTests.cs `RunPumpAsync` helper):** Replace `await Task.Delay(400)` with `await capturingLogger.WaitForEntriesAsync(1, TimeSpan.FromSeconds(2))`. Wait signal per RESEARCH.md: pump processes 1 event → `LogMatchedEvent` fires (or `LogPumpStopped` on cancellation — the implementer picks whichever message is the most reliable terminal log; preferred is the post-cancel terminal log so the count is deterministic).
2. **Line 359 (CounterIncrementTests.cs `RunPumpAsync`):** Same shape as line 285. Replace `Task.Delay(400)` with `WaitForEntriesAsync(N, TimeSpan.FromSeconds(2))` where `N` is the expected log-entry count for the configured `BatchSource` event count. Implementer reads the helper's source and the calling test's event count to pick `N`.
3. **Line 393 (CounterIncrementTests.cs `RunPumpAsyncWithSource` w/ `FaultingSource`):** Replace `Task.Delay(300)` with `WaitForEntriesAsync(1, TimeSpan.FromSeconds(2))` waiting for at least 1 `Error`-level entry (the `LogPumpFaulted` emission). If a log-level filter is needed, extend the helper signature with an optional `LogLevel? minLevel = null` parameter — but only if necessary; the simpler "any entry" form is preferred.
4. **Line 425 (CounterIncrementTests.cs `RunDispatcherAsync`):** Replace `Task.Delay(shouldThrow ? 200 : 100)` with `WaitForEntriesAsync(1, TimeSpan.FromSeconds(2))` waiting for the dispatcher's terminal log emission (success → `LogActionSucceeded`; failure → retry logs + `LogActionExhausted`). **RESEARCH.md uncertainty flag:** if the dispatcher's success path emits no log message, fall back to a `MeterListener`-based wait for that one site (the architect explicitly authorizes this fallback for line 425 only — log-record polling is the primary plan). Document the fallback in the commit message if used.
5. **CHANGELOG.md** `[Unreleased]` `### Internal` entry per CONTEXT-16 D8:
   - `- Replaced 4 fixed-time \`Task.Delay\` polling sites in observability tests with deterministic \`CapturingLogger<T>.WaitForEntriesAsync\` (issue #22).`
   - Add a short follow-up sentence noting the ROADMAP invariant correction: `- ROADMAP greppable invariant for #22 corrected to \`git grep -nE 'Task\\.Delay\\([0-9]' tests/FrigateRelay.Host.Tests/Observability/\` (numeric delays only); the 2 \`Task.Delay(Timeout.Infinite, ct)\` cancellation-await sites in fake \`IEventSource\` stubs are structurally correct and intentionally retained.`

**TDD:** false (mechanical site replacement; behavior preserved — existing assertions in the affected tests gate correctness)

**Acceptance Criteria:**
- `git grep -nE 'Task\.Delay\([0-9]' tests/FrigateRelay.Host.Tests/Observability/` returns **empty**.
- `git grep -nE 'Task\.Delay\(Timeout\.Infinite' tests/FrigateRelay.Host.Tests/Observability/` returns exactly 2 hits (the cancellation-await sites — confirms the tightened invariant scope).
- `git grep -n 'WaitForEntriesAsync' tests/FrigateRelay.Host.Tests/Observability/` returns exactly 4 hits (one per replaced site).
- All 151 existing Host tests pass under `dotnet run --project tests/FrigateRelay.Host.Tests -c Release` (no net-new tests from this plan; all-existing-pass framing).
- CHANGELOG `[Unreleased]` `### Internal` block contains both the helper line and the invariant-correction line.

## Verification

```bash
# Build
dotnet build FrigateRelay.sln -c Release

# All Host tests pass (no count change from this plan)
dotnet run --project tests/FrigateRelay.Host.Tests -c Release

# Tightened invariant — numeric delays only
git grep -nE 'Task\.Delay\([0-9]' tests/FrigateRelay.Host.Tests/Observability/   # empty

# Cancellation-await sites preserved (sanity check on the 2 fake-source idioms)
git grep -nE 'Task\.Delay\(Timeout\.Infinite' tests/FrigateRelay.Host.Tests/Observability/   # 2 hits

# Helper used at exactly 4 call sites
git grep -n 'WaitForEntriesAsync' tests/FrigateRelay.Host.Tests/Observability/   # 4 hits
git grep -n 'WaitForEntriesAsync' tests/FrigateRelay.TestHelpers/                 # 1 hit (declaration)
```

## Notes

- **Field name is `Entries`, not `Records`.** The Phase 15 PLAN-1.1 build's "Records did not exist" surprise is the same root cause; the helper is named `WaitForEntriesAsync` to match.
- **CHANGELOG.md is shared with PLAN-1.1 and PLAN-1.3** — sequential build dispatch eliminates merge friction.
- **The greppable invariant is a CORRECTION to the ROADMAP's original wording.** Document this explicitly in the commit message and the CHANGELOG entry so the next architect reviewing ROADMAP understands the scope-tightening rationale.
