# Verification — Phase 11 (Plan Quality, Verifier Independent Pass)

**Phase:** 11 — Open-Source Polish
**Date:** 2026-04-28
**Type:** plan-quality (independent of architect's self-VERIFICATION.md)
**Verdict:** PASS

---

## ROADMAP Coverage

| ROADMAP Deliverable | Plan / Task | Status |
|---|---|---|
| `README.md` (overview, quickstart, config walkthrough, scaffold tutorial) | PLAN-2.2 Task 1 | COVERED — acceptance criteria audit build/docker/dotnet new commands |
| `CONTRIBUTING.md` (coding standards, test expectations, PR checklist) | PLAN-2.2 Task 2 | COVERED — acceptance criteria specify build/test/CLAUDE.md links |
| `LICENSE` (MIT, Brian Lehnen, 2026) | PLAN-2.1 Task 1 | COVERED — acceptance criteria verify exact copyright line |
| `.github/ISSUE_TEMPLATE/bug_report.yml` + `feature_request.yml` | PLAN-2.4 Task 1 | COVERED — YAML schema + config.yml issue-chooser |
| `.github/pull_request_template.md` | PLAN-2.4 Task 2 | COVERED — checklist mirrors CONTRIBUTING.md |
| `templates/FrigateRelay.Plugins.Template/` (scaffold, build clean, test pass) | PLAN-2.3 Tasks 1+2+3 | COVERED — template.json + csproj + ExampleActionPlugin + test |
| `docs/plugin-author-guide.md` (one sample per contract) | PLAN-3.1 Task 2 | COVERED — 5+ fenced blocks (Action/Validation/Snapshot/Registrar/Program) |
| Scaffold-smoke CI step (per D4 in `docs.yml`, NOT `ci.yml`) | PLAN-2.4 Task 3 | COVERED — workflow creates docs.yml with scaffold-smoke job |
| `samples/FrigateRelay.Samples.PluginGuide/` (in sln, CI in `docs.yml` only) | PLAN-3.1 Task 1 | COVERED — samples project + sln wiring + conditional build in docs.yml |
| README has no reference-project boilerplate | PLAN-2.2 Task 1 | COVERED — acceptance criteria grep for absence of "File Transfer Server" |
| CHANGELOG.md (Keep-a-Changelog, retroactive Phases 1–10) | PLAN-2.1 Task 3 | COVERED — acceptance criteria verify ≥10 phase entries, no [1.0.0] yet |
| SECURITY.md (GitHub private vulnerability reporting, no mailto) | PLAN-2.1 Task 2 | COVERED — acceptance criteria grep for "advisories/new" URL, zero mailto |
| CLAUDE.md staleness fixes (2 surgical edits, D3 #2) | PLAN-2.2 Task 3 | COVERED — edits specified: remove "pre-implementation", update Jenkinsfile note |

**Verdict: All 13 ROADMAP deliverables have explicit plan/task coverage with runnable acceptance criteria.**

---

## CONTEXT-11 Decision Coverage

| Decision | Honored by | Status | Notes |
|---|---|---|---|
| D1 — MIT license (Brian Lehnen, 2026) | PLAN-2.1 Task 1 | PASS | Acceptance: `grep -qE '^Copyright \(c\) 2026 Brian Lehnen$' LICENSE` |
| D2 — `dotnet new` template (NOT copy-and-rename) | PLAN-2.3 | PASS | template.json with shortName + sourceName; smoke uses `dotnet new install`/`uninstall` |
| D3 #1 — Phase 9 integration test triage | PLAN-1.1 Tasks 1+2+3 | PASS | Two test fixes + conditional escape-hatch with ISSUES.md |
| D3 #2 — CLAUDE.md staleness fixes | PLAN-2.2 Task 3 | PASS | Two surgical edits specified with grep acceptance criteria |
| D3 #3 — CHANGELOG.md (retroactive) | PLAN-2.1 Task 3 | PASS | Keep-a-Changelog format, source from HISTORY.md, ≥10 phase sections |
| D3 #4 — SECURITY.md | PLAN-2.1 Task 2 | PASS | GitHub advisory flow, no mailto, maintainer setup note |
| D4 — NEW `docs.yml` workflow (NOT extend `ci.yml`) | PLAN-2.4 Task 3 | PASS | Creates new file with concurrency/path-filters/scaffold-smoke/samples-build jobs |
| D5 — Template short-name `frigaterelay-plugin` | PLAN-2.3 Task 1 | PASS | template.json: `"shortName": "frigaterelay-plugin"` |
| D6 — GitHub private vulnerability reporting (no mailto) | PLAN-2.1 Task 2 | PASS | `security/advisories/new` URL, acceptance criteria scan for zero mailto |
| D7 — Wave 1 = test triage; doc waves gated; 1 retry; escape-hatch allowed | PLAN-1.1 + Wave 2 deps | PASS | Plan structure: Wave 1 → Wave 2 (all declare `dependencies: [1.1]`) → Wave 3 |
| D8 — Samples in `FrigateRelay.sln`; CI test in `docs.yml` only | PLAN-3.1 Task 1 + PLAN-2.4 Task 3 | PASS | Samples project via `dotnet sln add`; conditional `if: steps.detect.outputs.exists` in docs.yml |

**Verdict: All 8 CONTEXT-11 decisions (11 sub-items) are honored with explicit plan coverage and acceptance criteria.**

---

## Structural Rules

### Rule: ≤3 tasks per plan

| Plan | Task Count | Status |
|---|---|---|
| PLAN-1.1 | 3 | PASS |
| PLAN-2.1 | 3 | PASS |
| PLAN-2.2 | 3 | PASS |
| PLAN-2.3 | 3 | PASS |
| PLAN-2.4 | 3 | PASS |
| PLAN-3.1 | 3 | PASS |

**All 6 plans ≤3 tasks. PASS.**

### Rule: File-disjoint within parallel waves

**Wave 2 (parallel, 4 plans):**
- PLAN-2.1: `LICENSE`, `SECURITY.md`, `CHANGELOG.md`
- PLAN-2.2: `README.md`, `CONTRIBUTING.md`, `CLAUDE.md`
- PLAN-2.3: `templates/**` only
- PLAN-2.4: `.github/ISSUE_TEMPLATE/**`, `.github/pull_request_template.md`, `.github/workflows/docs.yml`

**File overlap check:** Zero files appear in more than one Wave 2 plan. ✅

**Wave 3 sequential touches:**
- PLAN-3.1 creates `docs/plugin-author-guide.md`, `samples/**`, `.github/scripts/check-doc-samples.sh`.
- PLAN-3.1 modifies `.github/workflows/docs.yml` (appends job) and `FrigateRelay.sln` (adds project).
- Sequential modification of `docs.yml` (created Wave 2, extended Wave 3) is expected per D4. ✅
- No other Wave 3 plan touches these files. ✅

**Verdict: File-disjoint rule satisfied across waves.**

### Rule: Wave dependencies declared

| Plan | Declared Dependencies | Status |
|---|---|---|
| PLAN-1.1 | [] (Wave 1, gate) | PASS |
| PLAN-2.1 | [1.1] | PASS |
| PLAN-2.2 | [1.1] | PASS |
| PLAN-2.3 | [1.1] | PASS |
| PLAN-2.4 | [1.1] | PASS |
| PLAN-3.1 | [1.1, 2.3, 2.4] | PASS |

**All Wave 2 plans declare Wave 1 gate (1.1). PLAN-3.1 declares both Wave 2 gates it depends on (2.3 for template, 2.4 for docs.yml extension). Dependency graph is acyclic and correct. PASS.**

### Rule: Acceptance criteria are runnable

Spot-check 5 representative criteria:

1. **PLAN-2.1 Task 1 (LICENSE):** `grep -qE '^Copyright \(c\) 2026 Brian Lehnen$' LICENSE` — valid bash, runnable. ✅
2. **PLAN-2.2 Task 1 (README):** `grep -q 'dotnet new frigaterelay-plugin' README.md` — valid bash, runnable. ✅
3. **PLAN-2.3 Task 1 (template.json):** `python3 -c 'import json; json.load(open(...))' ` — valid Python, runnable. ✅
4. **PLAN-2.4 Task 3 (docs.yml):** `python3 -c 'import yaml; yaml.safe_load(open(...))' ` — valid Python, runnable. ✅
5. **PLAN-3.1 Task 3 (doc-rot check):** `bash .github/scripts/check-doc-samples.sh` — valid bash, runnable. ✅

**All acceptance criteria sampled are concrete, executable commands with no vague assertions like "looks good" or "should work." PASS.**

---

## Architect's Self-Coverage Table Accuracy

The architect's own `VERIFICATION.md` (lines 9–22) provides a coverage table. Cross-check 5 entries:

1. **Row 1: "`README.md` ... PLAN-2.2 Task 1"** — Cross-check: PLAN-2.2 Task 1 is indeed `README.md`. Acceptance includes grep for `docker compose` + `dotnet new frigaterelay-plugin`. ✅ Accurate.

2. **Row 3: "`LICENSE` ... PLAN-2.1 Task 1"** — Cross-check: PLAN-2.1 Task 1 is indeed `LICENSE`. Acceptance includes `grep -qE '^Copyright \(c\) 2026 Brian Lehnen$' LICENSE`. ✅ Accurate.

3. **Row 7: "`docs/plugin-author-guide.md` ... PLAN-3.1 Task 2"** — Cross-check: PLAN-3.1 Task 2 is indeed `docs/plugin-author-guide.md`. Architect claims "one sample per contract" (IActionPlugin, IValidationPlugin, ISnapshotProvider, IPluginRegistrar). Plan specifies 5+ fenced blocks per Task 2 description. ✅ Accurate.

4. **Row 8: "Scaffold-smoke CI step ... PLAN-2.4 Task 3"** — Cross-check: PLAN-2.4 Task 3 creates `.github/workflows/docs.yml` with the `scaffold-smoke` job. Acceptance includes `grep -q 'dotnet new frigaterelay-plugin' .github/workflows/docs.yml`. ✅ Accurate.

5. **Row 5: "`.github/pull_request_template.md` ... PLAN-2.4 Task 2"** — Cross-check: PLAN-2.4 Task 2 is indeed `.github/pull_request_template.md`. Acceptance includes `grep -q 'run-tests.sh'` and `grep -q 'CHANGELOG.md'`. ✅ Accurate.

**Verdict: 5/5 architect table entries spot-checked are accurate. No mismatches found.**

---

## Architect-Flagged Risks (5 items)

### Risk 1: PLAN-1.1 Task 2 test-file lookup deferred to builder

**Plan text:** PLAN-1.1 Task 2 explicitly defers file-path lookup of `TraceSpans_CoverFullPipeline` test to builder ("builder MUST first run `ls tests/FrigateRelay.IntegrationTests/Observability/` and read the file before editing").

**Verifier assessment:** This is acceptable. The uncertainty flag (#1 in RESEARCH.md) explicitly flags this as unknown at plan-time due to Phase 9 delivery ambiguity. The plan is conservative: builder does the lookup, then proceeds with one retry budget per D7. If the file doesn't exist, escape-hatch via Task 3 creates an ISSUES.md entry and marks the test ignored. This is a documented, bounded risk with a safety valve.

**Verdict: ACCEPTABLE.**

### Risk 2: Wave 2 forward-references to PLAN-3.1 paths intentional

**Plan text:** PLAN-2.2 (README, CONTRIBUTING) references `docs/plugin-author-guide.md` and `samples/FrigateRelay.Samples.PluginGuide/` paths. These don't exist until PLAN-3.1 lands (Wave 3).

**Architect's coverage note (VERIFICATION.md line 73):** "Forward references in Wave 2 are acceptable. PLAN-2.2 (README/CONTRIBUTING) references `docs/plugin-author-guide.md` and `samples/…` paths that don't exist until PLAN-3.1 lands. This is by design — the paths stabilize at write time and never need rewrites. Verifier should NOT flag these as broken until after Wave 3."

**Verifier assessment:** This is correct. The references are **by path**, not by content. When PLAN-3.1 lands, those paths exist and the references resolve. At build-time of Wave 2, the references are forward-pointers that are semantically valid (documented as "see X"), not broken links. The acceptance criteria for PLAN-2.2 Task 1 include `grep -q 'docs/plugin-author-guide.md' README.md`, which passes if the string is present (not if the file exists). This pattern is sound.

**Verdict: ACCEPTABLE — verifier will defer content-validation of those references to post-Phase-11 checks.**

### Risk 3: PLAN-2.4 samples-build job intentionally no-op until PLAN-3.1

**Plan text:** PLAN-2.4 Task 3 adds `samples-build` job to `docs.yml` with conditional `if: steps.detect.outputs.exists == 'true'` guard. The detect step checks for the existence of `samples/FrigateRelay.Samples.PluginGuide`. Until PLAN-3.1 lands, the directory doesn't exist, so detect returns false, and the build steps skip.

**Verifier assessment:** This is correct and well-documented. The conditional is a standard GitHub Actions pattern that prevents first-run-red. The path filter on the workflow (line 175) `samples/**` also means the job doesn't even trigger unless a commit touches the samples directory. Both layers ensure the job activates cleanly once PLAN-3.1 lands.

**Verdict: ACCEPTABLE — no-op-until-dependency-lands is a standard workflow pattern.**

### Risk 4: Owner/repo slug deferred to `git remote` lookup at builder time

**Plan text:** PLAN-2.1 Task 2 (SECURITY.md) and PLAN-2.4 Task 1 (config.yml) both have `<owner>/<repo>` placeholders for the GitHub advisory URL. Builder runs `git remote get-url origin` to resolve them.

**Verifier assessment:** This is acceptable. The SECURITY.md and config.yml files are repo-root deliverables. Hardcoding a org slug would either require assuming a GitHub org (which the user hasn't specified) or baking in a placeholder. Deferring to `git remote` is the correct approach for a greenfield repo. Acceptance criteria include `grep -q 'security/advisories/new'`, which passes regardless of the `<owner>/<repo>` form (either resolved from git remote or left as literal placeholder).

**Verdict: ACCEPTABLE.**

### Risk 5: PLAN-3.1 doc-rot check uses Python in bash heredoc (deviates from `secret-scan.sh` precedent)

**Plan text:** PLAN-3.1 Task 3 creates `.github/scripts/check-doc-samples.sh` using `python3 - <<'PY'` heredoc to extract and compare multiline fenced code blocks. The precedent, `secret-scan.sh`, is pure bash (line-by-line grep).

**Verifier assessment:** This is justified. Multiline fenced-block extraction (regex over `re.M | re.S` flags) is awkward in pure bash. The plan explicitly notes: "Builder may use Python via `python3 - <<'PY'` heredoc as shown above, or pure bash with `awk`/`sed` — preference for Python because the regex over multiline fenced blocks is awkward in pure bash." This is a conscious architectural choice, not a deviation. The precedent (secret-scan.sh) is line-by-line; this task requires block extraction. The deviation is justified.

**Verdict: ACCEPTABLE — deviation is justified by the structural difference (line-by-line vs block extraction).**

---

## Findings

### Critical (block build)

None identified. All plans have sound structure and coverage.

### Minor (note for builder)

1. **PLAN-1.1 Wave 1 gate has unknown depth.** Task 2 file-path lookup deferred to builder per D7 budget. This is by design, but if the triage fails and both retry budgets are exhausted, Wave 2 doc work is gated. Plan includes escape-hatch; mark tests as `[Ignore("ID-NN")]` and proceed. Builder should allocate extra time for Task 1 reproduction.

2. **PLAN-2.1 Task 3 (CHANGELOG.md) depends on HISTORY.md structure.** Plan specifies builder must grep `.shipyard/HISTORY.md` for `## Phase [0-9]+` entries to confirm structure. If HISTORY.md does not follow that pattern, builder must adjust the source-discovery logic.

3. **PLAN-2.2 Task 3 CLAUDE.md edits are surgical.** Both edits are specified with exact strings to find/replace. Builder should use `grep` to locate the lines before editing to confirm they exist in the exact form specified.

4. **PLAN-3.1 Task 1 samples project depends on relative path stability.** The scaffold template's ProjectReference paths assume samples is in-repo (`..\..\..\..\src\FrigateRelay.Abstractions`). PLAN-3.1 Task 1 documents this and specifies the relative path form. Acceptance criteria verify the path with grep.

5. **PLAN-3.1 Task 2 and Task 3 coupling: doc-rot check requires samples files to exist.** If Task 1 samples project is incomplete or misspelled, Task 3's byte-match check will fail. Builder should complete Task 1 fully before running Task 3 verification.

---

## Structural Verification

**Wave dependency graph (from VERIFICATION.md lines 39–54):**

```
Wave 1 (gate):     PLAN-1.1
                      │
                      ▼  (gates Wave 2)
Wave 2 (parallel): PLAN-2.1, PLAN-2.2, PLAN-2.3, PLAN-2.4
                      │
                      ▼  (gates Wave 3)
Wave 3 (seq):      PLAN-3.1
```

**Verified: All Wave 2 plans declare `dependencies: [1.1]`. PLAN-3.1 declares `dependencies: [1.1, 2.3, 2.4]` (correct dependencies on the two Wave 2 plans it directly depends on).**

**File lineage:**

- Wave 2 plans touch 13 distinct files/directories (all repo-root + `.github/ISSUE_TEMPLATE` + `templates/`).
- Wave 3 appends to `.github/workflows/docs.yml` (created Wave 2) and `FrigateRelay.sln`.
- No file is created or destructively modified by multiple plans.

**Verified: Structural integrity is sound.**

---

## Recommendation

**PASS**

All 6 plans have been verified independent of the architect's self-VERIFICATION.md:

1. ✅ **ROADMAP coverage**: All 13 Phase 11 deliverables have explicit plan/task coverage.
2. ✅ **CONTEXT-11 decisions**: All 8 decisions (11 sub-items) are honored.
3. ✅ **Structural rules**: ≤3 tasks/plan, file-disjoint waves, acyclic dependencies, runnable acceptance criteria.
4. ✅ **Architect's self-coverage**: 5 spot-checks of the architect's own table all accurate.
5. ✅ **Flagged risks**: All 5 architect-noted risks are acceptable or justified.

**No critical gaps. The architect produced a comprehensive, well-structured, executable plan set that covers the full Phase 11 scope.**

---

## Verifier's Note on Wave 1 Risk

PLAN-1.1 is the only unknown-depth work in the phase. The two failing integration tests may take 30 minutes to fix (shallow issues) or may require deeper investigation (moderate issues). The plan budgets **1 retry per test** and includes a clean **escape-hatch** (mark ignored, create ISSUES.md entry). Verifier recommends builder allocate +1 hour to Wave 1 and not proceed to Wave 2 until either:

- Both tests pass (all acceptance criteria in Task 1 + 2 exit 0), OR
- Both tests are explicitly `[Ignore]`'d with new ISSUES.md entries (Task 3 escape-hatch).

D7 acceptance gate (per context line 97): `194/194 passing OR 192/192 passing + 2 [Ignore]'d with tracking IDs`.

---

<!-- context: turns=1, compressed=no, task_complete=yes -->
