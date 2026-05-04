# Issue Tracker

## Open Issues

### ID-1: Simplify IEventMatchSink justification in PLAN-3.1

**Source:** verifier (Phase 3 spec-compliance review)
**Severity:** Non-blocking (clarity improvement)
**Status:** Open

**Description:**
PLAN-3.1 Task 1 Context section includes a detailed multi-paragraph explanation of why `IEventMatchSink` is added to Abstractions. The justification is correct, but could be condensed to a single sentence for readability:

> "IEventMatchSink keeps Host plugin-agnostic by delegating event routing and dedupe logic to the plugin implementation."

**Current text (lines in PLAN-3.1 Context):**
"EventPump becomes a trivial fan-out... Plugin implements it doing matcher + dedupe + log. Simpler."

**Suggested revision:**
Replace the above with the one-liner above, or similar.

**Impact:** Documentation clarity only. No code changes needed.

**Owner:** (None assigned; deferred for Phase 3 builder to address if desired)

---

### ID-3: `TargetFramework` missing from BlueIris csproj(s)

**Source:** verifier (Phase 4 post-build review, REVIEW-1.2)
**Severity:** Minor
**Status:** Open

**Description:**
`src/FrigateRelay.Plugins.BlueIris/FrigateRelay.Plugins.BlueIris.csproj` (and possibly the test csproj) may be missing an explicit `<TargetFramework>net10.0</TargetFramework>`, relying solely on `Directory.Build.props` inheritance. Build passes but explicit declaration is preferred for clarity and tooling support.

**Impact:** Non-functional. Build succeeds via inheritance.

---

### ID-4: `--filter-query` flag in CLAUDE.md may be stale for MSTest v4.2.1  *[CLOSED 2026-04-28]*

**Source:** verifier (Phase 4 post-build review, REVIEW-1.2)
**Severity:** Minor
**Status:** **Closed** (Phase 10 closeout, 2026-04-28)

**Description:**
CLAUDE.md documents `--filter-query` as the MTP single-test filter flag. The installed runner is MSTest v4.2.1 (confirmed in test output). The correct flag for this version should be verified and CLAUDE.md updated if it differs.

**Impact:** Developer friction when running a single test by name. No test-correctness impact.

**Resolution:**
Phase 10 documenter (DOCUMENTATION-10.md) confirmed `--filter-query` is not honored by MSTest v4.2.1 / MTP. CLAUDE.md "Single test by name" example replaced with `--filter "PluginRegistrarRunnerTests"` (class-name form), which works on the installed runner. Comment also updated to specify the runner version explicitly.

---

### ID-5: `CapturingLogger<T>` duplicated as inner class in dispatcher tests

**Source:** verifier (Phase 4 post-build review, REVIEW-2.1)
**Severity:** Minor
**Status:** Resolved (commit `c68dfaf`, 2026-04-25)

**Description:**
`CapturingLogger` was defined as a private inner class in `tests/FrigateRelay.Host.Tests/Dispatch/ChannelActionDispatcherTests.cs`. CLAUDE.md documents the capturing-logger pattern as a shared convention.

**Resolution:**
Extracted to `tests/FrigateRelay.Host.Tests/CapturingLogger.cs` as `internal sealed class CapturingLogger<T> : ILogger<T>` (commit `c68dfaf`). Inner class removed; `ChannelActionDispatcherTests` updated to use `CapturingLogger<ChannelActionDispatcher>`. All 29 Host tests pass.

---

### ID-6: `OperationCanceledException` sets `ActivityStatusCode.Error` in dispatcher consumer  *[CLOSED 2026-04-27]*

**Source:** verifier (Phase 4 post-build review, REVIEW-2.1)
**Severity:** Minor
**Status:** **Closed** (commit `06ff862`, Phase 9 PLAN-2.1 Task 2)

**Description:**
In `ChannelActionDispatcher`'s consumer loop, an `OperationCanceledException` (which occurs during graceful shutdown) sets the OTel `Activity` status to `ActivityStatusCode.Error`. This is semantically incorrect — graceful cancellation is not an error. The status should be `Unset` or `Ok` when cancelled via the shutdown token.

**Resolution:**
`catch (OperationCanceledException) when (ct.IsCancellationRequested)` block in
`src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs` now calls
`actionActivity?.SetStatus(ActivityStatusCode.Unset)` instead of
`dispatchActivity?.SetStatus(ActivityStatusCode.Error, "Cancelled")`.
Graceful shutdown no longer produces Error-status spans in OTel traces.

**Impact:** Misleading OTel traces during normal host shutdown. Low-risk one-line fix.

---

### ID-7: CONTEXT-4 D3 lists `{score}` placeholder but parser does not accept it

**Source:** verifier (Phase 4 post-build review, REVIEW-2.2)
**Severity:** Note (doc stale)
**Status:** Open

**Description:**
CONTEXT-4 D3 defines 5 URL template placeholders including `{score}`. The architect dropped `{score}` during plan design (recorded in PLAN-1.2 Task 2 and the pre-execution VERIFICATION.md Section G) because `EventContext` has no `Score` property. The code is correct — the parser's allowlist contains only `{camera}`, `{label}`, `{event_id}`, `{zone}`. However, CONTEXT-4 D3 has not been updated to remove `{score}` from the table.

**Impact:** The stale CONTEXT-4 doc will mislead the Phase 5 builder who reuses the templater for `BlueIrisSnapshot`. Remove the `{score}` row from CONTEXT-4 D3, or add a strikethrough/note that it was deferred.

---

### ID-8: `PASS_THROUGH_ARGS` not forwarded in `--coverage` branch of `run-tests.sh`

**Source:** verifier (Phase 4 post-build review, REVIEW-3.2)
**Severity:** Minor
**Status:** Open

**Description:**
In `.github/scripts/run-tests.sh`, the `--coverage` branch (lines 67-70) calls `dotnet run` without appending `"${PASS_THROUGH_ARGS[@]}"`. Only the non-coverage branch (line 86) passes them through. Any extra arguments (e.g. a future `--filter-query`) are silently dropped in Jenkins coverage runs.

**Fix:** Append `"${PASS_THROUGH_ARGS[@]}"` to the `dotnet run` invocation at line 70, after the `--coverage-output` argument.

**Impact:** Silent arg drop in Jenkins coverage runs. Does not affect CI (non-coverage) or local fast runs.

---

### ID-9: User-facing documentation deferred to Phase 12

**Source:** documenter (Phase 4 post-build review, DOCUMENTATION-4)
**Severity:** Minor
**Status:** Deferred (user decision, 2026-04-25)

**Description:**
Phase 4 ships a working MQTT → BlueIris vertical slice but the repo has no `README.md`, no `docs/` tree, and no operator-facing configuration documentation. XML doc-comments on the public abstractions surface are in good shape; the gap is operator/user docs.

**Decision:** Defer all documentation generation to ship time (Phase 12) per user choice. Rationale: Phases 5–7 add Snapshot providers, Pushover, and CodeProject.AI validator — operator docs written now would need substantial rewrites once those plugins land. Architecture and plugin-author docs similarly benefit from waiting until the plugin patterns are stable across 3+ implementations.

**Reactivation triggers:**
- A new contributor asks how to configure FrigateRelay — write a minimal `README.md` quickstart.
- Phase 11 (Operations & Docs) begins per ROADMAP — generate the full docs tree.
- An external user opens an issue asking for setup instructions.

---

### ID-11: `CapturingLogger<T>` duplicated across test assemblies — Rule of Three  *[RESOLVED 2026-04-26]*

**Source:** reviewer (Phase 5 REVIEW-2.2, 2026-04-26)
**Severity:** Minor
**Status:** **Resolved** (Phase 6 prep cleanup, commit pending)

**Resolution:**
Extracted to `tests/FrigateRelay.TestHelpers/FrigateRelay.TestHelpers.csproj` (`OutputType=Library`, no test runner deps). `CapturingLogger<T>` raised to `public sealed`, namespace `FrigateRelay.TestHelpers`. The 4 duplicate copies (Host.Tests, BlueIris.Tests, FrigateSnapshot.Tests, Pushover.Tests) deleted. Each test csproj gains a `<ProjectReference>` to TestHelpers; type is exposed via `global using FrigateRelay.TestHelpers;` in a `Usings.cs` per test project. `run-tests.sh` glob (`tests/*.Tests/*.Tests.csproj`) does not pick up the helper project (no `.Tests` suffix). 124/124 tests still pass after extraction.

---

### ID-13: Newline sanitization missing on operator-controlled values in startup-validation exception messages

**Source:** auditor (Phase 8 AUDIT-8, 2026-04-27)
**Severity:** Low (advisory, CWE-117 log-spoofing only)
**Status:** Open — deferred to a future hardening pass

**Description:**
`StartupValidation.cs` interpolates subscription names, profile names, plugin names, and validator keys (all operator-controlled via `appsettings.json`) directly into `InvalidOperationException` messages. The aggregated form uses `string.Join("\n  - ", errors)`, so a name containing `\n  - ` would produce a multi-line message that resembles additional validation failures in structured log output. Exploitability is negligible — the attacker already controls the config file — but the log forensics value of clean error messages is real.

**Fix:** Sanitize embedded values: `name.Replace("\n", "\\n").Replace("\r", "\\r")`. Better yet, switch to structured logging where the errors list is a structured parameter rather than embedded in the message string.

**Reactivation triggers:**
- Operators report confusing log output during startup failures.
- A general structured-logging pass is undertaken (Phase 9 observability or later).

---

### ID-14: `ActionEntryTypeConverter` accepts empty/whitespace plugin names

**Source:** auditor (Phase 8 AUDIT-8, 2026-04-27)
**Severity:** Low (advisory)
**Status:** Open — deferred

**Description:**
`ActionEntryTypeConverter.ConvertFrom(string s)` returns `new ActionEntry(s)` for any input — including `""` and `"   "`. The value flows into a case-insensitive DI name lookup in `StartupValidation.ValidateActions`, which catches it as "unknown action plugin" — but the resulting message `"references unknown action plugin '   '"` is confusing.

**Fix:** Add a guard in `ActionEntryTypeConverter.ConvertFrom`: `if (string.IsNullOrWhiteSpace(s)) throw new InvalidOperationException("Action plugin name cannot be empty or whitespace.");`. Trivial; deferred only because no operator has hit it.

---

### ID-16: `ValidateObservability` has no unit tests

**Source:** reviewer (Phase 9 REVIEW-2.2, 2026-04-27)
**Severity:** Minor
**Status:** **Closed** (commit `9dfdb83`, Phase 9 PLAN-3.1 Task 1)

**Description:**
`StartupValidation.ValidateObservability` was added in PLAN-2.2 commit `c7ee4d1` but had no direct tests. The existing `ValidateAll` test suite supplies a minimal `ServiceCollection` without `IConfiguration`, so the `if (configuration is not null)` guard skips Pass 0 entirely — no test exercises `ValidateObservability`. A future refactor that removes the guard would silently regress fail-fast behavior.

**Resolution:**
Added `tests/FrigateRelay.Host.Tests/Observability/ValidateObservabilityTests.cs` (3 tests):
1. malformed `Otel:OtlpEndpoint` (e.g. `"not-a-uri"`) produces one error containing `"Otel:OtlpEndpoint"`.
2. malformed `Serilog:Seq:ServerUrl` produces one error containing `"Serilog:Seq:ServerUrl"`.
3. valid absolute URIs for both keys produce zero errors.
Uses `new ConfigurationBuilder().AddInMemoryCollection(...).Build()` as the config source.

---

### ID-17: `ValidateObservability` did not check `OTEL_EXPORTER_OTLP_ENDPOINT` env-var fallback *[CLOSED 2026-04-27]*

**Source:** reviewer (Phase 9 REVIEW-2.2, 2026-04-27)
**Severity:** Minor
**Status:** **Closed** (orchestrator inline fix, Phase 9 between Wave 2 and Wave 3)

**Description:**
`HostBootstrap.cs` resolves `otlpEndpoint` as `config["Otel:OtlpEndpoint"] ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")`, but the original `ValidateObservability` checked only the config key. If the env var was set to a malformed value while the config key was empty, validation passed and `new Uri(otlpEndpoint)` threw `UriFormatException` with a raw stack trace instead of the structured diagnostic.

**Resolution:**
`ValidateObservability` now applies the same precedence: `config["Otel:OtlpEndpoint"] ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")`. Whichever value HostBootstrap consumes is the value validated.

---

### ID-22: Test fixture `Task.Delay` magic delays (Phase 9 observability tests)

**Source:** simplifier (Phase 9 SIMPLIFICATION-9, 2026-04-27); reviewer (REVIEW-3.1 Suggestion #1/#2)
**Severity:** Low (test fragility under CI load)
**Status:** Open — deferred (orchestrator initial-fix attempt deferred due to API mismatch on `CapturingLogger<T>` polling property)

**Description:**
Three sites in `tests/FrigateRelay.Host.Tests/Observability/` use fixed `Task.Delay` to wait for pipeline state to settle:
- `EventPumpSpanTests.cs:285` — `Task.Delay(400)` between pump start and stop.
- `CounterIncrementTests.cs:356` — `Task.Delay(400)` between pump start and stop.
- `CounterIncrementTests.cs:390` — `Task.Delay(300)` between fault-source start and stop.
- `CounterIncrementTests.cs:420–422` — `Task.Delay(shouldThrow ? 200 : 100)` between dispatcher enqueue and stop.

Under load these sleeps may be insufficient (causing flake); on fast machines they're unnecessary tax.

**Mitigation (deferred):** Replace with polling on a deterministic signal. Initial inline attempt used `logger.Records.Any()` but `CapturingLogger<T>` does not expose `Records`. Correct property name needs verification (likely `LogEntries`, `Captured`, or similar). Alternatively, expose the in-memory exporter's `activities` collection or the `MeterListener` measurement count to the helper for polling.

---

### ID-18: Counter cardinality DOS via attacker-influenceable MQTT camera/label tags

**Source:** auditor (Phase 9 AUDIT-9, 2026-04-27)
**Severity:** Low (advisory; CWE-400)
**Status:** Open — deferred to Phase 11 hardening pass

**Description:**
`frigaterelay.events.received` and `frigaterelay.events.matched` carry `camera` and `label` tags drawn from MQTT event payloads. An attacker (or misconfigured Frigate instance) with MQTT publish access can emit events with arbitrary `camera`/`label` values, exploding cardinality in the OTel collector. Most hosted backends bill or hard-cap by series count.

**Mitigation:** Normalize tag values against a known-camera allowlist (`var cameraTag = _knownCameras.Contains(context.Camera) ? context.Camera : "other"`), or document that operators must restrict MQTT publish ACLs. Mirrors Phase 8 ID-13 at the metrics layer.

---

### ID-19: OTel/log span tag injection from operator-controlled string values

**Source:** auditor (Phase 9 AUDIT-9, 2026-04-27)
**Severity:** Low (advisory; CWE-117)
**Status:** Open — deferred

**Description:**
Subscription, plugin, and validator names from `appsettings.json` are written verbatim as OTel span tags AND interpolated into span names (`$"action.{plugin.Name.ToLowerInvariant()}.execute"`). Values containing CRLF, null bytes, or excessively long strings could confuse OTLP receivers. Self-inflicted misconfiguration risk.

**Mitigation:** Add a startup-validation pass enforcing `[A-Za-z0-9_-]+` for subscription/plugin/validator names. Bundle with ID-13/14 in the structured-logging hardening pass.

---

### ID-20: OTLP endpoint URI scheme not restricted to http/https

**Source:** auditor (Phase 9 AUDIT-9, 2026-04-27)
**Severity:** Low (advisory; CWE-183)
**Status:** Open — deferred

**Description:**
`ValidateObservability` accepts any absolute URI for `Otel:OtlpEndpoint`. `file:///etc/passwd` or `ftp://...` passes validation; the OpenTelemetry SDK rejects non-HTTP at runtime with `ArgumentException` (no file reads), but the operator gets a crash instead of a structured diagnostic.

**Mitigation:** Add scheme check after `Uri.TryCreate`:
```csharp
if (uri.Scheme is not ("http" or "https" or "grpc"))
    errors.Add($"Otel:OtlpEndpoint scheme '{uri.Scheme}' is not permitted. Use http/https.");
```

---

### ID-21: Serilog file sink path is operator-controlled without validation  *[CLOSED 2026-04-27]*

**Source:** auditor (Phase 9 AUDIT-9, 2026-04-27)
**Severity:** Low (advisory; CWE-22, future-tense)
**Status:** **Closed** (commits `506999d` + `c3294b9`, Phase 10 PLAN-1.2)

**Description:**
The file sink path is hard-coded `"logs/frigaterelay-.log"` (relative, safe) currently. But `Serilog:File:Path` is honored by `ReadFrom.Configuration` if set in env vars or `appsettings.Local.json`. On a container running as root (Phase 10 concern), this could redirect log output to arbitrary paths and overwrite system files.

**Mitigation:** Phase 10 Dockerfile MUST run as non-root user (already in CLAUDE.md). Optionally validate `Serilog:File:Path` for `..` segments at startup.

**Resolution:**
`StartupValidation.ValidateSerilogPath(IConfiguration, ICollection<string>)` added in Phase 10
PLAN-1.2 Task 1 (commit `506999d`). The pass iterates every `Serilog:WriteTo:*:Args:path` value
and rejects: `..` path-traversal segments (CWE-22), UNC paths beginning with `\\`, and absolute
paths outside the allowlist (`/var/log/frigaterelay/`, `/app/logs/`). Relative paths and absent
values pass through without error. The pass is wired into `ValidateAll` immediately after
`ValidateObservability` (Pass 0 cluster); follows the D7 collect-all pattern — appends to the
shared `errors` accumulator, never throws inline. Nine unit tests in
`tests/FrigateRelay.Host.Tests/Configuration/SerilogPathValidationTests.cs` cover all rejection
and acceptance cases plus a `ValidateAll` integration assertion (commit `c3294b9`). The residual
risk from container-as-root is addressed in combination with the non-root `USER` directive in the
Phase 10 Dockerfile (PLAN-2.1).

---

### ID-15: Secret-scan does not cover RFC 1918 class A (`10.x.x.x`) or class B (`172.16-31.x.x`)

**Source:** auditor (Phase 8 AUDIT-8, 2026-04-27)
**Severity:** Low (CI hardening)
**Status:** Open — deferred

**Description:**
`.github/scripts/secret-scan.sh` PATTERNS array covers `192\.168\.` (RFC 1918 class C) only. A developer accidentally committing `10.0.0.5` or `172.16.0.1` would not be caught. No such IPs exist in the committed tree today, but the tripwire's coverage is asymmetric.

**Fix:** Add two patterns to PATTERNS, two LABELS entries, and two matching fixture lines in `.github/secret-scan-fixture.txt` (the tripwire self-test fails if any pattern lacks a fixture line). Patterns:
- `10\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}`
- `172\.(1[6-9]|2[0-9]|3[01])\.[0-9]{1,3}\.[0-9]{1,3}`

**Reactivation triggers:**
- Phase 11 (Operations & Docs) hardening pass.
- A dependabot or contributor PR introduces an unscoped IP literal.

---

### ID-16: `ValidateObservability` has no unit tests  *[CLOSED 2026-04-27]*

**Source:** reviewer (Phase 9 REVIEW-2.2, 2026-04-27)
**Severity:** Important
**Status:** **Closed** (commit `9dfdb83`, Phase 9 PLAN-3.1 Task 1)

**Resolution:** Duplicate entry — see first ID-16 entry above. Closed by the same commit.

---

### ID-17: `ValidateObservability` does not validate the `OTEL_EXPORTER_OTLP_ENDPOINT` env-var fallback

**Source:** reviewer (Phase 9 REVIEW-2.2, 2026-04-27)
**Severity:** Important
**Status:** Open

**Description:**
`HostBootstrap.cs` lines 56–57 resolve `otlpEndpoint` from `builder.Configuration["Otel:OtlpEndpoint"]` first, then falls back to `Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")`. If the env var is set to a malformed value (e.g. `"not-a-uri"`) while the config key is empty, `ValidateObservability` (which only checks `config["Otel:OtlpEndpoint"]`) reports no error. The OTLP exporter registration at line 65 then calls `new Uri(otlpEndpoint)`, which throws `UriFormatException` at startup — bypassing the fail-fast validation path entirely and producing a less actionable stack trace rather than the structured error message.

**Fix:** In `ValidateObservability`, apply the same env-var fallback logic used in `HostBootstrap`:
```csharp
var endpoint = config["Otel:OtlpEndpoint"]
    ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
if (!string.IsNullOrWhiteSpace(endpoint) && !Uri.TryCreate(endpoint, UriKind.Absolute, out _))
    errors.Add($"Otel:OtlpEndpoint '{endpoint}' is not a valid absolute URI.");
```
Alternatively, resolve the merged endpoint value once in `ValidateAll` and pass it to both the validation and wiring sites to keep them in sync.

**Reactivation triggers:**
- Next builder pass touching `StartupValidation.cs` or `HostBootstrap.cs`.
- Any operator report of a cryptic `UriFormatException` at startup.

---

### ID-23: File sink active in container — B4 deviation (HostBootstrap programmatic wiring)  *[CLOSED 2026-04-27]*

**Source:** reviewer (Phase 10 REVIEW-1.3, 2026-04-27)
**Severity:** Important
**Status:** **Closed** (Phase 10 PLAN-2.1 Task 1)

**Description:**
`HostBootstrap.ConfigureServices` wires `.WriteTo.File("logs/frigaterelay-.log", ...)` programmatically
and unconditionally (`src/FrigateRelay.Host/HostBootstrap.cs` lines 43–47). `appsettings.Docker.json`'s
Console-only `WriteTo` override has no effect on programmatically-added sinks — `ReadFrom.Configuration`
adds configuration-driven sinks but does not remove sinks already registered in code. In a container
deployment (`ASPNETCORE_ENVIRONMENT=Docker`), the file sink writes to `/app/logs/` inside the writable
container layer, defeating full log capture via `docker logs` and partially violating CONTEXT-10 B4
("rolling file sink off by default in container"). The deviation is acknowledged in PLAN-1.3 and
`appsettings.Docker.json`'s `_comment`; not hidden, but also not fixed.

**Resolution:**
`HostBootstrap.ConfigureServices` in `src/FrigateRelay.Host/HostBootstrap.cs` now wraps the
`.WriteTo.File(...)` call in an environment guard (PLAN-2.1 Task 1, same commit as `docker/Dockerfile`):
```csharp
if (!string.Equals(builder.Environment.EnvironmentName, "Docker",
        StringComparison.OrdinalIgnoreCase))
{
    lc.WriteTo.File(path: "logs/frigaterelay-.log", rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7, formatProvider: null);
}
```
The guard activates when `ASPNETCORE_ENVIRONMENT=Docker` (set in `docker/Dockerfile` and
`docker/docker-compose.example.yml`). The Console sink is unconditional — `docker logs` captures all
output. Non-Docker deploys (Production, Development) continue to receive the rolling file sink.
Bundled with the Dockerfile because the env-var convention is established there; the two changes form
a single cohesive unit. `<InvariantGlobalization>false</InvariantGlobalization>` (redundant .NET 10
default) also removed from `Host.csproj` in the same commit.

---

### ID-24: `release.yml` GitHub Actions are tag-pinned, not SHA-pinned

**Source:** auditor (Phase 10 AUDIT-10, 2026-04-28)
**Severity:** Low / Advisory (CWE-829, SLSA L2+)
**Status:** Open (deferred)

**Description:**
Every third-party action in `.github/workflows/release.yml` (`actions/checkout`, `docker/setup-qemu-action`, `docker/setup-buildx-action`, `docker/login-action`, `docker/metadata-action`, `docker/build-push-action`) is pinned to a major-version tag rather than a commit SHA. Mutable tags can be force-pushed by the action maintainer; a supply-chain compromise of any of these would execute arbitrary code in the release job with `packages: write` permission. The existing `ci.yml` has the same pattern.

**Mitigation:** Replace each `uses: action@vN` with `uses: action@<full-SHA>  # vN`. Dependabot `github-actions` ecosystem (already configured) maintains the SHA pins automatically. One-time find-and-replace; track as pre-v1.0 release hardening.

---

### ID-25: `mosquitto-smoke.conf` warning could be more prominent

**Source:** auditor (Phase 10 AUDIT-10, 2026-04-28)
**Severity:** Low / Advisory (documentation; CWE-306 surface)
**Status:** Open (deferred)

**Description:**
`docker/mosquitto-smoke.conf` runs Mosquitto with `allow_anonymous true` on `0.0.0.0` for the release-pipeline smoke gate. The combination is contained within the ephemeral GitHub Actions runner VM and `--network host`, so the smoke setup itself is not exposed externally. However the file is committed to the repo; an operator copying it as a starting point for their own broker config could inadvertently deploy an unauthenticated MQTT listener. Existing single-line warning is present but easily overlooked.

**Mitigation:** Add a second `# WARNING: anonymous + bind-all — CI use ONLY` line with a divider at the top of the file. No code change required.

---

### ID-26: `docker-compose.example.yml` exposes /healthz on all host interfaces by default

**Source:** auditor (Phase 10 AUDIT-10, 2026-04-28)
**Severity:** Low / Advisory (CWE-200; OWASP A05:2021)
**Status:** Open (deferred)

**Description:**
The example compose file maps `8080:8080` (binds to `0.0.0.0:8080` on the Docker host). The `/healthz` endpoint exposes only boolean operational state (`status`, `started`, `mqttConnected`) — no credentials, no PII — so the disclosure is informational only. Acceptable for the home-lab target audience; problematic on internet-exposed hosts.

**Mitigation:** Add a comment in the compose example noting that operators on untrusted networks should bind to `127.0.0.1:8080:8080` and front the service with a reverse proxy + auth. Not a correctness fix.

---

### ID-27: `ValidateSerilogPath` does not block Windows absolute paths (accepted gap)

**Source:** auditor (Phase 10 AUDIT-10, 2026-04-28)
**Severity:** Low / Advisory (CWE-22, residual)
**Status:** Open (deferred — accepted by design for the Linux/container target)

**Description:**
`StartupValidation.ValidateSerilogPath` rejects `..` traversal, UNC paths, and absolute paths outside the Linux allowlist (`/var/log/frigaterelay/`, `/app/logs/`). It does not reject Windows-style absolute paths (e.g., `C:\Windows\…`), which is documented as an accepted gap in the implementation comment. If the service is run on Windows in `Production` (non-Docker) with a malicious `appsettings.Local.json`, log output could be redirected to arbitrary Windows filesystem locations. Exploitability is negligible — the attacker must already control the config file.

**Mitigation:** Add a `Path.IsPathRooted` + `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)` guard in a future hardening pass. No change required for v1; the Docker target is Linux-only.

---

### ID-28: `tools/FrigateRelay.MigrateConf` accepts unvalidated CLI paths  *[CLOSED 2026-04-28]*

**Source:** auditor (Phase 12 AUDIT-12, 2026-04-28)
**Severity:** Low / Advisory (CWE-22, operator-self-inflicted)
**Status:** **Closed** (Phase 12 closeout, 2026-04-28)

**Description:**
`tools/FrigateRelay.MigrateConf/Program.cs` `RunMigrate` and `RunReconcile` passed `--input`, `--output`, `--frigaterelay`, `--legacy` values directly to `IniReader.Read` / `File.WriteAllText` without canonicalization. The tool is offline and runs as the invoking operator, so no privilege escalation is possible — but path traversal would let an operator-mistake or shell-glob accidentally write to an unintended location. Inconsistent with the `ValidateSerilogPath` hardening precedent established in Phase 10 (ID-21 close).

**Resolution:**
Phase 12 closeout commit added `Path.GetFullPath(value)` canonicalization at CLI-parse time in both `RunMigrate` and `RunReconcile`. Four lines total. No behavioral change for valid relative paths (they resolve relative to CWD as before); `..` segments are now collapsed to their canonical absolute form, surfacing intent clearly. Mirrors the Phase 11 `check-doc-samples.sh` hardening pattern (CWE-22 path-traversal guard via `(samples_dir / filename).resolve()` containment check).

---

## Closed Issues

### ID-2: `IActionDispatcher`/`DispatcherOptions` should be `internal` *[CLOSED 2026-04-27]*

**Source:** verifier (Phase 4 post-build review, REVIEW-1.1)
**Status:** **Closed** (commit `b5b87eb`, Phase 8 PLAN-1.1)

**Resolution:**
Phase 8 PLAN-1.1 visibility sweep flipped `IActionDispatcher` and `DispatcherOptions` to `internal`, alongside `SubscriptionOptions`, `HostSubscriptionsOptions`, `SnapshotResolverOptions`, `DedupeCache`, and `SubscriptionMatcher`. `<InternalsVisibleTo Include="DynamicProxyGenAssembly2" />` added in MSBuild item form to permit NSubstitute proxying of `IActionDispatcher`. Build green; 55 Host tests pass.

---

### ID-10: `ActionEntry`, `ActionEntryJsonConverter`, `SnapshotResolverOptions` raised to `public` — should be `internal` *[CLOSED 2026-04-27]*

**Source:** reviewer (Phase 5 REVIEW-1.2, 2026-04-26)
**Status:** **Closed** (commits `b5b87eb`, `e622a39`, `6264154`, Phase 8 PLAN-1.1 + PLAN-1.2)

**Resolution:**
Phase 8 PLAN-1.1 internalized `ActionEntryJsonConverter` and `SnapshotResolverOptions` (commit `e622a39`). Phase 8 PLAN-1.2 internalized `ActionEntry` itself (commit `6264154`) — feasible because the visibility sweep also internalized the surrounding `SubscriptionOptions` / `HostSubscriptionsOptions` carriers, eliminating the CS0053 cascade that originally forced the types public. All host-internal configuration types now correctly express the boundary.

---

### ID-12: `Actions: ["BlueIris"]` string-form back-compat broken under `IConfiguration.Bind` *[CLOSED 2026-04-27]*

**Source:** Phase 5 build (integration test regression, 2026-04-26)
**Severity:** Medium
**Status:** **Closed** (commit `6264154`, Phase 8 PLAN-1.2)

**Resolution:**
Implemented Option 1: `ActionEntryTypeConverter : TypeConverter` decorating `ActionEntry` via `[TypeConverter(typeof(ActionEntryTypeConverter))]`. `CanConvertFrom(string) => true`, `ConvertFrom(string s) => new ActionEntry(s)`. Coexists with the existing `[JsonConverter]` on disjoint code paths (binder uses TypeConverter; `JsonSerializer.Deserialize` uses JsonConverter). 3 TDD tests in `ActionEntryTypeConverterTests` exercise string-only / object-only / mixed array binding via `IConfiguration.Bind` — all green. Operators with Phase 4 `appsettings.json` files using the string-array shape `["BlueIris"]` will now bind correctly; the silent-drop regression is fixed.

---

### ID-29: Eviction-callback log captures stale `plugin.Name` from loop variable

**Source:** reviewer (Phase 13 REVIEW-1.1, 2026-05-04)
**Severity:** Low (log-only; counter tags unaffected)
**Status:** Open

**Description:**
At `src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs:100–101`, the `Channel.CreateBounded` eviction callback captures `plugin` from the surrounding `foreach (var plugin in plugins)` loop. The post-PLAN-1.1 code is:

```csharp
var channel = Channel.CreateBounded<DispatchItem>(channelOptions, evicted =>
{
    DispatcherDiagnostics.IncrementDrops(evicted, "channel_full");
    LogDropped(_logger, evicted.Context.EventId, plugin.Name, capacity, null);
});
```

Counter tags are correct because `IncrementDrops` reads from the captured `evicted` parameter (per CONTEXT-13 OQ-2). But `LogDropped` uses `plugin.Name` from the closure, not `evicted.Plugin.Name`. If eviction fires after the outer `foreach` loop has advanced (theoretical but possible in queued-eviction edge cases), the log message's `Action` field could refer to a different plugin than the one whose item was actually dropped.

**Pre-existing.** The closure-capture pattern existed before Phase 13; PLAN-1.1's helper-method refactor surfaced it during REVIEW-1.1. PLAN-1.1's commit scope was tight to the helper migration so the fix was deliberately deferred.

**Remediation:**
One-line swap — replace `plugin.Name` with `evicted.Plugin.Name` in the `LogDropped` call so the log message and the counter tags share a single source of truth (the evicted item itself).

**Impact:**
Operator-facing log accuracy under high-throughput drop scenarios. No counter-data correctness impact, no security impact.
