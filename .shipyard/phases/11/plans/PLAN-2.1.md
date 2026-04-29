---
phase: 11-oss-polish
plan: 2.1
wave: 2
dependencies: [1.1]
must_haves:
  - LICENSE (MIT, Brian Lehnen, 2026) at repo root
  - SECURITY.md at repo root using GitHub private vulnerability reporting (D6)
  - CHANGELOG.md (Keep-a-Changelog) covering Phases 1–10 retroactively (D3 #3)
files_touched:
  - LICENSE
  - SECURITY.md
  - CHANGELOG.md
tdd: false
risk: low
---

# Plan 2.1: Static repo docs — LICENSE + SECURITY + CHANGELOG (root files)

## Context

Three static root-level documents that satisfy CONTEXT-11 D1, D6, and D3 #3. All three are net-new (RESEARCH.md sec 1 confirmed zero collisions). File-disjoint with PLAN-2.2 (which owns README and CONTRIBUTING) and with all other Wave 2 plans.

Format choices (architect-discretion locked here):

- **CHANGELOG format = Keep-a-Changelog v1.1.0** (RESEARCH.md sec 8). Single `## [Unreleased]` section covering Phases 1–10, sub-headings per phase. The first proper version section (`## [1.0.0] — YYYY-MM-DD`) lands in Phase 12 cutover, NOT this phase.
- **SECURITY.md owner/repo slug.** RESEARCH.md uncertainty flag #4: the owner/repo for the GitHub private-advisory URL is unverified. Builder MUST run `git remote -v` to confirm the actual GitHub `<owner>/<repo>` slug before writing the link, OR (preferred) use a relative GitHub URL form that does not bake the owner into the file: `https://github.com/<owner>/<repo>/security/advisories/new` with the `<owner>/<repo>` placeholder replaced from `git remote get-url origin`. If `git remote` does not expose a GitHub URL, fall back to documenting the manual flow ("Repository → Security tab → Report a vulnerability") without a literal URL.

## Dependencies

- **Wave 1 gate:** PLAN-1.1 must complete (CONTEXT-11 D7).

## Tasks

### Task 1: LICENSE — MIT (Brian Lehnen, 2026)

**Files:**
- `LICENSE` (create)

**Action:** create

**Description:**
Create `LICENSE` at repo root. Use the standard SPDX-MIT boilerplate verbatim with `Copyright (c) 2026 Brian Lehnen` as the only substitution. Do not add a third "Permission notice" rider, do not embed a project name in the boilerplate (the SPDX-canonical text refers only to "the Software").

**Acceptance Criteria:**
- `test -f LICENSE`
- `grep -q '^MIT License' LICENSE`
- `grep -qE '^Copyright \(c\) 2026 Brian Lehnen$' LICENSE`
- `grep -q 'Permission is hereby granted, free of charge' LICENSE`
- `grep -qE 'WITHOUT WARRANTY OF ANY KIND' LICENSE`
- `grep -nE 'Apache|BSD|GPL|LGPL|AGPL' LICENSE` returns zero matches (sanity — wrong-license check).

### Task 2: SECURITY.md — GitHub private vulnerability reporting

**Files:**
- `SECURITY.md` (create at repo root)

**Action:** create

**Description:**
Per CONTEXT-11 D6: GitHub private vulnerability reporting only; no personal email; no `mailto:` link. Document the maintainer-only one-time setup step (Settings → Code security → "Private vulnerability reporting") as a prominent admin-checklist note, NOT as a code task this phase performs.

Sections (RESEARCH.md sec 9 template):

1. `# Security Policy`
2. `## Supported Versions` — table noting FrigateRelay is pre-1.0 and only the latest commit on `main` receives security fixes.
3. `## Reporting a Vulnerability` — explicit "Do not open a public GitHub issue" warning; numbered steps to file via the GitHub Security tab; note maintainer response window of 7 days.
4. **Maintainer note (block-quoted)** — call out that "Private vulnerability reporting" must be enabled via repo Settings → Code security; this is a one-time admin step.

**Owner/repo URL resolution:** Builder runs `git remote get-url origin`. If the remote is `https://github.com/<owner>/<repo>.git` or `git@github.com:<owner>/<repo>.git`, use that exact `<owner>/<repo>` in the advisory URL. If no GitHub remote is configured, write `<owner>/<repo>` literally and add a TODO comment for the maintainer to fill it in before publishing.

**Acceptance Criteria:**
- `test -f SECURITY.md`
- `grep -q '^# Security Policy' SECURITY.md`
- `grep -q 'Private vulnerability reporting' SECURITY.md` (Settings note)
- `grep -q 'security/advisories/new' SECURITY.md` (advisory URL form)
- `grep -nE 'mailto:|@[a-z0-9.-]+\.[a-z]{2,}' SECURITY.md` returns zero matches (no email exposure per D6).
- `grep -nE '192\.168\.|AppToken=|UserKey=' SECURITY.md` returns zero matches (secret-scan clean).

### Task 3: CHANGELOG.md — Keep-a-Changelog retroactive Phases 1–10

**Files:**
- `CHANGELOG.md` (create at repo root)

**Action:** create

**Description:**
Per CONTEXT-11 D3 #3: retroactive changelog covering Phase 1 through Phase 10. Format = Keep-a-Changelog v1.1.0 (https://keepachangelog.com/en/1.1.0/).

Document structure:

```
# Changelog

All notable changes to FrigateRelay are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Phase 10 — Dockerfile and multi-arch release workflow (2026-04-28)

#### Added
- `docker/Dockerfile` — multi-stage Alpine self-contained publish, non-root user UID 10001, HEALTHCHECK against `/healthz`.
- `docker/docker-compose.example.yml` + `docker/.env.example`.
- `.github/workflows/release.yml` — multi-arch GHCR push on `v*` tags with Mosquitto-sidecar smoke gate.
- `/healthz` readiness endpoint (200 only when MQTT connected AND host past `ApplicationStarted`).
- `StartupValidation.ValidateSerilogPath` — rejects `..`, UNC, and out-of-allowlist absolute paths.

#### Changed
- Host pivoted from `Microsoft.NET.Sdk.Worker` to `Microsoft.NET.Sdk.Web` for `MapHealthChecks`.
- `Jenkinsfile` SDK image is now digest-pinned.
- Dependabot watches the `docker` ecosystem.

(... per phase, in reverse chronological order: Phase 10 first, then 9, 8, 7, 6, 5, 4, 3, 2, 1 ...)
```

**Source material discovery (per RESEARCH.md uncertainty flag #2):**
1. Builder MUST first run `grep -nE '^## Phase [0-9]+' .shipyard/HISTORY.md | head -20` to confirm HISTORY.md per-phase section structure.
2. For each phase 1–10, extract the "Status" / "Deliverables" / key commit summaries from `.shipyard/HISTORY.md` AND cross-reference the ROADMAP Phase status block. Builder may also `git log --oneline --grep "shipyard(phase-N):" --all` to confirm commit set per phase.
3. Per-phase entry includes: short heading with phase name + completion date; `### Added`, `### Changed`, `### Fixed` sub-headings as applicable. Skip `### Deprecated` / `### Removed` / `### Security` headings if no items.
4. Do **NOT** invent items. If a phase has only Added items, list only `### Added`. Length per phase: 5–15 bullets is typical; trim aggressively to user-visible behavior, not internal restructures.

**Constraints:**
- Phases listed reverse-chronologically (10 → 1) under one `## [Unreleased]` heading.
- No `## [1.0.0]` section yet — that lands in Phase 12.
- No PII, no secrets, no IP literals (secret-scan tripwire).
- Issue IDs (`ID-N`) referenced where they closed an open issue, but full ISSUES.md contents are NOT inlined.

**Acceptance Criteria:**
- `test -f CHANGELOG.md`
- `grep -q '^# Changelog' CHANGELOG.md`
- `grep -q 'Keep a Changelog' CHANGELOG.md`
- `grep -q '## \[Unreleased\]' CHANGELOG.md`
- `grep -cE '^### Phase ([1-9]|10) — ' CHANGELOG.md` returns at least `10` (one entry per phase).
- `grep -nE '192\.168\.|AppToken=[A-Za-z0-9]{20,}|UserKey=[A-Za-z0-9]{20,}' CHANGELOG.md` returns zero matches.
- `grep -nE '^## \[1\.0\.0\]' CHANGELOG.md` returns zero matches (premature versioned section).

## Verification

Run from repo root:

```bash
# 0. All three files exist
test -f LICENSE && test -f SECURITY.md && test -f CHANGELOG.md

# 1. Static-content invariants
grep -q '^MIT License' LICENSE
grep -q 'Copyright (c) 2026 Brian Lehnen' LICENSE
grep -q '^# Security Policy' SECURITY.md
grep -q 'security/advisories/new' SECURITY.md
grep -q '## \[Unreleased\]' CHANGELOG.md
test "$(grep -cE '^### Phase ([1-9]|10) — ' CHANGELOG.md)" -ge 10

# 2. No-secrets / no-private-IP tripwire
grep -nE 'mailto:|192\.168\.|10\.[0-9]+\.[0-9]+\.[0-9]+\.[0-9]|AppToken=[A-Za-z0-9]{20,}|UserKey=[A-Za-z0-9]{20,}' \
  LICENSE SECURITY.md CHANGELOG.md && exit 1 || true

# 3. Solution still builds (no .cs added in this plan, but warnings-as-errors must hold)
dotnet build FrigateRelay.sln -c Release
```
