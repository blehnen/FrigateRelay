---
plan: 1.1
phase: 11
builder: claude-sonnet-4-6 (orchestrator-finished after builder budget cap)
date: 2026-04-28
---

# Build Summary: Plan 1.1 — Test Triage (Wave 1 Gate)

## Status: complete

## Tasks Completed

- **Task 1: `Validator_ShortCircuits_OnlyAttachedAction` log-capture fix** — PASS (commit `dd84185`)
  - Files: `tests/FrigateRelay.IntegrationTests/MqttToValidatorTests.cs`
  - Approach: pivoted from `ILoggerProvider` registration (Phase 9 fix `794a893`) to a Serilog `ILogEventSink`. The Phase 9 workaround was rendered ineffective by Phase 10's `Microsoft.NET.Sdk.Web` pivot — `SerilogLoggerFactory` does not consult DI-registered `ILoggerProvider` instances regardless of registration order.
  - New mechanism: `CapturingSerilogSink : ILogEventSink` translates `Serilog.Events.LogEvent` → `CapturedEntry`, wired via a second `builder.Services.AddSerilog((_, lc) => lc.WriteTo.Sink(captureSink))` call after `HostBootstrap.ConfigureServices`. Last `AddSerilog` registration wins; production sinks intentionally absent in this test factory.
- **Task 2: `TraceSpans_CoverFullPipeline` validator span name assertion fix** — PASS (commit `157bc01`)
  - Files: `tests/FrigateRelay.IntegrationTests/Observability/TraceSpansCoverFullPipelineTests.cs`
  - Approach: 1-line string correction. The test asserted on `validator.codeprojectai.check` (plugin type name) but `ChannelActionDispatcher` actually emits `validator.<instance-name>.check` keyed on the validator's config instance name. The integration test config names the validator `strict-person`, so the correct span name is `validator.strict-person.check`. Added a 2-line comment pointing at the dispatcher convention to prevent re-regression.
  - Production span emission is correct; only the test assertion was wrong.
- **Task 3: Escape-hatch (`[Ignore]` + new ISSUES ID)** — N/A. Did not fire — both target tests fixed cleanly.

## Files Modified

- `tests/FrigateRelay.IntegrationTests/MqttToValidatorTests.cs` — Serilog sink-based capture mechanism (52 insertions, 18 deletions). Net: removed inner `ILogger` plumbing, added `CapturingSerilogSink` + sink-routed `AddSerilog` call.
- `tests/FrigateRelay.IntegrationTests/Observability/TraceSpansCoverFullPipelineTests.cs` — span name assertion (3 insertions, 1 deletion).

## Decisions Made

- **Approach (b) over approach (a) for Task 1.** Resume guidance offered two options: (a) re-apply the Phase 9 `ILoggerProvider` registration if it had been reverted, (b) Serilog `ILogEventSink` if Phase 10 changed the wiring. Builder confirmed via reading current source that the Phase 9 line was still present BUT no longer effective post-Phase-10. Approach (b) was the correct choice.
- **Production sinks omitted from the second `AddSerilog` call** in tests. Trade-off: console/file output during tests goes silent. Acceptable because tests assert on captured entries, not on stdout. Production code paths are unaffected.
- **No new ISSUES ID needed.** Task 3 escape-hatch (`[Ignore]` + new ISSUES entry) didn't fire. ID-22 (Phase 9 `Task.Delay` polling magic delays) and the Phase 9 lessons-learned entry around Serilog clobbering DI providers remain open advisories — not closed by this work since this is a different fix mechanism.

## Issues Encountered

- **Builder context-budget overrun on first dispatch.** First builder invocation spent its full ~35 tool-use budget on Serilog/DI plumbing analysis without writing any artifact. Diagnostically the analysis was correct (it correctly concluded Fix B was needed), but produced no commits or SUMMARY. Same failure mode that has hit every closeout/research agent in this project from Phase 8 onward.
- **Resume succeeded with concrete-action guidance.** Second SendMessage invocation included explicit "stop analyzing, apply, commit" instructions plus two pre-decided fix paths to try in order. Builder applied the staged changes successfully but ran out of budget AGAIN before running tests via the for-loop (sandbox blocked the multi-step bash). Orchestrator (this turn) ran the tests, confirmed green, and split the staged changes into 2 commits matching plan task structure.
- **Phase 9 fix degradation was silent.** The Phase 9 commit `794a893` registered `ILoggerProvider` via `builder.Services.AddSingleton`. Phase 10's `Microsoft.NET.Sdk.Worker` → `Microsoft.NET.Sdk.Web` pivot in PLAN-1.1 changed the host bootstrap path enough that this workaround stopped functioning, but no test enforced the capture mechanism's correctness — the test simply started failing. Lesson: Phase 9 lessons-learned called out exactly this clobbering pattern as a known Serilog behavior; we should treat any Worker-SDK-related changes as potentially destabilizing for `ILoggerProvider` registrations.

## Verification Results

- **Build:** `dotnet build FrigateRelay.sln -c Release` → 0 errors, 0 warnings ✅
- **Targeted tests:**
  - `Validator_ShortCircuits_OnlyAttachedAction` → PASS (1/1, 4.7s)
  - `TraceSpans_CoverFullPipeline` → PASS (1/1, 6.2s)
- **Full integration suite:** `dotnet run --project tests/FrigateRelay.IntegrationTests -c Release --no-build` → **7/7 passed** ✅ (this includes both formerly-failing tests)
- **Other test projects (sample run):**
  - Abstractions: 25/25 ✅
  - Host: 101/101 ✅
  - FrigateMqtt: 18/18 ✅
  - BlueIris: 17/17 ✅
  - Pushover: 10/10 ✅
  - CodeProjectAi: 8/8 ✅
  - FrigateSnapshot: 6/6 ✅
- **Total: 192 passed / 0 failed across 8 test projects.** Wave 1 gate: **CLOSED GREEN.** Wave 2 may begin.

## Wave Gate Status

Per CONTEXT-11 D7: "Wave 2 doc waves START in Wave 2 or later, gated on green tests."
- ✅ Build clean (0 warnings)
- ✅ All test projects green (192/192)
- ✅ Both formerly-failing tests now pass on their own merits (no `[Ignore]`, no escape-hatch)
- **Wave 2 unblocked.**
