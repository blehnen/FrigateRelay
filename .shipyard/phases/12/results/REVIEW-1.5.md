# REVIEW-1.5: NDJSON Audit-Log Sink

**Commit:** 543e639  
**Reviewer:** reviewer-1-5  
**Date:** 2026-04-28  
**Status:** APPROVED WITH WARNINGS

---

## Stage 1 — Correctness

### Checklist
- [x] `Logging:File:CompactJson` opt-in flag — present in appsettings.json (default `false`)
- [x] `HostBootstrap.ApplyLoggerConfiguration` extracted as `internal static` method (Risk #2 testability refactor — confirmed via HostBootstrap.cs grep; method signature present at line ~151)
- [x] CompactJson=true → `CompactJsonFormatter` (NDJSON); false/absent → text format (default unchanged)
- [x] Docker suppression preserved — `environmentName == "Docker"` guard at HostBootstrap.cs:175 still applies to the file sink regardless of CompactJson flag
- [x] Console sink untouched — only file sink branch affected
- [x] `Serilog.Formatting.Compact 2.0.0` added to `FrigateRelay.Host.csproj` as explicit `PackageReference` (confirmed; MIT license, correct version pin)
- [x] 2 new tests in `tests/FrigateRelay.Host.Tests/Logging/CompactJsonFileSinkTests.cs` — both pass; 103/103 Host tests green

### Findings — Stage 1
None. All correctness criteria met.

---

## Stage 2 — Integration

### Checklist
- [x] File-disjoint with peers — commit 543e639 touches only `src/FrigateRelay.Host/HostBootstrap.cs`, `src/FrigateRelay.Host/FrigateRelay.Host.csproj`, `tests/FrigateRelay.Host.Tests/Logging/CompactJsonFileSinkTests.cs`. No overlap with PLAN-1.1/1.2/1.3/1.4 paths.
- [x] Task 3 drop (appsettings.Example.json doc stub) — **acceptable**. Commit message documents the reason: current ratio 56.8% leaves only ~40 chars headroom before ConfigSizeParityTest 60% limit. PLAN-1.5 Task 3 explicitly declared droppable under this condition. No cross-plan touch required.
- [x] LoggerMessage source-gen — no new `ILogger` call sites added in HostBootstrap; existing source-gen patterns unchanged.
- [x] Test baseline — 103/103 Host tests pass (pre-this-commit baseline was 101; 2 new tests added and green).

### Findings — Stage 2

#### WARNING F-1: `@i` vs `@t`/`@mt` field name discrepancy — PLAN-3.1 reconciler will silently skip all NDJSON lines

**Severity:** Medium — functional defect in Wave 3, not Wave 1. Flagged now so PLAN-3.1 builder can correct before implementation.

**Details:**

PLAN-1.5's Dependencies section documents the NDJSON envelope as `"@t"`, `"@m"`, `"@i"` and states "PLAN-3.1 reads exactly these field names." The architect's Risk #3 spec also lists `@i`.

However, `Serilog.Formatting.Compact` (`CompactJsonFormatter`) emits:
- `@t` — ISO-8601 timestamp (correct for PLAN-3.1's `ActionRow.Timestamp`)
- `@mt` — message template string (NOT `@m`; `@m` is rendered message, only emitted when different from `@mt`)
- `@i` — hex-encoded Murmur3 hash of the message template (a deduplication hint, NOT a semantic action name)

PLAN-3.1's `ReadFrigateRelayNdjson` in the plan spec uses `TryGetProperty("@i", ...)` and treats its value as the **action name discriminator** (e.g., `"BlueIrisDryRun"`, `"PushoverDryRun"`). This is incorrect: the actual `@i` value will be a hex hash like `"0a1b2c3d"`, not a plugin name string. The reconciler's filter/match logic will fail to extract action names, producing zero matched pairs for all inputs.

**Confirmed by PLAN-3.1 test fixture in plan doc:**
```json
{"@t":"2026-04-29T12:00:05Z","@m":"DryRun","@i":"BlueIrisDryRun","Camera":"DriveWayHD","Label":"person","EventId":"ev-1"}
```
The fixture assumes `@i` = `"BlueIrisDryRun"` — this value would never appear in real CompactJsonFormatter output.

**Correct approach for PLAN-3.1 builder:**
- Use the `LoggerMessage.Define` named parameter that carries the plugin/action name (e.g., `"Action"` or `"Plugin"` — whatever PLAN-1.1/1.2 emit as the structured property). The NDJSON line will carry it as a top-level JSON property (e.g., `"Action":"BlueIris"`).
- Alternatively, parse `@mt` (message template) and extract the action discriminator from context properties, not from `@i`.
- The `Camera`, `Label`, `EventId` fields are correct — these are emitted as top-level properties by `LoggerMessage.Define` and will appear in NDJSON as specified.

**Action required:** PLAN-3.1 builder must NOT use `@i` as an action-name field. This review cannot change PLAN-3.1 (it hasn't run); flagging for Wave 3 architect attention.

---

#### NOTE F-2: Test second method constructs LoggerConfiguration inline, not via `ApplyLoggerConfiguration`

**Severity:** Informational.

`HostBootstrap_ApplyLoggerConfiguration_When_CompactJsonFlag_True_Writes_Ndjson` builds a `LoggerConfiguration` inline in the test body rather than calling the extracted `ApplyLoggerConfiguration` method directly. This means the test verifies the CompactJsonFormatter behavior in isolation but does not exercise the actual branch logic inside `ApplyLoggerConfiguration`. Coverage of the method's branching logic relies entirely on the first test (`File_Sink_With_CompactJson_Emits_Ndjson_With_Camera_Label_EventId`) plus runtime.

This is a known plan limitation (Task 2 noted the difficulty of hooking into `HostBootstrap`) and is acceptable for Wave 1. No action required unless a future phase adds mutation testing.

---

## Summary

**Verdict:** APPROVED WITH WARNINGS  
**Blocking findings:** 0  
**Warning findings:** 1 (F-1 — `@i` misuse in PLAN-3.1 spec; must fix in Wave 3 before PLAN-3.1 implements)  
**Informational findings:** 1 (F-2 — second test is inline, not a direct `ApplyLoggerConfiguration` call)  
**Test count:** 103/103 Host tests pass (+2 new)  
**Task 3 drop:** Accepted — size-budget guard correctly applied
