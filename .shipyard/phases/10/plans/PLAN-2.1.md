---
phase: 10-docker-release
plan: 2.1
wave: 2
dependencies: [1.1, 1.2, 1.3]
must_haves:
  - Multi-stage Dockerfile on mcr.microsoft.com/dotnet/runtime-deps:10.0-alpine (digest-pinned)
  - Non-root user UID 10001
  - HEALTHCHECK using wget --spider against /healthz on EXPOSE 8080
  - docker-compose.example.yml — FrigateRelay only, no broker bundled, env_file pattern
  - .env.example with all expected env-var bindings documented
files_touched:
  - docker/Dockerfile
  - docker/docker-compose.example.yml
  - docker/.env.example
  - docker/mosquitto-smoke.conf
tdd: false
risk: medium
---

# Plan 2.1: Dockerfile + non-root user + compose example + .env.example

## Context

Implements CONTEXT-10 D1 (Alpine base), D3 (self-contained publish, multi-stage build), D6 (compose scope = FrigateRelay only), B3 (digest pinning), and the non-root container user that pairs with PLAN-1.2's path validation to close ID-21. Depends on PLAN-1.1 (`/healthz` endpoint exists for `HEALTHCHECK`), PLAN-1.2 (path validation closes the non-root residual risk), and PLAN-1.3 (publish flags allow `dotnet publish --self-contained`).

Also produces `docker/mosquitto-smoke.conf` here (rather than in PLAN-2.2) because it's a Docker-domain artifact and keeps `release.yml` lean.

## Dependencies

All of Wave 1 (PLAN-1.1, PLAN-1.2, PLAN-1.3).

## Tasks

### Task 1: Multi-stage Dockerfile + non-root user + HEALTHCHECK

**Files:**
- `docker/Dockerfile` (create)

**Action:** create

**Description:**
Multi-stage build:

1. **Build stage** — `mcr.microsoft.com/dotnet/sdk:10.0` (Debian-based; cross-compiles to musl via `-r linux-musl-x64` / `-r linux-musl-arm64`). Copy solution, restore, publish self-contained for the target RID. Use `TARGETARCH` BuildKit arg to map to RID:
   - `amd64` -> `linux-musl-x64`
   - `arm64` -> `linux-musl-arm64`
2. **Runtime stage** — `mcr.microsoft.com/dotnet/runtime-deps:10.0-alpine@sha256:<DIGEST>`. Add non-root user UID 10001 GID 10001. Copy publish output to `/app`. `WORKDIR /app`. `USER 10001:10001`. `EXPOSE 8080`. `HEALTHCHECK CMD wget -q --spider http://localhost:8080/healthz || exit 1`. `ENTRYPOINT ["./FrigateRelay.Host"]`.

**Digest pin (R3 instructions for builder):**
The plan does NOT freeze a digest. The builder MUST, before finalizing the Dockerfile, run:

```bash
docker pull mcr.microsoft.com/dotnet/runtime-deps:10.0-alpine
docker inspect mcr.microsoft.com/dotnet/runtime-deps:10.0-alpine --format '{{index .RepoDigests 0}}'
```

and paste the resulting `sha256:...` value into both the `FROM ...@sha256:...` line in this Dockerfile AND the SDK image digest in `Jenkinsfile` (PLAN-2.2 Task 2 covers Jenkinsfile). The builder MUST NOT use a placeholder like `<FILL_IN>` — the file shipped to disk MUST contain a real digest captured at build time. If `docker pull` is not possible in the builder's environment, the build MUST fail loudly rather than ship a placeholder.

Comment block at top of Dockerfile (CONTEXT-10 D1 fallback documentation):

```
# Base: Alpine (musl libc) for smallest footprint (~80-90 MB target).
# Fallback: if a runtime issue surfaces with MQTTnet sockets or OTel HTTP/2,
# swap to mcr.microsoft.com/dotnet/runtime-deps:10.0-bookworm-slim and change the
# RID flags from linux-musl-x64/arm64 to linux-x64/arm64. No other changes needed.
```

Also document the `OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf` opt-out for Alpine HTTP/2 edge cases as a one-line `# ENV` hint in the runtime stage.

**Acceptance Criteria:**
- `test -f docker/Dockerfile`
- `grep -q 'FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-alpine@sha256:[a-f0-9]\{64\}' docker/Dockerfile` (real digest, not a placeholder)
- `grep -q 'USER 10001' docker/Dockerfile`
- `grep -q 'EXPOSE 8080' docker/Dockerfile`
- `grep -q 'HEALTHCHECK' docker/Dockerfile && grep -q 'wget' docker/Dockerfile && grep -q '/healthz' docker/Dockerfile`
- `grep -nE '192\.168\.|10\.[0-9]+\.[0-9]+\.[0-9]+' docker/Dockerfile` returns zero matches.
- `docker build --platform linux/amd64 -f docker/Dockerfile -t fr-test:amd64 .` exits 0.
- `docker image inspect fr-test:amd64 --format '{{.Size}}'` is less than 125829120 (120 MB ceiling per ROADMAP).

### Task 2: docker-compose.example.yml — FrigateRelay only

**Files:**
- `docker/docker-compose.example.yml` (create)
- `docker/.env.example` (create)

**Action:** create

**Description:**
Per CONTEXT-10 D6: ONE service (`frigaterelay`), no broker bundled, env_file pattern for secrets, `FrigateMqtt__Server` pointing at a service name (NOT an IP) in the comment block.

`docker/docker-compose.example.yml`:

```yaml
# FrigateRelay — example Docker Compose.
# This file does NOT bundle a broker. Point FrigateMqtt__Server at your
# existing Mosquitto / Frigate broker (e.g. a service name on the same
# Docker network, or a hostname reachable from this container).
# Copy .env.example to .env and fill in the secrets before `docker compose up`.

services:
  frigaterelay:
    image: ghcr.io/<owner>/frigaterelay:latest
    restart: unless-stopped
    env_file:
      - .env
    environment:
      ASPNETCORE_ENVIRONMENT: Docker
      ASPNETCORE_URLS: http://+:8080
      FrigateMqtt__Server: mosquitto         # service name on YOUR network
      FrigateMqtt__Port: "1883"
    volumes:
      - ./config/appsettings.Local.json:/app/appsettings.Local.json:ro
    ports:
      - "8080:8080"
    healthcheck:
      test: ["CMD", "wget", "-q", "--spider", "http://localhost:8080/healthz"]
      interval: 30s
      timeout: 5s
      retries: 3
      start_period: 30s
```

`docker/.env.example`:

```
# Blue Iris API credentials. Bound to BlueIris:* options class.
BLUEIRIS__BASEURL=http://blueiris.lan:81
BLUEIRIS__USERNAME=
BLUEIRIS__PASSWORD=

# Pushover credentials. Bound to Pushover:* options class.
PUSHOVER__APITOKEN=
PUSHOVER__USERKEY=

# Optional: OpenTelemetry OTLP exporter endpoint.
# OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector.lan:4317
# OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf   # uncomment if HTTP/2 issues on Alpine
```

The `<owner>` placeholder in `image:` is intentional — operators fork-and-customize. Do NOT use a hard-coded GHCR org.

**Acceptance Criteria:**
- `test -f docker/docker-compose.example.yml && test -f docker/.env.example`
- `grep -nE '192\.168\.|10\.[0-9]+\.[0-9]+\.[0-9]+\.[0-9]' docker/docker-compose.example.yml docker/.env.example` returns zero matches.
- `grep -nE '^[A-Z_]+=[^[:space:]]+' docker/.env.example | grep -v '=$'` returns zero matches (every var has empty value or is commented — no real secrets).
- `grep -q 'env_file' docker/docker-compose.example.yml`
- `grep -q 'FrigateMqtt__Server: mosquitto' docker/docker-compose.example.yml`
- `docker compose -f docker/docker-compose.example.yml config` exits 0 (compose file is syntactically valid).

### Task 3: docker/mosquitto-smoke.conf

**Files:**
- `docker/mosquitto-smoke.conf` (create)

**Action:** create

**Description:**
Per researcher R2 — `eclipse-mosquitto:2` rejects anonymous connections by default with no listener configured. PLAN-2.2's `release.yml` smoke step bind-mounts this file into the Mosquitto sidecar. Architect chose the explicit-config approach (not the bundled `/mosquitto-no-auth.conf`) because it's self-documenting and lives next to the compose example for parity.

```
# Smoke-test Mosquitto config — used ONLY by .github/workflows/release.yml
# during the post-build /healthz smoke step. Do not deploy this in production.
listener 1883 0.0.0.0
allow_anonymous true
```

**Acceptance Criteria:**
- `test -f docker/mosquitto-smoke.conf`
- `grep -q 'listener 1883' docker/mosquitto-smoke.conf`
- `grep -q 'allow_anonymous true' docker/mosquitto-smoke.conf`

## Verification

Run from repo root (Docker daemon required):

```bash
docker build --platform linux/amd64 -f docker/Dockerfile -t fr-test:amd64 .
SIZE=$(docker image inspect fr-test:amd64 --format '{{.Size}}')
test "$SIZE" -lt 125829120 || { echo "Image too large: $SIZE bytes"; exit 1; }
docker compose -f docker/docker-compose.example.yml config

# Invariants
grep -q 'USER 10001' docker/Dockerfile
grep -q 'sha256:[a-f0-9]\{64\}' docker/Dockerfile
git grep -nE '192\.168\.|10\.[0-9]+\.[0-9]+\.[0-9]+\.[0-9]' docker/ && exit 1 || true

# Smoke runs end-to-end (manual sanity, optional pre-push):
docker run --rm -d --name fr-smoke -p 18080:8080 \
  -v "$PWD/docker/appsettings.Smoke.json:/app/appsettings.Local.json:ro" \
  fr-test:amd64
sleep 10
curl -fsS http://localhost:18080/healthz || { docker logs fr-smoke; docker rm -f fr-smoke; exit 1; }
docker rm -f fr-smoke
```
