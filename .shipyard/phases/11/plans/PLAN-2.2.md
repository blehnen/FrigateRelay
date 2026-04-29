---
phase: 11-oss-polish
plan: 2.2
wave: 2
dependencies: [1.1]
must_haves:
  - README.md (overview + docker-run quickstart + config walkthrough + plugin scaffold pointer)
  - CONTRIBUTING.md (coding standards, test expectations, PR checklist)
  - CLAUDE.md staleness fixes (D3 #2 — two stale lines)
files_touched:
  - README.md
  - CONTRIBUTING.md
  - CLAUDE.md
tdd: false
risk: low
---

# Plan 2.2: README + CONTRIBUTING + CLAUDE.md staleness fixes

## Context

Three root-level/near-root documents. README and CONTRIBUTING are net-new; CLAUDE.md gets two surgical edits from RESEARCH.md sec 5 (CONTEXT-11 D3 #2). File-disjoint with PLAN-2.1, PLAN-2.3, PLAN-2.4.

**Forward-reference policy.** README and CONTRIBUTING reference deliverables that land in PLAN-2.3 (template), PLAN-2.4 (workflows + GitHub templates), and PLAN-3.1 (plugin-author-guide + samples). All references are by **path** (e.g. `templates/FrigateRelay.Plugins.Template/`, `docs/plugin-author-guide.md`, `samples/FrigateRelay.Samples.PluginGuide/`) and use the **`dotnet new frigaterelay-plugin`** invocation form (CONTEXT-11 D5). Builder MUST keep these references stable — when those plans land, the paths match, no rewrites needed.

## Dependencies

- **Wave 1 gate:** PLAN-1.1 must complete (CONTEXT-11 D7).

## Tasks

### Task 1: README.md (root)

**Files:**
- `README.md` (create)

**Action:** create

**Description:**
The README is the project's front door. ROADMAP Phase 11 mandates: overview, quickstart (docker-run-based), full config walkthrough against the Phase 8 example, "Adding a new action plugin" tutorial pointer.

Required sections (in order):

1. **Title + one-paragraph project pitch.** What FrigateRelay is (greenfield .NET 10 background service that bridges Frigate MQTT events to BlueIris/Pushover/etc with per-action validators). Avoid marketing prose. No badges/screenshots/demo GIFs (CONTEXT-11 explicitly out of scope).

2. **Quickstart — Docker run** (uses Phase 10's `ghcr.io/<owner>/frigaterelay:latest` image and `docker/docker-compose.example.yml`). Show:
   - `git clone` + `cp docker/.env.example .env`
   - Edit `.env` with BlueIris + Pushover secrets (link to `SECURITY.md` for "do not commit").
   - `docker compose -f docker/docker-compose.example.yml up`.
   - Curl `/healthz` to verify readiness.

3. **Configuration walkthrough.** Reference the Phase 8 example file `config/appsettings.Example.json` (verify path with `ls config/` first; if path differs, use the actual path). Walk through: `FrigateMqtt`, `Profiles`, `Subscriptions`, per-action `SnapshotProvider` override. Use a small inline JSON snippet for one profile + one subscription, not the full file. Note the env-var override convention (`FrigateMqtt__Server`) for Docker.

4. **"Adding a new action plugin" — short tutorial** that hands off to `docs/plugin-author-guide.md`. The README itself shows the **scaffold one-liner only**:

   ```bash
   dotnet new install templates/FrigateRelay.Plugins.Template
   dotnet new frigaterelay-plugin -n FrigateRelay.Plugins.MyPlugin -o src/FrigateRelay.Plugins.MyPlugin
   ```

   Then says "see `docs/plugin-author-guide.md` for the full walkthrough." Do not duplicate the guide here.

5. **Project status / roadmap pointer.** One line: "Pre-1.0; phase 11 is the OSS-polish gate before v1.0.0 cutover. See `.shipyard/ROADMAP.md` for plan and `CHANGELOG.md` for history."

6. **License pointer** — one line linking to `LICENSE` (PLAN-2.1 Task 1).

**Constraints:**
- No "File Transfer Server" or any reference-project boilerplate (ROADMAP success criterion).
- No hard-coded IPs/hostnames (CLAUDE.md invariant + secret-scan).
- All GHCR image references use `<owner>/frigaterelay` placeholder, NOT a baked-in org slug.
- Markdown linting: no broken inline code blocks, no missing language tags on fenced code, no trailing whitespace lines.

**Acceptance Criteria:**
- `test -f README.md`
- `grep -q '^# ' README.md` (one H1 title)
- `grep -q 'docker compose' README.md` (quickstart present)
- `grep -q 'dotnet new frigaterelay-plugin' README.md` (scaffold one-liner)
- `grep -q 'docs/plugin-author-guide.md' README.md` (handoff)
- `grep -nE 'File Transfer Server|FrigateMQTTProcessingService' README.md` returns zero matches.
- `grep -nE '192\.168\.|10\.[0-9]+\.[0-9]+\.[0-9]+\.[0-9]|AppToken=[A-Za-z0-9]{20,}|UserKey=[A-Za-z0-9]{20,}' README.md` returns zero matches.
- `grep -q 'CHANGELOG.md' README.md && grep -q 'LICENSE' README.md` (cross-references).

### Task 2: CONTRIBUTING.md

**Files:**
- `CONTRIBUTING.md` (create at repo root)

**Action:** create

**Description:**
Coding standards + test expectations + PR checklist. CONTRIBUTING is contributor-facing; CLAUDE.md is agent-facing. Do not duplicate CLAUDE.md verbatim — instead, summarize the **contributor-relevant subset** and link to CLAUDE.md for the full architecture invariant set.

Required sections:

1. **How to build + test** — point at the canonical commands from CLAUDE.md ("Commands" section): `dotnet build FrigateRelay.sln -c Release`, the `dotnet run --project tests/...` test invocation pattern (NOT `dotnet test`), and `bash .github/scripts/run-tests.sh` as the wrapper.

2. **Coding standards (subset)** — pull these from CLAUDE.md "Architecture invariants":
   - Warnings-as-errors enforced repo-wide; build must be clean on Linux + Windows.
   - No `.Result` / `.Wait()` in source.
   - Test names use `Method_Condition_Expected` underscores (DAMP convention; CA1707 silenced for tests).
   - Use the shared `CapturingLogger<T>` from `FrigateRelay.TestHelpers`, not per-assembly copies.
   - No hard-coded IPs/hostnames in source (including comments).
   - No secrets in committed config.
   - `<InternalsVisibleTo>` via MSBuild item form, not source attribute.
   - Prefer `<NewLine />` link to CLAUDE.md "Architecture invariants" section for the full list.

3. **Test expectations.**
   - New + changed features need unit OR integration tests.
   - Test projects use MSTest v3 + MTP runner; `OutputType=Exe`; invoked via `dotnet run`.
   - Integration tests use Testcontainers (Docker required).
   - FluentAssertions pinned at 6.12.2 — do not upgrade (license constraint).

4. **PR checklist** (rendered as a Markdown task list, copy-pastable into PR description):
   - [ ] Build is green on Linux: `dotnet build FrigateRelay.sln -c Release`.
   - [ ] Tests pass: `bash .github/scripts/run-tests.sh`.
   - [ ] No new `.Result` / `.Wait()` calls.
   - [ ] No hard-coded IPs/hostnames or secrets.
   - [ ] CHANGELOG.md updated under `## [Unreleased]` if user-visible.
   - [ ] New plugin? Followed `docs/plugin-author-guide.md`.
   - [ ] Phase-managed: if this is a Shipyard phase commit, message follows `shipyard(phase-N): ...` convention.

5. **Plugin-author shortcut** — one-line pointer to `dotnet new frigaterelay-plugin` invocation + link to `docs/plugin-author-guide.md`.

6. **Reporting issues** — link to `SECURITY.md` for vulnerabilities; otherwise GitHub Issues.

**Acceptance Criteria:**
- `test -f CONTRIBUTING.md`
- `grep -q 'dotnet build FrigateRelay.sln' CONTRIBUTING.md`
- `grep -q 'run-tests.sh' CONTRIBUTING.md`
- `grep -q 'FluentAssertions' CONTRIBUTING.md` (license-pinning callout)
- `grep -q 'CapturingLogger' CONTRIBUTING.md`
- `grep -q 'CLAUDE.md' CONTRIBUTING.md` (link to full invariant set)
- `grep -q 'SECURITY.md' CONTRIBUTING.md` (vulnerability handoff)
- `grep -nE '192\.168\.|AppToken=[A-Za-z0-9]{20,}' CONTRIBUTING.md` returns zero matches.

### Task 3: CLAUDE.md staleness fixes (D3 #2)

**Files:**
- `CLAUDE.md` (modify — two surgical edits)

**Action:** modify

**Description:**
RESEARCH.md sec 5 identified two stale lines. Both are single-line edits; do NOT rewrite surrounding sections.

**Edit 1 — "Project state" section.** Find the line that reads:

> FrigateRelay is a **greenfield .NET 10 rewrite**, currently **pre-implementation**. Nothing but planning docs exists in-tree yet.

Replace with text reflecting the post-Phase-10 reality. Suggested:

> FrigateRelay is a **.NET 10 background service** that supersedes the legacy `FrigateMQTTProcessingService`. Implementation is complete through **Phase 10** (Docker + multi-arch release workflow). Phase 11 (this phase) adds OSS polish (LICENSE, README, plugin scaffold, plugin-author guide). Phase 12 is the parity-cutover gate before v1.0.0.

The next paragraph in CLAUDE.md ("Before writing or changing code, read: ...") MUST be preserved verbatim — those file references are still authoritative.

**Edit 2 — Jenkinsfile description in CI section.** Find the line that reads:

> `Jenkinsfile` — coverage pipeline. Scripted. Docker agent `mcr.microsoft.com/dotnet/sdk:10.0` (tag-pinned — digest pin + Dependabot `docker` ecosystem deferred to Phase 10).

Replace with:

> `Jenkinsfile` — coverage pipeline. Scripted. Docker agent `mcr.microsoft.com/dotnet/sdk:10.0` digest-pinned (current SHA committed in `Jenkinsfile`; bump manually per the inline comment — Dependabot `docker` ecosystem watches `docker/Dockerfile` only, NOT `Jenkinsfile`, intentionally decoupled).

**Constraints:**
- Both edits are inline replacements — no new sections, no new bullets, no rewriting of surrounding paragraphs.
- All other CLAUDE.md content (architecture invariants, conventions, testing, deliberately-excluded list) MUST remain bytewise unchanged.

**Acceptance Criteria:**
- `grep -q 'currently \*\*pre-implementation\*\*' CLAUDE.md` returns zero matches (stale text removed).
- `grep -q 'Phase 11' CLAUDE.md` returns at least one match (post-edit reference).
- `grep -q 'tag-pinned — digest pin' CLAUDE.md` returns zero matches (stale Jenkinsfile blurb removed).
- `grep -q 'Jenkinsfile.*digest-pinned' CLAUDE.md` returns at least one match.
- `git diff CLAUDE.md` shows changes confined to the two target lines plus immediate surrounding lines (no broad rewrites).

## Verification

Run from repo root:

```bash
# 0. All three files present
test -f README.md && test -f CONTRIBUTING.md && test -f CLAUDE.md

# 1. Cross-references resolve to plans-2.x targets (paths exist after wave 2 + 3)
grep -q 'docs/plugin-author-guide.md' README.md
grep -q 'dotnet new frigaterelay-plugin' README.md
grep -q 'CHANGELOG.md' README.md
grep -q 'CLAUDE.md' CONTRIBUTING.md

# 2. Staleness scan — should now FAIL (zero matches confirms staleness removed)
grep -q 'pre-implementation' CLAUDE.md && exit 1 || true
grep -q 'tag-pinned — digest pin + Dependabot' CLAUDE.md && exit 1 || true

# 3. Secret + private-IP tripwire
grep -nE '192\.168\.|10\.[0-9]+\.[0-9]+\.[0-9]+\.[0-9]|AppToken=[A-Za-z0-9]{20,}|UserKey=[A-Za-z0-9]{20,}' \
  README.md CONTRIBUTING.md && exit 1 || true

# 4. Wrong-project boilerplate scan
grep -nE 'File Transfer Server|FrigateMQTTProcessingService' README.md && exit 1 || true

# 5. Solution still builds (no source changes in this plan; sanity check)
dotnet build FrigateRelay.sln -c Release
```
