# Review: Plan 1.2

## Verdict: PASS (after one revision cycle addressing 3 Important findings)

Reviewer agent (sonnet) returned APPROVE with 0 critical findings, 3 Important findings, and 2 Suggestions on commit `d4335a3`. All 3 Important findings + 1 Suggestion were addressed in commit `201cf07` before opening the PR.

## Findings

### Critical

None.

### Important (all addressed)

1. **Cancellation test missed `WireMock.LogEntries.Should().BeEmpty()` assertion** — `RoboflowValidatorTests.cs:176-194`. The test proved `OperationCanceledException` propagates but a future regression that checked `ct.IsCancellationRequested` AFTER the HTTP call would still pass. **Fixed in `201cf07`:** added `stub.LogEntries.Should().BeEmpty("a cancelled token must not reach the HTTP layer")` after the `Assert.ThrowsAsync` call.

2. **`HttpRequestException` → `FailOpen` path was untested** — `RoboflowValidator.cs:91-94` had no test coverage. **Fixed in `201cf07`:** added Test 9 `ValidateAsync_HttpServerError_FailOpen_ReturnsAllow` (HTTP 500 + FailOpen → `Verdict.Pass()`, EventId 7102 logged). Test count rose 250 → 251.

3. **`Usings.cs` missing `global using FluentAssertions;`** — `tests/FrigateRelay.Plugins.Roboflow.Tests/Usings.cs`. Test file had a file-level `using FluentAssertions;` to compensate, but SUMMARY-1.2 incorrectly claimed the global was in `Usings.cs`. **Fixed in `201cf07`:** added the global using; removed the redundant file-level using.

### Suggestions (1 of 2 addressed)

- **Test 1 didn't assert `Verdict.Score`** — `RoboflowValidatorTests.cs:19-33`. CPAI exemplar test 8 asserts the score is forwarded (`BeApproximately(0.87, 0.001)`); Roboflow test 1 only asserted `Passed.Should().BeTrue()`. **Fixed in `201cf07`:** added `verdict.Score.Should().BeApproximately(0.92, 0.001)`. Pins the `Verdict.Pass(p.Confidence)` behavior.
- **Redundant `using FrigateRelay.Plugins.Roboflow;` and same-namespace declaration** — RoboflowValidatorTests.cs is at `namespace FrigateRelay.Plugins.Roboflow.Tests;` so types from `FrigateRelay.Plugins.Roboflow` are not auto-imported. The `using` is needed; reviewer noted as informational only. **Not changed** — same pattern as CPAI.

### Positive

- All 8 originally-required tests cover the contract: allow / reject low-confidence / reject bad-label / no-snapshot / timeout-FailClosed / timeout-FailOpen / unavailable-FailClosed / cancellation. Plus Test 9 (now 9 total) closes the unavailable-FailOpen gap.
- WireMock stubs match Roboflow Inference v1.2.7 OpenAPI shape exactly (`POST /infer/object_detection` with `{model_id, image: {type, value}, confidence}` request and `{predictions, image, time}` response).
- `CapturingLogger<T>` from `FrigateRelay.TestHelpers` used throughout; no NSubstitute on `ILogger<T>` (CLAUDE.md convention).
- EventId assertions (7101 timeout, 7102 unavailable) match `RoboflowValidator.cs:117,121` exactly.
- Cancellation test #8 proves the catch-block ordering invariant (`OperationCanceledException when ct.IsCancellationRequested` rethrows BEFORE `TaskCanceledException` catches it).
- csproj package set is a clean mirror of CPAI's (FluentAssertions 6.12.2 license-pinned, MSTest 4.2.2, NSubstitute 5.3.0, WireMock.Net 2.4.0).
- CHANGELOG bullet under `[Unreleased]` `### Added` references issue #13, includes the manual-smoke recipe per CONTEXT-14 OQ-2 fallback (16 GB image rationale captured), correct Keep-a-Changelog ordering relative to `### Fixed`.
- `FrigateRelay.sln` has the new test project entry; CI auto-discovery picks it up via `find tests -maxdepth 2 -name '*Tests.csproj'` — no CI changes needed.

## Stage 1 (Correctness) check results

- 8 tests cover the spec contract (all 8 verified individually): **PASS**
- Test names use underscores per CLAUDE.md convention: **PASS**
- `CapturingLogger<T>` used for log assertions, no NSubstitute on `ILogger<T>`: **PASS**
- EventId 7101 / 7102 assertions match `RoboflowValidator.cs:117,121`: **PASS**
- WireMock stub matches Roboflow Inference v1.2.7 OpenAPI: **PASS**
- Cancellation test asserts propagation, not swallowed-and-converted: **PASS** (and now also asserts WireMock untouched per the fix)
- csproj package set matches CPAI exemplar: **PASS**
- Usings.cs includes `global using FrigateRelay.TestHelpers;`: **PASS** (and now `FluentAssertions;` per the fix)

## Stage 2 (Integration) check results

- CHANGELOG bullet under `[Unreleased]` `### Added` references #13 with manual-smoke recipe: **PASS**
- `### Added` appears above `### Fixed`: **PASS**
- Test count progression 242 → 250 → 251 (242 + 9 new): **PASS**
- `FrigateRelay.sln` has the new test project entry: **PASS**
- Architectural invariants (no `Grpc.*`, `App.Metrics`, `OpenTracing`, `Jaeger.*`, `.Result`, `.Wait`, hard-coded IPs): **PASS**
- CI auto-discovery picks up the new test project: **PASS**

## Final verdict

**PASS.** Wave 1 (`feature/13-roboflow-validator`) is ready to PR to `main`. 10 commits total (9 from PLAN-1.1+1.2 build, 1 from this review-feedback revision). Build clean, 251/251 tests, all architectural invariants pass.
