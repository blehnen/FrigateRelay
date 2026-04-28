---
phase: 10
type: documentation-review
date: 2026-04-28
verdict: ACCEPTABLE — DEFER_TO_DOCS_SPRINT
blocker_grade: false
---

# Documentation Review — Phase 10

**Phase:** 10 — Dockerfile + Multi-Arch Release  
**Date:** 2026-04-28  
**Verdict:** ACCEPTABLE / DEFER_TO_DOCS_SPRINT  
**Diff base:** e08d4f4..HEAD

## Summary

Phase 10 added a production Dockerfile, multi-arch release workflow, `/healthz` endpoint, Serilog path validation, and associated Docker ecosystem files. No README or `docs/` tree exists in the repo yet — that gap is pre-existing, tracked as ID-9, and deferred to Phase 12. No new public API surface was added (all new types are `internal` except `IMqttConnectionStatus`, which is justified by the dependency-direction constraint and needs XML docs). Two stale statements in `CLAUDE.md` should be updated now (both low-cost, both catch developer friction before Phase 11).

---

## Public API Surface

| Type | File | Visibility | Justification | XML docs |
|------|------|-----------|---------------|----------|
| `IMqttConnectionStatus` | `src/FrigateRelay.Abstractions/IMqttConnectionStatus.cs` | **`public`** | Forced: Sources project must depend on the interface without creating a circular Host → Sources dependency. Documented in SUMMARY-1.1 Decision #1. | **Missing** — no `<summary>` on the interface or its two members (`IsConnected`, `SetConnected`). |
| `MqttConnectionStatus` | `src/FrigateRelay.Host/Health/MqttConnectionStatus.cs` | `internal sealed` | Correct. Impl detail of Host. | N/A |
| `MqttHealthCheck` | `src/FrigateRelay.Host/Health/MqttHealthCheck.cs` | `internal sealed` | Correct. | N/A |
| `HealthzResponseWriter` | `src/FrigateRelay.Host/Health/HealthzResponseWriter.cs` | `internal sealed` | Correct. | N/A |
| `StartupValidation.ValidateSerilogPath` | `src/FrigateRelay.Host/StartupValidation.cs` | `internal static` | Correct. | N/A |

**Public-leaking?** Only `IMqttConnectionStatus` is public, and that is architecturally justified. It is the only new addition to `FrigateRelay.Abstractions` — the package that will eventually surface as a NuGet for plugin authors (PROJECT.md Non-Goals notes this is deferred but the design targets it). That makes XML docs on this interface non-trivial to defer indefinitely.

**Assessment:** One interface, two members. XML docs are missing but low-effort. Not blocker-grade for Phase 10 (the interface shape is stable), but should not reach Phase 12 undocumented.

---

## README Currency

**Status: README does not exist.** The repo root has no `README.md` or `README.*` file. This is pre-existing (ID-9, deferred since Phase 4). Phase 10 does not change the verdict — defer to Phase 12 docs sprint.

Anticipated README sections once written, incorporating Phase 10 reality:

- What FrigateRelay does (one paragraph — PROJECT.md has good source text)
- Install path: `docker pull ghcr.io/<owner>/frigaterelay:latest` is now the canonical path (Phase 10 delivered this)
- Supported arches: `linux/amd64`, `linux/arm64`
- Quick-start: copy `.env.example` → `.env`, fill secrets, run `docker compose -f docker/docker-compose.example.yml up`
- `/healthz` endpoint: what it returns, when it transitions 503 → 200, how to wire into uptime checks
- Config layering: `appsettings.json` + `appsettings.Docker.json` + env vars + optional `appsettings.Local.json` mount
- Link to `docker/docker-compose.example.yml` and `docker/.env.example`

---

## CLAUDE.md Currency

Two statements are now demonstrably stale after Phase 10:

### Stale item 1 — `--filter-query` flag

**Location:** CLAUDE.md, Commands section, "Single test by name" block  
**Current text:** `dotnet run --project tests/FrigateRelay.Host.Tests -c Release -- --filter-query "..."`  
**Reality:** SUMMARY-1.2 Decision #2 documents that `--filter-query` does not work with MSTest v4.2.1; the correct flag is `--filter "ClassName"`. This is also pre-existing as **ID-4**.  
**Recommendation:** Update the example to use `--filter "TestClassName"` and note that the full MTP path-based filter is not supported by the installed runner version. This is the only CLAUDE.md change recommended for immediate action — it actively misleads contributors running tests for the first time.

### Stale item 2 — `release.yml` described as "not present yet"

**Location:** CLAUDE.md, CI section  
**Current text:** `- \`.github/workflows/release.yml\` — planned for Phase 10 (multi-arch Docker build + GHCR push on tag \`v*\`). Not present yet.`  
**Reality:** `release.yml` was delivered in PLAN-2.2. The "not present yet" language is now false.  
**Recommendation:** Update to describe the actual workflow: two-job structure (smoke on amd64, then push-multiarch gated on smoke success), triggered on `v*` tags, pushes to GHCR with `:semver` + `:latest` + `:major` tags. Worth doing before Phase 11 since new contributors will read this section to understand the release process.

### Stale item 3 — Project state description

**Location:** CLAUDE.md, Project state section  
**Current text:** "FrigateRelay is a **greenfield .NET 10 rewrite**, currently **pre-implementation**. Nothing but planning docs exists in-tree yet."  
**Reality:** Phase 10 is complete; the project is fully implemented with 100+ passing tests and a shipping Docker image.  
**Recommendation:** Defer to Phase 12 docs sprint — this section is agent-onboarding text, not user-facing. Low priority.

---

## Architecture Docs

No `docs/` directory exists. Architecture is documented only in:
- `CLAUDE.md` — Architecture invariants section (agent-onboarding, technically accurate post-Phase 10)
- `.shipyard/PROJECT.md` — Goals, requirements, design decisions
- `.shipyard/ROADMAP.md` — Phase-by-phase decisions
- Per-plan SUMMARY files

**Web SDK pivot rationale:** SUMMARY-1.1 Decision (Task 2) explains the `Microsoft.NET.Sdk.Worker` → `Microsoft.NET.Sdk.Web` pivot — the Web SDK was chosen for `/healthz` over raw TCP to avoid implementing HTTP manually. This reasoning exists only in SUMMARY-1.1.md and should be captured in a design decisions section when the `docs/` tree is created.

**Relay pipeline documentation:** The MQTT → Channel → Dispatcher → Plugin pipeline is described only in `CLAUDE.md` "Architecture invariants." No standalone architecture document. Defer to Phase 12.

**Plugin development guide:** Not present. PROJECT.md goal #3 says "open-source-ready" and mentions runtime-DLL-load discovery compatibility. The `IPluginRegistrar` pattern is documented in CLAUDE.md but not as a contributor guide. Defer to Phase 12.

---

## Config Reference

Phase 10 introduced new config surfaces with no reference documentation:

| Config surface | File | Documented? |
|---|---|---|
| `appsettings.Docker.json` — Console-only Serilog WriteTo override | `src/FrigateRelay.Host/appsettings.Docker.json` | In-file `_comment` key only |
| `appsettings.Smoke.json` — minimal smoke profile | `docker/appsettings.Smoke.json` | Inline comments in file |
| Serilog path allowlist (`/var/log/frigaterelay/`, `/app/logs/`) | `src/FrigateRelay.Host/StartupValidation.cs` | `<remarks>` block on `ValidateSerilogPath` (internal, not visible to operators) |
| `/healthz` response schema | `src/FrigateRelay.Host/Health/HealthzResponseWriter.cs` | Not documented |
| `ASPNETCORE_URLS` default (`http://+:8080`) | `src/FrigateRelay.Host/Program.cs` | In compose example comment only |
| `ASPNETCORE_ENVIRONMENT=Docker` semantics | `docker/docker-compose.example.yml` | Comment in compose file |

**Known limitation flag:** SUMMARY-1.3 documents that `appsettings.Docker.json` does NOT fully suppress the file sink — `IConfiguration` array-merge semantics keep parent `WriteTo` entries active. PLAN-2.1 SUMMARY-1.1 Decision #4 documents the mitigation (code-side guard in `HostBootstrap.cs` keyed on `EnvironmentName == "Docker"`). The two-layer mitigation (code + config) works, but the config file's in-file `_comment` does not explain the limitation. Operators who try to use `appsettings.Docker.json` alone to disable the file sink will be confused. Should be surfaced in operator docs.

---

## Operational Docs

### Docker deploy walkthrough

**Status: Missing.** The `docker/docker-compose.example.yml` and `docker/.env.example` exist and are well-commented inline, but there is no narrative walkthrough. The minimum a user needs:

1. Copy `.env.example` → `.env`, set `BLUEIRIS__BASEURL`, `BLUEIRIS__USERNAME`, `BLUEIRIS__PASSWORD`, `PUSHOVER__APITOKEN`, `PUSHOVER__USERKEY`
2. Create `config/appsettings.Local.json` with subscription configuration
3. `docker compose -f docker/docker-compose.example.yml up -d`
4. Verify with `curl http://localhost:8080/healthz` — expect `503` until broker connects, then `200`

Note: `docker/.env.example` line 2 contains `BLUEIRIS__BASEURL=http://blueiris.lan:81`. This is a hostname example, not an IP — does not violate the RFC-1918 prohibition. Acceptable as an example value.

### Multi-arch release process

**Status: Missing from user-facing docs.** SUMMARY-2.2 describes the two-job structure; CLAUDE.md line 126 still says "Not present yet." Steps to cut a release are not documented anywhere outside the workflow file itself:

1. Create and push a `v*` tag
2. `smoke` job builds amd64, runs `/healthz` poll against Mosquitto sidecar — must pass
3. `push-multiarch` builds `linux/amd64` + `linux/arm64`, pushes to GHCR with `:semver`, `:latest`, `:major` tags

Branch protection, release-notes process, and who can push tags are not addressed anywhere. Defer to Phase 12, but should be captured in a `CONTRIBUTING.md` or `docs/releasing.md`.

### Health-check semantics

**Status: Missing from user-facing docs.** The 503-then-200 transition is covered by `HealthzReadinessTests` and SUMMARY-1.1 Decision #4 (race-tolerance), but no operator-facing document explains:

- `/healthz` returns `503` until both `IHostApplicationLifetime.ApplicationStarted` fires AND MQTT broker connection is established
- Response body is compact camelCase JSON (`{"status":"Unhealthy","entries":{...}}`)
- Docker `HEALTHCHECK` uses `wget --spider` on port 8080 with 30s interval, 3 retries, 30s start_period
- Compose `healthcheck` mirrors the same

Defer to Phase 12 README.

---

## CHANGELOG / RELEASES

No `CHANGELOG.md` or `RELEASES.md` exists. `release.yml` uses `docker/metadata-action@v5` for image tagging but does not auto-generate release notes. GitHub will auto-generate release notes from PR titles if configured in `.github/release.yml` (separate from the workflow file) — that configuration does not currently exist.

**Recommendation:** Defer. Phase 12 is "Open-Source Polish" and is the natural home for a CHANGELOG and a GitHub release-notes configuration. No blocker here.

---

## Recommendations

### Update now (low-cost, developer-friction)

1. **CLAUDE.md — fix `--filter-query` example** (ID-4 resolution). Change `--filter-query "/*/*/..."` to `--filter "TestClassName"` with a note that full MTP path-based filters require a runner version that supports `--filter-query`. One-line edit, prevents contributor confusion on first test run.

2. **CLAUDE.md — update `release.yml` description** from "planned... Not present yet" to a brief description of the two-job structure and `v*` tag trigger. Two-line edit.

3. **`IMqttConnectionStatus` XML docs** — add `<summary>` to the interface and its two members (`IsConnected`, `SetConnected`). The interface is the only new public surface added to `FrigateRelay.Abstractions` in Phase 10. It will appear in any future NuGet package. Three `<summary>` tags is five minutes of work.

### Defer to Phase 11/12 docs sprint (expand ID-9)

1. **README.md** — full operator-facing document including install path, quick-start (Docker Compose), config layering, `/healthz` semantics, supported arches, and link to compose example.

2. **Architecture doc** — document the Web SDK pivot rationale, the relay pipeline, and the `IMqttConnectionStatus` → `MqttHealthCheck` wiring. Source material is in SUMMARY-1.1.

3. **Plugin development guide** — `IPluginRegistrar` pattern, how to add a new plugin, `IActionPlugin.ExecuteAsync` signature, `ISnapshotProvider` contract. Precondition for "open-source-ready" (PROJECT.md Goal #2).

4. **Config reference** — all `appsettings.*` keys including Health checks, Serilog allowlist, `appsettings.Docker.json` limitation note, `ASPNETCORE_ENVIRONMENT` semantics.

5. **CONTRIBUTING.md / releasing.md** — tag-based release process, branch protection expectations, release-notes process.

6. **CHANGELOG.md** — retroactive from Phase 1 (can be auto-generated from commit history + SUMMARY files).

7. **CLAUDE.md — stale project-state description** — remove "pre-implementation / Nothing but planning docs exists in-tree yet" language.

### Dismiss (not needed)

1. XML docs on `internal` types (`MqttConnectionStatus`, `MqttHealthCheck`, `HealthzResponseWriter`, `StartupValidation`) — internal to the host, not part of any public contract.

2. Separate architecture doc for Serilog path validation — the `ValidateSerilogPath` `<remarks>` block in the source file is sufficient for its container-centric threat model. No operator-facing surface.

3. Documentation of `docker/mosquitto-smoke.conf` — it exists solely as a CI smoke fixture and carries an inline warning against production use. No further documentation warranted.

---

## Coverage

- Files reviewed: 28 (all files in `git diff e08d4f4..HEAD --name-only`)
- SUMMARY files reviewed: 5 of 5 (PLAN 1.1, 1.2, 1.3, 2.1, 2.2)
- README assessed: yes — does not exist
- `docs/` dir present: no
- CLAUDE.md cross-checked: yes — 3 stale items found (2 recommended for immediate update, 1 deferred)
- ISSUES.md cross-checked: yes — ID-4 (filter-query) and ID-9 (doc sprint) confirmed open
- Public API surface confirmed: `IMqttConnectionStatus` is public by architectural necessity; all other new types are `internal`
