# Phase 2 Plan Verification

**Date:** 2026-04-24  
**Type:** plan-review  
**Reviewer:** Verification Engineer (Haiku 4.5)

---

## Verdict: READY

All Phase 2 plans meet specification compliance, decision honoring, and structural requirements. Coverage is complete, no conflicts detected, open-question resolutions are documented in each plan.

---

## Coverage Matrix

| ROADMAP Deliverable | Owning Plan | Status |
|---|---|---|
| `.github/workflows/build.yml` (triggers, matrix, restore/build/test, coverage artifacts) | PLAN-2.1 (`ci.yml`) | **COVERED** — File named `ci.yml` per D1 split (equivalent to "build.yml"; GH Actions PR gate). Coverage excluded per D1 (Jenkins-owned). |
| `.github/workflows/secret-scan.yml` + fake-token fixture | PLAN-1.2 | **COVERED** — Fixture at `.github/secret-scan-fixture.txt`, workflow at `.github/workflows/secret-scan.yml`, self-test job with tripwire proof. |
| `.github/dependabot.yml` | PLAN-1.1 | **COVERED** — nuget + github-actions ecosystems, weekly Monday schedule, Microsoft.Extensions/MSTest grouping. |
| Jenkinsfile (coverage, per D1 split) | PLAN-3.1 | **COVERED** — Scripted Jenkinsfile at repo root, MTP coverage flags, cobertura XML archiving. |

**Result:** Every ROADMAP Phase 2 deliverable is assigned to exactly one plan. No missing deliverables. ✓

---

## CONTEXT-2 Decision Honoring

| Decision | Requirement | Plan Evidence | Status |
|---|---|---|---|
| **D1 — GH Actions for PR gate, Jenkinsfile for coverage** | GH `ci.yml` (or equivalent) and Jenkinsfile must both exist; split is clean. | PLAN-2.1: `ci.yml` with "D1/D2 split" comment (line 113). PLAN-3.1: Jenkinsfile with "D1" cite (line 21). No coverage in `ci.yml`. Jenkinsfile has coverage. | **PASS** |
| **D2 — Test invocation is `dotnet run`, not `dotnet test`** | Every test invocation must use `dotnet run --project ... -c Release` pattern. | PLAN-2.1 Task 1: Steps 5–6 use `dotnet run` (lines 109–110). PLAN-3.1 Task 1: Two coverage invocations via `dotnet run` (lines 136–137). | **PASS** |
| **D3 — No shutdown-smoke step in Phase 2 CI** | Phase 2 CI must not include a graceful-shutdown test. (Phase 4 integration tests own it.) | PLAN-2.1: No shutdown smoke mentioned. PLAN-1.2, PLAN-3.1: No shutdown smoke. Explicit deferral in CONTEXT-2.md lines 48–56. | **PASS** |
| **D4 — Secret-scan has a self-test job against committed fixture** | `secret-scan.yml` must have two jobs: `scan` (main) and `tripwire-self-test` (fixture-only). Fixture must be committed and excluded from main scan. | PLAN-1.2 Task 3: Two jobs `scan` and `tripwire-self-test` (lines 145–149). Task 2: Script with `selftest` mode (line 117). Task 1: Fixture file with seven labelled lines (lines 99–103). Path exclusion in Task 2 (line 113). | **PASS** |
| **D5 — Dependabot covers `nuget` + `github-actions` ecosystems, not `docker`** | `.github/dependabot.yml` must have exactly two `updates:` entries; NuGet and GitHub Actions only. Header comment cites D5 scope and MSTest.Sdk non-issue. | PLAN-1.1 Task 1: Two ecosystem entries verified by `yq` checks (lines 72–76). Header comment cites D5 and Open Question 5 (lines 18–35). RESEARCH.md lines 104–144 confirms template. | **PASS** |

**Result:** All five CONTEXT-2 decisions are honored in the plans. ✓

---

## Plan Structural Compliance

| Plan | Task Count | Task Blocks Complete? | `files_touched` Present? | Wave Deps Correct? | Parallel-Safe? |
|---|---|---|---|---|---|
| PLAN-1.1 (Dependabot) | 1 | **PASS** — action, verify, done blocks present. | **PASS** — `.github/dependabot.yml` listed. | **PASS** — Wave 1, no dependencies. | **PASS** — Disjoint file from PLAN-1.2. |
| PLAN-1.2 (Secret-scan) | 3 | **PASS** — All three tasks have action, verify, done blocks. | **PASS** — Three files listed (fixture, script, workflow). | **PASS** — Wave 1, no dependencies. | **PASS** — Disjoint files from PLAN-1.1. |
| PLAN-2.1 (CI workflow) | 1 | **PASS** — action, verify, done blocks present. | **PASS** — `.github/workflows/ci.yml` listed. | **PASS** — Wave 2, deps [1.1, 1.2] correct. | **PASS** — Depends on Wave 1 foundation. |
| PLAN-3.1 (Jenkinsfile) | 1 | **PASS** — action, verify, done blocks present. | **PASS** — `Jenkinsfile` listed. | **PASS** — Wave 3, deps [1.1, 1.2, 2.1] correct. | **PASS** — Depends on Waves 1–2 conventions. |

**Result:** All plans have ≤ 3 tasks, complete task blocks, correct file listings, and consistent wave dependencies. ✓

---

## Acceptance Criteria Testability

Every success criterion in each plan is verifiable by a concrete command or structural check:

| Plan | Criterion | Verification Method |
|---|---|---|
| PLAN-1.1 | Version is `2` | `yq eval '.version' .github/dependabot.yml` |
| PLAN-1.1 | Two ecosystems present | `yq eval '.updates \| length'` and `yq eval '.updates[0].package-ecosystem, .updates[1].package-ecosystem'` |
| PLAN-1.1 | NuGet groups include microsoft-extensions and mstest | `yq eval '.updates[0].groups \| keys'` |
| PLAN-1.2 | Fixture file exists with seven lines | `test -f .github/secret-scan-fixture.txt && grep -c -E '^(pattern)'` |
| PLAN-1.2 | Script is executable and exits 0 on clean tree | `test -x .github/scripts/secret-scan.sh && bash .github/scripts/secret-scan.sh scan` |
| PLAN-1.2 | Self-test passes all seven patterns | `bash .github/scripts/secret-scan.sh selftest` (prints seven `PASS:` lines) |
| PLAN-1.2 | Workflow YAML parses and has two jobs | `yq eval '.jobs \| keys'` and `yq eval '.on \| keys'` |
| PLAN-2.1 | Workflow has `on: [push, pull_request]` | `yq eval '.on \| keys'` |
| PLAN-2.1 | Matrix includes `ubuntu-latest` and `windows-latest` | `yq eval '.jobs."build-and-test".strategy.matrix.os'` |
| PLAN-2.1 | setup-dotnet uses `global-json-file` | `yq eval '.jobs."build-and-test".steps[1].with."global-json-file"'` |
| PLAN-2.1 | Two `dotnet run` invocations (no `dotnet test`) | `grep -c 'dotnet run --project tests/'` (expect 2) |
| PLAN-3.1 | Jenkinsfile uses Docker agent with correct image | `grep -q "image 'mcr.microsoft.com/dotnet/sdk:10.0'" Jenkinsfile` |
| PLAN-3.1 | Two `dotnet run` coverage invocations | `grep -c 'dotnet run --project tests/'` (expect 2) |
| PLAN-3.1 | Coverage plugin and legacy fallback present | `grep -q "parser: 'COBERTURA'"` and `grep -q 'coberturaPublisher'` |
| PLAN-3.1 | No Docker volume args (per OQ4) | `! grep -q "args '-v"` |

**Result:** All acceptance criteria are measurable and unambiguous. ✓

---

## Architecture Invariants

| Invariant | Status | Evidence |
|---|---|---|
| No workflow references `dotnet test` against MTP projects | **PASS** | PLAN-2.1 uses `dotnet run` (lines 109–110). PLAN-3.1 uses `dotnet run` with MTP flags (lines 136–137). RESEARCH.md §DNWQ Patterns That DO NOT Apply (line 377) confirms MTP blocks `dotnet test` on .NET 10. |
| No workflow commits secrets (even fake, outside fixture) | **PASS** | PLAN-2.1 and PLAN-3.1 workflows contain no secret-shaped strings. Secrets are only in `.github/secret-scan-fixture.txt` (excluded from main scan per PLAN-1.2 Task 2 line 113). |
| Fixture file is the ONLY place secret-shaped strings may live | **PASS** | PLAN-1.2 Task 1 creates fixture with seven labelled synthetic strings (lines 99–103). Task 2 excludes fixture from main scan (line 113). RESEARCH.md §Secret-Scan Fixture Format warns fixture strings are fake (lines 283–310). |
| No plan references excluded third-party dependencies | **PASS** | None of the four plans mention `Newtonsoft`, `Serilog`, `OpenTracing`, `App.Metrics`, or `DotNetWorkQueue`. (These are explicitly excluded per PROJECT.md line 74.) |

**Result:** All architecture invariants are respected. ✓

---

## Architect's Open-Question Resolutions

All six open questions from CONTEXT-2.md are resolved in the plans with documented rationales:

| Question | Resolution | Plan Location | Rationale Present? |
|---|---|---|---|
| OQ1: `actions/setup-dotnet@v4` vs `@v5` | **@v4** (stable major, `global-json-file` support established in PR #224 2021-09-13) | PLAN-2.1 Context, lines 30–38 | **YES** — Explains stable-major parity, Dependabot float. |
| OQ2: Runner SDK edge case (no `10.0.x` at all) | Install from distribution channel; action fails loudly with "Version not found" if band unavailable (desired signal). No fallback entry needed. | PLAN-2.1 Context, lines 40–49 | **YES** — Explains install behavior, rationale for no fallback. |
| OQ3: Jenkins Coverage plugin vs legacy Cobertura | **Modern Coverage plugin** (`recordCoverage`). Legacy fallback commented inline. | PLAN-3.1 Context, lines 48–58 | **YES** — EOL status, richer trend data, commented fallback. |
| OQ4: NuGet cache topology (Docker volume vs workspace) | **Workspace-local** (`.nuget-cache` relative path). Zero pre-provisioning, portable. | PLAN-3.1 Context, lines 60–75 | **YES** — Explains portability, workspace reuse, cold-build budget. |
| OQ5: Dependabot MSTest.Sdk non-issue | Non-issue. Test projects use `Microsoft.NET.Sdk`, not `MSTest.Sdk`. Regular `PackageReference`. | PLAN-1.1 Context, lines 32–35 | **YES** — Cites dependabot-core#12824, confirms non-applicability. |
| OQ6: `fetch-depth` (history vs shallow) | **Shallow** (`fetch-depth: 1`, default). `git grep` on index does not need history. | PLAN-1.2 Context, lines 51–55 | **YES** — Explains file-content-scan nature, ~2x speed win. |

**Result:** All six architect decisions are documented with clear rationales. ✓

---

## Verification Command Sanity

Each plan's verify section is executable and non-vague:

| Plan | Verify Commands | Assessment |
|---|---|---|
| PLAN-1.1 Task 1 | `yq eval '.version' .github/dependabot.yml && yq eval '.updates \| length' ...` | **CONCRETE** — Three separate `yq` checks with specific keys and expected outputs (2, 2, ecosystem names, group keys). |
| PLAN-1.2 Task 1 | `test -f .github/secret-scan-fixture.txt && grep -c -E '^(pattern)'` | **CONCRETE** — File existence + pattern count (expect 7). |
| PLAN-1.2 Task 2 | `test -x .github/scripts/secret-scan.sh && bash .github/scripts/secret-scan.sh scan && bash .github/scripts/secret-scan.sh selftest` | **CONCRETE** — Executable check + two script invocations with exit-code verification. |
| PLAN-1.2 Task 3 | `yq eval '.on \| keys' .github/workflows/secret-scan.yml && yq eval '.jobs \| keys'` | **CONCRETE** — Job and trigger key checks. |
| PLAN-2.1 Task 1 | `yq eval '.on \| keys' .github/workflows/ci.yml && yq eval '.env' .github/workflows/ci.yml && ... grep -c 'dotnet run --project tests/'` | **CONCRETE** — Matrix OS, env vars, setup-dotnet step, grep for two `dotnet run` invocations. |
| PLAN-3.1 Task 1 | `test -f Jenkinsfile && grep -q "image 'mcr.microsoft.com/dotnet/sdk:10.0'" && grep -c 'dotnet run --project tests/' && ... && ! grep -q "args '-v"` | **CONCRETE** — Seven separate `grep` checks for image, coverage patterns, fallback, and no Docker volume args. |

**Result:** All verification commands are concrete, runnable, and unambiguous. No "check that it works" vagueness detected. ✓

---

## Regressions Check

Prior Phase 1 VERIFICATION.md exists and was reviewed. No Phase 2 plans introduce changes that would cause Phase 1 to regress:

- Phase 1 deliverables (buildable solution with MSTest, global.json, csproj structure) are unchanged by Phase 2 plans.
- Phase 2 plans add new files (workflows, dependabot, Jenkinsfile) without modifying Phase 1 source or project files.
- No dependency changes introduced that would affect Phase 1 build.

**Result:** No regressions detected. ✓

---

## File Conflict Check

Four plans touch six files with no overlaps:

| File | Plans Touching It |
|---|---|
| `.github/dependabot.yml` | PLAN-1.1 (creator) |
| `.github/workflows/secret-scan.yml` | PLAN-1.2 (creator) |
| `.github/secret-scan-fixture.txt` | PLAN-1.2 (creator) |
| `.github/scripts/secret-scan.sh` | PLAN-1.2 (creator) |
| `.github/workflows/ci.yml` | PLAN-2.1 (creator) |
| `Jenkinsfile` | PLAN-3.1 (creator) |

**Result:** No file conflicts. Each plan owns its deliverables exclusively. ✓

---

## Scope Boundary Check

All plans respect Phase 2 boundaries (no bleeding into Phase 1, 3, 4, or 10):

| Phase Boundary | Check | Result |
|---|---|---|
| Phase 1 (buildable solution) | Plans do not modify source, csproj, or global.json | **PASS** |
| Phase 3 (MQTT ingestion) | Plans do not mention MQTT, FrigateMqtt, or event sources | **PASS** |
| Phase 4 (integration tests) | D3 and CONTEXT-2.md explicitly defer graceful-shutdown testing to Phase 4 | **PASS** |
| Phase 10 (Docker + release) | PLAN-1.1 defers `docker` ecosystem to Phase 10 (line 45). PLAN-3.1 defers publish/deploy (line 11). | **PASS** |

**Result:** Phase 2 scope is tight and correctly bounded. ✓

---

## Summary of Findings

### Coverage
- ✓ All four ROADMAP Phase 2 deliverables are covered by exactly one plan each.
- ✓ No deliverables are missing or duplicated.

### Decision Honoring
- ✓ All five CONTEXT-2 decisions (D1–D5) are honored in plan text and acceptance criteria.
- ✓ All six open-question resolutions are documented with explicit rationales.

### Structural Quality
- ✓ All plans have ≤ 3 tasks with complete `<action>`, `<verify>`, `<done>` blocks.
- ✓ All plans list `files_touched` in front matter.
- ✓ Wave dependencies are correct and consistent (Wave 1 independent, Wave 2 depends on Wave 1, Wave 3 depends on Waves 1–2).
- ✓ No circular dependencies or missing dependencies detected.

### Testability
- ✓ All acceptance criteria are verifiable by concrete, runnable commands.
- ✓ No subjective criteria like "code is clean" or "works correctly."
- ✓ Verification commands use standard tools (`yq`, `grep`, `test`, `bash`).

### Architecture Invariants
- ✓ No `dotnet test` against MTP projects.
- ✓ No secrets committed outside the fixture.
- ✓ Fixture is excluded from main scan.
- ✓ No excluded dependencies mentioned.

### Regressions
- ✓ No Phase 1 files are modified.
- ✓ No Phase 1 dependencies are changed.

### Conflicts
- ✓ No file is touched by more than one plan.
- ✓ Plans are parallelizable within their wave (1.1 and 1.2 can run concurrently).

---

## Recommendations

No blocking issues detected. Plans are **ready for execution**. 

**Optional process notes (non-blocking):**
1. Verify that the target Jenkins instance has the **Coverage plugin** installed before PLAN-3.1 execution. The Jenkinsfile includes a fallback comment for the legacy Cobertura plugin, but the primary path uses `recordCoverage()`.
2. First push to GitHub after merge will serve as runtime proof for PLAN-2.1 (ci.yml) and PLAN-1.2 (secret-scan). Document the run time (should be ≤ 3 minutes per leg per CONTEXT-2.md line 82).
3. First Dependabot run (PLAN-1.1) will occur automatically on merge; GitHub Insights will show a confirmation in the Dependency graph tab if no updates exist.

---

## Verdict

**READY** — Phase 2 plans are specification-compliant, architecturally sound, and ready for execution.

All success criteria are measurable, all decisions are documented, and no conflicts or regressions were detected.
