---
plan: 1.1
phase: 11
reviewer: claude-sonnet-4-6 (orchestrator-finished from agent dump)
date: 2026-04-28
---

# Review: Plan 1.1 — Test Triage (Wave 1 Gate)

## Verdict: PASS

Two non-blocking Important items identified; no Critical findings.

## Stage 1 — Correctness

### Critical
None.

### Important
None at correctness level.

### Positive
- **Task 1 — `CapturingSerilogSink` correctness:** Translates `LogEvent.Properties["EventId"]` (a `StructureValue` with `Id` int + `Name` string), `SourceContext` (string property, quote-stripped via `Trim('"')`), all 6 Serilog level mappings to `LogLevel`, and `RenderMessage()` for the message body. All emission patterns covered.
- **Task 2 — Span name verification:** `src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs:219` emits `$"validator.{validator.Name.ToLowerInvariant()}.check"`. The integration test config at `TraceSpansCoverFullPipelineTests.cs:302` sets `["Validators:strict-person:Type"] = "CodeProjectAi"`, making the instance name `strict-person`. Assertion `validator.strict-person.check` matches emission exactly. 1-line fix is correct and minimal.
- **Task 3 — Not exercised:** No `[Ignore]` attributes added; no new ISSUES.md entries created. Both target tests fixed on their own merits.

## Stage 2 — Integration

### Critical
None.

### Important

1. **`CapturingSerilogSink` is a private nested class, not shared via `FrigateRelay.TestHelpers`.** Currently only `MqttToValidatorTests` uses it, so duplication isn't yet a concern. CLAUDE.md convention (post-Phase-6 dedup of `CapturingLogger<T>` from 4 copies into `FrigateRelay.TestHelpers`) is the precedent. If a second test class adds Serilog capture, extract this sink + its `CapturedEntry` companion to `tests/FrigateRelay.TestHelpers/` following the established pattern.
   - **Remediation:** no immediate action; tracked as latent. Promote to TestHelpers on first second-consumer.

2. **`Validator_Pass_BothActionsFire` capture wiring is silent-pass.** Line 129 asserts `.NotContain(e => e.EventId.Name == "ValidatorRejected")`. An empty `Entries` bag (broken sink) would still satisfy this — the assertion is vacuously true. The test would go green even if log capture stopped working, masking a future regression of the same Phase 9 / Phase 10 / Phase 11 capture-mechanism degradation pattern.
   - **Remediation:** before the negative check, add `captureProvider.Entries.Should().NotBeEmpty("capture sink must have received at least one log entry from the dispatcher")` as a sentinel. Converts silent-pass into meaningful evidence that the sink mechanism is actually working in this test path. Non-blocking — can land in Wave 2 or as a follow-up.

### Suggestions (non-blocking, fyi only)

- `CapturingSerilogSink` primary-constructor parameter name `sink` shadows nothing problematic; flagged purely for stylistic awareness. No change needed.
- The `REVIEW-3.1 Important #2` comment in `TraceSpansCoverFullPipelineTests.cs:122-123` references a Phase 9 review file that's inside `.shipyard/phases/` and not part of the contributor index. The new comment alongside it (pointing to the dispatcher convention) is self-contained and sufficient. Optional: drop the historical-review reference in a future cleanup pass.

### Positive
- **Convention adherence:** test method naming uses underscores (`Method_Condition_Expected`), `<InternalsVisibleTo>` not used (test is at its proper boundary), no `_var` rename hacks.
- **No cross-plan conflicts possible** since Wave 1 has only this plan, and `grep -rn "CapturingLoggerProvider" tests/` confirms no other consumers were affected.
- **Phase 9 failure mode addressed structurally.** The Phase 9 fix (`builder.Services.AddSingleton<ILoggerProvider>`) was a workaround that broke under Phase 10's SDK pivot. The new Serilog `ILogEventSink` mechanism is fundamentally more robust because it integrates at Serilog's own pipeline rather than relying on `ILoggerFactory` consulting DI providers — a behavior Serilog never guaranteed. This fix should survive future bootstrap changes that the prior workaround would not have.
- **Test results green:** orchestrator-run full test suite (192/192 across 8 projects) confirms zero regressions.

## Verdict reasoning

Both commits cleanly address the documented Phase 9 regressions. Task 1's Serilog-sink approach is the architecturally correct fix (vs the Phase 9 workaround). Task 2 is a 1-line assertion correction with a clarifying comment. The two Important items are forward-facing observations (latent dedup, silent-pass guard) that don't block Wave 2; they're worth tracking but not worth holding up the gate.

**Wave 1 gate: GREEN. Wave 2 unblocked.**

Critical: 0 · Important: 2 · Suggestions: 2 · Positive: 4
