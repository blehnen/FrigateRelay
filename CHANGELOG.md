# Changelog

All notable changes to FrigateRelay are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed

- **P0 for operators with diverging Frigate ↔ Blue Iris camera names.** Restored legacy `FrigateMQTTProcessingService.CameraShortName` semantics that the v1.0.0 → v1.0.1 migration tool dropped. Blue Iris's HTTP trigger API returns 200 OK on unknown camera names but **silently does nothing** — so URL templates that send Frigate's lowercase id (e.g. `driveway`) to a Blue Iris install expecting its own shortname (e.g. `DriveWayHD`) appeared to succeed but never actually triggered the recording. The bug went unnoticed during the v1.0.0 parity window because the legacy service was running concurrently and firing the trigger with the correct name; once an operator stopped legacy, BI triggers stopped. (#32, verified end-to-end on 2026-05-01.) The fix:
  - New optional `CameraShortName` field on each subscription in `appsettings.json`. Defaults to null; when set, surfaces through the new `{camera_shortname}` URL-template token. The dedupe-cache and subscription matcher continue to key on `Camera` (Frigate's id) — only template substitution sees the override.
  - New `{camera_shortname}` token added to `EventTokenTemplate.AllowedTokens` (alongside the existing `{camera}`, `{label}`, `{event_id}`, `{zone}`). Resolution order: `CameraShortName ?? Camera`, so operators whose names already match keep working without setting the override.
  - `EventContext` gained an optional `CameraShortName` property that the host's `EventPump` populates per-dispatch via a `with`-clone (the source-agnostic invariant on `EventContext` is preserved — `IEventSource` implementations never set this field).
  - `tools/FrigateRelay.MigrateConf` now preserves the legacy `CameraShortName` value into the new field on each subscription (skipping emit when it equals `CameraName` — the override is redundant in that case). The migrated `BlueIris.TriggerUrlTemplate` now uses `{camera_shortname}` so existing legacy operators get the corrected behaviour automatically.
  - `README.md` "Key concepts" gains a bullet documenting `CameraShortName` and the silent-no-op trap it solves; the canonical `appsettings.Example.json` is intentionally left minimal (no `CameraShortName`) to preserve the Phase 8 success-criterion ≤60% size-parity gate.

  **Operator-action item for anyone running v1.0.0 / v1.0.1 with diverging Frigate/BI camera names** (this is most operators with a pre-existing BI install): after upgrading to the version with this fix, set `CameraShortName` on each subscription and change your `BlueIris:TriggerUrlTemplate` from `&camera={camera}` to `&camera={camera_shortname}`. If your Frigate camera ids and BI shortnames already match — congratulations, no config change needed.

### Documentation

- **Validator engine status** section in `README.md` and `CodeProjectAiOptions` XML doc remarks rewritten to hard-confirm Blue Onyx as a supported backend (no separate plugin needed — point `Validators:<name>:BaseUrl` at the Blue Onyx host and the existing CPAI plugin handles it). Verified end-to-end by an operator on 2026-05-01. Also documented the concrete performance caveat: Blue Onyx GPU acceleration is available only via its Windows EXE/service distribution; the Docker image is CPU-only and slower than CPAI's CUDA-enabled Docker image on the same hardware. Closes #12 (the original "add Blue Onyx validator" ask reduces to docs work since the existing plugin already handles it).

## [1.0.1] — 2026-05-01

Maintenance release — operator-reported MQTT bugs from the v1.0.0 parity-window debugging session, plus docs accuracy fixes. No breaking config changes; one operator-action item (rename `PUSHOVER__APITOKEN` → `PUSHOVER__APPTOKEN` in `.env` and update Seq/Loki queries keyed on `EventId` → `FrigateEventId`).

### Added

- `tests/FrigateRelay.IntegrationTests.RealBroker/` — opt-in integration-test project that runs against an operator-supplied MQTT broker rather than the in-process Testcontainers Mosquitto. Tests self-skip via `Assert.Inconclusive` unless `FRIGATERELAY_TEST_REAL_BROKER=1` is set; broker host/port and optional credentials come from `FRIGATERELAY_TEST_MQTT_HOST`/`_PORT`/`_USERNAME`/`_PASSWORD`. Topic prefix is `frigaterelay-test/<guid>/events` (never `frigate/events`) so a misconfigured test cannot trigger production action plugins. The project is not auto-discovered by `.github/scripts/run-tests.sh` — invoke it explicitly via `dotnet run --project tests/FrigateRelay.IntegrationTests.RealBroker`. See CONTRIBUTING.md for the operator run-book. Closes the harness portion of #17; the silent-SUBACK regression test (acceptance criterion 3) lands with the #16 fix in a follow-up PR.

### Fixed

- `.github/workflows/docs.yml` — removed two job-level `if: hashFiles(...) != ''` guards that caused every triggered run to fail with `startup_failure` ("Unrecognized function: 'hashFiles'") since the workflow first landed. `hashFiles()` is only allowed in step-level expressions, not in job-level `if:`. The guards were defensive "skip until file lands" checks added during Phase 11 while templates/docs/samples were being built out; vestigial post-v1.0.0. The workflow's top-level `paths:` filter already restricts triggers correctly. Closes #26.
- `templates/FrigateRelay.Plugins.Template/` — corrected `FrigateRelay.Abstractions` `<ProjectReference>` paths in both the plugin csproj and its tests csproj. Old paths used `..\..\..\..\src\FrigateRelay.Abstractions\...` (4 levels), sized for the template's *storage* location (`templates/FrigateRelay.Plugins.Template/src/FrigateRelay.Plugins.Example/`). When the `scaffold-smoke` workflow rendered the template and copied the output into `<repo>/src/<plugin>/` and `<repo>/tests/<plugin>.Tests/`, those paths resolved 2 levels above the repo root and the build failed with `CS0246: 'IActionPlugin' could not be found`. Corrected to match the in-repo plugin convention (`..\FrigateRelay.Abstractions\` for the plugin csproj, `..\..\src\FrigateRelay.Abstractions\` for the test csproj — same as `BlueIris` / `Pushover` / etc). This drift was masked for the entire v1.0.0 cycle by the `docs.yml` startup_failure above; surfaced and fixed in the same PR.
- `FrigateMqttEventSource.RunReconnectLoopAsync` — silent SUBACK denials are now detected. After `_client.SubscribeAsync(...)`, the new `ProcessSubscribeResult` helper inspects `MqttClientSubscribeResult.Items` and emits a structured `MqttSubscribeDenied` warning (EventId 5) per topic where the broker returned anything other than `GrantedQoS{0,1,2}` — including `NotAuthorized` (135), `TopicFilterInvalid` (143), `ImplementationSpecificError` (131), etc. If no topic in the request was granted, the connection is marked unhealthy via `IMqttConnectionStatus.SetConnected(false)` so `/healthz` returns 503 (instead of falsely reporting Healthy with zero events flowing). Closes #16.
- `FrigateMqttEventSource.RunReconnectLoopAsync` — added an `_client.IsConnected` short-circuit before the `TryPingAsync` probe, plus a distinct `catch (InvalidOperationException) when ex.Message.Contains("connect/disconnect is pending")` that logs at `Debug` level (EventId 6, `MqttConnectInflight`) and leaves the connection-status singleton untouched. Eliminates the spurious every-5-second `WRN MqttConnectFailed: System.InvalidOperationException: Not allowed to connect while connect/disconnect is pending` log entries that surfaced under network flap or slow-CONNACK conditions. The reconnect loop now self-heals without polluting the warning stream. Closes #19.
- `FrigateMqttOptions.ClientId` default changed from the static literal `"frigate-relay"` to `"frigate-relay-{MachineName}-{ProcessId}"`. Two FrigateRelay processes against the same broker (production container + developer debug session, two HA replicas, accidental duplicate deploys) now get distinct ClientIds out of the box, eliminating the broker-takeover thrash documented in #18. Operators who explicitly configured `ClientId` in their `appsettings.json` / env vars are unaffected — explicit values still win. Operators on strict MQTT 3.1.1 brokers enforcing the 23-character ClientId limit may need to override. Closes #18.
- `README.md` and `docker/.env.example` — corrected env-var documentation to match what `BlueIrisOptions` and `PushoverOptions` actually bind: removed `BLUEIRIS__BASEURL` / `BLUEIRIS__USERNAME` / `BLUEIRIS__PASSWORD` (those properties don't exist; auth is expressed inside `BlueIris:TriggerUrlTemplate` either via IP-whitelist or `&user=<USER>&pw=<PW>`), and renamed `PUSHOVER__APITOKEN` → `PUSHOVER__APPTOKEN` (the property is `AppToken`, not `ApiToken` — the typo silently bound to nothing). **Operators copying the v1.0.0 README/`.env.example` should update their `.env` files: rename `PUSHOVER__APITOKEN` → `PUSHOVER__APPTOKEN`, drop the three bogus `BLUEIRIS__*` entries, and set `BLUEIRIS__TRIGGERURLTEMPLATE` to the full trigger URL.** Also added documented examples for optional `FRIGATESNAPSHOT__BASEURL` and `VALIDATORS__<name>__*` keys. Also fixed the stale "Pre-1.0; Phase 11" line in the README's Project Status section. Closes #24.
- `README.md` + `CodeProjectAiOptions` XML doc — added a "Validator engine status" section noting that CodeProject.AI active development has stopped upstream. Existing CPAI installs and Blue Onyx-via-CPAI-API users are unaffected; the plugin is intentionally **not** marked `[Obsolete]` because it still works against current Blue Onyx (API-compatible) and older CPAI installs. Section points at the planned alternative validators on the v1.1 roadmap (#12 Blue Onyx, #13 Roboflow, #14 DOODS2). Closes #15.
- Renamed the `{EventId}` log-template placeholder to `{FrigateEventId}` across every `LoggerMessage.Define[...]` and `[LoggerMessage]` call site in `src/` (17 sites across `EventPump`, `ChannelActionDispatcher`, `SnapshotResolver`, `BlueIrisActionPlugin`, `CodeProjectAiValidator`, `FrigateSnapshotProvider`, `PushoverActionPlugin`). The old name collided with Serilog's `Microsoft.Extensions.Logging` bridge, which enriches every entry with an `EventId` property derived from the `LoggerMessage.Define` `new EventId(N, "Name")` argument — that bridge-enriched property won over the call-site value, so structured logs printed e.g. `event_id={"Id":1,"Name":"MatchedEvent"}` instead of the actual Frigate event id (`1745558400.0-abc`). Operators see real event ids in console / file / Seq / OTLP output now. **Downstream observable change:** the structured-log property name shifts from `EventId` to `FrigateEventId` — operators who built Seq dashboards, Loki queries, or OTLP-trace lookups keyed on `EventId` need to update those queries (low risk, but worth a re-check). Closes #22.

## [1.0.0] — 2026-05-01

Initial public release. 1:1 functional parity with the legacy `FrigateMQTTProcessingService` verified via a 24-hour live A/B window across all production cameras with zero missed alerts and zero spurious alerts — see `docs/parity-report.md`.

### Phase 12 — Parity Cutover (2026-04-28)

#### Added

- `BlueIris:DryRun` and `Pushover:DryRun` per-action config flags. When `true`, the action plugin emits a structured `would-execute` log entry (`BlueIrisDryRun` / `PushoverDryRun` EventId) at Info level and returns success without calling the external API. Used during the parity window for logging-only side-by-side runs against the legacy service; default `false` (production-safe).
- `tools/FrigateRelay.MigrateConf/` — .NET 10 console app that converts a legacy `FrigateMQTTProcessingService.conf` (hand-rolled INI reader; `Microsoft.Extensions.Configuration.Ini` not used) to a FrigateRelay-shaped `appsettings.Local.json`. Default subcommand: `migrate --input <path> --output <path>`.
- `tools/FrigateRelay.MigrateConf/` `reconcile` subcommand — pairs FrigateRelay NDJSON audit-log entries against a legacy CSV export of `(timestamp, camera, label, action, outcome)` tuples; produces a counts summary and per-discrepancy detail for `docs/parity-report.md`.
- `tests/FrigateRelay.MigrateConf.Tests/` — MSTest v4.2.1 round-trip and reconcile coverage; uses `tests/FrigateRelay.MigrateConf.Tests/Fixtures/sanitized-legacy.conf` fixture (secrets and IPs sanitized).
- `docs/migration-from-frigatemqttprocessing.md` — field-by-field INI → JSON mapping covering `[ServerSettings]`, `[PushoverSettings]`, and `[SubscriptionSettings]` blocks; documents secrets supplied via env vars (`Pushover__AppToken`, `Pushover__UserKey`, `BlueIris__Password`); explains deliberately-dropped fields (`Camera` per-subscription URL → global template, `LocationName`, `CameraShortName`).
- `docs/parity-window-checklist.md` — operator run book for the ≥48-hour side-by-side parity window: enabling DryRun + NDJSON sink, collecting legacy CSV export, running the reconcile subcommand, interpreting the parity report, and close-out steps before cutover.
- `docs/parity-report.md` — parity-window reconciliation output (template; populated by the operator after the window closes using the reconcile subcommand).
- `RELEASING.md` — manual v1.0.0 release run book: pre-flight checklist, CHANGELOG promotion step, `git tag -a v1.0.0` + `git push origin v1.0.0` commands, description of what `release.yml` does automatically after the tag push, post-release verification, and rollback procedure.
- README "Migrating from FrigateMQTTProcessingService" section linking `tools/FrigateRelay.MigrateConf/`, `docs/migration-from-frigatemqttprocessing.md`, `docs/parity-window-checklist.md`, `docs/parity-report.md`, and `RELEASING.md`.

#### Changed

- `FrigateRelay.Host` rolling file sink (`logs/frigaterelay-.log`) gains an opt-in `Logging:File:CompactJson` config key. When `true`, the file sink uses `Serilog.Formatting.Compact.CompactJsonFormatter` (NDJSON) so the reconcile subcommand can parse structured log output during the parity window. Default `false` — human-readable text format unchanged for production users.

---

### Phase 11 — Open-Source Polish (2026-04-28)

#### Added

- `LICENSE` — MIT License, Copyright (c) 2026 Brian Lehnen.
- `README.md` — overview, Docker quickstart (`docker compose -f docker/docker-compose.example.yml up`), config layering, `/healthz` semantics, plugin scaffold pointer.
- `CONTRIBUTING.md` — coding standards, test commands (MSTest v4.2.1 + MTP `--filter`), PR checklist, `FrigateRelay.TestHelpers` + FluentAssertions 6.12.2 pin, SECURITY.md pointer.
- `SECURITY.md` — vulnerability disclosure via GitHub private security advisories (no email exposure); maintainer Settings flag note.
- `templates/FrigateRelay.Plugins.Template/` — `dotnet new` template (`.template.config/template.json` with short-name `frigaterelay-plugin`); seeds a buildable `IActionPlugin` + `IPluginRegistrar` scaffold mirroring the BlueIris/Pushover layout.
- `docs/plugin-author-guide.md` — tutorial-first 11-section guide covering all four contract interfaces (`IActionPlugin`, `IValidationPlugin`, `ISnapshotProvider`, `IPluginRegistrar`).
- `samples/FrigateRelay.Samples.PluginGuide/` — buildable companion project with one sample per contract; wired into `FrigateRelay.sln` for IDE intellisense.
- `.github/workflows/docs.yml` — new dedicated docs-CI workflow with three jobs: `scaffold-smoke` (dotnet new install + build), `samples-build`, `doc-samples-rot` (verifies guide code blocks match samples verbatim).
- `.github/ISSUE_TEMPLATE/bug_report.yml` + `feature_request.yml` + `config.yml` — GitHub Issue Forms with self-cert checkboxes; blank issues disabled; security advisory redirect.
- `.github/pull_request_template.md` — checklist (build clean, tests green, secret-scan, CHANGELOG entry, plugin-author convention).
- `.github/scripts/check-doc-samples.sh` — bash + Python heredoc enforcing verbatim copy of `csharp filename=…` fences from `plugin-author-guide.md` against the samples project (doc-rot prevention).

#### Fixed

- Phase 9 integration regressions: `Validator_ShortCircuits_OnlyAttachedAction` log capture (replaced `ILoggerProvider` workaround with a `Serilog.ILogEventSink` — Phase 10's Web SDK pivot rendered the previous fix ineffective) and `TraceSpans_CoverFullPipeline` validator span name assertion (`validator.<instance>.check`, not `validator.<plugin-type>.check`). Test suite restored to 192/192 passing.
- Closes **ID-4**: CLAUDE.md / CONTRIBUTING.md test-running examples updated from stale `--filter-query` to MSTest v4.2.1's `--filter`.

#### Changed

- `CLAUDE.md` — "Project state" section updated (no longer says "pre-implementation"); Jenkinsfile description corrected to reflect Phase 10's digest pin + Dependabot docker ecosystem closures.
- README, CONTRIBUTING, `.github/ISSUE_TEMPLATE/config.yml` — `<owner>/frigaterelay` placeholders replaced with `blehnen/FrigateRelay` (the published GitHub slug confirmed via `git remote -v`).

### Phase 10 — Dockerfile and multi-arch release workflow (2026-04-28)

#### Added

- `docker/Dockerfile` — multi-stage Alpine self-contained publish, non-root user UID 10001, `HEALTHCHECK` via `wget --spider /healthz`.
- `docker/docker-compose.example.yml` + `docker/.env.example` — FrigateRelay-only compose example; secrets supplied via `.env`.
- `.github/workflows/release.yml` — multi-arch GHCR push on `v*` tags (linux/amd64 + linux/arm64 via QEMU/buildx) with Mosquitto-sidecar smoke gate hard-failing on `/healthz != 200`.
- `/healthz` readiness endpoint — 200 only when MQTT is connected AND `ApplicationStarted` has fired.
- `StartupValidation.ValidateSerilogPath` — rejects path traversal (`..`), UNC paths, and out-of-allowlist absolute paths (CWE-22 mitigation, closes ID-21).
- `IMqttConnectionStatus` / `MqttConnectionStatus` — thread-safe connection-state signal for the health check.
- `appsettings.Docker.json` — Console-only Serilog sink for container environments (rolling file off by default).
- `docker/mosquitto-smoke.conf` — minimal no-auth broker config for release smoke test.
- `.dockerignore` — excludes `src/`, `tests/`, `.shipyard/`, secrets, IDE artifacts.

#### Changed

- Host SDK pivoted from `Microsoft.NET.Sdk.Worker` to `Microsoft.NET.Sdk.Web` to expose `MapHealthChecks`.
- `Jenkinsfile` SDK base image is now digest-pinned (supply-chain hardening).
- Dependabot `docker` ecosystem added to `.github/dependabot.yml` to watch the Jenkinsfile SDK pin.

---

### Phase 9 — Observability (OpenTelemetry + Serilog) (2026-04-27)

#### Added

- OpenTelemetry tracing — `ActivitySource "FrigateRelay"` with 5 named spans covering the full pipeline (`mqtt.receive`, `event.match`, `dispatch.enqueue`, `dispatch.consume`, `action.execute`).
- OpenTelemetry metrics — `Meter "FrigateRelay"` with 8 counters (`frigaterelay.events.*`, `frigaterelay.actions.*`, `frigaterelay.validators.*`, `frigaterelay.errors.unhandled`) with per-subscription/action/camera/label tag dimensions.
- `ActivityContext` propagated on `DispatchItem` across the `Channel<T>` boundary so child spans correctly parent to the root MQTT-receive span.
- Conditional OTLP exporter — registers when `OTEL_EXPORTER_OTLP_ENDPOINT` is set; no-ops otherwise.
- Serilog structured logging — `UseSerilog` with rolling file sink + conditional Seq sink (when `Serilog:Seq:ServerUrl` is set).
- `StartupValidation.ValidateObservability` — fail-fast on malformed OTLP or Seq URIs.
- Unit tests for span shape and counter increments; integration test `TraceSpans_CoverFullPipeline` asserting root + 4 child spans (192/194 pass; 2 pre-existing integration regressions tracked separately).

#### Fixed

- ID-6: `OperationCanceledException` during shutdown no longer sets `ActivityStatusCode.Error`; cancellation on `ct.IsCancellationRequested` is treated as expected termination.

---

### Phase 8 — Profiles in Configuration (2026-04-27)

#### Added

- Named `Profiles` dictionary in configuration — operators define reusable action lists; subscriptions reference a profile by name or declare an inline `Actions` list (mutually exclusive, fail-fast if both or neither).
- `ProfileResolver` — resolves subscriptions at startup, appending all errors before throwing one aggregated `InvalidOperationException` (collect-all D7 pattern, closes ID-2 + ID-10).
- `ActionEntryTypeConverter` — `IConfiguration.Bind` now converts a scalar string `"BlueIris"` into `ActionEntry("BlueIris")`, restoring the legacy shorthand form (closes ID-12).
- `appsettings.Example.json` — 9-subscription reference configuration in Profiles shape, linked as a test fixture for `ConfigSizeParityTest`.
- `StartupValidation.ValidateAll` collect-all entry point — all startup passes accumulate into a shared `List<string> errors`; operators see every misconfiguration at once.

#### Changed

- Visibility sweep: 7 `FrigateRelay.Host` types internalized (`IActionDispatcher`, `ChannelActionDispatcher`, `DispatchItem`, `EventPump`, `SubscriptionMatcher`, `DedupeCache`, `ProfileResolver`) — closes ID-2.
- `ActionEntry`, `ActionEntryJsonConverter`, `SnapshotResolverOptions` internalized; `DynamicProxyGenAssembly2` `<InternalsVisibleTo>` added to host csproj for NSubstitute mocking — closes ID-10.

#### Fixed

- `StartupValidation.ValidateSnapshotProviders` and `ValidateValidators` were dead code (never called from `HostBootstrap.ValidateStartup`); both wired in this phase — prevents silent no-op startup validation.

---

### Phase 7 — Validators (CodeProject.AI integration) (2026-04-26)

#### Added

- `IValidationPlugin` gains `SnapshotContext` parameter — snapshot resolved once per dispatch and shared between validator chain and action plugin.
- `ActionEntry.Validators` field — per-action named validator references; validators are per-action, not global (decision V3).
- `FrigateRelay.Plugins.CodeProjectAi` — `CodeProjectAiValidator` posts to CodeProject.AI `/v1/vision/detection`, applies `MinConfidence` + `AllowedLabels` filter, and honours configurable `OnError: FailClosed | FailOpen`.
- Named validator instances via `AddKeyedSingleton<IValidationPlugin>` — multiple named instances of the same validator type with independent options.
- `StartupValidation.ValidateValidators` — fail-fast on undefined validator name references.
- `SnapshotContext(SnapshotResult? preResolved)` constructor — dispatcher pre-resolves once when validators are present and passes cached result through the chain.
- Integration test `Validator_ShortCircuits_OnlyAttachedAction` — validates that a failing validator cancels only the action it is attached to, not sibling actions.

#### Changed

- Validator `HttpClient` has **no Polly retry handler** (asymmetric with action plugins — documented; validating a stale inference is worse than skipping).

---

### Phase 6 — Pushover notifications + snapshot attachment (2026-04-26)

#### Added

- `FrigateRelay.Plugins.Pushover` — `PushoverActionPlugin` sends notifications via Pushover API (`multipart/form-data`) with configurable `MessageTemplate`, `Priority`, and optional image attachment.
- `EventTokenTemplate` — shared token-substitution engine (moved to `FrigateRelay.Abstractions`) replacing per-plugin URL-template copies; supports `{camera}`, `{label}`, `{event_id}`, `{zone}`.
- `IActionPlugin.ExecuteAsync` gains a `SnapshotContext` parameter (ARCH-D2) — plugins that don't use snapshots accept and ignore it; snapshot-consuming plugins call `snapshot.ResolveAsync(ctx, ct)`.
- `SnapshotContext` readonly struct — wraps `ISnapshotResolver` + per-action/subscription override names; `default(SnapshotContext).ResolveAsync` short-circuits to `null`.
- `DispatchItem` carries `SnapshotContext` so the dispatcher passes it through to each plugin.
- Integration test `MqttToBothActionsTests` — verifies BlueIris trigger fires AND Pushover notification sends in the same dispatch.

#### Changed

- `BlueIrisActionPlugin.ExecuteAsync` updated to accept (and ignore) the new `SnapshotContext` parameter.

---

### Phase 5 — Snapshot providers (BlueIris + Frigate) (2026-04-26)

#### Added

- `ISnapshotProvider` plugin type — `FetchAsync(SnapshotRequest, CancellationToken)` returns `SnapshotResult?` (null when no image available).
- `FrigateRelay.Plugins.BlueIris` gains `BlueIrisSnapshotProvider` — fetches JPEG from BlueIris camera URL template with optional TLS bypass (`AllowInvalidCertificates`).
- `FrigateRelay.Plugins.FrigateSnapshot` — new plugin assembly; `FrigateSnapshotProvider` fetches latest snapshot from Frigate's HTTP API by event ID.
- `ISnapshotResolver` (host-internal) — resolves provider by name following 3-tier precedence: per-action override → per-subscription default → global `DefaultSnapshotProvider`.
- `StartupValidation.ValidateSnapshotProviders` — fail-fast on undefined snapshot provider references.
- `SnapshotResolverOptions` — binds from top-level `Snapshots` config section.

#### Fixed

- `StartupValidation.ValidateSnapshotProviders` was defined but never called from `HostBootstrap.ValidateStartup` — wired in Phase 5 cleanup.
- `EventId` URL-encoded in `FrigateSnapshotProvider` path construction (advisory A1).
- `[Url]` DataAnnotation added to `FrigateSnapshotOptions.BaseUrl` (advisory A2).

---

### Phase 4 — Action dispatcher + BlueIris trigger plugin (2026-04-25)

#### Added

- `IActionDispatcher` / `ChannelActionDispatcher` — per-plugin `Channel<DispatchItem>` with `BoundedChannelFullMode.DropOldest` (capacity 256, configurable); drop events logged at Warning with event_id + action + capacity.
- `frigaterelay.dispatch.drops` counter (tagged `action`) — telemetry for queue overflow drops.
- `frigaterelay.dispatch.exhausted` counter (tagged `action`) — telemetry for Polly retry exhaustion.
- `FrigateRelay.Plugins.BlueIris` — `BlueIrisActionPlugin` sends HTTP GET to Blue Iris trigger URL; URL template with `{camera}`, `{label}`, `{event_id}`, `{zone}` tokens; unknown placeholders fail at startup.
- Polly v8 retry — `AddResilienceHandler` on `HttpClient`; 3 / 6 / 9 s fixed delays on transient HTTP errors.
- Per-plugin TLS opt-in — `SocketsHttpHandler.SslOptions.RemoteCertificateValidationCallback` gated by `AllowInvalidCertificates: true` config flag.
- `SubscriptionOptions.Actions` array — subscriptions reference action plugins by name; unknown names fail fast at startup.
- `FrigateRelay.IntegrationTests` project — `MqttToBlueIris_HappyPath` test using Testcontainers (Mosquitto) + WireMock.Net.
- `--skip-integration` flag in `run-tests.sh` for Windows CI runners where Linux containers are unavailable.

#### Fixed

- `PluginRegistrarRunner.RunAll` was incorrectly called after `builder.Build()` (introduced in Phase 1 simplification) — moved back to pre-`Build` so registrar mutations reach the DI container.
- `FrigateMqttEventSource.DisposeAsync` threw `ObjectDisposedException` on shutdown — `Interlocked.Exchange` idempotency guard + targeted `catch (ObjectDisposedException)` wrappers added.

---

### Phase 3 — MQTT source + event pipeline (2026-04-24)

#### Added

- `FrigateRelay.Sources.FrigateMqtt` — `FrigateMqttEventSource` subscribes to `frigate/events` via MQTTnet v5 plain `IMqttClient` with 5-second custom reconnect loop; per-client TLS via `WithTlsOptions`.
- `EventContextProjector` — maps `FrigateEventObject` MQTT payload (System.Text.Json) to immutable `EventContext`.
- `EventPump` (`BackgroundService`) — consumes `IAsyncEnumerable<EventContext>` from `IEnumerable<IEventSource>`, runs subscription matching + deduplication, enqueues to `IActionDispatcher`.
- `SubscriptionMatcher` (host-internal) — camera / object-label / zone / `false_positive` / stationary-event filters.
- `DedupeCache` — scoped `IMemoryCache` keyed per subscription on camera + label; deduplicate repeated events within the TTL window.
- `SubscriptionOptions` + `HostSubscriptionsOptions` — top-level `Subscriptions` config section binding.
- `FrigateEventObject` record — DTO for Frigate `frigate/events` MQTT payload with `Before`/`After` nullable sub-objects.
- `.github/scripts/run-tests.sh` — auto-discovers test projects via `find tests -maxdepth 2`; CI + Jenkinsfile need zero edits when a new test project is added.

#### Changed

- `PlaceholderWorker` removed; replaced by `EventPump`.
- `FrigateMqttOptions` reduced to transport-only (host, port, credentials, TLS); `Subscriptions` moved to top-level config section.

---

### Phase 2 — CI + secret scanning + Dependabot (2026-04-24)

#### Added

- `.github/workflows/ci.yml` — PR gate; matrix `[ubuntu-latest, windows-latest]`; `actions/setup-dotnet@v4` with `global-json-file: global.json`; build + `dotnet run --project` per test project; no coverage.
- `.github/workflows/secret-scan.yml` — `scan` job (repo-wide grep) + `tripwire-self-test` job (greps fixture file, fails if any pattern does NOT match — proves regex set is alive).
- `.github/secret-scan-fixture.txt` — committed fixture of secret-shaped strings for tripwire self-test.
- `.github/dependabot.yml` — weekly Monday updates for `nuget` + `github-actions` ecosystems; FluentAssertions hard-pinned to `< 7.0.0` (license constraint).
- `Jenkinsfile` — scripted coverage pipeline; Docker agent `mcr.microsoft.com/dotnet/sdk:10.0`; MTP coverage extension with `--coverage-output-format cobertura`; workspace-local NuGet cache.

---

### Phase 1 — Solution scaffold and abstractions (2026-04-24)

#### Added

- `FrigateRelay.sln` — solution file with `Directory.Build.props` (`Nullable=enable`, `TreatWarningsAsErrors=true`, `LangVersion=latest`).
- `global.json` — .NET SDK version pin with `rollForward: latestFeature`.
- `FrigateRelay.Abstractions` — public plugin contracts: `IEventSource`, `IActionPlugin`, `IValidationPlugin`, `ISnapshotProvider`, `IPluginRegistrar`, `PluginRegistrationContext`, `EventContext`, `Verdict`, `SnapshotContext`.
- `FrigateRelay.Host` — generic host (`Microsoft.Extensions.Hosting`), DI bootstrap (`HostBootstrap`), `PluginRegistrarRunner`, `PlaceholderWorker`.
- `tests/FrigateRelay.Abstractions.Tests` — 10 contract-shape tests covering `Verdict` static factories, `EventContext` immutability, `PluginRegistrationContext` ctor.
- `tests/FrigateRelay.Host.Tests` — 7 host-wiring tests covering `PluginRegistrarRunner` and `PlaceholderWorker`.
- `tests/FrigateRelay.TestHelpers` — shared `CapturingLogger<T> : ILogger<T>` used across all test projects.
- `.editorconfig` — suppresses CA1707 for `tests/**/*.cs` (underscore test-name convention).
- `appsettings.json`, `appsettings.Local.json` (gitignored), `appsettings.Development.json` — base configuration layering.
- `UserSecretsId` pinned to a stable GUID for consistent contributor experience.

[unreleased]: https://github.com/blehnen/FrigateRelay/compare/v1.0.1...HEAD
[1.0.1]: https://github.com/blehnen/FrigateRelay/releases/tag/v1.0.1
[1.0.0]: https://github.com/blehnen/FrigateRelay/releases/tag/v1.0.0
