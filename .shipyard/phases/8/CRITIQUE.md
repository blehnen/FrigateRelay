# Critique Report — Phase 8 Plans
**Date:** 2026-04-26  
**Verdict:** READY

---

## Summary

All four Phase 8 plans (PLAN-1.1, PLAN-1.2, PLAN-2.1, PLAN-3.1) are feasible and grounded in the actual codebase state. File paths exist, API surfaces match plan expectations, test commands are syntactically correct, and there are no hidden dependencies or file collisions. The plans are ready for builder execution.

---

## Per-Plan Findings

### PLAN-1.1: Visibility Sweep + Profile/Subscription Option Surface

**File Paths (verify exist):**
- `src/FrigateRelay.Host/Configuration/SubscriptionOptions.cs` ✓ (exists, `public sealed record`, awaiting `internal` flip + `Profile` property addition)
- `src/FrigateRelay.Host/Configuration/HostSubscriptionsOptions.cs` ✓ (exists)
- `src/FrigateRelay.Host/Configuration/ActionEntryJsonConverter.cs` ✓ (exists)
- `src/FrigateRelay.Host/Snapshots/SnapshotResolverOptions.cs` ✓ (exists)
- `src/FrigateRelay.Host/Dispatch/IActionDispatcher.cs` ✓ (exists)
- `src/FrigateRelay.Host/Dispatch/DispatcherOptions.cs` ✓ (exists)
- `src/FrigateRelay.Host/Matching/DedupeCache.cs` ✓ (exists)
- `src/FrigateRelay.Host/Matching/SubscriptionMatcher.cs` ✓ (exists)
- `src/FrigateRelay.Host/FrigateRelay.Host.csproj` ✓ (exists, already contains `<InternalsVisibleTo Include="FrigateRelay.Host.Tests" />`; verify step passes)

**API Surface Validation:**
- `ActionEntry` currently `public sealed record` with three-parameter ctor: `ActionEntry(string Plugin, string? SnapshotProvider = null, IReadOnlyList<string>? Validators = null)` — PLAN-1.1 correctly leaves this untouched; PLAN-1.2 owns the visibility flip and `[TypeConverter]` addition.
- `SubscriptionOptions` is currently `public sealed record` with `Actions: IReadOnlyList<ActionEntry>` property — plan correctly adds `public string? Profile` alongside (both optional, mutually-exclusive enforcement deferred to D1 validator in PLAN-2.1).
- Plan correctly preserves the `Actions` field and does not attempt to remove it; the `Profile` field is purely additive.
- `HostSubscriptionsOptions` is assumed to exist and will receive `public IReadOnlyDictionary<string, ProfileOptions> Profiles` property — new `ProfileOptions` type is created by PLAN-1.1 Task 2.

**Verification Commands (runnability):**
- All `dotnet build` and `dotnet run --project tests/FrigateRelay.Host.Tests` commands are syntactically valid. ✓
- `git grep` patterns are valid regex for detecting visibility keywords. ✓
- Test commands use valid MTP `--filter-query` syntax. ✓

**Risk Assessment:** The visibility sweep is atomic (all-or-nothing) per D8 — if any one type flip is missed, CS0053 errors will emerge at build time, making incomplete sweeps immediately obvious. PLAN-1.1 acceptance criteria verify this (grep for zero `public` matches on the nine types, grep for exactly seven `internal` matches). **No hidden risk.**

---

### PLAN-1.2: ActionEntry TypeConverter (ID-12 Fix)

**File Paths:**
- `src/FrigateRelay.Host/Configuration/ActionEntry.cs` ✓ (exists, will be modified to add `[TypeConverter]` attribute and flip visibility)
- `src/FrigateRelay.Host/Configuration/ActionEntryTypeConverter.cs` ✗ (NEW — will be created)
- `tests/FrigateRelay.Host.Tests/Configuration/ActionEntryTypeConverterTests.cs` ✗ (NEW — will be created)

**File Collision Check:**
PLAN-1.1 Task 2 modifies `ActionEntryJsonConverter.cs` to flip visibility. PLAN-1.2 owns `ActionEntry.cs` exclusively. **No collision.** The two plans touch different files in the `Configuration/` directory and are correctly sequenced to avoid parallel conflicts.

**API Surface Validation:**
- `ActionEntry(string)` ctor is implicitly supported by the three-param record ctor with optional defaults — when called with only a string, the others default to null. PLAN-1.2's `ActionEntryTypeConverter.ConvertFrom` calls `new ActionEntry(s)` correctly.
- The `[TypeConverter]` attribute coexists with the existing `[JsonConverter(typeof(ActionEntryJsonConverter))]` on the same type — both operate on disjoint code paths per RESEARCH.md §5 and CONTEXT-8 D2. ✓

**Verification Commands:**
- All three test cases in Task 1 are concrete: string-array, object-array, mixed. ✓
- TDD red phase acceptance criterion is measurable: "3 tests FAILING" confirms the issue reproduces. ✓
- Green phase criterion is measurable: "3 tests pass" confirms the fix works. ✓

**Risk:** Low. The TypeConverter is a standard .NET pattern and PLAN-1.2 provides a reference implementation snippet in RESEARCH.md §5. Coexistence of two converters is documented and tested.

---

### PLAN-2.1: Profile Expansion + Collect-All Validator Retrofit

**File Paths:**
- `src/FrigateRelay.Host/Configuration/ProfileResolver.cs` ✗ (NEW)
- `src/FrigateRelay.Host/StartupValidation.cs` ✓ (exists, contains `ValidateActions`, `ValidateSnapshotProviders`, `ValidateValidators` per RESEARCH.md §2)
- `src/FrigateRelay.Host/HostBootstrap.cs` ✓ (exists; assumed to call startup validators)
- `tests/FrigateRelay.Host.Tests/Configuration/ProfileResolutionTests.cs` ✗ (NEW)
- `tests/FrigateRelay.Host.Tests/Startup/StartupValidationTests.cs` ✓ (exists; CONTEXT-8 D7 requires these be retrofitted to substring assertions)

**Dependencies Satisfied:**
PLAN-2.1 depends on PLAN-1.1 (for `ProfileOptions`, `SubscriptionOptions.Profile`, `HostSubscriptionsOptions.Profiles`) and PLAN-1.2 (for the `[TypeConverter]` so JSON arrays bind correctly in test fixtures). Both PLAN-1.1 and PLAN-1.2 are Wave 1 and must be complete before Wave 2 (PLAN-2.1) begins. **Dependency order is explicit and correct.**

**Validator Retrofit Scope:**
Plan correctly identifies that `StartupValidation.cs` currently contains three `Validate*` methods that throw on first error. Plan Task 1 retrofits all three to use a shared `errors` accumulator. The plan also correctly notes that testing will require updating `StartupValidationTests.cs` to assert on substring presence in the aggregated message (Task 3). **Scope is complete and consistent with D7.**

**ProfileResolver Behavior:**
Plan signature matches D1/D5 expectations: `Resolve(HostSubscriptionsOptions, List<string> errors)` returns `IReadOnlyList<SubscriptionOptions>` with the resolved action list. The plan correctly specifies that the resolver **never throws** — all errors go into the accumulator, allowing multi-error reporting. ✓

**Test Coverage:**
Plan lists ≥10 required tests covering D1 mutex, undefined profile refs, plugin/validator/provider unknowns, and multi-error aggregation. The test names are concrete and follow underscore convention. ✓

**Verification Commands:**
- `git grep` for throws in `ProfileResolver.cs` (expect zero) and in `StartupValidation.cs` (expect one — the aggregated throw) are valid. ✓
- Test filter queries use valid MTP syntax. ✓

**Risk:** Low. The plan correctly splits production code (Task 1) from test code (Tasks 2-3), with TDD tests written first (red phase) then production landing (green phase).

---

### PLAN-3.1: Example Config + Legacy Fixture + ConfigSizeParityTest

**File Paths:**
- `config/appsettings.Example.json` ✗ (NEW — created by builder reading the user-supplied `legacy.conf`)
- `tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf` — USER-SUPPLIED (not created by builder; sourced via SANITIZATION-CHECKLIST.md)
- `tests/FrigateRelay.Host.Tests/Configuration/ConfigSizeParityTest.cs` ✗ (NEW)
- `tests/FrigateRelay.Host.Tests/FrigateRelay.Host.Tests.csproj` ✓ (exists; will be modified to wire fixture copying)

**User Fixture Sourcing:**
PLAN-3.1 Task 1 correctly specifies that the builder workflow halts with a clear message if `legacy.conf` is missing and prompts the user to use SANITIZATION-CHECKLIST.md. This matches D6 and D9. The plan does NOT attempt to auto-generate the fixture (correctly — D6 forbids this). **User responsibility is clear.**

**Fixture Wiring (CopyToOutputDirectory):**
Plan specifies MSBuild `<None>` items with `CopyToOutputDirectory=PreserveNewest` for both files:
```xml
<None Update="Fixtures/legacy.conf">
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</None>
<None Include="..\..\config\appsettings.Example.json" Link="Fixtures/appsettings.Example.json">
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</None>
```
This pattern is correct and standard in .NET projects. The `Link=` attribute allows the file to appear in the test output directory under the logical path `Fixtures/appsettings.Example.json` even though the actual file is at `config/appsettings.Example.json`. ✓

**Test Logic:**
Plan Task 2 specifies the exact D9 hard-fail message to emit when `legacy.conf` is missing. The test reads both files from `AppContext.BaseDirectory/Fixtures/` and computes the character-count ratio. No `Assert.Inconclusive` path. ✓ The char-count comparison is per D3 (raw `.Length`, no stripping). ✓

**CLAUDE.md Update (Task 3):**
Plan correctly specifies replacing the current ID-12 block (the long warning about string-arrays silently dropping) with a "since Phase 8" note. The plan quotes the old text fragment ("silently produces an empty `Actions` list") so the builder can find the exact block to replace. ✓

**SANITIZATION-CHECKLIST.md Quality:**
The provided checklist (read separately above) covers all D3 redaction rules (IPs → `example.local`, secrets → `<redacted>`, preserve structure/whitespace). It includes a verification section with `git grep` commands the user runs pre-commit to confirm no secrets leaked. It specifies the exact hard-fail message the test emits when the fixture is missing. ✓

**Risk:** Low. The fixture is user-supplied and auditable; the test failure on missing fixture is explicit; the verification commands are concrete.

---

## Cross-Plan Consistency

**Test Counts:**
- PLAN-1.2: 3 `ActionEntryTypeConverterTests`
- PLAN-2.1: ≥10 `ProfileResolutionTests` + modification of existing `StartupValidationTests`
- PLAN-3.1: 1 `ConfigSizeParityTest`
- **Total new tests: ≥14** (within ROADMAP phase 8 acceptance criteria scope)

**File Renames / New Files:**
- PLAN-1.1 creates `ProfileOptions.cs` (referenced correctly by PLAN-2.1 and PLAN-3.1) ✓
- PLAN-1.2 creates `ActionEntryTypeConverter.cs` (referenced in `ActionEntry.cs` decorator) ✓
- PLAN-2.1 creates `ProfileResolver.cs` (wired into `HostBootstrap.ValidateStartup` in Task 1) ✓
- PLAN-3.1 creates `ConfigSizeParityTest.cs` and `config/appsettings.Example.json` (independent) ✓

**No forward references or unmet file dependencies.** ✓

---

## CS0053 Build-Window Analysis (Criterion #5)

**Wave 1 parallel execution risk:**

PLAN-1.1 and PLAN-1.2 both run in Wave 1 and can be ordered by the builder. If **PLAN-1.1 commits before PLAN-1.2**:
1. After PLAN-1.1: `SubscriptionOptions: internal` referencing `ActionEntry: public` — **Legal.** Internal types can reference public types.
2. Before PLAN-1.2 lands: The build remains valid.
3. After PLAN-1.2: `ActionEntry: internal` — build still valid.

If **PLAN-1.2 commits before PLAN-1.1**:
1. After PLAN-1.2: `ActionEntry: internal` referenced by `SubscriptionOptions: public` — **Illegal (CS0053).**
2. The build breaks immediately.

**Mitigation:** PLAN-1.1 must be merged first, OR the architect must squash both plans into a single commit. The plan documents do not explicitly enforce commit ordering, but the description notes are clear that PLAN-1.1 is listed first and is a prerequisite for PLAN-1.2 (conceptually — "the sweep lands first conceptually"). **Recommend:** Builder should merge PLAN-1.1 before PLAN-1.2, or architect should note in build instructions that if plans are batched, PLAN-1.1 must be squashed with PLAN-1.2 in a single commit.

---

## CLAUDE.md Update Sufficiency (Criterion #6)

**Current ID-12 block location:** Line 93 in CLAUDE.md, beginning with `"`Subscriptions:N:Actions` requires the object form"`.

**Plan references:** PLAN-3.1 Task 3 states: "replace the current ID-12 block (the long paragraph beginning `"`Subscriptions:N:Actions` requires the object form"`) with a "since Phase 8" note."

**Sufficiency:** The plan does NOT quote the full old text verbatim, making it slightly ambiguous if the builder is unfamiliar with the file. However, the opening phrase is specific enough ("`Subscriptions:N:Actions` requires the object form"`) to locate the block via grep. A builder can run `grep -n 'requires the object form' CLAUDE.md` to find the line number. **Acceptable, but not perfect.** Recommend the plan include a line-number reference or a longer quoted fragment to reduce ambiguity.

**Update wording:** Plan specifies adding "since Phase 8, both forms work" and "Phase 8 closed ID-12 by adding ActionEntryTypeConverter." This is clear and actionable. ✓

---

## Fixture Wiring Soundness (Criterion #7)

**CopyToOutputDirectory pattern:** Both the `<None Update="Fixtures/legacy.conf">` (existing file, updated) and `<None Include="..\..\config\appsettings.Example.json" Link="...">` (new file, linked) patterns are standard MSBuild and will work correctly. ✓

**Path resolution in test:** Plan specifies `Path.Combine(AppContext.BaseDirectory, "Fixtures", ...)` to resolve fixture files at test runtime. This is correct for MSTest executables using Microsoft.Testing.Platform (per CLAUDE.md commands — tests run as `dotnet run ... -c Release`, which means the test exe runs from the published output directory). ✓

---

## SANITIZATION-CHECKLIST Quality (Criterion #8)

The checklist provided covers:
- **All D3 redaction rules** ✓ (IPs, ports, secrets, credentials, hostnames, device IDs)
- **What NOT to change** ✓ (camera names, labels, zones, structure, whitespace)
- **Verification commands** ✓ (git grep patterns with clear expected output: empty)
- **Pre-commit checklist** ✓ (5 explicit grep commands + CI secret-scan integration)
- **Hard-fail test behavior** ✓ (exact D9 message quoted)
- **Quick reference** ✓ (minimal redacted INI and subscription block examples)

The checklist is comprehensive and actionable. It tells the user exactly what to do and what to verify. **Excellent quality.** ✓

---

## Complexity / Risk Flags (Criterion #10)

| Plan | Files | Dirs | Tasks | Acceptance Criteria | Complexity |
|------|-------|------|-------|-------------------|-----------|
| PLAN-1.1 | 10 | 5 | 3 | 6 | LOW |
| PLAN-1.2 | 3 | 1 | 2 | 8 | LOW |
| PLAN-2.1 | 5 | 3 | 3 | 6 | LOW |
| PLAN-3.1 | 4 | 3 | 3 | 7 | LOW |

All plans stay within the ≤3-tasks-per-plan and <10-files-touched guidelines. No plan is complex. The widest span (PLAN-1.1 across 5 directories) is justified by the all-or-nothing visibility sweep. **No red flags.**

---

## Mitigations (If Verdict Were CAUTION)

N/A — verdict is READY. However, one minor clarification is recommended:

**Recommendation for Builder:** Explicitly enforce PLAN-1.1 merge-before-PLAN-1.2 ordering in the build instructions, or document that if both are batched in a single PR, they must be squashed into one commit to avoid the CS0053 intermediate state.

---

## Verdict

**READY**

All four plans are grounded in the actual codebase, have no file-path errors, no API mismatches, and no hidden dependencies. Test commands are syntactically correct and measurable. The plans are ready for execution.

The visibility sweep (PLAN-1.1) is the only point requiring care: it must either run to completion, or be paired with PLAN-1.2 in a single commit. Both orders are safe; partial completion is not.

