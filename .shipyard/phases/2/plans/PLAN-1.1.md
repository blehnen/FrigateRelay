---
phase: ci-skeleton
plan: 1.1
wave: 1
dependencies: []
must_haves:
  - Dependabot v2 config covering nuget + github-actions ecosystems weekly
  - NuGet update grouping for Microsoft.Extensions.* and MSTest/Microsoft.Testing.* families
  - Config parses and passes structural YAML checks
files_touched:
  - .github/dependabot.yml
tdd: false
risk: low
---

# PLAN-1.1 — Dependabot (nuget + github-actions, weekly)

## Context

Phase 2 ROADMAP calls for a Dependabot v2 config restricted to `nuget` and
`github-actions` ecosystems on a weekly cadence. Docker is intentionally
excluded — Phase 10 adds the Dockerfile and will register the `docker`
ecosystem in the same PR that introduces the image.

Decisions and constraints:

- **D5** (CONTEXT-2.md) — two ecosystems, weekly, Mondays. Keep noise low by
  grouping `Microsoft.Extensions.*` (released in lockstep) and
  `MSTest*` / `Microsoft.Testing.*` (single-vendor family).
- **Open Question 5 resolution (MSTest.Sdk)** — non-issue. The two test
  projects use `Sdk="Microsoft.NET.Sdk"` (not `Sdk="MSTest.Sdk"`). The
  `MSTest` 4.2.1 dependency is a regular `PackageReference`, which Dependabot
  updates normally. The known Dependabot gap around MSBuild SDKs in
  `global.json` (`dependabot-core#12824`) does not apply. Noted here for the
  record; no configuration change required.
- Pre-merge verification is structural only. Runtime proof that Dependabot
  opens PRs is an observation on the first push to `main` after this plan
  merges, not a gating test.

Parallel-safe with PLAN-1.2: disjoint files (`.github/dependabot.yml` vs
`.github/workflows/secret-scan.yml` + fixture + script).

Follow-ups (out of scope, flagged for orchestrator):

- Add `docker` ecosystem entry in Phase 10 alongside the Dockerfile.
- Consider `ignore:` lists if patch-version churn becomes noisy (no data yet).

## Dependencies

None. This is a Wave 1 foundation plan.

## Tasks

### Task 1 — Author `.github/dependabot.yml`

**Files:** `.github/dependabot.yml`
**Action:** Create the file
**Description:** Create `.github/dependabot.yml` with `version: 2` and two
`updates` entries: one for `package-ecosystem: "nuget"` at `directory: "/"`
on a weekly Monday schedule with two `groups` (the `microsoft-extensions`
pattern `Microsoft.Extensions.*` and the `mstest` patterns
`MSTest*` and `Microsoft.Testing.*`), and one for
`package-ecosystem: "github-actions"` at `directory: "/"` on the same weekly
Monday schedule. Include a header comment noting the D5 scope decision and
the MSTest.Sdk non-issue (per Open Question 5). Use the template in
RESEARCH.md lines 104–144 as the authoritative shape.

**Acceptance Criteria:**
- File exists at `.github/dependabot.yml`.
- `version: 2` is the first non-comment key.
- Exactly two entries in the `updates:` list.
- Both entries use `directory: "/"`, `interval: "weekly"`, `day: "monday"`.
- NuGet entry contains `groups.microsoft-extensions.patterns` matching
  `Microsoft.Extensions.*` and `groups.mstest.patterns` matching both
  `MSTest*` and `Microsoft.Testing.*`.

<task id="1" files=".github/dependabot.yml" tdd="false">
  <action>Create `.github/dependabot.yml` with `version: 2` and two `updates` entries (nuget, github-actions), both `directory: "/"` weekly on Monday. NuGet entry includes `groups.microsoft-extensions` (pattern `Microsoft.Extensions.*`) and `groups.mstest` (patterns `MSTest*`, `Microsoft.Testing.*`). Header comment cites D5 scope and the MSTest.Sdk non-issue from Open Question 5. Follow the RESEARCH.md template verbatim.</action>
  <verify>yq eval '.version' .github/dependabot.yml &amp;&amp; yq eval '.updates | length' .github/dependabot.yml &amp;&amp; yq eval '.updates[0].package-ecosystem, .updates[1].package-ecosystem' .github/dependabot.yml &amp;&amp; yq eval '.updates[0].groups | keys' .github/dependabot.yml</verify>
  <done>`yq` prints `2` for `.version`, `2` for `.updates | length`, the two ecosystem names `nuget` and `github-actions` (in either order), and the NuGet groups include both `microsoft-extensions` and `mstest`.</done>
</task>

## Verification

- Structural: commands in the task above (yq).
- Optional tool if present: `actionlint .github/dependabot.yml` (skip if
  `actionlint` is not installed on the builder host — not a gating step).
- Runtime (post-merge observation, NOT a gate): after this plan lands on
  `main`, the first Dependabot run should either open a PR (if updates
  exist) or post a log entry in the repository Insights → Dependency graph
  → Dependabot tab confirming the config was parsed. Record the observation
  in the plan's result summary; do not block on it.
