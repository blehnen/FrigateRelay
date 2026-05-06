# Build Summary: Plan 2.2

## Status: complete

## Tasks Completed

- **Task 1: Scaffold `tests/FrigateRelay.Plugins.Doods2.Tests/`** — complete — files: `tests/FrigateRelay.Plugins.Doods2.Tests/{FrigateRelay.Plugins.Doods2.Tests.csproj,Usings.cs}` + `FrigateRelay.sln` entry. Commit `a19e060`.
- **Task 2: 9 Doods2Validator HTTP-path tests** — complete — file: `tests/FrigateRelay.Plugins.Doods2.Tests/Doods2ValidatorTests.cs`. Commit `6ba4259`.
- **Task 3: CHANGELOG bullet under `[Unreleased]` `### Added`** for issue #14 — complete. Commit `04eda39`.

## Files Modified

- `tests/FrigateRelay.Plugins.Doods2.Tests/FrigateRelay.Plugins.Doods2.Tests.csproj` (created) — `OutputType=Exe`, MSTest v3 + Microsoft.Testing.Platform, FluentAssertions 6.12.2, NSubstitute, WireMock.Net. ProjectReferences to `FrigateRelay.Plugins.Doods2`, `FrigateRelay.Abstractions`, `FrigateRelay.TestHelpers`. Mirrors the Roboflow test csproj.
- `tests/FrigateRelay.Plugins.Doods2.Tests/Usings.cs` (created) — three globals: `FluentAssertions`, `FrigateRelay.TestHelpers`, `Microsoft.VisualStudio.TestTools.UnitTesting`. Matches Roboflow's post-REVIEW-1.2 shape.
- `tests/FrigateRelay.Plugins.Doods2.Tests/Doods2ValidatorTests.cs` (created) — 9 `[TestMethod]` cases. Helpers `MakeOptions`, `MakeContext`, `MakeSnapshotContext`, `MakeValidator`, `StubDetectEndpoint` (with `JsonPathMatcher` body matchers on `$.detector_name`, `$.data`, `$.detect`).
- `FrigateRelay.sln` — added the new test project entry.
- `CHANGELOG.md` — `[Unreleased]` `### Added` gains a DOODS2 bullet (issue #14) above the Roboflow #13 bullet from PR #42 (chronological-descending order).

## The 9 tests (per PLAN-2.2 §Task 2)

1. `ValidateAsync_DetectionAboveThresholdAfterNormalization_ReturnsAllow` — stub returns `(label="person", confidence=80.0)`; `MinConfidence=0.5`, `AllowedLabels=["person"]`. Validator normalizes `80.0 / 100.0 = 0.8 >= 0.5` → `Verdict.Pass(0.80)`. Asserts `Verdict.Score.Should().BeApproximately(0.80, 0.001)`. **Load-bearing assertion that the 0-100 → 0-1 normalization works.**
2. `ValidateAsync_DetectionBelowThresholdAfterNormalization_ReturnsReject` — `confidence=30.0`, threshold 0.50 → `Passed == false`, reason starts with `"validator_no_match"`.
3. `ValidateAsync_LabelNotInAllowList_ReturnsReject` — `class="dog"`, `AllowedLabels=["person"]` → reject.
4. `ValidateAsync_NoSnapshot_ReturnsRejectImmediately` — `default(SnapshotContext)` → `Verdict.Fail("validator_no_snapshot")`. Asserts `stub.LogEntries.Should().BeEmpty()`.
5. `ValidateAsync_HttpTimeout_FailClosed_ReturnsReject` — 2s WireMock delay, 200ms client timeout, `FailClosed` → `Verdict.Fail("validator_timeout")`. Asserts EventId 7201 logged via `CapturingLogger<Doods2Validator>`.
6. `ValidateAsync_HttpTimeout_FailOpen_ReturnsAllow` — same setup, `FailOpen` → `Verdict.Pass()`. EventId 7201 still logged.
7. `ValidateAsync_HttpServerError_FailClosed_ReturnsReject` — HTTP 500, `FailClosed` → `Verdict.Fail("validator_unavailable: ...")`. EventId 7202 logged.
8. `ValidateAsync_HttpServerError_FailOpen_ReturnsAllow` — same stub, `FailOpen` → `Verdict.Pass()`. **Closes the FailOpen-on-HTTP-error coverage gap** (mirrors PR #42 REVIEW-1.2 fix from Wave 1).
9. `ValidateAsync_CancellationRequested_PropagatesOperationCanceled` — pre-cancelled token → `Assert.ThrowsAsync<OperationCanceledException>`. Asserts `stub.LogEntries.Should().BeEmpty("a cancelled token must not reach the HTTP layer")` so a future regression that moves the cancellation check after the network call fails the test (matches PR #42 fix).

## Decisions Made

- **Cloned the Roboflow test template verbatim** with three DOODS2 swaps: endpoint path (`/detect` vs `/infer/object_detection`), confidence-scale stub responses (raw 0-100 vs 0-1), EventId range (7201/7202 vs 7101/7102). All other patterns — `JsonPathMatcher` body matching, `CapturingLogger<T>` for log assertions, cancellation-with-empty-LogEntries assertion, FailOpen-on-HTTP-error test — are direct translations of the Wave-1 final shape (post-REVIEW-1.2).
- **CHANGELOG ordering: descending-chronological.** Placed the DOODS2 #14 bullet ABOVE the Roboflow #13 bullet under `### Added` because #14 is more recent (Keep a Changelog convention is flexible on ordering within a section; descending matches the Phase 13 / v1.1 precedent).
- **Test count progression captured:** the builder originally wrote a 258-test baseline target (since 250 baseline + 9 = 259) but the real baseline was 258 (Roboflow's 5 registrar tests landed mid-PR-#42 to satisfy codecov). Final cumulative: 258 + 9 = **267**. Well above the 259 gate from PLAN-2.2.
- **`StubDetectEndpoint` uses `params (string label, double rawConfidence0to100)[]`** — concise C# 12 tuple-param syntax. Mirrors the Roboflow helper's `object response` pattern but with a stricter shape that catches typo regressions in test stubs.

## Issues Encountered

- **None.** No sandbox interruptions, all 3 tasks completed in a single builder session. The DOODS2 test scope is small enough (no protobuf codegen, no in-process gRPC server, just WireMock stubs) that the builder finished cleanly within its context budget.
- The builder reported the SUMMARY inline rather than writing it to disk. Orchestrator wrote this file from the inline content. Lesson seed: builder prompts should include "and write the SUMMARY file as a separate Write tool call before final report" if not already.

## Verification Results

- `dotnet build FrigateRelay.sln -c Release` — **0 warnings, 0 errors**.
- `bash .github/scripts/run-tests.sh --skip-integration` — **267/267 passing, 0 failures**. Test-count gate hit: 258 baseline + 9 new DOODS2 tests = 267. PLAN-2.2 spec floor was 259; we exceed by 8 (the +5 Roboflow registrar tests added late in PR #42).
- `git grep -nE 'Grpc\.' src/` — empty ✓ (gRPC fully gone post-reversal).
- `git grep -nE 'App\.Metrics|OpenTracing|Jaeger\.' src/` — empty ✓.
- `git grep -nE '\.(Result|Wait)\(' src/` — empty ✓.
- `git grep -n 'ServicePointManager' src/` — only doc-comment "never use" references ✓.
- `git grep -n 'Substitute.For<ILogger' tests/FrigateRelay.Plugins.Doods2.Tests/` — empty ✓ (CapturingLogger only, per CLAUDE.md convention).
- 3 atomic commits on `feature/14-doods2-validator`: `a19e060` (csproj), `6ba4259` (9 tests), `04eda39` (CHANGELOG).

## Wave 2 final state

7 commits total on `feature/14-doods2-validator`:
- 4 from PLAN-2.1: `dbc9588` (proto+csproj — superseded by reversal), `bf59ca6` (validator — superseded), `1963657` (DI wiring — superseded), `d027799` (SUMMARY-2.1).
- 1 reversal: `97fcb0e` (drop gRPC scope, HTTP-only).
- 3 from PLAN-2.2: `a19e060` (test scaffold), `6ba4259` (9 tests), `04eda39` (CHANGELOG).

Plus this commit will be `<next>`: SUMMARY-2.2 itself.

Build clean, 267/267 tests, all architectural invariants pass. Ready for REVIEW-2.2 dispatch.

## Notes for REVIEW-2.2 reviewer

- All 9 tests cover the contract spec'd in PLAN-2.2 §Task 2.
- Tests #4 and #9 explicitly assert `stub.LogEntries.Should().BeEmpty()` — this is the regression-catching shape from PR #42's REVIEW-1.2 (the cancellation test no longer relies solely on exception type to prove the cancellation check fires before the HTTP call).
- Test #8 (FailOpen-on-HTTP-500) closes the coverage gap that was originally CodeRabbit's "Important" finding on PR #42's RoboflowValidator. DOODS2 is shipping with the gap pre-closed.
- The `Doods2HttpResponse` / `Doods2Detection` / `Doods2HttpRequest` DTOs are `internal`; tests access them via the `<InternalsVisibleTo Include="FrigateRelay.Plugins.Doods2.Tests" />` MSBuild item already in `src/FrigateRelay.Plugins.Doods2/FrigateRelay.Plugins.Doods2.csproj` (from PLAN-2.1).
- No `DynamicProxyGenAssembly2` `<InternalsVisibleTo>` entry needed because no test mocks an internal type via NSubstitute.
- The CHANGELOG bullet position (above Roboflow #13) is descending-chronological. The reviewer may have an opinion on ascending vs descending — both are defensible per Keep a Changelog.
