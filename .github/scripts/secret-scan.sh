#!/usr/bin/env bash
# .github/scripts/secret-scan.sh
#
# Secret-scan helper for FrigateRelay.
#
# USAGE:
#   bash .github/scripts/secret-scan.sh scan      # scan the tree for secret-shaped strings
#   bash .github/scripts/secret-scan.sh selftest   # verify every pattern matches the fixture
#
# MODES:
#   scan      Run git grep -nE for each pattern across the full tracked tree,
#             EXCLUDING .shipyard/ (planning docs discuss pattern shapes) and
#             .github/secret-scan-fixture.txt (intentional fake-credential file).
#             Exits 1 if any pattern matches; exits 0 if the tree is clean.
#
#   selftest  Run git grep -qE for each pattern against ONLY the fixture file.
#             Exits 1 if any pattern does NOT match (pattern is broken/drifted).
#             Prints PASS:/FAIL: per pattern for visibility. Exits 0 if all match.
#
# Pattern list is the authoritative set — one place, used by both modes.
# Update here only; the workflow calls this script.

set -euo pipefail

# ── Pattern registry ────────────────────────────────────────────────────────
# Parallel arrays: LABELS[i] <-> PATTERNS[i]
# All patterns use ERE syntax (git grep -E).

LABELS=(
  "AppToken"
  "UserKey"
  "RFC-1918 IP"
  "Generic apiKey"
  "Bearer token"
  "GitHub PAT"
  "AWS Access Key"
)

PATTERNS=(
  'AppToken\s*=\s*[A-Za-z0-9]{20,}'
  'UserKey\s*=\s*[A-Za-z0-9]{20,}'
  '192\.168\.[0-9]{1,3}\.[0-9]{1,3}'
  'api[Kk]ey\s*[=:]\s*["'"'"']?[A-Za-z0-9_\-]{20,}'
  'Bearer\s+[A-Za-z0-9._\-]{20,}'
  'ghp_[A-Za-z0-9]{36}'
  'AKIA[A-Z0-9]{16}'
)

FIXTURE=".github/secret-scan-fixture.txt"

# ── Mode dispatch ────────────────────────────────────────────────────────────

if [[ $# -ne 1 ]]; then
  echo "Usage: $0 scan | selftest" >&2
  exit 2
fi

MODE="$1"

case "$MODE" in

  scan)
    # Scan the full tracked tree, excluding:
    #   .shipyard/                      — planning docs legitimately discuss pattern shapes
    #   CLAUDE.md                       — developer conventions doc; cites the 192.168.x.x
    #                                     pattern as a *forbidden* example (same as .shipyard)
    #   .github/secret-scan-fixture.txt — intentional fake-credential tripwire file
    # Any match outside these paths is a potential committed secret → fail.
    FAILED=0
    for i in "${!LABELS[@]}"; do
      label="${LABELS[$i]}"
      pattern="${PATTERNS[$i]}"
      # Use pathspec exclusions
      if git grep -nE "$pattern" -- \
          ':!.shipyard' \
          ':!CLAUDE.md' \
          ':!.github/secret-scan-fixture.txt' \
          2>/dev/null; then
        echo "FAIL: pattern '$label' matched in the tree — possible committed secret." >&2
        FAILED=1
      fi
    done
    if [[ "$FAILED" -ne 0 ]]; then
      echo "" >&2
      echo "Secret-scan FAILED: one or more patterns matched. Review the lines above." >&2
      exit 1
    fi
    echo "Secret-scan PASSED: no secret-shaped strings found in tracked files."
    exit 0
    ;;

  selftest)
    # Assert every pattern still matches the fixture.
    # A non-match means the regex was broken (over-tightened, typo, etc.).
    FAILED=0
    for i in "${!LABELS[@]}"; do
      label="${LABELS[$i]}"
      pattern="${PATTERNS[$i]}"
      if git grep -qE "$pattern" -- "$FIXTURE" 2>/dev/null; then
        echo "PASS: $label"
      else
        echo "FAIL: $label — pattern did not match fixture" >&2
        FAILED=1
      fi
    done
    if [[ "$FAILED" -ne 0 ]]; then
      echo "" >&2
      echo "Tripwire self-test FAILED: one or more patterns did not match the fixture." >&2
      echo "A regex may have been broken by a recent edit. Fix the pattern or the fixture." >&2
      exit 1
    fi
    echo "All patterns matched fixture — scanner is healthy."
    exit 0
    ;;

  *)
    echo "Unknown mode '$MODE'. Usage: $0 scan | selftest" >&2
    exit 2
    ;;

esac
