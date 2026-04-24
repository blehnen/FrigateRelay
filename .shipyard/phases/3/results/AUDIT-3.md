# Security Audit Report — Phase 3

**Date:** 2026-04-24
**Scope:** `post-plan-phase-3..HEAD` (11 commits)
**Auditor:** Security & Compliance Auditor (claude-sonnet-4-6)
**Phase adds:** MQTTnet v5 integration, reconnect loop, channel event pump, matcher + dedupe, per-client TLS

---

## Executive Summary

**Verdict:** PASS
**Risk Level:** Low

Phase 3 introduces the first external network surface (MQTT broker connection) and handles it correctly: TLS is scoped per-client with no `ServicePointManager` mutation, the insecure-certificate bypass is opt-in and defaulted to `false`, and STJ deserialization uses no polymorphic or `$type` patterns. No secrets are hardcoded or committed. The only findings are advisory-level notes about a missing MQTT credential scaffold, an unbounded channel under a hostile-broker scenario, and two `CancellationToken.None` usages that are intentional but worth documenting. No Critical or Important findings were identified; the phase is clear to ship.

---

## STRIDE Threat Model (pre-scan prioritization)

| Threat | Surface | Priority |
|--------|---------|----------|
| Spoofing | MQTT broker identity (no cert pinning, AllowInvalidCertificates flag) | High — checked first |
| Tampering | Untrusted JSON payload from broker | High — checked second |
| Information Disclosure | Exception messages in logs, RawPayload stored verbatim | Medium |
| Denial of Service | Unbounded channel flooded by hostile broker | Medium |
| Repudiation | Structured logging via LoggerMessage — all events logged | Low |
| Elevation of Privilege | No auth/authz surface in Phase 3 | N/A |

---

## What to Do

| Priority | Finding | Location | Effort | Action |
|----------|---------|----------|--------|--------|
| 1 | No MQTT username/password field in options (future phase) | `FrigateMqttOptions.cs` | Small | Add `Username`/`Password` nullable strings now; mark `[DataProtected]` or source from user-secrets. Prevents retrofitting later when a broker requires auth. |
| 2 | Unbounded channel — no back-pressure cap | `FrigateMqttEventSource.cs:52` | Trivial | Document the design limit (dozens/min) in a code comment; add a bounded alternative note for hostile-broker environments. |
| 3 | `CancellationToken.None` on channel write and disconnect | `FrigateMqttEventSource.cs:178,219` | Trivial | Add inline comments explaining why None is correct in each case to prevent a future maintainer from silently removing the intent. |

### Themes

- All three findings are documentation/forward-compatibility gaps, not exploitable defects.
- The TLS and deserialization surfaces are implemented correctly.

---

## Detailed Findings

### Critical

_None._

### Important

_None._

### Advisory

- **[A1] No MQTT credential fields in `FrigateMqttOptions`** (`FrigateMqttOptions.cs`) — The options record has no `Username` or `Password` properties. This is noted as intentional for Phase 3 (no auth yet), but is worth flagging: when broker auth is added in a later phase, credentials must come from environment variables or .NET user-secrets (already scaffolded in `Program.cs`), not from `appsettings.json`. Consider adding nullable fields now with explicit XML-doc guidance so the pattern is established before Phase 4 adds broker connectivity config. (CWE-522)

- **[A2] Unbounded `Channel<EventContext>` with no back-pressure** (`FrigateMqttEventSource.cs:52-57`) — `Channel.CreateUnbounded<EventContext>()` will grow without bound if a hostile or misconfigured broker floods the topic faster than the pump consumes events. For the stated workload of dozens of events per minute this is acceptable, but there is no documented ceiling. Consider adding a comment referencing the design assumption, or switching to `Channel.CreateBounded<EventContext>(capacity)` with `BoundedChannelFullMode.DropOldest` for resilience. (CWE-400)

- **[A3] `CancellationToken.None` on channel write** (`FrigateMqttEventSource.cs:178`) — `_channel.Writer.WriteAsync(context, CancellationToken.None)` is intentional (the unbounded writer never blocks, so a token is irrelevant), but it appears inconsistent next to cancellation-aware code elsewhere. A brief comment (`// Unbounded channel — write never blocks; token not needed`) prevents a future reviewer from flagging this as an oversight.

- **[A4] `CancellationToken.None` on graceful disconnect** (`FrigateMqttEventSource.cs:219`) — `_client.DisconnectAsync(disconnectOptions, CancellationToken.None)` during `DisposeAsync` is correct (disposal must complete even after the host token is cancelled), but again benefits from a comment to document intent.

- **[A5] No NuGet lock file committed** — `packages.lock.json` is absent from the repository. Without a lock file, `dotnet restore` resolves the best-match version at build time, which can silently pull a different patch of MQTTnet or the Microsoft.Extensions packages. Add `<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>` to the Directory.Build.props and commit the generated lock file to pin the supply chain. (OWASP A06:2021, CWE-1357)

---

## Specific Audit Checks (as requested)

### TLS path

**PASS.** `BuildClientOptions()` (`FrigateMqttEventSource.cs:137-155`) uses `MqttClientOptionsBuilder.WithTlsOptions(...)` which is scoped exclusively to that single `IMqttClient` instance. No `ServicePointManager` reference exists anywhere in the diff or the repository (`git grep ServicePointManager` is clean). The `AllowInvalidCertificates` bypass is gated behind an explicit `if (allowInvalid)` check and defaults to `false` in `FrigateMqttTlsOptions`. The insecure path is narrowly scoped and correctly documented as development-only. No finding raised.

### Secret handling

**PASS.** No MQTT username/password fields exist yet (intentional per scope). No hardcoded credentials, tokens, API keys, or connection strings were found in any changed file. The `.gitignore` correctly excludes `*.env` and `**/appsettings.Local.json`. `Program.cs` documents the full layered config stack including user-secrets for Development. See A1 for the forward-compatibility note.

### Unbounded channel (DoS surface)

**ADVISORY (A2).** Acknowledged as acceptable for the stated workload. Flagged as A2 above; not a defect at current scale.

### Deserialization attack surface

**PASS.** `JsonSerializer.Deserialize<FrigateEvent>(rawPayload, FrigateJsonOptions.Default)` (`FrigateMqttEventSource.cs:172`) deserializes into a closed, sealed record hierarchy (`FrigateEvent`, `FrigateEventObject`). No `[JsonPolymorphic]`, no `$type` handling, no `ReferenceHandler.Preserve`, no custom `JsonConverter` with unsafe behavior. `FrigateJsonOptions.Default` uses STJ's safe defaults with only `SnakeCaseLower` naming policy and case-insensitive matching. Malformed payloads are caught by the surrounding `try/catch` at line 179 and logged at Warning with no rethrow.

### `JsonSerializerOptions.MakeReadOnly`

**PASS.** `FrigateJsonOptions.CreateReadOnly()` calls `options.MakeReadOnly(populateMissingResolver: true)` before assigning to the `static readonly` field. The `populateMissingResolver: true` parameter correctly pre-installs the `DefaultJsonTypeInfoResolver`, preventing the .NET 10 "must specify TypeInfoResolver before read-only" exception. The shared instance is immutable; downstream callers cannot mutate it.

### Dependencies (MQTTnet 5.x)

**NO KNOWN CVEs.** MQTTnet `5.1.0.1559` has no published CVEs in the NVD as of the audit date. MQTTnet is a pure managed-code MQTT client library with no native dependencies and a small attack surface. No lock file is committed (see A5). All Microsoft.Extensions packages are pinned to `10.0.7` (consistent across all projects), which is current. No known CVEs for those versions.

### `Program.cs` composition root

**PASS.** The bootstrap `LoggerFactory` is created inside a `using` block (`Program.cs:40-44`) and disposed immediately after plugin registration completes. No lingering state. The `PluginRegistrationContext` holds no secrets. `builder.Build()` is called after all registrations, which is correct ordering.

### `DedupeCache` lock (TOCTOU)

**PASS.** `TryEnter` (`DedupeCache.cs:61-77`) wraps both `_cache.TryGetValue` and `_cache.Set` inside a single `lock (_writeLock)` block. The check-and-insert pair is atomic. This correctly addresses the TOCTOU race that would exist if the lock only covered the Set call.

---

## Cross-Component Analysis

**Auth + Data flow coherence:** There is no authentication surface in Phase 3. The trust boundary is the MQTT broker connection. Events arriving from the broker are treated as untrusted inputs: they are deserialized into a closed type, filtered by the D5 guard (stationary/false-positive), projected to a source-agnostic `EventContext`, and then matched against subscription rules. At no point does broker-supplied data reach a code-execution path; it only influences log output and (in future phases) action dispatch. The `RawPayload` field stores the broker payload verbatim — this is by design for downstream action plugins but should be noted: if a future action plugin logs or forwards `RawPayload`, it could surface unexpectedly large or binary content. That is a Phase 4+ concern, not a Phase 3 defect.

**Error handling coherence:** All three exception boundaries (reconnect loop, message handler, disconnect) log at Warning or Error with structured messages via `LoggerMessage.Define`. Exception objects are passed as the last parameter (the `Exception?` slot), which means structured loggers (e.g. Serilog, Application Insights) receive the full exception object. `ex.Message` is also interpolated into the message string (`FrigateMqttEventSource.cs:182,224`), which is redundant when the exception object is already attached. This is not a vulnerability but could produce noisy duplicate exception text in some sink configurations.

---

## Analysis Coverage

| Area | Checked | Notes |
|------|---------|-------|
| Code Security (OWASP) | Yes | All changed .cs files reviewed; injection, deserialization, error handling checked |
| Secrets & Credentials | Yes | Full diff + `git grep` scan; gitignore verified |
| Dependencies | Yes | MQTTnet 5.1.0.1559 — no known CVEs; no lock file (A5) |
| IaC / Container | N/A | No Docker/Terraform/Ansible files in this phase |
| Configuration | Yes | `appsettings.json`, `FrigateMqttOptions`, TLS options reviewed |
