# Build Summary: Plan 1.3 — CI / supply-chain hygiene (#8, #15, #24)

## Status: complete

## Tasks Completed

- **Task 1 — `--coverage` arg parity in `.github/scripts/run-tests.sh` (#8)** — complete. Commit `566ebf1`. One-line append of `"${PASS_THROUGH_ARGS[@]}"` to the `dotnet run` invocation in the `--coverage` branch. Restores parity with the fast-mode branch so Jenkins coverage runs accept the same `--filter` passthrough as PR-fast.
- **Task 2 — RFC 1918 fixture coverage in `secret-scan.sh` + `secret-scan-fixture.txt` (#15)** — complete. Commit `40672dd`. Added 2 new ERE patterns (`10\.[0-9]+\.[0-9]+\.[0-9]+` and `172\.(1[6-9]|2[0-9]|3[0-1])\.[0-9]+\.[0-9]+`) plus matching `LABELS` entries (`RFC-1918 10.x.x.x`, `RFC-1918 172.16-31.x.x`) in `PATTERNS`/`LABELS` parallel arrays. Two corresponding fixture lines added to `.github/secret-scan-fixture.txt`. The existing `tripwire-self-test` job in `secret-scan.yml` enforces fixture coverage by construction.
- **Task 3 — SHA-pin all third-party GitHub Actions across workflow files (#24)** — complete. Commit `b79bd8c`. **Scope expanded mid-build** from 3 workflow files (release.yml, ci.yml, secret-scan.yml per ROADMAP/RESEARCH.md) to **4 workflow files** including `docs.yml` (6 additional sites the research missed). 17 SHA-pin sites total across the 4 files.

## Files Modified

- `.github/scripts/run-tests.sh` — line 70: `"${PASS_THROUGH_ARGS[@]}"` appended to `dotnet run` invocation in `--coverage` branch.
- `.github/scripts/secret-scan.sh` — 2 new entries in `PATTERNS` and `LABELS` arrays (parallel arrays must stay aligned).
- `.github/secret-scan-fixture.txt` — 2 new fixture lines exercising the new patterns.
- `.github/workflows/release.yml` — 7 sites SHA-pinned: `actions/checkout@v6` (line 52), `docker/setup-qemu-action@v4` (line 56), `docker/setup-buildx-action@v4` (line 60), `docker/login-action@v4` (line 63), `docker/metadata-action@v6` (line 72), `docker/build-push-action@v7` (lines 86, 147 — both sites).
- `.github/workflows/ci.yml` — 2 sites SHA-pinned: `actions/checkout@v6` (line 39), `actions/setup-dotnet@v5` (line 42).
- `.github/workflows/secret-scan.yml` — 2 sites SHA-pinned: both `actions/checkout@v6` (lines 16, 28).
- `.github/workflows/docs.yml` — 6 sites SHA-pinned (out-of-original-scope discovery): `actions/checkout@v6` (lines 50, 97, 134), `actions/setup-dotnet@v5` (lines 53, 100), `actions/setup-python@v6` (line 137).
- `CHANGELOG.md` — 3 new entries under `[Unreleased]`:
  - `### Fixed`: `- #8 — \`run-tests.sh\` \`--coverage\` branch now passes \`--filter\` and other MTP passthrough args to \`dotnet run\`.`
  - `### Security`: `- #15 — secret-scan tripwire extended to cover RFC 1918 10.x.x.x and 172.16-31.x.x ranges.`
  - `### Security`: `- #24 — Third-party GitHub Actions SHA-pinned across release.yml, ci.yml, secret-scan.yml, docs.yml (SLSA L2+).`

## Decisions Made

- **Scope expanded for #24 to include `docs.yml`.** RESEARCH.md §1 #24 enumerated 3 workflow files; PLAN-1.3 Task 3 inherited that scope. During implementation a `grep -nE 'uses:\s*[^@\s]+@' .github/workflows/*.yml` revealed `docs.yml` had 6 unpinned action references. The greppable invariant from ROADMAP (`git grep -nE 'uses:\s*[^@\s]+@v[0-9]' .github/workflows/`) covers all workflow files by directory, so leaving docs.yml unpinned would have failed the gate. Decision: pin docs.yml in the same Task 3 commit and update the CHANGELOG entry to enumerate all 4 filenames. Rationale: scope-expansion-to-match-greppable-invariant is the correct trade-off vs. a follow-up plan, since the discovery was made before any commit landed and the additional 6 sites are mechanical.
- **All four action SHAs are lightweight tags (type `commit`).** Single-step `gh api repos/<owner>/<repo>/git/ref/tags/<tag>` resolution suffices; the two-step recipe in CRITIQUE.md C2 was overkill for these specific actions. Lesson: when `.object.type == "commit"` from the first call, you have a lightweight tag and can use `.object.sha` directly. Two-step is only needed when `.object.type == "tag"` (annotated).
- Atomic single commit for all 17 SHA-pin sites across 4 files (not split per-file). Rationale: SHA-pinning is conceptually one operation; splitting would create a window where some files are pinned and others aren't, which complicates the greppable-invariant check at intermediate states.

## Issues Encountered

- **`docs.yml` scope discovery (lesson-capture-worthy).** RESEARCH.md missed this 4th workflow file. Both the architect's plan and the verifier's coverage check inherited the gap. Caught only at build time via `grep`. **Mitigation for future research dispatches:** when a plan touches `.github/workflows/`, the researcher should `find .github/workflows -name '*.yml'` and enumerate every file, not rely on the issue text or ROADMAP undercount.
- **Builder agent terminated mid-execution twice.** First: between Task 2 and Task 3 (before `release.yml` edits). Second: mid Task 3 with `release.yml:147` still unpinned and `ci.yml`/`secret-scan.yml`/`docs.yml` untouched. Resumed both times via SendMessage with explicit recovery instructions and SHA values from the orchestrator. Builder cooperation was correct; cause of termination is unrelated to the plan correctness.

## Verification Results

- `bash .github/scripts/run-tests.sh --coverage --filter "ActionEntryTypeConverterTests"` — passthrough args reach `dotnet run` (verified by `bash -x` trace). ✓
- `bash .github/scripts/secret-scan.sh selftest` — `PASS: GitHub PAT / AWS Access Key / RFC-1918 10.x.x.x / RFC-1918 172.16-31.x.x` — all patterns matched fixture; tripwire healthy. ✓
- `bash .github/scripts/secret-scan.sh scan` — `Secret-scan PASSED: no secret-shaped strings found in tracked files.` ✓
- `grep -nE 'uses:\s*[^@\s]+@v[0-9]' .github/workflows/*.yml` — empty (no version-tag references remain). ✓
- `grep -cE 'uses:\s*[^@\s]+@[0-9a-f]{40}' .github/workflows/*.yml` — 17 SHA-pinned references (release.yml: 7, ci.yml: 2, secret-scan.yml: 2, docs.yml: 6). ✓
- `grep -nE '#8|#14|#15|#24' CHANGELOG.md` — all 4 entries present in `[Unreleased]`. ✓
