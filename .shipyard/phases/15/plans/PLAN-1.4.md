---
phase: 15-v1.2.1-hardening
plan: 1.4
wave: 1
dependencies: []
must_haves:
  - mosquitto-smoke.conf has a prominent multi-line WARNING header
  - docker-compose.example.yml ports stanza recommends 127.0.0.1 binding for untrusted networks
files_touched:
  - docker/mosquitto-smoke.conf
  - docker/docker-compose.example.yml
  - CHANGELOG.md
tdd: false
risk: low
---

# Plan 1.4: Docker operator-doc hygiene (#25, #26)

## Context

Two documentation-only edits with zero behavioral impact. #25 elevates the existing single-line "Do not deploy this in production" comment in `docker/mosquitto-smoke.conf` to a prominent multi-line `# WARNING` header so an operator copying the file as a starting point cannot miss the "anonymous-only" character (RESEARCH.md §1 #25). #26 adds a comment in `docker/docker-compose.example.yml` recommending `127.0.0.1:8080:8080` binding for untrusted networks (RESEARCH.md §1 #26 — there is no existing port-binding security comment to duplicate; the file has a clean slate). Both files are sole-owned by this plan; no overlap with PLAN-1.1, PLAN-1.2, or PLAN-1.3.

## Dependencies

None — Wave 1 root.

## Tasks

### Task 1: Mosquitto-smoke.conf WARNING header (#25)
**Files:** `docker/mosquitto-smoke.conf`
**Action:** modify
**Description:**
Per RESEARCH.md §1 #25, the file is currently 4 lines: 2 understated comment lines + 2 functional lines (`listener 1883 0.0.0.0`, `allow_anonymous true`). Replace the 2 comment lines with a prominent multi-line `# WARNING` block that:
1. Opens with a banner line of `#` characters (e.g. `# ============================================================`).
2. Contains a `# WARNING:` line.
3. States explicitly that the broker is anonymous-only and listens on all interfaces (`0.0.0.0`).
4. States explicitly that it is intended ONLY for the GitHub Actions `release.yml` smoke step.
5. States explicitly "DO NOT use this configuration in any non-CI environment".
6. References `.github/workflows/release.yml` by path so a curious operator can find the consumer.
7. Closes with the same banner line.

The functional lines (`listener 1883 0.0.0.0` + `allow_anonymous true`) are unchanged. Mosquitto ignores comment lines; behavior is unaffected. Header should be ≥ 6 lines and ≤ 12 lines so it is visually unmissable but not noisy.

**TDD:** false (documentation-only).

**Acceptance Criteria:**
- `docker/mosquitto-smoke.conf` opens with a banner-style `# WARNING` block of ≥ 6 lines.
- `head -n 1 docker/mosquitto-smoke.conf` shows a `#`-banner line.
- `grep -c '^# WARNING' docker/mosquitto-smoke.conf` returns ≥ 1.
- `grep -F 'release.yml' docker/mosquitto-smoke.conf` returns ≥ 1 match.
- `grep -F 'allow_anonymous true' docker/mosquitto-smoke.conf` still returns 1 match (functional config preserved).
- A subsequent `release.yml` smoke run still passes (broker still starts, `/healthz` still returns 200) — operator-validated, not part of this plan's automated check.

### Task 2: docker-compose.example.yml localhost binding recommendation (#26) + CHANGELOG entries (#25, #26)
**Files:** `docker/docker-compose.example.yml`, `CHANGELOG.md`
**Action:** modify
**Description:**
Per RESEARCH.md §1 #26, the example file's ports stanza at lines 21–22 currently reads:

```yaml
    ports:
      - "8080:8080"
    # healthcheck inherited from the image's HEALTHCHECK directive.
```

Add a comment IMMEDIATELY ABOVE the `- "8080:8080"` line that:
1. Notes the default binding is `0.0.0.0` (publicly reachable on the Docker host).
2. Recommends `- "127.0.0.1:8080:8080"` for untrusted networks (loopback-only).
3. Is brief — ≤ 3 comment lines so the YAML stays readable.

Do NOT change the default value `"8080:8080"` — comment only (per RESEARCH.md §1 #26 explicit constraint). The unrelated `# healthcheck inherited...` comment at line 22 remains in place.

**CHANGELOG.md:** Append two `[Unreleased]` `### Documentation` (or `### Security`) lines:
- `- #25 — Prominent WARNING header on \`docker/mosquitto-smoke.conf\` clarifying CI-only / anonymous-only character.`
- `- #26 — \`docker/docker-compose.example.yml\` recommends \`127.0.0.1:8080:8080\` binding for untrusted networks.`

**TDD:** false (documentation-only).

**Acceptance Criteria:**
- The new comment appears IMMEDIATELY above the `- "8080:8080"` line in `docker/docker-compose.example.yml`.
- The comment includes the literal string `127.0.0.1:8080:8080` so an operator can copy-paste.
- The default `- "8080:8080"` line is unchanged.
- `docker compose -f docker/docker-compose.example.yml config` parses successfully (YAML still valid; comments don't break parse).
- `CHANGELOG.md` `[Unreleased]` lists `#25` and `#26`.

## Verification

```bash
# #25 — header in place + functional config preserved
head -n 12 docker/mosquitto-smoke.conf
grep -c '^# WARNING' docker/mosquitto-smoke.conf      # >= 1
grep -F 'release.yml' docker/mosquitto-smoke.conf     # at least 1 match
grep -F 'allow_anonymous true' docker/mosquitto-smoke.conf  # exactly 1 match

# #26 — comment in place + YAML still valid
grep -B1 -A1 -F '"8080:8080"' docker/docker-compose.example.yml
docker compose -f docker/docker-compose.example.yml config > /dev/null

# CHANGELOG entries present
grep -nE '#25|#26' CHANGELOG.md
```
