# Phase 15 Security Audit

**Date:** 2026-05-07
**Branch:** `feature/phase-15-v1.2.1` (10 commits + 1 review-followup ahead of `main`)
**Phase theme:** Security hardening — closing CWE-117 (#13/#19), CWE-183 (#20), CWE-22 residual (#27), CWE-829/SLSA L2+ (#24), and extending the secret-scan tripwire (#15).
**Author:** Orchestrator (auditor agent terminated mid-execution; audit performed directly)

## Verdict: NO_CRITICAL_FINDINGS

Phase 15 is itself a security hardening phase. The audit confirms each closure was correctly implemented and that no new security surface was introduced.

## OWASP Top 10 coverage

| Category | Phase 15 impact | Audit result |
|---|---|---|
| A01 Broken access control | No auth changes | N/A |
| A02 Cryptographic failures | No crypto changes | N/A |
| A03 Injection | #13 sanitize + #19 name allowlist (CWE-117 closure) | **STRENGTHENED.** Sanitize helper escapes (not strips) `\r`/`\n` so a name with a newline can no longer split a structured-log line into two. ValidateNames regex `^[A-Za-z0-9_. -]+$` rejects CRLF, control chars, slashes, colons, at-signs at startup before they reach a span-tag write site. |
| A05 Security misconfiguration | Docker compose binding doc + mosquitto WARNING | **STRENGTHENED.** docker-compose.example.yml comment recommends `127.0.0.1:8080:8080` for untrusted networks; mosquitto-smoke.conf has prominent multi-line WARNING calling out anonymous-only / CI-only character. |
| A06 Vulnerable / outdated components | SHA-pinned 3rd-party Actions | **STRENGTHENED.** 17 sites pinned across 4 workflow files. Dependabot (`github-actions` ecosystem, weekly Monday) maintains SHA bumps as PRs going forward. Closes the supply-chain attack surface where a compromised tag could re-point at malicious code with no merge-time signal. |
| A07 Identification / authn | No auth changes | N/A |
| A08 Software/data integrity | SHA-pin closure (A06 above) covers this | **STRENGTHENED.** |
| A09 Logging / monitoring failures | #13 sanitize closes log-line spoofing via CRLF in operator-controlled values | **STRENGTHENED.** |
| A10 SSRF | OTLP scheme allowlist (#20) restricts to http/https/grpc | **STRENGTHENED indirectly.** `file://` and `ftp://` schemes blocked at startup — operator can no longer accidentally point an OTLP exporter at a local file (information disclosure path) or external FTP server. |

## Secrets scanning across changed files

`bash .github/scripts/secret-scan.sh scan` exits 0 — no secret-shaped strings in tracked files.

`git diff main..HEAD` reviewed for newly-introduced secret-shaped patterns:
- No API-key-shaped strings (40-hex tokens are GitHub Action SHAs; documented and Dependabot-maintained, not credentials).
- No hardcoded IPs in source/tests/docker. The 2 RFC 1918 fixture lines (`10.0.1.100`, `172.16.0.50`) are exclusively in `.github/secret-scan-fixture.txt` and the secret-scan workflow's tripwire-self-test job depends on them being there.
- No passwords or tokens in committed configs (default `""` for secret fields preserved).

The new RFC 1918 patterns in `secret-scan.sh` (10.x, 172.16-31.x) **expand** the secret-scan tripwire's coverage; the existing `192.168.x.x` rule is unchanged. The secret-scan posture is strictly improved.

## Dependency changes

`git diff main..HEAD -- 'src/**/*.csproj' 'tests/**/*.csproj' 'samples/**/*.csproj'` — empty. Phase 15 added zero NuGet packages.

`Directory.Packages.props` (CPM) — unchanged. No version bumps.

FluentAssertions remains pinned at 6.12.2 (license-critical invariant per CLAUDE.md).

## IaC / infrastructure changes

| File | Change | Security impact |
|---|---|---|
| `docker/mosquitto-smoke.conf` | Multi-line WARNING header added; `allow_anonymous true` and `0.0.0.0` listener unchanged | Improves operator awareness; no functional change |
| `docker/docker-compose.example.yml` | Comment recommending 127.0.0.1 binding; default `8080:8080` mapping unchanged | Improves operator awareness; no functional change |
| `.github/workflows/release.yml`, `ci.yml`, `secret-scan.yml`, `docs.yml` | 17 SHA-pin replacements | Strengthens supply-chain integrity; no functional change |
| `.github/scripts/secret-scan.sh`, `.github/secret-scan-fixture.txt` | 2 new patterns + 2 new fixture lines | Strengthens tripwire coverage; tripwire-self-test job already enforces fixture parity |
| `.github/scripts/run-tests.sh` | `--coverage` branch passthrough fix (#8) + fast-mode `--` separator fix (review followup) | Functional fix only; no security implication |

`docker compose -f docker/docker-compose.example.yml config` exits 1 due to required `.env` (pre-existing condition, by design). No new IaC functional defect.

## Configuration security

`config/appsettings.Example.json` — unchanged. CONTEXT-15 D1 confirmed all 9 spaced subscription names + 1 profile name pass the new `^[A-Za-z0-9_. -]+$` regex. No operator config breakage.

`appsettings.json`, `appsettings.Docker.json` — unchanged.

Secret-shaped fields (e.g. `Pushover.AppToken`, `BlueIris.TriggerUrlTemplate` user/pass) remain `""` defaults, env-var-supplied at runtime.

## Cross-task security coherence

- **#13 + #19 are intentionally complementary.** #19 is the structural fix (illegal characters never reach the boundary); #13 is the defensive fix (any future code path that interpolates an operator string into a log/span tag is safe by default). The `Sanitize` helper is `internal static` and applied consistently across both `StartupValidation.cs` and `ProfileResolver.cs`. The `ValidateNames` regex pass runs at "Pass 0.5" — between observability validation and profile resolution — so profile keys are checked pre-resolution.
- **#20 covers both OTLP endpoint paths.** `ValidateObservability` reads `config["Otel:OtlpEndpoint"] ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")` — same precedence as `HostBootstrap`. Whichever value would be consumed at runtime is the value validated. `Serilog:Seq:ServerUrl` not gated on scheme by design (Seq is HTTP-only).
- **#27 layered with #21** (Phase 10 `ValidateSerilogPath` Linux allowlist). The new Windows-rooted-path guard runs as the **fourth** check (`..` → UNC → Linux-allowlist → Windows-rooted). Path falls through cleanly when running on Linux for Windows-shaped paths (`onWindows == false`).

## New attack surface

| Concern | Assessment |
|---|---|
| `NameAllowlist` regex catastrophic backtracking (ReDoS)? | No. `^[A-Za-z0-9_. -]+$` is linear. No alternation, no nested quantifiers. |
| `IsWindowsRootedPath` helper (drive-letter pattern)? | Not a regex — explicit char-by-char check. Linear, length-bounded. |
| `Sanitize` helper itself a ReDoS / allocation surface? | No. `string.Replace("\r", @"\r").Replace("\n", @"\n")` — two linear passes. Worst case O(n) where n = name length. |
| `ValidateObservability` scheme allowlist — can attacker bypass? | No. The check happens after `Uri.TryCreate` which canonicalizes the scheme. Check is pattern-match against `"http"|"https"|"grpc"` — exact equality. |

## Recommendations (non-blocking)

1. **Documenter findings (GAPS_NON_BLOCKING)** — operator-facing documentation for #19 name-allowlist, #20 OTLP scheme restriction, and #27 Windows-Serilog-path guard. See `DOCUMENTATION-15.md`. Could ride along with this PR or be a separate doc patch.
2. **Future hardening pass candidate (Phase 16+):** structured-logging migration so the errors list is a structured parameter rather than embedded in the message string. Would deepen the #13/#19 closure by removing string interpolation entirely on the boundary. Out of scope for v1.2.1; revisit if/when the project moves to structured Serilog templates throughout.
3. **`gh api` SHA-resolution recipe** would be useful contributor doc — captures the lightweight-vs-annotated-tag two-step lesson from PLAN-1.3 SUMMARY-1.3.md "Decisions Made". Minor; CONTRIBUTING.md scope.

## Result

**0 CRITICAL findings.** **0 IMPORTANT findings unresolved.** Phase 15 is safe to ship pending operator tag-cut.
