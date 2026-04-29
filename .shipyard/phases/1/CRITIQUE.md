# Plan Critique — Phase 1 (Step 6a: Feasibility Stress Test)

**Date:** 2026-04-24  
**Verdict:** READY

---

## Summary

All three Phase 1 plans are **feasible and will execute successfully**. No cross-wave blocking dependencies, no invalid command syntax, versions match RESEARCH.md throughout, and file scopes are non-overlapping within each wave. Three observations flag minor risks — all mitigatable — but none rise to "REVISE."

---

## Per-Plan Findings

### PLAN-1.1 — Repo Tooling and Empty Solution

**Files touched:** `.editorconfig`, `.gitignore`, `global.json`, `Directory.Build.props`, `FrigateRelay.sln`  
**Wave:** 1 (no dependencies)  
**Risk:** Medium  
**Complexity:** 5 files, 1 directory

**Feasibility checks:**

- ✅ **Q1 Resolution (MSTest.Sdk vs PackageReference):** Plan selects **Approach B (PackageReference)**, rationale is sound (Dependabot coverage). `global.json` explicitly omits `msbuild-sdks` block. RESEARCH.md confirms this choice is valid and documented.
- ✅ **Q2 Resolution (TreatWarningsAsErrors scope):** Applied globally to `Directory.Build.props` including test projects. Plan documents that per-project `<NoWarn>` overrides are acceptable. RESEARCH.md Section 1, point 2 flags this as "cannot verify without a build attempt" — appropriate for Phase 1, will be caught at build time.
- ✅ **Version consistency:** SDK version `10.0.203` matches RESEARCH.md Table (released 2026-04-21). `rollForward: latestFeature` aligns with CONTEXT-1 decision D4.
- ✅ **Verify commands:** Task 3 runs `dotnet build FrigateRelay.sln -c Release` and pipes to grep for warnings/errors. Syntax is correct and will work post-execution (empty solution builds without error — Foundation assumption is sound).
- ✅ **`git grep ServicePointManager` check:** Valid risk-reduction command; repos without the string cannot have the global TLS bypass hidden in compiled code.

**Status:** PASS — straightforward tooling, no forward references, no hidden dependencies.

---

### PLAN-2.1 — FrigateRelay.Abstractions and Contract-Shape Tests

**Files touched:** 14 files (csproj + 9 abstraction interfaces + 4 test files)  
**Wave:** 2  
**Dependencies:** PLAN-1.1 (needs `global.json`, `Directory.Build.props`, empty `.sln`)  
**Risk:** High  
**Complexity:** 14 files, 1 src + 1 tests directory

**Feasibility checks:**

- ✅ **D1 enforcement (PluginRegistrationContext dual exposure):** Plan mandates `IServiceCollection Services` + `IConfiguration Configuration` as init-only properties. Syntax and shape are standard C# record patterns. CONTEXT-1 D1 is clear on rationale (avoid forcing host to know plugin config names).
- ✅ **D3 enforcement (Verdict private constructor + factories):** Plan defines Verdict as `readonly record struct` with private ctor + three static factories: `Pass()`, `Pass(double score)`, `Fail(string reason)`. Invariants (failed verdict always has reason, passed never does) are unit-tested. This pattern is idiomatic C#; syntax is correct.
- ✅ **Version consistency:** MSTest 4.2.1, FluentAssertions 6.12.2, NSubstitute 5.3.0 all match RESEARCH.md Table exactly (row-by-row). NSubstitute.Analyzers.CSharp 1.0.17 also matches.
- ✅ **Dependency constraint:** Plan states "Abstractions assembly depends **only** on `Microsoft.Extensions.*`." Task 1 adds only `Microsoft.Extensions.DependencyInjection.Abstractions 10.0.0` and `Microsoft.Extensions.Configuration.Abstractions 10.0.0`. Verification command (`dotnet list package --include-transitive`) will confirm no third-party runtime tail.
- ✅ **Test architecture:** EventContext immutability verified via Reflection (walking properties for `IsInitOnly` metadata). Verdict invariants tested with DataRow cases for null/empty/whitespace. PluginRegistrationContext instantiated with NSubstitute stubs. All three approaches are sound.
- ✅ **MSTest v3 / MTP syntax:** Plan uses Approach B from RESEARCH.md (Section "Approach B — Microsoft.NET.Sdk + PackageReference"). Csproj properties `EnableMSTestRunner=true`, `TestingPlatformDotnetTestSupport=true`, `OutputType=Exe` are exact verbatim from RESEARCH § Approach B. Test filter syntax via `dotnet test --filter "FullyQualifiedName~..."` is documented as working in RESEARCH § `dotnet test --filter behavior with MTP`.
- ⚠️ **Task 1 verify command:** References `/tmp/abs-build.log` piped from build. Grep for `"warning|error"` with `-Ei` flag is case-insensitive (E = extended regex, i = case-insensitive). This is safe — will catch `warning:`, `WARNING:`, `error:`, etc. Minor note: the negation `! grep` will exit 0 if grep finds nothing (correct for "should have zero warnings"). Syntax is sound.

**Forward reference check:**
- PLAN-2.1 depends on PLAN-1.1 and creates types (Verdict, EventContext, etc.) that PLAN-3.1 consumes. This is a clean dependency chain (Wave 2 → Wave 3), not a forward reference.

**Status:** PASS — high-risk scope (contract shape) is addressed via comprehensive unit tests, versions are locked to RESEARCH, syntax is idiomatic.

---

### PLAN-3.1 — FrigateRelay.Host, Registrar Loop, and Host Tests

**Files touched:** 8 files (2 csproj + 4 host files + 2 test files)  
**Wave:** 3  
**Dependencies:** PLAN-2.1 (needs FrigateRelay.Abstractions.dll, IPluginRegistrar interface)  
**Risk:** Medium  
**Complexity:** 8 files, 1 src + 1 tests directory

**Feasibility checks:**

- ✅ **D1 enforcement (PluginRegistrationContext in Program.cs):** Plan constructs context with `builder.Services` and `builder.Configuration` before `builder.Build()`. CONTEXT-1 D1 and RESEARCH § "User Secrets in Worker SDK projects" confirm this timing is correct.
- ✅ **D2 enforcement (M.E.L. console provider only):** Plan explicitly omits Serilog. No Phase 9 concerns bleed into Phase 1. Log line "Host started" is via injected `ILogger<PlaceholderWorker>`. Correct.
- ✅ **D4 (SDK pin):** Plan inherits `global.json` from PLAN-1.1; no override needed in csproj.
- ✅ **Worker SDK selection:** Plan specifies `<Project Sdk="Microsoft.NET.Sdk.Worker">`. RESEARCH § Host Project SDK Choice confirms this is correct for `appsettings.json` auto-copy behavior and `Microsoft.Extensions.Hosting` implicit inclusion.
- ✅ **Q3 resolution (appsettings.Local.json copy):** Plan adds explicit `<Content Include="appsettings.Local.json" CopyToOutputDirectory="PreserveNewest" Condition="Exists('appsettings.Local.json')" />`. RESEARCH § open question 3 flags SDK glob behavior as "environment-sensitive." This explicit Content item is the safe choice and matches the documented recommendation.
- ✅ **Q4 resolution (UserSecretsId):** Hard-coded GUID `9a7f6e02-3c8b-4d2e-9b17-afb4c6e03a10` in csproj. RESEARCH § open question 4 recommends a stable ID (not ad-hoc `dotnet user-secrets init` generation). Correct. Plan also adds explicit PackageReference to `Microsoft.Extensions.Configuration.UserSecrets 10.0.7` — RESEARCH § "User Secrets in Worker SDK projects" mandates this for Worker SDK projects. ✅
- ✅ **Registrar discovery shape:** Plan documents two options: (1) reflection over ServiceDescriptors, (2) extension method `AddPluginRegistrar<T>()` that registers and immediately invokes. Plan selects option 2 ("simpler and preferred"). Both are feasible; option 2 is indeed simpler and avoids reflection at composition time. Correct.
- ✅ **PlaceholderWorker logging and cancellation:** Task 1 specifies `_logger.LogInformation("Host started")` once in `ExecuteAsync`, then `await Task.Delay(Timeout.Infinite, stoppingToken)` wrapped in try/catch swallowing `OperationCanceledException`. Pattern is standard and will exit cleanly (exit code 0 after SIGINT).
- ✅ **Task 3 verify command (runtime smoke test):** Uses `timeout --signal=SIGINT --preserve-status 5 dotnet run ...` on Linux/WSL. This is the correct way to send SIGINT and preserve the exit code. Windows PowerShell equivalent is documented but not executed (correct — Windows execution will happen at build time in CI or by reviewer). Syntax is sound.
- ✅ **Version consistency:** MSTest 4.2.1, FluentAssertions 6.12.2, NSubstitute 5.3.0, NSubstitute.Analyzers.CSharp 1.0.17 all match RESEARCH.md. Microsoft.Extensions.* packages pinned to 10.0.0 and 10.0.7 match RESEARCH (v10.0.7 ships with runtime; v10.0.0 for abstractions is correct — older toolstack, still compatible).
- ✅ **Test architecture:** PluginRegistrarRunnerTests verifies context passing via NSubstitute `.Received(1).Register(context)` and `.Should().BeSameAs()` for identity. PlaceholderWorkerTests captures logger calls and asserts "Host started" appears exactly once. Both patterns are solid.
- ⚠️ **Host project test count target:** Plan-2.1 promises ≥8 tests; PLAN-3.1 adds 5 more (PluginRegistrarRunnerTests: 3, PlaceholderWorkerTests: 2), yielding ≥13 total. ROADMAP Phase 1 success criterion requires "≥6 passing tests across both test projects." Plan will exceed this. Minor note: the task description counts DataRow cases in the Fail_WithNullOrWhitespaceReason test (null, "", "   ") as 4 separate test executions — `dotnet test` will report these as separate runs, confirming the count.

**Cross-plan dependency check:**
- PLAN-3.1 references types from PLAN-2.1: `IPluginRegistrar`, `PluginRegistrationContext`, `IEventSource`, `IActionPlugin`, `IValidationPlugin`, `ISnapshotProvider`. All are defined in PLAN-2.1 Task 2. Clean dependency chain.

**Status:** PASS — complex host composition (Program.cs DI, registrar loop, graceful shutdown) is correctly specified, versions locked, and dependencies resolved.

---

## Cross-Cutting Observations

### 1. Verify Command Platforms
All verify commands in the three plans are **platform-agnostic at source** or explicitly documented:
- `dotnet build`, `dotnet test`, `dotnet run` — work identically on Windows, WSL, Linux, macOS.
- `git grep` — works via `git bash` on Windows (Git for Windows bundles it); no issue.
- `timeout --signal=SIGINT` (Task 3.3) — Linux/WSL only. Plan documents Windows PowerShell equivalent (`Start-Process` + `Stop-Process`). ✅
- `grep -Ei` — POSIX standard; works on all platforms via git bash or native grep.

**No blocking platform issues.**

### 2. `.sln` File Addition Chain
- PLAN-1.1 creates an empty `FrigateRelay.sln`.
- PLAN-2.1 Task 3 runs `dotnet sln add src/FrigateRelay.Abstractions/FrigateRelay.Abstractions.csproj` (within task's verify command).
- PLAN-3.1 Task 1 runs `dotnet sln add src/FrigateRelay.Host/FrigateRelay.Host.csproj` (within task's verify command).
- PLAN-3.1 Task 2 runs `dotnet sln add tests/FrigateRelay.Host.Tests/FrigateRelay.Host.Tests.csproj` (within task's verify command).

PLAN-2.1 Task 1 also runs `dotnet sln add tests/FrigateRelay.Abstractions.Tests/FrigateRelay.Abstractions.Tests.csproj`.

**Observation:** Each plan adds its own projects to the `.sln` as part of the verify step. This is safe because `dotnet sln add` is idempotent (adding the same project twice is a no-op). Waves execute sequentially (Wave 1 → Wave 2 → Wave 3); by the time Wave 2 runs, Wave 1 is complete. By Wave 3, both Wave 1 and Wave 2 are done. No race condition, no file lock conflict.

**Status:** SAFE.

### 3. Missing Intra-Wave Dependencies Check
- **Wave 1:** Only PLAN-1.1, no intra-wave deps.
- **Wave 2:** Only PLAN-2.1, no intra-wave deps (PLAN-1.1 is Wave 1).
- **Wave 3:** Only PLAN-3.1, no intra-wave deps (PLAN-2.1 is Wave 2).

**Status:** No intra-wave hidden dependencies.

### 4. NSubstitute Analyzer Warning Risk
PLAN-2.1 and PLAN-3.1 add `NSubstitute.Analyzers.CSharp 1.0.17` with `<PrivateAssets>all</PrivateAssets>`. The analyzer may emit CS warnings (e.g., "did you forget to configure the substitute?"). Combined with `TreatWarningsAsErrors=true` globally, this could fail the build **if** the test code triggers an analyzer rule.

**Mitigation:** RESEARCH.md § open question 2 explicitly flags this: "If the build breaks at zero warnings, a per-project `<NoWarn>` list may be needed for test projects only. Cannot verify without a build attempt." Plan-2.1 does not include a preemptive `<NoWarn>` — this is correct per the research guidance. If the build fails, the remedy is a single line in each test `.csproj` listing the analyzer rule ID. Not a blocker; will be caught at build time.

**Status:** Expected risk, mitigatable, documented in RESEARCH.

### 5. Task Sequencing Within Waves
- **PLAN-2.1 Wave 2:** Task 1 (Verdict, EventContext, SnapshotRequest/Result, .csproj) → Task 2 (interfaces + PluginRegistrationContext, builds) → Task 3 (tests, references abstractions from Tasks 1–2). Sequential dependency: Task 3 depends on Task 1 being compiled. Correct ordering.
- **PLAN-3.1 Wave 3:** Task 1 (csproj, Program.cs, PlaceholderWorker, PluginRegistrarRunner) → Task 2 (test csproj, tests) → Task 3 (runtime smoke test). Sequential dependency: Task 2 references Task 1 files; Task 3 requires both Task 1 and Task 2 executable. Correct ordering.

**Status:** Intra-plan task dependencies are correctly ordered.

---

## Recommendations for the Builder

1. **TreatWarningsAsErrors + Analyzers Watch:** During Phase 1 build, monitor the first compile. If NSubstitute.Analyzers or any other analyzer emits a warning in test projects, apply a targeted `<NoWarn>` in the test `.csproj` file. This is a known minor risk documented in RESEARCH, not a plan defect.

2. **appsettings.Local.json Copy Verification:** After build, verify that the bin/Release output directory contains a copy of `appsettings.Local.json` (or its absence does not break the build). The explicit `<Content>` item with `Condition="Exists(...)"` should handle this, but a quick `ls -la bin/Release/net10.0/` check will confirm the conditional copy works.

3. **Windows and WSL Parity Test:** Verify the `timeout --signal=SIGINT` command behavior on actual Windows CI (or run the Windows PowerShell equivalent documented in PLAN-3.1 Task 3). The plan documents both; CI should run the Windows variant to confirm exit codes match.

---

## Summary Table

| # | Plan | Status | Key Risk | Mitigations |
|---|------|--------|----------|------------|
| 1 | PLAN-1.1 | READY | Medium: tooling files (low impact) | N/A — straightforward. |
| 2 | PLAN-2.1 | READY | High: contract shape (catch at unit tests) | Comprehensive contract-shape tests (TDD). Versions locked to RESEARCH. |
| 3 | PLAN-3.1 | READY | Medium: host lifecycle (catch at integration) | Runtime smoke test in Task 3; cancellation semantics proven. |

---

## Verdict

**READY** — All three plans are feasible, cross-dependencies are clean, verify commands are syntactically valid and platform-appropriate, versions match RESEARCH.md throughout. No blockers. Proceed to execution.

The minor risks (analyzer warnings, conditional file copy, platform-specific smoke test) are all **known and documented in RESEARCH.md** and will be caught during build/test; they do not require plan changes.
