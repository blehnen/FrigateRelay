# Phase 15 Plan Critique

**Date:** 2026-05-07
**Plans reviewed:** PLAN-1.1, PLAN-1.2, PLAN-1.3, PLAN-1.4
**Wave:** 1 (all parallel, no declared cross-dependencies)
**Reviewer type:** Plan pre-execution critique (feasibility stress test)

---

## Verdict: CAUTION

All four plans are fundamentally sound and implement against real, confirmed file paths and API surfaces. No plan is REVISE-blocking. Three CAUTION items require builder awareness before execution begins.

---

## Per-Plan Findings

---

### PLAN-1.1: StartupValidation hardening (#13, #19, #20, #27)

#### Check 1 — File paths exist

| Path | Action | Exists? |
|------|--------|---------|
| `src/FrigateRelay.Host/StartupValidation.cs` | modify | YES |
| `src/FrigateRelay.Host/Configuration/ProfileResolver.cs` | modify | YES |
| `tests/FrigateRelay.Host.Tests/Configuration/StartupValidationNameSanitizationTests.cs` | create | NO — correct, create-action |
| `tests/FrigateRelay.Host.Tests/Configuration/StartupValidationNameAllowlistTests.cs` | create | NO — correct, create-action |
| `tests/FrigateRelay.Host.Tests/Observability/ValidateObservabilityTests.cs` | modify | YES |
| `tests/FrigateRelay.Host.Tests/Configuration/SerilogPathValidationTests.cs` | modify | YES |
| `CHANGELOG.md` | modify | YES |

**Result: PASS** — all paths correct; the two new test files are legitimately absent pre-build.

#### Check 2 — API surface matches

| Symbol | Plan claim | Actual |
|--------|-----------|--------|
| `StartupValidation.Sanitize` | new, expected absent | Absent — confirmed by grep (PASS; create) |
| `StartupValidation.ValidateNames` | new, expected absent | Absent — confirmed by grep (PASS; create) |
| `StartupValidation.ValidateObservability` | existing at lines 71–82 | Present at line 71 — PASS |
| `StartupValidation.ValidateSerilogPath` | existing at lines 99–115 | Present at line 99 — PASS |
| `StartupValidation.ValidateAll` | calls passes in sequence | Present at line 29; calls ValidateObservability (39), ValidateSerilogPath (40), ProfileResolver.Resolve (44), ValidateActions (48), ValidateSnapshotProviders (53), ValidateValidators (56) — PASS |
| `ProfileResolver.Resolve` + `errors.Add` sites | lines 41, 49, 61–64 | File exists; plan's description of sites is consistent with RESEARCH.md — PASS |
| XML doc comment "future hardening pass" | lines 92–96 of ValidateSerilogPath | Lines 92–95 contain exactly the "future hardening pass" language the plan targets — PASS |
| `StartupValidation` class is `internal static` (not `partial`) | noted in plan for Regex choice | Confirmed: line 12 `internal static class StartupValidation` — no `partial` — plan correctly chooses `private static readonly Regex` over `[GeneratedRegex]` — PASS |

**One nuance (WARN, non-blocking):** The plan's Task 1 acceptance criterion states `git grep -nE 'errors\.Add\(\$"[^"]*\{(sub\.Name|profileName|entry\.Plugin|entry\.SnapshotProvider|key|endpoint|seq|path|globalDefaultProviderName)'` should return zero unsanitized matches. The grep pattern uses a negative lookahead concept but is actually a positive grep expecting no output. This is the correct idiom: the command matches unsanitized sites; zero output = PASS. The pattern is syntactically valid ERE for git grep.

**`ValidateSerilogPath` signature change:** Plan Task 3 adds `Func<bool>? isWindows = null` as a third parameter. `ValidateAll` at line 40 currently calls `ValidateSerilogPath(configuration, errors)` — the plan correctly notes the new parameter defaults to `null` making the call site compile unchanged. PASS.

**Result: PASS** with one minor note on the grep acceptance criterion (see above — self-consistent, no issue).

#### Check 3 — Verify commands runnable

| Command | Status |
|---------|--------|
| `dotnet build FrigateRelay.sln -c Release` | FrigateRelay.sln exists — PASS |
| `dotnet run --project tests/FrigateRelay.Host.Tests -c Release` | project path exists — PASS |
| `dotnet run ... -- --filter "StartupValidationNameSanitizationTests"` | MTP filter syntax correct per CLAUDE.md — PASS |
| `dotnet run ... -- --filter "StartupValidationNameAllowlistTests"` | PASS |
| `dotnet run ... -- --filter "ValidateObservabilityTests"` | PASS |
| `dotnet run ... -- --filter "SerilogPathValidationTests"` | PASS |
| `git grep ServicePointManager src/` | Valid git grep — PASS |
| `git grep -nE '\.(Result\|Wait)\(' src/` | Valid ERE — PASS |
| `git grep -nE 'App\.Metrics\|OpenTracing\|Jaeger\.' src/` | Valid ERE — PASS |
| `git grep -nE 'errors\.Add\(\$"[^"]*\{(sub\.Name\|profileName\|...)' src/FrigateRelay.Host/...` | Valid ERE — PASS |

**Result: PASS**

#### Check 4 — Forward references (Wave 1 parallel)

PLAN-1.1 introduces `StartupValidation.Sanitize` (internal static). PLAN-1.1 also modifies `ValidateObservability` in Task 2 with `Sanitize(endpoint)` wrapping — this is within PLAN-1.1 itself. No other plan in Wave 1 depends on `Sanitize`.

**WARN:** PLAN-1.1 Task 2 (ValidateObservability scheme allowlist, issue #20) is described separately from Task 1 (Sanitize helper), but Task 2's acceptance criterion requires that the `endpoint` value is wrapped in `Sanitize(...)`. This is an intra-plan dependency (Task 1 must be implemented before Task 2 compiles correctly). Since both tasks are within PLAN-1.1, this is not a cross-plan wave-split issue — the builder must complete Task 1 before Task 2 within the same file edit. Non-blocking; document for builder.

**Result: PASS** (intra-plan only, not cross-plan)

#### Check 5 — Hidden file dependencies (see consolidated map below)

PLAN-1.1 owns: `StartupValidation.cs`, `ProfileResolver.cs`, two new test files, `SerilogPathValidationTests.cs` (modify), `ValidateObservabilityTests.cs` (modify), `CHANGELOG.md`.

`CHANGELOG.md` is also modified by PLAN-1.2, PLAN-1.3, and PLAN-1.4. See consolidated map.

**Result: WARN** — CHANGELOG.md file conflict (all 4 plans). See File Map section.

#### Check 6 — Complexity

Files touched: 7. Directories: `src/FrigateRelay.Host/`, `src/FrigateRelay.Host/Configuration/`, `tests/FrigateRelay.Host.Tests/Configuration/`, `tests/FrigateRelay.Host.Tests/Observability/`, root. **Under 10 files, under 4 directories.** Not HIGH RISK.

**Result: PASS**

---

### PLAN-1.2: ActionEntryTypeConverter empty/whitespace guard (#14)

#### Check 1 — File paths exist

| Path | Action | Exists? |
|------|--------|---------|
| `src/FrigateRelay.Host/Configuration/ActionEntryTypeConverter.cs` | modify | YES |
| `tests/FrigateRelay.Host.Tests/Configuration/ActionEntryTypeConverterTests.cs` | modify | YES |
| `CHANGELOG.md` | modify | YES |

**Result: PASS**

#### Check 2 — API surface matches

| Symbol | Plan claim | Actual |
|--------|-----------|--------|
| `ActionEntryTypeConverter.ConvertFrom` | lines 32–40, current 4-line shape with no guard | Confirmed: line 32 `ConvertFrom`, no `IsNullOrWhiteSpace` or `IsNullOrEmpty` present — PASS |
| Current test count: 6 | RESEARCH.md §1 | `grep -c "\[TestMethod\]"` on ActionEntryTypeConverterTests.cs = **6** — PASS |

**Result: PASS**

#### Check 3 — Verify commands runnable

| Command | Status |
|---------|--------|
| `dotnet build FrigateRelay.sln -c Release` | PASS |
| `dotnet run --project tests/FrigateRelay.Host.Tests -c Release -- --filter "ActionEntryTypeConverterTests"` | PASS |
| `grep -n '#14' CHANGELOG.md` | Valid grep — PASS |

**Result: PASS**

#### Check 4 — Forward references

No cross-plan dependencies. PLAN-1.2 solely modifies `ActionEntryTypeConverter.cs` and its test file.

**Result: PASS**

#### Check 5 — Hidden file dependencies

PLAN-1.2 owns: `ActionEntryTypeConverter.cs`, `ActionEntryTypeConverterTests.cs`, `CHANGELOG.md`. `CHANGELOG.md` conflict with PLAN-1.1, PLAN-1.3, PLAN-1.4. See File Map.

**Result: WARN** — CHANGELOG.md conflict only.

#### Check 6 — Complexity

3 files, 2 directories. **Well under thresholds.** Not HIGH RISK.

**Result: PASS**

---

### PLAN-1.3: CI / supply-chain hygiene (#8, #15, #24)

#### Check 1 — File paths exist

| Path | Action | Exists? |
|------|--------|---------|
| `.github/scripts/run-tests.sh` | modify | YES |
| `.github/scripts/secret-scan.sh` | modify | YES |
| `.github/secret-scan-fixture.txt` | modify | YES |
| `.github/workflows/release.yml` | modify | YES |
| `.github/workflows/ci.yml` | modify | YES |
| `.github/workflows/secret-scan.yml` | modify | YES |
| `CHANGELOG.md` | modify | YES |

**Result: PASS**

#### Check 2 — API surface matches

| Symbol/reference | Plan claim | Actual |
|-----------------|-----------|--------|
| `run-tests.sh` `--coverage` branch at lines 67–70 | missing `PASS_THROUGH_ARGS` | Confirmed: line 67–70 has `dotnet run ... -- --coverage ...` with NO `PASS_THROUGH_ARGS`; line 86 has it in non-coverage branch — PASS |
| `secret-scan.sh` LABELS/PATTERNS arrays (7 entries each) | lines 29–47 | Confirmed: 7 labels (AppToken, UserKey, RFC-1918 IP, Generic apiKey, Bearer token, GitHub PAT, AWS Access Key), 7 patterns — PASS |
| `release.yml` `uses:` lines | 7 references at specific lines | All 7 confirmed present by grep — PASS |
| `ci.yml` `uses:` lines | 2 references (lines 39, 42) | Confirmed at lines 39, 42 — PASS |
| `secret-scan.yml` `uses:` lines | 2 references at lines 16, 28 | Confirmed — PASS |
| Total `uses: action@vN` = 11 lines | Plan task 3 | Confirmed: 11 lines across 3 files — PASS |

**Result: PASS**

#### Check 3 — Verify commands runnable

| Command | Status |
|---------|--------|
| `bash -x .github/scripts/run-tests.sh --coverage --filter "ActionEntryTypeConverterTests" 2>&1 \| head -50` | Valid bash dry-run — PASS |
| `bash .github/scripts/secret-scan.sh selftest` | Script accepts `selftest` as sole positional arg — PASS |
| **`bash .github/scripts/secret-scan.sh`** (no arg, line 129 of plan) | **FAIL** — script requires exactly 1 positional arg (`scan` or `selftest`); invoking with zero args exits 2 with usage message. The plan's verification block line 129 is `bash .github/scripts/secret-scan.sh` without the `scan` argument. |
| `git grep -nE 'uses:\s*[^@\s]+@v[0-9]' .github/workflows/` | Valid ERE — PASS |
| `git grep -nE 'uses:\s*[^@\s]+@[0-9a-f]{40}' .github/workflows/ \| wc -l` | Valid — PASS |
| `grep -nE '#8\|#15\|#24' CHANGELOG.md \| head -10` | Valid — PASS |

**FAIL — One broken verification command:** The scan-mode check on PLAN-1.3 verification line 129 reads `bash .github/scripts/secret-scan.sh` with no argument. The script exits 2 when no arg is provided. The correct command is `bash .github/scripts/secret-scan.sh scan`. This is a documentation error in the plan's verification block, not a behavioral error in the implementation. The acceptance criteria text (line 71–73) correctly names both `selftest` and scan mode; only the Verification code block is wrong. Builder must use `bash .github/scripts/secret-scan.sh scan` instead.

**SHA resolution deferral (WARN):** Plan Task 3 correctly defers SHA resolution to build-time with `gh api` commands. Builder must run the SHA lookup commands at execution time and substitute real values. This is expected and documented. However: if a tagged action version is annotated (vs lightweight), the `gh api /repos/<owner>/<repo>/git/ref/tags/<v>` returns the tag-object SHA, not the commit SHA — the plan includes the correct follow-up step (`gh api .../git/tags/<sha>` to get `.object.sha`). Builder must verify each tag's type.

**Result: FAIL on one verification command** (broken scan invocation — non-blocking defect in the plan document, not in the implementation steps)

#### Check 4 — Forward references

PLAN-1.3 is self-contained. None of its changes depend on PLAN-1.1, PLAN-1.2, or PLAN-1.4.

**Result: PASS**

#### Check 5 — Hidden file dependencies

PLAN-1.3 owns: `run-tests.sh`, `secret-scan.sh`, `secret-scan-fixture.txt`, `release.yml`, `ci.yml`, `secret-scan.yml`, `CHANGELOG.md`. `CHANGELOG.md` conflict with other plans. The workflow files are solely owned by PLAN-1.3 — no overlap.

**Result: WARN** — CHANGELOG.md conflict only.

#### Check 6 — Complexity

7 files, 3 directories (`.github/scripts/`, `.github/workflows/`, `.github/`). Under 10 files, at the 3-directory boundary. Not HIGH RISK but broadest blast radius of the four plans.

**Result: PASS** (not HIGH RISK by the stated thresholds)

---

### PLAN-1.4: Docker operator-doc hygiene (#25, #26)

#### Check 1 — File paths exist

| Path | Action | Exists? |
|------|--------|---------|
| `docker/mosquitto-smoke.conf` | modify | YES |
| `docker/docker-compose.example.yml` | modify | YES |
| `CHANGELOG.md` | modify | YES |

**Result: PASS**

#### Check 2 — API surface matches

| Symbol/reference | Plan claim | Actual |
|-----------------|-----------|--------|
| `mosquitto-smoke.conf` current state: 4 lines, 2 comments + 2 functional | RESEARCH.md §25 | Consistent with source inventory — PASS |
| `docker-compose.example.yml` ports at lines 21–22 | RESEARCH.md §26 | Consistent — PASS |

**Result: PASS**

#### Check 3 — Verify commands runnable

| Command | Status |
|---------|--------|
| `head -n 12 docker/mosquitto-smoke.conf` | Valid — PASS |
| `grep -c '^# WARNING' docker/mosquitto-smoke.conf` | Valid — PASS |
| `grep -F 'release.yml' docker/mosquitto-smoke.conf` | Valid — PASS |
| `grep -F 'allow_anonymous true' docker/mosquitto-smoke.conf` | Valid — PASS |
| `grep -B1 -A1 -F '"8080:8080"' docker/docker-compose.example.yml` | Valid — PASS |
| `docker compose -f docker/docker-compose.example.yml config > /dev/null` | Valid (Docker must be running) — PASS |
| `grep -nE '#25\|#26' CHANGELOG.md` | Valid — PASS |

**Result: PASS**

#### Check 4 — Forward references

No cross-plan dependencies.

**Result: PASS**

#### Check 5 — Hidden file dependencies

PLAN-1.4 owns: `mosquitto-smoke.conf`, `docker-compose.example.yml`, `CHANGELOG.md`. First two are uniquely owned. `CHANGELOG.md` conflict with other plans.

**Result: WARN** — CHANGELOG.md conflict only.

#### Check 6 — Complexity

3 files, 2 directories. Not HIGH RISK.

**Result: PASS**

---

## File → Plan Ownership Map

| File | PLAN-1.1 | PLAN-1.2 | PLAN-1.3 | PLAN-1.4 | Conflict? |
|------|----------|----------|----------|----------|-----------|
| `src/FrigateRelay.Host/StartupValidation.cs` | OWNER | — | — | — | No |
| `src/FrigateRelay.Host/Configuration/ProfileResolver.cs` | OWNER | — | — | — | No |
| `src/FrigateRelay.Host/Configuration/ActionEntryTypeConverter.cs` | — | OWNER | — | — | No |
| `tests/FrigateRelay.Host.Tests/Configuration/StartupValidationNameSanitizationTests.cs` | OWNER (create) | — | — | — | No |
| `tests/FrigateRelay.Host.Tests/Configuration/StartupValidationNameAllowlistTests.cs` | OWNER (create) | — | — | — | No |
| `tests/FrigateRelay.Host.Tests/Observability/ValidateObservabilityTests.cs` | OWNER | — | — | — | No |
| `tests/FrigateRelay.Host.Tests/Configuration/SerilogPathValidationTests.cs` | OWNER | — | — | — | No |
| `tests/FrigateRelay.Host.Tests/Configuration/ActionEntryTypeConverterTests.cs` | — | OWNER | — | — | No |
| `.github/scripts/run-tests.sh` | — | — | OWNER | — | No |
| `.github/scripts/secret-scan.sh` | — | — | OWNER | — | No |
| `.github/secret-scan-fixture.txt` | — | — | OWNER | — | No |
| `.github/workflows/release.yml` | — | — | OWNER | — | No |
| `.github/workflows/ci.yml` | — | — | OWNER | — | No |
| `.github/workflows/secret-scan.yml` | — | — | OWNER | — | No |
| `docker/mosquitto-smoke.conf` | — | — | — | OWNER | No |
| `docker/docker-compose.example.yml` | — | — | — | OWNER | No |
| **`CHANGELOG.md`** | YES | YES | YES | YES | **YES — all 4 plans** |

### CHANGELOG.md conflict analysis

All four plans append entries to `CHANGELOG.md [Unreleased]`. This is an intentional parallel edit to the same file. Since all edits are additive (appending new lines to the `[Unreleased]` section) and no plan deletes or reformats existing content, this is a merge-time conflict, not a logic conflict. The builder must serialize the `CHANGELOG.md` edits (apply last, after all other changes are staged) or use a single commit that aggregates all four sets of CHANGELOG entries. This is manageable; it is not a blocker for parallel execution of the other file changes.

---

## Risk Callouts

### CAUTION-1 — PLAN-1.3 verification block: broken `secret-scan.sh` scan invocation

**Finding:** PLAN-1.3 Verification section line 129 reads `bash .github/scripts/secret-scan.sh` with no argument. The script requires exactly one positional arg (`scan` or `selftest`) and exits 2 with an error message otherwise. Executing this command will fail.

**Impact:** Verifier running post-build PLAN-1.3 verification will see an exit-2 failure that is the plan's documentation error, not a real defect. May cause false-FAIL in automated verification runs.

**Fix for builder:** Replace the bare `bash .github/scripts/secret-scan.sh` with `bash .github/scripts/secret-scan.sh scan` in the verification block. Note: this is a plan-document error only — do not modify the plan file before the architect approves; instead, substitute manually at verification time.

### CAUTION-2 — PLAN-1.1 intra-plan: Task 1 must complete before Task 2 compiles

**Finding:** Task 2 (ValidateObservability scheme allowlist) wraps `endpoint` in `Sanitize(...)`. `Sanitize` is introduced by Task 1. Both tasks are in PLAN-1.1, same file. If the builder implements Task 2 first, the file will not compile until Task 1 is also done.

**Impact:** Minor sequencing constraint within a single plan. No cross-plan issue.

**Mitigation:** Builder completes Task 1 (Sanitize helper) before Task 2 within PLAN-1.1. Not a wave-split issue.

### CAUTION-3 — SHA-pin build-time lookup: annotated vs lightweight tags

**Finding:** PLAN-1.3 Task 3 requires the builder to resolve 7 GitHub Action SHAs at execution time. The plan documents the `gh api` indirection for annotated tags. Docker's actions (`docker/build-push-action`, `docker/metadata-action`, etc.) often use annotated tags. The builder must follow the two-step resolution for each: first call `git/ref/tags/<v>`, then if the returned SHA starts with a tag object (not commit), call `git/tags/<sha>` for the commit SHA.

**Impact:** Skipping the second step results in a tag-object SHA in the workflow file, which GitHub Actions will fail to resolve at runtime — the `smoke` job will error on action checkout.

**Mitigation:** Builder runs both resolution steps for each action; uses `jq '.object.sha'` on both calls to extract the inner SHA. The plan already documents this; it is flagged here as a CAUTION requiring deliberate execution rather than mechanical copy-paste.

### CAUTION-4 — Test-count baseline discrepancy (non-blocking)

**Finding:** RESEARCH.md §3 reports 293 `[TestMethod]` attributes by static grep. CONTEXT-15.md states 291 as the post-Phase-14 baseline. The discrepancy of 2 is unresolved. ROADMAP Phase 15 success criterion is "baseline + 13 new tests."

**Impact:** If the true runner-reported baseline is 291 (some `[TestMethod]` methods are in abstract base classes or otherwise non-runnable), the post-Phase-15 gate is 304. If it is 293, the gate is 306. The builder must run the test suite once before writing tests to establish the actual runner-reported count, and report that count in the build verification.

**Mitigation:** Run `dotnet run --project tests/FrigateRelay.Host.Tests -c Release` and `dotnet run --project tests/FrigateRelay.Abstractions.Tests -c Release` before beginning TDD and capture actual pass counts.

---

## What the Builder Must Verify at Start of Build

1. **Test-count baseline:** Run both test projects with `dotnet run --project tests/<X> -c Release` and record the reported pass count. This is the number to add 13 to for the Phase 15 gate. Do not use the static `grep -c "\[TestMethod\]"` count.

2. **SHA resolution for each of 7 GitHub Actions:** Run the `gh api` commands in PLAN-1.3 Task 3 at build time. For each action, resolve annotated tags via the two-step indirection. Do not use SHAs from any cached or pre-computed source — tags may have moved since RESEARCH.md was written.

3. **Current `actions/checkout` version:** RESEARCH.md cites `v6` for `actions/checkout` across all three workflow files. Verify this is still the current tag (no bump landed since RESEARCH date of 2026-05-07) before resolving the SHA. Same for `setup-dotnet@v5`.

4. **No RFC 1918 IPs in non-fixture files:** Before adding the new secret-scan patterns in PLAN-1.3, run `git grep -nE '10\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}|172\.(1[6-9]|2[0-9]|3[0-1])\.[0-9]{1,3}\.[0-9]{1,3}' -- ':!.github/secret-scan-fixture.txt' ':!.shipyard/' ':!CLAUDE.md'` to confirm the tree is clean before adding the patterns. If any match exists, the scan job will fail immediately after the patterns are added.

5. **CHANGELOG.md serialization:** Because all four plans touch CHANGELOG.md, the builder must apply CHANGELOG edits last (after all other changes in each plan are complete and staged) to avoid repeated merge conflicts. A single final `CHANGELOG.md` edit incorporating all four plans' entries is the cleanest approach.

6. **`secret-scan.sh scan` verification:** The PLAN-1.3 verification block mistakenly omits the `scan` argument. Use `bash .github/scripts/secret-scan.sh scan` when verifying the scan mode passes. **[FIXED 2026-05-07 inline — PLAN-1.3.md line 129 now reads `bash .github/scripts/secret-scan.sh scan`.]**

7. **`ValidateSerilogPath` signature compatibility:** After adding the `isWindows` parameter, ensure the call site at `StartupValidation.cs` line 40 (`ValidateSerilogPath(configuration, errors)`) still compiles without modification — the new parameter must default to `null`.
