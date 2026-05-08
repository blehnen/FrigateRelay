# Phase 16 Security Audit

**Date:** 2026-05-08
**Auditor:** auditor agent (claude-sonnet-4-6)
**Scope:** all changes in `pre-build-phase-16..HEAD` (9 commits, 21 files, +699 / -73 lines)

---

## Verdict: PASS_WITH_NOTES

No critical or exploitable vulnerabilities were found. Phase 16 may proceed to ship. Three advisory-level findings are documented below for operator awareness and future improvement.

---

## Executive Summary

Phase 16 introduces `MetricsTagWriter` / `MetricsTagsOptions` for OTel cardinality control (PLAN-1.1), replaces `Task.Delay` polling with `WaitForEntriesAsync` in tests (PLAN-1.2), and unifies `HttpClient` registration in the CPAI and Doods2 plugin registrars (PLAN-1.3). None of these changes introduce directly exploitable vulnerabilities. The registrar refactor correctly preserves per-handler TLS-bypass scope; no global `ServicePointManager` is introduced. The camera allowlist normalization is architecturally sound. The only findings are advisory: the design intentionally passes raw camera values to structured logs while emitting normalized values to metrics, creating a minor log/metric correlation gap; `NormalizeCameraTag` silently passes `null`/empty through to the OTLP backend; and `HashSet<string>` is reallocated on every normalization call under a non-empty allowlist. None of these block shipping.

---

## STRIDE Threat Model (pre-scan focus)

| Threat | Surface in Phase 16 | Risk |
|--------|---------------------|------|
| Spoofing | No auth surface added | None |
| Tampering | `KnownCameras` config bound from `IConfiguration` — operator-controlled, bounded string[] | None |
| Repudiation | Structured logs (raw camera) vs. metrics (normalized camera) diverge — minor traceability gap | Low |
| Information Disclosure | Camera names visible in OTLP metric tags — pre-existing behavior, no regression | None |
| Denial of Service | `NormalizeCameraTag` allocates a `HashSet<string>` per call when allowlist is non-empty | Low |
| Elevation of Privilege | No auth/authz changes | None |

---

## Findings

### Critical

None.

---

### Important

None.

---

### Advisory

**[A1] Log/metric correlation divergence — raw camera in logs, normalized camera in metrics**
- **Location:** `src/FrigateRelay.Host/EventPump.cs:118` and `src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs:333,385`
- **Description:** `LogMatchedEvent` and `LogValidatorRejected` emit `context.Camera` (raw value) to the structured log. `DispatcherDiagnostics.IncrementEventsMatched` and the validator pass/reject counters emit `NormalizeCameraTag(context.Camera)` (possibly `"other"`) to OTLP metrics. When a camera name is unknown and the allowlist is non-empty, log entries will reference the real camera name while the metric tag reads `"other"`. Operators correlating a spike in `frigaterelay.validators.rejected{camera="other"}` against log lines will need to filter by fields other than camera name.
- **Impact:** Reduced observability correlation; no security impact. (CWE-778 — Insufficient Logging)
- **Remediation:** Document in `docs/observability.md` that log entries always carry the raw camera value and metric tags carry the normalized value. No code change required for v1.3.0 unless operator guidance is insufficient.

---

**[A2] Null/empty camera bypasses cardinality protection — passes through unguarded to OTLP backend**
- **Location:** `src/FrigateRelay.Host/Observability/MetricsTagWriter.cs:51-52`
- **Description:** `NormalizeCameraTag` returns the input unchanged when `string.IsNullOrEmpty(camera)` is true. A Frigate source that emits MQTT payloads with a missing or empty camera field will record a metric tag of `null` or `""`. While this cannot grow cardinality unboundedly (it is a single degenerate value), it bypasses the allowlist's `"other"` folding semantics, so operators cannot distinguish "empty/missing camera" from "known camera" in dashboards. This is the design decision documented in the XML doc comment and in `docs/observability.md`.
- **Impact:** Minor observability gap; no exploitable security impact. An attacker who can inject Frigate MQTT payloads with empty camera fields (threat: Spoofing / Tampering of MQTT messages) would cause `null`-tagged metrics, not cardinality explosion. MQTT authentication is a separate perimeter concern outside FrigateRelay's trust boundary.
- **Remediation (operator guidance):** Add a note to `docs/observability.md` recommending operators add `""` or a sentinel value to `KnownCameras` if they observe null-tagged metrics. Alternatively, a future enhancement could normalize `null`/empty to `"unknown"` when the allowlist is non-empty.

---

**[A3] Per-call `HashSet<string>` allocation in `NormalizeCameraTag` under non-empty allowlist**
- **Location:** `src/FrigateRelay.Host/Observability/MetricsTagWriter.cs:60-62`
- **Description:** When `KnownCameras` is non-empty, `NormalizeCameraTag` allocates a new `HashSet<string>(known, StringComparer.OrdinalIgnoreCase)` on every call. The source comment explicitly acknowledges this and defers optimization. For typical camera counts (<20) and moderate event rates this is negligible. At high event throughput (thousands of events/second) this creates measurable GC pressure.
- **Impact:** No security impact. Denial-of-service risk is low because the set size is bounded by operator configuration, not by external input. (CWE-400 — Uncontrolled Resource Consumption applies only if an external actor can control `KnownCameras`, which is an operator-only config key.)
- **Remediation:** Cache the `HashSet<string>` and invalidate on `IOptionsMonitor.OnChange`. The existing `IOptionsMonitor<MetricsTagsOptions>` injection already provides the change notification hook. Defer to post-v1.3.0 unless profiling demonstrates impact.

---

## Coverage by Area

| Area | Checked | Notes |
|------|---------|-------|
| Code Security (OWASP Top 10) | Yes | No injection, no auth surface, no deserialization beyond `IConfiguration.Bind` on bounded `string[]`. `NormalizeCameraTag` output is metric tag only — not used in SQL, command execution, log templates, or HTML. |
| Secrets & Credentials | Yes | `secret-scan.sh scan` returned PASS. `secret-scan.sh selftest` confirmed all 9 fixture patterns match. No API keys, tokens, IPs, or passwords in phase 16 diff. `appsettings.json` not changed. |
| Dependencies | Yes | Zero new `<PackageReference>` entries in any `.csproj` or `Directory.Packages.props`. No new transitive dependencies introduced. No `npm audit` / `pip-audit` / `cargo audit` applicable — pure .NET solution. |
| IaC / Container | N/A | No changes to `docker/`, `.github/workflows/`, `Jenkinsfile`, Terraform, Ansible, or Helm files. CI test-discovery auto-include verified in place (`.github/scripts/run-tests.sh`). |
| Configuration Security | Yes | `Otel:MetricsTags:KnownCameras` defaults to `[]` (passthrough, no behavior change for unconfigured operators). No debug mode, verbose error, or CORS changes. No secrets in the new config section. |
| Cross-phase coherence | Yes | See section below. |

---

## Cross-Component Analysis

### TLS bypass scope — registrar refactor (PLAN-1.3) — CLEAN

The CPAI and Doods2 registrar refactor moves `client.BaseAddress` and `client.Timeout` assignment from the factory lambda (inside `AddKeyedScoped`) into the `AddHttpClient(name, configureClient)` overload. The `.ConfigurePrimaryHttpMessageHandler(sp => { ... })` chain that conditionally applies `SslOptions.RemoteCertificateValidationCallback` when `AllowInvalidCertificates: true` is unchanged and remains on the same `AddHttpClient(...)` fluent call. The TLS bypass is therefore still scoped to the named `HttpClient` for that specific plugin instance only. No global `ServicePointManager.ServerCertificateValidationCallback` is set. `git grep ServicePointManager src/` returns only comments and XML doc references — zero runtime assignments.

The new `Register_AllowInvalidCertificatesTrue_ConfiguresTlsBypassHandler` test in `CodeProjectAiPluginRegistrarTests` exercises this path and confirms the validator resolves without exception.

### ParallelValidators strict-AND invariant (Phase 14) — PRESERVED

`ChannelActionDispatcher` lines 233-240 confirm the parallel path still delegates to `RunValidatorsInParallelAsync`. The Phase 16 changes inject `_metricsTagWriter.NormalizeCameraTag(...)` at counter call sites only — they do not modify control flow, verdict aggregation, or the `anyRejected` result that gates action execution. The strict-AND semantics (all validators must pass) are unaffected.

### Phase 15 `ValidateNames` / startup validation — PRESERVED

`StartupValidation.cs` has zero diff in Phase 16 (`git diff pre-build-phase-16..HEAD -- src/FrigateRelay.Host/StartupValidation.cs` returns empty). The collect-all validation pattern (D7), `Sanitize()` usage in error messages, and name allowlist permissive-printable rules are untouched.

### Metrics tag injection surface — BOUNDED

The `camera` value flows from `EventContext.Camera`, which originates from the Frigate MQTT payload parsed by `FrigateMqttEventSource`. It is used exclusively as an OTLP metric tag value via `TagList`. OTLP tag values are opaque strings transmitted over gRPC — there is no injection vector analogous to SQL, shell, or HTML. Log messages that include the raw camera value use `LoggerMessage.Define<...>` source-generated delegates with structured logging, not string interpolation, so log injection (CWE-117) is not a risk.

### `IOptionsMonitor` config reload safety

`MetricsTagWriter` reads `_monitor.CurrentValue` on every `NormalizeCameraTag` call, which is safe for concurrent access (the monitor replaces the reference atomically on reload). No stale-state risk.

### `WaitForEntriesAsync` timing (PLAN-1.2) — NOT a security concern

The `DateTime.UtcNow` polling loop in `CapturingLogger<T>` is test-only infrastructure. The 25ms poll interval does not affect production code paths. The fallback `MeterListener` pattern in `CounterIncrementTests` is correctly scoped to the test method.

---

## Recommendations

1. **docs/observability.md** — Add a note under the `KnownCameras` section clarifying: (a) log entries always carry the raw camera value; metric tags carry the normalized value; when using the allowlist, correlate on `event_id` or `subscription` rather than `camera` to join logs and metrics. (b) operators who observe null/empty-tagged metrics should consider whether their Frigate source emits empty camera fields and whether adding an empty-string sentinel to `KnownCameras` is appropriate.

2. **Post-v1.3.0 backlog** — Cache the `HashSet<string>` in `MetricsTagWriter` using `OnChange` invalidation (A3 above). Track as a minor performance improvement, not a security item.

3. **No blocking actions required** — Phase 16 is clear to proceed to `/shipyard:ship`.
