---
phase: ci-skeleton
plan: 1.2
wave: 1
dependencies: []
must_haves:
  - Secret-scan workflow that fails on any committed secret-shaped string outside excluded paths
  - Fixture file with one match per pattern, committed and excluded from the main scan
  - Tripwire self-test job that asserts every pattern still matches the fixture
  - Local reusable bash script so the same patterns run in CI and on a developer workstation
files_touched:
  - .github/workflows/secret-scan.yml
  - .github/secret-scan-fixture.txt
  - .github/scripts/secret-scan.sh
tdd: false
risk: medium
---

# PLAN-1.2 — Secret-scan workflow + tripwire fixture + self-test

## Context

ROADMAP Phase 2 requires a committed-secrets tripwire. **D4** (CONTEXT-2.md)
refines the ROADMAP's "one-time manual verification" into an ongoing
self-test: a committed fixture plus a `tripwire-self-test` job that fails if
the regex set stops matching the fixture. This catches regex drift (an
over-tightening edit that silently disables a pattern) without any manual
dance.

**Pattern set (RESEARCH.md §Secret-Scan Regex Set):** seven ERE patterns —
`AppToken=`, `UserKey=`, RFC-1918 `192.168.x.x`, anchored generic API key,
`Bearer ...`, `ghp_...` (36-char), `AKIA...` (16-char). ROADMAP's looser
`api[a-z0-9]{28,}` is deliberately replaced by the anchored form to avoid
matching identifiers like `apiclientlogginghandler`.

**Two jobs in the workflow:**

1. `scan` — `git grep -nE` the full tree for every pattern, **excluding**
   `.shipyard/` (planning docs legitimately discuss secret shapes) and the
   fixture file itself (intentionally full of matches). Any hit fails the
   build with the offending line in the log.
2. `tripwire-self-test` — runs the same pattern set against ONLY the
   fixture. If any pattern fails to match, the scanner is broken and the
   job fails. This enforces that every pattern remains active.

**Shared implementation:** both jobs call a single script
`.github/scripts/secret-scan.sh` with a mode flag (`scan` or `selftest`).
This keeps the pattern list in exactly one place and makes the workflow
trivially runnable on a developer workstation (`bash .github/scripts/secret-scan.sh scan`).

**Resolution of Open Question 6 (`fetch-depth`):** `git grep` against the
working tree does not need history. Use `actions/checkout@v4` with
`fetch-depth: 1` on both jobs. Shallow is safe because the scan is a file-
content scan, not a history walk; `git grep` on the index works with any
fetch depth ≥ 1. The small speed win is worth standardizing on.

**Security note on fixture strings:** every fixture value is visibly
synthetic (e.g., `AKIAIOSFODNN7EXAMPLE00` from AWS public documentation,
sequential alphabets, fake JWT with `alg=none`). No string in the fixture
is a real credential shape that could coincidentally validate. This is a
deliberate property — document it in the fixture's header comment so a
future editor does not replace examples with "more realistic" strings.

**Excluded from scope (flagged as follow-ups):**

- `trufflehog` or `gitleaks` as a second-opinion scanner — not in
  ROADMAP Phase 2. A later phase may layer one in if regex drift remains a
  concern.
- Pre-commit hook running the same script — repo convention in Phase 1 did
  not establish a pre-commit harness; adding one is out of scope.

Parallel-safe with PLAN-1.1 (disjoint files).

## Dependencies

None. Wave 1, independent of PLAN-1.1.

## Tasks

### Task 1 — Author the fixture file

**Files:** `.github/secret-scan-fixture.txt`
**Action:** Create the file
**Description:** Create `.github/secret-scan-fixture.txt` with a header
comment explaining it is a tripwire fixture (strings are fake, do not
replace with realistic credentials) and exactly one labelled line per
pattern in the regex set. Use the draft in RESEARCH.md §Secret-Scan Fixture
Format (lines 280–310) as the authoritative content. Each pattern gets its
own section header comment referencing the pattern number so future readers
can map fixture lines back to the regex table.

**Acceptance Criteria:**
- File exists at `.github/secret-scan-fixture.txt`.
- Contains exactly seven labelled example lines, one per pattern (1–7).
- Header comment warns against replacing values with realistic strings.
- Every line uses obviously-synthetic values (e.g., `EXAMPLE`, sequential
  alphabets, `alg=none` JWT).

<task id="1" files=".github/secret-scan-fixture.txt" tdd="false">
  <action>Create `.github/secret-scan-fixture.txt` with a header comment (tripwire purpose; strings are synthetic; do not replace with realistic credentials) followed by seven labelled example lines, one per pattern — AppToken, UserKey, RFC-1918 IP, apiKey, Bearer, ghp_ PAT, AKIA AWS key — using the exact values from RESEARCH.md §Secret-Scan Fixture Format.</action>
  <verify>test -f .github/secret-scan-fixture.txt &amp;&amp; grep -c -E '^(AppToken=|UserKey=|server_url=http://192\.168\.|apiKey |Authorization: Bearer |token=ghp_|aws_access_key_id=AKIA)' .github/secret-scan-fixture.txt</verify>
  <done>File exists. `grep -c` reports `7` — one line per pattern is present.</done>
</task>

### Task 2 — Author `.github/scripts/secret-scan.sh`

**Files:** `.github/scripts/secret-scan.sh`
**Action:** Create the file
**Description:** Create an executable bash script with `set -euo pipefail`
that defines the seven ERE patterns (as an associative array or parallel
label/pattern arrays) and accepts a single positional argument:
- `scan` — runs `git grep -nE <pattern>` for each pattern across the full
  tree, excluding `.shipyard/` and `.github/secret-scan-fixture.txt` via
  `-- ':!.shipyard' ':!.github/secret-scan-fixture.txt'` pathspec. Any
  match prints the label and the `git grep` output and flips a failure
  flag. Exit 1 if any hit; exit 0 otherwise.
- `selftest` — runs `git grep -qE <pattern> -- .github/secret-scan-fixture.txt`
  for each pattern. Any pattern that does NOT match the fixture prints
  `FAIL: <label>` and flips the failure flag. Exit 1 on any non-match;
  exit 0 otherwise (with `PASS: <label>` for each pattern on success).
The script must be chmod +x.
Pattern list is the authoritative one from RESEARCH.md §Secret-Scan Regex
Set — copy the exact ERE strings.

**Acceptance Criteria:**
- File exists and is executable (`test -x`).
- Running `bash .github/scripts/secret-scan.sh scan` from the repo root on
  the current tree exits 0 (no secrets in tracked source today).
- Running `bash .github/scripts/secret-scan.sh selftest` exits 0 and prints
  `PASS:` for each of the seven pattern labels.
- Pattern count matches the regex table (seven patterns).

<task id="2" files=".github/scripts/secret-scan.sh" tdd="false">
  <action>Create executable `.github/scripts/secret-scan.sh` (`set -euo pipefail`, `chmod +x`) with a single positional arg `scan` or `selftest`. Define the seven ERE patterns from RESEARCH.md §Secret-Scan Regex Set. `scan` mode runs `git grep -nE "$pattern" -- ':!.shipyard' ':!.github/secret-scan-fixture.txt'` for each pattern; any hit fails the script. `selftest` mode runs `git grep -qE "$pattern" -- .github/secret-scan-fixture.txt` per pattern; any non-match fails. Print `PASS: &lt;label&gt;` / `FAIL: &lt;label&gt;` lines for selftest visibility.</action>
  <verify>test -x .github/scripts/secret-scan.sh &amp;&amp; bash .github/scripts/secret-scan.sh scan &amp;&amp; bash .github/scripts/secret-scan.sh selftest</verify>
  <done>Script is executable. `scan` mode exits 0 (no secrets in current tree). `selftest` mode exits 0 and prints seven `PASS:` lines (one per pattern).</done>
</task>

### Task 3 — Author `.github/workflows/secret-scan.yml`

**Files:** `.github/workflows/secret-scan.yml`
**Action:** Create the file
**Description:** Create a GitHub Actions workflow named `secret-scan`
triggered on `push` (all branches) and `pull_request`. Two jobs on
`ubuntu-latest`:
1. `scan` — `actions/checkout@v4` with `fetch-depth: 1`, then
   `run: bash .github/scripts/secret-scan.sh scan`.
2. `tripwire-self-test` — `actions/checkout@v4` with `fetch-depth: 1`, then
   `run: bash .github/scripts/secret-scan.sh selftest`.
Jobs run in parallel (no `needs:` between them). Each job has a
descriptive `name:`. No secrets, no artifacts, no `permissions:` elevation
beyond the default `contents: read`.

**Acceptance Criteria:**
- File exists at `.github/workflows/secret-scan.yml`.
- `on:` triggers include both `push` and `pull_request`.
- `jobs:` has exactly two entries: `scan` and `tripwire-self-test`.
- Both jobs use `actions/checkout@v4` with `fetch-depth: 1`.
- Both jobs shell out to `.github/scripts/secret-scan.sh` with the
  appropriate mode arg.
- No workflow-level `permissions:` block or a `contents: read`-only block.

<task id="3" files=".github/workflows/secret-scan.yml" tdd="false">
  <action>Create `.github/workflows/secret-scan.yml`: `name: secret-scan`, `on: [push, pull_request]`, two parallel jobs on `ubuntu-latest` (`scan` and `tripwire-self-test`), each using `actions/checkout@v4` with `fetch-depth: 1`, each running `bash .github/scripts/secret-scan.sh scan|selftest`. Either no workflow-level `permissions:` block or `permissions: contents: read`.</action>
  <verify>yq eval '.on | keys' .github/workflows/secret-scan.yml &amp;&amp; yq eval '.jobs | keys' .github/workflows/secret-scan.yml &amp;&amp; yq eval '.jobs.scan.steps[0].uses, .jobs."tripwire-self-test".steps[0].uses' .github/workflows/secret-scan.yml</verify>
  <done>`yq` reports `on` contains both `push` and `pull_request`; `jobs` contains exactly `scan` and `tripwire-self-test`; both jobs' first step uses `actions/checkout@v4`.</done>
</task>

## Verification

- Run the script against the current tree: `bash .github/scripts/secret-scan.sh scan` must exit 0 (no tracked secrets today).
- Run the self-test: `bash .github/scripts/secret-scan.sh selftest` must exit 0 with seven `PASS:` lines.
- Parse-check the workflow YAML: `yq eval '.' .github/workflows/secret-scan.yml > /dev/null` exits 0.
- Sanity: the fixture file is matched by every pattern (this is what the selftest proves — do not duplicate the check outside the script).
- Runtime (post-merge observation, NOT a gate): first push to a branch after merge runs both jobs green in Actions UI. If they fail, a broken-regex-detected-after-the-fact is exactly the scenario D4 was designed to catch — treat a failure as information, not a blocker.
