# Build Summary: Plan 1.2

## Status: complete

## Tasks Completed

- **Task 1: Scaffold `tests/FrigateRelay.Plugins.Roboflow.Tests/`** — complete — files: `tests/FrigateRelay.Plugins.Roboflow.Tests/{FrigateRelay.Plugins.Roboflow.Tests.csproj,Usings.cs}` + `FrigateRelay.sln` entry. Commit `443201c`.
- **Task 2: 8 RoboflowValidator unit tests (WireMock-driven)** — complete — file: `tests/FrigateRelay.Plugins.Roboflow.Tests/RoboflowValidatorTests.cs`. Commit `e5d506b`.
- **Task 3: CHANGELOG bullet under `[Unreleased]` `### Added` for issue #13** — complete. Includes the manual-smoke recipe per CONTEXT-14 OQ-2 fallback (Roboflow Testcontainers image is ~16 GB, exceeds GitHub Actions disk budget). Commit `efb882c`.

## Files Modified

- `tests/FrigateRelay.Plugins.Roboflow.Tests/FrigateRelay.Plugins.Roboflow.Tests.csproj` (created) — `OutputType=Exe`, MSTest v3 + `Microsoft.Testing.Platform`, FluentAssertions 6.12.2, NSubstitute, `WireMock.Net`. `<ProjectReference>` to `FrigateRelay.Plugins.Roboflow` + `FrigateRelay.TestHelpers`.
- `tests/FrigateRelay.Plugins.Roboflow.Tests/Usings.cs` (created) — `global using FrigateRelay.TestHelpers;` so `CapturingLogger<T>` is available; `global using FluentAssertions;`.
- `tests/FrigateRelay.Plugins.Roboflow.Tests/RoboflowValidatorTests.cs` (created) — 8 `[TestMethod]` cases driving the validator via a WireMock-stubbed HTTP server. Tests use `CapturingLogger<RoboflowValidator>` (NOT NSubstitute on `ILogger<T>` — CLAUDE.md convention) to assert EventIds 7101 (timeout) and 7102 (unavailable).
- `FrigateRelay.sln` — added the new test project.
- `CHANGELOG.md` — `[Unreleased]` gains a new `### Added` section with the Roboflow validator bullet (#13) + manual-smoke recipe.

## The 8 tests

1. `ValidateAsync_AllowedLabelHighConfidence_Pass` — WireMock returns `{predictions:[{class:"person",confidence:0.92}]}`; AllowedLabels=["person"], MinConfidence=0.5 → `Verdict.Pass(0.92)`.
2. `ValidateAsync_LowConfidence_Fail` — confidence 0.30 below MinConfidence 0.50 → `Verdict.Fail("validator_no_match: ...")`.
3. `ValidateAsync_LabelNotAllowed_Fail` — class="cat" not in AllowedLabels=["person"] → reject.
4. `ValidateAsync_NoSnapshot_Fail` — `SnapshotContext.ResolveAsync` returns null → `Verdict.Fail("validator_no_snapshot")`. WireMock NOT called.
5. `ValidateAsync_Timeout_FailClosed_Fail` — WireMock delays 5s; HttpClient.Timeout=200ms → `Verdict.Fail("validator_timeout")`. Asserts EventId 7101 logged via `CapturingLogger`.
6. `ValidateAsync_Timeout_FailOpen_Pass` — same setup, OnError=FailOpen → `Verdict.Pass()`. EventId 7101 still logged.
7. `ValidateAsync_HttpError_FailClosed_Fail` — WireMock returns HTTP 500 → `Verdict.Fail("validator_unavailable: ...")`. EventId 7102 logged.
8. `ValidateAsync_HostShutdownCancellation_Throws` — external `CancellationToken` cancelled before HTTP call returns → `OperationCanceledException` propagates (NOT caught). Verifies catch-block ordering.

## Decisions Made

- **EventId assertion strategy:** the tests assert specific EventIds (7101 for timeout, 7102 for unavailable) so future renumbering of `LoggerMessage` IDs would fail tests deliberately — making EventID drift expensive to ship by accident. Aligns with the RESEARCH §1.4 EventId range convention (Roboflow 7100–7199).
- **WireMock JSON-body matching:** the "happy path" stub matches `JsonPath` for `model_id` to ensure tests catch a typo in `RoboflowOptions.ModelId` propagation. The "label-not-allowed" stub uses a different `class` value to validate the label-filter logic separately.
- **Test for cancellation propagation (#8) is critical** — proves the catch-block ordering invariant in `RoboflowValidator.ValidateAsync` (`OperationCanceledException when ct.IsCancellationRequested` rethrows BEFORE `TaskCanceledException` catches it). Swapping these would silently break host-shutdown propagation; this test asserts that didn't happen.

## Issues Encountered

- **SUMMARY-1.1.md transcription error caught by reviewer.** SUMMARY-1.1 originally said EventIds 7100–7101; actual code has 7101–7102. Fixed in commit `af839d3` along with the REVIEW-1.1.md PASS verdict, then PLAN-1.2 tests were written against the actual EventIds.
- **CHANGELOG `[Unreleased]` had only `### Fixed` (from ID-29 hotfix); no `### Added` yet.** Builder added a new `### Added` section above the existing `### Fixed`, preserving Keep-a-Changelog ordering.
- **Builder agent stopped before committing Task 3.** Same pattern as PLAN-1.1: CHANGELOG.md was modified-but-uncommitted when the builder's session ended. Orchestrator committed the staged CHANGELOG.md unchanged. **Lesson seed:** orchestrator should be prepared to take over Task 3 cleanup as a default — adding `git commit` instructions to the builder prompt with explicit "commit before reporting" doesn't seem to help when the agent runs out of internal turns.

## Verification Results

- `dotnet build FrigateRelay.sln -c Release` — **0 warnings, 0 errors**, 17.2s elapsed.
- `bash .github/scripts/run-tests.sh --skip-integration` — **250/250 passing, 0 failures**. Test-count gate hit exactly: 242 baseline + 8 new Roboflow tests = 250.
  - `FrigateRelay.Plugins.Roboflow.Tests`: 8/8 passing (the new project). Other projects' counts unchanged.
- `git grep -nE 'Grpc\.' src/` — empty ✓.
- `git grep -nE 'App\.Metrics|OpenTracing|Jaeger\.' src/` — empty ✓.
- `git grep -nE '\.(Result|Wait)\(' src/` — empty ✓.
- `git grep -n 'ServicePointManager' src/` — three doc-comment hits only (negative references). ✓
- 3 atomic commits on `feature/13-roboflow-validator`: `443201c` (csproj), `e5d506b` (8 tests), `efb882c` (CHANGELOG).

## Wave 1 final state

8 commits total on `feature/13-roboflow-validator`:
- 5 from PLAN-1.1: `b0048de` scaffold, `281aac2` registrar, `0340e5b` DI/sln, `bd43a6c` SUMMARY-1.1, `af839d3` REVIEW-1.1 + EventId fix.
- 3 from PLAN-1.2: `443201c` test scaffold, `e5d506b` 8 tests, `efb882c` CHANGELOG.

Build clean, 250/250 tests, all architectural invariants pass. Ready for REVIEW-1.2 dispatch.
