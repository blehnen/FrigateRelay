# Security Audit — Phase 1

## Severity summary
Critical: 0 | High: 0 | Medium: 0 | Low: 1 | Info: 2

## Findings

### CRITICAL
None.

### HIGH
None.

### MEDIUM
None.

### LOW

**[L1] `appsettings.Local.json` exclusion pattern is file-name exact, not path-glob**
- **Location:** `.gitignore:449`
- **Description:** The entry `appsettings.Local.json` (no leading `**/`) only ignores the file at the repo root. The canonical location is `src/FrigateRelay.Host/appsettings.Local.json`. On most Git implementations this still works because Git matches bare filenames against any path component, but the intent is ambiguous and differs from the `**/[Bb]in/*` style used elsewhere in the same file.
- **Impact:** Low — if Git's bare-name matching does NOT apply (some edge-case clients / CI tooling), a developer's local override containing secrets could be committed. (CWE-312)
- **Remediation:** Change line 449 to `**/appsettings.Local.json` to be unambiguous and consistent with the rest of the .gitignore style.

### INFO

**[I1] No CI secret-scan workflow yet**
- **Location:** `.github/workflows/` (absent)
- **Description:** CLAUDE.md invariant (line 94) and PROJECT.md NFR (line 82) both call for a `secret-scan.yml` GitHub Actions workflow that greps for `AppToken`, `UserKey`, API-key shapes, and `192.168.x.x`. The workflow does not exist yet. CI is scoped to a later phase, so this is not a current defect, but the control is documented as required.
- **Remediation:** Add `.github/workflows/secret-scan.yml` in the CI phase. Until then, the `git grep` check from PROJECT.md can be run manually pre-merge.

**[I2] FluentAssertions 6.12.2 — license pin is intentional; confirm no known CVEs on adoption**
- **Location:** `tests/*/FrigateRelay.*.Tests.csproj`
- **Description:** FluentAssertions 6.12.2 is pinned below the v8 commercial-license boundary, as documented in PROJECT.md. `dotnet list package --vulnerable` returned no CVEs for this version. This is informational only — the pin is deliberate and correct.
- **Remediation:** None required. Re-check at each phase if a CVE is published against 6.x.

## Checks performed

| Check | Result |
|-------|--------|
| Secrets scan (`git grep` for api_key, token, password, secret, 192.168.x, 10.x.x, credentialed URLs) | PASS — zero matches in src/, tests/, *.json, *.props |
| `appsettings.json` content review | PASS — logging config only; no secrets, no URLs, no IPs |
| `appsettings.Local.json` in .gitignore | PASS (with advisory caveat — see L1) |
| `.env` in .gitignore | PASS — line 12 covers `*.env` |
| Hard-coded IP 192.168.0.58 grep | PASS — zero matches |
| `ServicePointManager.ServerCertificateValidationCallback` grep | PASS — zero matches |
| `AppToken` / `UserKey` grep | PASS — zero matches |
| OWASP Top 10 surface review | N/A — no HTTP, no SQL, no user input, no deserialization of untrusted data in Phase 1 |
| Dependency vulnerability scan — all 4 projects | PASS — `dotnet list package --vulnerable --include-transitive` returned no vulnerable packages for any project |
| IaC / Docker review | N/A — no IaC in Phase 1 |
| Configuration security | PASS — no debug flags, no verbose errors, no CORS config committed |

## Legacy concerns — structural resolution

| CONCERNS.md concern | Status in new code | Evidence |
|---------------------|--------------------|----------|
| Global `ServicePointManager.ServerCertificateValidationCallback` TLS bypass | Structurally impossible — no `System.Net` usage, no `HttpClient`, no networking in Phase 1 | `grep` returns zero matches across src/ and tests/ |
| Plaintext Pushover `AppToken` / `UserKey` in config | Resolved — no Pushover plugin exists; `appsettings.json` contains only log-level config; PROJECT.md mandates env-var/secret injection | `appsettings.json` reviewed; no credential fields present |
| Hard-coded IP `192.168.0.58` | Resolved — zero matches in all committed files | `git grep '192\.168\.'` returns zero hits in src/tests |
| No automated tests | Resolved — 17 tests across 2 test projects cover Verdict, EventContext, SnapshotRequest/Result, PluginRegistrarRunner, PlaceholderWorker | `tests/` directory confirmed |
| No version control | Resolved — git repo with signed conventional commits | Branch `Initcheckin`, 9 commits in Phase 1 |

## Dependency review

| Package | Version | Vuln status |
|---------|---------|-------------|
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.0 | No CVEs |
| Microsoft.Extensions.Configuration.Abstractions | 10.0.0 | No CVEs |
| Microsoft.Extensions.Hosting | 10.0.7 | No CVEs |
| Microsoft.Extensions.Configuration.UserSecrets | 10.0.7 | No CVEs |
| Microsoft.Extensions.Logging.Abstractions | 10.0.7 | No CVEs |
| Microsoft.Extensions.Configuration | 10.0.7 | No CVEs |
| Microsoft.Extensions.DependencyInjection | 10.0.7 | No CVEs |
| MSTest | 4.2.1 | No CVEs |
| FluentAssertions | 6.12.2 | No CVEs (license pin intentional — see I2) |
| NSubstitute | 5.3.0 | No CVEs |
| NSubstitute.Analyzers.CSharp | 1.0.17 | No CVEs (analyzer-only, build-time) |

All versions are exact pins, not ranges. Lock files (NuGet package restore with exact versions in csproj) are consistent.

## Recommendations

1. **(L1 — Low effort)** Prefix `.gitignore` entry with `**/`: change `appsettings.Local.json` → `**/appsettings.Local.json`.
2. **(I1 — CI phase)** Implement `.github/workflows/secret-scan.yml` as specified in CLAUDE.md invariants when the CI phase is reached.
3. **(Future phases)** When `HttpClient` is introduced for BlueIris/Pushover plugins, enforce the per-plugin `SocketsHttpHandler.SslOptions` pattern required by PROJECT.md NFR — never a global `ServicePointManager` bypass.
