# Build Summary: PLAN-1.3 — Registrar unification + CPAI backfill (#30)

## Status: complete

## Tasks Completed
- **Task 1: 3-file atomic refactor** — moved `BaseAddress` + `Timeout` mutations from each registrar's keyed-singleton factory body into the `AddHttpClient(name, (sp, client) => ...)` builder lambda. Identical edit applied to all three registrars; preserves byte-for-byte symmetry. Single atomic commit `64cae5b`.
- **Task 2: CPAI test backfill** — created `CodeProjectAiPluginRegistrarTests.cs` mirroring the DOODS2 5-test shape exactly with type names swapped. Commit `5681b8d`.
- **Task 3: CHANGELOG entry** — `[Unreleased]` `### Changed` section with both bullets per CONTEXT-16 D8 wording. Commit `943ef0e`.

## Files Modified
- `src/FrigateRelay.Plugins.CodeProjectAi/PluginRegistrar.cs`
- `src/FrigateRelay.Plugins.Roboflow/PluginRegistrar.cs`
- `src/FrigateRelay.Plugins.Doods2/PluginRegistrar.cs`
- `tests/FrigateRelay.Plugins.CodeProjectAi.Tests/CodeProjectAiPluginRegistrarTests.cs` (new)
- `CHANGELOG.md`

## Decisions Made
- **Added explicit `using FluentAssertions;` to the new test file.** The CPAI test project uses per-file imports (not a global using in `Usings.cs`), unlike the DOODS2 reference project. Mirrored the target project's existing pattern to stay internally consistent rather than blindly copying the DOODS2 file's reliance on a global using.
- **No `<InternalsVisibleTo Include="DynamicProxyGenAssembly2" />` addition needed.** Neither the DOODS2 baseline nor the new CPAI registrar tests mock internal types via NSubstitute — all assertions go through `IServiceProvider` resolution. The existing CPAI test csproj structure was sufficient (PLAN-1.3 Task 2 step 4 explicitly says "confirm via grep before assuming"; confirmed unnecessary).

## Issues Encountered
- **Builder agent reported its Write tool was sandboxed** for the SUMMARY file specifically. The orchestrator wrote this file directly. The builder's tool budget was exhausted before this file landed; the previous 3 commits were authored by the builder.

## Verification Results
- `dotnet build FrigateRelay.sln -c Release`: **0 warnings, 0 errors**.
- **CPAI tests — full suite:** 13 passed, 0 failed (8 pre-existing `CodeProjectAiValidatorTests` + 5 new `CodeProjectAiPluginRegistrarTests`).
- **CPAI tests — filtered `CodeProjectAiPluginRegistrarTests`:** 5 passed, 0 failed.
- **Roboflow tests:** 16 passed, 0 failed (gates the registrar refactor's behavior preservation — 5 of these are `RoboflowPluginRegistrarTests`).
- **DOODS2 tests:** 14 passed, 0 failed (gates the registrar refactor's behavior preservation — 5 of these are `Doods2PluginRegistrarTests`).
- **Atomic-commit invariant:** `git log --oneline -1 -- src/FrigateRelay.Plugins.CodeProjectAi/PluginRegistrar.cs src/FrigateRelay.Plugins.Roboflow/PluginRegistrar.cs src/FrigateRelay.Plugins.Doods2/PluginRegistrar.cs` returns exactly `64cae5b shipyard(phase-16): unify HttpClient registration in validator plugin registrars (#30)` — single commit hash covers all three paths. CONTEXT-16 D6 satisfied.
- **BaseAddress/Timeout grep:** `git grep -nE '(http|client)\.(BaseAddress|Timeout)\s*=' src/FrigateRelay.Plugins.{CodeProjectAi,Roboflow,Doods2}/` returns **6 hits total** (3 `client.BaseAddress` + 3 `client.Timeout`), all at lines 59–60 of each registrar inside the `AddHttpClient(name, (sp, client) => ...)` builder lambda. Zero hits in factory bodies.

## Lesson Seeds
- **`global using` divergence between sibling test projects.** DOODS2 test project has `global using FluentAssertions;` in `Usings.cs`; CPAI test project does not. When mirroring a test file from a peer project, check the target project's `Usings.cs` rather than assuming the reference project's convention carries over. The mirror-exactly intent is for the test logic, not the import boilerplate.
- **Atomic 3-file commits via single `git add` + `git commit` are clean and verifiable.** The `git log --oneline -1 -- <three paths>` invariant is the right verification for D6 — a single hash is a single hash. No special tooling needed.
- **`AddHttpClient(name, (sp, client) => ...)` builder lambda is the idiomatic place for `BaseAddress`/`Timeout` per Microsoft.Extensions.Http design.** The factory-body mutation pattern that was here before was a v1.0/v1.1 carryover that pre-dated the keyed-DI overload availability. CodeRabbit's #30 callout was correct.
- **Builder's Write tool sandboxing for `.shipyard/phases/N/results/` files** — recurring across multiple plans this phase. The orchestrator now writes SUMMARY files inline based on the agent's final-message dump. Pattern noted for Phase 17 process improvements.

<!-- context: turns=38+orchestrator-inline, compressed=no, task_complete=yes -->
