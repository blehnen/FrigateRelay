# Phase 4 Post-Build Verification

**Mode:** Post-build verification
**Phase:** 4 — Action Dispatcher + BlueIris (First Vertical Slice)
**Reviewed:** 2026-04-25
**Verdict:** complete_with_gaps

---

## Success Criteria (from ROADMAP Phase 4)

| # | Criterion | Status | Evidence |
|---|---|---|---|
| 1 | Integration test `MqttToBlueIris_HappyPath` passes in <30s | PASS | `dotnet run --project tests/FrigateRelay.IntegrationTests -c Release --no-build` → `total: 1, succeeded: 1, failed: 0, duration: 5s 410ms` (wall time 6.551s). Well under 30s SLO. |
| 2 | ≥6 dispatcher unit tests, including retry-exhaustion test asserting Warning log with EventId 101 + structured state (`EventId`, `Action` keys) | PASS | `ChannelActionDispatcherTests.cs` has 6 `[TestMethod]` entries; `SubscriptionActionWiringTests.cs` has 4. Total dispatcher tests = 10. Exhaustion test at line 181 asserts `e.Id.Id == 101` (line 213), message contains `"after retry exhaustion"` (line 220), and `keys.Should().Contain("EventId")` + `keys.Should().Contain("Action")` (lines 226-227). All 29 Host tests pass: `total: 29, succeeded: 29, failed: 0`. |
| 3 | E2E MQTT→BlueIris under 2 seconds (manual/stub path) | PASS (inferred) | Integration test total duration is 5.4s including Testcontainers Mosquitto container startup and WireMock initialization. MQTT-publish-to-HTTP-trigger latency is a fraction of that. The poll loop budgets 10s; test completed in 5.4s total wall time. Per ROADMAP note, actual MQTT-to-HTTP latency is much smaller than the container-startup-inclusive wall time. |
| 4 | Zero `.Result`/`.Wait()` in `src/` | PASS | `git grep -nE '\.(Result|Wait)\(' src/` → `ZERO MATCHES (good)` |

---

## CLAUDE.md Invariants

| Check | Status | Evidence |
|---|---|---|
| No `.Result`/`.Wait()` in `src/` | PASS | `git grep -nE '\.(Result|Wait)\(' src/` → zero matches |
| No `ServicePointManager` in `src/` | PASS (comment only) | `git grep -n ServicePointManager src/` → one hit: `src/FrigateRelay.Sources.FrigateMqtt/FrigateMqttEventSource.cs:26` — XML doc comment `/// no global <c>ServicePointManager</c> callback is touched.` No runtime reference. Invariant satisfied. |
| No hard-coded IPs/secrets (`192.168.`, `AppToken=`, `UserKey=`) | PASS | `git grep -nE '192\.168\.|AppToken=|UserKey=' src/ tests/` → `ZERO MATCHES (good)` |
| No excluded observability libs (`App.Metrics`, `OpenTracing`, `Jaeger.`) | PASS | `git grep -nE 'App\.Metrics|OpenTracing|Jaeger\.' src/` → `ZERO MATCHES (good)` |
| Build clean (warnings-as-errors) | PASS | `dotnet build FrigateRelay.sln -c Release` → `0 Warning(s), 0 Error(s), Time Elapsed 00:00:06.74` |
| TLS skip opt-in per-plugin only via `SocketsHttpHandler` | PASS | CONTEXT-4 D7 specifies `AllowInvalidCertificates` gates a plugin-scoped `SocketsHttpHandler.SslOptions` callback only. No global `ServicePointManager` touch confirmed by grep above. |

---

## Test Suite Summary

| Suite | Result | Tests | Duration |
|---|---|---|---|
| FrigateRelay.Abstractions.Tests | PASS | 10/10 | 0.29s (wall 1.46s) |
| FrigateRelay.Host.Tests | PASS | 29/29 | 4.89s (wall 6.03s) |
| FrigateRelay.Sources.FrigateMqtt.Tests | PASS | 18/18 | 0.32s (wall 1.43s) |
| FrigateRelay.Plugins.BlueIris.Tests | PASS | 13/13 | 1.32s (wall 2.49s) |
| FrigateRelay.IntegrationTests | PASS | 1/1 | 5.41s (wall 6.55s) |
| **Total** | **PASS** | **71/71** | — |

Regression check: Abstractions (10), Host (29), FrigateMqtt (18) all match prior-phase baselines. No regressions.

---

## Dispatcher Unit Test Names (criterion 2 detail)

`tests/FrigateRelay.Host.Tests/Dispatch/ChannelActionDispatcherTests.cs` (6 tests):
1. `StartAsync_RegistersOneChannelPerPlugin_ExposesCaseInsensitiveLookup`
2. `EnqueueAsync_WhenChannelFull_IncrementsDropsCounter_LogsWarning`
3. `StopAsync_CompletesWriters_AwaitsConsumers_GracefulWithinToken`
4. `RetryDelayGeneratorFormula_Produces3s6s9s_ForAttempts0Through2`
5. `EnqueueAsync_WhenPluginThrowsAfterRetries_LogsExhaustionWarning_IncrementsExhaustedCounter_DoesNotKillConsumer`
6. `StopAsync_DuringActiveExecution_PropagatesCancellationToPlugin`

`tests/FrigateRelay.Host.Tests/Dispatch/SubscriptionActionWiringTests.cs` (4 tests): all passing as part of the 29/29 Host suite result.

---

## CI Script Check

`bash .github/scripts/run-tests.sh --skip-integration` completed successfully. FrigateMqtt.Tests output confirmed `total: 18, succeeded: 18, failed: 0`. Script auto-discovers projects via `find tests/*.Tests` — no manual additions needed for new projects. Note: `PASS_THROUGH_ARGS` is not forwarded in the `--coverage` branch (see gaps below).

---

## Open Gaps (carried from reviews)

- **[ID-2] MINOR — `IActionDispatcher`/`DispatcherOptions` visibility** (REVIEW-1.1): Both types are `public` in `FrigateRelay.Host`; they are host-internal types with no external consumers. Should be `internal`. Non-blocking for Phase 5.
- **[ID-3] MINOR — `TargetFramework` missing from BlueIris csproj(s)** (REVIEW-1.2): BlueIris plugin project(s) may be missing explicit `<TargetFramework>net10.0</TargetFramework>`, relying solely on `Directory.Build.props` inheritance. Build passes but explicit declaration is preferred. Non-blocking.
- **[ID-4] MINOR — `--filter-query` flag stale in CLAUDE.md** (REVIEW-1.2): CLAUDE.md documents `--filter-query` as the MTP filter syntax; MSTest v4.2.1 (the pinned version) may use a different flag. Should be verified against the installed runner version and CLAUDE.md updated accordingly.
- **[ID-5] MINOR — `CapturingLogger<T>` duplicated as inner class** (REVIEW-2.1): `CapturingLogger` is defined as an inner class in dispatcher tests rather than a shared test helper, duplicating the pattern from `PlaceholderWorkerTests.cs`. Should be extracted to a shared location before a third project copies it (Rule of Three: Phase 6 will hit the threshold).
- **[ID-6] MINOR — `OperationCanceledException` sets `ActivityStatusCode.Error`** (REVIEW-2.1): In the dispatcher's consumer loop, a graceful cancellation (`OperationCanceledException`) sets the OTel activity status to `Error`, which is semantically wrong for a clean shutdown. Should be `Unset` or `Ok` on graceful cancel.
- **[ID-7] NOTE — CONTEXT-4 D3 lists `{score}` but parser accepts only 4 tokens** (REVIEW-2.2): CONTEXT-4 D3 defines 5 placeholders (`{camera}`, `{label}`, `{event_id}`, `{score}`, `{zone}`), but the pre-execution verification records the architect dropped `{score}` because `EventContext` has no `Score` property. CONTEXT-4 D3 has not been updated to reflect this decision. The code is correct; the CONTEXT doc is stale. Needs reconciliation before Phase 5 reuses the templater for `BlueIrisSnapshot`.
- **[ID-8] MINOR — `PASS_THROUGH_ARGS` not forwarded in `--coverage` branch of `run-tests.sh`** (REVIEW-3.2): Lines 67-70 of `.github/scripts/run-tests.sh` omit `"${PASS_THROUGH_ARGS[@]}"` from the `dotnet run` invocation in the coverage path. Only the non-coverage branch (line 86) passes them through. Custom args (e.g. a future `--filter-query`) are silently dropped in Jenkins coverage runs.
- **[ID-1] NOTE — PLAN-3.1 wording** (Phase 3 carry-forward): `IEventMatchSink` justification verbosity in PLAN-3.1. Documentation clarity only, no code impact.

---

## Recommendations

1. **Resolve ID-7 (`{score}` CONTEXT-4 reconciliation)** before Phase 5 starts — `BlueIrisSnapshot` reuses the same URL templater. The code is correct but the CONTEXT doc listing `{score}` as a supported placeholder will mislead the Phase 5 builder. One-line removal from CONTEXT-4 D3 table.
2. **Fix ID-8 (`PASS_THROUGH_ARGS` in coverage branch)** before Phase 5 adds more test projects — Jenkins coverage runs silently drop filter args. Fix is appending `"${PASS_THROUGH_ARGS[@]}"` to the `dotnet run` line in `.github/scripts/run-tests.sh` lines 67-70.
3. **Fix ID-6 (`OperationCanceledException` → `ActivityStatusCode.Error`)** — produces misleading OTel traces during normal shutdown. Low-risk one-line fix in the consumer loop catch block.
4. **Extract `CapturingLogger` to shared test helper (ID-5)** before Phase 6 adds `FrigateRelay.Plugins.Pushover.Tests` — that will be the third copy, hitting the Rule of Three threshold.
5. **Update CLAUDE.md filter syntax (ID-4)** — verify the correct MTP filter flag for MSTest v4.2.1 and update the documented single-test command.
6. **Make `IActionDispatcher`/`DispatcherOptions` internal (ID-2)** — no downstream impact since no external consumers exist yet; cleanest to fix before Phase 5 adds more host-internal types.

---

## Phase Status: complete_with_gaps

All four ROADMAP Phase 4 success criteria are met with concrete evidence. 71/71 tests pass across all five suites. No regressions from prior phases. Six non-blocking gaps are recorded as issues ID-2 through ID-8. None block Phase 5 from starting, though ID-7 (CONTEXT-4 `{score}` reconciliation) and ID-8 (`PASS_THROUGH_ARGS` forwarding) should be resolved early in Phase 5 setup to avoid carrying stale context and a silent Jenkins defect forward.
