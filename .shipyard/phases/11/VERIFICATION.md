# Verification — Phase 11 (Pre-Build Plan-Quality)

**Phase:** 11 — Open-Source Polish
**Date:** 2026-04-28
**Type:** plan-review (pre-execution quality gate)

---

## ROADMAP Deliverable Coverage

| ROADMAP deliverable | Plan / Task | Verifiable how |
|---|---|---|
| `README.md` (overview, quickstart, config walkthrough, scaffold tutorial pointer) | PLAN-2.2 Task 1 | `grep -q 'docker compose' README.md && grep -q 'dotnet new frigaterelay-plugin' README.md` + boilerplate-scrub greps |
| `CONTRIBUTING.md` (standards + test expectations + PR checklist) | PLAN-2.2 Task 2 | `grep -q 'run-tests.sh' && grep -q 'CapturingLogger' && grep -q 'CLAUDE.md'` |
| `LICENSE` (MIT, Brian Lehnen, 2026) | PLAN-2.1 Task 1 | `grep -qE '^Copyright \(c\) 2026 Brian Lehnen$' LICENSE` |
| `.github/ISSUE_TEMPLATE/bug_report.yml` + `feature_request.yml` | PLAN-2.4 Task 1 | YAML-parse + `grep -q 'name: "Bug report"'` |
| `.github/pull_request_template.md` | PLAN-2.4 Task 2 | `grep -q '## Checklist' && grep -q 'run-tests.sh'` |
| `templates/FrigateRelay.Plugins.Template/` scaffold (build clean + 1 passing test) | PLAN-2.3 Tasks 1+2+3 | `dotnet new install …` succeeds; `dotnet new list \| grep frigaterelay-plugin` |
| `docs/plugin-author-guide.md` (one sample per contract) | PLAN-3.1 Task 2 | `grep -cE '^\`\`\`csharp filename=' >= 4` |
| Scaffold-smoke CI step (per ROADMAP wording — relocated to `docs.yml` per D4) | PLAN-2.4 Task 3 | `grep -q 'dotnet new frigaterelay-plugin' .github/workflows/docs.yml` |
| `samples/FrigateRelay.Samples.PluginGuide/` in CI build/test | PLAN-3.1 Tasks 1+3 | `dotnet build FrigateRelay.sln`; `bash .github/scripts/check-doc-samples.sh` |
| README has no reference-project boilerplate | PLAN-2.2 Task 1 acceptance | `grep -nE 'File Transfer Server\|FrigateMQTTProcessingService' README.md` returns 0 |

## CONTEXT-11 Decision Coverage

| Decision | Honored by | Notes |
|---|---|---|
| D1 — MIT / Brian Lehnen / 2026 | PLAN-2.1 Task 1 | Acceptance pins exact copyright line via grep |
| D2 — `dotnet new` template (not copy-and-rename) | PLAN-2.3 Tasks 1+2+3 | template.json with `sourceName: FrigateRelay.Plugins.Example`; CI smoke uses `dotnet new install`/`uninstall` |
| D3 — Phase 11 absorbs 4 adjacent items | PLAN-1.1 (test triage), PLAN-2.1 Task 3 (CHANGELOG), PLAN-2.1 Task 2 (SECURITY), PLAN-2.2 Task 3 (CLAUDE.md staleness) | All four absorbed items have explicit task coverage |
| D4 — NEW `.github/workflows/docs.yml` | PLAN-2.4 Task 3 (creates), PLAN-3.1 Task 3 (extends) | Acceptance: `name: Docs` + `concurrency: group: docs-…`; no coverage flags |
| D5 — shortName `frigaterelay-plugin` | PLAN-2.3 Task 1 | `grep -q '"shortName": "frigaterelay-plugin"'` |
| D6 — GitHub private vulnerability reporting | PLAN-2.1 Task 2 | `grep -q 'security/advisories/new'`; mailto-scan negative |
| D7 — Wave 1 = test triage; doc waves gated; 1 retry; escape-hatch allowed | PLAN-1.1 Tasks 1+2+3 | Tasks 1-2 are fixes; Task 3 is conditional `[Ignore]` + ISSUES.md ID-28+. All Wave 2/3 plans declare `dependencies: [1.1]` |
| D8 — `samples/…PluginGuide/` in `FrigateRelay.sln`; CI test in `docs.yml` only | PLAN-3.1 Task 1 (sln add), PLAN-2.4 Task 3 (`samples-build` job) | `dotnet sln add` invocation; conditional `if: steps.detect.outputs.exists` until 3.1 lands |

## Wave Dependency Graph

```
Wave 1 (gate):            PLAN-1.1  (test triage — 2 fixes + 1 conditional escape-hatch)
                              │
                              ▼  (gates entire phase per D7)
Wave 2 (4 parallel):      PLAN-2.1   PLAN-2.2   PLAN-2.3   PLAN-2.4
                          (root      (README +  (template/ (.github/
                          docs)      CONTRIB +  **)        ** workflows
                                     CLAUDE.md)            + ISSUE/PR)
                              │           │         │           │
                              └───────────┴────┬────┴───────────┘
                                               ▼
Wave 3 (sequential):                     PLAN-3.1
                                         (docs/** + samples/** +
                                          extends docs.yml +
                                          adds doc-rot script)
```

**File-disjoint check (Wave 2):**
- PLAN-2.1: `LICENSE`, `SECURITY.md`, `CHANGELOG.md`
- PLAN-2.2: `README.md`, `CONTRIBUTING.md`, `CLAUDE.md`
- PLAN-2.3: `templates/**` only
- PLAN-2.4: `.github/ISSUE_TEMPLATE/**`, `.github/pull_request_template.md`, `.github/workflows/docs.yml`

Zero file overlap across the four parallel Wave 2 plans. ✅

**Sequential touch on shared files:**
- `.github/workflows/docs.yml` — created in PLAN-2.4 (W2), appended in PLAN-3.1 (W3). Sequential, not parallel. ✅
- `FrigateRelay.sln` — only PLAN-3.1 touches it (via `dotnet sln add`). No conflict. ✅
- `.shipyard/ISSUES.md` — only PLAN-1.1 Task 3 touches it (conditional, escape-hatch only). ✅

## Risk + Sequencing Notes for the Verifier

1. **Wave 1 is the unknown-depth risk.** PLAN-1.1 Task 2 (`TraceSpans_CoverFullPipeline`) deliberately defers test-file lookup to the builder per RESEARCH.md uncertainty flag #1. If the file does not exist or hypothesis is wrong, Task 3 escape-hatch is the safety valve. Verifier should accept "192/192 + 2 `[Ignore]`'d with new ISSUES.md IDs starting at ID-28" as a green Wave 1.

2. **Forward references in Wave 2 are acceptable.** PLAN-2.2 (README/CONTRIBUTING) references `docs/plugin-author-guide.md` and `samples/…` paths that don't exist until PLAN-3.1 lands. This is by design — the paths stabilize at write time and never need rewrites. Verifier should NOT flag these as broken until after Wave 3.

3. **PLAN-2.4 `samples-build` job is intentionally a no-op until PLAN-3.1.** The `if: steps.detect.outputs.exists == 'true'` guard prevents red-on-first-run. Once PLAN-3.1 lands, the guard activates. Don't flag as dead code.

4. **Owner/repo slug is unresolved by design** (RESEARCH.md uncertainty flag #4). PLAN-2.1 Task 2 and PLAN-2.4 Task 1 both delegate to `git remote get-url origin` at builder time. If the repo has no GitHub remote, builder leaves `<owner>/<repo>` literal with TODO. Verifier should accept either form.

5. **Doc-rot check uses Python in a bash wrapper.** PLAN-3.1 Task 3 deviates from `secret-scan.sh`'s pure-bash precedent because multi-line fenced-block extraction is awkward in bash. `setup-python@v5` is added in `docs.yml`. Verifier should not flag this as inconsistency — the precedent was line-by-line greps, not block extraction.

6. **No `Microsoft.NET.Sdk.Web` / `WebApplication` invariants in scope.** Phase 10 owns those. Phase 11 plans do not modify `src/FrigateRelay.Host/`.

7. **Builder must remember:** `dotnet new uninstall templates/FrigateRelay.Plugins.Template/` after every local smoke (PLAN-2.3 Task 3 cleanup). Failing to do so leaves a polluted local SDK template cache that breaks subsequent runs.
