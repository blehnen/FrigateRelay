---
phase: foundation-abstractions
plan: 1.1
wave: 1
dependencies: []
must_haves:
  - Repo root tooling files created and self-consistent
  - global.json pins 10.0.203 with rollForward=latestFeature (D4, RESEARCH)
  - Directory.Build.props enforces net10.0, nullable, warnings-as-errors, latest C#
  - Empty FrigateRelay.sln at repo root buildable with zero warnings
  - git grep ServicePointManager returns zero matches
files_touched:
  - .editorconfig
  - .gitignore
  - global.json
  - Directory.Build.props
  - FrigateRelay.sln
tdd: false
risk: medium
---

# PLAN-1.1 — Repo Tooling and Empty Solution

## Context

This plan establishes the foundation tooling every subsequent project will inherit. Per CONTEXT-1 D4 and RESEARCH.md, `global.json` pins SDK `10.0.203` (bundled with 10.0.7 runtime, released 2026-04-21) using `rollForward: latestFeature` so contributors on 10.0.2xx bands build without friction.

Open-question resolutions baked into this plan:

- **Q1 — MSTest.Sdk project SDK vs PackageReference.** Decision: use **Approach B (PackageReference)** across the Phase 1 test projects. Rationale: Dependabot cannot update `msbuild-sdks` entries in `global.json` ([dependabot-core#12824](https://github.com/dependabot/dependabot-core/issues/12824)); since `PROJECT.md` mandates Dependabot coverage (Phase 2), PackageReference keeps the whole dependency graph under Dependabot's watch. Therefore `global.json` omits the `msbuild-sdks` block.
- **Q2 — `TreatWarningsAsErrors` scope.** Decision: apply **globally** in `Directory.Build.props` including test projects. Rationale: architecture invariants in `CLAUDE.md` state warnings-as-errors is non-negotiable; test code is production-shipped behavior in this repo. If a specific test project surfaces noisy analyzer warnings, the remedy is a targeted `<NoWarn>` list inside that `.csproj` — not a blanket opt-out. No override is added in Phase 1; verification will catch it if needed.

No source code is written in this plan — only tooling files.

## Dependencies

None. This is Wave 1, parallel-safe with PLAN-1.2 (tooling only — disjoint files).

## Tasks

<task id="1" files=".editorconfig, .gitignore" tdd="false">
  <action>Create a minimal .editorconfig at repo root enforcing UTF-8, LF line endings for *.cs/*.csproj/*.props/*.json/*.md, 4-space indent for C#, 2-space indent for JSON/YAML, final newline, trim trailing whitespace, and dotnet_diagnostic.IDE0005.severity=warning to drive unused-using cleanup. Create a .gitignore covering the standard .NET Visual Studio set: bin/, obj/, .vs/, *.user, TestResults/, *.trx, *.coverage, coverage.cobertura.xml, publish/, *.nupkg, .idea/, appsettings.Local.json. Include a top-of-file comment referencing https://github.com/github/gitignore/blob/main/VisualStudio.gitignore as the upstream source.</action>
  <verify>test -f /mnt/f/git/FrigateRelay/.editorconfig && test -f /mnt/f/git/FrigateRelay/.gitignore && grep -q "appsettings.Local.json" /mnt/f/git/FrigateRelay/.gitignore && grep -q "charset = utf-8" /mnt/f/git/FrigateRelay/.editorconfig</verify>
  <done>Both files exist. .gitignore excludes bin/, obj/, TestResults/, coverage artifacts, and appsettings.Local.json. .editorconfig sets UTF-8, LF, 4-space C# indent.</done>
</task>

<task id="2" files="global.json, Directory.Build.props" tdd="false">
  <action>Create global.json at repo root with exactly this JSON (no msbuild-sdks block per Q1 resolution): {"sdk":{"version":"10.0.203","rollForward":"latestFeature"}}. Create Directory.Build.props at repo root with a single PropertyGroup setting TargetFramework=net10.0, Nullable=enable, TreatWarningsAsErrors=true, LangVersion=latest, ImplicitUsings=enable, plus EnforceCodeStyleInBuild=true and AnalysisLevel=latest-recommended. Include an XML comment stating "Warnings-as-errors applies globally including tests per CLAUDE.md invariant; per-project NoWarn overrides are acceptable, blanket false is not."</action>
  <verify>cat /mnt/f/git/FrigateRelay/global.json | grep -q '"10.0.203"' && cat /mnt/f/git/FrigateRelay/global.json | grep -q '"latestFeature"' && grep -q "TreatWarningsAsErrors>true" /mnt/f/git/FrigateRelay/Directory.Build.props && grep -q "LangVersion>latest" /mnt/f/git/FrigateRelay/Directory.Build.props</verify>
  <done>global.json contains 10.0.203 and rollForward=latestFeature and no msbuild-sdks block. Directory.Build.props contains the five required properties plus EnforceCodeStyleInBuild and AnalysisLevel.</done>
</task>

<task id="3" files="FrigateRelay.sln" tdd="false">
  <action>Create an empty solution file FrigateRelay.sln at repo root via `dotnet new sln --name FrigateRelay`. No projects are added in this plan (PLAN-2.1 and PLAN-3.1 add them and reference the solution via `dotnet sln add`). Confirm the solution is syntactically valid by running dotnet build against it — it should succeed with "Build succeeded" and zero warnings since there are no projects yet.</action>
  <verify>cd /mnt/f/git/FrigateRelay && dotnet build FrigateRelay.sln -c Release 2>&1 | tee /tmp/w1-build.log && ! grep -Eq "warning|error" /tmp/w1-build.log; echo "exit=$?"; git grep ServicePointManager || echo "GREP_CLEAN"</verify>
  <done>FrigateRelay.sln exists and is a valid sln. `dotnet build FrigateRelay.sln -c Release` exits 0 with zero warnings/errors. `git grep ServicePointManager` returns empty (prints "GREP_CLEAN").</done>
</task>

## Verification

Run from repo root `/mnt/f/git/FrigateRelay/`:

```bash
# Files present
test -f .editorconfig && test -f .gitignore && test -f global.json && test -f Directory.Build.props && test -f FrigateRelay.sln

# SDK pin correct
grep -q '"10.0.203"' global.json
grep -q '"latestFeature"' global.json
! grep -q 'msbuild-sdks' global.json   # Q1: omitted so Dependabot covers everything

# Build invariants present
grep -q 'TreatWarningsAsErrors>true' Directory.Build.props
grep -q 'Nullable>enable' Directory.Build.props
grep -q 'LangVersion>latest' Directory.Build.props

# Solution builds clean (no projects yet)
dotnet build FrigateRelay.sln -c Release

# ServicePointManager structurally impossible
git grep ServicePointManager ; [ $? -ne 0 ] && echo OK
```

Expected: all commands succeed, zero warnings, `git grep ServicePointManager` returns no matches (exits non-zero — OK printed).
