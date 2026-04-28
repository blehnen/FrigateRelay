# Simplification Review — Phase 12

**Phase:** 12 — Parity Cutover
**Date:** 2026-04-28
**Diff base:** post-plan-phase-12..HEAD
**Scope:** ~25 files; tools/FrigateRelay.MigrateConf/, tests/MigrateConf.Tests/, src/FrigateRelay.Plugins.BlueIris/, src/FrigateRelay.Plugins.Pushover/, src/FrigateRelay.Host/, docs/, root

## Summary

Phase 12 is documentation- and tooling-heavy with two small plugin changes (DryRun flags). No High findings. One Medium candidate (NDJSON log configuration defensive branch). Two Low notes on cross-doc key repetition and 2-verb CLI dispatch non-issue confirmation.

## Findings

### High (apply now or before ship)

None.

### Medium (apply opportunistically)

None.

### Low (track or dismiss)

**L1 — Cross-doc config-key repetition in operator docs**
- **Type:** Consolidate
- **Effort:** Trivial
- **Locations:** `docs/migration-from-frigatemqttprocessing.md`, `docs/parity-report.md` (template), parity-window-checklist section
- **Description:** The `Logging:File:Enabled` / `Logging:File:Path` / `Logging:File:CompactJson` triple appears in full in the migration doc and is likely repeated in the parity-window checklist for NDJSON setup context. If the same block appears verbatim in two operator docs, a single-line forward reference ("see migration doc §NDJSON Sink") would suffice in the secondary location.
- **Suggestion:** Audit both docs side-by-side; replace the duplicate config block in the checklist with a `> See [migration doc](migration-from-frigatemqttprocessing.md#ndjson-sink)` callout. One authoritative copy, one reference.

**L2 — MigrateConf 2-verb dispatch (non-issue confirmation)**
- **Type:** Note (no action)
- **Effort:** Trivial
- **Locations:** `tools/FrigateRelay.MigrateConf/Program.cs`
- **Description:** The prompt flagged potential over-engineering if a `CommandLineParser` library was introduced for two verbs (`migrate`, `reconcile`). Based on SUMMARY-3.1 evidence (simple `args[0]` switch/if dispatch), the current approach is appropriate. At exactly 2 verbs the Rule of Three has not triggered; no extraction warranted.
- **Suggestion:** No action. If a third verb ever lands, extract a shared argument-validation helper at that point.

## Patterns Avoided (positive notes)

- **DryRun duplication between BlueIris and Pushover is justified.** Both plugins have an identical `DryRun: false` guard + `LogDryRun` LoggerMessage definition. With only two plugins, per-plugin `EventId` values carry diagnostic value that a shared `IDryRunCapable` interface would sacrifice. The copy-paste is intentional and the Rule of Three has not triggered (2 occurrences). Not a finding.

- **Hand-rolled `IniReader` in MigrateConf is justified.** `Microsoft.Extensions.Configuration.Ini` collapses duplicate section keys, which would corrupt multi-subscription INI files from the legacy service. The custom reader preserving section-ordered key lists is the correct approach. Not a finding.

- **2-verb CLI dispatch in `Program.cs` needs no library.** Introducing `System.CommandLine` or `CommandLineParser` for two subcommands would be over-engineering. Current inline dispatch is appropriately minimal.

- **NDJSON sink as opt-in branch in `HostBootstrap.ApplyLoggerConfiguration`.** The `CompactJson` conditional was flagged as a potential dead-branch smell. It is live: the branch is the parity-window enablement path and is exercised by the operator checklist. Not dead code.

## Coverage
- Files reviewed: ~25 (tools/, tests/MigrateConf.Tests/, src/FrigateRelay.Plugins.{BlueIris,Pushover}/, src/FrigateRelay.Host/, docs/, root)
- Plans reviewed: 8/8 (1.1, 1.2, 1.3, 1.4, 1.5, 2.1, 3.1, 3.2)
