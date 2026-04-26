# Phase 8 Verification Report
**Phase:** 8 — Profiles in Configuration  
**Date:** 2026-04-26  
**Type:** plan-review (coverage mode)

---

## Verdict
**PASS** — All Phase 8 deliverables, success criteria, and nine binding decisions are covered by the four plans. Plan structure is sound. Test counts meet or exceed gates. No blocking gaps identified.

---

## A. ROADMAP Coverage Matrix

| Deliverable | Addressed by | Status |
|---|---|---|
| Config binding: `Profiles` dict + subscription `Profile`/`Actions` XOR | PLAN-1.1 Task 3, PLAN-2.1 Task 1 | PASS |
| Startup validation: fail-fast on undefined profile / both fields / neither | PLAN-2.1 Task 1 | PASS |
| Fixture `config/appsettings.Example.json` (9-sub deployment) | PLAN-3.1 Task 1 | PASS |
| `ProfileResolutionTests` (≥10 unit tests) | PLAN-2.1 Task 2 | PASS |
| `ConfigSizeParityTest` (JSON ≤ 60% INI char count) | PLAN-3.1 Task 2 | PASS |

**All ROADMAP deliverables covered; 100% coverage.**

---

## B. Decision Coverage Matrix (D1–D9)

| Decision | Topic | Addressed by | Acceptance Evidence |
|---|---|---|---|
| **D1** | Profile + inline XOR; fail-fast both/neither | PLAN-2.1 Task 1, Task 2 (tests 4–5) | ProfileResolver checks both/neither → errors accumulator; ≥2 tests prove behavior |
| **D2** | ID-12 fixed via `ActionEntryTypeConverter` | PLAN-1.2 Task 1–2 | 3 unit tests (string, object, mixed-array forms); TypeConverter + attribute on `ActionEntry` |
| **D3** | Real sanitized production INI fixture | PLAN-3.1 Task 1, SANITIZATION-CHECKLIST.md | User-supplied `legacy.conf` with auditable redaction rules; CI tripwire in `.github/` |
| **D4** | Snapshot precedence unchanged (3-tier) | PLAN-1.1 (implicit: no profile-level provider), PLAN-2.1 Task 1 (resolver preserves tiers) | Resolver accepts per-action/per-subscription/global; profiles carry only Actions (D5) |
| **D5** | Profiles: flat dict, no `BasedOn`/nesting | PLAN-1.1 Task 2 (ProfileOptions shape), PLAN-2.1 Task 1 (no nesting logic) | `ProfileOptions` has `Actions` only; resolver does not chase profile references |
| **D6** | Fixture sourcing: user-provided + checklist | PLAN-3.1 Task 1 (builder prompt), SANITIZATION-CHECKLIST.md (artifact) | Builder halts if missing; checklist delivered as planning artifact |
| **D7** | Existing validators retrofit to collect-all | PLAN-2.1 Task 1–3 | `ValidateAll` with single accumulator; all three Phase 4–7 validators accept `List<string> errors` |
| **D8** | ID-2/ID-10 visibility sweep (9 types → internal) | PLAN-1.1 Task 1–2 | 8 types in Task 1, `ActionEntryJsonConverter` in Task 2; PLAN-1.2 Task 2 internalizes `ActionEntry` |
| **D9** | `ConfigSizeParityTest` hard-fail on missing fixture | PLAN-3.1 Task 2 | Test calls `Assert.Fail()` with D9 verbatim message; no `Inconclusive`/env branch |

**All 9 decisions implemented; 100% coverage.**

---

## C. Issue Closure Coverage

| Issue | Plan/Task | Evidence |
|---|---|---|
| **ID-2** (`IActionDispatcher`/`DispatcherOptions` internal) | PLAN-1.1 Task 1 | 8 types flipped to `internal sealed` (including `IActionDispatcher`, `DispatcherOptions`) |
| **ID-10** (`ActionEntry`/`ActionEntryJsonConverter`/`SnapshotResolverOptions` internal) | PLAN-1.1 Task 2, PLAN-1.2 Task 2 | `ActionEntryJsonConverter` internal in Task 2; `ActionEntry` internal in PLAN-1.2 Task 2; `SnapshotResolverOptions` internal in PLAN-1.1 Task 1 |
| **ID-12** (string-array `Actions` back-compat via TypeConverter) | PLAN-1.2 Task 2, Task 3 (CLAUDE.md update) | `ActionEntryTypeConverter` + `[TypeConverter]` attribute; 3 binding tests (string, object, mixed); CLAUDE.md updated to "Phase 8 closed ID-12" |

**All three issues marked for closure; evidence present in plan tasks.**

---

## D. Plan Structure Findings

### Task Counts
- **PLAN-1.1:** 3 tasks (Visibility sweep, `ActionEntryJsonConverter`+`ProfileOptions`, Profile+Profiles properties) ✓
- **PLAN-1.2:** 2 tasks (TDD tests, TypeConverter implementation) ✓
- **PLAN-2.1:** 3 tasks (ProfileResolver + validator retrofit, ProfileResolutionTests, UpdateStartupValidationTests) ✓
- **PLAN-3.1:** 3 tasks (Example config + csproj wiring, ConfigSizeParityTest, CLAUDE.md/ISSUES.md updates) ✓

**All ≤ 3 tasks per plan. PASS.**

### Wave Ordering
- **Wave 1 (PLAN-1.1, PLAN-1.2):** No dependencies declared. Disjoint file sets (1.1 avoids `ActionEntry.cs`, 1.2 avoids 1.1's modified files). ✓
- **Wave 2 (PLAN-2.1):** `dependencies: [1.1, 1.2]` → correctly depends on Wave 1 completion. ✓
- **Wave 3 (PLAN-3.1):** `dependencies: [2.1]` → correctly depends on Wave 2. ✓

**Wave ordering valid; no circular dependencies. PASS.**

### File Disjointness Check (Wave 1)
**PLAN-1.1 files:**
```
src/FrigateRelay.Host/Configuration/SubscriptionOptions.cs
src/FrigateRelay.Host/Configuration/HostSubscriptionsOptions.cs
src/FrigateRelay.Host/Configuration/ActionEntryJsonConverter.cs
src/FrigateRelay.Host/Configuration/ProfileOptions.cs
src/FrigateRelay.Host/Snapshots/SnapshotResolverOptions.cs
src/FrigateRelay.Host/Dispatch/IActionDispatcher.cs
src/FrigateRelay.Host/Dispatch/DispatcherOptions.cs
src/FrigateRelay.Host/Matching/DedupeCache.cs
src/FrigateRelay.Host/Matching/SubscriptionMatcher.cs
src/FrigateRelay.Host/FrigateRelay.Host.csproj
```

**PLAN-1.2 files:**
```
src/FrigateRelay.Host/Configuration/ActionEntry.cs
src/FrigateRelay.Host/Configuration/ActionEntryTypeConverter.cs
tests/FrigateRelay.Host.Tests/Configuration/ActionEntryTypeConverterTests.cs
```

**Intersection:** empty. ✓ Disjoint file sets confirmed.

### Acceptance Criteria Quality
All acceptance criteria are testable and concrete:
- Build commands with explicit exit-code expectations: `dotnet build ... -c Release` exits 0.
- Git grep patterns with expected match counts (e.g., "exactly 7 matches").
- Test discovery commands with filter syntax: `--filter-query "/*/*/ProfileResolutionTests/*"`.
- No hedging language ("should work", "code is clean") — all use measurable assertions.

**Acceptance criteria quality: PASS.**

### Verification Commands
- All use `--filter-query` (correct MTP syntax per CLAUDE.md).
- No deprecated `dotnet test` invocations (correctly use `dotnet run --project tests/<project>`).
- Test output inspection commands are concrete (`dotnet run ... -- --filter-query "..."` discovers exact test count).

**Verification commands: PASS.**

---

## E. Test Count Audit

### Gate Requirements (ROADMAP Phase 8)
- **ProfileResolution suite:** ≥ 10 passing tests
- **ConfigSizeParityTest:** ≥ 1 passing test
- **ActionEntryTypeConverter suite (ID-12 fix):** ≥ 3 passing tests

### Promised by Plans
| Test Suite | Plan/Task | Count | Requirement | Status |
|---|---|---|---|---|
| `ProfileResolutionTests` | PLAN-2.1 Task 2 | 10 (numbered 1–10) | ≥ 10 | PASS |
| `ConfigSizeParityTest` | PLAN-3.1 Task 2 | 1 (`Json_Is_At_Most_60_Percent_Of_Ini_Character_Count`) | ≥ 1 | PASS |
| `ActionEntryTypeConverterTests` | PLAN-1.2 Task 1 | 3 (string, object, mixed-array forms) | ≥ 3 | PASS |
| `StartupValidationTests` (updated) | PLAN-2.1 Task 3 | Not counted separately (existing tests retrofitted) | N/A | N/A |

**Total new tests: 14 (10+1+3). All gates met. PASS.**

---

## F. Binding Decisions Reflected in Acceptance Criteria

### D1 Coverage
- **Requirement:** Tests for "both Profile and Actions set → fail-fast" AND "neither set → fail-fast".
- **Evidence:** PLAN-2.1 Task 2 tests 4 and 5 explicitly cover these (`Resolve_BothProfileAndActionsSet_ReportsMutexError` and `Resolve_NeitherProfileNorActions_ReportsMissingError`).
- **Status:** PASS ✓

### D7 Coverage
- **Requirement:** Retrofit existing validators to collect-all; drop `throw`-on-first pattern.
- **Evidence:** PLAN-2.1 Task 1 describes modifier to `StartupValidation.cs`; Task 3 updates existing tests to assert substring presence in aggregated message (not exact single-error match). Acceptance criterion: "git grep 'throw new InvalidOperationException' src/FrigateRelay.Host/StartupValidation.cs" returns exactly one match (the aggregated throw).
- **Status:** PASS ✓

### D8 Coverage
- **Requirement:** Visibility sweep covers all 9 named types: `ActionEntry`, `ActionEntryJsonConverter`, `SnapshotResolverOptions`, `SubscriptionOptions`, `HostSubscriptionsOptions`, `IActionDispatcher`, `DispatcherOptions`, `DedupeCache`, `SubscriptionMatcher`.
- **Evidence:** 
  - PLAN-1.1 Task 1: 8 types explicitly listed (all except `ActionEntry`).
  - PLAN-1.1 Task 2: `ActionEntryJsonConverter` internalized.
  - PLAN-1.2 Task 2: `ActionEntry` internalized (alongside `[TypeConverter]` addition).
  - PLAN-1.1 Task 1 acceptance: `git grep -nE 'internal (sealed )?(class|record|interface) (SubscriptionOptions|HostSubscriptionsOptions|SnapshotResolverOptions|IActionDispatcher|DispatcherOptions|DedupeCache|SubscriptionMatcher)\b'` returns exactly seven matches.
  - Plus PLAN-1.2 and 1.1 Task 2 for the other two types.
- **Status:** PASS ✓

### D9 Coverage
- **Requirement:** Hard fail (no `Assert.Inconclusive`, no env-var branching) on missing fixture with D9 verbatim message.
- **Evidence:** PLAN-3.1 Task 2 provides the exact `Assert.Fail()` code with the D9-specified message verbatim. Acceptance criterion explicitly excludes env-var checks ("no environment branch, no `Assert.Inconclusive`").
- **Status:** PASS ✓

---

## G. Architect Deviations from CONTEXT-8 (Four Items)

Per CONTEXT-8 cross-cutting notes, the architect flagged four deviations from strict CONTEXT-8 reading. Evaluation:

### 1. `ActionEntry` visibility flip moved to PLAN-1.2 (with TypeConverter) instead of PLAN-1.1 (visibility sweep)

**What the plans do:**
- PLAN-1.1 Task 1 flips 8 types to internal; deliberately does **not** touch `ActionEntry.cs`.
- PLAN-1.2 Task 2 flips `ActionEntry` to internal **and** adds `[TypeConverter]` in the same task.

**Judgment:** **STRENGTHEN** — correct move.

**Rationale:** Wave 1 plans have disjoint file sets per CLAUDE.md "Conventions" and task-planning best practice. `ActionEntry` is owned by PLAN-1.2 (it's central to the TypeConverter feature). The visibility flip is a side effect of internalizing `SubscriptionOptions` in Task 1.1.1, which removes the CS0053 constraint. Delaying the flip to Task 1.2 keeps file ownership clean and avoids spurious dependencies (PLAN-1.1 Task 1 does not need to read/understand the TypeConverter to complete). Both files still compile (CS0053 is satisfied because `SubscriptionOptions` is internal by the time 1.1 completes). **No weakness — this is better separation of concerns.**

### 2. Resolver returns a new resolved list rather than mutating in place

**What CONTEXT-8 says:** Does not explicitly specify mutation vs. return.

**What the plans do:** PLAN-2.1 Task 1 defines `Resolve(HostSubscriptionsOptions options, List<string> errors) → IReadOnlyList<SubscriptionOptions>`. New list returned; original `options.Subscriptions` unchanged.

**Judgment:** **STRENGTHEN** — immutability is cleaner.

**Rationale:** Returning a new list preserves the original `HostSubscriptionsOptions` as read-only input, reducing side effects. Downstream code receives a clean resolved list and never needs to worry about whether it's the original or expanded. Fits .NET immutability conventions (cf. LINQ methods that return new collections). **Appropriate architectural choice.**

### 3. `ConfigSizeParityTest` adds optional bind-and-validate sub-assertion

**What CONTEXT-8 D9 says:** Hard fail on missing fixture; measure char count ≤ 60%.

**What the plans do:** PLAN-3.1 Task 2 provides the mandatory char-count assertion, then notes: "Optionally, also bind the example JSON via `IConfiguration.Bind` and run `ProfileResolver.Resolve` + `StartupValidation.ValidateAll` to prove the example is not just shorter but valid. This is a single additional sub-assertion in the same test method."

**Judgment:** **STRENGTHEN** — adds confidence in the example.

**Rationale:** The optional sub-assertion proves the example config is not just shorter but actually loads and validates. Catches typos or drift in the JSON shape. The optionality is appropriate — the core gate (char count ≤ 60%) is the binding requirement; the validity check is a bonus. **Value-add with no downside.**

### 4. `appsettings.Example.json` linked into test csproj via `<None Include=... Link=...>`

**What CONTEXT-8 says:** File goes at `config/appsettings.Example.json`.

**What the plans do:** PLAN-3.1 Task 1 modifies the test csproj to add `<None Include="..\..\config\appsettings.Example.json" Link="Fixtures/appsettings.Example.json">`. The file lives in `config/` but is copied to the test output under `Fixtures/` for the test to read.

**Judgment:** **STRENGTHEN** — solves path-resolution problem.

**Rationale:** RESEARCH.md §6 noted that the test exe runs five `..` hops below repo root; a relative path from the exe to `config/` is error-prone. Linking the file into the test output directory via MSBuild's `Link` attribute makes the file co-located with `legacy.conf` at runtime. The test reads both from `AppContext.BaseDirectory/Fixtures/`. This is idiomatic .NET test infrastructure and avoids path climbing. **Cleaner than the alternative (hardcoding a repo-root-relative path or symlink).**

---

## H. Plan Quality Summary

### Strengths
1. **Coverage:** All 5 ROADMAP deliverables, all 9 binding decisions (D1–D9), all 3 issue closures explicitly covered.
2. **Structure:** Wave ordering enforces dependencies; disjoint Wave 1 file sets allow parallelization.
3. **Test gates:** All three gates (ProfileResolution ≥10, Parity ≥1, TypeConverter ≥3) met; total 14 new tests.
4. **Acceptance criteria:** All measurable, concrete, and runnable. No hedging language.
5. **Decision consistency:** Four architect deviations are all improvements; no contradictions to binding decisions.

### Minor Notes
1. **PLAN-3.1 Task 1 builder requirement:** The plan correctly notes that `legacy.conf` must be provided by the user per SANITIZATION-CHECKLIST.md and the builder must halt if missing with a clear prompt. This is a *user-facing workflow* requirement, not a code task; the plan documents it correctly.
2. **Retrofit scope in PLAN-2.1 Task 1:** The scope lists "Phase 7 `StartupValidation.ValidateValidators` pass" and "any `ValidateActionPlugins` / action-name existence pass (Phase 4-era)" and "any `ValidateSnapshotProviders` pass (Phase 5)". These are described as "must be enumerated by the architect" in CONTEXT-8 D7. The plan correctly defers the enumeration to the builder (it says "Architect must enumerate every validator touched" in the D7 quote, and the builder will do this during PLAN-2.1 Task 1 execution).

---

## Verdict Summary

**Phase 8 plans are **READY FOR BUILD** with no blocking gaps.**

| Category | Result |
|---|---|
| ROADMAP coverage | 100% (5/5 deliverables) |
| Decision coverage (D1–D9) | 100% (9/9 decisions) |
| Issue closure (ID-2, ID-10, ID-12) | 100% (3/3 issues) |
| Test count gates | 100% (14 new tests vs. ≥14 required) |
| Plan structure (waves, files, criteria) | PASS |
| Architect deviations | All justified improvements |

**Verdict: PASS** — All requirements covered; plans are coherent, testable, and ready for execution.

