---
phase: 11-oss-polish
plan: 2.4
wave: 2
dependencies: [1.1]
must_haves:
  - .github/ISSUE_TEMPLATE/bug_report.yml + feature_request.yml + config.yml (issue chooser)
  - .github/pull_request_template.md
  - .github/workflows/docs.yml (NEW workflow per D4) — scaffold-smoke + samples-build job stubs
files_touched:
  - .github/ISSUE_TEMPLATE/bug_report.yml
  - .github/ISSUE_TEMPLATE/feature_request.yml
  - .github/ISSUE_TEMPLATE/config.yml
  - .github/pull_request_template.md
  - .github/workflows/docs.yml
tdd: false
risk: low
---

# Plan 2.4: GitHub issue/PR templates + docs.yml workflow

## Context

Three GitHub-meta deliverables that live entirely in `.github/`. File-disjoint with all other Wave 2 plans.

**Architect-discretion locked here:**

- **Issue chooser config (`config.yml`).** RESEARCH.md flagged this as architect-discretion. Decision: include it, with one external link entry pointing at SECURITY.md so the chooser actively redirects security reports out of the public-issue flow. This is a 6-line YAML and prevents the most common new-user mistake (filing a security advisory as a public bug).
- **`docs.yml` job structure (RESEARCH.md sec 7).** Two jobs:
  1. `scaffold-smoke` — installs the template (PLAN-2.3), renders to a scratch dir, copies the rendered output INTO the in-repo `src/`+`tests/` tree (so the relative `ProjectReference` resolves), builds + runs the test, then `git restore` to clean up. Hard-fail on any non-zero exit.
  2. `samples-build` — builds + runs the samples test project (PLAN-3.1 deliverable). Path-filtered so it only runs when `samples/**`, `docs/**`, `templates/**`, or `.github/workflows/docs.yml` change.

  At PLAN-2.4 build time the `samples/` project does NOT yet exist (lands in PLAN-3.1 / Wave 3). The `samples-build` job is added with a `continue-on-error: false` flag and a path filter that includes `samples/**` — when the path exists post-3.1, the job activates. Until then, it skips because the path filter excludes commits that don't touch those paths. This is intentional: PLAN-2.4 lays the CI rail; PLAN-3.1 lays the rolling stock.
- **`bug_report.yml` form schema.** Standard YAML form with: dropdown for plugin context, free-text repro steps, env (Docker / native / OS / .NET version), logs, expected vs actual.
- **`feature_request.yml` form schema.** Lighter: free-text problem, free-text proposed solution, alternatives considered.
- **`pull_request_template.md` checklist.** Mirrors the CONTRIBUTING.md PR checklist (PLAN-2.2 Task 2) so the auto-injected PR body has the build/test/changelog/secrets gates pre-rendered.

## Dependencies

- **Wave 1 gate:** PLAN-1.1 must complete (CONTEXT-11 D7).
- **Forward reference to PLAN-2.3:** the `scaffold-smoke` job in `docs.yml` requires `templates/FrigateRelay.Plugins.Template/` to exist. Both plans are in Wave 2 — within the same wave, plans are file-disjoint and can build in any order, BUT the docs.yml `scaffold-smoke` job will fail on first run until the template lands. Wave 2 closeout requires both plans complete before any docs.yml run is expected to pass; this is a wave-level invariant, not a per-plan dependency.

## Tasks

### Task 1: Issue templates (bug_report + feature_request + config.yml)

**Files:**
- `.github/ISSUE_TEMPLATE/bug_report.yml` (create)
- `.github/ISSUE_TEMPLATE/feature_request.yml` (create)
- `.github/ISSUE_TEMPLATE/config.yml` (create)

**Action:** create

**Description:**

**`bug_report.yml`** — GitHub issue forms YAML schema (https://docs.github.com/en/communities/using-templates-to-encourage-useful-issues-and-pull-requests/syntax-for-issue-forms). Fields:

- `name`: "Bug report"
- `description`: "Report a defect or unexpected behavior"
- `labels`: `["bug"]`
- `body` array of inputs:
  - `markdown` block with a "Before filing" reminder to scan SECURITY.md for vulnerability reports.
  - `dropdown` for affected component: Host / FrigateMqtt source / BlueIris plugin / Pushover plugin / CodeProjectAi plugin / FrigateSnapshot plugin / Build/CI / Docs / Other.
  - `textarea` "Expected behavior" (required).
  - `textarea` "Actual behavior" (required).
  - `textarea` "Reproduction steps" (required).
  - `dropdown` "Environment": Docker / native (Linux) / native (Windows).
  - `input` ".NET SDK version" (placeholder: "10.0.x").
  - `textarea` "Relevant logs" (no required; optional).
  - `checkboxes` self-cert: "I have searched existing issues" / "I have read SECURITY.md and confirmed this is not a security vulnerability".

**`feature_request.yml`** — same schema, lighter:

- `name`: "Feature request"
- `description`: "Suggest a new capability or improvement"
- `labels`: `["enhancement"]`
- `body`:
  - `markdown` block referencing CONTRIBUTING.md and noting that v1 has explicit Non-Goals (link to PROJECT.md non-goals).
  - `textarea` "Problem you're trying to solve" (required).
  - `textarea` "Proposed solution" (required).
  - `textarea` "Alternatives considered" (optional).
  - `checkboxes`: "I have read PROJECT.md Non-Goals and this isn't excluded".

**`config.yml`** — issue chooser config:

```yaml
blank_issues_enabled: false
contact_links:
  - name: Security vulnerability
    url: https://github.com/<owner>/<repo>/security/advisories/new
    about: Report security issues privately via GitHub's security advisory flow. Do not file a public issue.
```

Owner/repo placeholder rule mirrors PLAN-2.1 Task 2: builder runs `git remote get-url origin` to resolve `<owner>/<repo>`, OR leaves the placeholder with a TODO comment if no GitHub remote is configured.

**Acceptance Criteria:**
- All three files exist at the listed paths.
- `python3 -c 'import yaml; yaml.safe_load(open("'"$f"'"))'` exits 0 for each file (where `$f` is each path).
- `grep -q 'name: "Bug report"' .github/ISSUE_TEMPLATE/bug_report.yml`
- `grep -q 'name: "Feature request"' .github/ISSUE_TEMPLATE/feature_request.yml`
- `grep -q 'blank_issues_enabled: false' .github/ISSUE_TEMPLATE/config.yml`
- `grep -q 'security/advisories/new' .github/ISSUE_TEMPLATE/config.yml`
- `grep -nE '192\.168\.|AppToken=' .github/ISSUE_TEMPLATE/` returns zero matches.

### Task 2: Pull request template

**Files:**
- `.github/pull_request_template.md` (create)

**Action:** create

**Description:**

GitHub auto-prepends `pull_request_template.md` to the PR body. Mirror the CONTRIBUTING.md PR checklist (PLAN-2.2 Task 2 item #4) so authors have the gates inline:

```markdown
## Summary

<!-- One paragraph: what changes and why. Link to relevant issue if applicable. -->

## Type of change

- [ ] Bug fix
- [ ] New feature / enhancement
- [ ] Build / CI / docs
- [ ] Refactor / chore

## Checklist

- [ ] Build is green on Linux: `dotnet build FrigateRelay.sln -c Release`
- [ ] Tests pass: `bash .github/scripts/run-tests.sh`
- [ ] No new `.Result` / `.Wait()` calls in source
- [ ] No hard-coded IPs/hostnames or secrets in committed files
- [ ] `CHANGELOG.md` updated under `## [Unreleased]` if user-visible
- [ ] Plugin author? Followed `docs/plugin-author-guide.md` and used the scaffold (`dotnet new frigaterelay-plugin`)
- [ ] Phase commit? Message follows `shipyard(phase-N): ...` convention

## Notes for reviewer

<!-- Risk areas, manual tests run, anything you want extra eyes on. -->
```

**Acceptance Criteria:**
- `test -f .github/pull_request_template.md`
- `grep -q '## Checklist' .github/pull_request_template.md`
- `grep -q 'run-tests.sh' .github/pull_request_template.md`
- `grep -q 'CHANGELOG.md' .github/pull_request_template.md`
- `grep -q 'dotnet new frigaterelay-plugin' .github/pull_request_template.md`
- `grep -nE '192\.168\.|AppToken=' .github/pull_request_template.md` returns zero matches.

### Task 3: docs.yml workflow — scaffold-smoke + samples-build (stub)

**Files:**
- `.github/workflows/docs.yml` (create)

**Action:** create

**Description:**

Per CONTEXT-11 D4: NEW workflow file (do NOT extend `ci.yml`). Per RESEARCH.md sec 7: mirror `ci.yml`'s concurrency / setup-dotnet / env-block patterns; add path filters to keep this workflow off pure source-only commits.

```yaml
name: Docs

on:
  push:
    branches: [main]
    paths:
      - 'docs/**'
      - 'samples/**'
      - 'templates/**'
      - '.github/workflows/docs.yml'
  pull_request:
    paths:
      - 'docs/**'
      - 'samples/**'
      - 'templates/**'
      - '.github/workflows/docs.yml'

permissions:
  contents: read

concurrency:
  group: docs-${{ github.ref }}
  cancel-in-progress: true

env:
  DOTNET_CLI_TELEMETRY_OPTOUT: 1
  DOTNET_NOLOGO: 1
  NUGET_XMLDOC_MODE: skip

jobs:
  scaffold-smoke:
    name: Scaffold smoke (dotnet new frigaterelay-plugin)
    runs-on: ubuntu-latest
    timeout-minutes: 10
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json
      - name: Install plugin template
        run: dotnet new install templates/FrigateRelay.Plugins.Template/
      - name: Render scaffold to scratch dir
        run: |
          set -euo pipefail
          mkdir -p /tmp/smoke-render
          dotnet new frigaterelay-plugin -n FrigateRelay.Plugins.SmokeScaffold -o /tmp/smoke-render
      - name: Copy scaffold into in-repo tree (for relative ProjectReference resolution)
        shell: bash
        run: |
          set -euo pipefail
          cp -r /tmp/smoke-render/src/FrigateRelay.Plugins.SmokeScaffold src/
          cp -r /tmp/smoke-render/tests/FrigateRelay.Plugins.SmokeScaffold.Tests tests/
      - name: Build scaffolded plugin + tests
        run: |
          dotnet build src/FrigateRelay.Plugins.SmokeScaffold/FrigateRelay.Plugins.SmokeScaffold.csproj -c Release
          dotnet build tests/FrigateRelay.Plugins.SmokeScaffold.Tests/FrigateRelay.Plugins.SmokeScaffold.Tests.csproj -c Release
      - name: Run scaffolded test
        run: dotnet run --project tests/FrigateRelay.Plugins.SmokeScaffold.Tests -c Release --no-build
      - name: Cleanup (ensure scratch dirs do not pollute downstream jobs)
        if: always()
        run: |
          dotnet new uninstall templates/FrigateRelay.Plugins.Template/ || true
          rm -rf /tmp/smoke-render src/FrigateRelay.Plugins.SmokeScaffold tests/FrigateRelay.Plugins.SmokeScaffold.Tests

  samples-build:
    name: Build + test samples project
    runs-on: ubuntu-latest
    timeout-minutes: 10
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json
      - name: Detect samples project
        id: detect
        shell: bash
        run: |
          if [ -d samples/FrigateRelay.Samples.PluginGuide ]; then
            echo "exists=true" >> "$GITHUB_OUTPUT"
          else
            echo "exists=false" >> "$GITHUB_OUTPUT"
            echo "samples/FrigateRelay.Samples.PluginGuide does not exist yet (PLAN-3.1 not landed)."
          fi
      - name: Build samples project
        if: steps.detect.outputs.exists == 'true'
        run: dotnet build samples/FrigateRelay.Samples.PluginGuide -c Release
      - name: Run samples tests
        if: steps.detect.outputs.exists == 'true'
        shell: bash
        run: |
          # Samples project ships a tiny self-test entry point (PLAN-3.1).
          # If a separate test project lands, swap to dotnet run --project tests/...Samples.Tests.
          dotnet run --project samples/FrigateRelay.Samples.PluginGuide -c Release --no-build
```

**Constraints:**
- Per CLAUDE.md "Split CI architecture": NO coverage flags here. NO TRX upload. NO artifact upload. This workflow is doc-rot detection only.
- `actions/setup-dotnet@v4` with `global-json-file: global.json` (matches `ci.yml`).
- Do NOT add SHA-pinning here (ID-24 is tracked separately and applies repo-wide; bundling that work into PLAN-2.4 is scope creep).

**Why the `samples-build` step is conditional.** Wave 2's PLAN-3.1 dependency is Wave 3 → at the moment this workflow lands, `samples/` does not exist. The `if: steps.detect.outputs.exists == 'true'` guard makes the workflow a no-op until PLAN-3.1 ships, then activates. This avoids a "first commit lands red" situation.

**Acceptance Criteria:**
- `test -f .github/workflows/docs.yml`
- `python3 -c 'import yaml; yaml.safe_load(open(".github/workflows/docs.yml"))'` exits 0 (valid YAML).
- `grep -q 'name: Docs' .github/workflows/docs.yml`
- `grep -q 'concurrency:' .github/workflows/docs.yml && grep -q 'group: docs-' .github/workflows/docs.yml`
- `grep -q 'global-json-file: global.json' .github/workflows/docs.yml`
- `grep -q 'dotnet new install templates/FrigateRelay.Plugins.Template' .github/workflows/docs.yml`
- `grep -q 'dotnet new frigaterelay-plugin' .github/workflows/docs.yml`
- `grep -nE 'TRX|coverage|--coverage' .github/workflows/docs.yml` returns zero matches (no coverage in docs workflow per CLAUDE.md split CI).

## Verification

Run from repo root:

```bash
# 0. All target files exist
test -f .github/ISSUE_TEMPLATE/bug_report.yml
test -f .github/ISSUE_TEMPLATE/feature_request.yml
test -f .github/ISSUE_TEMPLATE/config.yml
test -f .github/pull_request_template.md
test -f .github/workflows/docs.yml

# 1. YAML validity
for f in .github/ISSUE_TEMPLATE/*.yml .github/workflows/docs.yml; do
  python3 -c "import yaml; yaml.safe_load(open('$f'))" || { echo "Invalid YAML: $f"; exit 1; }
done

# 2. PR template + docs workflow consistency
grep -q 'run-tests.sh' .github/pull_request_template.md
grep -q 'dotnet new frigaterelay-plugin' .github/pull_request_template.md
grep -q 'global-json-file: global.json' .github/workflows/docs.yml

# 3. Split-CI invariant — no coverage in docs.yml
grep -nE 'TRX|coverage' .github/workflows/docs.yml && exit 1 || true

# 4. Path filter sanity
grep -q "templates/\*\*" .github/workflows/docs.yml
grep -q "samples/\*\*" .github/workflows/docs.yml
grep -q "docs/\*\*" .github/workflows/docs.yml

# 5. Secret + IP scan
grep -nE '192\.168\.|10\.[0-9]+\.[0-9]+\.[0-9]+\.[0-9]|AppToken=[A-Za-z0-9]{20,}' .github/ISSUE_TEMPLATE/ \
  .github/pull_request_template.md .github/workflows/docs.yml && exit 1 || true

# 6. Solution still builds (no source changes)
dotnet build FrigateRelay.sln -c Release
```
