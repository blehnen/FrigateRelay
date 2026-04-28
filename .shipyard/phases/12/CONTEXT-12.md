# Phase 12 — Discussion Capture (CONTEXT-12.md)

**Phase:** 12 — Parity Cutover (final phase before v1.0.0)
**Date:** 2026-04-28
**Method:** `/shipyard:plan 12` Step 2b discussion capture (7 decisions across 2 AskUserQuestion rounds)

This file captures user-locked decisions. Researcher and architect MUST treat these as constraints, not suggestions.

---

## Phase scope (from ROADMAP)

- Side-by-side deployment with the legacy `FrigateMQTTProcessingService` for ≥48 hours against the production MQTT broker.
- `docs/migration-from-frigatemqttprocessing.md` — INI → `appsettings.json` field-by-field mapping.
- Migration script (per D3, a C# console tool) that converts the author's real `.conf` to a valid `appsettings.json` passing Phase 8's `ConfigSizeParityTest`.
- Parity log reconciliation: CSV export from each service of `(timestamp, camera, label, action, outcome)` tuples + `docs/parity-report.md`.
- README migration section linking the doc + tool.
- Release tag `v1.0.0` cut on GHCR (manual, per D7).

**Success criteria (from ROADMAP):**
- Zero missed alerts and zero spurious alerts across the 48-hour parity window. Discrepancies must be explained as intentional improvements OR fixed before cutover.
- Cooldown behavior matches: for every camera+label firing within one minute, both services issued exactly one notification.
- Migration tool output passes `ConfigSizeParityTest` (Phase 8 artifact).
- README references migration doc + tool.
- `v1.0.0` tag pushed (which auto-builds + publishes multi-arch GHCR images via Phase 10's `release.yml`).

---

## Decisions

### D1 — Parity-window risk posture: **logging-only**

**Decision:** During the ≥48h parity window, FrigateRelay does NOT trigger real BlueIris/Pushover. The legacy service hits production targets normally; FrigateRelay logs every would-be-action at structured-log level (Info or higher with a known EventId) and the reconciliation reads those logs.

**Notes:**
- Zero blast radius if FrigateRelay misbehaves during the window.
- Reconciliation pairs `(camera, label, timestamp)` tuples between legacy's actual triggers and FrigateRelay's would-be-triggers.
- Implementation mechanism per D5.

---

### D2 — 3-wave plan structure with explicit observation checkpoint

**Decision:** Architect produces **3 waves**:

- **Wave 1 — Prep:** active build work. DryRun flag rollout (D5), `tools/FrigateRelay.MigrateConf/` tool + tests (D6), parity-CSV export mechanism (architect picks: existing Serilog file sink + parsing OR a dedicated audit-log sink — research dependent), `docs/migration-from-frigatemqttprocessing.md` doc, side-by-side bringup instructions.
- **Wave 2 — Observation checkpoint:** a single plan that encodes the 48h passive watch as an OPERATOR CHECKLIST. No automated execution; the plan documents what the user collects and verifies during the wait. Acts as a clean gate between Wave 1 (active) and Wave 3 (reconciliation). The literal 48h sleep happens out-of-session — you can `/shipyard:resume` after the window closes.
- **Wave 3 — Reconcile + release:** active build work. CSV reconciliation tooling, `docs/parity-report.md` template, README migration-section update, v1.0.0 release prep (manual tag per D7).

**Notes:**
- Wave 2 is a discrete plan (not a no-op task in Wave 1) so it shows up in `/shipyard:status` and the `post-build-phase-12` checkpoint covers it cleanly.
- Wave 3 builders will read a *real* parity-window output the operator collected during Wave 2; if Wave 2 hasn't completed, Wave 3 builders should fail fast or gracefully skip rather than fabricate results.
- Total elapsed time: Wave 1 = 3-4h active. Wave 2 = ~48h passive (no work). Wave 3 = 1-2h active + manual tag.

---

### D3 — Migration script implementation: **C# console tool**

**Decision:** `tools/FrigateRelay.MigrateConf/` — a real .NET 10 console application using `OutputType=Exe`. NOT a Python script (which the ROADMAP suggested as a possible path).

**Notes:**
- Uses the same `IConfiguration` types it migrates to. Schema mismatches surface at compile time, not runtime.
- Must read the legacy INI (`SharpConfig` is excluded per CLAUDE.md, so use `Microsoft.Extensions.Configuration.Ini` or hand-rolled parsing — architect decides).
- Output: a JSON file conforming to `appsettings.json` shape (Profiles + Subscriptions per S2).
- Must accept a `--input <path>` and `--output <path>` CLI args.
- Smoke verification: round-trip the author's real `.conf` (or a sanitized fixture) through the tool and assert the output passes `ConfigSizeParityTest` (Phase 8 artifact in `tests/FrigateRelay.Host.Tests/Configuration/ConfigSizeParityTest.cs`).

---

### D4 — Phase 12 stays narrow (no docs sprint absorption)

**Decision:** Architect plans only the ROADMAP-listed Phase 12 deliverables. ID-9's deferred docs (operator-reference, architecture diagrams, full config-reference, operations-guide) **stay deferred** beyond v1.0.0.

**Notes:**
- Phase 11's README + plugin-author-guide + CONTRIBUTING + SECURITY + CHANGELOG are deemed sufficient for the v1.0.0 release surface.
- A future minor-release docs pass (or never, if the existing docs are sufficient) handles ID-9.
- Architect MUST NOT add `docs/architecture.md`, `docs/configuration-reference.md`, or `docs/operations-guide.md` in Phase 12.

---

### D5 — DryRun mechanism: **per-action `DryRun: true` config flag in existing plugins**

**Decision:** Add a `DryRun` bool property to each action plugin's config record (e.g., `BlueIrisActionOptions.DryRun`, `PushoverActionOptions.DryRun`). When `true`, the plugin's `ExecuteAsync` logs a structured `would-execute` entry (with EventId, target, payload summary) at Info level instead of calling the external API. Returns success normally so the dispatcher metrics and counters reflect a successful "execute".

**Notes:**
- Touches each action plugin csproj (`FrigateRelay.Plugins.BlueIris`, `FrigateRelay.Plugins.Pushover`, and any others). Validators and snapshot providers are NOT affected — they don't call external action APIs.
- Default `DryRun = false` (production-safe).
- Operator sets `DryRun: true` per-action in `appsettings.Local.json` during the parity window, then removes it for production.
- Use `LoggerMessage` source-gen for the `would-execute` log emission per CLAUDE.md (CA1848 compliance).
- Reconciliation script (Wave 3) parses these structured log entries to build the FrigateRelay side of the parity comparison.

---

### D6 — `tools/FrigateRelay.MigrateConf/` solution layout with companion tests

**Decision:**
- New csproj at `tools/FrigateRelay.MigrateConf/FrigateRelay.MigrateConf.csproj` (`OutputType=Exe`, `TargetFramework=net10.0`, mirrors the BlueIris csproj shape).
- Companion test project at `tests/FrigateRelay.MigrateConf.Tests/FrigateRelay.MigrateConf.Tests.csproj` mirroring the plugin-test pattern (MSTest v4.2.1, FluentAssertions 6.12.2 pin, `OutputType=Exe`).
- Both added to `FrigateRelay.sln`.
- ci.yml's test loop discovers the test project automatically (per Phase 3 `find tests/*.Tests`).
- The tool csproj references `FrigateRelay.Host` (or a subset) for `IConfiguration` schema awareness.

**Notes:**
- This is a real production deliverable, not a one-off — it ships in the v1.0.0 release as the canonical migration path. Test coverage is non-optional.
- Smoke test: run the tool against `tests/FrigateRelay.MigrateConf.Tests/Fixtures/sanitized-legacy.conf` (author can create from real `.conf`) and assert output passes `ConfigSizeParityTest` (cross-reference imported from `tests/FrigateRelay.Host.Tests`).

---

### D7 — Manual v1.0.0 tag after parity report passes

**Decision:** The actual `git tag v1.0.0 && git push --tags` is a **manual operator step**, NOT an agent task. Architect's Wave 3 produces the parity report and updates the README; the operator reads the report, confirms success criteria, and runs the tag command themselves.

**Notes:**
- Phase 10's `.github/workflows/release.yml` triggers on `v*` push tags — so the manual `git push --tags` automatically builds and publishes the multi-arch GHCR images.
- Architect's Wave 3 should include a `RELEASING.md` snippet (or section in CONTRIBUTING.md or migration doc) with the exact commands.
- This is the lowest-automation-risk path. Agent territory ends at the parity report.

---

## Out-of-scope explicitly noted

- ID-9 deferred docs (operator-reference, architecture diagrams, full config-reference, operations-guide) — out of scope per D4.
- ID-22 (Phase 9 `Task.Delay` polling magic delays) — Phase 11 closeout left this open; not Phase 12 scope.
- ID-24..ID-27 advisories from Phase 10/11 audits — all Low/deferred; not Phase 12 scope.
- Setting up the LEGACY `FrigateMQTTProcessingService` deployment itself. Phase 12 assumes the user has the legacy service running side-by-side already; Phase 12 only builds FrigateRelay's parity-window deliverables.
- Real `.conf` confidentiality — author's real `FrigateMQTTProcessingService.conf` is NOT to be committed. The `tests/FrigateRelay.MigrateConf.Tests/Fixtures/` should hold a sanitized copy with placeholder secrets/IPs/hostnames. Researcher should confirm the existing `tests/FrigateRelay.Host.Tests/Configuration/sanitized-legacy.conf` (referenced in Phase 8 SUMMARY-3.1) is suitable as a starting point.
- Hot-reload, runtime DLL discovery, durable queue — all out of v1 scope per PROJECT.md Non-Goals.

---

## Open architect-discretion items (NOT user decisions)

- Parity-CSV export mechanism — extract from Serilog file sink output via the migrate-conf tool's reconciler subcommand, OR add a dedicated `audit-log` Serilog sink with structured columns. Researcher should investigate; architect picks based on least-effort path that produces a clean CSV.
- Reconciliation tooling location — could be:
  - Inside `tools/FrigateRelay.MigrateConf/` as a second subcommand (e.g., `migrate-conf reconcile --legacy <csv> --frigaterelay <log>`)
  - A separate `tools/FrigateRelay.ParityReport/` project
  - A bash + Python script under `scripts/`
  - Architect picks; bias toward subcommand of the existing tool to minimize new csproj surface.
- Wave 2's observation-checkpoint plan format — likely a single task that produces `docs/parity-window-checklist.md` (operator-facing) and a Wave-2 SUMMARY documenting "checklist created, hand-off to operator".
- Migration doc structure — field-by-field table per ROADMAP; architect picks INI block order (`[ServerSettings]`, `[PushoverSettings]`, `[SubscriptionSettings]`, ...) and which appsettings fields each maps to.
- Whether to include sample fixture data committed alongside the tool. Strong preference: yes, with secrets sanitized.

---

## Calibration notes for the architect

- Total deliverables: ~7 from ROADMAP. With ≤3-tasks-per-plan, expect **5-7 plans across 3 waves**.
- Wave 2 will likely be a single 1-task plan (the operator checklist).
- Wave 1 is the heavy wave (DryRun rollout across N plugins, MigrateConf tool, doc, parity-CSV export).
- Wave 3 is medium (reconciliation tool, parity-report doc, README update, RELEASING snippet).
- All Phase 9/10/11 conventions hold: `shipyard(phase-12):` commit prefix, builders use `LoggerMessage` source-gen, FluentAssertions 6.12.2 pin, MSTest v4.2.1 + MTP, etc.
- Test count baseline: 192/192. Phase 12 net new: ≥3-5 tests for `MigrateConf` round-trip + DryRun unit tests per affected plugin. Target: ≥197/197.
- Build clean (warnings-as-errors) discipline applies to the new tool csproj; mirror the `<GenerateDocumentationFile>` + `<NoWarn CS1591>` pattern from Phase 11 templates if needed.
