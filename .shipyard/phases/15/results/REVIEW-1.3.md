# Review: Plan 1.3 — CI / supply-chain hygiene (#8, #15, #24)

**Reviewer:** Sonnet (background reviewer agent)
**Date:** 2026-05-07
**Commits reviewed:** `566ebf1` (#8), `40672dd` (#15), `b79bd8c` (#24), `d8e6198` (review-followup fixes)

## Verdict: PASS (post-followup; APPROVE-WITH-IMPORTANT-FINDINGS pre-followup)

The reviewer agent terminated before writing this file to disk; this is a transcription of its returned report (3 sections below) plus the orchestrator's record of the two follow-up fixes that landed in `d8e6198`.

---

## Stage 1 — Correctness

### Task 1: `run-tests.sh` `--coverage` branch arg parity (#8)

**Status: PASS**
- Line 67 opens MTP-arg mode with `-- \`; line 71 appends `"${PASS_THROUGH_ARGS[@]}"` as the final element. Quoting protects multi-word args like `--filter "ActionEntryTypeConverterTests"`.
- Coverage branch sends passthrough after `--` (MTP domain), fast-mode sent them before `--` — see Important finding 2 below; addressed in the follow-up commit.

### Task 2: RFC 1918 fixture coverage (#15)

**Status: PASS**
- `LABELS` and `PATTERNS` arrays both grew from 7 → 9 entries, parallel-aligned.
- Pattern 8: `10\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}` — uses `{1,3}` quantifier per spec (SUMMARY-1.3.md line 8 incorrectly stated `[0-9]+`; the actual file matches the spec).
- Pattern 9: `172\.(1[6-9]|2[0-9]|3[0-1])\.[0-9]{1,3}\.[0-9]{1,3}` — alternation covers exactly 16-31.
- Fixture lines at `.github/secret-scan-fixture.txt:40,43` exercise both new patterns.
- `tripwire-self-test` loop iterates `for i in "${!LABELS[@]}"` (lines 100–109), backward-compatible with array growth.
- No false positives: `git grep -E '10\.[0-9]+\.[0-9]+\.[0-9]+' src/` returns no 4-octet IP matches in tracked code.

### Task 3: SHA-pin Actions (#24, scope expanded to docs.yml)

**Status: PASS**
- Greppable invariant `git grep -nE 'uses:\s*[^@\s]+@v[0-9]' .github/workflows/` — empty.
- 17 SHA-pin sites total: `release.yml` (7), `ci.yml` (2), `secret-scan.yml` (2), `docs.yml` (6).
- Format consistent: `uses: action@<40-hex>  # vN` with two spaces before `#` so Dependabot can match.
- Same SHA reused for the same `action@vN` reference across files: `actions/checkout@v6` → `de0fac2e...` (8 occurrences); `actions/setup-dotnet@v5` → `c2fa09f4...` (4 occurrences); `docker/build-push-action@v7` → `bcafcacb...` (2 occurrences in release.yml).
- All 4 unique action SHAs verified via `gh api` (lightweight tags returning commit type directly).

---

## Stage 2 — Integration

### Critical
None.

### Important (2 — both addressed in `d8e6198`)

1. **`#8` miscategorized as `### Security`.** The PLAN listed all three Task entries under `### Security`, but `#8` is a bug fix (missing passthrough arg in coverage branch) with no security surface. Placing under `### Security` would inflate that section and mislead consumers of the changelog. **Fixed in `d8e6198`:** moved to `### Fixed` alongside `#14`.

2. **Pre-existing fast-mode bug in `run-tests.sh` exposed by parity work.** Line 87 (pre-Phase-15) had `dotnet run --project "$proj" -c "$CONFIG" --no-build "${PASS_THROUGH_ARGS[@]}"` — no `--` separator. `dotnet run` interprets `--filter "<x>"` as a CLI flag rather than passing it to MTP, producing a hard error on the fast path. Pre-existing but obvious now that the coverage branch is correct. **Fixed in `d8e6198`:** added `--` separator with an empty-args guard so we don't emit a bare `--`. Verified with `bash .github/scripts/run-tests.sh --skip-integration --filter "ActionEntryTypeConverterTests"` reaching MTP correctly.

### Suggestions (2 — non-blocking)

1. **SUMMARY-1.3.md line 8 inaccuracy** — states builder used `[0-9]+` for the 10.x pattern; actual file uses `{1,3}` per spec. Cosmetic; the SUMMARY is wrong, not the code. Not corrected post-build (reviewers reading the SUMMARY can cross-check the code).

2. **docs.yml scope expansion was the right call.** The greppable invariant covers all workflow files by directory; leaving docs.yml unpinned would have failed the gate. Catching at build time and expanding atomically vs. spinning up a follow-up plan is the correct trade-off.

### Positive

- Same SHA reused consistently across multiple sites for the same action — Dependabot will produce a single PR per action bump rather than N PRs.
- Architect's CHANGELOG-serialize-last advisory honored (per-plan CHANGELOG entries; no merge friction observed).
- Fast-mode `--` separator fix is bonus hardening that matches CLAUDE.md's `dotnet run --project ... -- --filter "X"` convention.

---

**Critical:** 0
**Important:** 2 (both addressed in `d8e6198`)
**Suggestions:** 2 (non-blocking)
**Final verdict:** **PASS** post-followup.
