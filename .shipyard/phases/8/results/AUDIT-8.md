# Security Audit — Phase 8

## Verdict: PASS

**Risk Level: Low**

Phase 8 introduces profile expansion, a TypeConverter fix for `IConfiguration.Bind`, and a sanitized legacy fixture. No secrets are committed, no exploitable code paths were introduced, and no new dependencies were added. The only finding is an advisory-level log injection note arising from operator-controlled config values flowing into exception messages — this requires the attacker to already have write access to the configuration file, so it presents no realistic attack surface in the threat model.

## Critical Findings

None.

## Important Findings

None.

## Low / Notes

### N1 — Operator-controlled names embedded in exception messages without newline sanitization
- **Location:** `src/FrigateRelay.Host/StartupValidation.cs` lines 43, 76–78, 101–104, 111–114, 121–124, 154–157
- **Description:** Subscription names, profile names, plugin names, and validator keys sourced from `appsettings.json` are interpolated directly into `InvalidOperationException` messages using `string.Join("\n  - ", errors)`. A subscription name containing a literal `\n  - ` sequence would produce a multi-line error message that superficially resembles an additional validation failure in structured log output. (CWE-117 — Improper Output Neutralization for Logs)
- **Exploitability:** Negligible in practice. An attacker capable of injecting arbitrary strings into `appsettings.json` already controls the process configuration. The concern is log-forensics confusion, not code execution.
- **Remediation (deferred acceptable):** When constructing the aggregated error, sanitize embedded config-sourced values by replacing newline characters: `sub.Name.Replace("\n", "\\n").Replace("\r", "\\r")`. Alternatively, use structured logging (pass the errors list as a structured parameter) instead of embedding in the exception message string.

### N2 — `ActionEntryTypeConverter` accepts empty and whitespace-only plugin names
- **Location:** `src/FrigateRelay.Host/Configuration/ActionEntryTypeConverter.cs` line 34
- **Description:** `ConvertFrom` passes any string — including `""` and `"   "` — to `new ActionEntry(s)`. The value only flows into a case-insensitive DI name lookup in `StartupValidation.ValidateActions`, so it results in a startup error rather than runtime misbehavior. However, a whitespace plugin name produces a confusing error message: `"references unknown action plugin '   '"`.
- **Exploitability:** None — fails fast at startup.
- **Remediation (deferred acceptable):** Add a guard: `if (string.IsNullOrWhiteSpace(s)) throw new InvalidOperationException("Action plugin name cannot be empty or whitespace.");`. Produces a cleaner diagnostic.

### N3 — Secret scan does not cover RFC 1918 class A (`10.x.x.x`) or class B (`172.16-31.x.x`) ranges
- **Location:** `.github/scripts/secret-scan.sh` line 42
- **Description:** The scan pattern `192\.168\.[0-9]{1,3}\.[0-9]{1,3}` only covers the RFC 1918 class C range. A developer accidentally committing `10.0.0.5` or `172.16.0.1` would not be caught.
- **Current exposure:** No such IPs appear in the committed tree — confirmed by manual review.
- **Remediation (deferred acceptable):** Add two additional patterns to the `PATTERNS` array and corresponding entries in `LABELS` and the fixture file: `10\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}` and `172\.(1[6-9]|2[0-9]|3[01])\.[0-9]{1,3}\.[0-9]{1,3}`. Per CLAUDE.md, a fixture line must accompany each new pattern or the tripwire self-test will fail.

## Scan Evidence

- **Secret scan (manual pattern review):** `legacy.conf` uses `192.0.2.x` (RFC 5737 TEST-NET-1) throughout — compliant. `AppToken =` and `UserKey =` are blank. `appsettings.Example.json` contains no credentials, no IPs, no URLs with embedded auth.
- **Git history of `legacy.conf`:** Single commit (added already sanitized); no prior version with real IPs exists in history.
- **Dependency changes:** Zero new `<PackageReference>` elements. Only `<None>` fixture-copy entries added to `tests/FrigateRelay.Host.Tests/FrigateRelay.Host.Tests.csproj`.
- **IaC changes:** None — no Docker, Terraform, or Kubernetes files in the diff.
- **`<Link>` build artifact path:** Build-time copy only (`CopyToOutputDirectory`); no runtime assembly loader path involved.
- **`InternalsVisibleTo` form:** MSBuild item form used correctly (`DynamicProxyGenAssembly2`); no source-level `[assembly:]` attributes added.

## Recommendations

1. Sanitize newlines from config-sourced values before embedding in exception messages (N1) — small effort, improves log forensics clarity.
2. Add an empty/whitespace guard to `ActionEntryTypeConverter.ConvertFrom` (N2) — trivial effort.
3. Extend the secret scan to cover RFC 1918 class A and B ranges (N3) — small effort; requires a matching fixture line per pattern per the tripwire requirement in CLAUDE.md.

None of these findings block the phase from proceeding to `/shipyard:ship` or to Phase 9.
