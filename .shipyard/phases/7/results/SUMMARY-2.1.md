# Build Summary: Plan 7.2.1 — `FrigateRelay.Plugins.CodeProjectAi` project + validator + registrar

## Status: complete

## Tasks Completed

- **Task 1 + Task 3 (bundled)** — New plugin project `src/FrigateRelay.Plugins.CodeProjectAi/`: csproj with explicit `<TargetFramework>net10.0</TargetFramework>` (per ID-3 advisory direction), references Abstractions + `Microsoft.Extensions.{Http,Options,Options.DataAnnotations,Options.ConfigurationExtensions}` (NO `Microsoft.Extensions.Http.Resilience` — CONTEXT-7 D4 lock-in), `<InternalsVisibleTo>` MSBuild item for the test assembly. `CodeProjectAiOptions` (CONTEXT-7 D5 verbatim) + `ValidatorErrorMode` enum. Internal `CodeProjectAiResponse` + `CodeProjectAiPrediction` DTOs. Full `CodeProjectAiValidator` implementation (sealed partial class for `[LoggerMessage]` source-gen) — multipart POST, decision rule, OnError catch-block ordering per RESEARCH §6. Full `PluginRegistrar` enumerating top-level `Validators` config dict, filtering `Type == "CodeProjectAi"`, registering keyed `IValidationPlugin` per instance with named-options + per-instance HttpClient + per-instance TLS handler (no `AddResilienceHandler`). Both projects added to `FrigateRelay.sln`. Commit `072961c`.
- **Task 2 (TDD)** — 8 unit tests in `tests/FrigateRelay.Plugins.CodeProjectAi.Tests/CodeProjectAiValidatorTests.cs` covering the full decision matrix: confidence pass / fail, allowed-label gate hit / miss / empty-allows-any, FailClosed timeout / FailOpen timeout, multipart wire-format unquoted-name assertion, full v2.x response parsing happy path. WireMock.Net 2.4.0 stubs the CodeProject.AI endpoint. `MakeSnapshot()` uses the Phase 7 `SnapshotContext(SnapshotResult?)` pre-resolved ctor so tests are self-contained. Commit `be28f4c`.

## Files Modified

- `src/FrigateRelay.Plugins.CodeProjectAi/FrigateRelay.Plugins.CodeProjectAi.csproj` (new)
- `src/FrigateRelay.Plugins.CodeProjectAi/CodeProjectAiOptions.cs` (new)
- `src/FrigateRelay.Plugins.CodeProjectAi/CodeProjectAiResponse.cs` (new)
- `src/FrigateRelay.Plugins.CodeProjectAi/CodeProjectAiValidator.cs` (new)
- `src/FrigateRelay.Plugins.CodeProjectAi/PluginRegistrar.cs` (new)
- `tests/FrigateRelay.Plugins.CodeProjectAi.Tests/FrigateRelay.Plugins.CodeProjectAi.Tests.csproj` (new)
- `tests/FrigateRelay.Plugins.CodeProjectAi.Tests/Usings.cs` (new)
- `tests/FrigateRelay.Plugins.CodeProjectAi.Tests/CodeProjectAiValidatorTests.cs` (new)
- `FrigateRelay.sln` (added 2 projects)

## Decisions Made

- **`Verdict.Pass(score)` carries the matched prediction's confidence.** Test 8 asserts `verdict.Score == 0.87` for the qualifying person prediction. The `Verdict` static factory `Pass(double score)` exists for exactly this case (Phase 1 D3) and the validator should use it rather than the parameterless `Pass()`. This makes operator dashboards more useful (the captured score appears in OTel attributes downstream).
- **`sealed partial class CodeProjectAiValidator`.** The `[LoggerMessage]` source-gen requires partial methods, which require the containing class to be partial. The outer type's `partial` modifier is inherited from C#'s nested-partial requirement (a non-partial outer class can't host a partial nested class). `sealed partial class` is the correct combination — sealed is preserved, partial is added for the source-gen.
- **`Score: float?` vs `score: double` precision conversion.** `Verdict.Score` is `float?` (Phase 1 design); the prediction's `confidence` is `double` (matches CodeProject.AI JSON). Test 8 asserts `BeApproximately(0.87, 0.001)` to absorb the float-vs-double precision delta.
- **CA5359 suppression scoped to AllowInvalidCertificates branch only**, mirroring BlueIris precedent (`#pragma warning disable CA5359` ... `#pragma warning restore CA5359`). No global `<NoWarn>` entry — the suppression is local to the intentional opt-in code path, so future accidental `RemoteCertificateValidationCallback` usage elsewhere in the codebase still gets caught.
- **`MakeSnapshot()` test helper uses `new SnapshotContext(SnapshotResult)`** — the Phase 7 PreResolved ctor (PLAN-1.1 Task 2). Tests don't construct an `ISnapshotResolver`; they just pre-bake a `SnapshotResult` and pass it through. This keeps the test surface tight and demonstrates the dispatcher's intended sharing pattern.

## Issues Encountered

- **CS0260 missing partial modifier.** The `[LoggerMessage]` source-gen pattern requires `partial methods` inside `partial class Log`, but a partial nested class needs the OUTER class to be partial too. First build failed; fixed with `sealed partial class CodeProjectAiValidator`.
- **CA5359 (always-true certificate callback)** flagged the `AllowInvalidCertificates` branch. This is the same analyzer warning BlueIris already suppresses with a scoped `#pragma warning disable/restore CA5359`. Mirrored that pattern.
- **CS8602 in test file** on `req.RequestMessage.BodyAsBytes!` — `RequestMessage` itself is nullable in the WireMock log type. Fixed with `req.RequestMessage?.BodyAsBytes ?? throw new InvalidOperationException(...)` for clearer test failure semantics.
- **No CI workflow edits needed.** `.github/scripts/run-tests.sh` auto-discovers test projects via `find tests -maxdepth 2 -name '*Tests.csproj'`. Phase 3's extraction means the third test project (Rule of Three trigger) drops in with zero workflow changes — the original run-tests.sh design absorbed exactly this scenario.

## Verification Results

- `dotnet build FrigateRelay.sln -c Release` — **0 warnings, 0 errors**.
- `dotnet run --project tests/FrigateRelay.Plugins.CodeProjectAi.Tests -c Release --no-build` — **8/8 tests pass** in 2.2s.
- Architecture invariant greps (will run phase-wide at Step 5).
- `git grep -n "AddResilienceHandler" src/FrigateRelay.Plugins.CodeProjectAi/` — empty (intentional, CONTEXT-7 D4).

## Lesson seeding (for `/shipyard:ship`)

- **`[LoggerMessage]` source-gen forces `partial` up the nesting chain.** A `partial Log` nested class requires the outer class to be `partial`. This is a one-time learning cost per new plugin assembly — capturing it in CLAUDE.md `## Conventions` as "modern logger source-gen requires `partial class` declaration" would save 5 minutes of future debug time.
- **`Verdict.Pass(score)` is underused.** Phase 1 introduced the score-carrying overload but most plugins just use the parameterless `Pass()`. CodeProject.AI is the first place where carrying the score makes operator dashboards meaningfully better — the matched prediction's confidence becomes available downstream in OTel attributes / structured logs without re-inferring it. Future plugins emitting confidence-like metrics should follow this pattern.
- **Auto-discovery via `run-tests.sh`'s `find` glob is paying off**. Phase 3's extraction (initially flagged as Rule of Two violation by Phase 2 simplifier) absorbed this third test project with zero edit. The architect was right to extract early; the simplifier was wrong to flag it.