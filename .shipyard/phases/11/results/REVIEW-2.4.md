# REVIEW-2.4 — GitHub Templates + docs.yml

**Status**: COMPLETE  
**Reviewer**: reviewer-2-4  
**Commits**: bba935d (issue templates), e1a746a (PR template), e7ba81f (docs.yml)  
**Verdict**: PASS — 0 blockers, 2 minor observations

---

## Stage 1 — Correctness

### `.github/ISSUE_TEMPLATE/bug_report.yml` (bba935d)

| Check | Result |
|---|---|
| GitHub Forms schema: `name`, `description`, `labels`, `body` array | PASS |
| `body` includes `markdown`, `dropdown`, `textarea`, `input`, `checkboxes` types | PASS |
| Affected component `dropdown` (Host/FrigateMqtt/BlueIris/Pushover/CodeProjectAi/FrigateSnapshot/Build-CI/Docs/Other) | PASS |
| Expected/Actual/Repro `textarea` fields, all required | PASS |
| Environment `dropdown` (Docker/native Linux/native Windows) | PASS |
| .NET SDK version `input` (optional, placeholder "10.0.x") | PASS |
| Relevant logs `textarea` (optional, render:text) | PASS |
| Self-cert `checkboxes` (duplicate search + SECURITY.md confirmation) | PASS |
| YAML valid (`python3 yaml.safe_load`) | PASS |
| No secrets/IPs | PASS |
| **Minor**: No top-level `title:` field (pre-fill hint). GitHub Forms does not require it; plan's spec table listed it as schema field but builder omitted. Functional gap: none. | NOTE |

### `.github/ISSUE_TEMPLATE/feature_request.yml` (bba935d)

| Check | Result |
|---|---|
| GitHub Forms schema valid | PASS |
| `markdown` block references CONTRIBUTING.md and Non-Goals in PROJECT.md | PASS |
| Problem/Proposed solution `textarea` fields (required) | PASS |
| Alternatives considered (optional) | PASS |
| Self-cert `checkboxes` (Non-Goals read) | PASS |
| YAML valid | PASS |
| No secrets/IPs | PASS |

### `.github/ISSUE_TEMPLATE/config.yml` (bba935d)

| Check | Result |
|---|---|
| `blank_issues_enabled: false` | PASS |
| `contact_links` entry for security advisory | PASS |
| URL uses `<owner>/<repo>` placeholder with TODO comment | PASS — consistent with PLAN-2.1 pattern; orchestrator fix-up expected |
| Security advisory redirect to private advisory flow | PASS |
| YAML valid | PASS |

### `.github/pull_request_template.md` (e1a746a)

| Check | Result |
|---|---|
| `## Summary` section with placeholder comment | PASS |
| `## Type of change` checkboxes (bug fix/feature/build-CI-docs/refactor) | PASS |
| `## Checklist` section | PASS |
| `dotnet build FrigateRelay.sln -c Release` gate | PASS |
| `bash .github/scripts/run-tests.sh` gate | PASS |
| No `.Result`/`.Wait()` gate | PASS |
| No hard-coded IPs/secrets gate | PASS |
| `CHANGELOG.md` gate | PASS |
| Plugin author `docs/plugin-author-guide.md` + `dotnet new frigaterelay-plugin` gate | PASS |
| Phase commit convention gate | PASS |
| `## Notes for reviewer` section | PASS |
| No secrets/IPs | PASS |
| **Minor**: `run-tests.sh` referenced in template does not exist at `Initcheckin` branch tip; `run-tests.sh` is listed in `.github/scripts/` (confirmed via `ls`). Script exists — reference is valid. | PASS |

### `.github/workflows/docs.yml` (e7ba81f)

| Check | Result |
|---|---|
| NEW file — ci.yml NOT touched (verified `git show e7ba81f -- .github/workflows/ci.yml` empty) | PASS — D4 honored |
| `push: branches: [main]` + path filters (docs/**, samples/**, templates/**, docs.yml) | PASS |
| `pull_request:` trigger with same path filters, no `branches:` filter | PASS |
| `permissions: contents: read` only | PASS |
| `concurrency: group: docs-${{ github.ref }}, cancel-in-progress: true` | PASS |
| `DOTNET_CLI_TELEMETRY_OPTOUT`, `DOTNET_NOLOGO`, `NUGET_XMLDOC_MODE` env block | PASS |
| `scaffold-smoke` job: `if: hashFiles('templates/**/template.json') != ''` | PASS |
| `scaffold-smoke`: `actions/setup-dotnet@v4` with `global-json-file: global.json` | PASS |
| `scaffold-smoke`: `dotnet new install templates/FrigateRelay.Plugins.Template/` | PASS |
| `scaffold-smoke`: `dotnet new frigaterelay-plugin -n FrigateRelay.Plugins.SmokeScaffold` | PASS |
| `scaffold-smoke`: copies rendered output into in-repo tree for relative ProjectReference resolution | PASS |
| `scaffold-smoke`: builds + runs test project | PASS |
| `scaffold-smoke`: `Cleanup` step with `if: always()` — uninstalls template and removes scratch dirs | PASS |
| `samples-build` job: conditional via `detect` step + `if: steps.detect.outputs.exists == 'true'` | PASS |
| `samples-build`: no `hashFiles` guard on job itself (detect step is the gate) | PASS — matches plan spec |
| No `--coverage`, TRX, or artifact upload in docs.yml | PASS — split-CI invariant held |
| YAML valid (`python3 yaml.safe_load`) | PASS |
| Secret scan (`bash .github/scripts/secret-scan.sh scan`) | PASS |

---

## Stage 2 — Integration

| Check | Result |
|---|---|
| `docs.yml` mirrors `ci.yml` concurrency pattern (`group: docs-${{ github.ref }}`) | PASS |
| `docs.yml` mirrors `ci.yml` setup-dotnet@v4 + global-json-file | PASS |
| `docs.yml` uses `shell: bash` on steps with set -euo pipefail | PASS |
| `scaffold-smoke` skip guard: when PLAN-2.3 not landed, `hashFiles` returns '' → job skips, not fails | PASS |
| `samples-build` skip guard: when PLAN-3.1 not landed, detect outputs `exists=false` → build/run steps skip, not fails | PASS |
| `config.yml` `<owner>/<repo>` placeholder: consistent with PLAN-2.1 approach, orchestrator fix-up documented | PASS |
| PR template `run-tests.sh` reference: script exists at `.github/scripts/run-tests.sh` | PASS |
| Secret scan passes on all new files | PASS |

---

## Findings

**Blockers**: 0

**Minor observations** (no action required):

1. `bug_report.yml` and `feature_request.yml` have no top-level `title:` field (pre-fill suggestion shown in GitHub new-issue UI). GitHub Forms does not require it; the plan's spec section mentioned it in the schema description but did not list it as an acceptance criterion. Functional impact: none — the form works correctly without it.

2. `samples-build` job always runs (checkout + setup-dotnet) even when samples don't exist; only the build/run steps are guarded. This means 2 unnecessary steps execute on every docs-path commit until PLAN-3.1 lands. Not a correctness issue — just minor CI overhead. Acceptable as-is; if desired, a job-level `if: hashFiles('samples/**/*.csproj') != ''` could short-circuit it entirely (mirrors `scaffold-smoke` pattern).

---

## Verdict

**PASS** — all plan acceptance criteria met, split-CI invariant held, secret scan clean, YAML valid, D4 (new workflow, no ci.yml changes) honored. 2 minor non-blocking observations noted above.
