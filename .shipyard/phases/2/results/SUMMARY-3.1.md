---
plan: 3.1
wave: 3
date: 2026-04-24
builder: Claude Sonnet 4.6
commit: 3070ac6
status: complete
---

# SUMMARY-3.1 — Jenkinsfile (coverage pipeline, scripted)

## What was done

Created `/Jenkinsfile` at the repo root — a scripted Jenkins pipeline targeting
`mcr.microsoft.com/dotnet/sdk:10.0`. Scope is coverage-only (D1); no release,
no publish, no credentials.

## MTP coverage output path reality check

**Docker simulation run:** `mcr.microsoft.com/dotnet/sdk:10.0` against this repo.

**Finding:** The `--coverage-output <path>` flag **IS honored** by
`Microsoft.Testing.Extensions.CodeCoverage` on this SDK image. The file was
written exactly to the explicit path `/tmp/explicit.cobertura.xml` (11,609 bytes,
valid cobertura XML with `<coverage` root element).

MTP also writes a secondary copy to the assembly output TestResults subdirectory:
`tests/FrigateRelay.Host.Tests/bin/Release/net10.0/TestResults/coverage/host-tests/FrigateRelay.Host.Tests.cobertura.xml`

**CRITIQUE.md caveat status:** The caveat that `--coverage-output` may not be
honored does NOT apply to this SDK/runtime combination. The explicit path is honored.

**Archive glob used:** `coverage/**/*.cobertura.xml`

Targets workspace-relative paths:
- `coverage/abstractions-tests/FrigateRelay.Abstractions.Tests.cobertura.xml`
- `coverage/host-tests/FrigateRelay.Host.Tests.cobertura.xml`

No post-test copy step or TestResults glob needed.

## Decisions implemented

**OQ3 — Coverage plugin:** Modern `recordCoverage` is the default. A
commented-out `coberturaPublisher(...)` line immediately above provides a
discoverable fallback for legacy Cobertura plugin installs.

**OQ4 — NuGet cache:** `dotnet restore --packages .nuget-cache` — workspace-local,
no Docker volume mount, no external pre-provisioning. The Docker `agent` block
has no `args` override.

**Scope:** Coverage-only. No notifications, no publish/deploy, no parallel matrix,
no reportgenerator. Phase 10 handles release.

## Verification results

| Check | Result |
|-------|--------|
| File exists at repo root | PASS |
| `mcr.microsoft.com/dotnet/sdk:10.0` image | PASS |
| `docker {` agent block present | PASS |
| `dotnet run --project tests/` count = 2 | PASS (2) |
| `parser: 'COBERTURA'` present | PASS |
| `allowEmptyArchive: false` present | PASS |
| `coberturaPublisher` fallback comment present | PASS |
| `cleanWs()` present | PASS |
| `post {` block present | PASS |
| `dotnet test` not present | PASS (0 matches) |
| `XPlat Code Coverage` not present | PASS (0 matches) |
| `--collect` not present | PASS (0 matches) |
| `args '-v` not present | PASS (0 matches) |

Docker simulation outcome: **PASS** (7/7 tests green, cobertura XML written to explicit path).

## Files created

- `/Jenkinsfile` — 99 lines

## Commit

`3070ac6` — `shipyard(phase-2): add coverage Jenkinsfile (Wave 3, PLAN-3.1)`
