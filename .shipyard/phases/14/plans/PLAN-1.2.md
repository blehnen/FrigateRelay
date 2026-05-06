---
phase: 14-v1.2-inference-engines-parallel-validation
plan: 1.2
wave: 1
dependencies: [1.1]
must_haves:
  - tests/FrigateRelay.Plugins.Roboflow.Tests project with WireMock-driven coverage
  - At least 8 new tests covering allow / reject-low-confidence / reject-bad-label / no-snapshot / timeout-FailClosed / timeout-FailOpen / unavailable-FailClosed / cancellation
  - CHANGELOG bullet under [Unreleased] / ### Added with manual-smoke recipe (per OQ-2 fallback)
  - Test count rises from 242 baseline to ≥ 250
files_touched:
  - tests/FrigateRelay.Plugins.Roboflow.Tests/FrigateRelay.Plugins.Roboflow.Tests.csproj
  - tests/FrigateRelay.Plugins.Roboflow.Tests/RoboflowValidatorTests.cs
  - tests/FrigateRelay.Plugins.Roboflow.Tests/Usings.cs
  - CHANGELOG.md
tdd: true
risk: low
---

# Plan 1.2: Roboflow validator tests + CHANGELOG (PR #13 — Roboflow Inference validator)

## Context

PLAN-1.1 ships the production `RoboflowValidator`; this plan covers it with WireMock-driven unit tests. Per CONTEXT-14 OQ-2 (resolved by RESEARCH §6: NOT VIABLE for Testcontainers — `roboflow/roboflow-inference-server-cpu` is 16.7 GB and exceeds the GitHub Actions disk-free budget), there is **no** integration-test container; the CHANGELOG bullet documents a manual-smoke recipe operators can run locally.

CPAI's test project is the canonical clone target (RESEARCH §1.6) — same WireMock + NSubstitute + `FrigateRelay.TestHelpers` `CapturingLogger<T>` pattern. The CI auto-discovers test projects via `find tests -maxdepth 2 -name '*Tests.csproj'` (CLAUDE.md "When adding a new test project: no CI changes required") so the new csproj at `tests/FrigateRelay.Plugins.Roboflow.Tests/FrigateRelay.Plugins.Roboflow.Tests.csproj` is sufficient.

Test count target: 242 baseline → **250 (+8 new)** per RESEARCH §8.

## Dependencies

- **PLAN-1.1** — production `RoboflowValidator`, `RoboflowOptions`, `RoboflowResponse`, `PluginRegistrar` must compile before tests can be written.

## Tasks

### Task 1: Scaffold the test project + Usings.cs

**Files:**
- `tests/FrigateRelay.Plugins.Roboflow.Tests/FrigateRelay.Plugins.Roboflow.Tests.csproj` (new)
- `tests/FrigateRelay.Plugins.Roboflow.Tests/Usings.cs` (new)

**Action:** create

**Description:**

Mirror `tests/FrigateRelay.Plugins.CodeProjectAi.Tests/FrigateRelay.Plugins.CodeProjectAi.Tests.csproj` exactly. The csproj must:
- Use `Microsoft.NET.Sdk` SDK with `<OutputType>Exe</OutputType>` (MSTest v3 / Microsoft.Testing.Platform pattern — CLAUDE.md "Commands" section).
- `<ProjectReference Include="..\..\src\FrigateRelay.Plugins.Roboflow\FrigateRelay.Plugins.Roboflow.csproj" />`.
- `<ProjectReference Include="..\FrigateRelay.TestHelpers\FrigateRelay.TestHelpers.csproj" />` (provides `CapturingLogger<T>` per CLAUDE.md "Conventions" section — do NOT redefine a per-assembly copy).
- Standard MSTest v3 packages — match versions used in the CPAI test csproj at the time of planning.
- WireMock.Net package — match the version used by other test projects (verify via `grep -r WireMock.Net tests/*/*.csproj`).
- NSubstitute — match the version in CPAI tests.
- FluentAssertions pinned to `6.12.2` (CLAUDE.md "Testing" — license-critical).

`Usings.cs` must include:
```csharp
global using FrigateRelay.TestHelpers;
global using Microsoft.VisualStudio.TestTools.UnitTesting;
```
plus any other global usings the CPAI test project uses (verify and copy). Test names use underscores (`Method_Condition_Expected`); CA1707 is silenced for `tests/**.cs` via `.editorconfig` — do not re-enable per-project (CLAUDE.md "Conventions").

Add the project to the solution: `dotnet sln FrigateRelay.sln add tests/FrigateRelay.Plugins.Roboflow.Tests/FrigateRelay.Plugins.Roboflow.Tests.csproj`.

**Acceptance Criteria:**
- `dotnet build tests/FrigateRelay.Plugins.Roboflow.Tests/FrigateRelay.Plugins.Roboflow.Tests.csproj -c Release` succeeds with zero warnings (zero tests yet).
- `dotnet sln FrigateRelay.sln list | grep -i 'FrigateRelay.Plugins.Roboflow.Tests'` returns the new project.
- The csproj has `<OutputType>Exe</OutputType>`, references `FrigateRelay.TestHelpers`, and pins `FluentAssertions` to `6.12.2`.
- `bash .github/scripts/run-tests.sh --skip-integration` discovers the new project (visible in stdout) and exits 0.

---

### Task 2: Write 8 RoboflowValidator tests against WireMock

**Files:** `tests/FrigateRelay.Plugins.Roboflow.Tests/RoboflowValidatorTests.cs` (new)

**Action:** create (TDD — write tests then verify they pass against PLAN-1.1's implementation)

**Description:**

Single test file modeled on `tests/FrigateRelay.Plugins.CodeProjectAi.Tests/CodeProjectAiValidatorTests.cs`. Use a `[TestClass]` with a `WireMockServer` field, started in `[TestInitialize]` and disposed in `[TestCleanup]`. Construct the validator under test with a real `HttpClient` whose `BaseAddress` points at the WireMock URL and whose `Timeout` matches the test's intent (5s for happy-path, 200ms for timeout tests so they don't slow CI).

Helpers needed in the same file (private statics):
- `MakeOptions(double minConf = 0.5, string[]? labels = null, RoboflowValidatorErrorMode onError = FailClosed, TimeSpan? timeout = null, string modelId = "rfdetr-base/1")` — builds `RoboflowOptions`.
- `MakeContext()` — builds an `EventContext` with a synthetic camera/label/event id (mirror CPAI's helper).
- `MakeSnapshotContext(ReadOnlyMemory<byte>? bytes = null)` — builds a `SnapshotContext` from a pre-resolved fixture (`new SnapshotContext(new SnapshotResult(...))`-style; copy from CPAI tests).
- `StubInferEndpoint(WireMockServer ws, RoboflowResponse response)` — sets up `ws.Given(Request.Create().UsingPost().WithPath("/infer/object_detection")).RespondWith(...)`.

The 8 tests (all use `Method_Condition_Expected` naming):

1. `ValidateAsync_PredictionAboveThreshold_ReturnsAllow` — stub returns one prediction with `confidence=0.92`, `class="person"`; opts have `MinConfidence=0.5`, `AllowedLabels=["person"]`. Expect `Verdict.Passed == true`.
2. `ValidateAsync_PredictionBelowThreshold_ReturnsReject` — stub returns one prediction with `confidence=0.30`; opts `MinConfidence=0.5`. Expect `Verdict.Passed == false` and `Reason` starts with `"validator_no_match"`.
3. `ValidateAsync_LabelNotInAllowList_ReturnsReject` — stub returns one prediction with `confidence=0.92`, `class="dog"`; opts `AllowedLabels=["person"]`. Expect `Verdict.Passed == false`, reason starts with `"validator_no_match"`.
4. `ValidateAsync_NoSnapshot_ReturnsRejectImmediately` — pass `default(SnapshotContext)` (no resolver). Expect `Verdict.Fail("validator_no_snapshot")`. WireMock receives ZERO requests (assert via `ws.LogEntries.Count == 0`).
5. `ValidateAsync_HttpTimeout_FailClosed_ReturnsReject` — stub configured with `WithDelay(TimeSpan.FromSeconds(2))`; client timeout 200ms; opts `OnError=FailClosed`. Expect `Verdict.Fail("validator_timeout")`. Verify the warning log emitted via `CapturingLogger<RoboflowValidator>` contains EventId 7101.
6. `ValidateAsync_HttpTimeout_FailOpen_ReturnsAllow` — same stub, opts `OnError=FailOpen`. Expect `Verdict.Pass()`.
7. `ValidateAsync_HttpServerError_FailClosed_ReturnsReject` — stub returns HTTP 500; opts `OnError=FailClosed`. Expect `Verdict.Passed == false` and reason starts with `"validator_unavailable: "`. Verify EventId 7102 logged.
8. `ValidateAsync_CancellationRequested_PropagatesOperationCanceled` — pass a `CancellationToken` from a `CancellationTokenSource` whose `Cancel()` is called *before* the call. Expect `OperationCanceledException` is thrown (NOT swallowed into a verdict — RESEARCH §1.4 lines 66-69 catch ordering: `OperationCanceledException when ct.IsCancellationRequested` rethrows, NOT a validator failure). Use `Assert.ThrowsExactlyAsync<OperationCanceledException>(...)` or MSTest equivalent.

Each test has a single `[TestMethod]` attribute and an XML doc-comment summarizing the case. Use the `CapturingLogger<RoboflowValidator>` helper from `FrigateRelay.TestHelpers` (CLAUDE.md "Conventions" — DO NOT use NSubstitute on `ILogger<T>`).

**Acceptance Criteria:**
- All 8 tests pass: `dotnet run --project tests/FrigateRelay.Plugins.Roboflow.Tests -c Release` exits 0 and reports `total: 8`.
- `dotnet build FrigateRelay.sln -c Release` zero warnings.
- Cumulative test count rises to ≥ 250 (242 baseline + 8 new): `bash .github/scripts/run-tests.sh --skip-integration | grep -c 'total:'` shows the new project's totals row.
- Tests use the shared `CapturingLogger<RoboflowValidator>`, NOT NSubstitute on `ILogger<T>` (verify: `git grep -n 'Substitute.For<ILogger' tests/FrigateRelay.Plugins.Roboflow.Tests/` returns empty).
- LoggerMessage event-IDs are asserted (7101 timeout, 7102 unavailable) so future EventId drift fails CI.

---

### Task 3: CHANGELOG entry with manual-smoke recipe

**Files:** `CHANGELOG.md`

**Action:** modify

**Description:**

Add a bullet under the `[Unreleased]` section's `### Added` heading. If `[Unreleased]` does not yet exist on `main` (last release was `v1.1.0`), add it above the `[1.1.0]` section using Keep-a-Changelog formatting matching the existing conventions in this file.

Bullet text:
```markdown
- **Roboflow Inference validator (#13).** New `FrigateRelay.Plugins.Roboflow` validator
  plugin: HTTP-based `IValidationPlugin` against a self-hosted Roboflow Inference server
  (`http://<host>:9001`). Per-instance `ModelId`, `MinConfidence`, `AllowedLabels`,
  `OnError` (FailClosed/FailOpen), and `Timeout` config. Add to `appsettings.json` under
  `Validators:<key>: { "Type": "Roboflow", ... }` and reference the key from any
  `ActionEntry.Validators` list. WireMock-driven unit tests; no Testcontainers
  integration test (the upstream `roboflow/roboflow-inference-server-cpu` image is
  ~16 GB, exceeding GitHub Actions disk budget).

  **Manual smoke recipe** (operator runs locally):
  ```bash
  docker run --rm -p 9001:9001 \
      -e ROBOFLOW_API_KEY=... \
      roboflow/roboflow-inference-server-cpu:latest
  curl -X POST http://localhost:9001/infer/object_detection \
      -H 'Content-Type: application/json' \
      -d '{"model_id":"rfdetr-base/1","image":{"type":"base64","value":"<b64>"},"confidence":0.5}'
  ```
```

Do NOT use real IPs / hostnames in the bullet (CLAUDE.md "no hard-coded IPs"). Use `<host>` and `localhost` as placeholders.

**Acceptance Criteria:**
- `[Unreleased]` section exists in `CHANGELOG.md` with an `### Added` heading.
- The Roboflow bullet references issue `#13` and includes the manual-smoke recipe.
- The CI secret-scan does not flag the recipe (`bash .github/scripts/secret-scan.sh` exits 0; `<b64>` placeholder is intentionally non-secret-shaped).
- No real IPs (`192.168.x.x`, etc.) appear in the CHANGELOG entry.

## Verification

```bash
# Build clean
dotnet build FrigateRelay.sln -c Release

# Tests pass — Roboflow project + the rest of the suite
dotnet run --project tests/FrigateRelay.Plugins.Roboflow.Tests -c Release --no-build
bash .github/scripts/run-tests.sh --skip-integration

# Test count gate — at least 250 total (242 baseline + 8 new)
TOTAL=$(bash .github/scripts/run-tests.sh --skip-integration 2>&1 | grep -E '^total:' | awk '{ sum += $2 } END { print sum }')
[ "$TOTAL" -ge 250 ] || { echo "test count regression: $TOTAL < 250"; exit 1; }

# Architectural invariants
git grep -nE 'App\.Metrics|OpenTracing|Jaeger\.' src/                # must be empty
git grep -nE '\.(Result|Wait)\(' src/                                 # must be empty
git grep -n 'ServicePointManager' src/                                # must be empty

# Tests use the shared CapturingLogger, not NSubstitute on ILogger<T>
git grep -n 'Substitute.For<ILogger' tests/FrigateRelay.Plugins.Roboflow.Tests/    # must be empty

# Secret scan + tripwire still clean
bash .github/scripts/secret-scan.sh

# CHANGELOG references the new feature
grep -n '#13' CHANGELOG.md
grep -n 'Roboflow' CHANGELOG.md
```
