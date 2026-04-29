# Security Audit — Phase 10

**Phase:** 10 — Dockerfile and Multi-Arch Release Workflow
**Date:** 2026-04-28
**Verdict:** PASS_WITH_NOTES
**Risk level:** Low
**Diff base:** e08d4f4..HEAD
**Files reviewed:** 22 (all materially changed files; test files spot-checked)
**Lines reviewed:** 1,151 insertions per diff stat

---

## Summary

Phase 10 introduces a well-structured Docker/CI surface. The Dockerfile follows supply-chain best practice (multi-stage, non-root UID 10001, both base images digest-pinned). The release workflow is cleanly scoped to `push: tags: v*` with minimal permissions and a hard-fail smoke gate. No critical issues were found. Four advisory items are proposed for deferred tracking: GitHub Actions action-pinning is tag-only (not SHA-pinned), the smoke-gate runs Mosquitto with `allow_anonymous true` and no network isolation caveat documented, the `docker-compose.example.yml` exposes port 8080 to all host interfaces by default, and the `ValidateSerilogPath` allowlist does not cover Windows-style absolute paths (documented as an accepted gap but worth tracking).

---

## Critical Findings (block ship)

None.

---

## Important Findings (should fix this phase)

None.

---

## Advisory / Low Findings (defer with tracking)

### A1 — GitHub Actions steps use tag-pinned actions, not SHA-pinned

**Location:** `.github/workflows/release.yml` — all `uses:` lines
**Description:** Every third-party action (`actions/checkout@v4`, `docker/setup-qemu-action@v4`, `docker/setup-buildx-action@v4`, `docker/login-action@v4`, `docker/metadata-action@v6`, `docker/build-push-action@v7`) is pinned to a version tag, not a commit SHA. Mutable tags can be force-pushed by the action maintainer; a supply-chain compromise of any of these would execute arbitrary code in the release job with `packages: write` permission.
**Standard:** CWE-829, SLSA L2+
**Remediation:** Replace each `uses: action@vN` with `uses: action@<full-SHA>  # vN` and add Dependabot `github-actions` ecosystem (already present in `dependabot.yml`) which will keep them current. This is a one-time find-and-replace; Dependabot maintains it.
**Calibration:** This is standard OSS practice in the GitHub ecosystem and the existing `ci.yml` has the same pattern. Low urgency but should be addressed before a v1 formal release. Propose as **ID-24**.

---

### A2 — Smoke-gate Mosquitto runs `allow_anonymous true` on `0.0.0.0` with `--network host`

**Location:** `.github/workflows/release.yml:99–101`, `docker/mosquitto-smoke.conf:3–4`
**Description:** The smoke step starts `eclipse-mosquitto:2` with `allow_anonymous true` and `listener 1883 0.0.0.0` on `--network host`. On GitHub-hosted runners this is contained within the ephemeral runner VM and poses no direct risk. However, the `mosquitto-smoke.conf` file is committed to the repo and carries no header comment warning against production use beyond the first line. The combination of `allow_anonymous true` + `0.0.0.0` is a production anti-pattern; an operator copying this config verbatim for their own Mosquitto could expose an unauthenticated broker.
**Standard:** CWE-306
**Remediation:** Existing one-line comment "Smoke-test ... Do not deploy this in production" is present but could be more prominent. Consider adding `# WARNING: anonymous access + all-interface bind — CI use ONLY` as the second line. No code change required; the risk is documentation clarity. Propose as **ID-25**.

---

### A3 — `docker-compose.example.yml` publishes port 8080 to all host interfaces

**Location:** `docker/docker-compose.example.yml:26` (`ports: - "8080:8080"`)
**Description:** The example exposes the health endpoint on `0.0.0.0:8080` of the Docker host by default. The `/healthz` endpoint itself leaks operational state (`started`, `mqttConnected` boolean flags). In a home-lab deployment where the Docker host is directly on the LAN this is accessible to any LAN host. The endpoint carries no auth.
**Standard:** CWE-200 (Information Exposure), OWASP A05:2021 Security Misconfiguration
**Remediation:** Advisory only — the service is designed for home-lab use and the health endpoint is intentionally public for Docker/Compose polling. Document in the compose comment that operators should restrict port binding to `127.0.0.1:8080:8080` if the host is on an untrusted network, or use a reverse proxy with auth for external exposure. The current JSON payload (only `status`, `started`, `mqttConnected`) leaks no credentials or PII. Propose as **ID-26**.

---

### A4 — `ValidateSerilogPath` does not reject Windows-style absolute paths (accepted gap)

**Location:** `src/FrigateRelay.Host/StartupValidation.cs:92–95` (comment block)
**Description:** The implementation explicitly documents that Windows absolute paths (e.g., `C:\Windows\...`) are not blocked, rationalised by the Alpine/Linux container target. If the service is run on Windows (non-Docker, `Production` environment) with a misconfigured path, a malicious `appsettings.Local.json` could redirect log output to arbitrary Windows filesystem locations. The attacker must already control the config file, so exploitability is negligible.
**Standard:** CWE-22 (residual)
**Remediation:** The code comment is accurate. For completeness, add a `Path.IsPathRooted` + `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)` guard in a future hardening pass. Propose as **ID-27**. Low urgency — no container deploy is affected.

---

## CLAUDE.md Invariant Compliance

| Invariant | Status | Evidence |
|-----------|--------|----------|
| TLS-skip per-plugin only (no global callback) | **PASS** | `FrigateMqttEventSource.BuildClientOptions` uses `WithTlsOptions(o => o.WithCertificateValidationHandler(_ => true))` scoped to the plugin's MQTTnet client, gated by `AllowInvalidCertificates: true` config. `git grep ServicePointManager src/` returns zero matches in source (two doc-comment mentions only). |
| No global `ServicePointManager.ServerCertificateValidationCallback` | **PASS** | `git grep ServicePointManager src/` — 2 hits in XML doc-comments only; zero in executable code paths. |
| Secret-scan greps clean over diff | **PASS** | Scan of `docker/`, `appsettings.*.json`, `release.yml`: `secrets.GITHUB_TOKEN` (correct usage), `127.0.0.1` (loopback for smoke, not RFC 1918), placeholder values in `.env.example`. No real credentials. `AppToken`, `UserKey`, `apiKey`, bearer tokens, GitHub PATs, AWS keys: not present. |
| No hard-coded IPs/hostnames | **PASS** | `127.0.0.1` appears only in `release.yml:111` as the smoke-gate loopback target inside the CI runner VM — not a committed production hostname. `appsettings.Smoke.json` uses `"localhost"` (generic). `.env.example` uses `blueiris.lan` as a placeholder with a comment. No RFC 1918 addresses committed. |
| No App.Metrics / OpenTracing / Jaeger | **PASS** | `git grep -nE 'App\.Metrics|OpenTracing|Jaeger\.' src/` — zero matches. |
| No `.Result`/`.Wait()` in src/ | **PASS** | `git grep -nE '\.(Result|Wait)\(' src/` — zero matches. |

---

## Cross-Component Analysis

**SDK pivot (Worker → Web) and endpoint surface:** `Program.cs` uses `WebApplication.CreateBuilder` which starts Kestrel. The listen address is controlled entirely by `ASPNETCORE_URLS` (defaulting to `http://+:8080` in Docker via `ENV ASPNETCORE_URLS=http://+:8080` in the Dockerfile). Only one endpoint is mapped: `app.MapHealthChecks("/healthz", ...)`. No other routes are registered. There is no authentication/authorization concern because the health endpoint carries no sensitive data and is by design publicly accessible for probe use. The `HealthzResponseWriter` output contains only `status`, check `name`, and boolean `data` fields — no stack traces, no exception messages, no configuration values, no PII.

**MqttConnectionStatus thread safety:** `MqttConnectionStatus` uses `Volatile.Read`/`Volatile.Write` over a backing `int` field (0/1). This is correct for a single-writer (the MQTT reconnect loop), single-reader (`MqttHealthCheck`) scenario. No race condition is possible: `SetConnected` is called only from `RunReconnectLoopAsync` and `DisposeAsync`; `IsConnected` is read from the ASP.NET health-check thread. Volatile semantics ensure visibility across threads without boxing overhead.

**Dispose-during-health-check race:** `DisposeAsync` sets `_connectionStatus.SetConnected(false)` on the way out. If a health check fires concurrently with disposal, it will see `IsConnected=false` and return `503 Unhealthy`. This is correct behaviour — the container is shutting down.

**appsettings.Docker.json bundling:** The file is `CopyToOutputDirectory=PreserveNewest` so it ships inside the container image at `/app/appsettings.Docker.json`. It contains only Serilog console-sink config, no secrets, no hostnames. The `ASPNETCORE_ENVIRONMENT=Docker` env var activates it. This is the intended design (CONTEXT-10 B4).

**`.dockerignore` adequacy:** Excludes `.git/`, `.shipyard/`, `tests/`, `**/appsettings.Local.json`, `.env`, `.env.*`, `.github/`, `**/bin/`, `**/obj/`. All categories expected to be excluded are present. One minor gap: `CLAUDE.md` is not excluded but contains no secrets and poses no risk inside the image. Similarly `global.json`, `Directory.Build.props` are COPY'd into the build stage (intentionally, for NuGet restore layer caching) and are not in the runtime image.

**Jenkinsfile digest pin:** `mcr.microsoft.com/dotnet/sdk:10.0@sha256:8a90a473da5205a16979de99d2fc20975e922c68304f5c79d564e666dc3982fc` — digest is present and non-placeholder (commit `ddd3528` replaced the `DIGEST_PLACEHOLDER` from `1c3eaaa`). Dependabot `docker` ecosystem watches `docker/Dockerfile` only; Jenkinsfile requires manual update when Dockerfile digest bumps (documented in both files).

**`release.yml` trigger and permissions:** Trigger is `push: tags: ['v*']` only — no `workflow_dispatch`, no `pull_request_target`. This is correct; no fork-triggered workflow injection risk. `permissions: contents: read, packages: write` — minimal. No `id-token: write` (OIDC not used). `concurrency: group: release-${{ github.ref }}, cancel-in-progress: false` is present (per fix-up `14166c2`). `github.ref` in the concurrency group is safe — it is not interpolated into a `run:` shell step, only used as a string key.

**`${{ github.ref_name }}` injection check:** `github.ref_name` does not appear in any `run:` block in `release.yml`. The metadata-action receives `images: ghcr.io/${{ github.repository }}` as an `with:` input (not a shell interpolation), which is safe — GitHub Actions sanitises expression values in `with:` blocks. No injection vector.

---

## Deferred Items (recommend new ISSUES IDs)

| Proposed ID | Description | Severity |
|-------------|-------------|----------|
| ID-24 | GitHub Actions `uses:` in `release.yml` are tag-pinned, not SHA-pinned — supply-chain hardening | Low/Advisory |
| ID-25 | `docker/mosquitto-smoke.conf` comment could more prominently warn against production use | Low/Advisory |
| ID-26 | `docker-compose.example.yml` port 8080 exposed on all host interfaces; recommend documenting `127.0.0.1:8080:8080` alternative for hardened deployments | Low/Advisory |
| ID-27 | `ValidateSerilogPath` does not block Windows absolute paths — accepted gap for Linux/container target; track for completeness | Low/Advisory |

---

## Audit Coverage

| Area | Checked | Notes |
|------|---------|-------|
| Code Security (OWASP) | Yes | `HealthzResponseWriter`, `MqttHealthCheck`, `MqttConnectionStatus`, `IMqttConnectionStatus`, `StartupValidation.ValidateSerilogPath`, `Program.cs`, `HostBootstrap.cs`, `FrigateMqttEventSource.cs` reviewed |
| Secrets & Credentials | Yes | `appsettings.Docker.json`, `appsettings.Smoke.json`, `.env.example`, `release.yml`, `Jenkinsfile` scanned; no live credentials found |
| Dependencies | Partial | `FrigateRelay.Host.csproj` delta reviewed (3 packages removed as now transitive via Web SDK; no new packages added). Full `dotnet audit` not run — no new package additions to audit. |
| IaC / Container | Yes | `Dockerfile`, `docker-compose.example.yml`, `.dockerignore`, `mosquitto-smoke.conf`, `release.yml`, `dependabot.yml`, `Jenkinsfile` all reviewed |
| Configuration | Yes | All `appsettings.*.json` files in diff reviewed; `ValidateSerilogPath` path-traversal logic audited against test cases |
| STRIDE threat model | Yes — pre-analysis | Spoofing: no new auth surface. Tampering: health endpoint is read-only. Repudiation: Docker logging correct. Information Disclosure: `/healthz` leaks only boolean operational state (acceptable). DoS: no new unbounded resource path. EoP: non-root UID 10001 in container confirmed. |
