# Releasing FrigateRelay

This document is the operator's run book for cutting the `v1.0.0` release tag. The actual `git tag` command is a **manual operator step** — not an agent task. Phase 10's `release.yml` workflow fires automatically once the tag is pushed.

## Pre-release checklist

Every item must be checked before running the tag command.

- [ ] All Phase 12 plans complete (`/shipyard:status` shows phase-12 closed)
- [ ] `dotnet build FrigateRelay.sln -c Release` exits 0 (warnings-as-errors; must be clean on both Ubuntu and Windows)
- [ ] `.github/scripts/run-tests.sh` exits 0 (all test projects green)
- [ ] `make verify-observability` exits 0 (reference compose stack boots and Prometheus + Grafana respond healthy). Operator should additionally have a FrigateRelay instance running and emitting to the OTel Collector beforehand to manually confirm a tagged counter sample reaches Prometheus (browse `:9090/graph` for any `frigaterelay_*_total` series).
- [ ] `.github/scripts/secret-scan.sh` exits 0 (no secret-shaped strings in the tree)
- [ ] `docs/parity-report.md` is populated (not the template placeholder) AND shows zero missed alerts AND zero spurious alerts; or every discrepancy is explicitly documented as an intentional behavioral improvement
- [ ] The ≥48-hour parity window has closed and the [`docs/parity-window-checklist.md`](docs/parity-window-checklist.md) close-out steps are complete
- [ ] `DryRun: true` removed from production `appsettings.Local.json` for all action plugins (BlueIris, Pushover)
- [ ] `Logging:File:CompactJson: true` removed from production `appsettings.Local.json` (or set to `false`) — this was enabled for audit-log parseability during the parity window only
- [ ] `CHANGELOG.md` `[Unreleased]` entry promoted to `[1.0.0] — YYYY-MM-DD` (see step below)

## Step 1: Promote CHANGELOG

In `CHANGELOG.md`, replace the `## [Unreleased]` heading with a versioned heading and add a new empty `[Unreleased]` section above it:

```bash
# Manual edit — open CHANGELOG.md and make this change:
#
# Before:
#   ## [Unreleased]
#
# After:
#   ## [Unreleased]
#
#   ## [1.0.0] — 2026-MM-DD
#
# (replace 2026-MM-DD with today's date)
# Move all current [Unreleased] content under the [1.0.0] heading.
```

Then update the comparison link at the bottom of `CHANGELOG.md`:

```
[unreleased]: https://github.com/blehnen/FrigateRelay/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/blehnen/FrigateRelay/releases/tag/v1.0.0
```

Commit the CHANGELOG promotion:

```bash
git add CHANGELOG.md
git commit -m "chore: promote [Unreleased] to [1.0.0] for release"
```

## Step 2: Verify working tree

```bash
git status                    # confirm clean working tree (no uncommitted changes)
git log --oneline -5          # confirm HEAD is the right commit
dotnet build FrigateRelay.sln -c Release   # final clean-build check
.github/scripts/secret-scan.sh             # final secret-scan check
```

## Step 3: Tag and push

```bash
git tag -a v1.0.0 -m "v1.0.0 — initial public release"
git push origin v1.0.0
```

## What happens automatically after the push

Pushing the `v1.0.0` tag triggers `.github/workflows/release.yml` (added in Phase 10). That workflow:

1. Builds a `linux/amd64` smoke image from `docker/Dockerfile`.
2. Starts FrigateRelay against a Mosquitto sidecar and polls `/healthz` for 30 seconds.
3. **If the smoke gate passes:** builds and pushes multi-arch images (`linux/amd64` + `linux/arm64`) to GHCR as `ghcr.io/<owner>/frigaterelay:1.0.0`, `:1`, and `:latest`. (Substitute `<owner>` from `git remote -v`.)
4. **If the smoke gate fails:** the multi-arch push does NOT happen. Investigate the smoke failure, fix the root cause, and re-tag (see rollback below).

Monitor the workflow at `https://github.com/blehnen/FrigateRelay/actions`.

## Step 4: Post-release verification

```bash
# Confirm images were published (substitute <owner> from `git remote -v`)
docker pull ghcr.io/<owner>/frigaterelay:1.0.0
docker pull ghcr.io/<owner>/frigaterelay:latest

# Quick smoke against the published image
docker run --rm ghcr.io/<owner>/frigaterelay:1.0.0 --version 2>/dev/null || true
```

Also confirm the GitHub Release was created by the workflow (or create it manually via the GitHub UI, linking to `CHANGELOG.md`).

## Rollback

If the parity report uncovers a regression after the tag is pushed:

1. Delete the tag locally and remotely:
   ```bash
   git tag -d v1.0.0
   git push --delete origin v1.0.0
   ```
2. Note: deleting the tag does **not** remove already-pushed GHCR images. Flagged broken images can be deleted via the GitHub Packages UI or:
   ```bash
   gh api -X DELETE /user/packages/container/frigaterelay/versions/<version-id>
   ```
3. Fix the regression, update `CHANGELOG.md`, and cut `v1.0.1` rather than reusing `v1.0.0`. Semver requires version identifiers to be immutable once published.

## Advisory: release.yml action pinning (ID-24)

The existing `release.yml` pins GitHub Action versions by tag rather than SHA digest. This is a supply-chain hardening advisory (Low severity). It can be addressed in a v1.0.1 minor pass — it does not block the v1.0.0 cut.
