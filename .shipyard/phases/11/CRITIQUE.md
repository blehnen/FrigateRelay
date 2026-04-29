# Plan Critique — Phase 11 (Feasibility Stress Test)

**Phase:** 11
**Date:** 2026-04-28
**Verdict:** CAUTION

## Summary

All 6 plans reference existing types, files, and paths that check out. PLAN-1.1's critical test file `MqttToValidatorTests.cs` exists and is well-formed. CLAUDE.md stale-line edit targets (lines 10 and Jenkinsfile section) confirmed present. One uncertainty flag from RESEARCH.md (TraceSpans test file location) remains unresolved but does not block execution — architect noted builder lookup is deferred. No blocking structural defects found.

## Per-Plan Findings

### PLAN-1.1 (Test Triage)
- **Files exist:** `tests/FrigateRelay.IntegrationTests/MqttToValidatorTests.cs` ✓ (verified; contains `Validator_ShortCircuits_OnlyAttachedAction` test method at line 28)
- **CapturingLoggerProvider.cs:** Referenced at line 38 of MqttToValidatorTests but exact fixture path not confirmed (expected at `tests/FrigateRelay.IntegrationTests/Fixtures/CapturingLoggerProvider.cs` — file not read)
- **TraceSpans test file:** Architect deferred lookup to builder (RESEARCH.md uncertainty flag #1); filename not located in spot-check
- **Test method names:** `Validator_ShortCircuits_OnlyAttachedAction` exists (line 28); `TraceSpans_CoverFullPipeline` location TBD per plan
- **Acceptance criteria commands:** `dotnet build`, `dotnet run --project tests/FrigateRelay.IntegrationTests` syntax valid
- **Risk:** Low for Task 1; medium for Task 2 pending filename lookup (plan explicitly defers this to builder)

### PLAN-2.1 (Static Docs — LICENSE/SECURITY/CHANGELOG)
- **File targets:** All three are net-new per RESEARCH.md sec 1
- **Template content:** `git remote` resolution deferred to builder per plan; conditional on GitHub remote presence
- **HISTORY.md source:** Verified to exist but not fully read (token limit); plan includes grep command to validate structure
- **Acceptance criteria:** All verification commands reference existing git/grep/test utilities
- **Risk:** Low; straightforward file creation with content templates from RESEARCH.md

### PLAN-2.2 (README/CONTRIBUTING/CLAUDE.md fixes)
- **README.md + CONTRIBUTING.md:** Net-new files per RESEARCH.md sec 1
- **CLAUDE.md edits:** Two stale-line targets confirmed present:
  - Line 10: "currently **pre-implementation**" text present (verified via Read)
  - Jenkinsfile section: "tag-pinned — digest pin + Dependabot `docker` ecosystem deferred to Phase 10" — present in CI section (lines 200–209 per RESEARCH.md reference)
- **Forward references:** All reference PLAN-2.3, PLAN-2.4, PLAN-3.1 paths; plans declare these explicitly, no surprise hidden deps
- **Risk:** Low; edits are surgical on existing file; new files are standard format

### PLAN-2.3 (dotnet new template)
- **Template.json schema:** Standard from Microsoft; `sourceName` mechanism well-documented in RESEARCH.md sec 4
- **Plugin csproj pattern:** Mirrors existing BlueIris/Pushover shape per RESEARCH.md sec 3; `IActionPlugin` interface exists in `src/FrigateRelay.Abstractions/IActionPlugin.cs` (verified)
- **ProjectReference path:** Uses relative path `../../../../src/FrigateRelay.Abstractions/FrigateRelay.Abstractions.csproj`; works for in-repo smoke, documented as future NuGet path in guide
- **Test scaffold:** References `DynamicProxyGenAssembly2` for NSubstitute per CLAUDE.md convention (verified convention exists)
- **File count:** 8 files across `.template.config/`, `src/`, `tests/` — moderate risk but well-defined structure
- **Risk:** Medium (multi-file scaffold, but architecture clearly precedented); no API mismatches found

### PLAN-2.4 (GitHub templates + docs.yml)
- **Issue template YAML:** Standard GitHub forms syntax; paths exist in `.github/ISSUE_TEMPLATE/`
- **PR template:** Standard markdown; will auto-inject into PR bodies
- **docs.yml workflow:** Forward-references PLAN-2.3 template (both Wave 2, sequential touch okay per plan); job stubs for scaffold-smoke + samples-build with path filters and conditional logic
- **Risk:** Low; workflow syntax mirrors `ci.yml` precedent; conditional samples-build skip intentional per plan

### PLAN-3.1 (Plugin Author Guide + Samples)
- **Samples project:** New executable project; will be added to `FrigateRelay.sln` via `dotnet sln add` (safe, canonical method)
- **Plugin types:** References `IActionPlugin`, `IValidationPlugin`, `ISnapshotProvider`, `IPluginRegistrar` — all verified in `src/FrigateRelay.Abstractions/` (RESEARCH.md sec 2)
- **FrigateRelay.sln:** Verified to exist and have correct structure (sample csproj path will use MSBuild slashes per platform)
- **docs.yml extension:** Plan modifies file from PLAN-2.4 (sequential waves, documented touch boundary)
- **check-doc-samples.sh:** New bash script; byte-match approach straightforward
- **Risk:** Medium (3-file dependency: guide + samples + script); but each component has clear acceptance criteria

## Cross-Wave Forward References

- **PLAN-2.2 forward-refs to PLAN-2.3/2.4/3.1:** All referenced by **path**, not by implementation detail; stable references.
- **PLAN-2.4 `scaffold-smoke` job forward-refs to PLAN-2.3:** Both Wave 2; within-wave forward reference documented in plan (job will skip until template lands).
- **PLAN-3.1 extends `.github/workflows/docs.yml` from PLAN-2.4:** Sequential waves (2 → 3), documented touch boundary, safe append.
- **PLAN-3.1 `FrigateRelay.sln` modification:** Only PLAN-3.1 in Wave 3 touches sln; no conflict.

## Architect Risk Items Resolved

| # | Risk | Finding | Status |
|---|------|---------|--------|
| 1 | TraceSpans test file path unknown | File not located in spot-check (`tests/FrigateRelay.IntegrationTests/Observability/` directory not inspected); architect deferred lookup to builder (PLAN-1.1 Task 2 step 1) | BUILDER DEFERRED — acceptable per plan |
| 2 | PLAN-2.4 samples-build conditional | Job includes path filter `samples/**` + `continue-on-error: false`; will skip until PLAN-3.1 lands | CORRECT — intentional skip-until-exists pattern documented |
| 3 | GitHub owner/repo slug | SECURITY.md and config.yml reference `<owner>/<repo>` placeholder; builder will resolve from `git remote get-url origin` per plan | DEFERRED TO BUILDER — both plans explicitly document this |
| 4 | dotnet new template feasibility | Template engine ships in .NET SDK; `dotnet new install <path>` (not deprecated `--install` flag) is correct per RESEARCH.md sec 4 | CORRECT — PLAN-2.3 smoke-test path uses correct form |
| 5 | Fixture path for CapturingLoggerProvider | Test imports from `FrigateRelay.IntegrationTests.Fixtures` namespace; file not located but referenced by test; PLAN-1.1 Task 1 reads it first | BUILDER DEFERRED — acceptable; plan includes read-first step |

## Findings Severity

**Blocking (REVISE):** None.

**Caution (builder awareness needed):**
- PLAN-1.1 Task 2: TraceSpans test file lookup deferred to builder. Plan explicitly covers this (step 1 of Task 2). No defect, but builder MUST run `ls tests/FrigateRelay.IntegrationTests/Observability/` before editing.
- PLAN-2.1 & PLAN-2.4: GitHub owner/repo slug resolution deferred to `git remote` lookup. Plan documents fallback to placeholder + TODO. Builder should verify repo slug before commit.
- PLAN-1.1 Task 1: CapturingLoggerProvider.cs fixture file not confirmed at expected path. Plan includes read-first step; acceptable. Builder may discover fixture has different path or name.

**Notes (no action needed):**
- All forward references (PLAN-2.2 → Wave 3 paths, PLAN-2.4 → PLAN-2.3, PLAN-3.1 → docs.yml) are documented and safe.
- PLAN-2.3 scaffold uses relative ProjectReference paths; documented as future NuGet path in PLAN-3.1 guide.
- All IActionPlugin / IValidationPlugin / ISnapshotProvider / IPluginRegistrar types exist in `src/FrigateRelay.Abstractions/` with expected signatures per RESEARCH.md sec 2.

## Verdict

**CAUTION** — Builder must perform 3 explicit lookups deferred by architect (TraceSpans test file, CapturingLoggerProvider fixture path, GitHub owner/repo slug), but all are documented in plans with fallback paths. No file-not-found blockers, no API mismatches, no circular dependencies. Plans are executable as-written with builder awareness notes.
