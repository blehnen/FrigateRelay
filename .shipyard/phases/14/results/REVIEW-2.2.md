# Review: Plan 2.2

## Verdict: PASS (after one revision cycle addressing 2 Important findings)

Reviewer agent (sonnet) returned APPROVE with 0 critical, 2 Important, 2 Suggestions on commit `cffac3f`. Both Importants + 1 Suggestion were addressed inline before opening the PR.

## Findings

### Critical

None.

### Important (both addressed)

1. **`Doods2Validator.cs:75` sent `MinConfidence` (0-1 scale) as the threshold in the DOODS2 `detect` dict, which expects 0-100 scale.** Operator config `MinConfidence=0.5` was sent to DOODS2 as `{"*": 0.5}` instead of `{"*": 50.0}`, causing DOODS2 to filter at 0.5% confidence (effectively no server-side filter). Masked in practice because the plugin's own `EvaluateDetections` post-filter normalizes per-detection (0-100 → 0-1) and re-applies the threshold. Operators saw correct verdicts but the DOODS2 server-side filter was being mis-encoded — silent semantic mismatch. **Fixed:** `_opts.MinConfidence * 100.0`. Added a regression-locking assertion in Test 1: `body!.Should().Contain("\"*\":50")` so a future regression that drops the multiplier fails the build.

2. **`Doods2Validator.ValidateAsync` had no explicit `ct.ThrowIfCancellationRequested()` at the top of the try block.** Test 9 (cancellation) relied on `HttpClient`'s pre-cancel behavior to ensure no request reaches the wire. This is generally true but is an implementation-detail dependency. Same pattern exists in `RoboflowValidator` on main (parity, not blocker). **Fixed for DOODS2:** added `ct.ThrowIfCancellationRequested();` as the first line in the try block. Test 9's `stub.LogEntries.Should().BeEmpty()` assertion is now self-documenting — if a future refactor moves the cancellation check, the explicit guard fails fast. Roboflow on `main` should get the same fix in a follow-up commit (noted as a v1.2.x cleanup item; not in this PR's scope).

### Suggestion (1 of 2 addressed)

- **Tests 5–8 use inline `stub.Given(...)` instead of the `StubDetectEndpoint` helper** because they exercise error-path handling where the request-body contract is irrelevant. **Added comment** at Test 5's stub site noting this and pointing to Test 1's body-shape assertion as the contract-guard locus.
- **`Microsoft.Extensions.Hosting` package reference** in the test csproj — reviewer flagged as possibly unused. **Not changed** — same package set as the Roboflow test csproj on `main`, kept for parity. If a future cleanup pass removes it from Roboflow, DOODS2 should follow.

### Positive

- All 9 tests cover the contract spec'd in PLAN-2.2: allow-after-normalization / reject-low-confidence / reject-bad-label / no-snapshot / timeout-FailClosed/Open / unavailable-FailClosed/Open / cancellation. The unavailable-FailOpen test (#8) closes the coverage gap that PR #42 RoboflowValidator caught in REVIEW-1.2 — DOODS2 ships with the gap pre-closed.
- Test 1 is the load-bearing 0-100 → 0-1 normalization assertion (`Verdict.Score.Should().BeApproximately(0.80, 0.001)`).
- Test 9 (cancellation) now asserts BOTH `Assert.ThrowsAsync<OperationCanceledException>` AND `stub.LogEntries.Should().BeEmpty(...)` — regression-catching shape from PR #42 REVIEW-1.2.
- `CapturingLogger<Doods2Validator>` used throughout; no NSubstitute on `ILogger<T>` (CLAUDE.md convention).
- WireMock body matchers (`JsonPathMatcher` on `$.detector_name`, `$.data`, `$.detect`) protect the contract per PR #42 REVIEW-1.2 lesson.
- EventId assertions (7201 timeout, 7202 unavailable) match `Doods2Validator.cs` `LoggerMessage` declarations exactly.
- csproj is a clean mirror of the Roboflow test csproj (FluentAssertions 6.12.2 license-pinned, MSTest 4.2.2, NSubstitute 5.3.0, WireMock.Net 2.4.0). **No `Grpc.*` references** — the gRPC scope reversal is fully visible.
- CHANGELOG bullet under `[Unreleased]` `### Added` references issue #14, explicitly notes "HTTP-only" twice + the `snowzach/doods2` upstream rationale, lists `DetectorName` options, MinConfidence normalization note, all relevant config knobs.

## Stage 1 (Correctness) check results

- 9 tests cover the contract: **PASS**
- Test 1 asserts `BeApproximately(0.80, 0.001)`: **PASS**
- Test 1 (post-fix) asserts request body contains `"*":50` — locks the 0-100 scale on the DOODS2 `detect` dict: **PASS**
- Test 4 (no-snapshot) asserts `stub.LogEntries.Should().BeEmpty()`: **PASS**
- Test 9 (cancellation) asserts `ThrowsAsync<OperationCanceledException>` AND `LogEntries.Should().BeEmpty()`: **PASS**
- Test names use underscores: **PASS**
- `CapturingLogger<T>` used, no NSubstitute on `ILogger<T>`: **PASS**
- EventId 7201 / 7202 match source: **PASS**
- WireMock stub shape matches DOODS2 v2 OpenAPI: **PASS**
- csproj package set matches Roboflow exemplar; no `Grpc.*`: **PASS**
- Usings.cs has FluentAssertions, FrigateRelay.TestHelpers, MSTest globals: **PASS**

## Stage 2 (Integration) check results

- CHANGELOG bullet under `[Unreleased]` `### Added` references #14, "HTTP-only" callout, DetectorName options: **PASS**
- `### Added` ordering relative to `### Fixed`: **PASS**
- Test count progression 258 → 267 (=+9 new): **PASS**
- `FrigateRelay.sln` has the new test project entry: **PASS**
- Architectural invariants (no `Grpc.*`, `App.Metrics`, `OpenTracing`, `Jaeger.*`, `.Result`, `.Wait`): **PASS**
- CI auto-discovery picks up the new test project: **PASS**

## Final verdict

**PASS.** Wave 2 (`feature/14-doods2-validator`) is ready to PR to `main`. 9 commits total: 4 from PLAN-2.1 (scaffold, validator, DI, SUMMARY), 1 reversal commit (gRPC dropped), 3 from PLAN-2.2 build (csproj, 9 tests, CHANGELOG), 1 SUMMARY-2.2, plus this review's revision commit (Important findings 1+2 addressed inline). Build clean, 267/267 tests, all architectural invariants pass.

The pre-emptive scale-mismatch fix turned a "works by accident" silent semantic bug into a regression-locked correctness invariant. The cancellation guard makes the test intent self-documenting and removes the reliance on `HttpClient` internals.
