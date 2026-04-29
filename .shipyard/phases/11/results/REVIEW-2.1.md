# REVIEW-2.1 — LICENSE, SECURITY.md, CHANGELOG.md

**Reviewer:** reviewer-2-1
**Date:** 2026-04-28
**Commits:** c73443f (LICENSE), aec477e (SECURITY.md), 2a68cee (CHANGELOG.md)
**Plan:** PLAN-2.1

## Verdict: PASS

---

## Stage 1 — Correctness

### LICENSE (c73443f)

All acceptance criteria pass:

| Check | Result |
|-------|--------|
| `grep -q '^MIT License' LICENSE` | PASS |
| `grep -qE '^Copyright \(c\) 2026 Brian Lehnen$' LICENSE` | PASS — exact wording confirmed |
| `grep -q 'Permission is hereby granted, free of charge'` | PASS |
| `grep -qE 'WITHOUT WARRANTY OF ANY KIND'` | PASS |
| No Apache/BSD/GPL/LGPL/AGPL in file | PASS |
| Standard 21-line SPDX-MIT boilerplate | PASS |

D1 honored: MIT, Brian Lehnen, 2026.

### SECURITY.md (aec477e)

All acceptance criteria pass:

| Check | Result |
|-------|--------|
| `grep -q '^# Security Policy'` | PASS |
| `grep -q 'Private vulnerability reporting'` | PASS |
| `grep -q 'security/advisories/new'` | PASS |
| `grep -nE 'mailto:\|@[a-z0-9.-]+\.[a-z]{2,}'` | PASS — no email exposure |
| No `192.168.x.x` / secrets | PASS |

D6 honored: GitHub private vulnerability reporting only; no mailto; no email exposure. Advisory URL uses `blehnen/FrigateRelay` (confirmed via `git remote get-url origin` per PLAN-2.1 instructions). Maintainer Settings note present as block-quote. Response window of 7 days stated.

### CHANGELOG.md (2a68cee)

All acceptance criteria pass:

| Check | Result |
|-------|--------|
| `grep -q '^# Changelog'` | PASS |
| `grep -q 'Keep a Changelog'` | PASS — links keepachangelog.com/en/1.1.0/ |
| `grep -q '## \[Unreleased\]'` | PASS |
| Phase count (`grep -cE '^### Phase ([1-9]\|10) — '`) | PASS — 10 phases present |
| No `## [1.0.0]` premature version section | PASS |
| No secrets / IP literals | PASS |

D3 honored: retroactive Phases 1–10 under single `## [Unreleased]`; first versioned section deferred to Phase 12.

**CHANGELOG truthfulness spot-check (vs git commit history):**

- Phase 1: CHANGELOG mentions `Directory.Build.props`, `FrigateRelay.Abstractions` with `IEventSource`/`IActionPlugin`/etc., `FrigateRelay.Host` + `PlaceholderWorker`, Abstractions tests. Confirmed by 9 Phase 1 commits (`b480f12` through `ef68446`). Accurate.
- Phase 8: CHANGELOG mentions `ProfileResolver`, `ActionEntryTypeConverter`, visibility sweep of 7 host types, `DynamicProxyGenAssembly2` IVT, `ValidateAll` collect-all. Confirmed by Phase 8 commits (`b5b87eb`, `6264154`, `a880bac`, etc.). Accurate.
- Phase 10: CHANGELOG matches PLAN-10 deliverables (Dockerfile, release.yml, /healthz, ValidateSerilogPath, IMqttConnectionStatus). Accurate.

---

## Stage 2 — Integration

| Check | Result |
|-------|--------|
| `bash .github/scripts/secret-scan.sh scan` | PASS — "no secret-shaped strings found in tracked files" |
| File-disjoint with PLAN-2.2 (README, CONTRIBUTING, CLAUDE.md) | PASS — zero overlap |
| File-disjoint with PLAN-2.3 (templates/**) | PASS — zero overlap |
| File-disjoint with PLAN-2.4 (.github/ISSUE_TEMPLATE/**, docs.yml) | PASS — zero overlap |
| Each commit touches only its declared file | PASS — c73443f→LICENSE, aec477e→SECURITY.md, 2a68cee→CHANGELOG.md |

---

## Findings

None. No defects found.

---

## Positive Notes

- SECURITY.md is well-structured: "Do not open a public GitHub issue" warning is prominent; maintainer Settings callout is a block-quote making it visually distinct and hard to miss.
- CHANGELOG format is strict Keep-a-Changelog v1.1.0 with Semantic Versioning reference — both linked.
- Phase entries are appropriately concise (5–15 bullets each) and focus on user-visible behavior rather than internal refactors, matching PLAN-2.1 guidance.
- Issue IDs (`ID-N`) referenced where applicable — gives operators a cross-reference trail.
- Builder correctly resolved `blehnen/FrigateRelay` from `git remote` rather than hardcoding a guess.
- Three atomic commits (one file each) — clean git history, easy bisect.
