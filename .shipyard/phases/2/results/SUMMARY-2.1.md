---
plan: 2.1
wave: 2
date: 2026-04-24
builder: Claude Sonnet 4.6
---

# SUMMARY-2.1 — GitHub Actions CI (ci.yml)

## File created

`.github/workflows/ci.yml`

## Verification output

```
yq parse:            PARSE OK
on | keys:           - push / - pull_request
matrix.os:           [ubuntu-latest, windows-latest]
dotnet run matches:  2  (Abstractions.Tests, Host.Tests)
dotnet test:         NO dotnet test: OK
coverage flags:      NO coverage flags: OK
```

## Local simulation results

| Step | Result |
|------|--------|
| dotnet restore | All projects up-to-date |
| dotnet build -c Release --no-restore | 0 errors, 0 warnings |
| dotnet run Abstractions.Tests -c Release --no-build | 10 passed, 0 failed |
| dotnet run Host.Tests -c Release --no-build | 7 passed, 0 failed |

Total: 17 tests, 17 passed. Matches Wave-1 post-state.

## Decisions

**Matrix:** 2 legs (ubuntu-latest, windows-latest). fail-fast: false so both legs
always report, preserving cross-OS parity signal.

**shell: bash on test steps:** Specified on both dotnet run steps. Prompt spec
requires it for Windows path consistency; PLAN.md was silent so prompt wins.

**setup-dotnet cache:** OFF. Plan is silent; spec default is OFF. Restore cost low.

**Concurrency group:** ci-${{ github.ref }} / cancel-in-progress: true. Cancels
superseded runs on force-push or rapid commits.

**permissions:** contents: read at workflow level (principle of least privilege).

**timeout-minutes: 15:** Protects against hung dotnet processes; well above 3-min target.

**No dotnet test:** D2 enforced. Zero invocations in file.

**No coverage, no artifacts, no TRX:** D1 — Jenkins-side. Fast gate only.

## Issues

- yq absent on WSL2 host; installed to /tmp/yq v4.53.2 for verification only.
- Spec grep for 'coverage' matched D1 comment on first draft. Comment rephrased.
- act not available; local restore+build+run is the proxy for CI gate success.
