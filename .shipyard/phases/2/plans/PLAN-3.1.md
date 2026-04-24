---
phase: ci-skeleton
plan: 3.1
wave: 3
dependencies: [1.1, 1.2, 2.1]
must_haves:
  - Scripted Jenkinsfile targeting mcr.microsoft.com/dotnet/sdk:10.0
  - Per-test-project coverage run using MTP flags (--coverage --coverage-output-format cobertura --coverage-output <path>)
  - Cobertura XML archived as build artifact
  - post-always cleanup / archive ensuring artifact survives stage failure
  - No publish, no deploy, no credentials, no parallel matrix (Phase 10 / not-in-scope)
files_touched:
  - Jenkinsfile
tdd: false
risk: medium
---

# PLAN-3.1 — Jenkinsfile (coverage pipeline, scripted)

## Context

Per **D1**, Jenkins owns coverage. The Jenkinsfile mirrors DNWQ's style
(scripted, Docker agent) but intentionally drops DNWQ patterns that do not
apply here (see RESEARCH.md §DNWQ Patterns That DO NOT Apply — no
per-transport matrix, no VSTest `--collect`, no `-f net10.0`, no
`reportgenerator`, no Codecov upload, no `withCredentials`).

Coverage invocation per **D2** — MTP CLI flags:

```
dotnet run --project tests/<project> -c Release --no-build -- \
  --coverage \
  --coverage-output-format cobertura \
  --coverage-output coverage/<slug>/<project>.cobertura.xml
```

Two projects, two invocations. Output layout from RESEARCH.md §MTP Code
Coverage CLI:

```
coverage/
  abstractions-tests/FrigateRelay.Abstractions.Tests.cobertura.xml
  host-tests/FrigateRelay.Host.Tests.cobertura.xml
```

### Open Question resolutions

**OQ3 — Jenkins Coverage plugin vs legacy Cobertura plugin:** Use the
**modern Coverage plugin** (`recordCoverage`). Rationale: the legacy
Cobertura plugin is end-of-life and receives no updates; the modern
Coverage plugin is the documented replacement, supports Cobertura XML as a
parser (`parser: 'COBERTURA'`), and produces richer trend data. If the
target Jenkins instance does not have the Coverage plugin installed, the
correct remediation is to install the plugin, not to downgrade the
pipeline. Include a `// Fallback:` comment line above `recordCoverage`
showing the legacy `coberturaPublisher(...)` one-liner, commented out, so
an operator running an old Jenkins can swap without a source dive. This
keeps the modern path as the default and makes the fallback discoverable.

**OQ4 — NuGet cache topology:** Use `dotnet restore --packages
.nuget-cache` with the cache placed inside the workspace (relative path).
Rationale: (a) zero pre-provisioning — no `docker volume create` required
on the Jenkins host, making the Jenkinsfile self-contained and portable
across Jenkins agents; (b) Jenkins' standard workspace cleanup policy does
not delete `.nuget-cache` within a single build, so the cache survives
the restore→build→test chain; (c) for cross-build caching, Jenkins's
built-in workspace-reuse (same job name + same agent) gives acceptable
hit rates without Docker-volume coupling. Trade-off accepted: cold builds
on a fresh agent re-download packages (~30-60s on a small repo — well
within budget). The DNWQ pattern of a named Docker volume is valid but
assumes a dedicated, stable Jenkins host, which we do not guarantee here.

This also means the Docker `agent` block does NOT need
`args '-v nuget-cache:/root/.nuget/packages'`. Drop it. The RESEARCH.md
skeleton's `args` line is an example to replace.

### Shape (scripted)

- `pipeline { agent none; ... stages { stage('Build & Coverage') { agent
  { docker { image 'mcr.microsoft.com/dotnet/sdk:10.0' } } ... } } }`
- `environment` block: same three DNWQ vars as `ci.yml`
  (`DOTNET_CLI_TELEMETRY_OPTOUT=1`, `DOTNET_NOLOGO=1`,
  `NUGET_XMLDOC_MODE=skip`).
- `triggers { cron('0 2 * * 0') }` — weekly Sunday 02:00 in addition to
  push-triggered runs, matching DNWQ's cadence.
- Single stage with steps: `dotnet restore FrigateRelay.sln --packages
  .nuget-cache` → `dotnet build FrigateRelay.sln -c Release --no-restore`
  → `dotnet run ... --coverage --coverage-output-format cobertura
  --coverage-output coverage/abstractions-tests/...` (Abstractions) →
  same for Host (`coverage/host-tests/...`).
- `post { always { archiveArtifacts coverage/**/*.cobertura.xml,
  allowEmptyArchive: false; recordCoverage(...); } }` at stage level so
  artifacts survive a test-step failure.
- Pipeline-level `post { failure { echo ... } success { echo ... } }`
  for log visibility.

### Follow-ups flagged (out of scope)

- Pinning the SDK image to a specific patch (e.g., `sdk:10.0.107`) for
  reproducibility — defer until pinning becomes necessary; floating `10.0`
  tag auto-tracks patch releases today, which matches
  `rollForward: latestFeature` in `global.json`.
- Slack/Teams/email notification in `post { failure }` — no chat integration
  is configured in PROJECT.md scope.
- Merging coverage XMLs into a single report via `reportgenerator` —
  deferred; the Coverage plugin handles multi-file Cobertura natively.

## Dependencies

- **PLAN-1.1, PLAN-1.2, PLAN-2.1** — everything in Waves 1 and 2. The
  Jenkinsfile depends on the workflow conventions being settled
  (environment vars, action-version baseline, test-invocation pattern),
  not on specific file contents, but Wave-3 placement ensures coherent
  final Phase 2 shape.

## Tasks

### Task 1 — Author `Jenkinsfile`

**Files:** `Jenkinsfile`
**Action:** Create the file
**Description:** Create a scripted `Jenkinsfile` at the repo root. Header
comment states scope (coverage pipeline, not release/publish — that is
Phase 10) and cites D1/D2. Contents:

- `pipeline { agent none ... }` outer block.
- `environment` block: `DOTNET_CLI_TELEMETRY_OPTOUT = '1'`,
  `DOTNET_NOLOGO = '1'`, `NUGET_XMLDOC_MODE = 'skip'`.
- `triggers { cron('0 2 * * 0') }`.
- One `stage('Build & Coverage')`:
  - `agent { docker { image 'mcr.microsoft.com/dotnet/sdk:10.0' } }` with
    **no** `args` override (per OQ4 resolution).
  - Steps (each in a `sh '...'` block):
    1. `dotnet restore FrigateRelay.sln --packages .nuget-cache`
    2. `dotnet build FrigateRelay.sln -c Release --no-restore`
    3. `dotnet run --project tests/FrigateRelay.Abstractions.Tests -c Release --no-build -- --coverage --coverage-output-format cobertura --coverage-output coverage/abstractions-tests/FrigateRelay.Abstractions.Tests.cobertura.xml`
    4. `dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build -- --coverage --coverage-output-format cobertura --coverage-output coverage/host-tests/FrigateRelay.Host.Tests.cobertura.xml`
  - `post { always { archiveArtifacts artifacts: 'coverage/**/*.cobertura.xml', allowEmptyArchive: false; recordCoverage(tools: [[parser: 'COBERTURA', pattern: 'coverage/**/*.cobertura.xml']], id: 'cobertura', name: 'FrigateRelay Coverage') } }`.
  - Include a single commented-out line immediately above `recordCoverage`:
    `// Fallback for Jenkins instances on the legacy Cobertura plugin: coberturaPublisher(coberturaReportFile: 'coverage/**/*.cobertura.xml')`
- Pipeline-level `post { failure { echo 'Pipeline failed...' } success
  { echo 'Coverage pipeline completed.' } }`.

**Acceptance Criteria:**
- File exists at repo root as `Jenkinsfile`.
- Contains exactly one `stage('Build & Coverage')` — no other stages.
- Uses `mcr.microsoft.com/dotnet/sdk:10.0` image.
- The Docker `agent` block does NOT contain `args` (per OQ4).
- Two `dotnet run` invocations present, one per test project, each with
  the MTP coverage flag trio.
- `archiveArtifacts` with `allowEmptyArchive: false` present in stage-level
  `post { always }`.
- `recordCoverage` present with `parser: 'COBERTURA'`.
- A commented-out `coberturaPublisher(...)` fallback line is present.

<task id="1" files="Jenkinsfile" tdd="false">
  <action>Create scripted `Jenkinsfile` at repo root per the description above. One stage `Build &amp; Coverage` with Docker agent `mcr.microsoft.com/dotnet/sdk:10.0` (no `args`), restore with `--packages .nuget-cache`, build `-c Release --no-restore`, two `dotnet run` coverage invocations writing cobertura to `coverage/abstractions-tests/` and `coverage/host-tests/`. Stage-level `post { always }` archives `coverage/**/*.cobertura.xml` (`allowEmptyArchive: false`) and calls `recordCoverage` with `parser: 'COBERTURA'`. Include a commented `coberturaPublisher(...)` fallback line above `recordCoverage`. Pipeline-level `post { failure / success }` blocks for visibility.</action>
  <verify>test -f Jenkinsfile &amp;&amp; grep -q "image 'mcr.microsoft.com/dotnet/sdk:10.0'" Jenkinsfile &amp;&amp; grep -c 'dotnet run --project tests/' Jenkinsfile &amp;&amp; grep -q "parser: 'COBERTURA'" Jenkinsfile &amp;&amp; grep -q 'allowEmptyArchive: false' Jenkinsfile &amp;&amp; grep -q 'coberturaPublisher' Jenkinsfile &amp;&amp; ! grep -q "args '-v" Jenkinsfile</verify>
  <done>File exists. Image is `mcr.microsoft.com/dotnet/sdk:10.0`. `grep -c` reports `2` for `dotnet run --project tests/`. `recordCoverage` parser is `COBERTURA`. `allowEmptyArchive: false` present. Commented-out `coberturaPublisher` line present. No `args '-v` line (per OQ4).</done>
</task>

## Verification

- Structural: the grep chain in Task 1 proves the Jenkinsfile shape.
- Functional (container simulation, no Jenkins required): on a host with
  Docker, run:
  ```
  docker run --rm -v "$(pwd):/src" -w /src \
    -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 \
    mcr.microsoft.com/dotnet/sdk:10.0 bash -c "\
      dotnet restore FrigateRelay.sln --packages .nuget-cache && \
      dotnet build FrigateRelay.sln -c Release --no-restore && \
      dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build -- \
        --coverage --coverage-output-format cobertura \
        --coverage-output /tmp/host.cobertura.xml"
  ```
  Expected: exit 0, `/tmp/host.cobertura.xml` is non-empty and contains
  `<coverage` as its root (cobertura XML).
- Optional (if `docker` unavailable on the builder): `groovy -e` or
  Jenkins CLI `declarative-linter` is out of scope — pipeline linting
  requires a running Jenkins instance. Document "container simulation
  deferred" in the result summary if Docker is not installed; do not
  treat this as a gating failure.
- Full Jenkins run is a post-merge observation — the first push to a
  branch configured in the target Jenkins instance must complete green,
  archive both cobertura XML files, and display coverage in the build
  summary.
