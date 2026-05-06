---
phase: 14-v1.2-inference-engines-parallel-validation
plan: 2.2
wave: 2
dependencies: [2.1]
must_haves:
  - tests/FrigateRelay.Plugins.Doods2.Tests project (used by PLAN-2.2 only — PLAN-2.3 was REMOVED, gRPC scope reverted)
  - At least 9 HTTP-path tests covering allow / reject-low-confidence / reject-bad-label / no-snapshot / timeout-FailClosed / timeout-FailOpen / unavailable-FailClosed / unavailable-FailOpen / cancellation
  - JSON-response decode failure routed through OnError (mirrors Roboflow PR #42 fix)
  - DOODS2 confidence 0-100 → 0-1 normalization is asserted
  - CHANGELOG bullet under [Unreleased] / ### Added describing DOODS2 (HTTP-only, gRPC scope reverted)
  - Cumulative test count rises from 250 (post-Wave 1) to ≥ 259
files_touched:
  - tests/FrigateRelay.Plugins.Doods2.Tests/FrigateRelay.Plugins.Doods2.Tests.csproj
  - tests/FrigateRelay.Plugins.Doods2.Tests/Usings.cs
  - tests/FrigateRelay.Plugins.Doods2.Tests/Doods2ValidatorTests.cs
  - CHANGELOG.md
tdd: true
risk: low
---

# Plan 2.2: DOODS2 HTTP-transport tests (PR #14 — DOODS2 validator)

## Context

PLAN-2.1 ships the HTTP-only `Doods2Validator`. This plan covers WireMock-driven unit tests + the CHANGELOG bullet, mirroring the Roboflow test shape from PLAN-1.2.

**Note: PLAN-2.3 was REMOVED on 2026-05-06.** The DOODS2 gRPC scope was reverted after the orchestrator probed the live DOODS2 v2 server and confirmed upstream's README: "DOODS2 drops support for gRPC as I doubt very much anyone used it anyways." See `.shipyard/phases/14/plans/PLAN-2.3.md` for the reversal record. PLAN-2.2 absorbs PLAN-2.3's residual responsibilities (CHANGELOG bullet, cancellation test).

Key DOODS2-specific assertion: the validator must normalize the upstream's 0-100 confidence to 0-1 before comparing to `MinConfidence` (RESEARCH §7.2 — "confidence is **0-100** scale in DOODS2, not 0-1"). Tests in this plan stub WireMock with confidences in 0-100 and assert the operator-facing 0-1 `MinConfidence` works as expected.

Test count target: 250 (post-Wave 1) → **259 (+9 new)** covering the full HTTP-only contract.

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

The csproj only needs WireMock + the existing test stack. (Historical note: PLAN-2.3 was originally going to add `Grpc.AspNetCore.Server` for in-process gRPC tests, but the gRPC scope was reverted — see PLAN-2.3.md.)

`Usings.cs` matches the Roboflow tests' shape: `global using FluentAssertions;`, `global using FrigateRelay.TestHelpers;`, and `global using Microsoft.VisualStudio.TestTools.UnitTesting;`. (PR #42's REVIEW-1.2 caught the FluentAssertions omission — needed so `.Should()` chains compile without per-file usings.)

Add to solution: `dotnet sln FrigateRelay.sln add tests/FrigateRelay.Plugins.Doods2.Tests/FrigateRelay.Plugins.Doods2.Tests.csproj`.

**Acceptance Criteria:**
- `dotnet build tests/FrigateRelay.Plugins.Doods2.Tests/FrigateRelay.Plugins.Doods2.Tests.csproj -c Release` succeeds with zero warnings.
- `dotnet sln FrigateRelay.sln list | grep -i 'FrigateRelay.Plugins.Doods2.Tests'` returns the new project.
- The csproj references `FrigateRelay.Plugins.Doods2` and `FrigateRelay.TestHelpers`. NO `Grpc.*` references at all (gRPC scope reverted; see PLAN-2.3.md).
- `bash .github/scripts/run-tests.sh --skip-integration` discovers the new project and exits 0 (no tests yet, but auto-discovery confirmed).

---

### Task 2: Write 9 Doods2Validator tests (HTTP — full contract)

**Files:** `tests/FrigateRelay.Plugins.Doods2.Tests/Doods2ValidatorTests.cs` (new)

**Action:** create (TDD — tests verify PLAN-2.1's implementation)

**Description:**

Single test file modeled on `tests/FrigateRelay.Plugins.Roboflow.Tests/RoboflowValidatorTests.cs` (PLAN-1.2 Task 2 + PR #42 review-feedback fixes). All tests construct the validator with a real `HttpClient` aimed at WireMock — DOODS2 is HTTP-only as of the v2 upstream rewrite (see PLAN-2.3 reversal record).

Helpers in the same file (private statics):
- `MakeOptions(double minConf = 0.5, string[]? labels = null, Doods2ValidatorErrorMode onError = FailClosed, TimeSpan? timeout = null, string detectorName = "default")`.
- `StubDetectEndpoint(WireMockServer ws, params (string label, double rawConfidence0to100)[] detections)` — sets up `ws.Given(Request.Create().UsingPost().WithPath("/detect")).WithBody(JsonPathMatcher("$.detector_name")).WithBody(JsonPathMatcher("$.data")).WithBody(JsonPathMatcher("$.detect")).RespondWithJson(...)` building a `Doods2HttpResponse` whose `Detections` carries the supplied tuples (raw 0-100 confidence as DOODS2 returns it). Body matchers harden the contract per PR #42's REVIEW-1.2 lesson — a future serializer regression that drops or renames fields fails every test.

The 9 tests:

1. `ValidateAsync_DetectionAboveThresholdAfterNormalization_ReturnsAllow` — stub returns `(label="person", confidence=80.0)`; opts `MinConfidence=0.5`, `AllowedLabels=["person"]`. Validator normalizes `80.0 / 100.0 = 0.8 >= 0.5` → expect `Verdict.Passed == true` and `Verdict.Score.Should().BeApproximately(0.80, 0.001)`. **Load-bearing assertion that the 0-100 → 0-1 normalization works** (RESEARCH §7.2).

2. `ValidateAsync_DetectionBelowThresholdAfterNormalization_ReturnsReject` — stub returns `(label="person", confidence=30.0)`; opts `MinConfidence=0.5`. Validator normalizes `0.30 < 0.5` → expect `Verdict.Passed == false`, reason starts with `"validator_no_match"`.

3. `ValidateAsync_LabelNotInAllowList_ReturnsReject` — stub returns `(label="dog", confidence=90.0)`; opts `AllowedLabels=["person"]`. Expect `Verdict.Passed == false`.

4. `ValidateAsync_NoSnapshot_ReturnsRejectImmediately` — pass `default(SnapshotContext)`; expect `Verdict.Fail("validator_no_snapshot")`. Assert WireMock receives ZERO requests.

5. `ValidateAsync_HttpTimeout_FailClosed_ReturnsReject` — stub `WithDelay(TimeSpan.FromSeconds(2))`; client timeout 200ms; opts `OnError=FailClosed`. Expect `Verdict.Fail("validator_timeout")`. Verify `CapturingLogger<Doods2Validator>` shows EventId 7201.

6. `ValidateAsync_HttpTimeout_FailOpen_ReturnsAllow` — same stub, `OnError=FailOpen`. Expect `Verdict.Pass()`.

7. `ValidateAsync_HttpServerError_FailClosed_ReturnsReject` — stub returns HTTP 500; `OnError=FailClosed`. Expect reason starts with `"validator_unavailable: "`. Verify EventId 7202.

8. `ValidateAsync_HttpServerError_FailOpen_ReturnsAllow` — same stub, `OnError=FailOpen` → expect `Verdict.Pass()`. Closes the FailOpen-on-HTTP-error coverage gap (matches PR #42's REVIEW-1.2 finding for Roboflow).

9. `ValidateAsync_CancellationRequested_PropagatesOperationCanceled` — pre-cancelled token → `Assert.ThrowsAsync<OperationCanceledException>`. Asserts `stub.LogEntries.Should().BeEmpty()` so a future regression that moves the cancellation check after the network call fails the test (matches PR #42's REVIEW-1.2 fix).

XML doc-comments per test summarizing the case. Each test uses `[TestMethod]`. Tests use the shared `CapturingLogger<Doods2Validator>` from `FrigateRelay.TestHelpers` — DO NOT use NSubstitute on `ILogger<T>` (CLAUDE.md "Conventions").

**Acceptance Criteria:**
- All 9 tests pass: `dotnet run --project tests/FrigateRelay.Plugins.Doods2.Tests -c Release` exits 0 and reports `total: 9`.
- `dotnet build FrigateRelay.sln -c Release` zero warnings.
- Cumulative test count ≥ 259 (250 post-Wave 1 + 9 new).
- Test #1 explicitly demonstrates the 0-100 → 0-1 normalization (the 80.0 → pass + 30.0 → fail asymmetry on a 0.5 threshold is the intended evidence).
- LoggerMessage event IDs 7201 (timeout) and 7202 (unavailable) are asserted in tests #5 and #7.
- `git grep -n 'Substitute.For<ILogger' tests/FrigateRelay.Plugins.Doods2.Tests/` returns empty.

---

### Task 3: CHANGELOG bullet for DOODS2 (#14)

**Files:** `CHANGELOG.md`

**Action:** modify

**Description:**

Add a bullet under `[Unreleased]` `### Added` describing the new DOODS2 validator. Reference:
- Self-hosted DOODS2 v2 (HTTP-only — gRPC scope was reverted, see commit message + PLAN-2.3.md for rationale).
- Per-instance `DetectorName` (`default` TFLite / `tensorflow` Faster R-CNN / `pytorch` YOLOv5s).
- `MinConfidence` 0.0-1.0 range; the validator normalizes DOODS2's 0-100 wire format internally.
- `OnError` (FailClosed/FailOpen), `Timeout`, `AllowInvalidCertificates` knobs.
- Issue #14.

**Acceptance Criteria:**
- `git diff CHANGELOG.md` shows exactly one new bullet under `[Unreleased]` `### Added` (above the Roboflow #13 bullet from PR #42 if alphabetical; below if chronological — match the file's existing pattern).
- Bullet references issue #14 by number.
- Bullet explicitly notes "HTTP-only" so future operators don't search for a gRPC config knob.

## Verification

```bash
# Build clean
dotnet build FrigateRelay.sln -c Release

# Tests pass — DOODS2 HTTP-path tests + everything else
dotnet run --project tests/FrigateRelay.Plugins.Doods2.Tests -c Release --no-build
bash .github/scripts/run-tests.sh --skip-integration

# Test count gate — at least 259 total (250 post-Wave 1 + 9 new HTTP-path tests)
TOTAL=$(bash .github/scripts/run-tests.sh --skip-integration 2>&1 | grep -E '^total:' | awk '{ sum += $2 } END { print sum }')
[ "$TOTAL" -ge 259 ] || { echo "test count regression: $TOTAL < 259"; exit 1; }

# Architectural invariants — gRPC fully gone (scope reverted; see PLAN-2.3.md)
git grep -nE 'Grpc\.' src/ && exit 1 || true                          # source clean
dotnet list src/FrigateRelay.Host/FrigateRelay.Host.csproj package --include-transitive | grep -E 'Grpc\.' && exit 1 || true   # transitive clean
git grep -nE 'App\.Metrics|OpenTracing|Jaeger\.' src/                # must be empty
git grep -nE '\.(Result|Wait)\(' src/                                 # must be empty

# Tests use the shared CapturingLogger
git grep -n 'Substitute.For<ILogger' tests/FrigateRelay.Plugins.Doods2.Tests/    # must be empty
```
