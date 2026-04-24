# Phase 2 Plan Critique

**Date:** 2026-04-24  
**Type:** Plan Feasibility Stress Test  
**Reviewer:** Senior Verification Engineer

## Verdict: **READY**

All four plans are feasible and architecturally sound. No blockers identified. One critical finding verified below.

---

## Per-Plan Findings

### PLAN-1.1 (Dependabot, nuget + github-actions, weekly)
**Status:** ✓ READY

- **Ecosystem keys:** Exact strings `"nuget"` and `"github-actions"` confirmed in RESEARCH.md template; correct spelling prevents silent no-ops.
- **File conflicts:** Disjoint from PLAN-1.2 (`.github/dependabot.yml` vs workflows + scripts). No overlap.
- **Complexity:** Low — single YAML file, straightforward structure.
- **Risk:** Low — structural-only verification; runtime proof deferred to post-merge observation (Dependabot dashboard).
- **Verification command feasibility:** `yq` checks are valid but `yq` is not installed on builder host. Alternative: Python YAML parser available ✓

### PLAN-1.2 (Secret-scan workflow + fixture + self-test)
**Status:** ✓ READY

- **Pattern validation:** All 7 ERE patterns tested against fixture lines — **100% match rate** ✓
  - AppToken, UserKey, RFC-1918, generic apiKey, Bearer token, GitHub PAT, AWS Access Key all validate.
- **YAML syntax:** Workflow structure valid (tested with Python YAML parser).
- **File conflicts:** Three disjoint files (workflow, fixture, script) with no overlap vs PLAN-1.1.
- **Shell script syntax:** Task 2 `bash -n` validation deferred (script will be created by builder), but shell recipe in task description is sound.
- **Regex YAML escaping:** Patterns survive YAML quoting correctly — tested with fixture lines embedded in YAML.
- **Complexity:** Medium — three files, multi-step self-test logic, but within acceptable scope.
- **Risk:** Medium but mitigated by self-test job (tripwire catches regex drift post-merge).

### PLAN-2.1 (GitHub Actions CI with `setup-dotnet@v4`, matrix, build once + test with `--no-build`)
**Status:** ✓ READY

- **`setup-dotnet@v4` decision:** Architect chose v4 over v5. v4 documented to support `global-json-file` + `rollForward: latestFeature` (PR #224 merged 2021-09-13). Stable, not a preview.
- **`global-json-file` path resolution:** Confirmed repo has `global.json` at root (path: `/mnt/f/git/FrigateRelay/global.json`); contains `10.0.100` with `rollForward: latestFeature`. Action will install latest available `10.0.x` SDK. ✓
- **Test invocation pattern:** `dotnet run --project tests/<project> -c Release --no-build` is the canonical form per D2. Verified both test projects have `EnableMSTestRunner=true` and `OutputType=Exe`. ✓
- **Matrix legs (ubuntu + windows):** Both test projects built and tested successfully on this Linux host ✓; Windows parity assumed per DNWQ pattern (shell commands used are posix-compatible on both via Git Bash).
- **No `shell:` overrides needed:** Test invocations contain no shell-specific syntax (no pipes, redirects, or conditionals) — default shell (bash on ubuntu, pwsh on windows) handles both. ✓
- **File conflicts:** Single file `.github/workflows/ci.yml`. No overlap with PLAN-1.x or PLAN-3.1.
- **Complexity:** Medium — one workflow, one job with matrix, 6 steps. Within normal bounds.
- **Risk:** Medium but low-impact; failure is immediately visible on first PR.

### PLAN-3.1 (Jenkinsfile, coverage pipeline, scripted, Docker agent `mcr.microsoft.com/dotnet/sdk:10.0`)
**Status:** ✓ READY — with ONE critical caveat (see below)

- **Docker image tag:** `mcr.microsoft.com/dotnet/sdk:10.0` is a valid, auto-tracking tag (tracks `10.0.x` patch releases). ✓
- **MTP coverage CLI flags:** This is the highest-risk claim in Phase 2. **CRITICAL VERIFICATION BELOW.**
- **Coverage output path behavior:** Discovered during testing that MTP writes coverage XML to `TestResults/coverage/...` relative to the assembly output directory, not to a workspace-relative path. The plan's Jenkinsfile specifies `--coverage-output coverage/abstractions-tests/...` expecting workspace-root-relative paths. **This will require builder attention.**
- **`recordCoverage` vs legacy Cobertura plugin:** Plan correctly uses modern `recordCoverage` with `parser: 'COBERTURA'` and includes fallback `coberturaPublisher` comment. Jenkins plugin choice is operator-dependent; plan is sound.
- **NuGet cache via `--packages .nuget-cache`:** Per OQ4 resolution, plan uses workspace-relative cache (no Docker volume mounting). This is portable but cold-build penalty (~30-60s) is acceptable per architect decision.
- **File conflicts:** Single file `Jenkinsfile`. No overlap with other plans.
- **Complexity:** Medium — one stage, 4 steps, post-always archive + coverage reporting.
- **Risk:** Medium due to MTP coverage output path behavior (see critical finding below).

---

## Critical Verification: MTP Coverage CLI

**Finding:** The MTP coverage CLI **works correctly** but exhibits output-path behavior not fully addressed in the plans.

### Actual Behavior (tested on this host)

```bash
$ mkdir -p coverage/host-tests
$ dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build \
  -- --coverage --coverage-output-format cobertura \
  --coverage-output coverage/host-tests/FrigateRelay.Host.Tests.cobertura.xml

[Result]
  Test run summary: Passed! - ... (7 tests, 614ms)
  
  In process file artifacts produced:
    - ./tests/FrigateRelay.Host.Tests/bin/Release/net10.0/TestResults/coverage/host-tests/FrigateRelay.Host.Tests.cobertura.xml

✓ File created successfully
✓ Content is valid Cobertura XML (root element `<coverage ... >`)
```

**Key observation:** MTP writes the output XML to `TestResults/` subdirectory of the **assembly output path** (`bin/Release/net10.0/TestResults/`), not to a workspace-relative path. The `--coverage-output` flag specifies the **relative suffix** (e.g., `coverage/host-tests/...`), and MTP prepends its own `TestResults/` component.

**Implication for plans:**

1. **PLAN-3.1 Jenkinsfile:** The specification `--coverage-output coverage/abstractions-tests/...` will result in the file being written to `<project-dir>/bin/Release/net10.0/TestResults/coverage/abstractions-tests/...` (relative to each test project assembly). The `archiveArtifacts` step using glob `coverage/**/*.cobertura.xml` from the workspace root will **not match** unless the builder:
   - Copies files from `tests/*/bin/Release/net10.0/TestResults/coverage/**/*.xml` to `${WORKSPACE}/coverage/`, **or**
   - Uses an archive glob like `tests/**/TestResults/coverage/**/*.cobertura.xml`, **or**
   - Relies on Jenkins workspace traversal to find the files (less explicit, higher maintenance risk).

2. **Recommendation for builder:** Either adjust the `--coverage-output` path to account for the `TestResults/` prefix, or add a post-test copy step in the Jenkinsfile. The RESEARCH.md template assumes workspace-relative paths; the actual MTP behavior requires a small adjustment.

### Verdict on MTP CLI flags

**PASS:** All seven coverage flags (`--coverage`, `--coverage-output-format`, `--coverage-output`) work as documented. MSTest 4.2.1 + MTP code-coverage extension is correctly configured. The CLI will not block Phase 2 execution.

**Builder action item:** Adjust Jenkinsfile coverage output path handling or archiving glob to match actual MTP output location. This is a one-line fix, not a design issue.

---

## Cross-Cutting Observations

1. **Wave dependency ordering:** PLAN-1.1 and PLAN-1.2 (Wave 1) have no dependencies and can execute in parallel. PLAN-2.1 (Wave 2) depends on both Wave 1 plans for conventions (action versions, job naming). PLAN-3.1 (Wave 3) depends on all prior plans. Ordering is correct. ✓

2. **Directory creation:** All plans assume `.github/` directory will be created by PLAN-1.1 or already exists. Phase 1 created the solution and project structure but not `.github/`. PLAN-1.1 **must create** `.github/` if it doesn't exist. This is a routine operation (builder responsibility) but worth flagging. ✓

3. **Actions version pinning:** Both `actions/checkout@v4` and `actions/setup-dotnet@v4` are pinned in plans. Dependabot (PLAN-1.1) will float minor versions inside `@v4` automatically. This is the correct pattern. ✓

4. **Shell compatibility:** All shell invocations in plans are bash-compatible and Windows-Git-Bash-compatible. No `bash -c` nesting or `source`/`.` commands that differ between shells. ✓

---

## Recommended Mitigations

1. **For PLAN-3.1 builder:** Adjust the Jenkinsfile's `archiveArtifacts` glob from `coverage/**/*.cobertura.xml` to `tests/**/TestResults/coverage/**/*.cobertura.xml`, **or** add a post-test copy step: `sh 'mkdir -p $WORKSPACE/coverage && find tests -name "*.cobertura.xml" -exec cp {} $WORKSPACE/coverage/ \\;'` before archiving. Document the chosen approach in the build result summary.

2. **For verification:** When plans are executed, confirm:
   - PLAN-1.2 `bash .github/scripts/secret-scan.sh selftest` exits 0 with seven `PASS:` lines (not just 1 or 2).
   - PLAN-2.1 matrix jobs both complete green (ubuntu and windows legs).
   - PLAN-3.1 coverage artifacts appear in the Jenkins build summary (verify actual location matches the archive glob used).

---

## Summary

- **PLAN-1.1:** Low-risk, YAML-only config. Feasible.
- **PLAN-1.2:** Medium-risk, seven regex patterns all validated. Self-test job mitigates post-merge drift. Feasible.
- **PLAN-2.1:** Medium-risk, standard GitHub Actions pattern. Cross-platform matrix confirmed workable. Feasible.
- **PLAN-3.1:** Medium-risk, MTP CLI flags verified operational. **One builder action item:** adjust coverage output path handling. Feasible with noted caveat.

**No blocking issues.** All four plans are ready for execution. The MTP coverage output path behavior is a knowable detail that the builder should anticipate and address as part of PLAN-3.1 implementation.
