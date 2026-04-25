# Phase 4 Plan Verification

**Mode:** Spec compliance (pre-execution)
**Reviewed:** 2026-04-25
**Verdict:** READY

---

## A. Deliverable coverage

| ROADMAP deliverable | Owned by | Status |
|---|---|---|
| 1. `IActionDispatcher.cs` with `ValueTask EnqueueAsync(...)` | PLAN-1.1 Task 1 | PASS |
| 2. `ChannelActionDispatcher` skeleton (channel-per-plugin, itemDropped, drop counter, graceful shutdown) | PLAN-1.1 Task 2 | PASS |
| 3. `DispatcherDiagnostics` with Meter "FrigateRelay" and `frigaterelay.dispatch.drops` counter | PLAN-1.1 Task 1 | PASS |
| 4. `BlueIrisOptions`, `BlueIrisUrlTemplate`, `BlueIrisActionPlugin`, registrar with Polly 3/6/9s | PLAN-1.2 Tasks 1–2 + PLAN-2.2 Tasks 1–2 | PASS |
| 5. `EventPump` dispatch wiring + `SubscriptionOptions.Actions[]` | PLAN-3.1 Task 1 | PASS |
| 6. `Program.cs` registers dispatcher + BlueIris registrar + startup validation (S2) | PLAN-3.1 Task 2 | PASS |
| 7. Action-name → plugin map (fail-fast on unknown names per D2 + S2) | PLAN-3.1 Task 2 | PASS |
| 8. Dispatcher unit tests (≥6) covering drops, exhaustion, cancellation | PLAN-1.1 Task 3 + PLAN-2.1 Tasks 2–3 | PASS |
| 9. BlueIris unit tests via WireMock (success, retry, exhaustion, TLS opt-in) | PLAN-1.2 Task 3 + PLAN-2.2 Task 3 | PASS |
| 10. Integration test project scaffolding (FrigateRelay.IntegrationTests) | PLAN-3.2 Task 1 | PASS |
| 11. `MosquittoFixture` with Testcontainers + anonymous-auth | PLAN-3.2 Task 1 | PASS |
| 12. `MqttToBlueIris_HappyPath` end-to-end test (<30s) | PLAN-3.2 Task 2 | PASS |
| 13. CI integration (run-tests.sh `--skip-integration`, ci.yml Windows skip, Jenkinsfile precondition doc) | PLAN-3.2 Task 3 | PASS |

**Result:** All 13 deliverables are assigned to specific plans and tasks. No orphaned deliverables.

---

## B. Decision honor check (CONTEXT-4 D1–D7)

| Decision | Plan enforcement | Evidence |
|---|---|---|
| **D1 — Per-plugin channel topology** | PLAN-1.1 Task 2 | `Dictionary<IActionPlugin, Channel<DispatchItem>>` keyed by plugin instance; 2 consumer tasks per channel. Isolation rationale preserved. |
| **D2 — Subscription Actions[] array + fail-fast** | PLAN-3.1 Task 1 + PLAN-3.1 Task 2 | `SubscriptionOptions.Actions` added; `Program.cs` validates all action names at startup against registered plugins; unknown names throw with diagnostic listing unknown + registered set. |
| **D3 — BlueIris URL template with allowlist** | PLAN-1.2 Task 2 | `BlueIrisUrlTemplate` parser with `FrozenSet<string>` containing exactly `{camera, label, event_id, zone}` (Q1 resolution: `{score}` dropped per EventContext verification). Test #3 encodes the deferral. |
| **D4 — Validators parameter empty in Phase 4** | PLAN-1.1 Task 1 + PLAN-3.1 Task 1 | `IActionDispatcher.EnqueueAsync` includes `IReadOnlyList<IValidationPlugin>` parameter; EventPump passes `Array.Empty<IValidationPlugin>()`; XML doc notes Phase 7 population. |
| **D5 — Channel capacity 256, configurable per-plugin, DropOldest** | PLAN-1.1 Task 2 + PLAN-2.1 Task 1 | `BoundedChannelOptions(256)` with `FullMode = DropOldest`; `DispatcherOptions.PerPluginQueueCapacity` dict for overrides; `BlueIrisOptions.QueueCapacity` wired via Program.cs (PLAN-3.1 Task 2). |
| **D6 — Drop telemetry: counter + warning log** | PLAN-1.1 Task 2 + PLAN-2.1 Task 1 | `itemDropped` callback emits `frigaterelay.dispatch.drops` counter + `LogWarning` with event_id + action + capacity in structured state. Retry-exhaustion emits separate `frigaterelay.dispatch.exhausted` counter + `LogWarning` from consumer catch. Both tagged with `action`. |
| **D7 — Polly v8 HttpClient + 3/6/9s delays** | PLAN-2.2 Task 2 | `AddResilienceHandler("BlueIris-retry", builder => builder.AddRetry(...))` with `DelayGenerator = args => TimeSpan.FromSeconds(3 * (args.AttemptNumber + 1))`. PLAN-2.1 Task 2 encodes test to verify formula. |

**Result:** All 7 decisions are honored by the plans. RESEARCH.md corrections (built-in `itemDropped` callback, Q1 resolution on `{score}`) are reflected in PLAN-1.1 and PLAN-1.2 respectively.

---

## C. Plan structural rules

### Checklist per plan

| Plan | Tasks | Files touched | Verification section | Wave deps | Status |
|---|---|---|---|---|---|
| **PLAN-1.1** | 3 | 6 files ✓ | ✓ (build, tests, grep) | None ✓ | PASS |
| **PLAN-1.2** | 3 | 6 files ✓ | ✓ (build, tests, grep) | None ✓ | PASS |
| **PLAN-2.1** | 3 | 5 files ✓ | ✓ (build, tests, grep, counter/EventId) | [1.1] ✓ | PASS |
| **PLAN-2.2** | 3 | 5 files ✓ | ✓ (build, tests, grep) | [1.2] ✓ | PASS |
| **PLAN-3.1** | 3 | 5 files ✓ | ✓ (build, tests, smoke) | [2.1, 2.2] ✓ | PASS |
| **PLAN-3.2** | 3 | 7 files ✓ | ✓ (build, integration, flags) | [2.1, 2.2] ✓ | PASS |

**All plans:** ≤3 tasks, `Files touched` lists present, `Verification` sections with runnable commands present. Wave dependencies are acyclic and point only backward (Wave 1 → 1, Wave 2 → 1, Wave 3 → 2). **PASS**.

---

## D. Cross-plan file conflicts

### Wave 1 (PLAN-1.1 + PLAN-1.2)

| File | Plan-1.1 | Plan-1.2 | Conflict? |
|---|---|---|---|
| `src/FrigateRelay.Host/Dispatch/IActionDispatcher.cs` | CREATE | — | No |
| `src/FrigateRelay.Host/Dispatch/DispatchItem.cs` | CREATE | — | No |
| `src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs` | CREATE | — | No |
| `src/FrigateRelay.Host/Dispatch/DispatcherDiagnostics.cs` | CREATE | — | No |
| `src/FrigateRelay.Host/FrigateRelay.Host.csproj` | MODIFY | — | No |
| `tests/FrigateRelay.Host.Tests/Dispatch/ChannelActionDispatcherTests.cs` | CREATE | — | No |
| `src/FrigateRelay.Plugins.BlueIris/BlueIrisOptions.cs` | — | CREATE | No |
| `src/FrigateRelay.Plugins.BlueIris/BlueIrisUrlTemplate.cs` | — | CREATE | No |
| `src/FrigateRelay.Plugins.BlueIris/FrigateRelay.Plugins.BlueIris.csproj` | — | CREATE | No |
| `tests/FrigateRelay.Plugins.BlueIris.Tests/BlueIrisUrlTemplateTests.cs` | — | CREATE | No |
| `tests/FrigateRelay.Plugins.BlueIris.Tests/FrigateRelay.Plugins.BlueIris.Tests.csproj` | — | CREATE | No |
| `FrigateRelay.sln` | — | MODIFY (add 2 projects) | No |

**Result:** No conflicts. Wave-1 plans are fully parallelizable.

### Wave 2 (PLAN-2.1 + PLAN-2.2)

| File | Plan-2.1 | Plan-2.2 | Conflict? |
|---|---|---|---|
| `src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs` | MODIFY (fill consumer) | — | No |
| `src/FrigateRelay.Host/Dispatch/DispatcherOptions.cs` | MODIFY (PerPluginQueueCapacity) | — | No |
| `src/FrigateRelay.Host/FrigateRelay.Host.csproj` | — (no new refs) | MODIFY (add packages) | No |
| `tests/FrigateRelay.Host.Tests/Dispatch/ChannelActionDispatcherTests.cs` | MODIFY (add 3 tests) | — | No |
| `tests/FrigateRelay.Host.Tests/FrigateRelay.Host.Tests.csproj` | — | — | No |
| `src/FrigateRelay.Plugins.BlueIris/BlueIrisActionPlugin.cs` | — | CREATE | No |
| `src/FrigateRelay.Plugins.BlueIris/PluginRegistrar.cs` | — | CREATE | No |
| `src/FrigateRelay.Plugins.BlueIris/FrigateRelay.Plugins.BlueIris.csproj` | — | MODIFY (add packages) | No |
| `tests/FrigateRelay.Plugins.BlueIris.Tests/BlueIrisActionPluginTests.cs` | — | CREATE | No |
| `tests/FrigateRelay.Plugins.BlueIris.Tests/FrigateRelay.Plugins.BlueIris.Tests.csproj` | — | MODIFY (add WireMock) | No |

**Result:** No conflicts. Wave-2 plans are fully parallelizable (PLAN-2.1 is a prerequisite for PLAN-2.2 only via the contract `IActionDispatcher`, which PLAN-1.1 already delivers).

### Wave 3 (PLAN-3.1 + PLAN-3.2)

| File | Plan-3.1 | Plan-3.2 | Conflict? |
|---|---|---|---|
| `src/FrigateRelay.Host/Configuration/SubscriptionOptions.cs` | MODIFY | — | No |
| `src/FrigateRelay.Host/EventPump.cs` | MODIFY | — | No |
| `src/FrigateRelay.Host/Program.cs` | MODIFY (wire dispatcher + registrars + validation) | — | No |
| `src/FrigateRelay.Host/HostBootstrap.cs` | — | CREATE (refactored from Program.cs) | No; PLAN-3.2 Task 2 creates this; PLAN-3.1 Task 2 delegates to it |
| `tests/FrigateRelay.Host.Tests/Dispatch/SubscriptionActionWiringTests.cs` | CREATE | — | No |
| `tests/FrigateRelay.Host.Tests/EventPumpDispatchTests.cs` | CREATE | — | No |
| `tests/FrigateRelay.IntegrationTests/FrigateRelay.IntegrationTests.csproj` | — | CREATE | No |
| `tests/FrigateRelay.IntegrationTests/Fixtures/MosquittoFixture.cs` | — | CREATE | No |
| `tests/FrigateRelay.IntegrationTests/MqttToBlueIrisSliceTests.cs` | — | CREATE | No |
| `.github/scripts/run-tests.sh` | — | MODIFY (add `--skip-integration` flag) | No |
| `.github/workflows/ci.yml` | — | MODIFY (Windows skip) | No |
| `Jenkinsfile` | — | MODIFY (doc-comment) | No |
| `FrigateRelay.sln` | — | MODIFY (add IntegrationTests project) | No |
| `src/FrigateRelay.Host/FrigateRelay.Host.csproj` | — | MODIFY (`<InternalsVisibleTo>` for IntegrationTests) | No |

**Result:** No conflicts. PLAN-3.2's `HostBootstrap.cs` extraction is a cross-plan refactoring of PLAN-3.1's work, but the plans coordinate via the "builder picks merge order" note in PLAN-3.2 Task 2. Files are disjoint except for the intentional `Program.cs` → `HostBootstrap` delegation.

---

## E. Success-criteria coverage

### ROADMAP Phase 4 quantitative gates

| Gate | Ownership | Evidence in plans |
|---|---|---|
| "≥ 1 end-to-end test (`MqttToBlueIris_HappyPath`) passing in under 30 seconds" | PLAN-3.2 Task 2 | Test method exists; `[Timeout(30_000)]` attribute; assertion on `wireMock.LogEntries.Count == 1`. Acceptance criterion requires <30s wall-clock. |
| "≥ 6 dispatcher unit tests" | PLAN-1.1 Task 3 (3 tests) + PLAN-2.1 Task 3 (3 tests) | PLAN-1.1: tests for channel construction, drop callback + counter + log, graceful shutdown. PLAN-2.1: retry-delay formula, retry-exhaustion + cancellation (2 tests). Total ≥6. |
| "Running the host manually fires the trigger URL within 2 seconds" | PLAN-3.2 Task 2 | Integration test; manual smoke check deferred. No automated assertion (inherent timing sensitivity). Documented as builder-time verification. |
| "`git grep -nE '\.(Result|Wait)\(' src/` returns zero matches" | All plans' Verification sections | Each plan includes this grep. No `.Result` or `.Wait()` allowed in source or tests. |
| Structured log for exhaustion includes event id | PLAN-2.1 Task 1 | `LogRetryExhausted` defined via `LoggerMessage.Define<string, string>` with message template `event_id={EventId} action={Action}...`. PLAN-2.1 Task 3 Test A explicitly asserts structured-state keys `EventId` and `Action` present. |

**Result:** All quantitative gates are owned and verifiable. Test counts are explicitly promised (6+), and structured-log assertions are encoded at the test level.

---

## F. CLAUDE.md invariant cross-check

| Invariant | Plan enforcement | Evidence |
|---|---|---|
| No `.Result`/`.Wait()` in source or tests | All plans' verification sections | Each plan includes `git grep -nE '\.(Result\|Wait)\('` checking both `src/` and `tests/` directories. |
| `frigaterelay.*` metric prefix | PLAN-1.1 Task 1 + PLAN-2.1 Task 1 | Counter names: `frigaterelay.dispatch.drops` and `frigaterelay.dispatch.exhausted`. Both declared in `DispatcherDiagnostics`. |
| `ActivitySource` name is `"FrigateRelay"` | PLAN-1.1 Task 1 | `DispatcherDiagnostics.ActivitySource = new("FrigateRelay")`. PLAN-2.1 Task 1 uses it in `ConsumeAsync` with parent-context linking. |
| `Meter` name is `"FrigateRelay"` | PLAN-1.1 Task 1 | `DispatcherDiagnostics.Meter = new("FrigateRelay")`. Counters created via `Meter.CreateCounter<long>(...)`. |
| No `ServicePointManager` anywhere | All plans' verification sections | Each plan includes `git grep -n "ServicePointManager" src/`. Must return zero matches. |
| TLS opt-in per-plugin only (via `SocketsHttpHandler.SslOptions.RemoteCertificateValidationCallback`) | PLAN-2.2 Task 2 | `ConfigurePrimaryHttpMessageHandler` builds `SocketsHttpHandler`; sets callback **only if** `opts.AllowInvalidCertificates == true`. Never touches global `ServicePointManager`. Test #6 verifies the callback assignment. |
| Plugins must NOT depend on Host | PLAN-1.2 Task 1 + PLAN-2.2 Task 2 | `FrigateRelay.Plugins.BlueIris.csproj` has no `ProjectReference` to `FrigateRelay.Host`. Acceptance criterion verifies with `git grep`. Per-plugin capacity wiring deferred to PLAN-3.1 (Program.cs, host-side). |
| Abstractions must NOT have third-party runtime deps | PLAN-1.2 Task 1 | New plugin projects reference only `FrigateRelay.Abstractions`; HTTP/resilience packages are in plugin csproj, not abstractions. |
| No hard-coded IPs/hostnames (including comments) | All plans' verification sections | Each plan includes `git grep -nE '192\.168\.|10\.0\.|http://[a-zA-Z0-9.-]+:[0-9]+'` on source. Testcontainers/WireMock use dynamic hostnames/ports. |
| `<InternalsVisibleTo>` MSBuild item form (not assembly attribute) | PLAN-1.2 Task 1 + PLAN-2.2 Task 2 + PLAN-3.2 Task 2 | `FrigateRelay.Host.csproj` already has `<InternalsVisibleTo Include="FrigateRelay.Host.Tests" />`; PLAN-1.2 adds `<InternalsVisibleTo Include="FrigateRelay.Plugins.BlueIris.Tests" />`; PLAN-3.2 Task 2 adds `<InternalsVisibleTo Include="FrigateRelay.IntegrationTests" />`. All MSBuild item form. |
| FluentAssertions pinned to 6.12.2 | PLAN-1.2 Task 3 + PLAN-2.2 Task 3 + PLAN-3.2 Task 1 | Acceptance criteria in each test plan specify exact version pin in csproj. Verification sections include `grep Version="6.12.2"`. |
| MSTest v3 + Microsoft.Testing.Platform + MTP runner (OutputType=Exe) | All test plans | All test csproj scaffolds include `<OutputType>Exe</OutputType>` and MSTest v3 + MTP package refs. Invoked via `dotnet run`, NOT `dotnet test`. |

**Result:** All CLAUDE.md invariants are explicitly enforced by the plans. No violations detected.

---

## G. Architect-call validation (4 items from architect summary)

| Call | Plan reflection | Status |
|---|---|---|
| **1. `{score}` placeholder dropped** | PLAN-1.2 Task 2 decision states: "EventContext carries `[EventId, Camera, Label, Zones, StartedAt, RawPayload, SnapshotFetcher]` — no `Score` property." Q1 resolution: drop `{score}` from allowlist. FrozenSet contains exactly `{camera, label, event_id, zone}`. Test #3 encodes deferral. | **PASS** — decision honored and encoded in tests. |
| **2. `frigaterelay.dispatch.exhausted` counter confirmed** | PLAN-2.1 Task 1 states Q2 resolution: counter name is `frigaterelay.dispatch.exhausted`, tagged with `action`, emitted from consumer catch block. PLAN-1.1 Task 1 declares both `Drops` and `Exhausted` counters in `DispatcherDiagnostics`. | **PASS** — counter name and location finalized. |
| **3. New `tests/FrigateRelay.Plugins.BlueIris.Tests/` project** | PLAN-1.2 Task 3 states: "new `tests/FrigateRelay.Plugins.BlueIris.Tests/` project, matching Phase-3 precedent of one test project per source project." Acceptance criterion requires both csproj files exist and tests pass. | **PASS** — matches per-source-project precedent. |
| **4. `DispatchItem` is `readonly record struct`** | PLAN-1.1 Task 1 shows exact struct definition: `internal readonly record struct DispatchItem(EventContext Context, IActionPlugin Plugin, IReadOnlyList<IValidationPlugin> Validators, Activity? Activity)`. | **PASS** — struct form confirmed. |

**Result:** All 4 architect calls are validated in the plan set.

---

## Issues found

### Non-blocking findings (logged for follow-up, not blocking verdict)

**Issue 1 — PLAN-3.1 Task 2 foot-gun (BlueIris registrar always active)**

**Severity:** CAUTION  
**Plan:** PLAN-3.1 Task 2  
**Description:** The plan adds `new FrigateRelay.Plugins.BlueIris.PluginRegistrar()` unconditionally to the registrar list. Because the registrar calls `.ValidateOnStart()` on `BlueIrisOptions`, a host with **no BlueIris config section and no subscription using BlueIris** will fail at startup with a missing-template error. This is a usability regression — a user running a minimal config with only FrigateMqtt + no plugins initially installed shouldn't pay validation taxes for plugins they don't use.

**Mitigation in plan:** PLAN-3.1 Task 2 notes this risk and proposes a guard: only add the registrar if `builder.Configuration.GetSection("BlueIris").Exists()`. The builder should apply this guard (documented in the notes as a recommendation). This pattern generalizes for future plugins.

**Impact:** Low — only affects startup with minimal configs. The acceptance criteria already note this must be tested ("running the host with no appsettings.json still starts successfully").

---

**Issue 2 — PLAN-3.2 Task 2 HostBootstrap refactoring cross-plan ripple**

**Severity:** CAUTION  
**Plan:** PLAN-3.2 Task 2  
**Description:** The integration test requires a `HostBootstrap.ConfigureServices(builder)` helper to be extracted from `Program.cs` so both the test and the real app share the same wiring path. PLAN-3.2 Task 2 notes: "if PLAN-3.1 has already shipped, this becomes a follow-up refactor commit owned by PLAN-3.2." This is a minor cross-plan ordering dependency if the plans ship sequentially rather than in parallel. The extraction is small (~30 lines) and low-risk, but the builder must be aware of the ordering.

**Impact:** Low — procedural, not architectural. The acceptance criteria capture the requirement.

---

## Final verdict

**Verdict: READY**

All 13 deliverables are assigned and accounted for. All 7 context decisions (D1–D7) are honored. All plans follow structural rules (≤3 tasks, files listed, verification sections present, acyclic dependencies). No cross-plan file conflicts within or across waves. All ROADMAP success criteria (both quantitative gates and qualitative invariants) are owned by specific plans with verifiable acceptance criteria. All CLAUDE.md invariants are explicitly enforced. The four architect calls are validated in the plan set. Two non-blocking findings (registrar foot-gun, HostBootstrap refactoring ripple) are documented for awareness but do not prevent execution.

The plan set is **specification-complete and ready for builder execution**.

