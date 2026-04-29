# Review: Plan 3.1

## Verdict: PASS (post-gap-fix)

This file was first written with `CRITICAL_ISSUES` (missing `PlaceholderWorkerTests.cs` and three explicit `Microsoft.Extensions.*` PackageReferences). Both defects were resolved by commit `ef68446`. Re-reviewed after the fix — **PASS**.

## Findings

### Critical
None.

### Minor (non-blocking, carry to Phase 2+)
- `tests/FrigateRelay.Host.Tests/PlaceholderWorkerTests.cs` — `CapturingLogger<T>` is a private nested class. If a second worker test class needs log capture in Phase 3+, this has to be copy-pasted. Low risk today; hoist to `tests/FrigateRelay.Host.Tests/TestHelpers/CapturingLogger.cs` before the first real worker lands.
- `src/FrigateRelay.Host/Program.cs` lines 40–41 — a `LoggerFactory` is constructed and immediately disposed for the pre-`Build()` registrar phase. With an empty registrar list (Phase 1), zero log lines are emitted. A one-line "Phase 3+ will see output here as plugins register" comment would orient future contributors.

### Positive
- `<InternalsVisibleTo>` MSBuild item keeps `PluginRegistrarRunner` internal in production while allowing tests — cleaner than an `[assembly:]` attribute in source.
- `PlaceholderWorker` uses `LoggerMessage.Define` for allocation-free logging — good discipline for what will become a template in later phases.
- `RunAll_RegistrarThrows_PropagatesAndShortCircuits` documents current behavior (propagate, don't isolate) rather than silently relying on it — future-defensive against a silent-catch regression.
- `CapturingLogger<T>` over NSubstitute is the right call for this assertion surface: plain class, clear failure messages, zero new deps. Documented as Decision 7.
- Commit hygiene: three commits map 1:1 to task 1, task 2 (partial), task 2 (gap-fix). Each carries rationale, not just a what-changed.

## Previously-flagged defects — resolution

| Defect (pre-fix) | Status | Evidence |
|---|---|---|
| Missing `PlaceholderWorkerTests.cs` | RESOLVED | Commit `ef68446` adds the file with `ExecuteAsync_LogsHostStarted_ExactlyOnce` and `ExecuteAsync_OnCancellation_CompletesWithoutThrowing`. |
| Missing 3 explicit `Microsoft.Extensions.*` PackageReferences | RESOLVED | `FrigateRelay.Host.Tests.csproj` lines 20–22: `Logging.Abstractions`, `Configuration`, `DependencyInjection`, all `10.0.7`. |

## Plan frontmatter cross-check

| `files_touched` entry | Present? | Commit(s) |
|---|---|---|
| `src/FrigateRelay.Host/FrigateRelay.Host.csproj` | ✅ | `2655b1d`, `ef68446` |
| `src/FrigateRelay.Host/Program.cs` | ✅ | `2655b1d` |
| `src/FrigateRelay.Host/PlaceholderWorker.cs` | ✅ | `2655b1d` |
| `src/FrigateRelay.Host/PluginRegistrarRunner.cs` | ✅ | `2655b1d` |
| `src/FrigateRelay.Host/appsettings.json` | ✅ | `2655b1d` |
| `tests/FrigateRelay.Host.Tests/FrigateRelay.Host.Tests.csproj` | ✅ | `1c1a2f6`, `ef68446` |
| `tests/FrigateRelay.Host.Tests/PluginRegistrarRunnerTests.cs` | ✅ | `1c1a2f6` |
| `tests/FrigateRelay.Host.Tests/PlaceholderWorkerTests.cs` | ✅ | `ef68446` |

All 8 entries accounted for. Task 2 `<action>` methods (a)–(e) all present.

## Phase 1 closeout assessment

| ROADMAP / CONTEXT criterion | Met? | Evidence |
|---|---|---|
| `dotnet build FrigateRelay.sln -c Release` — 0 warn, 0 err | ✅ | SUMMARY-3.1 verification |
| `dotnet sln list` shows 4 projects | ✅ | 2 src + 2 test |
| Tests: ≥ 6 passing, 0 failures | ✅ | 17 total (10 Abstractions + 7 Host) |
| Host logs "Host started" + exits 0 on SIGINT | ✅ | `pgrep | kill -INT` recipe: exit=0, "Application is shutting down..." |
| `FrigateRelay.Abstractions` — only `Microsoft.Extensions.*` deps | ✅ | `Configuration.Abstractions`, `DependencyInjection.Abstractions`, `Primitives` |
| No `.Result` / `.Wait()` in `src/` or `tests/` | ✅ | forbidden-patterns grep empty |
| No Serilog / OpenTracing / Newtonsoft / DotNetWorkQueue / App.Metrics | ✅ | forbidden-patterns grep empty |
| All logging via M.E.L. console only (D2) | ✅ | No Serilog refs anywhere |
| `UserSecretsId` hard-coded (Q4) | ✅ | `9a7f6e02-3c8b-4d2e-9b17-afb4c6e03a10` in Host csproj |
| `appsettings.Local.json` conditional Content item (Q3) | ✅ | `Condition="Exists('appsettings.Local.json')"` |
| `PluginRegistrationContext` carries `Services` + `Configuration` (D1) | ✅ | Both required; ctor `[SetsRequiredMembers]` |
| `Verdict` static factories + private ctor (D3) | ✅ | `readonly record struct`, factories `Pass()` / `Pass(score)` / `Fail(reason)` |
| SDK pin + rollForward (D4, corrected to `10.0.100`) | ✅ | `global.json`; `dotnet --version` → `10.0.107` |

## Check results

| Command | Result |
|---|---|
| `dotnet build FrigateRelay.sln -c Release` | Build succeeded. 0 Warning(s), 0 Error(s). |
| `dotnet run --project tests/FrigateRelay.Abstractions.Tests -c Release --no-build` | `total: 10  succeeded: 10  failed: 0` |
| `dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build` | `total: 7  succeeded: 7  failed: 0` |
| `dotnet sln list` | 4 projects |
| `git grep -nE '(\.Result\(|\.Wait\(|ServicePointManager|Newtonsoft|Serilog|OpenTracing|App\.Metrics|DotNetWorkQueue)' src/ tests/` | empty |
| `dotnet list src/FrigateRelay.Abstractions/FrigateRelay.Abstractions.csproj package --include-transitive` | only `Microsoft.Extensions.*` |
| Graceful shutdown smoke (`pgrep | kill -INT`) | exit=0; "Host started" + "Application is shutting down..." |

## Summary

Critical: 0 &nbsp;&nbsp;&nbsp; Minor: 2 (non-blocking) &nbsp;&nbsp;&nbsp; Positives: 5

Phase 1 closeout is fully satisfied. Proceed to Step 5 (phase verification) and the post-phase gates.
