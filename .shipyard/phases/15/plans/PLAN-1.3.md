---
phase: 15-v1.2.1-hardening
plan: 1.3
wave: 1
dependencies: []
must_haves:
  - run-tests.sh --coverage branch passes through PASS_THROUGH_ARGS to MTP
  - secret-scan.sh + fixture cover RFC 1918 10.x and 172.16-31.x ranges
  - all third-party GitHub Actions in release.yml / ci.yml / secret-scan.yml SHA-pinned
files_touched:
  - .github/scripts/run-tests.sh
  - .github/scripts/secret-scan.sh
  - .github/secret-scan-fixture.txt
  - .github/workflows/release.yml
  - .github/workflows/ci.yml
  - .github/workflows/secret-scan.yml
  - CHANGELOG.md
tdd: false
risk: low
---

# Plan 1.3: CI / supply-chain hygiene (#8, #15, #24)

## Context

Three CI / supply-chain hardening items in one plan. #8 restores `--filter` passthrough parity to the `--coverage` branch of `run-tests.sh` (one-liner RESEARCH.md §1 #8). #15 extends the secret-scan tripwire to cover the full RFC 1918 range (`10.x.x.x`, `172.16-31.x.x`) — the existing `tripwire-self-test` job in `secret-scan.yml` enforces fixture coverage by construction (RESEARCH.md §1 #15). #24 SHA-pins all third-party GitHub Actions for SLSA L2+ — RESEARCH.md §4 R6 surfaced that `secret-scan.yml` ALSO has `actions/checkout@v6` references, so the pinning scope is THREE workflow files, not the two ROADMAP names. Greppable invariant `git grep -nE 'uses:\s*[^@\s]+@v[0-9]' .github/workflows/` covers all three by directory.

## Dependencies

None — Wave 1 root. No file overlap with PLAN-1.1, PLAN-1.2, or PLAN-1.4.

## Tasks

### Task 1: run-tests.sh `--coverage` branch arg parity (#8)
**Files:** `.github/scripts/run-tests.sh`
**Action:** modify
**Description:**
Per RESEARCH.md §1 #8, the `--coverage` branch at lines 67–70 of `run-tests.sh` invokes `dotnet run` without appending `"${PASS_THROUGH_ARGS[@]}"`, while the non-coverage branch at line 86 does. Append `"${PASS_THROUGH_ARGS[@]}"` to the `--coverage` invocation on a continuation line after the `--coverage-output` arg, still inside the `-- ...` MTP passthrough section. The `--` separator already opens MTP-arg mode at line 67; `PASS_THROUGH_ARGS` entries like `--filter "ClassName"` are MTP args.

No test changes. No CI YAML changes — `ci.yml` and `Jenkinsfile` already invoke the script correctly with passthrough args. The fix is observable when a Jenkins coverage run is given `--filter "ClassName"`: pre-fix it runs the entire suite under coverage (filter dropped); post-fix it runs only the filtered class.

**TDD:** false (shell script change; the verification is a manual dry-run with `bash -x`).

**Acceptance Criteria:**
- `.github/scripts/run-tests.sh` `--coverage` branch contains `"${PASS_THROUGH_ARGS[@]}"` on a continuation of the `dotnet run ... -- --coverage ...` invocation.
- Manual dry-run: `bash -x .github/scripts/run-tests.sh --coverage --filter "ActionEntryTypeConverterTests" 2>&1 | grep -E 'dotnet run.*--filter'` shows the filter reaching the dotnet command.
- `git diff .github/scripts/run-tests.sh` shows a single-line addition; no other lines changed.

### Task 2: RFC 1918 fixture coverage (#15)
**Files:** `.github/scripts/secret-scan.sh`, `.github/secret-scan-fixture.txt`
**Action:** modify
**Description:**
Per RESEARCH.md §1 #15, append two parallel-array entries to `LABELS` and `PATTERNS` in `secret-scan.sh` (currently 7 entries each, lines 29–47):

| Index | Label | ERE pattern |
|------:|-------|-------------|
| 7 | `RFC-1918 10.x.x.x` | `10\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}` |
| 8 | `RFC-1918 172.16-31.x.x` | `172\.(1[6-9]\|2[0-9]\|3[0-1])\.[0-9]{1,3}\.[0-9]{1,3}` |

Append matching fixture lines at the end of `.github/secret-scan-fixture.txt`:
- One line for the `10.x` pattern (e.g. `server_url=http://10.0.1.100:8080/api # secret-scan:fixture`).
- One line for the `172.16-31.x` pattern (e.g. `server_url=http://172.16.0.50:8080/api # secret-scan:fixture`).

Each fixture line MUST include a brief comment naming the pattern index it satisfies, matching the existing fixture file convention.

The existing tripwire job (`secret-scan.yml` lines 23–33, RESEARCH.md §1 #15) iterates `PATTERNS[i]` and `git grep -qE` against the fixture — both new lines must match their respective patterns or the tripwire fails. Confirm no `10.x.x.x` or `172.16-31.x.x` literals exist in `src/`, `tests/`, `docker/`, or non-fixture `.github/` files before committing — the existing `192.168.x.x` invariant already enforces this shape (RESEARCH.md §1 #15 gotcha).

**TDD:** false (CI tripwire is the gate).

**Acceptance Criteria:**
- `bash .github/scripts/secret-scan.sh` (scan mode) exits 0 on a clean tree.
- `bash .github/scripts/secret-scan.sh selftest` exits 0 — proves both new patterns match their fixture lines.
- `bash .github/scripts/secret-scan.sh selftest` deliberately fails when the new fixture lines are removed (manual validation).
- `LABELS` and `PATTERNS` arrays each contain 9 entries (7 pre-existing + 2 new).

### Task 3: SHA-pin all third-party GitHub Actions (#24) + CHANGELOG entries
**Files:** `.github/workflows/release.yml`, `.github/workflows/ci.yml`, `.github/workflows/secret-scan.yml`, `CHANGELOG.md`
**Action:** modify
**Description:**
Per RESEARCH.md §1 #24 and §4 R6, replace every `uses: action@vN` with `uses: action@<full-40-char-SHA>  # vN` across THREE workflow files (not 2 — the ROADMAP undercounted; `secret-scan.yml` also has `actions/checkout@v6` references at lines 16 and 28).

**Action references to pin (7 unique versions, 11 total `uses:` lines):**
- `actions/checkout@v6` — `release.yml:52`, `ci.yml:39`, `secret-scan.yml:16`, `secret-scan.yml:28` (4 sites)
- `actions/setup-dotnet@v5` — `ci.yml:42` (1 site)
- `docker/setup-qemu-action@v4` — `release.yml:56` (1 site)
- `docker/setup-buildx-action@v4` — `release.yml:60` (1 site)
- `docker/login-action@v4` — `release.yml:63` (1 site)
- `docker/metadata-action@v6` — `release.yml:72` (1 site)
- `docker/build-push-action@v7` — `release.yml:86`, `release.yml:147` (2 sites)

**Resolving SHAs (the builder runs at PLAN execution time, NOT at planning time — SHAs change with new releases):**

```bash
gh api /repos/actions/checkout/git/ref/tags/v6 --jq '.object.sha'
gh api /repos/actions/setup-dotnet/git/ref/tags/v5 --jq '.object.sha'
gh api /repos/docker/setup-qemu-action/git/ref/tags/v4 --jq '.object.sha'
gh api /repos/docker/setup-buildx-action/git/ref/tags/v4 --jq '.object.sha'
gh api /repos/docker/login-action/git/ref/tags/v4 --jq '.object.sha'
gh api /repos/docker/metadata-action/git/ref/tags/v6 --jq '.object.sha'
gh api /repos/docker/build-push-action/git/ref/tags/v7 --jq '.object.sha'
```

If a tag is annotated, follow the indirection: `gh api /repos/<owner>/<repo>/git/tags/<sha>` and use the resulting `.object.sha` (the commit SHA, not the tag SHA). The trailing comment `# vN` is mandatory — it is what Dependabot's `github-actions` ecosystem uses to know which version line the SHA represents (RESEARCH.md §1 #24 — Dependabot already configured weekly Monday).

**CHANGELOG.md:** Append three `[Unreleased]` `### Security` lines covering the three issues this plan closes:
- `- #8 — \`run-tests.sh\` \`--coverage\` branch now passes \`--filter\` and other MTP passthrough args to \`dotnet run\`.`
- `- #15 — secret-scan tripwire extended to cover RFC 1918 10.x.x.x and 172.16-31.x.x ranges.`
- `- #24 — Third-party GitHub Actions SHA-pinned across release.yml, ci.yml, secret-scan.yml (SLSA L2+).`

**TDD:** false (workflow file change; CI is the gate).

**Acceptance Criteria:**
- `git grep -nE 'uses:\s*[^@\s]+@v[0-9]' .github/workflows/` returns ZERO matches (the ROADMAP greppable invariant).
- Every `uses: <action>@<40-hex>` line carries a trailing `# vN` comment naming the version.
- Both `release.yml` build-push lines retain the same `vN` comment (so Dependabot can bump them together).
- `CHANGELOG.md` `[Unreleased]` lists `#8`, `#15`, `#24`.
- A subsequent `release.yml` run on a `v1.2.1-rc.0` prerelease tag reaches the `smoke` job (action references resolve) — operator-validated, not part of this plan's automated check.

## Verification

```bash
# #8 dry-run
bash -x .github/scripts/run-tests.sh --coverage --filter "ActionEntryTypeConverterTests" 2>&1 | head -50

# #15 tripwire passes
bash .github/scripts/secret-scan.sh selftest

# #15 scan mode passes (no committed RFC 1918 IPs outside fixture)
bash .github/scripts/secret-scan.sh scan

# #24 invariant — no version-tag references remain
git grep -nE 'uses:\s*[^@\s]+@v[0-9]' .github/workflows/    # empty
git grep -nE 'uses:\s*[^@\s]+@[0-9a-f]{40}' .github/workflows/ | wc -l    # 11 lines

# CHANGELOG entries present
grep -nE '#8|#15|#24' CHANGELOG.md | head -10
```
