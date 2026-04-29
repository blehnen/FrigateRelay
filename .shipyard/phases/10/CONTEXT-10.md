# Phase 10 — Discussion Capture

**Phase.** 10 — Dockerfile and Multi-Arch Release Workflow
**Captured.** 2026-04-27
**Source.** Interactive `/shipyard:plan 10` discussion-capture pass.

This file pins the gray areas in the Phase 10 ROADMAP entry before research and planning. Downstream agents (researcher, architect, builder, reviewer) MUST treat the decisions below as inputs, not as questions to re-litigate.

---

## D1 — Base image

**Decision.** **Alpine.** Use `mcr.microsoft.com/dotnet/runtime-deps:10.0-alpine`.

**Rationale.** Smallest footprint (~80–90 MB final image expected) and the ROADMAP's stated lean. The Phase 10 image-size budget is **≤ 120 MB uncompressed** (ROADMAP success criterion); Alpine gives the most headroom.

**Risk + fallback.** musl libc occasionally surfaces issues with MQTTnet (native sockets) and OTel exporter gRPC (HTTP/2). The Phase 10 builder MUST:

1. Verify the image actually starts (Docker smoke step in release workflow — see D5) before publishing.
2. Document the debian-slim fallback path inline in `docker/Dockerfile` as a one-line comment block, so a future operator hitting an Alpine-specific runtime bug knows the escape hatch is `runtime-deps:10.0-bookworm-slim` with no other Dockerfile changes needed.

**Forbidden.** Do not pivot silently to debian-slim mid-build without surfacing the reason in `SUMMARY-W.P.md`. Any pivot triggers an updated ROADMAP entry under "Phase 10 status."

---

## D2 — `/healthz` transport

**Decision.** **ASP.NET Core minimal API.** Add `Microsoft.AspNetCore.App` framework reference + `AddHealthChecks()` + `MapHealthChecks("/healthz")` to `FrigateRelay.Host`.

**Rationale.** Standard HTTP semantics; first-class support in Docker `HEALTHCHECK`, Kubernetes probes, and external monitoring. The marginal image-size cost on top of `runtime-deps` is small (framework already present in the SDK build layer).

**Forbidden.** Do not introduce a raw `TcpListener` health channel. Do not depend on `Microsoft.AspNetCore.Diagnostics.HealthChecks.UI` or any UI package — `/healthz` is machine-consumed only.

**Wiring constraint.** The minimal-API host MUST coexist with the existing `Host.CreateApplicationBuilder(...)` generic-host topology — use `WebApplication.CreateBuilder(...)` with the same configuration/DI graph, OR add a small Kestrel-backed `IHostedService` if the generic host vs WebHost split causes friction. Architect to decide; document the choice in `RESEARCH.md`.

---

## D3 — Publish strategy

**Decision.** **Self-contained, untrimmed.**

`<PublishSingleFile>false</PublishSingleFile>`, `<SelfContained>true</SelfContained>`, `<PublishTrimmed>false</PublishTrimmed>`, `<PublishAot>false</PublishAot>` for `FrigateRelay.Host`.

**Rationale.** Required to land on a `runtime-deps` base (the runtime is bundled into the publish output, not the base image). Reflection-heavy dependencies — MQTTnet (codec discovery), OpenTelemetry SDK (exporter discovery), Serilog (configuration binding), `IConfiguration.Bind`, the `IPluginRegistrar` discovery loop — are all trim-hostile and AOT-hostile. v1 favours correctness over binary size; trimming and AOT are explicitly out of scope.

**Defer.** A future phase MAY revisit `PublishTrimmed=true` once explicit `TrimmerRoots` cover MQTTnet + OTel + Serilog config binding. NativeAOT is deferred indefinitely; the plugin model's reflection assumptions are not AOT-safe.

---

## D4 — `/healthz` semantics

**Decision.** **Single endpoint, ready-state.** `/healthz` returns `200 OK` only when:

1. The Frigate MQTT client reports `IsConnected == true`, AND
2. Every registered `IHostedService` has completed `StartAsync` (host is past `IHostApplicationLifetime.ApplicationStarted`).

Otherwise returns `503 Service Unavailable` with a short JSON body listing which check failed.

**Rationale.** Matches ROADMAP wording ("returns 200 once MQTT is connected and all hosted services are started"). Catches the failure mode the legacy service had — a silent MQTT disconnect with the process still running. Single endpoint keeps Compose `HEALTHCHECK` and the release-workflow smoke step (D5) trivial.

**No split live/ready.** Defer to a later phase if Kubernetes deploys land. v1's primary deploy is Docker Compose, which only consumes a single `HEALTHCHECK`.

**Implementation note.** Use `IHealthCheck` for the MQTT-connected check; subscribe to the existing `FrigateMqttEventSource` connection state OR expose a tiny `IMqttConnectionStatus` service the source updates. Avoid polling — react to the MQTT client's connect/disconnect events.

---

## D5 — Release-time smoke test

**Decision.** **Yes — `docker run` + `/healthz` GET against a Mosquitto sidecar; fail the release if not `200`.**

**Workflow shape.** In `.github/workflows/release.yml`, after the `linux/amd64` image is built (BEFORE multi-arch push):

1. `services: mosquitto` (eclipse-mosquitto:2) sidecar in the job.
2. `docker run -d --name fr --network <job-net> -e FrigateMqtt__Server=mosquitto ... <built-image>`
3. `wait-for-it` style poll: `curl -fsS http://localhost:8080/healthz` with retry loop, max 30 seconds.
4. On success → proceed to multi-arch buildx push. On failure → dump `docker logs fr` to the workflow log, fail the job, do NOT push.

**Rationale.** A musl-vs-glibc issue, a missing native dep, or a misconfigured `EntryPoint` should be caught by CI, not by users pulling `:v1.0.0` from GHCR.

**Smoke runs only on amd64.** ARM64 image is built via QEMU emulation in the same job; smoke-running it under emulation is too slow and noisy. The amd64 smoke is sufficient evidence — both arches share the same .NET binary, same Dockerfile, same runtime-deps base.

---

## D6 — `docker/docker-compose.example.yml` scope

**Decision.** **FrigateRelay only.**

- One service: `frigaterelay`, image `ghcr.io/<owner>/frigaterelay:latest`.
- Mounts `./config/appsettings.Local.json` read-only.
- Reads `BLUEIRIS_*` and `PUSHOVER_*` secrets from `.env` (via `env_file:` directive).
- Comment block at top: "Point `FrigateMqtt__Server` at your existing Mosquitto / Frigate broker. This file does not bundle a broker."

**Rationale.** The user persona is someone already running Frigate (which already has a broker). Bundling Mosquitto duplicates infrastructure they already have; bundling WireMock pollutes the example with test fixtures. Keep the demo aligned with real-world topology.

**`.env.example`** committed alongside the compose file, with placeholder values and a comment block explaining the env-var → option-class binding (`BLUEIRIS__APIKEY` → `BlueIris:ApiKey`, etc.).

---

## Bundled adjacent items (in scope for Phase 10)

The user explicitly opted into bundling these alongside the Docker work. They are **not** Phase 10 stretch goals — they are Phase 10 acceptance items.

### B1 — ID-21 mitigation: Serilog file sink path validation

**Why now.** ID-21 was deferred from Phase 9 with the explicit reactivation trigger "Phase 10 Docker work" because root-in-container amplifies the path-traversal risk. The Phase 10 Dockerfile MUST run as a non-root user (ROADMAP requirement); validating `Serilog:File:Path` for `..` segments + absolute-path constraints closes the residual risk.

**Plan.**
- Add a `ValidateSerilogPath` startup-validation pass following the Phase 8 D7 collect-all pattern (append to `errors`, never throw).
- Reject paths containing `..`, paths beginning with `/` outside an allowlist (`/var/log/frigaterelay/`, `/app/logs/`), and paths beginning with `\\` (UNC).
- Unit tests live in `tests/FrigateRelay.Host.Tests/Configuration/SerilogPathValidationTests.cs`.
- Closes ID-21 in `.shipyard/ISSUES.md` in the same plan that adds the Dockerfile (so the non-root user + path validation ship together).

### B2 — Dependabot `docker` ecosystem

**Plan.** Add a `docker` block to `.github/dependabot.yml` watching `docker/Dockerfile` (and `docker/docker-compose.example.yml` if it pins specific image tags). Weekly Monday cadence to match the existing `nuget` + `github-actions` rhythm.

**Rationale.** Pairs with B3 (digest pinning); without Dependabot, a digest pin becomes a maintenance burden. With Dependabot, the digest bumps appear as PRs the maintainer can review and merge.

### B3 — Tag + digest pinning of the base image

**Plan.** Pin both:
```dockerfile
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-alpine@sha256:<digest>
```

The readable tag (`10.0-alpine`) is for humans; the digest is for supply-chain integrity. Dependabot's `docker` ecosystem rewrites both atomically.

**Rationale.** Strongest supply-chain posture for a v1 OSS release. CLAUDE.md Phase 2 entry deferred this item ("Docker agent `mcr.microsoft.com/dotnet/sdk:10.0` (tag-pinned — digest pin + Dependabot `docker` ecosystem deferred to Phase 10)"); Phase 10 is the natural home.

**Scope.** This phase pins both the **runtime** base in the Dockerfile AND the **SDK** base used in the Jenkinsfile (`mcr.microsoft.com/dotnet/sdk:10.0` → digest-pinned). Dependabot covers both.

### B4 — Container-friendly logging config

**Decision.** Inside the container, write logs to `Console` only — let the container runtime collect stdout. The rolling file sink stays available for non-container deploys but is **off by default in the container**.

**Plan.** Add `docker/appsettings.Docker.json` (mounted via `ASPNETCORE_ENVIRONMENT=Docker` or copied into the image at `/app/`) that overrides `Serilog:WriteTo` to a Console-only sink. Document the override in the README quickstart.

**Rationale.** Writing log files into the container FS defeats `docker logs`, fills the writable layer, and creates path-validation pressure on B1. Console-only is the 12-Factor-App correct answer for containers.

---

## Decisions defaulted (NOT asked, documented for traceability)

The following are not gray areas — they are determined by ROADMAP, PROJECT.md, or unambiguous best practice. Builder may treat as locked.

| Topic | Default | Source |
|---|---|---|
| Multi-arch targets | `linux/amd64` + `linux/arm64` | ROADMAP Phase 10 deliverables |
| Multi-arch build mechanism | `docker/setup-qemu-action` + `docker/setup-buildx-action` | ROADMAP Phase 10 deliverables |
| Image registry | GHCR (`ghcr.io/<owner>/frigaterelay`) | ROADMAP Phase 10 deliverables |
| GHCR visibility | Public | PROJECT.md (MIT, OSS) |
| Release trigger | Push of git tag matching `v*` | ROADMAP Phase 10 deliverables |
| Image tags published | `:<semver>` and `:latest`, plus `:<major>` (e.g. `:v1`) for stability anchors | ROADMAP + standard convention |
| Non-root container user | Dedicated UID/GID (e.g. 10001), `USER` directive in Dockerfile | ROADMAP Phase 10 + B1 dependency |
| Image-size ceiling | ≤ 120 MB uncompressed | ROADMAP Phase 10 success criterion |
| Compose secrets pattern | `.env` file via `env_file:` | ROADMAP + CLAUDE.md secret-handling rules |
| Health-check probe inside Dockerfile `HEALTHCHECK` | `wget -q --spider http://localhost:<port>/healthz \|\| exit 1` (Alpine ships `wget`; `curl` requires a separate package) | Alpine image baseline |
| Self-contained publish RID | `linux-musl-x64` (Alpine) and `linux-musl-arm64` | D1 + D3 combo |

---

## Out of scope for Phase 10

The following are explicitly NOT part of Phase 10 — flag any architect or builder attempt to include them as a deviation.

- **Helm chart, Kubernetes manifests.** v1 deploy is Docker Compose. Defer.
- **`/metrics` Prometheus endpoint.** OTel OTLP push (Phase 9) is the metrics path. Pull-style Prometheus scrape would be a Phase 11+ stretch.
- **Image signing (cosign / sigstore).** Strong supply-chain hygiene but adds a non-trivial GHA workflow + key-management story; defer to a later release.
- **SBOM generation.** Same reasoning as cosign — pair them in a future security-hardening phase.
- **Trimmed or AOT publish.** Per D3.
- **Helm-style templating of the compose file.** Single example file; users fork and customize.
- **In-image Mosquitto / WireMock for demo.** Per D6.
- **Hot-reload of config.** PROJECT.md non-goal; Phase 10 must not introduce a config-watcher.
- **ID-13, ID-14, ID-15, ID-18, ID-19, ID-20, ID-22.** All open issues OTHER than ID-21 stay deferred — bundling them risks scope creep on a release-engineering phase.

---

## Cross-cutting constraints reminder

(Re-stated from CLAUDE.md — Phase 10 must honour these without exception.)

- `git grep ServicePointManager` returns zero in `src/`.
- `git grep -nE '\.(Result|Wait)\(' src/` returns zero in `src/`.
- `git grep -nE 'App\.Metrics|OpenTracing|Jaeger\.' src/` returns zero in `src/`.
- No hard-coded IPs/hostnames in source — including in commented blocks.
- No secrets in committed `appsettings*.json` (or `appsettings.Docker.json` from B4).
- Secret-scan workflow continues to pass.
- Both CI workflows (`ci.yml`) and Jenkinsfile continue to build green; the new test project (if Phase 10 adds one for healthz/serilog-path) MUST be added to BOTH `.github/scripts/run-tests.sh` AND the Jenkinsfile coverage step.
