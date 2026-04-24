# Review: Plan 3.1 (Phase 2)

## Verdict: PASS

Target commit: `3070ac6 shipyard(phase-2): add Jenkinsfile for coverage`. Reviewer: APPROVE, zero critical, one accepted limitation.

## Findings

### Critical
None.

### Minor (advisory / non-blocking)
- **Hard-coded test-project list** — `Jenkinsfile` names `FrigateRelay.Abstractions.Tests` and `FrigateRelay.Host.Tests` explicitly. Phase 3+ will need a Jenkinsfile edit per new test project. Same forward-integration note flagged on `ci.yml` (REVIEW-2.1). **Carry to Phase 3 architect**: decide whether to switch both `ci.yml` and `Jenkinsfile` to dynamic test-project discovery (`find tests/*.Tests -maxdepth 1 -type d`) before count grows past three.
- **`cleanWs()` ordering inside `post { always }`** is correct today (comes last, after `recordCoverage`), but the file does not warn a future editor "keep `cleanWs()` last." If someone reorders, coverage publish would run against a deleted workspace. A one-line comment would prevent regression. Low-cost; consider in a polish pass.

### Positive
- Scripted pipeline + Docker agent (`mcr.microsoft.com/dotnet/sdk:10.0`) matches DNWQ idiom and keeps the Jenkins master free of .NET installs.
- Weekly cron `0 2 * * 0` — explicit schedule rather than relying on manual triggers.
- `archiveArtifacts` with `fingerprint: true` and `allowEmptyArchive: false` — if coverage produces zero files, the build fails loudly. Correct discipline.
- Modern `recordCoverage(tools: [[parser: 'COBERTURA', ...]])` as primary publisher with commented `coberturaPublisher(...)` legacy fallback. Matches architect's Q3 resolution.
- Header comment references OQ3/OQ4 decisions — future readers find the "why" without needing CONTEXT-2.md.
- No `dotnet test`, no `--collect:"XPlat Code Coverage"`, no Docker `-v` volume for NuGet cache. Phase 2 scope discipline honoured.

## CRITIQUE.md feasibility caveat — resolution

CRITIQUE.md warned that `--coverage-output <path>` might not be honored and files might land in `TestResults/` subdirectories. Builder's Docker simulation against `mcr.microsoft.com/dotnet/sdk:10.0` produced an 11,609-byte cobertura XML written exactly to the explicit path. The WSL-host observation that prompted the caveat does not reproduce inside the SDK container. **Caveat resolved — does not apply.** The archive glob `coverage/**/*.cobertura.xml` is the correct target.

## Check results

| Command | Result |
|---|---|
| `grep -c 'dotnet run --project tests' Jenkinsfile` | 2 |
| `! grep -E 'dotnet test\b' Jenkinsfile` | empty |
| `! grep -E 'XPlat Code Coverage\|--collect' Jenkinsfile` | empty |
| `grep -E 'recordCoverage' Jenkinsfile` | present |
| `grep -E 'cleanWs\|post[[:space:]]*\{' Jenkinsfile` | present |
| `grep -E 'mcr\.microsoft\.com/dotnet/sdk:10\.0' Jenkinsfile` | present |
| `wc -l Jenkinsfile` | 99 lines — plausible size |
| Builder's Docker simulation | PASS (11,609-byte cobertura, 7/7 Host tests green) |
