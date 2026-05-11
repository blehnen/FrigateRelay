---
phase: 16-v1.3.0-minor
plan: 1.3
wave: 1
dependencies: []
must_haves:
  - "Atomic 3-file commit (per CONTEXT-16 D6) moving BaseAddress + Timeout configuration from each registrar's keyed-singleton factory body into the AddHttpClient builder lambda."
  - "All three registrars (CodeProjectAi, Roboflow, Doods2) end up byte-for-byte symmetric on the BaseAddress/Timeout site (RESEARCH.md confirms zero per-plugin variants today)."
  - "CPAI registrar test backfill: 5 tests in tests/FrigateRelay.Plugins.CodeProjectAi.Tests/CodeProjectAiPluginRegistrarTests.cs mirroring the Doods2PluginRegistrarTests.cs shape exactly."
  - "Roboflow + DOODS2 already have 5 registrar tests each (RC-2) — no backfill needed for those two."
  - "Greppable invariant: `git log --oneline -1 -- src/FrigateRelay.Plugins.CodeProjectAi/PluginRegistrar.cs src/FrigateRelay.Plugins.Roboflow/PluginRegistrar.cs src/FrigateRelay.Plugins.Doods2/PluginRegistrar.cs` shows a single commit covering all three (Task 1's atomic-commit acceptance)."
  - "CHANGELOG.md [Unreleased] ### Changed entry for #30."
files_touched:
  - src/FrigateRelay.Plugins.CodeProjectAi/PluginRegistrar.cs
  - src/FrigateRelay.Plugins.Roboflow/PluginRegistrar.cs
  - src/FrigateRelay.Plugins.Doods2/PluginRegistrar.cs
  - tests/FrigateRelay.Plugins.CodeProjectAi.Tests/CodeProjectAiPluginRegistrarTests.cs
  - CHANGELOG.md
tdd: false
risk: low
---

# Plan 1.3: Registrar HttpClient unification + CPAI test backfill (#30)

## Context

Issue #30 unifies HttpClient registration shape across the three validation-plugin registrars. Per RESEARCH.md, all three are byte-for-byte symmetric today (CodeRabbit's claim confirmed) — `BaseAddress` + `Timeout` are mutated in the keyed-singleton factory body at lines 75–84 of each. The cleaner shape (per CodeRabbit) is to move that mutation into the existing `AddHttpClient(name, (sp, client) => ...)` builder lambda. CONTEXT-16 D6 mandates an atomic 3-file commit (no per-plugin split — landing one in isolation creates lint-style drift across the trio). RESEARCH.md RC-2 corrects ISSUES.md ID-30: DOODS2 **already has** 5 registrar tests from Phase 14; only **CPAI** needs backfill (5 tests, not 10). D8 puts the CHANGELOG entry under `### Changed`. No operator-visible behavior change — internal API consistency cleanup.

## Dependencies

None — Wave 1 root. File-disjoint from PLAN-1.1 and PLAN-1.2 except CHANGELOG.md (sequential build dispatch).

## Tasks

### Task 1: Move BaseAddress + Timeout into AddHttpClient builder lambda — atomic 3-file commit
**Files:**
- `src/FrigateRelay.Plugins.CodeProjectAi/PluginRegistrar.cs`
- `src/FrigateRelay.Plugins.Roboflow/PluginRegistrar.cs`
- `src/FrigateRelay.Plugins.Doods2/PluginRegistrar.cs`

**Action:** refactor (registration shape only; behavior preserved)

**Description:**
1. For each of the three registrars, locate the keyed-singleton factory body at lines ~75–84 (RESEARCH.md confirms identical line ranges) where `http.BaseAddress = new Uri(opts.BaseUrl); http.Timeout = opts.Timeout;` is set on the resolved `HttpClient` after `IHttpClientFactory.CreateClient(...)`.
2. Move both mutations into the **existing** `AddHttpClient(name, ...)` call (lines ~54–72) by switching the builder overload to the two-argument form `AddHttpClient(name, (sp, client) => { var opts = sp.GetRequiredService<IOptionsMonitor<XOptions>>().Get(instanceKey); client.BaseAddress = new Uri(opts.BaseUrl); client.Timeout = opts.Timeout; })`. Preserve the existing `.ConfigurePrimaryHttpMessageHandler(sp => { /* TLS handler */ })` chain unchanged — the TLS bypass logic is independent.
3. The keyed-singleton factory body simplifies to fetch `IHttpClientFactory.CreateClient(name)` and pass the already-configured `HttpClient` directly to the validator constructor (no further mutation).
4. Apply the **identical** edit to all three files. RESEARCH.md confirms zero per-plugin variants — preserve symmetry.
5. **Atomic commit:** stage and commit all three files together in a single commit with message referencing #30. The greppable invariant `git log --oneline -1 -- <three paths>` must show exactly one commit covering all three. (Note: this plan does not commit — the build agent does. Spell out the atomic-commit requirement so it lands as a single commit.)
6. Existing tests in `RoboflowPluginRegistrarTests.cs` (5 tests) and `Doods2PluginRegistrarTests.cs` (5 tests) gate behavior preservation — neither asserts `BaseAddress`/`Timeout` flow per RESEARCH.md, but they assert keyed-service resolution + named-`HttpClient` resolution + 2-entry independence + no-section-clean + TLS-handler-builds-without-throw. All 10 must continue to pass.

**TDD:** false (registration-shape refactor; existing 10 plugin-registrar tests across Roboflow + DOODS2 prove behavior preservation)

**Acceptance Criteria:**
- All three registrars' `AddHttpClient(name, ...)` calls use the `(sp, client) => ...` two-arg builder form.
- The factory bodies no longer contain `http.BaseAddress` or `http.Timeout` mutations: `git grep -nE '(http|client)\.(BaseAddress|Timeout)\s*=' src/FrigateRelay.Plugins.CodeProjectAi/ src/FrigateRelay.Plugins.Roboflow/ src/FrigateRelay.Plugins.Doods2/` should show the assignments inside the `AddHttpClient` lambdas only (3 `client.BaseAddress` + 3 `client.Timeout` hits across the trio, all inside the builder lambda).
- `dotnet run --project tests/FrigateRelay.Plugins.Roboflow.Tests -c Release` passes (5 tests).
- `dotnet run --project tests/FrigateRelay.Plugins.Doods2.Tests -c Release` passes (5 tests).
- `dotnet build FrigateRelay.sln -c Release` zero warnings.

### Task 2: CPAI registrar test backfill (5 tests, mirror DOODS2 shape exactly)
**Files:**
- `tests/FrigateRelay.Plugins.CodeProjectAi.Tests/CodeProjectAiPluginRegistrarTests.cs` (new)

**Action:** create (test backfill)

**Description:**
1. Mirror `tests/FrigateRelay.Plugins.Doods2.Tests/Doods2PluginRegistrarTests.cs` exactly — same 5 tests, same assertion shapes, same fixture setup, only type names swapped (`Doods2` → `CodeProjectAi`, `Doods2Options` → `CodeProjectAiOptions`, `Doods2Validator` → `CodeProjectAiValidator`). The 5 tests per RESEARCH.md:
   - `Register_OneCodeProjectAiEntry_RegistersKeyedValidatorAndNamedHttpClient`
   - `Register_TwoCodeProjectAiEntries_RegistersBothIndependently`
   - `Register_NonCodeProjectAiEntry_DoesNotRegisterIt`
   - `Register_NoValidatorsSection_ReturnsCleanly`
   - `Register_AllowInvalidCertificatesTrue_ConfiguresTlsBypassHandler`
2. Verify the CPAI test project (`tests/FrigateRelay.Plugins.CodeProjectAi.Tests/`) currently contains only `CodeProjectAiValidatorTests.cs` (RESEARCH.md confirms). If a `*Registrar*` file exists already, stop and report — do not duplicate.
3. Tests follow the project test conventions: MSTest v3 + MTP, `Method_Condition_Expected` underscore naming (`CA1707` silenced for tests via `.editorconfig`), `CapturingLogger<T>` from `tests/FrigateRelay.TestHelpers/` if any logger assertion is needed (the DOODS2 baseline does not use it for these 5 tests; mirror that decision).
4. If NSubstitute is used to mock any `internal` types (e.g., the validator interface), the `CodeProjectAi.Tests.csproj` must already have `<InternalsVisibleTo Include="DynamicProxyGenAssembly2" />` from prior work — confirm via grep before assuming. If missing, add it (this is a per-csproj convention from CLAUDE.md).
5. The `*Tests.csproj` is auto-discovered by `.github/scripts/run-tests.sh` (the `find tests -maxdepth 2 -name '*Tests.csproj'` glob) — no CI workflow changes are needed.

**TDD:** false (backfill — proves existing behavior; behavior is already gated by Roboflow + DOODS2's identical tests post-Task-1)

**Acceptance Criteria:**
- `tests/FrigateRelay.Plugins.CodeProjectAi.Tests/CodeProjectAiPluginRegistrarTests.cs` exists with exactly 5 tests, names matching the DOODS2 list above with type-substituted prefixes.
- `dotnet run --project tests/FrigateRelay.Plugins.CodeProjectAi.Tests -c Release -- --filter "CodeProjectAiPluginRegistrarTests"` runs and passes 5 tests.
- `dotnet run --project tests/FrigateRelay.Plugins.CodeProjectAi.Tests -c Release` runs the full suite (existing `CodeProjectAiValidatorTests` + new registrar tests) — all pass, no regressions.

### Task 3: CHANGELOG entry for #30
**Files:**
- `CHANGELOG.md`

**Action:** modify (docs)

**Description:**
1. Add `[Unreleased]` `### Changed` entry per CONTEXT-16 D8:
   - `- Unified HttpClient registration shape across the three validation-plugin registrars (CodeProjectAi, Roboflow, DOODS2): \`BaseAddress\` and \`Timeout\` now live in the \`AddHttpClient\` builder lambda rather than in the keyed-singleton factory body (issue #30). Internal cleanup; no operator-visible behavior change.`
2. Add `### Tests` (or fold under an existing test-coverage bullet — implementer's choice consistent with prior Phase 15 CHANGELOG style) noting the CPAI registrar-test backfill (5 new tests, parity with Roboflow + DOODS2).

**TDD:** false

**Acceptance Criteria:**
- CHANGELOG `[Unreleased]` block contains a `### Changed` line referencing #30 and the registrar-shape unification.
- `grep -n '#30' CHANGELOG.md` returns at least 1 hit.

## Verification

```bash
# Build clean (warnings-as-errors)
dotnet build FrigateRelay.sln -c Release

# CPAI test project — full suite + new registrar tests
dotnet run --project tests/FrigateRelay.Plugins.CodeProjectAi.Tests -c Release
dotnet run --project tests/FrigateRelay.Plugins.CodeProjectAi.Tests -c Release -- --filter "CodeProjectAiPluginRegistrarTests"

# Roboflow + DOODS2 still green (behavior gates for the registrar-shape refactor)
dotnet run --project tests/FrigateRelay.Plugins.Roboflow.Tests -c Release
dotnet run --project tests/FrigateRelay.Plugins.Doods2.Tests -c Release

# Atomic-commit invariant (after build agent commits)
git log --oneline -1 -- src/FrigateRelay.Plugins.CodeProjectAi/PluginRegistrar.cs \
                       src/FrigateRelay.Plugins.Roboflow/PluginRegistrar.cs \
                       src/FrigateRelay.Plugins.Doods2/PluginRegistrar.cs
# Expect: a single commit hash covering all three paths.

# No BaseAddress/Timeout mutations remain in factory bodies (only inside AddHttpClient lambdas)
git grep -nE '(http|client)\.(BaseAddress|Timeout)\s*=' src/FrigateRelay.Plugins.CodeProjectAi/ \
                                                       src/FrigateRelay.Plugins.Roboflow/ \
                                                       src/FrigateRelay.Plugins.Doods2/
# Expect: 6 hits total (3 BaseAddress + 3 Timeout), all on lines inside the AddHttpClient builder.
```

## Notes

- **CHANGELOG.md is shared with PLAN-1.1 and PLAN-1.2.** Sequential dispatch in build phase eliminates merge friction (Phase 15 lesson).
- **Atomic 3-file commit (D6) is non-negotiable.** Splitting #30 across multiple commits creates exactly the lint-style drift CodeRabbit flagged.
- **DOODS2 already has 5 tests (RC-2).** Do NOT add `Doods2PluginRegistrarTests.cs` — it exists from Phase 14. ISSUES.md ID-30 description is stale; RESEARCH.md is the source of truth.
