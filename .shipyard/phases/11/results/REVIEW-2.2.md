# REVIEW-2.2 — README, CONTRIBUTING, CLAUDE.md staleness

**Status:** COMPLETE  
**Commits:** `758a67e` (README), `4a9e86c` (CONTRIBUTING), `1545a94` (CLAUDE.md staleness)  
**Reviewer:** reviewer-2-2  
**Verdict:** PASS with one advisory

---

## Stage 1 — Correctness

### README (758a67e)

| Check | Result |
|-------|--------|
| Zero "File Transfer Server" boilerplate | PASS |
| `docker compose -f docker/docker-compose.example.yml up` present | PASS |
| `.env.example` mentioned | PASS |
| `/healthz` 503→200 semantics documented | PASS (`curl` example shows `503 if MQTT is unreachable`) |
| Forward-ref `templates/FrigateRelay.Plugins.Template/` | PASS |
| Forward-ref `docs/plugin-author-guide.md` | PASS |
| `CHANGELOG.md` cross-reference | PASS |
| `LICENSE` cross-reference | PASS |
| No hard-coded IPs | PASS |
| No real secrets (only placeholder strings) | PASS |

### CONTRIBUTING (4a9e86c)

| Check | Result |
|-------|--------|
| Build command present | PASS |
| `run-tests.sh` wrapper documented | PASS |
| PR checklist present | PASS |
| Secret-scan policy (no hard-coded IPs/secrets) | PASS |
| `SECURITY.md` pointer | PASS |
| `CLAUDE.md` link for full invariant set | PASS |
| `FluentAssertions` license-pin callout | PASS |
| `CapturingLogger` convention documented | PASS |

**Advisory (non-blocking):** CONTRIBUTING line 29 uses `--filter-query` in the single-test example:
```
dotnet run --project tests/FrigateRelay.Host.Tests -c Release -- --filter-query "..."
```
The current CLAUDE.md (on disk) shows `--filter` at line 53. The PLAN-2.2 review instructions specifically flag "note MTP `--filter` not `--filter-query`". However, `--filter-query` is also a valid MTP option (the full predicate form), and it matches the system-prompt version of CLAUDE.md. This is an inconsistency between CLAUDE.md on disk vs system-prompt, not a CONTRIBUTING error — CONTRIBUTING is internally consistent with the system-prompt CLAUDE.md example. No action required unless CLAUDE.md itself is canonically updated to `--filter`.

### CLAUDE.md staleness (1545a94)

| Check | Result |
|-------|--------|
| "pre-implementation" stale text removed | PASS |
| "tag-pinned — digest pin + Dependabot" stale Jenkinsfile blurb removed | PASS |
| `Phase 11` reference added | PASS |
| `digest-pinned` updated Jenkinsfile description present | PASS |
| Diff confined to two target lines | PASS — diff shows exactly 2 lines changed, surrounding content untouched |

---

## Stage 2 — Integration

| Check | Result |
|-------|--------|
| No file overlap with PLAN-2.1 (LICENSE, SECURITY.md, CHANGELOG.md) | PASS |
| No file overlap with PLAN-2.3 (`templates/...`) | PASS |
| No file overlap with PLAN-2.4 (`.github/ISSUE_TEMPLATE/`, `.github/workflows/docs.yml`) | PASS |
| Forward-refs match PLAN-2.3 (`templates/FrigateRelay.Plugins.Template/`) | PASS |
| Forward-refs match PLAN-3.1 (`docs/plugin-author-guide.md`) | PASS (documented forward-ref) |
| Owner/repo placeholder consistency | PASS — both README and CONTRIBUTING use `<owner>/frigaterelay` placeholder; no commit uses real slug `blehnen/FrigateRelay` |
| Secret scan on new files | PASS — `UserKey=your-user-key` is a placeholder, not a real token (`AppToken=[A-Za-z0-9]{20,}` / `UserKey=[A-Za-z0-9]{20,}` grep returns zero matches) |

---

## Findings

**Findings requiring action:** 0  
**Advisories (non-blocking):** 1

- **Advisory:** `--filter-query` in CONTRIBUTING line 29 vs `--filter` in CLAUDE.md line 53 (on disk). Both are valid MTP flags. CONTRIBUTING matches the system-prompt CLAUDE.md example. No regression; no action required unless CLAUDE.md on disk is separately corrected.

---

## Positive Notes

- README is tight and project-specific — no generic boilerplate, correct placeholder usage throughout.
- `/healthz` semantics (503 → 200 progression) are documented exactly as required.
- CONTRIBUTING correctly summarizes the contributor-relevant subset of CLAUDE.md invariants without duplicating it.
- PR checklist is copy-pastable and covers all required gates.
- CLAUDE.md diff is surgical — exactly two lines changed, zero collateral.
- All forward-refs use stable paths that will resolve when PLAN-2.3 and PLAN-3.1 land.
