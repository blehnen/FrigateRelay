# SUMMARY-1.1 — Dependabot v2 Config (nuget + github-actions, weekly)

## Status

COMPLETE

## Tasks Completed

### Task 1 — Author `.github/dependabot.yml`

Created `.github/dependabot.yml` (and the `.github/` directory, which did not exist).
Committed as `dec0494` on branch `Initcheckin`.

## Files Modified

| File | Action |
|------|--------|
| `.github/dependabot.yml` | Created (53 lines) |

## Decisions Made

1. **FluentAssertions version pin** — Added `ignore:` block under the `nuget` ecosystem entry with `dependency-name: "FluentAssertions"` and `versions: [">= 7.0.0"]`. This prevents Dependabot from proposing any upgrade to 7.0.0 or higher. Rationale: FluentAssertions 7+ switched to a commercial license; PROJECT.md's MIT constraint prohibits upgrading past 6.12.2 (Apache-2.0).

2. **Template source** — Used RESEARCH.md lines 104-144 verbatim as the base shape, then added the FluentAssertions `ignore:` block and header comment clarifying that the github-actions ecosystem will go idle until PLAN-1.2 / PLAN-2.1 create workflow files (expected behavior per plan).

3. **`yq` installation** — `yq` was not present on the builder host. Installed `yq v4.53.2` (mikefarah/yq) to `~/yq` to run the plan's verification commands. This is a tooling bootstrap only; no project files were affected.

## Issues Encountered

- `yq` not installed system-wide. Installed to home directory (`~/yq`) for verification only. No impact on the deliverable.
- `actionlint` not installed. Skipped per plan instructions ("skip if not installed -- not a gating step").
- Git warned about LF->CRLF line ending conversion. Normal on Windows-hosted WSL2 repos with `core.autocrlf=true`; no action required.

## Verification Results

All gating checks passed:

| Command | Output | Expected | Pass? |
|---------|--------|----------|-------|
| `yq eval '.version'` | `2` | `2` | YES |
| `yq eval '.updates | length'` | `2` | `2` | YES |
| `yq eval '.updates[].package-ecosystem'` | `nuget` / `github-actions` | both ecosystems | YES |
| `yq eval '.updates[0].groups | keys'` | `microsoft-extensions`, `mstest` | both groups | YES |
| `grep -i fluentassertions` | present under `ignore:` with `>= 7.0.0` | present | YES |
| `yq eval .` (full parse) | no error | no error | YES |
| `actionlint` | not installed | skip | SKIPPED |

## Commit

`dec0494` -- `shipyard(phase-2): add Dependabot v2 config (nuget + github-actions, weekly)`
