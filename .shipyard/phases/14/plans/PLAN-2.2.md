---
phase: 14-v1.2-inference-engines-parallel-validation
plan: 2.2
wave: 2
dependencies: [2.1]
must_haves:
  - tests/FrigateRelay.Plugins.Doods2.Tests project (used by both PLAN-2.2 and PLAN-2.3)
  - At least 7 HTTP-path tests covering allow / reject-low-confidence / reject-bad-label / no-snapshot / timeout-FailClosed / timeout-FailOpen / unavailable-FailClosed
  - DOODS2 confidence 0-100 → 0-1 normalization is asserted
  - Cumulative test count rises from 250 (post-Wave 1) to ≥ 257
files_touched:
  - tests/FrigateRelay.Plugins.Doods2.Tests/FrigateRelay.Plugins.Doods2.Tests.csproj
  - tests/FrigateRelay.Plugins.Doods2.Tests/Usings.cs
  - tests/FrigateRelay.Plugins.Doods2.Tests/Doods2HttpValidatorTests.cs
tdd: true
risk: low
---

# Plan 2.2: DOODS2 HTTP-transport tests (PR #14 — DOODS2 validator)

## Context

PLAN-2.1 ships the dual-transport `Doods2Validator`. This plan covers the **HTTP transport path only** with WireMock-driven unit tests that mirror the Roboflow test shape from PLAN-1.2. PLAN-2.3 covers the gRPC path separately (in-process gRPC server pattern is more involved and gets its own plan to keep task counts ≤ 3).

Key DOODS2-specific assertion: the validator must normalize the upstream's 0-100 confidence to 0-1 before comparing to `MinConfidence` (RESEARCH §7.2 — "confidence is **0-100** scale in DOODS2, not 0-1"). Tests in this plan stub WireMock with confidences in 0-100 and assert the operator-facing 0-1 `MinConfidence` works as expected.

Test count target: 250 (post-Wave 1) → **257 (+7 new)** for the HTTP path. PLAN-2.3 adds 5 more for the gRPC path → 262.

## Dependencies

- **PLAN-2.1** — `Doods2Validator`, `Doods2Options`, `Doods2HttpResponse` types must compile.

## Tasks

### Task 1: Scaffold tests/FrigateRelay.Plugins.Doods2.Tests project

**Files:**
- `tests/FrigateRelay.Plugins.Doods2.Tests/FrigateRelay.Plugins.Doods2.Tests.csproj` (new)
- `tests/FrigateRelay.Plugins.Doods2.Tests/Usings.cs` (new)

**Action:** create

**Description:**

Mirror `tests/FrigateRelay.Plugins.Roboflow.Tests/FrigateRelay.Plugins.Roboflow.Tests.csproj` (created in PLAN-1.2 Task 1) with these DOODS2-specific additions:

```xml
<ProjectReference Include="..\..\src\FrigateRelay.Plugins.Doods2\FrigateRelay.Plugins.Doods2.csproj" />
<ProjectReference Include="..\FrigateRelay.TestHelpers\FrigateRelay.TestHelpers.csproj" />
```

The csproj must NOT add `Grpc.AspNetCore.Server` here — that is PLAN-2.3's responsibility (the in-process gRPC test host pattern). PLAN-2.2 only needs WireMock + the existing test stack. PLAN-2.3 will edit this same csproj to add the gRPC server packages.

`Usings.cs` matches the Roboflow tests' shape: `global using FluentAssertions;`, `global using FrigateRelay.TestHelpers;`, and `global using Microsoft.VisualStudio.TestTools.UnitTesting;`. (PR #42's REVIEW-1.2 caught the FluentAssertions omission — needed so `.Should()` chains compile without per-file usings.)

Add to solution: `dotnet sln FrigateRelay.sln add tests/FrigateRelay.Plugins.Doods2.Tests/FrigateRelay.Plugins.Doods2.Tests.csproj`.

**Acceptance Criteria:**
- `dotnet build tests/FrigateRelay.Plugins.Doods2.Tests/FrigateRelay.Plugins.Doods2.Tests.csproj -c Release` succeeds with zero warnings.
- `dotnet sln FrigateRelay.sln list | grep -i 'FrigateRelay.Plugins.Doods2.Tests'` returns the new project.
- The csproj references `FrigateRelay.Plugins.Doods2` and `FrigateRelay.TestHelpers`. NO `Grpc.AspNetCore.Server` reference yet (PLAN-2.3 adds it).
- `bash .github/scripts/run-tests.sh --skip-integration` discovers the new project and exits 0 (no tests yet, but auto-discovery confirmed).

---

### Task 2: Write 7 HTTP-path Doods2Validator tests

**Files:** `tests/FrigateRelay.Plugins.Doods2.Tests/Doods2HttpValidatorTests.cs` (new)

**Action:** create (TDD — tests verify PLAN-2.1's HTTP-path implementation)

**Description:**

Single test file modeled on `tests/FrigateRelay.Plugins.Roboflow.Tests/RoboflowValidatorTests.cs` (PLAN-1.2 Task 2). All tests construct the validator with `Transport = Doods2Transport.Http` and a real `HttpClient` aimed at WireMock. The gRPC client constructor argument can be a real `Detector.DetectorClient` over an unused `GrpcChannel.ForAddress("http://localhost:0")` (lazy; never invoked in HTTP mode) or a `null!` if Doods2Validator's constructor signature accepts it via nullable annotations — pick whichever PLAN-2.1's final shape allows.

Helpers in the same file (private statics):
- `MakeOptions(double minConf = 0.5, string[]? labels = null, Doods2ValidatorErrorMode onError = FailClosed, TimeSpan? timeout = null, string detectorName = "default")` — `Transport = Http` always.
- `StubDetectEndpoint(WireMockServer ws, params (string label, double rawConfidence0to100)[] detections)` — sets up `ws.Given(Request.Create().UsingPost().WithPath("/detect")).RespondWithJson(...)` building a `Doods2HttpResponse` whose `Detections` carries the supplied tuples (raw 0-100 confidence as DOODS2 returns it). Each detection's `Top/Left/Bottom/Right` can be fixed at `0/0/100/100` since the validator does not use bbox.

The 7 tests:

1. `ValidateAsync_Http_DetectionAboveThresholdAfterNormalization_ReturnsAllow` — stub returns `(label="person", confidence=80.0)`; opts `MinConfidence=0.5`, `AllowedLabels=["person"]`. Validator normalizes `80.0 / 100.0 = 0.8 >= 0.5` → expect `Verdict.Passed == true`. **This test is the load-bearing assertion that the 0-100 → 0-1 normalization works** (RESEARCH §7.2).

2. `ValidateAsync_Http_DetectionBelowThresholdAfterNormalization_ReturnsReject` — stub returns `(label="person", confidence=30.0)`; opts `MinConfidence=0.5`. Validator normalizes `0.30 < 0.5` → expect `Verdict.Passed == false`, reason starts with `"validator_no_match"`.

3. `ValidateAsync_Http_LabelNotInAllowList_ReturnsReject` — stub returns `(label="dog", confidence=90.0)`; opts `AllowedLabels=["person"]`. Expect `Verdict.Passed == false`.

4. `ValidateAsync_Http_NoSnapshot_ReturnsRejectImmediately` — pass `default(SnapshotContext)`; expect `Verdict.Fail("validator_no_snapshot")`. Assert WireMock receives ZERO requests.

5. `ValidateAsync_Http_Timeout_FailClosed_ReturnsReject` — stub `WithDelay(TimeSpan.FromSeconds(2))`; client timeout 200ms; opts `OnError=FailClosed`. Expect `Verdict.Fail("validator_timeout")`. Verify `CapturingLogger<Doods2Validator>` shows EventId 7201.

6. `ValidateAsync_Http_Timeout_FailOpen_ReturnsAllow` — same stub, `OnError=FailOpen`. Expect `Verdict.Pass()`.

7. `ValidateAsync_Http_HttpServerError_FailClosed_ReturnsReject` — stub returns HTTP 500; `OnError=FailClosed`. Expect reason starts with `"validator_unavailable: "`. Verify EventId 7202.

XML doc-comments per test summarizing the case. Each test uses `[TestMethod]`. Tests use the shared `CapturingLogger<Doods2Validator>` from `FrigateRelay.TestHelpers` — DO NOT use NSubstitute on `ILogger<T>` (CLAUDE.md "Conventions").

**Cancellation test is deferred to PLAN-2.3** (it covers the gRPC + HTTP cancellation paths together, since both transports rethrow `OperationCanceledException` identically per PLAN-2.1's catch-block ordering).

**Acceptance Criteria:**
- All 7 tests pass: `dotnet run --project tests/FrigateRelay.Plugins.Doods2.Tests -c Release` exits 0 and reports `total: 7` (or higher if PLAN-2.3 has already added gRPC tests by the time of execution).
- `dotnet build FrigateRelay.sln -c Release` zero warnings.
- Cumulative test count ≥ 257 (250 post-Wave 1 + 7 new).
- Test #1 explicitly demonstrates the 0-100 → 0-1 normalization (the 80.0 → pass + 30.0 → fail asymmetry on a 0.5 threshold is the intended evidence).
- LoggerMessage event IDs 7201 (timeout) and 7202 (unavailable) are asserted in tests #5 and #7.
- `git grep -n 'Substitute.For<ILogger' tests/FrigateRelay.Plugins.Doods2.Tests/` returns empty.

## Verification

```bash
# Build clean
dotnet build FrigateRelay.sln -c Release

# Tests pass — DOODS2 HTTP-path tests + everything else
dotnet run --project tests/FrigateRelay.Plugins.Doods2.Tests -c Release --no-build
bash .github/scripts/run-tests.sh --skip-integration

# Test count gate — at least 257 total (250 post-Wave 1 + 7 new HTTP-path tests)
TOTAL=$(bash .github/scripts/run-tests.sh --skip-integration 2>&1 | grep -E '^total:' | awk '{ sum += $2 } END { print sum }')
[ "$TOTAL" -ge 257 ] || { echo "test count regression: $TOTAL < 257"; exit 1; }

# Architectural invariants — gRPC still contained
dotnet list src/FrigateRelay.Host/FrigateRelay.Host.csproj package --include-transitive | grep -E 'Grpc\.' && exit 1 || true
git grep -nE 'App\.Metrics|OpenTracing|Jaeger\.' src/                # must be empty
git grep -nE '\.(Result|Wait)\(' src/                                 # must be empty

# Tests use the shared CapturingLogger
git grep -n 'Substitute.For<ILogger' tests/FrigateRelay.Plugins.Doods2.Tests/    # must be empty
```
