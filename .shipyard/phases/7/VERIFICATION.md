# Phase 7 — Spec Compliance Verification

**Verdict:** READY
**Date:** 2026-04-26
**Type:** plan-review

---

## Deliverable Coverage Matrix

| ROADMAP Deliverable | Plan(s) | Notes |
|---|---|---|
| `src/FrigateRelay.Plugins.CodeProjectAi/` project | PLAN-2.1 Task 1 | csproj, sln registration, test csproj all specified |
| `CodeProjectAiValidator : IValidationPlugin` | PLAN-2.1 Task 2 | Full implementation + 8 unit tests specified |
| `CodeProjectAiOptions` (BaseUrl, MinConfidence, AllowedLabels, OnError, Timeout, AllowInvalidCertificates) | PLAN-2.1 Task 1 | Exactly matches CONTEXT-7 D5 shape; `ZoneOfInterest` absent |
| `ValidatorErrorMode { FailClosed, FailOpen }` enum | PLAN-2.1 Task 1 | Declared alongside options |
| `PluginRegistrar` (keyed-instance enumeration of top-level `Validators`) | PLAN-2.1 Task 3 | Per RESEARCH §2 pattern; no AddResilienceHandler |
| Dispatcher per-action validator chain (CONTEXT-7 D6, D11) | PLAN-1.1 Task 3 | Validator chain ABOVE Polly block, per RESEARCH §7-4 |
| `validator_rejected` structured log (CONTEXT-7 D7 fields) | PLAN-1.1 Task 3 | `LoggerMessage.Define` source-gen; event_id/action/validator/reason fields |
| Other actions fire independently (V3) | PLAN-1.1 Task 3 | `return;` short-circuits that action's consumer only |
| `IValidationPlugin.ValidateAsync(EventContext, SnapshotContext, CancellationToken)` (D1) | PLAN-1.1 Task 2 | Breaks abstract boundary; zero existing impls to migrate per RESEARCH §4 |
| `SnapshotContext.PreResolved` constructor (RESEARCH §5 Option A) | PLAN-1.1 Task 1 | Prevents double-fetch across validator + action |
| `ActionEntry.Validators: IReadOnlyList<string>?` field (D2) | PLAN-1.2 Task 1 | Third positional parameter, default null |
| `ActionEntryJsonConverter` `Validators` round-trip (D2) | PLAN-1.2 Task 2 | Read + Write + DTO extended per RESEARCH §3 |
| `StartupValidation.ValidateValidators` fail-fast (D2) | PLAN-3.1 Task 2 | Wired into `HostBootstrap.ValidateStartup` — Phase 5 lesson explicitly called out |
| `EventPump.DispatchAsync` key→plugin resolution (D2) | PLAN-3.1 Task 1 | Replaces `Array.Empty<>()` placeholder |
| `HostBootstrap` conditional CodeProjectAi registrar wiring | PLAN-3.1 Task 3 | Guard on `Validators` section existence |
| Integration: `Validator_ShortCircuits_OnlyAttachedAction` | PLAN-3.1 Task 3 | ROADMAP success criterion #1 — specified |
| Integration: `Validator_Pass_BothActionsFire` | PLAN-3.1 Task 3 | ROADMAP success criterion #3 — specified |
| ≥ 8 unit tests across validator suite | PLAN-2.1 (8) + PLAN-1.1 (2+2) + PLAN-1.2 (2) + PLAN-3.1 (3) = 17 unit | Exceeds gate by 9; 2 integration = 19 total |

---

## CONTEXT-7 Decision Honor Matrix

| Decision | Plan / Task | Verdict | Evidence |
|---|---|---|---|
| D1: `ValidateAsync(EventContext, SnapshotContext, CancellationToken)` | PLAN-1.1 Task 2 | PASS | Exact signature specified in plan code block; XML doc calls out shared-snapshot semantics and V3 independence |
| D2: Top-level `Validators` dict + `ActionEntry.Validators` keyed references | PLAN-1.2 Tasks 1-2; PLAN-2.1 Task 3; PLAN-3.1 Tasks 1-2 | PASS | All four plan files implement the full D2 chain end-to-end; startup fail-fast covered in PLAN-3.1 T2 |
| D3: Scalar `MinConfidence` + `AllowedLabels` only (no per-label dict) | PLAN-2.1 Task 1 | PASS | `CodeProjectAiOptions` as specified in CONTEXT-7 D5; no nested per-label map anywhere |
| D4: Configurable `OnError` (FailClosed default) + NO Polly retry on validator HttpClient | PLAN-2.1 Task 3 | PASS | Registrar code block contains no `AddResilienceHandler`; plan acceptance criterion explicitly greps for it; code comment explains the intentional asymmetry |
| D5: `ZoneOfInterest` / bbox MUST NOT appear in `CodeProjectAiOptions` | PLAN-2.1 Task 1 | PASS | Options class in plan contains only BaseUrl, MinConfidence, AllowedLabels, OnError, Timeout, AllowInvalidCertificates — `ZoneOfInterest` absent |
| D6: First failing verdict stops that action's chain; other actions independent | PLAN-1.1 Task 3 | PASS | `return;` after `Log.ValidatorRejected` inside the per-consumer loop; comment "short-circuit THIS action only (V3)" |
| D7: `validator_rejected` fields: event_id, action, validator, reason; LoggerMessage source-gen | PLAN-1.1 Task 3 | PASS | `Log.ValidatorRejected` specified with those exact structured fields; `LoggerMessage.Define` source-gen called out |
| D8: Per-instance `AllowInvalidCertificates` via `ConfigurePrimaryHttpMessageHandler` on named client; never global | PLAN-2.1 Task 3 | PASS | `SocketsHttpHandler.SslOptions.RemoteCertificateValidationCallback` pattern used; `ServicePointManager` acceptance criterion greps confirm absence |
| D9: `IHttpClientFactory` named client per instance (`"CodeProjectAi:{key}"`) | PLAN-2.1 Task 3 | PASS | `AddHttpClient($"CodeProjectAi:{capturedKey}")` in registrar code block |
| D10: `POST /v1/vision/detection`, multipart `image` field, client-side confidence filter | PLAN-2.1 Task 2 | PASS | `BuildMultipart` + `EvaluatePredictions` matches RESEARCH §1 decision rule; `EnsureSuccessStatusCode` + `success` field check |
| D11: Dispatcher ordering — snapshot resolve → validator chain → action (inside Polly only for action) | PLAN-1.1 Task 3 | PASS | Code block places validator chain ABOVE `_resiliencePipeline.ExecuteAsync`; RESEARCH §7-4 rationale cited |
| D12: Unquoted `name=` multipart form (Phase 6 lesson) | PLAN-2.1 Task 2 (test 7) | PASS | Test 7 explicitly asserts unquoted-name regex; code comment in `BuildMultipart` names Phase 6 D12 |
| D13: `IConfiguration.Bind` ≠ `[JsonConverter]` — ID-12 not regressed; object form mandatory in fixtures | PLAN-1.2; PLAN-3.1 Task 3 | PASS | PLAN-1.2 explicitly scopes ID-12 out; PLAN-3.1 fixture uses object form and notes "MUST use OBJECT FORM"; no string-array form in any fixture snippet |

---

## Invariant Adherence

| Invariant (CLAUDE.md / PROJECT.md) | Plan(s) | Verdict | Evidence |
|---|---|---|---|
| No `.Result` / `.Wait()` | All plans | PASS | No such patterns in any plan code snippet; acceptance criteria in PLAN-2.1 Task 2 + Task 3 grep for them |
| No `ServicePointManager` global TLS bypass | PLAN-2.1 Tasks 2-3 | PASS | Acceptance criteria in PLAN-2.1 Task 2 + Task 3 explicitly grep `ServicePointManager` in CodeProjectAi source |
| No `App.Metrics`, `OpenTracing`, `Jaeger.*` | PLAN-2.1 | PASS | PLAN-2.1 verification block includes the grep; no such references in any code snippet |
| No hardcoded IPs/hostnames in source or fixtures | PLAN-2.1 Task 2; PLAN-3.1 Task 3 | PASS | Both plans explicitly note WireMock URL injected at runtime; acceptance criteria grep for `192.168.*` patterns; "No hardcoded IPs in tests either" in PLAN-2.1 Notes |
| `<InternalsVisibleTo>` MSBuild item form only | PLAN-2.1 Task 1 | PASS | csproj snippet uses `<InternalsVisibleTo Include="..." />` MSBuild item form; no `[assembly: InternalsVisibleTo(...)]` source attribute anywhere |
| Test names `Method_Condition_Expected` underscores | All plans | PASS | All test method names in plan snippets use underscore convention; CA1707 silencing relies on existing .editorconfig |
| `LoggerMessage.Define` source-gen for logging | PLAN-1.1 Task 3; PLAN-2.1 Task 2 | PASS | PLAN-1.1 specifies `LoggerMessage.Define` for `ValidatorRejected`; PLAN-2.1 shows `[LoggerMessage(...)]` partial class `Log` for `ValidatorTimeout`/`ValidatorUnavailable` |
| New test project added to `FrigateRelay.sln` | PLAN-2.1 Task 1 | PASS | `FrigateRelay.sln` in `files_touched`; acceptance criterion checks `dotnet sln list` |
| CI auto-discovery: `run-tests.sh` requires zero edit | PLAN-2.1 Task 1 | PASS | `run-tests.sh` uses `find tests -maxdepth 2 -name '*Tests.csproj'` glob (confirmed by reading the file); Jenkinsfile delegates to same script. New test project picked up automatically. PLAN-2.1 Task 1 instructs builder to verify this, which is correct. |
| `.NET 10 only`, `TreatWarningsAsErrors`, `Nullable enable` | PLAN-2.1 Task 1 | PASS | csproj snippet includes explicit `<TargetFramework>net10.0</TargetFramework>` per ID-3 advisory; inherits `Directory.Build.props` for other properties |
| `CapturingLogger<T>` from shared `FrigateRelay.TestHelpers` — no per-assembly copies | PLAN-1.1 Task 3; PLAN-2.1 Task 2; PLAN-3.1 Task 3 | PASS | All three plans explicitly reference `tests/FrigateRelay.TestHelpers/CapturingLogger<T>` and state "Do NOT redefine"; PLAN-2.1 test csproj includes `<ProjectReference … FrigateRelay.TestHelpers …>` |
| `IActionPlugin.ExecuteAsync` 3-parameter signature (Phase 6 ARCH-D2) | PLAN-1.1 Task 3 | PASS | Dispatcher code block calls `item.Plugin.ExecuteAsync(item.Context, shared, token)` — 3 args |
| Plugin contracts in Abstractions; Host never depends on concrete plugin types directly | PLAN-1.1 Task 2; PLAN-3.1 Task 3 | PASS | `IValidationPlugin` interface change is in Abstractions; Host references plugin via keyed `IValidationPlugin` (not `CodeProjectAiValidator`); registrar registered via `IPluginRegistrar` |
| Validators are per-action, not global (V3) | PLAN-1.1 Task 3; PLAN-3.1 Tasks 1-2 | PASS | `DispatchItem` carries per-action `IReadOnlyList<IValidationPlugin>`; `EventPump` resolves from `ActionEntry.Validators` keys |

---

## Plan Structure Audit

| Check | Result | Evidence |
|---|---|---|
| All plans have ≤ 3 tasks | PASS | PLAN-1.1: 3 tasks; PLAN-1.2: 2 tasks; PLAN-2.1: 3 tasks; PLAN-3.1: 3 tasks |
| Wave 1 plans (1.1, 1.2) have no dependencies | PASS | Both declare `dependencies: []` |
| Wave 2 plan (2.1) depends on both Wave 1 plans | PASS | `dependencies: ["1.1", "1.2"]` |
| Wave 3 plan (3.1) depends on all prior plans | PASS | `dependencies: ["1.1", "1.2", "2.1"]` |
| No same-wave forward references | PASS | PLAN-1.1 and PLAN-1.2 have no cross-reference to each other |
| No circular dependencies | PASS | 1.1/1.2 → 2.1 → 3.1 is a strict DAG |
| All `files_touched` lists complete | PASS (with one minor note) | All plans list their created/modified files; PLAN-3.1 lists `tests/FrigateRelay.IntegrationTests/Fixtures/appsettings.validators.json (or equivalent)` — the parenthetical is acceptable ambiguity since the exact fixture strategy is builder discretion |
| PLAN-2.1 `files_touched` includes `FrigateRelay.sln` | PASS | Listed explicitly |
| PLAN-3.1 `files_touched` includes `FrigateRelay.Host.csproj` reference to CodeProjectAi | FAIL (minor) | `src/FrigateRelay.Host/FrigateRelay.Host.csproj` is NOT listed in PLAN-3.1 `files_touched`, but Task 3 prose requires adding a `<ProjectReference>` to `FrigateRelay.Plugins.CodeProjectAi` from the Host csproj. A builder who reads only the front-matter may miss this file. |
| Test count ≥ 10 (gate) | PASS | 17 unit + 2 integration = 19 total (ROADMAP gate is ≥8 unit + ≥2 integration) |
| `tdd: true` plans have tests-first discipline specified | PASS | PLAN-1.1 (tdd:true): Task 1 and Task 3 both say "Test first"; PLAN-2.1 (tdd:true): Task 2 says "Tests first" |

---

## Findings

### Critical (block READY)

None.

### Minor (READY, but fix before /shipyard:build 7)

1. **PLAN-3.1 `files_touched` missing `src/FrigateRelay.Host/FrigateRelay.Host.csproj`.**  
   Task 3 prose instructs adding a `<ProjectReference Include="..\FrigateRelay.Plugins.CodeProjectAi\…" />` to `FrigateRelay.Host.csproj`, but this file is not in the plan's `files_touched` front-matter. A builder reading only the metadata will miss it. Architect should append the file to PLAN-3.1 `files_touched`.

2. **PLAN-3.1 Task 3 asks the builder to update CLAUDE.md** with the asymmetric-retry behavior note (D4 validator no-retry vs. action plugins that retry). `CLAUDE.md` is not in PLAN-3.1 `files_touched`. This is an intentional scope item (per CONTEXT-7 D4: "surface it explicitly in the registrar code comment AND in CLAUDE.md observability section"), but the file is not tracked. Low risk — the code comment in the registrar captures the same fact — but the CLAUDE.md update could be silently skipped.

3. **PLAN-1.2 is marked `tdd: false`** despite adding 2 converter tests in Task 2. The label does not affect builder behavior, but it mischaracterises the plan. Non-blocking.

4. **`StartupValidationTests.ValidateValidators_UnknownType_Throws` test setup requires DI container construction** with a fake keyed `IValidationPlugin` absent for the `"weird"` key, but the test description says "Since no registrar claims `MysteryAi`, key resolves to null in DI → throws." The test must actually exercise the DI lookup path (not just call `ValidateValidators` with a raw `IServiceProvider` stub), otherwise it proves nothing about the wiring. PLAN-3.1 Task 2 does not specify how to construct the `IServiceProvider` for this test. Builder should follow the pattern established in existing `StartupValidationTests` (if they exist) or construct a minimal `ServiceCollection` with the keyed registration absent. Flag for builder awareness — not a plan defect, just an execution note.

---

## Phase-level gate commands

(Absorbed from architect's draft VERIFICATION.md)

```bash
# Clean build (warnings-as-errors)
dotnet build FrigateRelay.sln -c Release

# All test projects — auto-discovered by run-tests.sh glob
bash .github/scripts/run-tests.sh

# Or per-project (MTP runner — NOT dotnet test):
dotnet run --project tests/FrigateRelay.Abstractions.Tests              -c Release
dotnet run --project tests/FrigateRelay.Host.Tests                      -c Release
dotnet run --project tests/FrigateRelay.Plugins.BlueIris.Tests          -c Release
dotnet run --project tests/FrigateRelay.Plugins.Pushover.Tests          -c Release
dotnet run --project tests/FrigateRelay.Plugins.CodeProjectAi.Tests     -c Release
dotnet run --project tests/FrigateRelay.IntegrationTests                -c Release  # requires Docker

# Architecture invariants (all must be empty)
git grep -nE '\.(Result|Wait)\(' src/
git grep -n "ServicePointManager" src/
git grep -nE 'App\.Metrics|OpenTracing|Jaeger\.' src/
git grep -nE '192\.168\.[0-9]+\.[0-9]+' src/ tests/
git grep -n "MemoryCache.Default" src/
git grep -n "Newtonsoft.Json" src/

# Phase 7 specific gates
git grep -n "ValidateAsync" src/FrigateRelay.Abstractions/IValidationPlugin.cs
# expect: Task<Verdict> ValidateAsync(EventContext ctx, SnapshotContext snapshot, CancellationToken ct);

git grep -n "PreResolved\|_hasPreResolved" src/FrigateRelay.Abstractions/SnapshotContext.cs

git grep -n "validator_rejected\|ValidatorRejected" src/FrigateRelay.Host/Dispatch/

git grep -n "Validators" src/FrigateRelay.Host/Configuration/ActionEntry.cs
git grep -n "Validators" src/FrigateRelay.Host/Configuration/ActionEntryJsonConverter.cs

ls src/FrigateRelay.Plugins.CodeProjectAi/
git grep -n "AddKeyedSingleton" src/FrigateRelay.Plugins.CodeProjectAi/PluginRegistrar.cs
git grep -n "AddResilienceHandler" src/FrigateRelay.Plugins.CodeProjectAi/   # MUST be empty (D4)

git grep -n "GetRequiredKeyedService<IValidationPlugin>" src/FrigateRelay.Host/EventPump.cs

git grep -n "ValidateValidators" src/FrigateRelay.Host/Configuration/StartupValidation.cs
git grep -n "ValidateValidators" src/FrigateRelay.Host/HostBootstrap.cs

git grep -n "FrigateRelay.Plugins.CodeProjectAi.PluginRegistrar" src/FrigateRelay.Host/HostBootstrap.cs

git grep -n "name=image" tests/FrigateRelay.Plugins.CodeProjectAi.Tests/

# Confirm Host csproj references CodeProjectAi plugin (missing from PLAN-3.1 files_touched — minor finding)
grep -n "CodeProjectAi" src/FrigateRelay.Host/FrigateRelay.Host.csproj

# Failure-mode smoke (Phase 5 review-3.1 lesson — ValidateValidators must be called, not dead)
# 1. Add a bad validator key to appsettings.Local.json
# 2. dotnet run --project src/FrigateRelay.Host -c Release
# 3. Expect: InvalidOperationException with "Validator '{key}' is referenced by Subscription[...]"
# 4. Revert fixture after verification

# Graceful shutdown smoke
dotnet run --project src/FrigateRelay.Host -c Release --no-build > /tmp/host.log 2>&1 &
sleep 3
kill -INT "$(pgrep -f 'FrigateRelay.Host/bin/Release/net10.0/FrigateRelay.Host$' | head -1)"
wait
# expect exit 0 and "Application is shutting down..." in /tmp/host.log
```
