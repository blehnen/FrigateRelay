# Security Audit — Phase 9

## Verdict: PASS_WITH_NOTES

**Risk Level: Low**

Phase 9 introduces OpenTelemetry tracing/metrics and Serilog structured logging. No secrets are committed, no exploitable injection vulnerabilities exist in the new code, and all architecture invariants hold. Two advisory-grade themes: operator-controlled values flow into span tags without sanitization, and `events.received` / `events.matched` counter dimensions are tagged with attacker-influenceable values from MQTT payloads. Neither is exploitable in the current single-tenant deployment, but both should be tracked for future hardening.

## Critical Findings

None.

## Important Findings

None.

## Advisory / Low Notes

### A1 — Counter cardinality inflation from MQTT-sourced tag values
- **Location:** `src/FrigateRelay.Host/Dispatch/DispatcherDiagnostics.cs:44–53`; `src/FrigateRelay.Host/EventPump.cs:93–95, 117–119`
- **Description:** `frigaterelay.events.received` and `frigaterelay.events.matched` are tagged with `camera` and `label` values drawn directly from incoming MQTT event payloads. An attacker (or misconfigured Frigate instance) with MQTT publish access can emit events with arbitrary `camera` or `label` values, creating an unbounded number of distinct time-series in the OTel collector. Most hosted collectors charge by series count or impose hard limits; exceeding them causes metrics loss or service interruption. (CWE-400)
- **Mitigation:** Either normalize tag values against an allowlist of known camera names (`var cameraTag = _knownCameras.Contains(context.Camera) ? context.Camera : "other"`), or document the operational constraint that MQTT ACLs must restrict who can publish to the Frigate event topic. Mirrors ID-13 (Phase 8) at the metrics layer.

### A2 — Span tag injection from operator-controlled string values
- **Location:** `src/FrigateRelay.Host/EventPump.cs:90–91, 104–106`; `src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs:179, 185–187`
- **Description:** Subscription, plugin, and validator names from `appsettings.json` are written verbatim as OTel span tags AND as span name fragments (`$"action.{plugin.Name.ToLowerInvariant()}.execute"`). Values containing CRLF, null bytes, or very long strings could confuse OTLP receivers or trace UIs. (CWE-117)
- **Mitigation:** Add a startup-validation pass enforcing `[A-Za-z0-9_-]+` for subscription/plugin/validator names, OR document the format constraint in operator docs. The `StartupValidation.ValidateAll` collect-all pattern is the right home.

### A3 — OTLP endpoint URI scheme not restricted to http/https
- **Location:** `src/FrigateRelay.Host/StartupValidation.cs:73`; `src/FrigateRelay.Host/HostBootstrap.cs:65, 72`
- **Description:** `ValidateObservability` accepts any absolute URI for `Otel:OtlpEndpoint`. `file:///etc/passwd` or `ftp://...` passes validation. The OpenTelemetry SDK rejects non-HTTP at runtime with `ArgumentException` — so no file reads — but the operator gets a crash instead of a clean startup diagnostic. (CWE-183)
- **Mitigation:** After `Uri.TryCreate`, add `if (uri.Scheme is not ("http" or "https" or "grpc")) errors.Add(...)`.

### A4 — Serilog file sink path is operator-controlled without validation
- **Location:** `src/FrigateRelay.Host/HostBootstrap.cs:42–43`
- **Description:** Hard-coded `"logs/frigaterelay-.log"` (relative, safe) currently. But `Serilog:File:Path` is honored by `ReadFrom.Configuration` if set in env or `appsettings.Local.json`. On a container running as root (Phase 10), this could overwrite system files. (CWE-22)
- **Mitigation:** Phase 10 Dockerfile MUST run as non-root user (already in CLAUDE.md). Optionally validate `Serilog:File:Path` for `..` segments at startup.

## Scan Evidence

- **Secret scan:** `bash .github/scripts/secret-scan.sh scan` — clean. `appsettings.json` `Otel:OtlpEndpoint == ""` and `Serilog:Seq:ServerUrl == ""` defaults verified.
- **Dependency vulnerabilities:** `dotnet list package --vulnerable --include-transitive` from `src/FrigateRelay.Host/` — clean. All 8 OTel + 5 Serilog packages at pinned versions per RESEARCH.md.
- **License audit:** All new packages Apache-2.0 (OpenTelemetry.*, Serilog.*) or MIT (FluentAssertions 6.12.2 still pinned). `Serilog.Sinks.Seq` Apache-2.0 (Datalust's sink package, distinct from the commercial Seq server).
- **Test-only enforcement:** `grep -rn 'OpenTelemetry.Exporter.InMemory' src/` returns 0 — confined to test csprojs as required.
- **IaC:** zero Dockerfile / Terraform / k8s changes in Phase 9.
- **Architecture invariants:** zero matches for `App\.Metrics|OpenTracing|Jaeger\.` in `src/`. Zero matches for `ServicePointManager` API calls (only XML doc comment references). Zero `.Result`/`.Wait()` introduced. Zero hard-coded IPs.
- **ID-17 env-var fallback:** Verified — `ValidateObservability` correctly reads `OTEL_EXPORTER_OTLP_ENDPOINT` as fallback. Single-process worker, no multi-tenant concern.

## Recommendations

1. **A1 (cardinality)** — track as a new ISSUES.md entry; addresses an operational risk that becomes more pressing once shipped to multi-camera production deployments.
2. **A2 (span tag injection)** — small enforcement change in `StartupValidation`; bundle with general operator-input hardening alongside ID-13/14 in a future hardening pass.
3. **A3 (URI scheme)** — small inline fix; can be added to `ValidateObservability` in any subsequent commit.
4. **A4 (file sink path)** — Phase 10 Dockerfile work owns the non-root mitigation; add a validation pass when convenient.

None of these findings block Phase 9 from finalizing or proceeding to `/shipyard:ship`.
