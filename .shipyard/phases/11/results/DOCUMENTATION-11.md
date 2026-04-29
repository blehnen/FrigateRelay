# Documentation Review — Phase 11

**Phase:** 11 — Open-Source Polish
**Date:** 2026-04-28
**Verdict:** ACCEPTABLE — one deferred gap (CHANGELOG Phase 11 entry), one low-severity CONTRIBUTING note
**Diff base:** 0861818..HEAD

## Summary

- API/Code docs: 0 new public interfaces (Phase 11 is docs-only; no source contracts changed)
- Architecture updates: 0 (no structural changes)
- User-facing docs: 6 created (README, CONTRIBUTING, SECURITY, CHANGELOG, plugin-author-guide, docs.yml), 3 GitHub templates created, 1 dotnet-new template created, 1 samples project created, 1 doc-rot script created
- CLAUDE.md: Updated by builder — filter syntax corrected, Jenkinsfile Dependabot note updated, project state paragraph updated

All Phase 11 deliverables are present, internally consistent, and cross-referenced correctly.
The samples project (`samples/FrigateRelay.Samples.PluginGuide`) covers all four contract types (`IActionPlugin`, `IValidationPlugin`, `ISnapshotProvider`, `IPluginRegistrar`) with working exercised examples. Doc-rot enforcement is wired into CI (`docs.yml` `doc-samples-rot` job, `check-doc-samples.sh`).

---

## Public API Surface

None. Phase 11 introduced no new public types — all changes are documentation, templates, and samples. The two integration test files in the diff (`MqttToValidatorTests.cs`, `TraceSpansCoverFullPipeline.cs`) are test additions, not public surface.

---

## Phase 11 Deliverables Coverage

| Deliverable | Status | Notes |
|---|---|---|
| `README.md` | COMPLETE | Quickstart (Docker), config layering, Profiles/Subscriptions example, plugin scaffold pointer, `/healthz` semantics documented, supported arches covered implicitly via `release.yml` reference |
| `CONTRIBUTING.md` | COMPLETE with note | Build/test commands accurate; MSTest v4.2.1 named explicitly; FluentAssertions pin explained; NSubstitute + `DynamicProxyGenAssembly2` documented; `CapturingLogger` referenced. Minor: `--filter` syntax uses `"PluginRegistrarRunnerTests"` (class name, no path wildcards) — consistent with CLAUDE.md. See Recommendations for a clarifying note. |
| `LICENSE` | COMPLETE | MIT, Copyright 2026 Brian Lehnen |
| `SECURITY.md` | COMPLETE | GitHub private advisory URL present; Supported Versions table present; maintainer Settings note present; 7-day SLA documented |
| `CHANGELOG.md` | GAP | Unreleased section contains Phase 10 entries only. Phase 11 deliverables (README, CONTRIBUTING, LICENSE, SECURITY, plugin-author-guide, templates, samples, docs.yml, issue templates) are not recorded. See Recommendations. |
| `.github/ISSUE_TEMPLATE/bug_report.yml` | COMPLETE | Covers component, expected/actual behavior, repro steps, environment, .NET version, logs, security self-cert |
| `.github/ISSUE_TEMPLATE/feature_request.yml` | COMPLETE | Problem/solution/alternatives + Non-Goals self-cert present |
| `.github/ISSUE_TEMPLATE/config.yml` | COMPLETE | Blank issues disabled; security advisory redirect present |
| `.github/pull_request_template.md` | COMPLETE | CHANGELOG update reminder, SECURITY reference, build/test gates, plugin scaffold pointer |
| `.github/workflows/docs.yml` | COMPLETE | Three jobs: `scaffold-smoke`, `samples-build`, `doc-samples-rot`. Correct conditional guards. Path filters cover docs/, samples/, templates/ |
| `.github/scripts/check-doc-samples.sh` | COMPLETE | Bash+Python stdlib, exit 0/1 semantics, 5 annotated fences verified |
| `docs/plugin-author-guide.md` | COMPLETE | 11 sections, tutorial-first, all 4 contracts covered with annotated `filename=` fences matching samples verbatim. Config layering documented (Section 7). Forward-compat design note (Section 10). |
| `templates/FrigateRelay.Plugins.Template/*` | COMPLETE with note | Renders via `dotnet new frigaterelay-plugin`; CI smoke-tests the render. Template generates `IActionPlugin` + `IPluginRegistrar` only — no `IValidationPlugin` or `ISnapshotProvider` stubs. Acceptable for v1 (action plugin is 90% of use cases); noted for Phase 12. |
| `samples/FrigateRelay.Samples.PluginGuide/*` | COMPLETE | All 4 contracts: `SampleActionPlugin`, `SampleValidationPlugin`, `SampleSnapshotProvider`, `SamplePluginRegistrar`. `Program.cs` exercises each with a synthetic `EventContext`. `dotnet run` exits 0. |

---

## CLAUDE.md Currency

The CLAUDE.md at HEAD was updated by the Phase 11 builder. Changes verified:

| Item | Status |
|---|---|
| Project state paragraph | Updated — now says "Implementation complete through Phase 10; Phase 11 adds OSS polish; Phase 12 is parity-cutover gate" |
| Single-test filter syntax | Updated from `--filter-query "/*/*/..."` to `--filter "ClassName"` — consistent with MSTest v4.2.1 MTP runner |
| Jenkinsfile Dependabot note | Updated — now documents that `docker` ecosystem in `dependabot.yml` watches `docker/Dockerfile` only, NOT `Jenkinsfile` (intentionally decoupled) |
| `run-tests.sh` note in CI section | Present — Rule-of-Three note updated to reflect script now exists (Phase 3 landed) |

**No new Phase 11 conventions require CLAUDE.md entries.** The three candidate conventions identified in the task prompt were evaluated:

1. "Samples project + dotnet-new template location" — already documented in CONTRIBUTING and plugin-author-guide. CLAUDE.md is not the right home for project layout facts that belong in contributor-facing docs.
2. "CapturingSerilogSink pattern" — Phase 11 did not introduce a `CapturingSerilogSink`. No such type was added in the diff. Dismiss.
3. "Doc-rot check via `check-doc-samples.sh`" — CI behavior, not a coding convention. The script is referenced in `docs.yml` and discoverable from there. Not a CLAUDE.md convention.

---

## Recommendations

### Generate now (blocker-grade — cost users)

**None.** No blocker-grade gaps found.

---

### Generate now (low cost, high value — deferred is fine but worth flagging)

**1. CHANGELOG.md — add Phase 11 entries to `[Unreleased]`**

The `[Unreleased]` section currently contains only Phase 10 entries. Phase 11 delivered significant user-visible OSS assets (README, CONTRIBUTING, LICENSE, SECURITY, plugin scaffold, docs). Per Keep a Changelog conventions these belong under `[Unreleased]`. The PR checklist in both CONTRIBUTING and pull_request_template.md instructs contributors to update CHANGELOG for user-visible changes, so this is a consistency issue.

Suggested entry (add above the existing Phase 10 block):

```markdown
### Phase 11 — Open-source polish (2026-04-28)

#### Added

- `README.md` — operator quickstart (Docker), configuration reference, plugin scaffold instructions.
- `CONTRIBUTING.md` — build/test commands, coding standards, PR checklist, test framework details.
- `LICENSE` — MIT license.
- `SECURITY.md` — supported versions, GitHub private vulnerability reporting, 7-day response SLA.
- `docs/plugin-author-guide.md` — tutorial-first guide covering all four plugin contracts (`IActionPlugin`, `IValidationPlugin`, `ISnapshotProvider`, `IPluginRegistrar`) with annotated samples, config binding, DI scope rules, and forward-compat design note.
- `templates/FrigateRelay.Plugins.Template` — `dotnet new frigaterelay-plugin` scaffold for new plugin projects.
- `samples/FrigateRelay.Samples.PluginGuide` — live exercised examples for all four plugin contracts; CI-verified (`docs.yml`).
- `.github/ISSUE_TEMPLATE/{bug_report.yml,feature_request.yml,config.yml}` — structured issue templates with security redirect.
- `.github/pull_request_template.md` — PR checklist with CHANGELOG, SECURITY, and build/test gates.
- `.github/workflows/docs.yml` — CI workflow: template scaffold smoke, samples build, doc-rot byte-match check.
- `.github/scripts/check-doc-samples.sh` — enforces verbatim sync between `plugin-author-guide.md` fenced blocks and `samples/` source files.
```

**2. CONTRIBUTING.md `--filter` note — clarify class-name vs full-path syntax**

Line 29 shows `-- --filter "PluginRegistrarRunnerTests"` (class name only). MSTest MTP `--filter` also accepts `"FullyQualifiedName~PluginRegistrarRunnerTests"` for disambiguation when class names collide across test projects. The current example is correct and works; adding a one-line note that class-name matching is an infix match would prevent future contributor confusion. Low priority.

---

### Defer to Phase 12 docs sprint

Per the ID-9 deferred-docs pattern, the following remain appropriate Phase 12 scope:

1. **Architecture overview / sequence diagram** — component interactions (MQTT → EventPump → Channel → Dispatcher → Plugin) and data flow. Phase 11 built the operator surface; Phase 12 should add an explanation document for maintainers.
2. **Operator reference** — exhaustive config-key reference (all `appsettings.json` fields, types, defaults, env-var overrides). README covers the shape but not completeness.
3. **Multi-arch Docker deploy guide** — GHCR pull command with digest pinning, compose override patterns for production secrets, Healthcheck polling in orchestrators (k8s/Portainer).
4. **Template enhancement** — adding optional `IValidationPlugin` and `ISnapshotProvider` stubs to `dotnet new frigaterelay-plugin` (currently `IActionPlugin` + `IPluginRegistrar` only).

---

### Dismiss

- **`IPluginRegistrar` must be stateless** — already documented in plugin-author-guide.md Section 10. No separate doc needed.
- **`docker/.env.example` reference** — README line 13 correctly references `cp docker/.env.example .env`. No gap.
- **Supported arches in README** — README does not list `linux/amd64,linux/arm64` explicitly. This is acceptable; the arches are documented in `release.yml` inline comments and the CHANGELOG Phase 10 entry. Adding a badges section is Phase 12 polish.

---

## Coverage

| Type | Count | Files |
|---|---|---|
| Tutorial | 1 | `docs/plugin-author-guide.md` (Sections 2–9) |
| How-to | 2 | `README.md` (Quickstart + config), `CONTRIBUTING.md` (build/test/PR workflow) |
| Reference | 3 | `CHANGELOG.md`, `SECURITY.md`, `LICENSE` |
| Explanation | 1 | `docs/plugin-author-guide.md` Section 10 (forward-compat design rationale) |
| Tooling | 2 | `.github/workflows/docs.yml`, `.github/scripts/check-doc-samples.sh` |
| Templates | 1 | `templates/FrigateRelay.Plugins.Template` |
| GitHub community | 4 | `bug_report.yml`, `feature_request.yml`, `config.yml`, `pull_request_template.md` |

**Gap count: 1 active** (CHANGELOG Phase 11 entry — low severity, not a blocker).
