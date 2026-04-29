---
phase: 12-parity-cutover
plan: 3.2
wave: 3
dependencies: [1.3, 1.4, 3.1]
must_haves:
  - README.md gains a "Migrating from FrigateMQTTProcessingService" section linking docs/migration-from-frigatemqttprocessing.md and tools/FrigateRelay.MigrateConf/
  - RELEASING.md created with the exact manual `git tag v1.0.0 && git push --tags` commands plus pre-release checklist
  - CHANGELOG.md [Unreleased] gains a Phase 12 entry covering DryRun, MigrateConf, parity report, NDJSON sink
files_touched:
  - README.md
  - RELEASING.md
  - CHANGELOG.md
tdd: false
risk: low
---

# Plan 3.2: README migration section + `RELEASING.md` + CHANGELOG `[Unreleased]` Phase 12 entry

## Context

Closes Phase 12's documentation surface for the v1.0.0 cutover:

1. **README migration section** — operator-discoverable link to the migration tooling and doc.
2. **`RELEASING.md`** — the exact manual commands the operator runs after parity sign-off, per CONTEXT-12 D7. The `v1.0.0` tag itself is NOT an agent action; this plan documents the operator's command list.
3. **CHANGELOG `[Unreleased]`** — proactive Phase 12 entry. Phase 11's lessons-learned (retroactive CHANGELOG catch by the documenter) should not repeat in Phase 12.

**Architect-discretion locked:**

- **`RELEASING.md` is a NEW file** at repo root. Not a section in CONTRIBUTING.md (which is contributor-facing, not operator-facing). Not a section in the migration doc (which is migration-from-legacy-facing, not release-facing). The release flow is its own concern.
- **CHANGELOG entry under `[Unreleased]`**, not under a new `[1.0.0]` heading. The `v1.0.0` tag push triggers GHCR build; the CHANGELOG `[1.0.0]` heading promotion is the operator's manual step (documented in `RELEASING.md` step list).
- **README section is APPEND-only.** Locate the existing structure and add the migration section in a sensible location (likely after Configuration, before Adding-a-plugin); do NOT rewrite or restructure the README.
- **No `CODEOWNERS`, no `CODE_OF_CONDUCT.md`, no `architecture.md`, no `operations-guide.md`** (D4). This plan adds exactly three files / sections: README append, `RELEASING.md`, CHANGELOG append.

## Dependencies

- **PLAN-1.3** — produces `tools/FrigateRelay.MigrateConf/`; the README link must point to it.
- **PLAN-1.4** — produces `docs/migration-from-frigatemqttprocessing.md`; README links to it.
- **PLAN-3.1** — produces `docs/parity-report.md`; `RELEASING.md` references "operator MUST review parity report before tagging".
- **Wave 3 file-disjoint with PLAN-3.1.** PLAN-3.2 owns ONLY `README.md`, `RELEASING.md`, `CHANGELOG.md`. PLAN-3.1 owns the tool + docs/parity-report.md. No overlap.

## Tasks

### Task 1: Append "Migrating from FrigateMQTTProcessingService" section to `README.md`

**Files:**
- `README.md` (modify — append a new `##` section)

**Action:** modify

**Description:**

1. Builder MUST first `grep -n '^## ' README.md` to map the existing section structure. Per RESEARCH §9 the README has Quickstart / Configuration / Adding-a-plugin etc. Insert the new section AFTER the Configuration section and BEFORE the Adding-a-plugin section (operators read top-to-bottom; migration is more relevant than plugin authoring for a v1.0.0 user).

2. New section content:

```markdown
## Migrating from FrigateMQTTProcessingService

If you are migrating from the author's earlier `FrigateMQTTProcessingService`
(.NET Framework 4.8 / Topshelf / SharpConfig INI) to FrigateRelay v1.0.0, the
project ships a one-shot conversion tool plus a field-by-field mapping doc:

- **Tool:** [`tools/FrigateRelay.MigrateConf/`](tools/FrigateRelay.MigrateConf/) — a .NET 10 console app that reads the legacy `.conf` and writes a FrigateRelay-shaped `appsettings.Local.json`.
  ```bash
  dotnet run --project tools/FrigateRelay.MigrateConf -c Release -- \
    --input /path/to/FrigateMQTTProcessingService.conf \
    --output appsettings.Local.json
  ```
- **Field-by-field mapping:** [`docs/migration-from-frigatemqttprocessing.md`](docs/migration-from-frigatemqttprocessing.md) — covers `[ServerSettings]`, `[PushoverSettings]`, `[SubscriptionSettings]` blocks; documents the secrets you must supply via env vars (`Pushover__AppToken`, `Pushover__UserKey`); explains the deliberately-dropped per-subscription `Camera` URL field.
- **Side-by-side parity window (recommended):** [`docs/parity-window-checklist.md`](docs/parity-window-checklist.md) — the 48-hour run book for verifying behavioral parity in DryRun mode before flipping to production.
- **Parity report:** [`docs/parity-report.md`](docs/parity-report.md) — the reconciliation output the operator reviews before declaring cutover.
```

**Acceptance Criteria:**
- `grep -q '^## Migrating from FrigateMQTTProcessingService' README.md`
- `grep -q 'tools/FrigateRelay.MigrateConf' README.md`
- `grep -q 'docs/migration-from-frigatemqttprocessing.md' README.md`
- `grep -q 'docs/parity-window-checklist.md' README.md`
- `grep -q 'docs/parity-report.md' README.md`
- README still parses as valid markdown (no broken section delimiters): `awk '/^## /{print}' README.md | wc -l` matches the pre-plan count + 1.
- `grep -nE '192\.168\.|10\.0\.0\.' README.md` returns zero matches.

### Task 2: Create `RELEASING.md` (operator's v1.0.0 release run book)

**Files:**
- `RELEASING.md` (create)

**Action:** create

**Description:**

Author at repo root. CONTEXT-12 D7 mandates this is the place where the manual `git tag v1.0.0 && git push --tags` lives. Content structure:

1. **Pre-release checklist** — every box must be checked before tagging:
   - [ ] All Phase 12 plans complete (`/shipyard:status` shows phase-12 closed)
   - [ ] `dotnet build FrigateRelay.sln -c Release` clean (warnings-as-errors)
   - [ ] `.github/scripts/run-tests.sh` exits 0 (every test project green)
   - [ ] `.github/scripts/secret-scan.sh` exits 0
   - [ ] `docs/parity-report.md` populated (NOT the template) AND shows zero missed AND zero spurious alerts (per ROADMAP Phase 12 success criterion); OR every discrepancy is documented as an intentional improvement
   - [ ] Operator has removed `DryRun: true` and `Logging:File:CompactJson: true` from production `appsettings.Local.json` (per `docs/parity-window-checklist.md` close-out)
   - [ ] `CHANGELOG.md` `[Unreleased]` entry promoted to `[1.0.0] — YYYY-MM-DD` heading

2. **CHANGELOG promotion step** — exact `sed`-or-manual-edit instructions:
   ```bash
   # In CHANGELOG.md, replace the [Unreleased] heading with [1.0.0] — YYYY-MM-DD,
   # and add a new empty [Unreleased] section above it.
   ```

3. **Tag + push** — the actual cutover commands:
   ```bash
   git status                              # confirm clean working tree
   git log --oneline -1                    # confirm HEAD is the right commit
   git tag -a v1.0.0 -m "v1.0.0 — initial public release"
   git push origin v1.0.0
   ```

4. **What happens automatically** — operator-readable explanation of `release.yml`:
   - The push of the `v*` tag triggers `.github/workflows/release.yml` (Phase 10).
   - That workflow builds a `linux/amd64` smoke image, starts FrigateRelay against a Mosquitto sidecar, polls `/healthz` for 30s; if healthy, builds + pushes multi-arch (`linux/amd64` + `linux/arm64`) images to `ghcr.io/<owner>/frigaterelay:1.0.0`, `:1`, `:latest`. **If the smoke gate fails, the multi-arch push does NOT happen** and the operator must investigate before re-tagging.

5. **Post-release verification:**
   ```bash
   # GHCR images exist
   docker pull ghcr.io/<owner>/frigaterelay:1.0.0   # operator substitutes <owner> from `git remote -v`
   docker pull ghcr.io/<owner>/frigaterelay:latest

   # Quick smoke against the published image
   docker run --rm -e FrigateMqtt__Server=localhost ghcr.io/<owner>/frigaterelay:1.0.0 --version 2>/dev/null || true
   ```

6. **Rollback** — single paragraph: if the parity report uncovers a regression after the tag is pushed, the operator deletes the tag (`git tag -d v1.0.0 && git push --delete origin v1.0.0`) — this does NOT delete the GHCR images, but flagged-known-broken images can be removed via the GitHub Packages UI or `gh api -X DELETE`. **The tag is cheap; cut a v1.0.1 with the fix rather than re-using v1.0.0** (semver immutability).

7. **ID-24 callout (optional, advisory):** the existing `release.yml` pins GitHub Action versions by tag, not SHA (per RESEARCH §10). This is a pre-v1 hardening advisory; the operator can defer to a v1.0.1 minor pass.

**Acceptance Criteria:**
- `test -f RELEASING.md`
- `grep -q 'git tag -a v1.0.0' RELEASING.md`
- `grep -q 'git push origin v1.0.0' RELEASING.md`
- `grep -q 'docs/parity-report.md' RELEASING.md`
- `grep -q 'ghcr.io/<owner>/frigaterelay' RELEASING.md` (placeholder, not a hard-coded slug — operator substitutes)
- `grep -q '\.github/workflows/release.yml' RELEASING.md`
- `grep -nE '192\.168\.|10\.0\.0\.' RELEASING.md` returns zero matches.
- `.github/scripts/secret-scan.sh` exits 0.

### Task 3: Add Phase 12 entry to `CHANGELOG.md` `[Unreleased]`

**Files:**
- `CHANGELOG.md` (modify — append entries under existing `[Unreleased]` heading)

**Action:** modify

**Description:**

Builder MUST first `grep -n '^## \[' CHANGELOG.md` to find the `[Unreleased]` heading (Phase 11 created it). Append entries under `### Added` / `### Changed` (create the subheadings if they don't exist) describing Phase 12's deliverables, NOT promoting `[Unreleased]` to `[1.0.0]` (the operator does that in `RELEASING.md` Task 2 step 1).

```markdown
### Added (Phase 12)

- `BlueIris:DryRun` and `Pushover:DryRun` per-action config flags. When `true`, the action plugin emits a structured `would-execute` log entry (EventId `BlueIrisDryRun` / `PushoverDryRun`) and returns success without firing the external API. Used during the parity-window for logging-only side-by-side runs against the legacy service.
- `tools/FrigateRelay.MigrateConf/` — .NET 10 console app that converts a legacy `FrigateMQTTProcessingService.conf` (SharpConfig INI) to a FrigateRelay-shaped `appsettings.Local.json`. Subcommands: `migrate` (default) and `reconcile`.
- `tools/FrigateRelay.MigrateConf/` reconcile subcommand — pairs FrigateRelay NDJSON audit logs against a legacy CSV and produces `docs/parity-report.md` (counts, missed alerts, spurious alerts).
- `tests/FrigateRelay.MigrateConf.Tests/` — round-trip + reconcile coverage.
- `docs/migration-from-frigatemqttprocessing.md` — INI → JSON field-by-field mapping.
- `docs/parity-window-checklist.md` — operator run book for the 48h side-by-side parity window.
- `docs/parity-report.md` — parity-window reconciliation output (template; populated by the operator after the window closes).
- `RELEASING.md` — manual v1.0.0 release run book.
- README "Migrating from FrigateMQTTProcessingService" section.

### Changed (Phase 12)

- `FrigateRelay.Host` rolling file sink (`logs/frigaterelay-.log`) gains an opt-in `Logging:File:CompactJson` config key. When `true`, the file sink uses `Serilog.Formatting.Compact.CompactJsonFormatter` (NDJSON) for audit-log parseability. Default `false` — text format unchanged for production users.
```

Tone: factual, past-tense, scoped to Phase 12. Match the existing CHANGELOG style for consistency (builder MUST sample 5-10 lines of pre-existing entries before authoring).

**Acceptance Criteria:**
- `grep -q '\[Unreleased\]' CHANGELOG.md` AND the `[Unreleased]` heading is still present (NOT promoted).
- `grep -q 'BlueIris:DryRun' CHANGELOG.md`
- `grep -q 'tools/FrigateRelay.MigrateConf' CHANGELOG.md`
- `grep -q 'Logging:File:CompactJson' CHANGELOG.md`
- `grep -q 'docs/parity-window-checklist.md' CHANGELOG.md`
- `grep -q 'RELEASING.md' CHANGELOG.md`
- The CHANGELOG continues to parse as valid markdown.
- `grep -nE '192\.168\.|10\.0\.0\.' CHANGELOG.md` returns zero matches.

## Verification

```bash
# 1. README has the migration section + all four links
grep -q '^## Migrating from FrigateMQTTProcessingService' README.md
grep -q 'tools/FrigateRelay.MigrateConf' README.md
grep -q 'docs/migration-from-frigatemqttprocessing.md' README.md
grep -q 'docs/parity-window-checklist.md' README.md
grep -q 'docs/parity-report.md' README.md

# 2. RELEASING.md exists with the manual tag commands
test -f RELEASING.md
grep -q 'git tag -a v1.0.0' RELEASING.md
grep -q 'git push origin v1.0.0' RELEASING.md
grep -q '\.github/workflows/release.yml' RELEASING.md

# 3. CHANGELOG entry under [Unreleased]
grep -q '\[Unreleased\]' CHANGELOG.md
grep -q 'BlueIris:DryRun' CHANGELOG.md
grep -q 'Logging:File:CompactJson' CHANGELOG.md

# 4. No regressions
dotnet build FrigateRelay.sln -c Release
.github/scripts/run-tests.sh --no-build

# 5. Secret-scan stays clean
.github/scripts/secret-scan.sh
```
