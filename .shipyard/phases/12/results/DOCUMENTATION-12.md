# Documentation Review — Phase 12

**Phase:** 12 — Parity Cutover
**Date:** 2026-04-28
**Verdict:** NEEDS_DOCS (one inaccuracy to fix; two CLAUDE.md gaps worth adding)
**Diff base:** post-plan-phase-12..HEAD

---

## Summary

Phase 12 is heavily doc-centric. All major deliverables are present, complete, and cross-linked.
One factual inaccuracy in `docs/parity-window-checklist.md` (the `@i` discriminator claim) must
be corrected before operators run the parity window — it will cause the manual grep/tail sanity
check to fail silently. Two CLAUDE.md additions are worth making now while the conventions are
fresh. Everything else is acceptable or deferred.

- API/Code docs: 2 properties documented with XML `<summary>` (BlueIrisOptions.DryRun,
  PushoverOptions.DryRun). No public API surface leaked from `tools/`.
- Architecture updates: none (D4 constraint honoured — no architecture/operations/config-reference
  docs added in Phase 12).
- User-facing docs: 5 new/updated files (migration guide, parity checklist, parity report
  template, RELEASING.md, README migration section).

---

## Public API Surface

| Type | Assembly | Access | XML doc? | Notes |
|---|---|---|---|---|
| `BlueIrisOptions.DryRun` | `FrigateRelay.Plugins.BlueIris` | `public bool` | Yes — full `<summary>` | Operator-facing config key |
| `PushoverOptions.DryRun` | `FrigateRelay.Plugins.Pushover` | `public bool` | Yes — full `<summary>` | Operator-facing config key |
| `Reconciler` | `tools/FrigateRelay.MigrateConf` | `internal static class` | N/A | Correct; tool-internal |
| `Reconciler.ActionRow` | same | `public sealed record` (nested in internal class) | No | Nested-public inside internal class is effectively internal; no public API leak |
| `Reconciler.ReconcileReport` | same | `public sealed record` (nested) | No | Same — effectively internal |
| `IniReader` | same | `internal static class` | N/A | Correct |
| `AppsettingsWriter` | same | `internal static class` | N/A | Correct |
| `Program` | same | `internal static class` | N/A | Correct |

No accidental public API surface leak from the tools project. The `public` modifiers on
`Reconciler.ActionRow` and `Reconciler.ReconcileReport` are nested inside an `internal` class and
are test-accessible (the test project references the csproj directly) but are not visible to
external consumers. No action required on visibility.

---

## Phase 12 Deliverables Coverage

| Deliverable | Status | Notes |
|---|---|---|
| `docs/migration-from-frigatemqttprocessing.md` | ACCEPTABLE | All 3 INI sections covered; appsettings paths accurate; secrets table complete; Camera-field caveat well explained |
| `docs/parity-window-checklist.md` | NEEDS FIX | `@i` discriminator claim is factually wrong — see Recommendations |
| `docs/parity-report.md` | ACCEPTABLE | Template is minimal but sufficient; reconcile subcommand command shown; sign-off gate stated |
| `RELEASING.md` | ACCEPTABLE | Pre-flight covers all 9 items; tag commands exact; post-release verification present; rollback procedure present including GHCR package deletion; advisory ID-24 flagged |
| `CHANGELOG.md [Unreleased]` | ACCEPTABLE | All Phase 12 changes listed (DryRun flags, MigrateConf tool, reconcile subcommand, docs set, NDJSON sink); format matches prior phases |
| `README.md migration section` | ACCEPTABLE | Correct placement (after config concepts, before plugin authoring); all 5 cross-links resolve; tool invocation command shown |
| `BlueIrisOptions.DryRun` XML doc | ACCEPTABLE | EventId number and name accurate (203, "BlueIrisDryRun"); default false stated |
| `PushoverOptions.DryRun` XML doc | ACCEPTABLE | EventId number and name accurate (4, "PushoverDryRun"); default false stated |
| `tools/FrigateRelay.MigrateConf/` | ACCEPTABLE | No user-facing doc surface; migration guide covers the operator workflow adequately |
| `tests/FrigateRelay.MigrateConf.Tests/` | ACCEPTABLE | Test project; no doc surface |
| `HostBootstrap.cs` NDJSON sink | ACCEPTABLE | Covered in CHANGELOG Changed section and in parity checklist overlay block |

---

## Cross-Reference Web

| Link | Source | Target | Resolves? |
|---|---|---|---|
| Migration doc → parity checklist | `docs/migration-from-frigatemqttprocessing.md` L174 | `docs/parity-window-checklist.md` | Yes |
| Migration doc → parity report | `docs/migration-from-frigatemqttprocessing.md` L173 | `docs/parity-report.md` | Yes |
| Migration doc → example output | L172 | `config/appsettings.Example.json` | Yes (file exists) |
| Parity checklist → migration doc | `docs/parity-window-checklist.md` L31 | `docs/migration-from-frigatemqttprocessing.md` | Yes |
| Parity checklist → Wave 3 / RELEASING | checklist L218 | RELEASING.md (implied via ROADMAP) | Indirect only — no direct link from checklist to RELEASING.md |
| Parity report template → checklist | `docs/parity-report.md` L6 | `docs/parity-window-checklist.md` | Yes |
| RELEASING.md → parity checklist | `RELEASING.md` L14 | `docs/parity-window-checklist.md` | Yes (markdown link) |
| RELEASING.md → parity report | `RELEASING.md` L13 | `docs/parity-report.md` | Yes (implicit path reference) |
| README → all 5 docs | `README.md` L93-102 | All five files | Yes |
| CHANGELOG comparison link | `CHANGELOG.md` L239 | `compare/HEAD...HEAD` | Stale placeholder — HEAD...HEAD compares nothing |

The checklist's "After 48 hours" section tells operators to run `/shipyard:resume` and mentions
that Wave 3 will produce `RELEASING.md`, but does not link to `RELEASING.md` directly. This is
acceptable: the checklist's audience is the parity-window operator, not the release engineer.
RELEASING.md is linked from the README and from RELEASING.md itself.

The CHANGELOG `[unreleased]` comparison link (`HEAD...HEAD`) is a known placeholder from initial
setup. It will be corrected when the `[Unreleased]` entry is promoted to `[1.0.0]` per the
RELEASING.md Step 1 instructions. No action needed before that step.

---

## CLAUDE.md Currency

**Project state paragraph (line 10):** States "Phase 11 (this phase)" and "Phase 12 is the
parity-cutover gate before v1.0.0." Phase 12 is now complete (or in-flight). The paragraph
should be updated to reflect Phase 12 complete status, but this is a ship-time concern, not a
blocker.

**Conventions section:** Two Phase 12 patterns are absent and worth recording:

1. **Hand-rolled `IniReader` pattern** — `Microsoft.Extensions.Configuration.Ini` was not used
   because its section-collapse semantics flatten repeated `[SubscriptionSettings]` blocks into
   the last value. `tools/FrigateRelay.MigrateConf/IniReader.cs` implements a minimal reader that
   preserves duplicate section names as a list. Future maintainers adding config sources with
   multi-section INI input should follow this pattern rather than reaching for M.E.C.Ini.

2. **DryRun config flag pattern** — `BlueIrisOptions.DryRun` / `PushoverOptions.DryRun` establish
   the convention: a `bool DryRun { get; init; }` on the plugin's `Options` class, checked at the
   top of `ExecuteAsync`, emitting a named `LoggerMessage.Define`-backed log entry at Info level
   with a distinct `EventId` (name and number), returning success. This is the reusable pattern
   for any future "shadow mode" or "audit mode" plugin operation. Naming convention:
   `{PluginName}DryRun` EventId name, Id allocated sequentially in the plugin's existing range.

3. **NDJSON Serilog sink opt-in** — `Logging:File:CompactJson` added to `HostBootstrap.cs`. The
   convention is: operator flips `true` in their `appsettings.Local.json` for structured-log
   audit periods (parity window, incident investigation). Default `false` preserves human-readable
   rolling file output. The `CompactJsonFormatter` from `Serilog.Formatting.Compact` is the
   designated formatter — do not introduce a second formatter for this purpose.

Whether to add these to CLAUDE.md is architect-discretion; they are flagged here for
ship-time consideration.

---

## Recommendations

### Generate now (blocker-grade)

#### Fix `@i` discriminator claim in `docs/parity-window-checklist.md`

**File:** `docs/parity-window-checklist.md`, lines 105-109.

**Problem:** The checklist states:

> With `CompactJson: true` the file is NDJSON. The EventId name is rendered at the `"@i"` key by
> Serilog's `CompactJsonFormatter`.

and shows a synthetic example:

```json
{"@t":"...","@mt":"...","@i":"BlueIrisDryRun","Camera":"driveway",...}
```

This is factually wrong. Serilog's `CompactJsonFormatter` renders `@i` as a **hex Murmur3 hash**
of the message template string — never an EventId name string. The actual NDJSON output from
`LoggerMessage.Define` with `new EventId(203, "BlueIrisDryRun")` is not confirmed in this diff;
the correct field for EventId name discrimination is likely either a top-level `"EventId"` object
property (e.g. `{"Id":203,"Name":"BlueIrisDryRun"}`) or a flattened `"EventId.Name"` property,
depending on Serilog's EventId destructuring. PLAN-3.1 contains an explicit reviewer warning
(lines 7-25) identifying this exact problem.

**Impact:** The operator's sanity-check commands use grep patterns that key on the `@i` field:

```bash
tail -f logs/frigaterelay-*.log | grep -E '"BlueIrisDryRun"|"PushoverDryRun"'
```

If this field does not exist or has the value `"cb2e5cf7"` (hash) rather than `"BlueIrisDryRun"`,
the grep returns zero results and the operator concludes the parity window is broken — a false
negative that could abort a valid run.

**Correction needed:** Run the host with `Logging:File:CompactJson: true` against a local MQTT
event and inspect one actual NDJSON line to determine the correct field name. Then update lines
105-109 of the checklist and the two `grep` commands on lines 178 and 195 to match the real
output shape. Until then, add a warning note that the `@i` example may not match real output and
that the operator should inspect an actual log line before trusting the grep.

This is a **blocker-grade doc fix** because it will cause the parity window sanity check to fail
for an operator following the checklist verbatim.

---

### Defer to post-v1.0.0 docs sprint

- Architecture diagrams (ID-9 family) — D4 constraint; explicitly out of scope for Phase 12.
- Operator config reference (full field enumeration for all plugins) — ID-9 deferred.
- `tools/FrigateRelay.MigrateConf/` user-facing README or man-page — tool is source-built only
  (no binary distribution); migration guide plus inline `--help` are sufficient for v1.0.0.
- CLAUDE.md `## Project state` paragraph update to reflect Phase 12 complete — ship-time task.
- CLAUDE.md three new Conventions entries (IniReader, DryRun pattern, NDJSON opt-in) — architect
  decision; these are patterns, not breaking omissions.

---

### Dismiss

- `docs/parity-report.md` cooldown-match section absent — the template contains exactly what a
  Wave 3 builder needs; the reconciler subcommand populates it. No additional scaffolding required.
- XML docs on `Reconciler.ActionRow` / `Reconciler.ReconcileReport` — nested inside an `internal`
  class; no external consumer. Dismiss.
- Missing direct link from parity checklist close-out steps to `RELEASING.md` — the checklist's
  post-48h step routes through `/shipyard:resume`, not directly to RELEASING.md. README provides
  the link for operators who need it.

---

## Coverage

| Surface | Type | Status |
|---|---|---|
| `docs/migration-from-frigatemqttprocessing.md` | How-to | Acceptable |
| `docs/parity-window-checklist.md` | How-to | Needs @i fix (blocker) |
| `docs/parity-report.md` | Reference (template) | Acceptable |
| `RELEASING.md` | How-to | Acceptable |
| `CHANGELOG.md [Unreleased]` | Reference | Acceptable |
| `README.md` migration section | How-to | Acceptable |
| `BlueIrisOptions.DryRun` XML `<summary>` | Reference | Acceptable |
| `PushoverOptions.DryRun` XML `<summary>` | Reference | Acceptable |
| CLAUDE.md currency (Phase 12 conventions) | Internal | Flag for ship-time |
| Architecture / config reference | Explanation / Reference | Deferred (D4) |
