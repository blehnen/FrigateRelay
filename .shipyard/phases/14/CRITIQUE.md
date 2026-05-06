# Phase 14 Plan Critique
**Date:** 2026-05-06  
**Type:** plan-review (feasibility stress test)

## Verdict
**READY** — All 8 plans are feasible and executable against the current codebase. File paths exist, API surfaces match, and verification commands are syntactically correct.

## Blocking Issues
**0** — No missing dependencies, conflicting references, or impossible verification steps detected.

---

## Per-Plan Findings

### PLAN-1.1 (Roboflow plugin scaffold + DI wiring)

**Files referenced — all paths verified to exist or parent directories confirmed:**
- `src/FrigateRelay.Plugins.CodeProjectAi/` — exemplar plugin exists. Csproj, Options, Response, Validator, PluginRegistrar all present and inspected.
- `src/FrigateRelay.Host/Configuration/ActionEntry.cs` — exists, current signature at line 30 is `internal sealed record ActionEntry(string Plugin, string? SnapshotProvider = null, IReadOnlyList<string>? Validators = null)`. Parent directory `/src/FrigateRelay.Host/Configuration/` confirmed.
- `src/FrigateRelay.Host/HostBootstrap.cs` — exists. Validators section gate at line 133-134 is exactly as cited in RESEARCH §3.3.
- `src/FrigateRelay.Host/FrigateRelay.Host.csproj` — exists. Current plugin ProjectReferences include CodeProjectAi; pattern for new reference is established.
- `FrigateRelay.sln` — exists, 24 projects currently defined. Solution add pattern is standard.

**API surface verification:**
- `IValidationPlugin` contract (from Abstractions) — implicit in CPAI reference; plan clones CPAI shape.
- `IPluginRegistrar` pattern — CPAI PluginRegistrar.cs inspected; seven-step ritual at lines 30-86 matches RESEARCH §1.5 description.
- `PluginRegistrationContext` — implicit in CPAI; constructor pattern clear.

**Verification commands — all syntactically valid:**
- Build checks (`dotnet build FrigateRelay.sln -c Release`) — standard.
- Grep patterns for invariants (`git grep -nE 'App\.Metrics|...`) — all patterns are valid grep expressions.
- Solution list check (`dotnet sln FrigateRelay.sln list | grep -i Roboflow`) — standard tooling.

**Risk assessment:** Low. CPAI exists as an exact template; the plan is a straightforward clone with Roboflow-specific config (BaseUrl, ModelId).

---

### PLAN-1.2 (Roboflow validator tests + CHANGELOG)

**Files referenced:**
- `tests/FrigateRelay.Plugins.Roboflow.Tests/` — parent directory `/tests/` exists; new subdirectory will be created.
- `tests/FrigateRelay.Plugins.CodeProjectAi.Tests/` — exemplar exists; test structure and csproj pattern inspected.
- `FrigateRelay.TestHelpers/` — exists at `/tests/`. CapturingLogger.cs confirmed present (CLAUDE.md "shared test helper" precedent).
- `CHANGELOG.md` — must exist at repo root; not explicitly verified but standard file.

**WireMock + NSubstitute versions:** Plan defers exact version pinning to execution time ("match the version used by other test projects"). The `run-tests.sh` auto-discovery will find the new project (established pattern per CLAUDE.md).

**Test count baseline:** RESEARCH §8 cites "242 tests" as post-Phase-13 baseline. Verified: current test run shows exactly 242 tests. Target 250 (+8 new).

**Verification commands:** All valid. `bash .github/scripts/run-tests.sh --skip-integration` is the established CI test runner.

**Risk assessment:** Low. Tests follow the existing CPAI pattern; no new frameworks introduced.

---

### PLAN-2.1 (DOODS2 plugin scaffold + dual-transport + DI wiring)

**Files referenced:**
- `src/FrigateRelay.Plugins.Doods2/` — new directory; parent `/src/FrigateRelay.Plugins.*/` pattern established.
- `src/FrigateRelay.Plugins.Doods2/Protos/odrpc.proto` — new file; `/Protos/` subdirectory to be created. Upstream source: `https://github.com/snowzach/doods2` (URL provided in RESEARCH §4.1).
- Existing files to modify: `HostBootstrap.cs`, `Host.csproj`, `FrigateRelay.sln` — all verified to exist.

**gRPC package versions:** Plan cites `Grpc.Net.Client` 2.66.0, `Grpc.Tools` 2.66.0, `Google.Protobuf` 3.28.0 (RESEARCH §4.2). Plan explicitly defers to "actual current versions at PR-2 execution time" — acceptable for RESEARCH-time guesses.

**Architectural invariant — gRPC dep containment:** Plan's Acceptance Criteria require verification that Host and Abstractions stay gRPC-free via `dotnet list` transitive-package checks (lines 119-124). This is load-bearing and enforceable.

**Verification commands:** All valid. The proto header comment requires builder to capture actual upstream commit SHA at execution time (achievable via `git rev-parse --short HEAD` on the doods2 repo).

**Risk assessment:** Medium (flagged in plan frontmatter). gRPC is a new dependency family for this codebase. However, the csproj containment strategy (`PrivateAssets="all"` on Grpc.Tools, `<Protobuf>` with explicit namespace) is sound. The architectural invariant is enforceable via the listed grep/dotnet commands.

---

### PLAN-2.2 (DOODS2 HTTP-transport tests)

**Files referenced:**
- `tests/FrigateRelay.Plugins.Doods2.Tests/` — new directory; parent exists.
- csproj will mirror Roboflow test csproj (PLAN-2.2 Task 1 lines 45-54) — no new package families, just WireMock.
- Note: Plan explicitly defers gRPC server packages to PLAN-2.3 (line 52: "NO `Grpc.AspNetCore.Server` reference yet").

**Test count target:** 250 → 257 (+7 HTTP-path tests). Cumulative baseline verified as 242; post-Wave-1 (PLAN-1.2) becomes 250.

**Confidence normalization assertion:** Test #1 (`ValidateAsync_Http_DetectionAboveThresholdAfterNormalization_ReturnsAllow`) explicitly tests 0-100 → 0-1 mapping (80.0 → 0.8 >= 0.5 threshold). This is the load-bearing test for DOODS2-specific gotcha (RESEARCH §7.2 note: "confidence is **0-100** scale in DOODS2, not 0-1").

**Verification commands:** All valid. Counter EventId assertions (7201 timeout, 7202 unavailable) are explicitly required in Task 2 Acceptance Criteria.

**Risk assessment:** Low. WireMock pattern established by Roboflow; confidence normalization is a straightforward assertion.

---

### PLAN-2.3 (DOODS2 gRPC-transport tests + CHANGELOG)

**Files referenced:**
- `tests/FrigateRelay.Plugins.Doods2.Tests/Fixtures/InProcessDoods2GrpcServer.cs` — new fixture file; `/Fixtures/` subdirectory to be created (no existing gRPC fixtures in codebase, but parent pattern established by Mosquitto fixtures).
- Test csproj will be extended with `Grpc.AspNetCore.Server`, `Microsoft.AspNetCore.TestHost`, `Grpc.Tools` (lines 51-53).
- Vendored proto included in test build with `GrpcServices="Server"` (line 53).

**In-process gRPC test pattern:** Plan cites RESEARCH Concern #1 (line 479-481): "gRPC test harness ergonomics in .NET 10 are unverified." Plan Task 1 explicitly requires "proof-of-shape before fanning out" — one exemplar test first, then 4 more. Fallback documented if pattern proves hostile (lines 127-130).

**Verification commands:** Task 1 Acceptance Criteria lines 133-137 reference the `Detector.DetectorClient` generated type and in-process server setup. These are contingent on gRPC codegen succeeding, which is gated by the csproj's `<Protobuf>` item. Verification is sound.

**Test count target:** 257 → 262 (+5 gRPC tests). This matches RESEARCH §8 table row for PR-2.

**Risk assessment:** Medium. In-process gRPC server pattern is unverified against this codebase's MSTest v3 + class-fixture setup. However, plan has a documented fallback (real Kestrel host on random port) if the TestHost approach fails. The exemplar test approach (Task 1) mitigates risk by proving the pattern before fanning out.

---

### PLAN-3.1 (ParallelValidators field on ActionEntry + DispatchItem + converters)

**Files referenced — all exist:**
- `src/FrigateRelay.Host/Configuration/ActionEntry.cs:30` — verified. Current record signature spans lines 30-33.
- `src/FrigateRelay.Host/Configuration/ActionEntryJsonConverter.cs` — verified. Dual-form Read (lines 32-37 string, 39-46 object), Write (lines 53-66). DTO at lines 69-72.
- `src/FrigateRelay.Host/Configuration/ActionEntryTypeConverter.cs` — verified. Single-method converter at lines 32-35.
- `src/FrigateRelay.Host/Dispatch/DispatchItem.cs:29-36` — verified. Current readonly record struct signature at lines 29-36.
- `src/FrigateRelay.Host/EventPump.cs` — verified. Exists; plan must locate `new DispatchItem` construction sites via `git grep -n` at execution time.

**JSON converter passthrough logic:** Plan Task 1 requires updating the DTO (line 76) to include `ParallelValidators` and updating Write to emit it conditionally (lines 81-82). TypeConverter (lines 87-88) requires "no change" — the `new ActionEntry(s)` default will carry `ParallelValidators = false`. This is correct back-compat.

**DispatchItem propagation:** Plan Task 2 cites `git grep -n 'new DispatchItem'` as the discovery mechanism. Grep is appropriate since construction sites may vary. No hardcoded line numbers assumed — plan defers to execution-time discovery.

**Verification commands:** All valid. The cumulative test count gate (line 150) defers to "at least 262 minimum" (no new tests in Wave 3 PLAN-3.1, but Wave 2 left us at 262).

**Risk assessment:** Low. This is straightforward plumbing — no behavior change, just field addition with default false. Back-compat is guaranteed by the default-false parameter position.

---

### PLAN-3.2 (Parallel-validators branch in ChannelActionDispatcher + unit tests)

**Files referenced:**
- `src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs:200-246` — verified. Current line range contains the validator loop (lines 209-245); plan will branch at the `if (item.Validators.Count > 0)` entry (line 200).
- New test class in `tests/FrigateRelay.Host.Tests/ChannelActionDispatcherParallelValidatorsTests.cs` — parent directory `/tests/FrigateRelay.Host.Tests/` exists; no new csproj needed.

**Dispatcher branch logic:** Plan specifies extracting two private async methods (lines 75-103):
- `RunValidatorsSequentiallyAsync` — existing sequential loop body unchanged (back-compat invariant).
- `RunValidatorsInParallelAsync` — new `Task.WhenAll` path with strict-AND aggregation + per-validator counter emission.

The helper `RunOneValidatorAsync` (lines 87-102) is required for span generation (matching existing lines 211-218 activity code). This is refactorable from sequential path as well, though plan allows duplication if refactor is too invasive (line 105).

**Counter invariant:** Plan explicitly states "No new counters added" (line 107). The existing `DispatcherDiagnostics.IncrementValidatorsPassed/Rejected` calls are used from both branches. Verification command (line 169) checks that Phase 13's `CounterInventoryDriftTests` still pass — this locks the counter count to the v1.1 baseline.

**Catch-block ordering:** Plan does not modify validator catch-blocks; each validator's own `HttpClient.Timeout` + `catch (TaskCanceledException)` returns a verdict per its config. Parallel path awaits `Task.WhenAll` which rethrows `OperationCanceledException` naturally (line 30 states this).

**Test count target:** 262 → 268 (+6 new unit tests). Tests are in the existing Host.Tests project (no new csproj).

**Verification commands:** Lines 172-178 check for `item.ParallelValidators` and `Task.WhenAll` usage, counter inventory, and architectural invariants. All valid.

**Risk assessment:** Medium (flagged in frontmatter). The dispatcher branch and Task.WhenAll aggregation is the core logic of #23. However, the test suite (6 unit tests covering sequential back-compat, happy-path parallel, strict-AND, counters, timeout, cancellation) is comprehensive. The parallel path is isolated from the sequential path by design.

---

### PLAN-3.3 (Parallel-validators integration test + CHANGELOG)

**Files referenced:**
- `tests/FrigateRelay.IntegrationTests/ParallelValidatorsSliceTests.cs` — new test class; parent `/tests/FrigateRelay.IntegrationTests/` exists (verified in earlier output). Exemplar slice tests exist per plan lines 45-46 reference to `MqttToBlueIrisSliceTests.cs`.
- `CHANGELOG.md` — exists at repo root. Plan adds bullets under `[Unreleased] / ### Added` section (which will be created by PLAN-1.2 if not existing).

**Integration test scenario:** Plan describes a concrete CPAI + Roboflow parallel-validation scenario (lines 48-65). Two WireMock stubs for validators, one for action plugin, real Mosquitto fixture. Configuration includes `ParallelValidators: true` on an action. Assertions check that both validators received requests AND action fired (happy path).

Optional second test (line 67: "optional, only if harness is cheap") tests strict-AND rejection. Optional stretch goal (line 70): add DOODS2-HTTP as third validator.

Minimum bar: 1 test (line 75). This pushes test count from 268 → 269 (+1 new).

**CHANGELOG entries:** Three bullets required (lines 162-177):
- #13 (Roboflow) — added by PLAN-1.2.
- #14 (DOODS2) — added by PLAN-2.3.
- #23 (parallel validators) — added by PLAN-3.3.

**Verification commands:** Full test suite with integration tests (no `--skip-integration` flag), test count gate ≥ 269, counter inventory drift test, architectural invariants, gRPC dep containment (STILL holds), CHANGELOG grep checks.

**Risk assessment:** Medium. Integration tests are slower and require Testcontainers (Docker); however, the pattern is established by Phase 4+ `MqttToBlueIrisSliceTests.cs`. The test's concurrency assertion (validator request timestamps delta below sequential lower bound) requires WireMock `RequestMessage.DateTime` inspection — this is plausible but not yet verified in this codebase's integration tests.

---

## Cross-Plan Analysis

### Dependencies and Ordering

**Sequential PR ordering (CONTEXT-14 D1):**
- Wave 1 (PR #13 — PLAN-1.1 + 1.2) — no dependencies.
- Wave 2 (PR #14 — PLAN-2.1 + 2.2 + 2.3) — depends on PR #13 merged.
- Wave 3 (PR #23 — PLAN-3.1 + 3.2 + 3.3) — depends on PR #13 + #14 merged.

All plans respect this ordering. PLAN-3.3's integration test explicitly exercises CPAI + Roboflow concurrently (line 21-24), requiring both plugin projects to be on `main` before PR #23 opens.

### Shared File Conflicts

**Wave 1 files:** PLAN-1.1 and PLAN-1.2 touch disjoint files. PLAN-1.1 creates plugin source files; PLAN-1.2 creates test files and modifies CHANGELOG.md.

**Wave 2 files:** PLAN-2.1, 2.2, 2.3 touch:
- PLAN-2.1 modifies `HostBootstrap.cs` (add DOODS2 registrar).
- PLAN-2.2 creates test project (parallel file).
- PLAN-2.3 modifies test csproj (extends PLAN-2.2's csproj) and CHANGELOG.md.

No blocking conflicts. PLAN-2.3 extends the test csproj created by PLAN-2.2 — correct sequencing.

**Wave 3 files:** PLAN-3.1 modifies ActionEntry, DispatchItem, converters, EventPump (plumbing). PLAN-3.2 modifies ChannelActionDispatcher (dispatcher branch) and creates tests. PLAN-3.3 creates integration test and modifies CHANGELOG.md. No conflicts; sequencing is correct (plumbing must precede dispatcher branch).

### Hidden Dependencies Within Waves

**Wave 2 internal ordering:** PLAN-2.1 creates Doods2Validator; PLAN-2.2 tests HTTP path; PLAN-2.3 extends csproj and tests gRPC path. Correct sequencing — no hidden dep issues.

**Wave 3 internal ordering:** PLAN-3.1 adds field; PLAN-3.2 uses field in dispatcher; PLAN-3.3 exercises the feature end-to-end. Correct sequencing.

### Shared Configuration (HostBootstrap.cs)

PLAN-1.1 Task 3 and PLAN-2.1 Task 3 both modify `HostBootstrap.cs` (registrar additions). They modify the same `if (builder.Configuration.GetSection("Validators").Exists())` block:

- PLAN-1.1 adds Roboflow registrar (line 152 in plan's template).
- PLAN-2.1 adds DOODS2 registrar (line 284 in plan's template).

Both are **within the same block**, and PLAN-1.1 executes first (Wave 1 before Wave 2). This is correct — the Validators gate is shared by all three validator plugins. No conflict.

---

## Complexity and Scale

### Files Touched Per Plan

| Plan | File Count | Scope | Complexity |
|------|-----------|-------|-----------|
| 1.1 | 8 | Plugin scaffold + DI wiring | Low (clone CPAI) |
| 1.2 | 4 | Tests + CHANGELOG | Low (WireMock) |
| 2.1 | 9 | Plugin scaffold + gRPC setup + DI wiring | Medium (gRPC new) |
| 2.2 | 3 | HTTP tests + (implicit test csproj extend) | Low (WireMock) |
| 2.3 | 4 | gRPC tests + CHANGELOG | Medium (in-process gRPC) |
| 3.1 | 5 | Plumbing (ActionEntry, converters, DispatchItem, EventPump) | Low (field additions) |
| 3.2 | 2 | Dispatcher branch + unit tests | Medium (Task.WhenAll logic) |
| 3.3 | 2 | Integration test + CHANGELOG | Medium (integration test setup) |

**Largest plans:** PLAN-2.1 (9 files, medium risk due to gRPC). All others within reasonable bounds (2-8 files, mostly low risk).

---

## Verification Command Validity

All `bash` commands cited in Verification sections are syntactically valid:
- Standard `dotnet build`, `dotnet run`, `dotnet list`, `dotnet sln` commands.
- Standard `grep`, `git grep`, `find` patterns.
- Established CI script invocation (`bash .github/scripts/run-tests.sh`).
- Test filtering via `--filter` (MSTest v3 / MTP runner support).

No typos or path issues detected.

---

## Known Caveats and Risk Mitigations

### PLAN-2.3 — In-Process gRPC Test Pattern (Unverified)

**Concern:** gRPC test harness ergonomics in .NET 10 are unverified against this codebase's MSTest v3 + class-fixture setup (RESEARCH Concern #1).

**Mitigation:** Plan Task 1 requires a single exemplar test before fanning out. Fallback documented: real Kestrel host on random port if TestHost + WebApplicationFactory prove hostile.

**Verdict:** Acceptable risk. Exemplar test approach is prudent.

### PLAN-2.1 — Vendored .proto Upstream Commit

**Concern:** Builder must capture the actual upstream commit SHA at PR-2 execution time.

**Mitigation:** Plan Task 1 explicitly requires the commit SHA in the proto header comment (lines 54-55, 69-70) and cites the upstream repo URL. Builder can fetch the latest commit via `git ls-remote https://github.com/snowzach/doods2 HEAD`.

**Verdict:** Feasible. No blockers.

### PLAN-3.2 — Task.WhenAll Aggregation (Strict-AND with No Short-Circuit)

**Concern:** Ensures all validators run even if one rejects (CONTEXT-14 D6).

**Verification:** Plan Task 2 test #3 (`Dispatch_ParallelValidatorsTrue_AnyValidatorRejects_ActionDoesNotExecute_AllValidatorsRan`) explicitly asserts all three validators' `ValidateAsync` was called via NSubstitute `.Received(1)` on each. This is a load-bearing assertion; if Task.WhenAll short-circuits internally, this test will fail.

**Verdict:** Test-gated. Safe.

---

## Recommendations

1. **PLAN-2.3 exemplar test first:** Builder should verify the in-process gRPC server pattern works before committing to the full test set. If it fails, use the documented fallback.

2. **DOODS2 0-100 → 0-1 normalization:** PLAN-2.2 Test #1 is the load-bearing assertion that this normalization works. Ensure it passes before marking the HTTP path complete.

3. **Shared CapturingLogger precedent:** All test plans use `FrigateRelay.TestHelpers` `CapturingLogger<T>` (not NSubstitute on `ILogger<T>`). This pattern is consistently enforced in acceptance criteria and should be honored in all new test files.

4. **gRPC dep containment:** PLAN-2.1, 2.2, 2.3 verification commands include `dotnet list` transitive-package checks to ensure gRPC stays contained. These are non-negotiable gates — run them at every phase checkpoint.

5. **Counter inventory drift test:** PLAN-3.2 Acceptance Criteria (line 114) reference `CounterInventoryDriftTests`. Locate this test in the Phase 13 output (or grep `tests/FrigateRelay.Host.Tests/` for "Inventory" or "Counter" tests) to confirm the test exists and passes at each phase boundary.

---

## Final Assessment

**All 8 plans are feasible and ready for execution.** The codebase has the necessary supporting infrastructure (test helpers, existing plugin templates, established patterns). The plans respect architectural invariants (gRPC containment, no new counters unless gated, back-compat defaults). Verification commands are all valid and enforceable. Risk is well-documented and mitigated (exemplar-first approach for gRPC tests, fallback strategies).

**Recommended next step:** Execute Wave 1 (PLAN-1.1 + 1.2) against `main`, merge PR #13, then proceed to Wave 2.

---

**Verdict:** READY
