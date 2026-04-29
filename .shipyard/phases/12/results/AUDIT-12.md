# Security Audit — Phase 12

**Phase:** 12 — Parity Cutover (final phase before v1.0.0)
**Date:** 2026-04-28
**Auditor:** shipyard:auditor (claude-sonnet-4-6)
**Verdict:** PASS_WITH_NOTES
**Risk level:** Low
**Diff base:** pre-build-phase-12..HEAD

---

## Summary

Phase 12 adds two operator-controlled DryRun flags (default `false`), an opt-in NDJSON Serilog sink, a standalone offline migration console tool, and operator documentation. No new network surfaces, no new auth code paths, no secrets introduced. Three advisory items are noted for tracking; none block ship.

---

## Critical Findings (block ship)

None.

---

## Important Findings (should fix this phase)

None.

---

## Advisory / Low Findings (defer with tracking)

### [A1] MigrateConf accepts unvalidated `--input` / `--output` paths (proposed ID-28)

- **Location:** `tools/FrigateRelay.MigrateConf/Program.cs:31` (`IniReader.Read(input)`) and `:33` (`File.WriteAllText(output, json)`)
- **Description:** The `migrate` and `reconcile` subcommands pass `--input`, `--output`, `--frigaterelay`, `--legacy` values directly to `File.ReadAllLines` / `File.WriteAllText` without canonicalization or bounds checks. An operator supplying a relative path like `../../etc/cron.d/evil` as `--output` would write there as their own user.
- **Impact:** Operator-self-inflicted path traversal (CWE-22). The tool is offline and runs as the invoking user, so no privilege escalation is possible beyond what the operator already holds. Risk is negligible in practice but inconsistent with the `ValidateSerilogPath` hardening precedent (ID-21/Phase 10).
- **Remediation:** At CLI parse time, call `Path.GetFullPath(value)` and assert the result does not contain `..` segments, or simply document (per RELEASING.md) that paths are trusted operator input. A two-line guard at `RunMigrate` / `RunReconcile` entry suffices:
  ```csharp
  var fullInput = Path.GetFullPath(input);
  var fullOutput = Path.GetFullPath(output);
  ```
- **Effort:** Trivial

### [A2] DryRun structured log payload contains MQTT-sourced camera/label values (carry-over ID-19)

- **Location:** `src/FrigateRelay.Plugins.BlueIris/BlueIrisActionPlugin.cs:25` (`LogDryRun`), `src/FrigateRelay.Plugins.Pushover/PushoverActionPlugin.cs:115` (`_wouldExecute`)
- **Description:** Both DryRun log messages emit `Camera` and `Label` values drawn from MQTT event payloads. If an attacker controls the MQTT broker (or publishes crafted events), they can inject arbitrary values into the NDJSON audit log, which the reconciler then parses. This is the same vector as existing ID-19 (OTel/log span tag injection). No new surface — the non-DryRun path emits these same values in existing log messages. However, the reconciler specifically parses `Camera` and `Label` out of these NDJSON entries, making the log-injection concern slightly more concrete in Phase 12 than it was in Phase 9.
- **Impact:** Log spoofing / reconciler result manipulation if MQTT broker is compromised (CWE-117). Outside threat model for a home-lab operator controlling their own broker.
- **Remediation:** Carry-over — address in the ID-13/14/19 structured-logging hardening pass. No new action needed this phase.
- **Effort:** Small (existing deferred item)

### [A3] `docs/migration-from-frigatemqttprocessing.md` uses RFC 1918 documentation-class IP `192.0.2.53` in examples

- **Location:** `docs/migration-from-frigatemqttprocessing.md:97,109,113`
- **Description:** The migration guide's `Camera` field examples use `192.0.2.53` (an IANA TEST-NET-1 documentation address, not RFC 1918). This is correct practice — documentation IPs should be from `192.0.2.0/24`, `198.51.100.0/24`, or `203.0.113.0/24` (RFC 5737). No issue. Noted here to confirm the secret-scan tripwire (which catches `192.168.x.x`) does NOT flag this address, consistent with ID-15's open gap (class-A/B coverage missing). No remediation needed for this doc; the gap is already tracked under ID-15.
- **Impact:** Informational — no security risk. Confirms ID-15 remains the correct tracking vehicle.
- **Remediation:** None required. Carry ID-15 forward.
- **Effort:** N/A

---

## CLAUDE.md Invariant Compliance

| Invariant | Check | Result |
|-----------|-------|--------|
| No `.Result` / `.Wait()` in `src/` | `BlueIrisActionPlugin`, `PushoverActionPlugin`, `HostBootstrap` all use `await` / `ConfigureAwait(false)` — no synchronous blocking | PASS — 0 hits |
| No `ServicePointManager` in `src/` | No new `HttpClient` wiring added; existing per-plugin `SocketsHttpHandler` pattern unchanged | PASS — 0 hits |
| No `App.Metrics` / `OpenTracing` / `Jaeger.*` in `src/` | No new observability dependencies added; `Serilog.Formatting.Compact 2.0.0` is the only new package | PASS — 0 hits |
| No hard-coded IPs / RFC 1918 in committed config or source | Doc examples use `192.0.2.53` (RFC 5737 documentation range, not RFC 1918); `AppsettingsWriter` emits `http://example.invalid/` placeholder for trigger URL | PASS — 0 hits |
| No secrets in committed `appsettings.json` | `AppsettingsWriter.Build` emits empty-string placeholders for `AppToken` and `UserKey`; parity-window docs instruct env-var supply | PASS |
| DryRun default is `false` (production-safe) | `BlueIrisOptions.DryRun` and `PushoverOptions.DryRun` both default to `false` (bool value-type zero default) | PASS |

---

## Cross-Component Analysis

**DryRun + reconciler coherence:** The reconciler (`Reconciler.ReadFrigateRelayNdjson`) discriminates DryRun entries by matching `@mt` prefix (`"BlueIris DryRun"` / `"Pushover DryRun"`). The log message templates in `BlueIrisActionPlugin` (line 25) and `PushoverActionPlugin` (line 115) produce exactly those prefixes. The coupling is string-literal based with no shared constant — a future rename of either message template would silently break the reconciler without a compile error. Low risk given both are in-repo, but worth a comment.

**DryRun + `CompactJson` interlock:** The parity-window checklist correctly instructs enabling both flags together, and the RELEASING.md pre-release checklist explicitly requires removing both before v1.0.0 cutover (items 7 and 8). The interlocking is documented but not enforced at startup — startup validation does not warn when `DryRun: true` is set in a `Production` environment. This is consistent with Phase 12's scope (operator tool, not service hardening) and acceptable for v1.

**NDJSON sink path:** The new `CompactJsonFormatter` path reuses the same hard-coded `"logs/frigaterelay-.log"` path as the existing text sink, so `ValidateSerilogPath` (Phase 10, ID-21 closed) covers it without any change. No new gap.

**MigrateConf has no network access:** The tool is a pure filesystem read/write utility with no `HttpClient`, no DI, no MQTT. Its attack surface is limited to the operator-supplied file paths (A1 above) and the legacy INI content it parses. The hand-rolled `IniReader` does no eval, no deserialization of complex types, and produces only `string` key-value pairs — no injection vector from INI content to JSON output beyond what `JsonSerializer.Serialize` (via `Esc()`) already escapes.

---

## Deferred Items (recommend new ISSUES IDs)

### ID-28 (new): MigrateConf path canonicalization

- **Source:** auditor (Phase 12 AUDIT-12, 2026-04-28)
- **Severity:** Low / Advisory (CWE-22, operator-self-inflicted)
- **Description:** `tools/FrigateRelay.MigrateConf/Program.cs` passes `--input` / `--output` / `--frigaterelay` / `--legacy` values directly to file I/O without `Path.GetFullPath` canonicalization. No privilege escalation possible (tool runs as invoking operator); inconsistency with Phase 10 `ValidateSerilogPath` precedent.
- **Fix:** Call `Path.GetFullPath(value)` at CLI parse in `RunMigrate` and `RunReconcile` before passing to `IniReader.Read` / `File.WriteAllText`. Four lines total.
- **Reactivation:** Any future CLI hardening pass for the tools/ tree.

Existing open items carried forward without change: ID-13, ID-14, ID-15, ID-18, ID-19, ID-20, ID-22, ID-24, ID-25, ID-26, ID-27.

---

## Audit Coverage

| Area | Status | Notes |
|------|--------|-------|
| Code Security (OWASP) | Yes | BlueIris + Pushover DryRun paths, MigrateConf IniReader + AppsettingsWriter, Reconciler CSV/NDJSON parsers reviewed |
| Secrets & Credentials | Yes | All new files scanned; no committed secrets; AppsettingsWriter emits placeholders; docs use placeholder values |
| Dependencies | Yes | One new package: `Serilog.Formatting.Compact 2.0.0` (no known CVEs; current as of 2026-04-28). MigrateConf has zero runtime deps beyond .NET 10 stdlib |
| IaC / Container | N/A | No Dockerfile, compose, or workflow changes in Phase 12 |
| Configuration | Yes | DryRun defaults `false`; CompactJson defaults `false`; RELEASING.md checklist requires removal before cutover |
| STRIDE | Yes | No new spoofing/EoP surfaces. Log injection (T/I) covered by A2 / ID-19 carry-over. DoS: no unbounded resource paths in new code |
