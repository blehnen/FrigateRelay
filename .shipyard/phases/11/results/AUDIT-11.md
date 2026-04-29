# Security Audit — Phase 11

**Phase:** 11 — Open-Source Polish
**Date:** 2026-04-28
**Verdict:** PASS_WITH_NOTES
**Risk level:** Low
**Diff base:** 0861818..HEAD
**Auditor:** shipyard:auditor

---

## Summary

Phase 11 is a docs-and-CI phase. No production runtime code changed except test-infrastructure
fixes in `MqttToValidatorTests.cs`. The cumulative diff introduces community health files (LICENSE,
README, CONTRIBUTING, SECURITY, CHANGELOG), a docs-validation workflow, issue/PR templates, a
plugin scaffold template, and a samples project. No exploitable vulnerabilities were found.
Two advisory items are raised: a path-traversal risk surface in `check-doc-samples.sh`'s Python
(low risk, sandboxed), and the continuing open issue (ID-24) that GitHub Actions are tag-pinned
rather than SHA-pinned, which now applies to `docs.yml` as well. Both are non-blocking.

---

## Critical Findings (block ship)

None.

---

## Important Findings (should fix this phase)

None.

---

## Advisory / Low Findings (defer with tracking)

### [A1] `check-doc-samples.sh` — unsanitized filename extracted from markdown allows path traversal within SAMPLES_DIR

**Location:** `.github/scripts/check-doc-samples.sh:52-54`

**Description:**
The Python script extracts `filename` from the `filename=(\S+)` capture group in a fenced code
block annotation and constructs `sample_path = samples_dir / filename`. `pathlib.Path.__truediv__`
in Python does NOT strip `..` components — a fence annotated `` ```csharp filename=../../some/path ``
would resolve to a path outside `SAMPLES_DIR`. In the CI context the script reads (not writes) the
resolved path, so the worst case is an unintended file being compared against the doc block content.
No write, execute, or exfiltration path exists — the script calls only `read_text` on the result.
`DOC` and `SAMPLES_DIR` are controlled by the repository itself (not fork PRs, since the workflow
uses `pull_request`, not `pull_request_target`). Combined exploitability is negligible.

**Impact:** A maliciously crafted doc file could read an arbitrary file from the runner's
filesystem and print a diff to CI output. Because only `pull_request` (not `pull_request_target`)
triggers the workflow, a fork PR cannot inject a modified `docs/plugin-author-guide.md` during
the trusted-context run — this only runs against repository-controlled content after merge to
`main`. Risk is low / informational. (CWE-22, advisory only)

**Remediation:**
Add a filename validation guard after line 52:

```python
filename = match.group(1)
# Guard against path traversal; only allow bare filenames with extension.
if "/" in filename or "\\" in filename or filename.startswith("."):
    print(f"::warning::Skipping unsafe filename annotation: {filename!r}")
    continue
```

---

### [A2] `docs.yml` GitHub Actions are tag-pinned, not SHA-pinned (extends ID-24)

**Location:** `.github/workflows/docs.yml:50,53,97,100,135,138`

**Description:**
`actions/checkout@v4`, `actions/setup-dotnet@v4`, and `actions/setup-python@v5` are pinned to
mutable version tags. This is consistent with existing `ci.yml` precedent and is already tracked
as ID-24 (Phase 10 audit). Phase 11 adds a third workflow with the same pattern, widening the
same surface.

**Impact:** If any of these action repositories were compromised and the tag force-pushed, the
`docs.yml` job would execute attacker-controlled code in the runner with `contents: read` scope.
Low likelihood; standard open-source posture. (CWE-829, SLSA L2+)

**Remediation:** Extend the ID-24 remediation sweep to include `docs.yml` when SHA-pinning is
addressed. No action required before this phase ships.

---

### [A3] `samples/FrigateRelay.Samples.PluginGuide` linked in `.sln` — verify no inadvertent host coupling

**Location:** `FrigateRelay.sln` (new `samples/` section added)

**Description:**
The solution file now includes the samples project. The samples project has a `ProjectReference`
to `FrigateRelay.Abstractions` only — no reference to `FrigateRelay.Host` or any production
plugin. Verified: no production `src/` project contains a reverse reference to the samples
project. The isolation is correct.

**Impact:** Informational. If a future commit inadvertently adds a `ProjectReference` from
`FrigateRelay.Host` to `samples/`, the samples would be pulled into the production image.
No current issue.

**Remediation:** None required now. Consider adding a CI assertion (`grep -rn
"Samples.PluginGuide" src/`) if this becomes a concern in later phases.

---

## CLAUDE.md Invariant Compliance

| Invariant | Status | Notes |
|-----------|--------|-------|
| No `.Result`/`.Wait()` in src/ | Pass | No new production code; samples use `await` throughout |
| No global `ServicePointManager` | Pass | Not referenced in any new file |
| TLS-skip opt-in per-plugin only | Pass | No TLS code added this phase |
| No secrets in committed config | Pass | README uses placeholder values (`your-api-token`, `your-user-key`); no real credentials |
| No hard-coded IPs/hostnames | Pass | `localhost` in README quickstart curl example is acceptable; no RFC 1918 addresses |
| Observability stack — no App.Metrics/OpenTracing/Jaeger | Pass | No observability deps added |
| FluentAssertions pinned to 6.12.2 | Pass | Template test csproj pins `FluentAssertions Version="6.12.2"` |
| SECURITY.md uses GitHub private vuln reporting, no mailto | Pass | SECURITY.md: `https://github.com/blehnen/FrigateRelay/security/advisories/new`; no mailto |
| CI split invariant (no coverage in GH workflows) | Pass | `docs.yml` has no `--coverage` flags |
| Secret-scan fixture / tripwire coverage | Pass | No new secret-scan patterns needed for this phase's content |

---

## Detailed Analysis Notes

### Secrets / Credentials Scan

All new files scanned for credential patterns:

- `README.md` — placeholder values only (`your-api-token`, `your-user-key`, `your-username`,
  `your-password`). The GitHub slug `blehnen/FrigateRelay` is a published-by-design value, not a
  leak. No RFC 1918 IPs. The `localhost` in the `curl -i http://localhost:8080/healthz` example is
  acceptable (developer machine reference).
- `SECURITY.md` — contains only the advisory URL, no credentials.
- `CONTRIBUTING.md` — no credentials; references public GitHub URLs.
- `CHANGELOG.md` — historical narrative; no credentials or IPs.
- `LICENSE` — MIT boilerplate.
- Issue / PR templates — placeholder text only.
- `templates/` and `samples/` code — no hardcoded credentials, no IP literals.
- `docs.yml` — uses standard `GITHUB_TOKEN` implicit token (no hardcoded PAT or secret).

### Dependency Audit

No new external packages beyond what was already present:

- `samples/FrigateRelay.Samples.PluginGuide.csproj` adds `Microsoft.Extensions.Hosting 10.0.0`,
  `Microsoft.Extensions.Http 10.0.4`, `Microsoft.Extensions.Options.ConfigurationExtensions
  10.0.4` — all first-party Microsoft packages consistent with existing host usage.
- `templates/` test csproj mirrors existing project conventions: MSTest 4.2.1, FluentAssertions
  6.12.2 (license-constrained pin honored), NSubstitute 5.3.0.
- No new third-party packages. No known CVEs introduced.

### `MqttToValidatorTests.cs` Wave 1 Fix (`CapturingSerilogSink`)

The fix replaces the `ILoggerProvider`-based capture (which broke after the Phase 10 WebApplication
pivot to Serilog factory) with a second `AddSerilog` call wiring a `CapturingSerilogSink`. This is
test-only code. Security observations:

- `CapturingSerilogSink` correctly uses `ConcurrentBag<CapturedEntry>` — thread-safe for concurrent
  test logging.
- The Serilog `LogEvent` property extraction (`SourceContext`, `EventId`) uses standard API surface;
  no unsafe string interpolation into log messages.
- The sink is registered only in test setup, not in production `HostBootstrap`. No production
  log-capture bypass concern.

### `check-doc-samples.sh` — bash + Python analysis

Bash layer:
- `set -euo pipefail` is present (line 15) — correct.
- `DOC` and `SAMPLES_DIR` are only used as arguments to the Python heredoc via `sys.argv`, not
  interpolated into `run:` strings or `eval`. No command injection via environment overrides.
- No `eval`, `exec`, or dynamic command construction anywhere in the bash layer.

Python layer:
- Uses only stdlib: `re`, `pathlib`, `difflib` — no subprocess calls.
- The only file-system operations are `read_text` — no writes, no execute.
- Path traversal advisory raised as A1 above; impact limited to reading unintended files within the
  runner filesystem (not remote exfiltration or code execution).

### `docs.yml` Workflow Security

- `permissions: contents: read` — minimal, correct. No `id-token`, `packages`, or `write` grants.
- `pull_request` trigger (not `pull_request_target`) — no secret access for fork PRs; safe.
- `concurrency` group present — prevents race conditions and redundant runs.
- `timeout-minutes: 10` on all jobs — DoS protection against hung builds.
- `dotnet new install templates/FrigateRelay.Plugins.Template/` installs from the checked-out repo
  path, not from NuGet.org or a third-party registry — supply chain is fully in-tree.
- No `${{ github.ref_name }}` or other user-controllable values interpolated into `run:` blocks.
- The scaffold `cp` step copies rendered output into `src/` and `tests/` then deletes it in
  `if: always()` cleanup — no tree pollution survives. Cleanup on failure is correct.
- `actions/setup-python@v5` is new relative to `ci.yml` — follows the same tag-pinned pattern
  (A2 above); no regression.

### `.github/ISSUE_TEMPLATE/config.yml`

Security advisory URL: `https://github.com/blehnen/FrigateRelay/security/advisories/new`.
Slug `blehnen/FrigateRelay` matches the repository identity. Correct. D6 invariant satisfied.

---

## Deferred Items — Proposed ISSUES Entries

No new ISSUES IDs are required. The two advisory items map to existing tracked items:

- **A1** (path traversal in check-doc-samples.sh) — new finding. Recommend adding as **ID-28**
  if the builder chooses to track it; otherwise acceptable as a doc note given the negligible
  exploitability in the `pull_request` (non-`pull_request_target`) trigger context.
- **A2** (docs.yml tag-pinned actions) — extend existing **ID-24** scope to include `docs.yml`.

---

## Audit Coverage

| Area | Status | Notes |
|------|--------|-------|
| Code Security (OWASP) | Yes | samples/ and template/ code reviewed; no injection, no unsafe deserialization, no auth/authz surface |
| Secrets & Credentials | Yes | All new files scanned; no real credentials found |
| Dependencies | Yes | No new external deps; all first-party or version-pinned existing packages |
| IaC / Workflow Security | Yes | docs.yml fully reviewed; permissions, triggers, pinning, concurrency analyzed |
| Configuration Security | Yes | No debug flags, no verbose error exposure, no CORS surface in new files |
| Cross-Component Coherence | Yes | samples/ has no reverse reference to Host; template isolated in templates/; sln inclusion verified |
| check-doc-samples.sh bash+Python | Yes | No command injection; path traversal advisory raised (A1) |
