# Review: Plan 3.1

## Verdict: MINOR_ISSUES

## Findings

### Critical
None.

### Minor

- **`OpenTelemetry.Exporter.InMemory` version 1.11.2 used instead of plan-spec 1.15.3** — `tests/FrigateRelay.Host.Tests/FrigateRelay.Host.Tests.csproj:26` and `tests/FrigateRelay.IntegrationTests/FrigateRelay.IntegrationTests.csproj:23`. Version 1.11.2 lacks the `AddInMemoryExporter(ICollection<Activity>, Action<ExportProcessorOptions>?)` overload that exposes `ExportProcessorType`, which is why the builder switched to manual `new SimpleActivityExportProcessor(new InMemoryExporter<Activity>(list))` + `AddProcessor(...)`. The pattern is correct; the version divergence is documentation-only.
  - Remediation: either bump both csprojs to 1.15.3 and use the simpler overload, or update the plan to ratify 1.11.2 as the verified version.

- **Integration test does not assert `validator.codeprojectai.check` span parenting** — `tests/FrigateRelay.IntegrationTests/Observability/TraceSpansCoverFullPipelineTests.cs:112–125`. The plan explicitly requires "one `validator.codeprojectai.check` span exists as a child of `action.pushover.execute`." Current test asserts action spans exist but does not assert validator-span existence or `ParentSpanId` against the action span.
  - Remediation: add `var validatorSpan = allSpans.FirstOrDefault(a => a.DisplayName == "validator.codeprojectai.check"); validatorSpan.Should().NotBeNull(...); validatorSpan!.ParentSpanId.Should().Be(pushoverSpan!.SpanId, ...);`.

- **Pre-existing regression: `Validator_ShortCircuits_OnlyAttachedAction` fails due to Wave 2 `AddSerilog` clobbering test capture provider** — `tests/FrigateRelay.IntegrationTests/MqttToValidatorTests.cs:230–234` vs `src/FrigateRelay.Host/HostBootstrap.cs:34–51`. The test registers `CapturingLoggerProvider` via `builder.Logging.AddProvider(capture)`, then `HostBootstrap.ConfigureServices` calls `builder.Services.AddSerilog(...)` which replaces the logging provider pipeline. The `ValidatorRejected` log is never captured → assertion fails. **PLAN-3.1 did not introduce this regression** — Wave 2 did. PLAN-3.1 builder correctly did not modify the pre-existing test (out of scope).
  - Remediation (Wave 2 owner / orchestrator): change `MqttToValidatorTests.BuildHostAsync` to register the capture provider via `builder.Services.AddSingleton<ILoggerProvider>(captureProvider)` AFTER `HostBootstrap.ConfigureServices` — service-collection registration survives `AddSerilog`'s provider replacement. **Orchestrator applying inline fix before phase verification.**

- **Duplicate ID-17 entry remains open in `.shipyard/ISSUES.md`** — second ID-17 entry at lines ~237–255 marked "Open" with the original env-var-fallback text, while the first ID-17 entry (lines ~193–204) is marked CLOSED. Pre-existing housekeeping gap; PLAN-3.1 builder did not introduce. Recommend appending `*[CLOSED 2026-04-27]*` + "Duplicate" note in a future cleanup pass.

### Positive

- **Test count target hit:** Host.Tests 69 → 88 (+19, exceeds ≥84 gate by 4). 9 + 4 + 3 + 3 unit tests across 4 files; 2 integration tests.
- **D5 test split honored:** unit tests in `Host.Tests/Observability/`, integration tests in `IntegrationTests/Observability/`.
- **D8 attribute table fully exercised:** all 5 span names asserted; `event.id` correlation tag verified across the chain; Producer/Consumer/Internal/Server kinds correct.
- **D3 counter dimensions verified:** `events.received` (`camera+label`); `events.matched` (+ `subscription`); `actions.*` (`subscription+action`); `validators.*` (`+validator`); **`errors.unhandled` empty tag set asserted** (D9).
- **D9 enforcement tested:** explicit test that `ErrorsUnhandled` does NOT increment on retry exhaustion (only `ActionsFailed` + `Exhausted`).
- **`ValidateObservability` ID-16 closure tests:** 3 tests cover malformed `Otel:OtlpEndpoint`, malformed `Serilog:Seq:ServerUrl`, and both-valid cases.
- **Integration test `TraceSpans_CoverFullPipeline` satisfies ROADMAP success criterion #1:** 1 root `mqtt.receive` + ≥4 children sharing `TraceId`, correctly parented via `ParentSpanId`.
- **Integration test `Counters_Increment_PerD3_TagDimensions` satisfies ROADMAP success criterion #2:** 1 event → 2 actions → 1 validator produces the exact counter set.
- **TracerProvider flush pattern correct:** `SimpleActivityExportProcessor` for synchronous export + `tracerProvider.ForceFlush(...)` belt-and-suspenders.
- **`CooldownSeconds=1` minimum** — addresses the `MemoryCache` `ArgumentOutOfRangeException` root cause. Documented in SUMMARY.
- **`MeterListener` thread-safety** — `lock (sink)` around list mutation in all `SetMeasurementEventCallback` handlers.
- **`Activity.TagObjects` (not `Activity.Tags`)** consistently used — captures numeric tags.
- **No `class CapturingLogger`** redefinitions in `Observability/` (shared `TestHelpers` used).
- **`OpenTelemetry.Exporter.InMemory`** present only in test csprojs; zero matches in `src/`.
- **No `[LoggerMessage]` source generator** introduced (D6).
- **Build clean** at every commit (warnings-as-errors enforced).
- **Commit prefix** `shipyard(phase-9):` consistent (no `feat(host)` drift).
- **TDD discipline** documented: red-green transitions explicit in each task commit.
- **ID-16 closure correctly performed** in commit `e4028bb` (both duplicate entries).

### Suggestions

- `Task.Delay(400)` polling in `RunPumpAsync` (`EventPumpSpanTests.cs:285`, `CounterIncrementTests.cs:357`) is fragile under load. Replace with `while (activities.Count == 0 && elapsed < 2s) await Task.Delay(20)`.
- `shouldThrow ? 200 : 100` magic delay in `RunDispatcherAsync` (`CounterIncrementTests.cs:420–422`) — same concern; poll instead.
- `tests/FrigateRelay.IntegrationTests/FrigateRelay.IntegrationTests.csproj` lacks a `ProjectReference` to `FrigateRelay.TestHelpers`. New test doesn't need it but proactively mirroring `Host.Tests` pattern would prevent a future PR.

## Summary
Critical: 0 | Minor: 4 | Positive: 19 | Suggestions: 3.

APPROVE — Plan 3.1 correctly delivers the observability test surface and exceeds the ≥84 Host.Tests gate. Three Minor findings (validator-span assertion gap, version mismatch, ID-17 duplicate) are non-blocking. Minor #3 (Wave 2 `AddSerilog` regression) is the only practical blocker for phase verification — orchestrator applying the test-fixture fix inline before dispatching the phase verifier.
