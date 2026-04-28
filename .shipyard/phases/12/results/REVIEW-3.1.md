# REVIEW-3.1 — PLAN-3.1: reconcile subcommand + parity-report.md

**Commit:** 1d18b31
**Reviewer:** reviewer-3-1
**Date:** 2026-04-28
**Verdict:** PASS

---

## Stage 1 — Correctness

### F-1 RESOLUTION CHECK (discriminator: @mt vs @i)

PASS. `Reconciler.cs:48` reads `@mt` (the message template property), not `@i`. The code includes an
explanatory comment at line 46 explicitly noting `@i` is a hex Murmur3 hash, NOT the action name.

Action discrimination (`Reconciler.cs:50-55`):
- `mt.StartsWith("BlueIris DryRun", StringComparison.Ordinal)` → "BlueIris"
- `mt.StartsWith("Pushover DryRun", StringComparison.Ordinal)` → "Pushover"

Cross-check against actual plugin LoggerMessage.Define emissions:
- `BlueIrisActionPlugin.cs:25`: template = `"BlueIris DryRun would-execute for camera={Camera} label={Label} event_id={EventId}"` — prefix "BlueIris DryRun" matches exactly.
- `PushoverActionPlugin.cs:114-115`: template = `"Pushover DryRun would-execute for camera={Camera} label={Label} event_id={EventId}"` — prefix "Pushover DryRun" matches exactly.

F-1 fully resolved. No use of `@i` anywhere in reconcile path.

### Reconciler Logic

PASS. `Reconciler.cs` implements:
- `(camera, label, action, bucket-tick)` key tuple for matching — pairs events within the same time bucket.
- Default bucket 60 seconds (per CONTEXT-12 D1 cooldown criterion).
- `missed` = legacy rows with no matching FrigateRelay key.
- `spurious` = FrigateRelay rows with no matching legacy key.
- `matched` = `legacy.Count - missed.Count` (correct).

Exit codes (`Program.cs:64`):
- 0 = parity clean (no missed, no spurious).
- 1 = usage error (via `Fail()` returning 1).
- 2 = parity gap (any missed or spurious present).

Correctly matches the required three-code contract.

### Tests

PASS. `dotnet run --project tests/FrigateRelay.MigrateConf.Tests -c Release --no-build` → **10/10 passed** (4 prior + 6 new).

Six new tests in `ReconcilerTests.cs`:
1. `Reconcile_PerfectMatch_ReturnsZeroMissedAndZeroSpurious` — matched pair
2. `Reconcile_LegacyOnlyAction_ReportsMissedAlert` — missed alert
3. `Reconcile_FrigateRelayOnlyAction_ReportsSpuriousAlert` — spurious alert
4. `Reconcile_DifferentMinuteBucket_TreatedAsDistinctEvents` — cooldown-window-edge (85s gap → different buckets)
5. `Reconcile_LineWithUnrecognizedTemplate_IsSkipped` — non-DryRun lines ignored
6. `RenderMarkdown_ReportWithMissedAndSpurious_ContainsBothTables` — markdown output shape

All required coverage categories present: matched, missed, spurious, cooldown-window edge.
NDJSON fixtures use realistic `@mt` templates with `@i` hex hash values (correct shape).

### parity-report.md

PASS. Template state confirmed — no fabricated parity data. Contains `TBD` placeholders and clear
operator instructions for running the reconcile subcommand after the 48-hour window. Includes
sections: Summary stats block, Missed alerts table, Spurious alerts table, Sign-off. Format matches
the `RenderMarkdown` output shape, ready to be overwritten by the tool.

---

## Stage 2 — Integration

### File disjoint with PLAN-3.2

PASS. Commit `1d18b31` modifies only:
- `tools/FrigateRelay.MigrateConf/Reconciler.cs`
- `tools/FrigateRelay.MigrateConf/Program.cs`
- `tests/FrigateRelay.MigrateConf.Tests/ReconcilerTests.cs`
- `docs/parity-report.md`

No overlap with PLAN-3.2 file set (README, RELEASING, CHANGELOG).

### LoggerMessage source-gen

PASS (N/A for tool). `MigrateConf` is a CLI tool, not a plugin — no `ILogger` usage in the reconciler
or program. The DryRun log messages being parsed are emitted by the plugins, and those already use
`LoggerMessage.Define` (verified above).

### FluentAssertions 6.12.2 pin

PASS. `tests/FrigateRelay.MigrateConf.Tests/FrigateRelay.MigrateConf.Tests.csproj:15` confirms
`Version="6.12.2"`.

### No .Result/.Wait()

PASS. `grep -rn '\.Result\|\.Wait()' tools/FrigateRelay.MigrateConf/` returns no matches.

### No secrets / RFC1918 IPs in source fixtures

PASS. ReconcilerTests.cs fixtures use camera names ("DriveWayHD", "BackYard") and synthetic
timestamps with no IP addresses. The 192.x IPs found are in `bin/Release/` build artifacts from
prior phases (the legacy.conf migration fixture), not in the new test code.

### ConfigSizeParityTest

PASS. `dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build -- --filter "ConfigSizeParityTest"` → **1/1 passed**.

---

## Finding Summary

| Category | Count |
|---|---|
| Critical issues | 0 |
| Minor issues | 0 |
| Notes | 0 |

---

## Verdict

**PASS**

F-1 fully resolved: `@mt` used throughout, `@i` never referenced. Exit codes correct (0/1/2). 10/10 tests pass. FluentAssertions pinned at 6.12.2. No forbidden patterns. parity-report.md is template-only (no fabricated data).
