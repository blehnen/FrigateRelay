---
phase: ci-skeleton
plan: 2.1
wave: 2
dependencies: [1.1, 1.2]
must_haves:
  - PR-gate workflow on push and pull_request
  - Matrix on ubuntu-latest and windows-latest
  - setup-dotnet via global-json-file (no hard-coded version)
  - Build once in Release, then dotnet run each test project with --no-build
  - No coverage, no TRX logger, no artifact uploads (Jenkins owns coverage per D1)
files_touched:
  - .github/workflows/ci.yml
tdd: false
risk: medium
---

# PLAN-2.1 — GitHub Actions CI (ci.yml, build + test matrix)

## Context

Fast PR gate per **D1** (mirror DNWQ split: GH Actions for PR gate, Jenkins
for coverage). **D2** — every test invocation uses `dotnet run` per project
because MSTest 4.2.1 + `EnableMSTestRunner=true` + `OutputType=Exe` on
.NET 10 SDK blocks `dotnet test` (`https://aka.ms/dotnet-test-mtp-error`).
**D3** — no shutdown smoke here; Phase 4 integration tests cover that.

### Open Question resolutions

**OQ1 — `actions/setup-dotnet@v4` vs `@v5`:** Use **`@v4`**. Rationale:
`v4` is the current stable major with documented `global-json-file` +
`rollForward: latestFeature` support (PR #224 merged 2021-09-13, carried
forward through v4). `v5` exists but Phase 1's `actions/checkout` guidance
in RESEARCH.md already argues `v4` over `v5` for stable-major parity, and
this plan keeps FrigateRelay on current-stable-major across all actions.
Dependabot (PLAN-1.1) will float the minor version inside `v4` automatically
and will propose the `v5` bump when `v5` becomes the stable major — that is
the correct time to move, not now.

**OQ2 — runner SDK edge case (no `10.0.x` at all):** `setup-dotnet@v4`
installs the SDK from the Microsoft distribution channel when the runner
image does not pre-bundle it, so the workflow will still succeed — install
is slower (~20s) but not fatal. If the `10.0` feature band is not yet
*published* at all (unlikely in steady state; only during the brief window
after `global.json` pins a new feature band that Microsoft has not yet
released), the action fails loudly with "Version 10.0.x not found". That
failure is the desired signal — do not add a fallback `dotnet-version:`
entry, because silently falling back to a different SDK would defeat the
purpose of pinning via `global.json`.

**OQ6 — `fetch-depth`:** Default (`1`, shallow). This workflow does not
use `git grep` or any history walk. Shallow checkout is ~2x faster on a
cold agent. Standardize on default across both jobs.

### Shape

- `on: [push, pull_request]` (default branches).
- `env` block (workflow-level) sets the three DNWQ-pattern variables —
  `DOTNET_CLI_TELEMETRY_OPTOUT=1`, `DOTNET_NOLOGO=1`, `NUGET_XMLDOC_MODE=skip`.
- One `build-and-test` job with `strategy.matrix.os:
  [ubuntu-latest, windows-latest]`, `runs-on: ${{ matrix.os }}`,
  `fail-fast: false` (both legs report).
- Steps: checkout → setup-dotnet (global-json) → `dotnet restore` →
  `dotnet build -c Release --no-restore` → two `dotnet run --project
  tests/... -c Release --no-build` invocations (Abstractions first, Host
  second — ordering does not matter functionally but deterministic
  ordering makes logs easier to read).
- Shell: default (`bash` on ubuntu, `pwsh` on windows). The test invocation
  lines are single-command shell invocations with no shell-specific syntax,
  so no `shell:` overrides are needed.
- No coverage flags, no TRX logger, no `actions/upload-artifact` (coverage
  is Jenkins-side per D1; ci.yml is a fast gate only).
- Target wall-clock on cold cache: ≤ 3 minutes per leg (CONTEXT-2.md).

### Follow-ups flagged (out of scope)

- Adding `fetch-depth: 0` or `actions/cache` for NuGet — speed tuning, not
  a correctness concern at current repo size.
- Uploading MTP test output logs on failure — only if flaky-test triage
  becomes a repeated need. Dead-weight today.
- Concurrency group to cancel superseded runs — small polish, not required.

## Dependencies

- **PLAN-1.1** — Dependabot monitors this workflow file's `uses:` versions.
- **PLAN-1.2** — secret-scan workflow pattern (same directory, same trigger
  shape) is the template for keeping workflow idioms consistent across the
  repo.

Both must land before this plan so conventions (action versions, job
naming, env blocks) are settled.

## Tasks

### Task 1 — Author `.github/workflows/ci.yml`

**Files:** `.github/workflows/ci.yml`
**Action:** Create the file
**Description:** Create a GitHub Actions workflow named `ci` triggered on
`push` and `pull_request`. Workflow-level `env` block with
`DOTNET_CLI_TELEMETRY_OPTOUT=1`, `DOTNET_NOLOGO=1`, `NUGET_XMLDOC_MODE=skip`.
Single job `build-and-test` with
`strategy: { fail-fast: false, matrix: { os: [ubuntu-latest, windows-latest] } }`
running on `${{ matrix.os }}`. Steps in order:
1. `actions/checkout@v4` (default `fetch-depth: 1`).
2. `actions/setup-dotnet@v4` with `global-json-file: global.json`.
3. `dotnet restore FrigateRelay.sln`.
4. `dotnet build FrigateRelay.sln -c Release --no-restore`.
5. `dotnet run --project tests/FrigateRelay.Abstractions.Tests -c Release --no-build`.
6. `dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build`.

Each step has a descriptive `name:`. No `shell:` overrides. No coverage,
no logger, no artifacts. A short header comment explains the D1/D2 split
(coverage is Jenkins-side; MTP requires `dotnet run`).

**Acceptance Criteria:**
- File exists at `.github/workflows/ci.yml`.
- `on:` includes both `push` and `pull_request`.
- Workflow-level `env:` contains exactly the three DNWQ environment
  variables with the specified values.
- `strategy.matrix.os` lists exactly `ubuntu-latest` and `windows-latest`.
- `strategy.fail-fast` is `false`.
- Step 2 uses `actions/setup-dotnet@v4` with `global-json-file: global.json`
  (no `dotnet-version:` key alongside).
- Steps 5 and 6 call `dotnet run` (NOT `dotnet test`) with `--no-build`.

<task id="1" files=".github/workflows/ci.yml" tdd="false">
  <action>Create `.github/workflows/ci.yml` per the description above: `name: ci`, `on: [push, pull_request]`, workflow-level `env` with the three DNWQ telemetry/nuget vars, one job `build-and-test` with `fail-fast: false` matrix on `[ubuntu-latest, windows-latest]`. Steps: checkout@v4 → setup-dotnet@v4 (global-json-file: global.json) → `dotnet restore FrigateRelay.sln` → `dotnet build FrigateRelay.sln -c Release --no-restore` → `dotnet run --project tests/FrigateRelay.Abstractions.Tests -c Release --no-build` → `dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build`. Header comment explains D1/D2.</action>
  <verify>yq eval '.on | keys' .github/workflows/ci.yml &amp;&amp; yq eval '.env' .github/workflows/ci.yml &amp;&amp; yq eval '.jobs."build-and-test".strategy.matrix.os' .github/workflows/ci.yml &amp;&amp; yq eval '.jobs."build-and-test".steps[1].uses, .jobs."build-and-test".steps[1].with."global-json-file"' .github/workflows/ci.yml &amp;&amp; grep -c 'dotnet run --project tests/' .github/workflows/ci.yml</verify>
  <done>`yq` confirms: `on` has both `push` and `pull_request`; `env` has the three vars set to `1`, `1`, `skip`; matrix `os` lists both `ubuntu-latest` and `windows-latest`; setup-dotnet step is `actions/setup-dotnet@v4` with `global-json-file: global.json`. `grep -c` reports at least `2` for `dotnet run --project tests/`.</done>
</task>

## Verification

- Primary structural: the yq/grep chain in Task 1.
- Optional local run (not gating — requires `act` installed, Docker
  running, and a runner image):
  `act -l` to list jobs, `act push -W .github/workflows/ci.yml --dry-run`
  to simulate without executing. Document in the result summary whether
  `act` was available.
- Full runtime verification requires pushing to GitHub — this is a
  post-merge observation. The first push must show both matrix legs
  complete green within roughly 3 minutes on cold cache.
- If either leg fails on the first real run, the failure is the signal D1
  wanted (build parity across ubuntu+windows). Do not mask failures with
  `continue-on-error`.
