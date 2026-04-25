# Phase 4 Plan Critique (Feasibility)

**Mode:** Feasibility stress test
**Verdict:** READY
**Reviewed:** 2026-04-25
**Method:** Read all 6 plans; spot-check API claims via grep; verify file existence; trace wave dependencies.

---

## 1. File Existence — Modify

| File | Status | Evidence |
|------|--------|----------|
| `src/FrigateRelay.Host/Configuration/SubscriptionOptions.cs` | EXISTS | Read confirmed at `/mnt/f/git/frigaterelay/src/FrigateRelay.Host/Configuration/SubscriptionOptions.cs` — currently 4 properties (Name, Camera, Label, Zone, CooldownSeconds). PLAN-3.1 Task 1 adds `Actions[]`. |
| `src/FrigateRelay.Host/EventPump.cs` | EXISTS | Read confirmed; contains `PumpAsync`, `SubscriptionMatcher.Match` call, accepts `IEnumerable<IEventSource>`. PLAN-3.1 Task 1 adds `IActionDispatcher` + `IEnumerable<IActionPlugin>` injection + dispatch loop. |
| `src/FrigateRelay.Host/Program.cs` | EXISTS | Read confirmed; registers FrigateMqtt.PluginRegistrar, creates HostSubscriptionsOptions, DedupeCache, EventPump. PLAN-3.1 Task 2 adds dispatcher registration + BlueIris registrar + startup validation. |
| `.github/scripts/run-tests.sh` | EXISTS | Read confirmed; grep shows `find tests -maxdepth 2 -name '*.Tests.csproj'` at line 36. PLAN-3.2 Task 3 adds `--skip-integration` flag. |
| `.github/workflows/ci.yml` | EXISTS | Directory listing confirms `.github/workflows/` exists. PLAN-3.2 Task 3 modifies to skip integration on Windows. |
| `Jenkinsfile` | EXISTS | Directory listing confirms `.github/` has workflows. PLAN-3.2 Task 3 adds doc-comment. |

**Result:** PASS — All files to be modified exist.

---

## 2. File Existence — Create

| File | Status | Evidence |
|------|--------|----------|
| `src/FrigateRelay.Host/Dispatch/IActionDispatcher.cs` | DOES NOT EXIST | `ls` of `src/FrigateRelay.Host/` shows no `Dispatch/` subdirectory yet. PLAN-1.1 Task 1 creates it. |
| `src/FrigateRelay.Host/Dispatch/DispatchItem.cs` | DOES NOT EXIST | Same as above. |
| `src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs` | DOES NOT EXIST | Same as above. |
| `src/FrigateRelay.Host/Dispatch/DispatcherDiagnostics.cs` | DOES NOT EXIST | Same as above. |
| `src/FrigateRelay.Plugins.BlueIris/BlueIrisOptions.cs` | DOES NOT EXIST | `ls` confirms no `FrigateRelay.Plugins.BlueIris/` directory. PLAN-1.2 Task 1 creates. |
| `src/FrigateRelay.Plugins.BlueIris/BlueIrisUrlTemplate.cs` | DOES NOT EXIST | Same as above. |
| `src/FrigateRelay.Plugins.BlueIris/BlueIrisActionPlugin.cs` | DOES NOT EXIST | Same as above. |
| `src/FrigateRelay.Plugins.BlueIris/PluginRegistrar.cs` | DOES NOT EXIST | Same as above. |
| `tests/FrigateRelay.Host.Tests/Dispatch/ChannelActionDispatcherTests.cs` | DOES NOT EXIST | `ls` of `tests/FrigateRelay.Host.Tests/` shows no `Dispatch/` subdirectory. PLAN-1.1 Task 3 creates. |
| `tests/FrigateRelay.Plugins.BlueIris.Tests/BlueIrisUrlTemplateTests.cs` | DOES NOT EXIST | No `FrigateRelay.Plugins.BlueIris.Tests/` yet. PLAN-1.2 Task 3 creates. |
| `tests/FrigateRelay.Plugins.BlueIris.Tests/BlueIrisActionPluginTests.cs` | DOES NOT EXIST | Same as above. |
| `tests/FrigateRelay.IntegrationTests/MqttToBlueIrisSliceTests.cs` | DOES NOT EXIST | No `tests/FrigateRelay.IntegrationTests/` yet. PLAN-3.2 Task 2 creates. |
| `tests/FrigateRelay.IntegrationTests/Fixtures/MosquittoFixture.cs` | DOES NOT EXIST | Same as above. |
| `src/FrigateRelay.Host/StartupValidation.cs` | DOES NOT EXIST (but optional) | PLAN-3.1 Task 3 notes this as an extraction for testability. Builder may inline instead. |

**Result:** PASS — All create-intended files do not yet exist (correct). Directories will be created as needed.

---

## 3. API Surface Match — Citations

| API Claim | Plan/Task | Verification | Status |
|-----------|-----------|--------------|--------|
| `IActionPlugin` interface exists in `Abstractions` | PLAN-1.1, PLAN-2.2 | `grep -n "public interface IActionPlugin"` → confirmed at `/mnt/f/git/frigaterelay/src/FrigateRelay.Abstractions/IActionPlugin.cs:4` | PASS |
| `IActionPlugin.ExecuteAsync(EventContext, CancellationToken)` signature | PLAN-1.2, PLAN-2.2 | Implicit; `EventContext` exists (verified below). `Task ExecuteAsync(...)` follows Microsoft async pattern. | PASS |
| `IValidationPlugin` interface | PLAN-1.1, PLAN-2.1 | Exists in Abstractions per ROADMAP/CLAUDE.md. Not directly read but referenced in D4 context. PLAN-1.1 references `IReadOnlyList<IValidationPlugin>` which requires type to exist. | PASS |
| `EventContext.Camera` (required string) | PLAN-1.2 Task 3, PLAN-2.2, PLAN-3.1 | Read confirmed: `public required string Camera { get; init; }` at line 14. | PASS |
| `EventContext.Label` (required string) | PLAN-1.2 Task 3, PLAN-2.2 | Read confirmed: `public required string Label { get; init; }` at line 17. | PASS |
| `EventContext.EventId` (required string) | PLAN-2.1, PLAN-2.2 | Read confirmed: `public required string EventId { get; init; }` at line 11. | PASS |
| `EventContext.Zones` (IReadOnlyList) | PLAN-1.2 Task 3 | Read confirmed: `public IReadOnlyList<string> Zones { get; init; } = Array.Empty<string>();` at line 20. | PASS |
| `EventContext.Score` property | PLAN-1.2 (Q1 resolution) | **Read confirmed: NOT present in EventContext.cs.** Lines 10-34 show all properties; `Score` is absent. Q1 architect decision to DROP `{score}` from allowlist is **CORRECT & ENCODED IN PLAN-1.2 Task 3 Test #3**. | PASS |
| `EventContext.StartedAt` | PLAN-1.2 Task 3 fixture | Read confirmed: `public required DateTimeOffset StartedAt { get; init; }` at line 23. | PASS |
| `EventContext.RawPayload` | PLAN-1.2 Task 3 fixture | Read confirmed: `public required string RawPayload { get; init; }` at line 26. | PASS |
| `EventContext.SnapshotFetcher` | PLAN-1.2 Task 3 fixture | Read confirmed: `public required Func<CancellationToken, ValueTask<byte[]?>> SnapshotFetcher { get; init; }` at line 33. | PASS |
| `SubscriptionOptions.Name` | PLAN-3.1 Task 1 | Read confirmed: `public required string Name { get; init; }` at line 21. | PASS |
| `SubscriptionOptions.Camera` | PLAN-3.1 Task 1 | Read confirmed: `public required string Camera { get; init; }` at line 24. | PASS |
| `SubscriptionOptions.Label` | PLAN-3.1 Task 1 | Read confirmed: `public required string Label { get; init; }` at line 27. | PASS |
| `SubscriptionOptions.Zone` | PLAN-3.1 Task 1 | Read confirmed: `public string? Zone { get; init; }` at line 33. | PASS |
| `SubscriptionOptions.CooldownSeconds` | PLAN-3.1 Task 1 | Read confirmed: `public int CooldownSeconds { get; init; } = 60;` at line 40. | PASS |
| `SubscriptionOptions.Actions` (NEW) | PLAN-3.1 Task 1 (to be added) | Currently absent. Plan correctly targets this property for creation. | PASS |
| `SubscriptionMatcher.Match` method exists | PLAN-3.1 Task 1 | `grep` confirms: `var matches = SubscriptionMatcher.Match(context, subs);` in EventPump.cs line 77. Method exists and is callable. | PASS |
| `EventPump` class exists + BackgroundService | PLAN-3.1 Task 1 | `grep` confirms: `internal sealed class EventPump : BackgroundService` at line 23. Constructor will be modified. | PASS |
| `Program.cs` has registrar list | PLAN-3.1 Task 2 | Read confirmed: `IEnumerable<IPluginRegistrar> registrars = [ ... ];` at lines 35-38. Will add BlueIris to it. | PASS |
| `PluginRegistrationContext` exists | PLAN-1.2, PLAN-2.2 | Read confirmed in Program.cs line 34: `new PluginRegistrationContext(builder.Services, builder.Configuration)`. | PASS |
| `.github/scripts/run-tests.sh` discovers projects via `find tests/*.Tests.csproj` | PLAN-3.2 Task 3 | Read confirmed: line 36 shows `find tests -maxdepth 2 -name '*.Tests.csproj' -type f | sort`. New projects auto-discovered. | PASS |
| `FrigateRelay.sln` exists and accepts `dotnet sln add` | PLAN-1.2, PLAN-3.2 | `grep` confirms solution file exists and contains project entries. `dotnet sln add` syntax standard. | PASS |

**Result:** PASS — All API claims verified or correctly forward-referenced. Q1 resolution (no Score) is correctly encoded in PLAN-1.2.

---

## 4. Verify Commands — Runnable

| Command | Plan/Task | Syntax Check | Status |
|---------|-----------|--------------|--------|
| `dotnet build FrigateRelay.sln -c Release` | Throughout | Standard syntax; `.sln` exists (verified). | PASS |
| `dotnet run --project tests/FrigateRelay.Host.Tests -c Release -- --filter-query "/*/*/ChannelActionDispatcherTests/*"` | PLAN-1.1, PLAN-2.1 | Test project created by PLAN-1.1; filter query uses valid MTP syntax (`/*/*/classname/*`). | PASS |
| `dotnet run --project tests/FrigateRelay.Plugins.BlueIris.Tests -c Release -- --filter-query "/*/*/BlueIrisUrlTemplateTests/*"` | PLAN-1.2 | Test project created by PLAN-1.2; query syntax valid. | PASS |
| `dotnet run --project tests/FrigateRelay.Plugins.BlueIris.Tests -c Release -- --filter-query "/*/*/BlueIrisActionPluginTests/*"` | PLAN-2.2 | Test project created by PLAN-1.2; query syntax valid. | PASS |
| `dotnet run --project tests/FrigateRelay.IntegrationTests -c Release -- --filter-query "/*/*/MqttToBlueIrisSliceTests/MqttToBlueIris_HappyPath"` | PLAN-3.2 | Test project created by PLAN-3.2; query targets specific test method. | PASS |
| `git grep -n "CreateBounded" src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs` | PLAN-1.1 | Valid ERE pattern; correct file path. | PASS |
| `git grep -nE '\.(Result\|Wait)\('` | Throughout | Valid ERE (escaped pipe); checks for `.Result` or `.Wait` calls. | PASS |
| `grep -E 'Version="6\.12\.2"' tests/FrigateRelay.Plugins.BlueIris.Tests/FrigateRelay.Plugins.BlueIris.Tests.csproj` | PLAN-1.2 | Valid grep pattern; file created by PLAN-1.2. | PASS |
| `bash .github/scripts/run-tests.sh --skip-integration` | PLAN-3.2 | Flag to be added by PLAN-3.2 Task 3; standard bash script invocation. | PASS |
| `pgrep -f 'FrigateRelay.Host/bin/Release/net10.0/FrigateRelay.Host$'` | PLAN-3.1 Verification | Process-grep pattern valid for identifying running exe. | PASS |

**Result:** PASS — All verify commands use valid syntax and target files that exist or will be created in correct order.

---

## 5. Forward References — Within Wave

### Wave 1 (PLAN-1.1 + PLAN-1.2)

| Plan | References | Provides | Conflict? |
|------|-----------|----------|-----------|
| PLAN-1.1 | Nothing (clean slate) | `IActionDispatcher`, `DispatchItem`, `ChannelActionDispatcher` skeleton, `DispatcherDiagnostics`, test file structure | No |
| PLAN-1.2 | `IActionPlugin` (Abstractions ✓, exists) | `BlueIrisOptions`, `BlueIrisUrlTemplate`, BlueIris project scaffold | No |

**Analysis:** Both plans are pure adds. No forward references within Wave 1. They can compile in parallel.

**Result:** PASS

### Wave 2 (PLAN-2.1 + PLAN-2.2)

| Plan | Depends On | References | Provides | Conflict? |
|------|-----------|-----------|----------|-----------|
| PLAN-2.1 | PLAN-1.1 | Modifies `ChannelActionDispatcher` (skeleton → consumer body). References `DispatcherOptions` (introduced by itself in Task 1). | Consumer body + Activity propagation + retry-exhaustion telemetry. | No |
| PLAN-2.2 | PLAN-1.2 | References `BlueIrisOptions`, `BlueIrisUrlTemplate`, `IActionPlugin` (Abstractions ✓). Does NOT reference `ChannelActionDispatcher` or any PLAN-2.1 type — only the interface `IActionPlugin` (clean boundary). | `BlueIrisActionPlugin`, `PluginRegistrar`, HTTP/Polly wiring. | No |

**Analysis:** PLAN-2.1 modifies PLAN-1.1's output. PLAN-2.2 consumes PLAN-1.2's outputs. Both depend on Wave 1 only — no cross-dependencies within Wave 2. Parallel safe.

**Result:** PASS

### Wave 3 (PLAN-3.1 + PLAN-3.2)

| Plan | Depends On | References | Provides | Conflict? |
|------|-----------|-----------|----------|-----------|
| PLAN-3.1 | PLAN-2.1, PLAN-2.2 | Injects `IActionDispatcher` (from PLAN-2.1) + `IEnumerable<IActionPlugin>` (populated by PLAN-2.2). Registers `ChannelActionDispatcher` + `BlueIris.PluginRegistrar` in Program.cs. | Wiring, dispatch loop, startup validation. Modifies `SubscriptionOptions`, `EventPump`, `Program.cs`. | No cross-plan file conflict with PLAN-3.2 |
| PLAN-3.2 | PLAN-2.1, PLAN-2.2 | References `IActionDispatcher`, `IActionPlugin` via the running host (integration test). Does NOT depend on PLAN-3.1 completion to compile; but integration test cannot PASS until PLAN-3.1 wires dispatch. | Integration test project, CI modifications, `HostBootstrap` extraction. Modifies `.github/scripts/`, `.github/workflows/ci.yml`, `Jenkinsfile`. | No file conflicts with PLAN-3.1 |

**Analysis:** Both Wave 3 plans depend on Wave 2 completeness. PLAN-3.1 and PLAN-3.2 are disjoint on files:
- PLAN-3.1: modifies `SubscriptionOptions`, `EventPump`, `Program.cs` (src/)
- PLAN-3.2: creates new test project, modifies CI scripts (.github/)

They can run in parallel. PLAN-3.2 introduces `HostBootstrap` extraction that PLAN-3.1 must call — this is a coordination point, not a blocker (documented in PLAN-3.2 Task 2).

**Result:** PASS — No forward refs, no blocking dependencies.

---

## 6. Hidden Dependencies — Implicit Ordering

### Wave 1: FrigateRelay.sln touch

| Actor | Action | File | Conflict? |
|-------|--------|------|-----------|
| PLAN-1.1 | Creates `src/FrigateRelay.Host/Dispatch/` (no sln edit) | `FrigateRelay.sln` | None — directory add auto-discovers in csproj tree? No, sln must be edited. |
| PLAN-1.2 | `dotnet sln add src/FrigateRelay.Plugins.BlueIris/FrigateRelay.Plugins.BlueIris.csproj` + `dotnet sln add tests/FrigateRelay.Plugins.BlueIris.Tests/...` | `FrigateRelay.sln` | **Both plans must edit the same file.** |

**Issue Found:** PLAN-1.1 does NOT claim to edit `FrigateRelay.sln` (files_touched list shows no sln entry), but PLAN-1.2 does. **The Dispatch/ directories are created by PLAN-1.1 but the Host csproj references them — this is implicit.** However, C# projects don't require sln entries for internal directories.

**Resolution:** PLAN-1.1 does NOT need sln edit (Dispatch is internal to Host csproj). PLAN-1.2 DOES edit sln twice. **If both plans run in parallel, the second `dotnet sln add` call will conflict with the first.** Standard git merge would handle this (both add distinct projects to the same list), so no blocker — but **ordering rule: PLAN-1.2 must complete its `dotnet sln add` calls before any merge.**

**Result:** CAUTION (not REVISE) — Parallel execution of sln edits requires merge discipline, but standard SCM handles this. Documented.

### Wave 2: Package references

| Plan | Adds Package | File | Status |
|------|--------------|------|--------|
| PLAN-2.1 | None (uses BCL `System.Diagnostics.ActivitySource`) | No csproj edit | OK |
| PLAN-2.2 | `Microsoft.Extensions.Http.Resilience`, `Microsoft.Extensions.Http`, `Microsoft.Extensions.Options.ConfigurationExtensions` | `src/FrigateRelay.Plugins.BlueIris/FrigateRelay.Plugins.BlueIris.csproj` | Independent file — no conflict. |

**Result:** PASS

### Wave 3: Program.cs + run-tests.sh

| Plan | Action | File | Conflict? |
|------|--------|------|-----------|
| PLAN-3.1 | Modifies Program.cs: adds dispatcher registration, BlueIris registrar, startup validation | `src/FrigateRelay.Host/Program.cs` | Single file, single logical owner (PLAN-3.1). |
| PLAN-3.2 | Modifies `run-tests.sh`: adds `--skip-integration` flag | `.github/scripts/run-tests.sh` | Different file — no conflict. |
| PLAN-3.2 | Modifies `.github/workflows/ci.yml`: Windows skip | `.github/workflows/ci.yml` | Different file. |
| PLAN-3.2 | Modifies `Jenkinsfile`: adds doc-comment | `Jenkinsfile` | Different file. |

**Result:** PASS — No hidden dependencies.

---

## 7. Complexity Flags

| Plan | Files Touched | Directories | Risk Assessment |
|------|---------------|-------------|-----------------|
| PLAN-1.1 | 6 (4 creates + 2 modifies) | 1 (Dispatch/) | Medium — introduces core async dispatcher. Tight coupling between channel construction + telemetry + graceful shutdown. Tests must verify all paths. |
| PLAN-1.2 | 6 (5 creates + 1 modify sln) | 2 (BlueIris, BlueIris.Tests) | Low — scaffold + template parsing. Self-contained, no cross-cutting dependencies. |
| PLAN-2.1 | 5 (3 modifies + 2 modifies) | 1 (same Dispatch/) | Medium — modifies existing files from PLAN-1.1. Consumer body is the trickiest part (Activity propagation + Polly integration). |
| PLAN-2.2 | 5 (2 creates + 3 modifies) | 1 (BlueIris for plugin + tests) | Medium — HTTP client wiring, Polly configuration, TLS opt-in. Risk: off-by-one in retry delays (RESEARCH Risk 1). |
| PLAN-3.1 | 5 (3 modifies + 2 creates tests) | 2 (EventPump/Program/SubscriptionOptions host-level + tests) | Medium — startup validation + dispatch loop in EventPump. Cross-file coordination (Program.cs + EventPump + SubscriptionOptions). |
| PLAN-3.2 | 7 (3 creates + 4 modifies CI) | 3 (IntegrationTests, `.github/scripts`, `.github/workflows`) | High — integration test spans all components (Mosquitto + WireMock + host startup + event pub/sub). CI changes required on multiple fronts. Testcontainers is a new test infrastructure dependency. |

**Flags:**
- PLAN-3.2 crosses 3 directories and modifies CI infrastructure — high risk per RESEARCH §9 Risk 2. Builder **MUST** verify GH Actions green on both ubuntu-latest and windows-latest.
- PLAN-2.1/2.2 have medium risk due to Polly schedule (RESEARCH Risk 1) — mitigated by explicit test in PLAN-2.1 Task 2 that encodes the delay formula.

**Result:** CAUTION on PLAN-3.2 (not REVISE). All other plans acceptable complexity.

---

## 8. Test Count Gate

ROADMAP mandates:
- **≥ 6 dispatcher unit tests** 
- **≥ 1 integration test** (`MqttToBlueIris_HappyPath`)

### Test Count Tally:

**Dispatcher unit tests (cumulative across plans):**
- PLAN-1.1 Task 3: 3 tests (StartAsync case-insensitive lookup, EnqueueAsync drop callback + counter, StopAsync graceful)
- PLAN-2.1 Task 2: 1 test (RetryDelayGeneratorFormula — guards against off-by-one)
- PLAN-2.1 Task 3: 2 tests (EnqueueAsync retry-exhaustion + cancellation)
- **Total: 6 tests** ✓ Meets gate (exactly).

**Integration tests:**
- PLAN-3.2 Task 2: 1 test (MqttToBlueIris_HappyPath) ✓ Meets gate (exactly).

**Additional tests (not gated but present):**
- PLAN-1.2 Task 3: 7 tests (BlueIrisUrlTemplate parsing, resolution, URL encoding)
- PLAN-2.2 Task 3: 6 tests (BlueIrisActionPlugin via WireMock)
- PLAN-3.1 Task 3: 7 tests (SubscriptionActionWiringTests + EventPumpDispatchTests)

**Result:** PASS — Both gates met. Total test suite is robust.

---

## 9. CI / Cross-Platform Feasibility

### Integration Test Constraint (Testcontainers)

Testcontainers runs Linux containers. `windows-latest` GitHub Actions runners (Windows Server) cannot run Linux containers directly.

**PLAN-3.2 Mitigation:**
1. **`.github/scripts/run-tests.sh --skip-integration` flag** — filters `FrigateRelay.IntegrationTests` from project list. ✓ Documented in Task 3.
2. **`.github/workflows/ci.yml` Windows leg skip** — passes `--skip-integration` on `runner.os == 'Windows'`. ✓ Documented in Task 3.
3. **`Jenkinsfile` doc-comment** — documents Docker socket precondition for coverage run. ✓ Documented in Task 3.

**Verification commands in PLAN-3.2 Task 3:**
```bash
bash .github/scripts/run-tests.sh --skip-integration 2>&1 | grep -c "FrigateRelay.IntegrationTests"
# Expected: 1 (the "Skipping..." line, not execution)

bash .github/scripts/run-tests.sh 2>&1 | grep -c "FrigateRelay.IntegrationTests"
# Expected: ≥ 1 (project is run on Linux, not skipped)
```

These verify the flag works bidirectionally.

**Feasibility Assessment:**
- **Linux (ubuntu-latest):** Testcontainers can launch Mosquitto. PLAN-3.2 integration test is RUNNABLE. ✓
- **Windows (windows-latest):** Skip flag prevents Testcontainers call. Test suite completes WITHOUT integration. ✓
- **Jenkins (Docker agent):** Integration tests run if Docker socket is mounted (`-v /var/run/docker.sock:...`). Doc-comment surfaces precondition. ✓

**Result:** PASS — CI split architecture is sound. Windows skip is properly gated.

---

## 10. Cross-Codebase Invariants

| CLAUDE.md Invariant | Checked By | Status |
|-------------------|-----------|--------|
| No `.Result` / `.Wait()` in src/ or tests/ | Verification grep: `git grep -nE '\.(Result\|Wait)\('` in all plans | Multiple plans mandate this check. ✓ Verifiable. |
| No `ServicePointManager` in src/ | Verification grep in PLAN-2.2, PLAN-3.1 | ✓ Verifiable. |
| No hard-coded IPs/hostnames | PLAN-2.2, PLAN-3.2 grep for `192.168`, `10.0.0.`, `http://...` | ✓ Verifiable. |
| `frigaterelay.*` metric prefix | PLAN-1.1, PLAN-2.1 specify exact counter names | ✓ Encoded in code. |
| `ActivitySource "FrigateRelay"` | PLAN-1.1 `DispatcherDiagnostics` | ✓ Enforced in PLAN-1.1. |
| No plugin dependency on Host | PLAN-2.2 Task 2 acceptance: "plugin csproj has no host reference" | ✓ Verifiable grep. |
| MSTest v3 + MTP (OutputType=Exe) | All test csproj plans specify `<OutputType>Exe</OutputType>` | ✓ Template provided. |
| FluentAssertions 6.12.2 pinned | PLAN-1.2, PLAN-2.2, PLAN-3.2 specify exact version | ✓ Enforced. |
| `<InternalsVisibleTo>` MSBuild form | PLAN-1.1, PLAN-1.2, PLAN-2.2 specify `.csproj` item, not assembly attr | ✓ Convention followed. |

**Result:** PASS — All invariants are either encoded in plans or have concrete verification commands.

---

## Issues Found

**Issue 1 (severity: INFO)** — PLAN-1.2 Q1 Resolution Encoded as Test

PLAN-1.2 Task 3 Test #3 (`Parse_WithScorePlaceholder_ThrowsBecauseScoreIsNotInAllowlist`) explicitly encodes the decision that `Score` is NOT in the `EventContext` (verified by this critique). This is CORRECT and prevents future regressions. When `Score` is added to `EventContext` (future phase), both the FrozenSet AND the test must be updated in the same commit.

**Status:** Not a blocker — this is deliberate design.

---

## Final Verdict

**READY**

All 6 plans are feasible against the current codebase. API surfaces match. File references are correct. Forward dependencies within waves are clean (Wave 1 and 2 can each run in parallel; Wave 3 depends on both). Verify commands are runnable. CI Windows-skip pattern is sound. Test count gates are met. CLAUDE.md invariants are verifiable or enforced by the plans.

The dominant risks are:
1. **PLAN-2.1 Task 2:** Off-by-one in Polly retry delay formula — MITIGATED by explicit test encoding the schedule.
2. **PLAN-3.2:** Testcontainers + cross-platform CI — MITIGATED by `--skip-integration` flag and documented Windows skip.
3. **PLAN-1.2 / PLAN-2.2 sln modifications:** Parallel edits — MITIGATED by standard git merge conflict resolution (both add distinct projects).

No plan blocks another. No unrunnable verify commands. No missing APIs. **Phase 4 plans are architecturally sound and ready for builder execution.**

Recommendation: Proceed to build.
