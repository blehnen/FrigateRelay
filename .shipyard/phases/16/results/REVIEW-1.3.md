# Review: PLAN-1.3 — Registrar unification + CPAI backfill (#30)

## Verdict: PASS

---

## Stage 1 — Spec Compliance

### Must-Have 1: Atomic 3-file commit
- Status: PASS
- Evidence: SUMMARY-1.3 reports `git log --oneline -1 -- <three paths>` returns exactly `64cae5b shipyard(phase-16): unify HttpClient registration in validator plugin registrars (#30)`. All three `PluginRegistrar.cs` files (CodeProjectAi, Roboflow, Doods2) share that single commit hash. CONTEXT-16 D6 satisfied.

### Must-Have 2: Byte-for-byte symmetry across the three registrars
- Status: PASS
- Evidence: Lines 56–61 of each registrar are structurally identical — `AddHttpClient(clientName, (sp, client) => { var opts = sp.GetRequiredService<IOptionsMonitor<XOptions>>().Get(instanceKey); client.BaseAddress = new Uri(opts.BaseUrl); client.Timeout = opts.Timeout; })`. Type names differ only where expected (`CodeProjectAiOptions` / `RoboflowOptions` / `Doods2Options`; `CodeProjectAi:` / `Roboflow:` / `Doods2:` prefix; concrete validator constructors). Zero per-plugin variations in the touched site. Files are 90 lines each, identical structure.
- Notes: `client.BaseAddress` and `client.Timeout` both appear at lines 59–60 of each file, strictly inside the `AddHttpClient` builder lambda. The factory bodies (lines 80–87) contain only `IHttpClientFactory.CreateClient(...)` + validator constructor calls — no `BaseAddress`/`Timeout` mutations remain there.

### Must-Have 3: CPAI registrar test backfill — 5 tests, DOODS2-mirrored names
- Status: PASS
- Evidence: `tests/FrigateRelay.Plugins.CodeProjectAi.Tests/CodeProjectAiPluginRegistrarTests.cs` exists (128 lines). Contains exactly 5 `[TestMethod]` methods with the specified names:
  1. `Register_OneCodeProjectAiEntry_RegistersKeyedValidatorAndNamedHttpClient`
  2. `Register_TwoCodeProjectAiEntries_RegistersBothIndependently`
  3. `Register_NonCodeProjectAiEntry_DoesNotRegisterIt`
  4. `Register_NoValidatorsSection_ReturnsCleanly`
  5. `Register_AllowInvalidCertificatesTrue_ConfiguresTlsBypassHandler`

### Must-Have 4: Roboflow + DOODS2 test preservation (10 tests unchanged)
- Status: PASS
- Evidence: SUMMARY-1.3 reports Roboflow: 16 passed (5 registrar tests among them); DOODS2: 14 passed (5 registrar tests among them). Zero failures. Neither test file was modified in this plan's commits.

### Must-Have 5: Greppable atomic-commit invariant
- Status: PASS
- Evidence: Confirmed by SUMMARY-1.3's verification section — single hash `64cae5b` for all three paths.

### Must-Have 6: CHANGELOG `[Unreleased]` `### Changed` entry for #30
- Status: PASS
- Evidence: `CHANGELOG.md` lines 19–22 contain a `### Changed` section under `[Unreleased]` with two bullets referencing `issue #30` — the registrar-shape unification and the CPAI test backfill. `grep '#30' CHANGELOG.md` returns at least 1 hit (lines 21–22).

### Task 1 Acceptance Criteria
- Status: PASS
- `AddHttpClient(name, (sp, client) => ...)` two-arg form: confirmed in all three registrars at lines 56–61.
- No `http.BaseAddress` or `http.Timeout` in factory bodies: grep across the three plugin directories returns only 6 hits (3 `client.BaseAddress` + 3 `client.Timeout`), all inside the builder lambdas.
- TLS handler chain (`ConfigurePrimaryHttpMessageHandler`) unchanged — present at lines 62–77 of each file, identical to pre-refactor shape.
- Build zero warnings: SUMMARY-1.3 reports `0 warnings, 0 errors`.

### Task 2 Acceptance Criteria
- Status: PASS
- File exists with 5 tests, names match the DOODS2-list with type-substituted prefixes.
- SUMMARY-1.3 reports full CPAI suite: 13 passed (8 pre-existing + 5 new); filtered `CodeProjectAiPluginRegistrarTests`: 5 passed.
- No pre-existing `*Registrar*` file was present to collide with (RESEARCH.md and SUMMARY confirm the directory previously contained only `CodeProjectAiValidatorTests.cs`).
- No NSubstitute on internal types: new test file uses only `IServiceProvider` resolution (no `Substitute.For<>` calls). `DynamicProxyGenAssembly2` `InternalsVisibleTo` omission is correct.
- MSTest v3 + MTP + `Method_Condition_Expected` underscore naming: confirmed. No `CapturingLogger<T>` usage (mirrors DOODS2 baseline which also omits it for these 5 tests).

### Task 3 Acceptance Criteria
- Status: PASS
- `### Changed` section present under `[Unreleased]`, line 19, with #30 reference.
- Second bullet covers CPAI test backfill. Both bullets follow the phase 15 CHANGELOG bullet style.
- CONTEXT-16 D8 mandated `### Changed` for #30 — satisfied.

### CLAUDE.md / PROJECT.md Invariant Checks
- **No `.Result`/`.Wait()`:** grep on `src/FrigateRelay.Plugins.CodeProjectAi/` returns zero matches. PASS.
- **No `ServicePointManager` in src (code):** four files matched on grep, but all hits are inside XML doc comments (`/// <c>ServicePointManager</c>`) only — confirmed in `CodeProjectAiOptions.cs`. Zero executable `ServicePointManager` assignments. PASS.
- **`DynamicProxyGenAssembly2`:** new test file uses no `Substitute.For<>` on any type. No internal types are mocked. Omission confirmed correct per PLAN task 2 step 4. PASS.
- **Plugin contracts unchanged:** `IValidationPlugin`, `IPluginRegistrar`, `PluginRegistrationContext` — none of these files are in the touched set. PASS.
- **Warnings-as-errors:** 0 warnings per SUMMARY-1.3 verification. PASS.

---

## Stage 2 — Code Quality

### Critical

None.

### Minor

None.

### Positive

- **Symmetry is exact.** The three registrar files are 90 lines each. The only differences from `CodeProjectAi` to `Roboflow` to `Doods2` are the type names, string literals, and XML doc cross-references — exactly what was intended. No accidental style drift.
- **`using FluentAssertions;` placement is correct.** CPAI test project's `Usings.cs` omits the global import (confirmed); the new file adds it as an explicit per-file `using`. DOODS2 has it globally; the builder correctly chose per-file for the target project rather than mechanically copying the DOODS2 header.
- **Test fixture logic is idiomatic and non-duplicative.** `BuildProvider(Dictionary<string, string?> kv)` helper is a private static identical in shape to the DOODS2 reference. Fixture setup is appropriately duplicated per-project (not extracted to TestHelpers) because the five registrar test classes are intentionally self-contained and mirror each other — this is the DAMP convention.
- **`Register_AllowInvalidCertificatesTrue_ConfiguresTlsBypassHandler`** correctly avoids asserting on `SocketsHttpHandler` internals. It exercises the branch by resolving the keyed validator (which forces DI to instantiate the factory) and creating the named `HttpClient` — the test verifies "builds without throw" without trying to inspect the handler callback, which would be implementation-coupling. Matches the DOODS2 baseline exactly.
- **`Register_OneCodeProjectAiEntry_RegistersKeyedValidatorAndNamedHttpClient`** asserts three things: keyed `IValidationPlugin` resolves to `CodeProjectAiValidator`, `IOptionsMonitor<CodeProjectAiOptions>.Get(key)` returns the bound options, and the named `HttpClient` factory produces a non-null client. This is the right surface — it exercises the post-refactor binding path (the `BaseAddress`/`Timeout` now live in the `AddHttpClient` lambda that runs when the factory is first invoked). The DOODS2 analogue additionally asserts `DetectorName` from options; the CPAI analogue asserts `MinConfidence`. Both field choices are correct discriminating fields for their respective options types.
- **No AI bloat.** Test methods are 10–16 lines each, comments are concise and match the DOODS2 baseline's `<summary>` style. No extra try/catch, no extra assertions beyond what DOODS2 has.

---

## Findings Summary

PLAN-1.3 is fully implemented. All three `PluginRegistrar.cs` files are byte-for-byte symmetric on the `BaseAddress`/`Timeout` site with the mutations correctly moved into the `AddHttpClient` builder lambda. The 3-file atomic commit constraint is satisfied. The 5-test CPAI backfill mirrors the DOODS2 baseline with correct type substitutions and respects the CPAI project's per-file `using FluentAssertions;` convention. The CHANGELOG `### Changed` entry covers both bullets for #30. No invariant violations, no quality issues.

Critical: 0 | Important: 0 | Suggestions: 0

<!-- context: turns=14, compressed=no, task_complete=yes -->
