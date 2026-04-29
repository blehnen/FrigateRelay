---
plan: 1.2
phase: ci-skeleton
wave: 1
date: 2026-04-24
builder: Claude Sonnet 4.6
---

# SUMMARY-1.2 — Secret-scan workflow + tripwire fixture + self-test

## What was done

### Task 1 — `.github/secret-scan-fixture.txt` (commit 3172d9f)

Created the tripwire fixture with a header comment (purpose, synthetic-values warning, do-not-replace instruction) and seven labelled lines tagged `# secret-scan:fixture`. Values from RESEARCH.md draft: sequential chars, `192.168.99.99`, `alg=none` JWT Bearer, `ghp_` 36-char PAT, `AKIAIOSFODNN7EXAMPLE00`.

Verification: `grep -c -E '^(AppToken=|UserKey=|...)' .github/secret-scan-fixture.txt` returned `7`.

### Task 2 — `.github/scripts/secret-scan.sh` (commit 01c051f)

Created executable bash script (`set -euo pipefail`, `chmod +x`) with parallel `LABELS[]`/`PATTERNS[]` arrays as the single authoritative pattern source. `scan` mode: `git grep -nE` with pathspec exclusions, exit 1 on any hit. `selftest` mode: `git grep -qE` per pattern against only the fixture, prints `PASS:`/`FAIL:`, exits 1 on any miss.

Verifications: syntax OK; `scan` exits 0 (after CLAUDE.md exclusion fix — see Issues); `selftest` exits 0, prints 7 `PASS:` lines.

### Task 3 — `.github/workflows/secret-scan.yml` (commit fea2d9a)

Created workflow with `on: push, pull_request`, `permissions: contents: read`, two parallel jobs on `ubuntu-latest`: `scan` and `tripwire-self-test`, each using `actions/checkout@v4` with `fetch-depth: 1`, each running the script with its mode arg.

Verification via `python3 yaml` (yq absent locally — see Issues): parses cleanly; `on` keys = push, pull_request; `jobs` keys = scan, tripwire-self-test; both first steps = `actions/checkout@v4`.

## Decisions

### Final pattern list (unchanged from RESEARCH.md)

| # | Label | ERE Pattern |
|---|-------|-------------|
| 1 | AppToken | `AppToken\s*=\s*[A-Za-z0-9]{20,}` |
| 2 | UserKey | `UserKey\s*=\s*[A-Za-z0-9]{20,}` |
| 3 | RFC-1918 IP | `192\.168\.[0-9]{1,3}\.[0-9]{1,3}` |
| 4 | Generic apiKey | `api[Kk]ey\s*[=:]\s*["']?[A-Za-z0-9_\-]{20,}` |
| 5 | Bearer token | `Bearer\s+[A-Za-z0-9._\-]{20,}` |
| 6 | GitHub PAT | `ghp_[A-Za-z0-9]{36}` |
| 7 | AWS Access Key | `AKIA[A-Z0-9]{16}` |

### Fixture-vs-scan exclusion strategy

`scan` mode excludes via `git grep` pathspecs:
1. `:!.shipyard` — planning docs discuss pattern shapes (per plan).
2. `:!CLAUDE.md` — dev-conventions doc that cites `192.168.0.58` as a forbidden-pattern example (same category as .shipyard; see Issues).
3. `:!.github/secret-scan-fixture.txt` — the tripwire fixture itself.

`selftest` mode targets only `.github/secret-scan-fixture.txt`.

`git grep` used (not raw `grep`) for Windows line-ending robustness.

## Issues

### Issue 1 — `CLAUDE.md` contains `192.168.0.58`

First scan run exited 1. `CLAUDE.md:95` matched the RFC-1918 pattern: the line reads "The legacy code had `http://192.168.0.58:5001` in a commented block; that pattern is forbidden here." — an architecture invariant note explaining why IPs are forbidden, not an actual committed IP. Added `:!CLAUDE.md` to the exclusion list (minimum correct suppression). Removing the IP from CLAUDE.md was rejected — the note is valuable to future contributors.

### Issue 2 — `yq` not installed locally

YAML validated via `python3 -c "import yaml; ..."`. All required keys confirmed present. `yq` will be available on `ubuntu-latest` CI runners.

### Issue 3 — Delete-line regression test result (REQUIRED)

Pattern 7 fixture line (`aws_access_key_id=AKIAIOSFODNN7EXAMPLE00`) was deleted. `selftest` run produced:

```
PASS: AppToken
PASS: UserKey
PASS: RFC-1918 IP
PASS: Generic apiKey
PASS: Bearer token
PASS: GitHub PAT
FAIL: AWS Access Key — pattern did not match fixture
Tripwire self-test FAILED: ...
Exit code: 1
```

Selftest correctly exited 1. Fixture restored; selftest re-run → all 7 PASS, exit 0.

## Commits

| Commit | Task | Description |
|--------|------|-------------|
| `3172d9f` | Task 1 | Add secret-scan tripwire fixture |
| `01c051f` | Task 2 | Add secret-scan shared bash script |
| `fea2d9a` | Task 3 | Add secret-scan workflow with tripwire self-test job |

## Files created

- `.github/secret-scan-fixture.txt`
- `.github/scripts/secret-scan.sh`
- `.github/workflows/secret-scan.yml`

## Files NOT touched

- `.github/dependabot.yml` — PLAN-1.1 (untouched)
