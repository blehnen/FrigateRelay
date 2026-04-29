# Review: Plan 1.2 (Phase 2)

## Verdict: MINOR_ISSUES

---

## Stage 1: Spec Compliance — PASS

### Task 1 — Fixture file
PASS. `.github/secret-scan-fixture.txt` exists, contains exactly 7 labelled lines (one per
pattern), all tagged `# secret-scan:fixture`. Header warns against replacing synthetic values.
Values are visibly fake: sequential alpha chars, `192.168.99.99`, `alg=none` JWT Bearer,
`ghp_ABC...` (36-char), `AKIAIOSFODNN7EXAMPLE00` (AWS public-doc canonical). No real credential
shape.

### Task 2 — Script
PASS. `.github/scripts/secret-scan.sh` has `set -euo pipefail`, parallel LABELS[]/PATTERNS[]
arrays (single authoritative source), subcommand-style `scan`/`selftest` dispatch. `scan` mode
excludes `.shipyard`, `CLAUDE.md` (justified by Issue 1), and the fixture via git pathspecs.
`selftest` mode runs `git grep -qE` per pattern against only the fixture and prints `PASS:`/`FAIL:`.
7 patterns present matching RESEARCH.md §Secret-Scan Regex Set.

### Task 3 — Workflow
PASS. `.github/workflows/secret-scan.yml`: `on: push, pull_request`, `permissions: contents:
read`, two parallel jobs (`scan`, `tripwire-self-test`), both on `ubuntu-latest`, both using
`actions/checkout@v4` with `fetch-depth: 1`, both calling the script with the correct mode arg.

---

## Stage 2: Code Quality

### Critical
None.

### Minor

**`\x27` hex escape in Pattern 4 may silently fail on some git versions**
File: `.github/scripts/secret-scan.sh`, line 43
```
'api[Kk]ey\s*[=:]\s*["\x27]?[A-Za-z0-9_\-]{20,}'
```
`\x27` (single-quote) is a PCRE hex escape, not a standard ERE character class element. `git grep
-E` uses POSIX ERE by default; hex escapes are not part of POSIX ERE. On git builds without PCRE
support the character class `["\x27]` may be treated as the literal characters `\`, `x`, `2`, `7`
rather than `"` or `'`. The fixture's Pattern 4 line uses a double-quoted value so the selftest
never exercises the single-quote branch, leaving the failure silent.

Remediation: Replace `\x27` with a literal single quote escaped for bash: `'"'"'` (close-single,
double-quoted-single, reopen-single) so the pattern becomes `'api[Kk]ey\s*[=:]\s*["'"'"'`
`]?[A-Za-z0-9_\-]{20,}'`. Alternatively, add a second fixture line for Pattern 4 with a
single-quoted value (`apiKey: 'xK9m...'`) to force the selftest to exercise that branch.

**`:!CLAUDE.md` exclusion is undocumented in the plan**
File: `.github/scripts/secret-scan.sh`, lines 65–67 (comment) and line 76
The plan's exclusion list is `.shipyard/` and the fixture only. `CLAUDE.md` was added by the
builder to suppress a true-positive RFC-1918 IP that appears in an architecture invariant note.
This is a correct and justified suppression, documented in SUMMARY Issue 1. However it creates
an undocumented exception that a future editor might not understand.

Remediation: Add a brief inline comment above the `:!CLAUDE.md` pathspec (already present on
line 65–67 — this is informational; the comment is adequate). No code change required, but the
ISSUES.md entry should record that `CLAUDE.md` holds a deliberate RFC-1918 IP example that is
suppressed by design, so a future refactor doesn't silently remove the exclusion.

### Positive
- Pattern list is the single source of truth in the script; workflow YAML contains zero pattern
  duplication.
- `fetch-depth: 1` on both jobs is correctly justified — `git grep` on the index does not need
  history.
- `set -euo pipefail` prevents silent errors.
- `FAILED` flag approach (rather than `set -e` abort on first hit) correctly reports ALL matching
  patterns before exiting, giving full diagnostic output.
- `# secret-scan:fixture` trailer is a clean, greppable audit trail.

---

## Regression test outcome

Documented in SUMMARY-1.2.md §Issue 3 (builder-performed):
- Pattern 7 fixture line (`aws_access_key_id=AKIAIOSFODNN7EXAMPLE00`) was deleted.
- `selftest` run produced `FAIL: AWS Access Key — pattern did not match fixture` and exited 1.
- All other 6 patterns printed `PASS:`.
- Fixture restored; `selftest` re-run → all 7 `PASS:`, exit 0.

**Outcome: tripwire correctly catches breakage. Regression test PASSED.**

---

## Check results

| Check | Result |
|---|---|
| Fixture exists with 7 lines | PASS |
| Fixture values obviously synthetic | PASS |
| Script: `scan` / `selftest` subcommands | PASS |
| Script: 7 patterns match RESEARCH.md | PASS |
| Script: `.shipyard` + fixture excluded in scan | PASS |
| Workflow: `on: push, pull_request` | PASS |
| Workflow: 2 jobs (`scan`, `tripwire-self-test`) | PASS |
| Workflow: `actions/checkout@v4` `fetch-depth: 1` | PASS |
| Workflow: `permissions: contents: read` | PASS |
| Regression delete-line test | PASS (selftest exits 1 on broken fixture) |
| `\x27` ERE portability | MINOR — single-quote branch not exercised by selftest |
| `:!CLAUDE.md` exclusion documented | MINOR — justified but undocumented in plan |
