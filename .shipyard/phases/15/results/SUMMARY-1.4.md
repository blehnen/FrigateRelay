# Build Summary: Plan 1.4 — Docker operator-doc hygiene (#25, #26)

## Status: complete

## Tasks Completed

- **Task 1 — WARNING header on `docker/mosquitto-smoke.conf` (#25)** — complete. Commit `a599863`. Added prominent multi-line `# WARNING: CI-ONLY CONFIGURATION — DO NOT USE IN PRODUCTION` block at the top of the file plus the existing single-line warning, clarifying anonymous-only / bind-all character to operators who might copy the file as a starting template.
- **Task 2 — `127.0.0.1` binding recommendation in `docker/docker-compose.example.yml` (#26)** — complete. Commit `07e23dc`. Added comment at the port-mapping site (`8080:8080`) recommending `127.0.0.1:8080:8080` for untrusted networks. Documentation-only; default `0.0.0.0:8080` binding preserved for the home-lab target audience.

## Files Modified

- `docker/mosquitto-smoke.conf` — multi-line WARNING header added at top (line 2 onwards).
- `docker/docker-compose.example.yml` — comment at line 22 recommending loopback binding.
- `CHANGELOG.md` — 2 new entries under `[Unreleased]` `### Documentation`:
  - `- #25 — Prominent WARNING header on \`docker/mosquitto-smoke.conf\` clarifying CI-only / anonymous-only character.`
  - `- #26 — \`docker/docker-compose.example.yml\` recommends \`127.0.0.1:8080:8080\` binding for untrusted networks.`

## Decisions Made

- WARNING header placed at top of `mosquitto-smoke.conf` for maximum visibility, ahead of the `allow_anonymous true` directive itself.
- Comment-style recommendation (vs. changing the example default to `127.0.0.1`) preserves the home-lab UX while educating operators on the security trade-off.

## Issues Encountered

- `docker compose -f docker/docker-compose.example.yml config` exits 1 due to missing `docker/.env` file — pre-existing condition (the example file requires the operator to supply `.env`). Affects anyone running PLAN-1.4's verification block verbatim. Mitigated by validating YAML syntax independently via `python3 yaml.safe_load`. **Lesson:** Phase 15 verification commands assumed the example compose file was self-validating, which it isn't by design.

## Verification Results

- `grep -n WARNING docker/mosquitto-smoke.conf` → match at line 2: `# WARNING: CI-ONLY CONFIGURATION — DO NOT USE IN PRODUCTION`. ✓
- `grep -n 127.0.0.1 docker/docker-compose.example.yml` → match at line 22: `# On untrusted networks restrict to loopback: - "127.0.0.1:8080:8080"`. ✓
- YAML syntax valid (independent `python3 yaml.safe_load`). ✓
