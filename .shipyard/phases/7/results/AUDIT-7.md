# Phase 7 — Security Audit

**Verdict:** PASS / Low risk
**Date:** 2026-04-26
**Scope:** All 25 files changed between `pre-build-phase-7` and `acc3de4`.

## OWASP Top 10 + secrets review

| Concern | Status | Evidence |
|---|---|---|
| **A01 Broken access control** | N/A | New code is plugin-scoped; no auth surface introduced. |
| **A02 Cryptographic failures** | NOTE-1 | `AllowInvalidCertificates` opt-in TLS bypass on validator HttpClient. Same pattern as BlueIris/Pushover — scoped to per-instance handler, never global. CA5359 suppressed locally with explanatory comment. Acceptable per CONTEXT-7 D8 + CLAUDE.md invariant. |
| **A03 Injection** | PASS | `EventId` URL-encoded by FrigateSnapshot (Phase 5 ID-resolved); validator submits image bytes via multipart, no string interpolation into URLs. CodeProject.AI returns JSON consumed by `JsonSerializer` — no string eval. |
| **A04 Insecure design** | PASS | Validator failure stance is configurable per instance (`OnError`); default FailClosed. **No retry on validator HttpClient (D4)** — operators upgrading from no-validator deployments get fail-closed semantics by default. |
| **A05 Security misconfiguration** | NOTE-2 | `Validators` config schema is new and operator-facing; misconfiguration paths (undefined key / unknown Type) are caught by `StartupValidation.ValidateValidators` with operator-guidance error messages. Fail-fast, not silent. |
| **A06 Vulnerable / outdated components** | PASS | New PackageReferences match existing versions: `Microsoft.Extensions.Http 10.0.4`, `Microsoft.Extensions.Options.ConfigurationExtensions 10.0.4`, `Microsoft.Extensions.Options.DataAnnotations 10.0.4`. No new third-party packages added (only same Microsoft.Extensions.* family). FluentAssertions 6.12.2 license-pin preserved. |
| **A07 Identification & auth failures** | N/A | No auth changes. |
| **A08 Software / data integrity** | PASS | All validator HTTP clients are per-instance; no shared mutable global state. |
| **A09 Logging & monitoring failures** | PASS | New `validator_rejected` structured log emits `event_id, camera, label, action, validator, reason`. Logger source-gen (`LoggerMessage.Define` + `[LoggerMessage]` attributes) — no string-interpolation logging. |
| **A10 SSRF** | NOTE-3 | Validator's `BaseUrl` is operator-supplied; `[Url]` data annotation enforces format. Per-instance `HttpClient` with `BaseAddress`. Operator can point validator at internal services — same exposure surface as BlueIris/Pushover, no new SSRF avenue. |

## Secrets scan

```
git grep -E 'AppToken=[A-Za-z0-9]{20,}|UserKey=[A-Za-z0-9]{20,}' src/ tests/    → empty
git grep -nE '192\.168\.[0-9]+\.[0-9]+' src/ tests/                              → empty
git grep -i  'password|token|secret' src/FrigateRelay.Plugins.CodeProjectAi/    → only XML doc comments referencing the legacy code's hardcoded behaviour as the antipattern (`/// // never set globally on ServicePointManager`).
```

Test stubs use `"test-app-token"` / `"test-user-key"` (literally those strings, < 20 chars) — under the secret-scan regex threshold and obviously fake.

**No new secrets, no hardcoded IPs, no committed credentials.**

## Dependency vulnerability

`Microsoft.Extensions.*` 10.0.x is the current stable .NET 10 release line. No CVEs known against any of the new package references at audit time.

## IaC

No infrastructure-as-code changes in this phase. Docker / CI workflows untouched (Phase 3's `run-tests.sh` auto-discovers the new test project; no edits required).

## Findings

- **0 Critical**
- **0 High**
- **0 Medium**
- **3 Low/Info notes** (NOTE-1 TLS bypass scoped, NOTE-2 config misconfig fail-fast, NOTE-3 SSRF surface unchanged) — all matching established Phase 4-6 patterns; no remediation required.

## Verdict rationale

Phase 7 introduces a new HTTP-calling plugin with operator-supplied URL targets, but every concern (TLS bypass, config validation, secrets, retry policy, log content) follows established invariants. The intentional asymmetry (validators don't retry, action plugins do) is documented loudly in code AND in CLAUDE.md (PLAN-3.1 Task 3 prescribed this — pending CLAUDE.md edit captured in DOCUMENTATION-7.md).

**No blockers for phase closure or ship.**