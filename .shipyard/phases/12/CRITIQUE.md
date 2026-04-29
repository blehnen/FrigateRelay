# Plan Critique — Phase 12 (Feasibility Stress Test)

**Phase:** 12 (Parity Cutover)
**Date:** 2026-04-28
**Verdict:** READY

## Summary

All 8 Phase 12 plans are structurally sound and architect-discretion fully locked. File paths are correct, API surfaces match (ExecuteAsync 3-param signature confirmed on both BlueIris and Pushover), and cross-wave dependencies are acyclic. Three critical locks verified: NDJSON field shape (Camera, Label, EventId) consistent across PLAN-1.1, 1.2, 1.5; legacy.conf fixture exists; MigrateConf subcommand router skeleton ready. No blocking issues.

## Per-Plan Findings

### PLAN-1.1: BlueIris DryRun

**Status: READY**

- **File paths:** All exist. `src/FrigateRelay.Plugins.BlueIris/BlueIrisOptions.cs` confirmed; `BlueIrisActionPlugin.cs` confirmed (1871 bytes, modified Apr 26).
- **API surface:** `public sealed record BlueIrisOptions` ✓; `ExecuteAsync(EventContext ctx, SnapshotContext snapshot, CancellationToken ct)` signature matches Phase 6 ARCH-D2 ✓.
- **Options fields:** record with `TriggerUrlTemplate`, `AllowInvalidCertificates`, `RequestTimeout`, `QueueCapacity`, `SnapshotUrlTemplate`. Plan instructs append `public bool DryRun { get; init; }` after `SnapshotUrlTemplate` ✓.
- **Test harness:** Existing `tests/FrigateRelay.Plugins.BlueIris.Tests/` directory exists.
- **LoggerMessage fields:** Plan specifies template `"BlueIris DryRun would-execute for camera={Camera} label={Label} event_id={EventId}"`. Field names are: Camera, Label, EventId. **LOCK CONFIRMED** for PLAN-1.5/3.1 downstream coupling.
- **EventId:** Plan specifies `EventId(203, "BlueIrisDryRun")`. Next-available after 202 ✓ (verified in code: 201 is LogTriggerSuccess, 202 is LogTriggerFailed).
- **Acceptance test:** All three commands in the `## Verification` block are runnable (dotnet build, grep checks, secret-scan).

### PLAN-1.2: Pushover DryRun

**Status: READY**

- **File paths:** All exist. `src/FrigateRelay.Plugins.Pushover/PushoverOptions.cs` confirmed; `PushoverActionPlugin.cs` confirmed (5350 bytes, modified Apr 26).
- **Options type:** `internal sealed class` (NOT a record) ✓. Plan correctly specifies `public bool DryRun { get; init; }` (not a `[Required]` property).
- **ExecuteAsync signature:** `public async Task ExecuteAsync(EventContext ctx, SnapshotContext snapshot, CancellationToken cancellationToken)` ✓.
- **Logging style:** Pushover uses nested `private static class Log` (verified in code; contains `_sendSucceeded`, `_sendFailed`, `_snapshotUnavailable` LoggerMessage.Define entries). Plan correctly specifies adding `_wouldExecute` inside that nested class.
- **EventId:** Plan specifies `EventId(4, "PushoverDryRun")`. Next-available: existing EventIds are 1, 2, 3 (verified in code) ✓.
- **LoggerMessage fields:** Same as PLAN-1.1 — Camera, Label, EventId. **LOCK CONFIRMED**.
- **Test harness:** Existing `tests/FrigateRelay.Plugins.Pushover.Tests/` directory exists.
- **Stub tokens:** Plan specifies `"stub-token-not-real"` and `"stub-user-not-real"` (not secret-scan-shaped). Safe ✓.

### PLAN-1.3: MigrateConf Tool + Tests

**Status: READY**

- **Directory structure:** `tools/` directory does NOT exist yet (per RESEARCH §7 confirmation). Correct scope for Wave 1 creation.
- **INI fixture:** `tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf` EXISTS and is 2329 bytes (confirmed). RESEARCH §8 orchestrator correction validated — file is at `Fixtures/legacy.conf`, NOT `Configuration/Fixtures/legacy.conf`.
- **Hand-rolled parser:** Plan mandates hand-rolled INI reader (not `Microsoft.Extensions.Configuration.Ini`) due to SharpConfig repeated-section semantics. Spec provided (60+ lines in PLAN-1.3 Task 1). Correct design ✓.
- **Csproj structure:** Plan specifies tool as `OutputType=Exe`, test as `OutputType=Exe` with MSTest v4.2.1 / FluentAssertions 6.12.2 / NSubstitute. Correct pattern ✓.
- **Fixture link in test csproj:** Plan specifies `<None Include="..\FrigateRelay.Host.Tests\Fixtures\legacy.conf" Link="Fixtures\legacy.conf"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></None>` — correct MSBuild pattern for cross-project fixture sharing ✓.
- **sln registration:** Plan instructs `dotnet sln FrigateRelay.sln add` for both tool and test csproj. Correct workflow ✓.
- **Subcommand router:** Program.cs skeleton shows verb router ready for Wave 3 `reconcile` append. Correct design ✓.
- **Test commands:** All verification commands are syntactically correct and runnable (build, run-tests, json validation via Python, ratio assertion).
- **Risk flag:** MEDIUM risk noted in plan. Justified — 494-line plan, multiple files (csproj, Program.cs, IniReader, AppsettingsWriter, test harness).

### PLAN-1.4: Migration Doc

**Status: READY**

- **File path:** `docs/migration-from-frigatemqttprocessing.md` (create). Correct operator-facing location ✓.
- **Soft-coupling:** Depends on PLAN-1.3's CLI shape (`--input`, `--output` args) being locked. Both are explicit in PLAN-1.3 and PLAN-1.4 — no conflicts ✓.
- **RFC 5737 safety:** Plan explicitly forbids RFC 1918 IPs, mandates RFC 5737 (192.0.2.x). Acceptance criteria includes grep check for both. Consistent with fixture convention ✓.
- **Table content:** Three subsections (ServerSettings, PushoverSettings, SubscriptionSettings) with field mappings. Architecturally correct per RESEARCH §1 enumeration.
- **Dropped field:** Plan documents the intentional drop of `[SubscriptionSettings].Camera` per-subscription URL, explaining the consolidation to `BlueIris:TriggerUrlTemplate` with `{camera}` token. Correct narrative.
- **Secrets guidance:** Plan specifies env vars `Pushover__AppToken`, `Pushover__UserKey` per CLAUDE.md convention (double underscore = `:` in IConfiguration). Correct ✓.
- **Acceptance tests:** All grep/wc commands are runnable.

### PLAN-1.5: NDJSON Audit Log Sink

**Status: READY**

- **File paths:** `src/FrigateRelay.Host/HostBootstrap.cs`, `src/FrigateRelay.Host/FrigateRelay.Host.csproj`, `tests/FrigateRelay.Host.Tests/Logging/CompactJsonFileSinkTests.cs` (create).
- **Serilog.Formatting.Compact package:** Plan instructs add at version 2.0.0 to csproj. Correct version per Phase releases (no pin to 2.1+) ✓.
- **Config key:** `Logging:File:CompactJson` (bool, default false). Opt-in design, production default unchanged ✓.
- **HostBootstrap refactor:** Plan specifies branching at `WriteTo.File` call (verified to exist at line 51 of HostBootstrap.cs). Refactor scope is clear.
- **Field shape lock:** Plan Task 1 specifies the LoggerMessage invocation pattern and the named parameters that PLAN-3.1 will parse. **CRITICAL LOCK:** the Serilog Compact format will emit properties as `"Camera": "...", "Label": "...", "EventId": "..."` at the JSON root level, plus `"@t"` (timestamp) and `"@i"` (EventId.Name). PLAN-3.1's reconciler will read exactly these names. **VERIFIED LOCKED**.
- **Test harness:** Task 2 outlines a test that emits a known event and asserts the NDJSON file contains the expected properties. Correct verification pattern ✓.
- **Task 3 size-risk:** Plan flags that adding documentation to `config/appsettings.Example.json` could break the 60% character ratio gate from Phase 8. Mitigation offered: drop the doc if ratio fails, document flag in PLAN-1.4 instead. Smart defensive design ✓.
- **Acceptance tests:** All build/test/smoke commands are runnable.
- **Risk:** MEDIUM (per plan). Justified — refactors HostBootstrap, adds new test, couples to Serilog formatter APIs.

### PLAN-2.1: Parity-Window Checklist

**Status: READY**

- **File path:** `docs/parity-window-checklist.md` (create). Operator-facing, ships with v1.0.0 ✓.
- **Dependencies:** Depends on Wave 1 complete (PLAN-1.1, 1.2, 1.3, 1.4, 1.5). Correctly marked in metadata.
- **Content structure:** Nine sections: Purpose, Pre-flight checklist, Parity-window JSON overlay, Bringup, Legacy CSV export, Watch the window, After 48h, What Wave 3 will produce, Failure-mode guidance. Comprehensive scope ✓.
- **Config flag references:** Plan specifies exact JSON shape with `"DryRun": true` and `"CompactJson": true`. Matches PLAN-1.1/1.2/1.5 semantics ✓.
- **NDJSON tail command:** Plan specifies `tail -f logs/frigaterelay-*.log | grep -E '"(BlueIrisDryRun|PushoverDryRun)"'`. CORRECT — the EventId.Name will appear as `"@i":"BlueIrisDryRun"` in Compact format (semicolon is standard JSON, not escaped). Operator can use this to spot DryRun events.
- **Legacy CSV shape:** Plan specifies header `timestamp,camera,label,action,outcome`. Simple, operator-tractable.
- **Wave 3 handoff:** Plan documents the operator collects `logs/frigaterelay-*.log` and `parity-window/legacy-actions.csv` and runs `/shipyard:resume` to start Wave 3. Correct workflow ✓.
- **Risk:** LOW (documentation only, no code).

### PLAN-3.1: Reconcile Subcommand + Parity Report

**Status: READY**

- **File paths:** Extends existing `tools/FrigateRelay.MigrateConf/Program.cs` (replace `RunReconcile` stub), creates `tools/FrigateRelay.MigrateConf/Reconciler.cs`, `tests/FrigateRelay.MigrateConf.Tests/ReconcilerTests.cs`, `docs/parity-report.md`.
- **Subcommand integration:** Program.cs already has verb router skeleton (PLAN-1.3 Task 1). PLAN-3.1 replaces the `RunReconcile` stub with real implementation. Correct incremental design ✓.
- **NDJSON field parsing:** Reconciler reads `@i` (EventId.Name), `@t` (timestamp ISO8601), `Camera`, `Label` from JSON root. **LOCK MATCHES PLAN-1.5 emission shape** — field names are exact ✓.
- **CSV parsing:** Simple header-based column indexing (timestamp, camera, label, action). No assumptions about order beyond header row ✓.
- **Bucket logic:** 60-second time bucket on `(camera, label, action, floor(timestamp / bucket))` tuple. Spec in code is sound (Reconciler.cs snippet provided).
- **Exit codes:** 0 = parity (no missed/spurious), 2 = gaps (operator must investigate), 1 = usage error. Correct semantics ✓.
- **Markdown render:** RenderMarkdown method produces table output for missed/spurious alerts. Operator-readable format ✓.
- **Test coverage:** 5+ tests covering perfect match, missed alert, spurious alert, bucket boundary, markdown render. Comprehensive ✓.
- **Parity-report.md template:** Plan provides both template (if operator hasn't run window) and real output (if artifacts exist). Smart dual-path design ✓.
- **Dependencies:** Correctly marks Wave 1 (PLAN-1.3, 1.5) and Wave 2 (PLAN-2.1) as prerequisites.
- **Risk:** MEDIUM (reconciliation logic is load-bearing for the parity sign-off).

### PLAN-3.2: README Migration Section + RELEASING.md + CHANGELOG

**Status: READY**

- **File paths:** README.md (modify), RELEASING.md (create), CHANGELOG.md (modify).
- **README section:** Plan specifies append-only (no restructure). Content links to four docs: migration tool path, migration field mapping, parity checklist, parity report. All are delivered by Waves 1–3 ✓.
- **RELEASING.md:** New file at repo root. Content:
  1. Pre-release checklist (7 checkboxes including parity-report review, DryRun/CompactJson flag removal).
  2. CHANGELOG promotion instruction.
  3. Tag + push commands (`git tag -a v1.0.0 -m ...`, `git push origin v1.0.0`).
  4. Explanation of `release.yml` smoke gate behavior.
  5. Post-release verification (docker pull, smoke).
  6. Rollback guidance (tag deletion, v1.0.1 cut).
  7. Optional ID-24 callout (action SHA pinning, deferred).
  
  Correct scope and operator-facing tone ✓.

- **CHANGELOG entry:** Plan specifies append under `[Unreleased]` with `### Added` and `### Changed` subsections. Content covers DryRun flags, MigrateConf tool, reconcile subcommand, tests, migration doc, parity checklist/report, RELEASING.md, README section, CompactJson flag. Comprehensive and factual ✓.
- **Critical note:** Plan explicitly does NOT promote `[Unreleased]` to `[1.0.0]` in Phase 12. That promotion happens in `RELEASING.md` as an operator manual step before `git tag v1.0.0`. Correct separation of concerns ✓.
- **Risk:** LOW (documentation and config).

## Cross-Wave Forward References

### Wave 1 → Wave 2 (Hard dependency)
- **PLAN-1.1/1.2/1.3/1.4/1.5 → PLAN-2.1:** All Wave 1 outputs (DryRun config keys, MigrateConf tool, migration doc, NDJSON sink) are referenced in the parity-window checklist. Checklist cannot be authored until Wave 1 lands. **CORRECT** (PLAN-2.1 dependencies marked: [1.1, 1.2, 1.3, 1.4, 1.5]).

### Wave 2 → Wave 3 (Soft: operator-controlled)
- **PLAN-2.1 artifacts (logs + CSV) → PLAN-3.1 reconciler:** The operator collects FrigateRelay NDJSON and legacy CSV during the 48h window, then runs `/shipyard:resume` to trigger Wave 3. PLAN-3.1 expects the files at `logs/frigaterelay-*.log` and `parity-window/legacy-actions.csv`. **CORRECT** — operator's checklist hands off these paths.

### Cross-Wave Field Coupling
- **PLAN-1.1/1.2 LoggerMessage field names ↔ PLAN-1.5 NDJSON emission ↔ PLAN-3.1 reconciler parsing:**
  - PLAN-1.1: `LoggerMessage.Define<string, string, string>(..., "BlueIris DryRun would-execute for camera={Camera} label={Label} event_id={EventId}")` → fields: **Camera, Label, EventId**.
  - PLAN-1.2: Same pattern via nested `Log` class: `LoggerMessage.Define<string, string, string>(..., "Pushover DryRun would-execute for camera={Camera} label={Label} event_id={EventId}")` → fields: **Camera, Label, EventId**.
  - PLAN-1.5: Serilog Compact formatter reads these named parameters from the LoggerMessage and emits them as top-level JSON properties (plus `@t` timestamp, `@i` EventId.Name).
  - PLAN-3.1 reconciler: `doc.RootElement.GetProperty("Camera")`, `doc.RootElement.GetProperty("Label")`, `doc.RootElement.GetProperty("EventId")`. **EXACT MATCH** ✓.

- **PLAN-3.1 reconciler → PLAN-3.2 CHANGELOG:** The reconcile output populates the parity-report markdown, which is reviewed by the operator per the RELEASING.md checklist before `git tag v1.0.0`. **CORRECT flow**.

### File Disjoint Verification (Wave 1 parallelism)
- **PLAN-1.1:** Touches `src/FrigateRelay.Plugins.BlueIris/BlueIrisOptions.cs`, `BlueIrisActionPlugin.cs`, `tests/FrigateRelay.Plugins.BlueIris.Tests/BlueIrisActionPluginTests.cs`.
- **PLAN-1.2:** Touches `src/FrigateRelay.Plugins.Pushover/PushoverOptions.cs`, `PushoverActionPlugin.cs`, `tests/FrigateRelay.Plugins.Pushover.Tests/PushoverActionPluginTests.cs`.
- **PLAN-1.3:** Touches `tools/FrigateRelay.MigrateConf/`, `tests/FrigateRelay.MigrateConf.Tests/`, `FrigateRelay.sln`.
- **PLAN-1.4:** Touches `docs/migration-from-frigatemqttprocessing.md` only.
- **PLAN-1.5:** Touches `src/FrigateRelay.Host/HostBootstrap.cs`, `src/FrigateRelay.Host/FrigateRelay.Host.csproj`, `tests/FrigateRelay.Host.Tests/Logging/CompactJsonFileSinkTests.cs`, `config/appsettings.Example.json` (optional per Task 3 size-budget guard).

**No overlaps within Wave 1.** All five plans are parallel-safe ✓.

### sln Conflicts
- PLAN-1.3 adds TWO csproj entries to FrigateRelay.sln: `tools/FrigateRelay.MigrateConf/FrigateRelay.MigrateConf.csproj` and `tests/FrigateRelay.MigrateConf.Tests/FrigateRelay.MigrateConf.Tests.csproj`.
- PLAN-1.5 does NOT modify the sln.
- PLAN-3.1 does NOT modify the sln (the tool and test csproj already registered by PLAN-1.3).

**No sln conflicts** ✓.

## Architect Risk Items Resolved

### 1. PLAN-1.3 Hand-rolled IniReader (SharpConfig repeated-section challenge)

**Status: RESOLVED ✓**

- **Challenge:** `Microsoft.Extensions.Configuration.Ini` uses dictionary semantics, so repeated `[SubscriptionSettings]` headers collapse via last-writer-wins. SharpConfig (the legacy parser) preserves each `[SubscriptionSettings]` block as a distinct list entry.
- **Architect solution:** Hand-rolled enumerator in PLAN-1.3 Task 1 (`IniReader.cs` lines 154-196 provided in plan). Logic:
  - Iterate file lines.
  - On each `[Header]` line, close the previous section dict and start a new one.
  - Yield one `Section(name, List<KeyValuePair>)` per header occurrence.
  - No `Dictionary` collapse; distinct list entries ✓.
- **Verification:** Plan Task 1 acceptance criterion includes `python3 -c 'import json; d=json.load(...); assert len(d["Subscriptions"]) == 9'` — confirms the 9 repeated SubscriptionSettings blocks round-trip intact ✓.

### 2. PLAN-1.5 HostBootstrap Refactor + Test Completeness

**Status: RESOLVED ✓**

- **Challenge:** HostBootstrap is a long-lived, mission-critical bootstrap routine. Refactoring for testability carries risk of introducing a bug.
- **Architect solution (implicit in plan design):**
  - PLAN-1.5 Task 1 specifies a scoped refactor: add a config-flag branch at the `WriteTo.File` call ONLY. No global changes to log level, enrichers, or activity propagation.
  - Task 2 provides two unit tests: one standalone test of the `CompactJsonFormatter` output (does NOT depend on HostBootstrap refactor), and one placeholder for a real HostBootstrap-integrated test (arch notes that if a public hook doesn't exist, builder can refactor to expose `BuildLoggerConfiguration(IConfiguration)` and test that).
  - Plan explicitly marks Task 2 Test #2 as "placeholder" — smart conservative design, allows builder to skip or defer the integrated test if too invasive.
  - Test #1 stands alone and is sufficient to lock the NDJSON field-name contract for PLAN-3.1 ✓.

### 3. NDJSON Field Shape Coupling: PLAN-1.1 ↔ PLAN-1.5 ↔ PLAN-3.1

**Status: RESOLVED ✓**

- **Challenge:** PLAN-1.1 and 1.2 define LoggerMessage field names (Camera, Label, EventId). PLAN-1.5 emits them via Serilog Compact format. PLAN-3.1 parses them by name. Any mismatch breaks the parity report.
- **Architect solution:**
  - PLAN-1.1 locks exact field names in the LoggerMessage template string: `"... for camera={Camera} label={Label} event_id={EventId}"`.
  - PLAN-1.2 uses the same field names via the nested `Log` class static method.
  - PLAN-1.5 Task 2 Test #1 explicitly asserts that a CompactJsonFormatter-emitted line contains `doc.RootElement.GetProperty("Camera")`, `"Label"`, `"EventId"`.
  - PLAN-3.1 Task 1 (`Reconciler.cs`) reads exactly these property names: `doc.RootElement.GetProperty("Camera")`, etc.
  - **All three field names are identical across plans** ✓. Coupling is explicit and verified.

### 4. PLAN-3.2 CHANGELOG `[Unreleased]` (no premature promotion)

**Status: RESOLVED ✓**

- **Challenge:** Phase 11 documenter had to catch-up retroactively with CHANGELOG entries. Phase 12 architect wants to be proactive: add Phase 12 entries DURING the phase, not after.
- **Architect solution:** PLAN-3.2 Task 3 explicitly specifies "append under `[Unreleased]`; do NOT promote to `[1.0.0]`". The promotion happens in `RELEASING.md` as a manual operator step just before `git tag v1.0.0`. This preserves the invariant that `[1.0.0]` appears only when the release is truly cut ✓.

### 5. run-tests.sh Auto-discovery (PLAN-1.3 test project)

**Status: RESOLVED ✓**

- **Challenge:** When `tests/FrigateRelay.MigrateConf.Tests/FrigateRelay.MigrateConf.Tests.csproj` lands, will `run-tests.sh` auto-discover it?
- **Architect solution:** RESEARCH §7 confirms `run-tests.sh` uses `find tests -maxdepth 2 -name '*Tests.csproj'`. The new test csproj matches this glob. PLAN-1.3 Task 1 acceptance criterion includes `find tests -maxdepth 2 -name '*Tests.csproj' | grep -q MigrateConf` ✓. No changes to ci.yml or `run-tests.sh` needed.

## Verification Commands Feasibility

All plans' `## Verification` blocks contain concrete, runnable commands. Spot checks:

- **PLAN-1.1:** `dotnet build src/FrigateRelay.Plugins.BlueIris/FrigateRelay.Plugins.BlueIris.csproj -c Release` ✓
- **PLAN-1.3:** `dotnet run --project tools/FrigateRelay.MigrateConf -c Release -- --input ... --output ...` ✓
- **PLAN-1.5:** `dotnet run --project src/FrigateRelay.Host -c Release --no-build > /tmp/host-smoke.log 2>&1 &` (with graceful shutdown via kill -INT) ✓
- **PLAN-3.1:** Reconcile smoke command with synthetic fixtures ✓
- **PLAN-3.2:** README/RELEASING/CHANGELOG grep + markdown validation ✓

All executable and will provide clear pass/fail feedback to the builder.

## Findings Severity

### Blocking (would require REVISE)
None. All plans are feasible as written.

### Caution (note for builder/reviewer)
1. **PLAN-1.3 NDJSON output size vs Phase 8 gate:** PLAN-1.5 Task 3 notes a size-ratio risk. If the appsettings.Example.json comment/documentation causes it to exceed the 60% char-count ceiling from ConfigSizeParityTest, Task 3 can be dropped and the flag documented in PLAN-1.4 instead. This is an acceptable contingency per the plan text ✓.

2. **PLAN-1.5 HostBootstrap refactor invasiveness:** If exposing a `BuildLoggerConfiguration(IConfiguration)` hook proves too disruptive, Task 2 Test #2 can be marked `[Ignore]` with a TODO. The standalone Test #1 is sufficient to lock the NDJSON contract for PLAN-3.1 ✓.

3. **PLAN-3.1 CSV header flexibility:** Reconciler reads CSV via column-index lookup on the header row. If an operator's legacy CSV is missing a column (e.g., no `outcome` column), the reconciler will skip that row gracefully (per code at line 145: `if (fields.Length < header.Length) continue`). **This is safe but should be documented in the reconciler's error message or the parity-window checklist** so the operator understands why rows disappear. Not blocking, but worth a note.

### Notes
1. **PLAN-2.1 depends on successful completion of all Wave 1 plans** before the operator can follow the checklist. This is a natural gate and correctly modeled in the plan dependencies.

2. **legacy.conf fixture location correction** (RESEARCH §8 orchestrator note): The researcher's original path was wrong; the actual location is `tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf`. This is verified to exist at 2329 bytes. Orchestrator correction is accurate and PLAN-1.3 references the correct path ✓.

3. **Phase 8 ConfigSizeParityTest still passes after MigrateConf lands:** PLAN-1.3 Task 3 explicitly verifies this via the filter-query command. Regression guard in place ✓.

## Verdict

**READY** — All 8 Phase 12 plans are architecturally sound, feasible to execute, and locked with sufficient detail for builder implementation. File paths are correct, API surfaces match, cross-wave dependencies are acyclic, and critical field-shape couplings are explicit and verified. Three architect discretion items (IniReader hand-parsing, HostBootstrap refactor scope, CHANGELOG [Unreleased] preservation) are correctly resolved. One contingency (PLAN-1.5 Task 3 size-ratio) is acceptable and documented. No blocking issues.

---

**Summary:**
- **Blocking items:** 0
- **Cautions:** 3 (all acceptable, with mitigations)
- **Notes:** 3 (informational, no action required)
- **Ready to build:** YES
