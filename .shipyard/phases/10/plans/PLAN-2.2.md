---
phase: 10-docker-release
plan: 2.2
wave: 2
dependencies: [1.1, 1.2, 1.3, 2.1]
must_haves:
  - .github/workflows/release.yml triggers on tag v*, builds amd64+arm64, runs Mosquitto-sidecar smoke, then pushes to GHCR
  - Smoke step FAILS the workflow if /healthz != 200 within 30s; dumps `docker logs fr` on failure
  - Jenkinsfile SDK image digest-pinned
  - .github/dependabot.yml gains `docker` ecosystem watching docker/Dockerfile
files_touched:
  - .github/workflows/release.yml
  - .github/dependabot.yml
  - Jenkinsfile
tdd: false
risk: high
---

# Plan 2.2: release.yml + Mosquitto smoke + Jenkinsfile/Dependabot digest discipline

## Context

Closes the Phase 10 release-engineering surface: the multi-arch GHA workflow that publishes images to GHCR, the Mosquitto-sidecar smoke gate that prevents broken images from being pushed (CONTEXT-10 D5), and the supply-chain hygiene items B2 (Dependabot docker ecosystem) + B3 (Jenkinsfile SDK digest pin).

Mosquitto smoke architecture (R2 resolution): bind-mount `docker/mosquitto-smoke.conf` (created in PLAN-2.1) into an `eclipse-mosquitto:2` sidecar container started inline in the workflow step (NOT as a `services:` block, because `services:` containers cannot bind-mount workspace files cleanly). The FrigateRelay container then runs on `--network host` with `FrigateMqtt__Server=127.0.0.1` to keep the broker hostname trivially resolvable.

## Dependencies

All of Wave 1 + PLAN-2.1 (needs the Dockerfile and `mosquitto-smoke.conf` to exist).

## Tasks

### Task 1: .github/workflows/release.yml — multi-arch build + smoke + push

**Files:**
- `.github/workflows/release.yml` (create)

**Action:** create

**Description:**
Workflow shape:

```yaml
name: release

on:
  push:
    tags: [ "v*" ]

permissions:
  contents: read
  packages: write

jobs:
  release:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - uses: docker/setup-qemu-action@v4
      - uses: docker/setup-buildx-action@v4

      - uses: docker/login-action@v4
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}

      - id: meta
        uses: docker/metadata-action@v6
        with:
          images: ghcr.io/${{ github.repository_owner }}/frigaterelay
          tags: |
            type=semver,pattern={{version}}
            type=semver,pattern={{major}}
            type=raw,value=latest

      # Build amd64 ONLY for smoke — load into local Docker
      - name: Build amd64 for smoke
        uses: docker/build-push-action@v7
        with:
          context: .
          file: docker/Dockerfile
          platforms: linux/amd64
          load: true
          tags: fr-smoke:local

      - name: Start Mosquitto sidecar
        run: |
          docker run -d --name mosquitto --network host \
            -v "$PWD/docker/mosquitto-smoke.conf:/mosquitto/config/mosquitto.conf:ro" \
            eclipse-mosquitto:2

      - name: Start FrigateRelay
        run: |
          docker run -d --name fr --network host \
            -e ASPNETCORE_ENVIRONMENT=Docker \
            -e ASPNETCORE_URLS=http://+:8080 \
            -e FrigateMqtt__Server=127.0.0.1 \
            -e FrigateMqtt__Port=1883 \
            -v "$PWD/docker/appsettings.Smoke.json:/app/appsettings.Local.json:ro" \
            fr-smoke:local

      - name: Smoke /healthz
        run: |
          set -e
          for i in $(seq 1 30); do
            if curl -fsS http://localhost:8080/healthz; then
              echo
              echo "healthz OK after ${i}s"
              exit 0
            fi
            sleep 1
          done
          echo "healthz did not return 200 within 30s"
          echo "--- FrigateRelay logs ---"
          docker logs fr || true
          echo "--- Mosquitto logs ---"
          docker logs mosquitto || true
          exit 1

      - name: Cleanup smoke containers
        if: always()
        run: |
          docker rm -f fr mosquitto || true

      # Multi-arch build + push (only reached if smoke passed)
      - name: Build and push multi-arch
        uses: docker/build-push-action@v7
        with:
          context: .
          file: docker/Dockerfile
          platforms: linux/amd64,linux/arm64
          push: true
          tags: ${{ steps.meta.outputs.tags }}
          labels: ${{ steps.meta.outputs.labels }}
```

Critical invariants encoded:
- Smoke is HARD-FAIL (`exit 1`), NOT warn-and-proceed (per orchestrator hard-constraint #10).
- Smoke runs ONLY on amd64 (arm64 emulated build is too slow to run here per CONTEXT-10 D5).
- FrigateMqtt__Server is `127.0.0.1` (localhost), NOT a hard-coded RFC-1918 IP.
- `secrets.GITHUB_TOKEN` is the only secret used; no PAT.

**Acceptance Criteria:**
- `test -f .github/workflows/release.yml`
- `grep -q "tags: \[ \"v\*\" \]" .github/workflows/release.yml`
- `grep -q 'platforms: linux/amd64,linux/arm64' .github/workflows/release.yml`
- `grep -q 'docker logs fr' .github/workflows/release.yml`
- `grep -q 'exit 1' .github/workflows/release.yml`
- `grep -nE '192\.168\.|10\.[0-9]+\.[0-9]+\.[0-9]+\.[0-9]' .github/workflows/release.yml` returns zero matches.
- `python3 -c "import yaml; yaml.safe_load(open('.github/workflows/release.yml'))"` exits 0 (valid YAML).
- (If `actionlint` available) `actionlint .github/workflows/release.yml` exits 0.

### Task 2: Pin Jenkinsfile SDK image to digest

**Files:**
- `Jenkinsfile` (modify — line 32: `image 'mcr.microsoft.com/dotnet/sdk:10.0'` -> `image 'mcr.microsoft.com/dotnet/sdk:10.0@sha256:<DIGEST>'`. Add a comment immediately above the line: `// Digest pin: bump manually when Dependabot bumps docker/Dockerfile (Jenkinsfile is not in the Dependabot docker watch).`)

**Action:** modify

**Description:**
Per CONTEXT-10 B3 + researcher C4 — Jenkinsfile is a Groovy file and is NOT auto-watched by Dependabot's `docker` ecosystem (which scans `Dockerfile`-named files). A documented manual-bump convention (the comment) is the chosen trade-off vs. a wrapper Dockerfile stub.

Builder fetches the digest the same way as the Dockerfile pin (PLAN-2.1):

```bash
docker pull mcr.microsoft.com/dotnet/sdk:10.0
docker inspect mcr.microsoft.com/dotnet/sdk:10.0 --format '{{index .RepoDigests 0}}'
```

The file MUST contain a real `sha256:...` digest, not a `<FILL_IN>` placeholder.

**Acceptance Criteria:**
- `grep -E 'mcr\.microsoft\.com/dotnet/sdk:10\.0@sha256:[a-f0-9]{64}' Jenkinsfile` returns one match.
- `grep -c 'mcr\.microsoft\.com/dotnet/sdk:10\.0' Jenkinsfile` returns exactly `1` (no un-pinned reference left behind).
- `grep -B1 'sdk:10.0@sha256' Jenkinsfile | grep -q 'Digest pin'` (the explanatory comment is present).

### Task 3: Add docker ecosystem to .github/dependabot.yml

**Files:**
- `.github/dependabot.yml` (modify — add a third `package-ecosystem` block for `docker`, directory `/docker`, weekly Monday cadence matching the existing nuget + github-actions blocks)

**Action:** modify

**Description:**
Append (do not replace existing blocks):

```yaml
  - package-ecosystem: "docker"
    directory: "/docker"
    schedule:
      interval: "weekly"
      day: "monday"
    commit-message:
      prefix: "chore(deps)"
```

This watches `docker/Dockerfile` for both tag and digest updates (Dependabot rewrites both atomically when both are present). Jenkinsfile is intentionally NOT watched here — the comment in PLAN-2.2 Task 2 documents the manual-bump convention.

**Acceptance Criteria:**
- `grep -q 'package-ecosystem: "docker"' .github/dependabot.yml`
- `grep -A3 'package-ecosystem: "docker"' .github/dependabot.yml | grep -q 'directory: "/docker"'`
- `python3 -c "import yaml; yaml.safe_load(open('.github/dependabot.yml'))"` exits 0.
- `grep -c 'package-ecosystem' .github/dependabot.yml` returns `3` (nuget + github-actions + docker).

## Verification

Run from repo root:

```bash
# YAML validity
python3 -c "import yaml; yaml.safe_load(open('.github/workflows/release.yml'))"
python3 -c "import yaml; yaml.safe_load(open('.github/dependabot.yml'))"

# Digest pin discipline
grep -qE 'mcr\.microsoft\.com/dotnet/sdk:10\.0@sha256:[a-f0-9]{64}' Jenkinsfile
grep -qE 'mcr\.microsoft\.com/dotnet/runtime-deps:10\.0-alpine@sha256:[a-f0-9]{64}' docker/Dockerfile

# Smoke step is hard-fail and references docker logs
grep -q 'docker logs fr' .github/workflows/release.yml
grep -q 'exit 1' .github/workflows/release.yml

# Multi-arch + tag-trigger discipline
grep -q 'platforms: linux/amd64,linux/arm64' .github/workflows/release.yml
grep -q 'tags: \[ "v\*" \]' .github/workflows/release.yml

# Dependabot docker block
grep -c 'package-ecosystem' .github/dependabot.yml | grep -q '^3$'

# No hard-coded IPs anywhere new
git grep -nE '192\.168\.|10\.[0-9]+\.[0-9]+\.[0-9]+\.[0-9]' .github/workflows/release.yml docker/ && exit 1 || true
```
