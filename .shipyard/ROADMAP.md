# FrigateRelay — ROADMAP

## Preamble

FrigateRelay is a greenfield .NET 10 background service that supersedes the author's `FrigateMQTTProcessingService`. Every architectural decision (plugin model A, validator scope V3, config shape S2, async pipeline P1 with P3 escape hatch, snapshots via `ISnapshotProvider`, observability via M.E.L.+Serilog+OTel, MIT license, Docker-first) is settled in `PROJECT.md`; phases below only execute those decisions.

**Ordering rationale.** Contracts and CI come first so every subsequent phase has a stable target and a working test/lint loop — this is where the reference project failed most badly (zero tests, no VCS, no CI). We then deliver one thin, demonstrable **vertical slice** (Frigate MQTT → match → BlueIris HTTP trigger) before fanning out additional plugins, so a real event round-trip proves the pipeline shape. Snapshot providers come before Pushover because Pushover is the first consumer of `ISnapshotProvider`. The CodeProject.AI validator lands after two actions exist so the per-action short-circuit semantics can be proven end-to-end rather than hypothetically. Observability goes in **before** release polish so production diagnostics ship with v1, not after. Docker release and OSS polish are last because they harden an already-working product. Parity cutover is final: it is the success criterion that validates the rewrite.

Risk is front-loaded: Foundation (Phase 1), CI (Phase 2), and MQTT ingestion (Phase 3) are the highest-risk items because every later phase depends on their shape. Later phases are incremental plugin work with decreasing architectural risk.

## Dependency Graph

```
Phase 1 (Foundation + Abstractions) ──┬─► Phase 3 (Frigate MQTT ingest) ─► Phase 4 (Dispatcher + BlueIris slice)
                                      │                                              │
Phase 2 (CI skeleton, parallel) ──────┘                                              ▼
                                                                        Phase 5 (Snapshot providers)
                                                                                    │
                                                                                    ▼
                                                                        Phase 6 (Pushover action)
                                                                                    │
                                                                                    ▼
                                                                        Phase 7 (CodeProject.AI validator)
                                                                                    │
                                                                                    ▼
                                                                        Phase 8 (Profiles in config)
                                                                                    │
                                                                                    ▼
                                                                        Phase 9 (Observability)
                                                                                    │
                                                                                    ▼
                                                                       Phase 10 (Docker + release wf)
                                                                                    │
                                                                                    ▼
                                                                       Phase 11 (OSS polish)
                                                                                    │
                                                                                    ▼
                                                                       Phase 12 (Parity cutover)
```

Phases 1 and 2 can execute in parallel once the `.sln` exists (Phase 2 needs a solution to build). Phases 3–12 are strictly sequential.

---

## Phase 1 — Foundation and Abstractions — **COMPLETE (2026-04-24)**

**Status.** All 12 closeout criteria met. 17 tests pass (10 Abstractions + 7 Host). Graceful shutdown confirmed. Reports: `.shipyard/phases/1/{VERIFICATION.md, results/{AUDIT-1.md, SIMPLIFICATION-1.md, DOCUMENTATION-1.md, SUMMARY-{1.1,2.1,3.1}.md, REVIEW-{1.1,2.1,3.1}.md}}`.

**Goal.** Stand up the repo layout, solution, and the public plugin contracts that every later phase consumes, plus a runnable (but work-free) host skeleton.

**Dependencies.** None.

**Risk.** **High** — contract shape drives every later phase. Getting `EventContext`, `Verdict`, and the registrar pattern wrong here means rework across all plugins.

**Estimate.** 4–6 hours.

**Deliverables.**
- `FrigateRelay.sln` at repo root with `Directory.Build.props` enabling `net10.0`, `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<LangVersion>latest</LangVersion>`.
- `src/FrigateRelay.Abstractions/` project exporting: `IEventSource`, `IActionPlugin`, `IValidationPlugin`, `ISnapshotProvider`, `EventContext`, `Verdict` (record with `Passed`, `Reason`, optional `Score`), `SnapshotRequest`/`SnapshotResult`, `IPluginRegistrar`, `PluginRegistrationContext`.
- `src/FrigateRelay.Host/` project: `Program.cs` using `Host.CreateApplicationBuilder`, layered config (`appsettings.json` + env vars + user-secrets + optional `appsettings.Local.json`), a no-op `BackgroundService` placeholder, DI composition root, and a plugin-registrar discovery loop that iterates `IEnumerable<IPluginRegistrar>` from DI.
- `tests/FrigateRelay.Abstractions.Tests/` and `tests/FrigateRelay.Host.Tests/` with MSTest v3 + FluentAssertions 6.12.2 + NSubstitute wired up; at least one contract-shape test per interface (e.g. `EventContext` is immutable, `Verdict.Fail` carries a reason).
- `.editorconfig`, `.gitignore`, `global.json` pinning the .NET 10 SDK.

**Success criteria (verifiable).**
- `dotnet build FrigateRelay.sln -c Release` succeeds on Windows and WSL Linux with zero warnings.
- `dotnet run --project src/FrigateRelay.Host` starts, logs "Host started" at Information level, and exits cleanly on Ctrl-C within 5 seconds.
- `dotnet test` reports **≥ 6** passing tests across both test projects, zero failures.
- Contract assemblies (`FrigateRelay.Abstractions.dll`) reference only `Microsoft.Extensions.*` — `dotnet list package --include-transitive` shows no third-party runtime deps.
- `git grep ServicePointManager` returns zero results (risk reduction: old codebase's global TLS bypass is structurally impossible — there is no place for it to live).

---

## Phase 2 — CI Skeleton — **COMPLETE (2026-04-24)**

**Status.** 6 deliverables shipped: `.github/dependabot.yml`, `.github/workflows/ci.yml`, `.github/workflows/secret-scan.yml`, `.github/scripts/secret-scan.sh`, `.github/secret-scan-fixture.txt`, `Jenkinsfile`. All 4 reviewers PASS (PLAN-1.2 had 1 minor → fixed in `579126e`). Post-phase gates clear. Reports: `.shipyard/phases/2/{VERIFICATION.md, results/{AUDIT-2.md, SIMPLIFICATION-2.md, DOCUMENTATION-2.md, SUMMARY-*.md, REVIEW-*.md}}`.

**Goal.** GitHub Actions pipeline that builds and tests on every push, matching the `DotNetWorkQueue` workflow pattern, plus a committed-secrets tripwire.

**Dependencies.** Phase 1 (needs a buildable solution).

**Risk.** **Medium** — CI being wrong is a productivity tax on every subsequent phase, but the blast radius is limited.

**Estimate.** 2–3 hours.

**Deliverables.**
- `.github/workflows/build.yml` — triggers on `push` and `pull_request`; matrix on `windows-latest` and `ubuntu-latest`; runs `dotnet restore`, `dotnet build -c Release --no-restore`, `dotnet test -c Release --no-build --collect:"XPlat Code Coverage" --logger trx`; uploads coverage and trx artifacts. Patterned on `F:\Git\DotNetWorkQueue\.github\workflows` (to be consulted during build).
- `.github/workflows/secret-scan.yml` — a `secret-leak-grep` job that fails the build if `git grep -E 'AppToken\s*=\s*[A-Za-z0-9]{20,}|UserKey\s*=\s*[A-Za-z0-9]{20,}|api[a-z0-9]{28,}|192\.168\.[0-9]+\.[0-9]+'` returns any matches outside `.shipyard/` documentation.
- `.github/dependabot.yml` for nuget + github-actions ecosystems (weekly).

**Success criteria (verifiable).**
- First PR into the repo shows both workflows completing green.
- A deliberately-committed test string `AppToken=abcdefghijklmnopqrstuvwxyz0123` in a dummy branch causes `secret-scan.yml` to **fail** with that line in its log (verified once, then reverted).
- Coverage artifact (`coverage.cobertura.xml`) is downloadable from the Actions run summary.

**Risk reductions vs. CONCERNS.md.** Directly addresses "no automated tests" (High), "no version control" (High), and committed-secrets risk (High — old config held plaintext Pushover `AppToken`/`UserKey`).

---

## Phase 3 — Frigate MQTT Ingestion and EventContext Projection — **COMPLETE (2026-04-24)**

**Status.** Frigate MQTT plugin shipped (MQTTnet v5, custom reconnect, channel bridge, per-client TLS). Host-side `EventPump` + `SubscriptionMatcher` + `DedupeCache` wired. CI consolidated via `.github/scripts/run-tests.sh` (discharges Phase 2 advisory). 44 tests pass. Graceful shutdown verified. Reports under `.shipyard/phases/3/`. Deviations from legacy: D1 fire-all-matching, D5 false_positive skip — both flagged for Phase 12 parity docs.

**Goal.** Subscribe to `frigate/events`, project payloads into `EventContext`, apply subscription matching and per-camera+label dedupe. No downstream actions yet — events terminate at a logged "matched" message.

**Dependencies.** Phase 1.

**Risk.** **High** — MQTT client lifecycle, reconnect, and cancellation-token wiring are the most common sources of hangs-on-shutdown and leaked-handler bugs. Fix the shape here or pay for it later.

**Estimate.** 4–6 hours.

**Deliverables.**
- `src/FrigateRelay.Sources.FrigateMqtt/` project: `FrigateMqttEventSource : IEventSource`, `FrigateMqttOptions` (server, port, client-id, topic, TLS opts), `FrigatePayload` DTOs (`FrigateEvent`, `FrigateEventBefore`, `FrigateEventAfter`), `SubscriptionMatcher`, `DedupeCache` wrapping `IMemoryCache` (scoped, **not** `MemoryCache.Default`).
- `FrigateMqtt.PluginRegistrar : IPluginRegistrar` — registers `FrigateMqttEventSource` as `IEventSource` and binds options from the `FrigateMqtt` config section.
- MQTTnet v5 `ManagedMqttClient` with auto-reconnect and per-plugin TLS options (`AllowInvalidCertificates` flag → plugin-owned `SocketsHttpHandler`/client TLS callback — **no global ServicePointManager callback**).
- `tests/FrigateRelay.Sources.FrigateMqtt.Tests/` — unit tests covering: payload deserialization (new/update/end), subscription match on camera+label, subscription match with zone filter (present in `before.current_zones`, `after.current_zones`, `before.entered_zones`, `after.entered_zones`), stationary-guard skip on `update`/`end`, dedupe cache hit/miss with configurable TTL, first-match-wins behavior.
- Host wiring so a sample `appsettings.Local.json` with one subscription produces "matched event: camera=X, label=Y" log lines when a hand-crafted payload is republished.

**Success criteria (verifiable).**
- `dotnet test tests/FrigateRelay.Sources.FrigateMqtt.Tests` reports **≥ 15** passing tests.
- Running the host against a locally-run `docker run eclipse-mosquitto` and `mosquitto_pub -t frigate/events -m '<sample payload>'` produces exactly one matched-event log line per configured-subscription match.
- Graceful shutdown: sending SIGINT while the subscription is active logs "MQTT disconnected" within 5 seconds and the process exits with code 0.
- `git grep -n "ServerCertificateValidationCallback" src/` returns zero matches in source (only in docs).

**Risk reductions vs. CONCERNS.md.** Eliminates the god-class `Main.cs` (Medium), per-subscription throttle key ambiguity (Low), `MemoryCache.Default` global collision (Low), and the triplicated `new`/`update`/`end` branches (Medium) by funneling all three into one `EventContext` projection.

---

## Phase 4 — Action Dispatcher + BlueIris (First Vertical Slice)

**Status:** complete_with_gaps (2026-04-25) — all 4 ROADMAP success criteria PASS; 71/71 tests across 5 suites; integration test passes in 4.3s (<30s SLO). 8 minor gaps tracked in `.shipyard/ISSUES.md` (ID-2 through ID-9), none blocking Phase 5. ID-5 resolved post-build. Security audit verdict: low_risk (3 advisory items, no critical/high).

**Goal.** Channel-backed `IActionDispatcher`, resilience policies, and the first `IActionPlugin` (`BlueIris`) wired end-to-end so one MQTT event triggers one HTTP GET.

**Dependencies.** Phase 3.

**Risk.** **Medium** — the dispatcher shape is a long-lived contract; get the channel bounds and backpressure semantics right now.

**Estimate.** 4–5 hours.

**Deliverables.**
- `src/FrigateRelay.Host/Dispatch/IActionDispatcher.cs` — abstraction with `ValueTask EnqueueAsync(EventContext ctx, IActionPlugin action, CancellationToken ct)`; seam exists so a future durable dispatcher (P3 escape hatch) can swap in.
- `ChannelActionDispatcher : IActionDispatcher, IHostedService` — bounded `Channel<DispatchItem>` per action, configurable capacity, drop-oldest metric on overflow, 2 consumer tasks per channel, `Microsoft.Extensions.Resilience` (Polly v8) retry pipeline with 3/6/9-second delays matching reference behavior, **and** a post-exhaustion log at Warning level with the dropped event id (directly fixes the "silent drop" concern).
- `src/FrigateRelay.Plugins.BlueIris/` — `BlueIrisActionPlugin : IActionPlugin`, `BlueIrisOptions` (trigger URL template, `AllowInvalidCertificates`), `BlueIris.PluginRegistrar`. Uses named `HttpClient` via `IHttpClientFactory`; TLS skipping is opt-in per-plugin only.
- `tests/FrigateRelay.Host.Tests/Dispatch/` — dispatcher unit tests: single-event throughput, backpressure behavior, retry-then-exhaust-with-warning, cancellation propagation.
- `tests/FrigateRelay.IntegrationTests/` (new): `MqttToBlueIrisSliceTests` using **Testcontainers Mosquitto** and a `WireMock.Net` stub representing Blue Iris. Publishes a Frigate-shaped payload, asserts the Blue Iris stub received exactly one GET with the expected URL.

**Success criteria (verifiable).**
- `dotnet test tests/FrigateRelay.IntegrationTests` reports **≥ 1** end-to-end test (`MqttToBlueIris_HappyPath`) passing in under 30 seconds.
- Unit tests for the dispatcher: **≥ 6** passing, covering retry exhaustion (asserts a "dropped after 3 retries" warning was logged, with event id in the log state).
- Running the host manually with a real Mosquitto broker + a real Blue Iris instance (or stub) fires the trigger URL within 2 seconds of the MQTT event.
- No `.Result` or `.Wait()` calls in source: `git grep -nE '\.(Result|Wait)\(' src/` returns zero matches.

**Risk reductions vs. CONCERNS.md.** Fixes "silent drop after retry exhaustion" (Medium), "`.Result` deadlock risk" (Medium), "`Task.WaitAll` in async handler" (Medium), and DotNetWorkQueue coupling.

---

## Phase 5 — Snapshot Providers

**Status:** complete_with_gaps (2026-04-26) — all 7 ROADMAP deliverables PASS; 100/100 tests across 6 suites (99 unit + 1 integration); 29 new tests vs ≥10 gate (+190%). 1 critical (REVIEW-3.1: `ValidateSnapshotProviders` was dead code) resolved inline before phase closeout. Security audit: PASS (Low) — 0 critical, 0 important, 2 advisory. 5 simplifier findings + 2 auditor advisory deferred (all trivial, recommended as a single chore commit before Phase 6). 3 new ISSUES entries (ID-10 accessibility cascade, ID-11 `CapturingLogger<T>` triplication, ID-12 `IConfiguration.Bind` back-compat regression for legacy `Actions: ["BlueIris"]` shape).

**Goal.** Add the `ISnapshotProvider` shipped implementations (`BlueIrisSnapshot`, `FrigateSnapshot`) and the resolution order (per-action override → per-subscription default → global `DefaultSnapshotProvider`).

**Dependencies.** Phase 4.

**Risk.** **Low** — self-contained plugin work with well-defined inputs.

**Estimate.** 2–3 hours.

**Deliverables.**
- `BlueIrisSnapshot : ISnapshotProvider` **colocated in** `src/FrigateRelay.Plugins.BlueIris/` (registrar registers both action and snapshot providers — first-class BlueIris as both action and snapshot source, per PROJECT.md goal #4).
- `src/FrigateRelay.Plugins.FrigateSnapshot/` — `FrigateSnapshotProvider` supporting `/api/events/<id>/snapshot.jpg` (configurable `bbox` overlay) and `/thumbnail.jpg`.
- `src/FrigateRelay.Host/Snapshots/SnapshotResolver.cs` implementing the 3-tier resolution order, keyed by provider name.
- Unit tests for resolver precedence with NSubstitute-mocked providers; unit tests for both concrete providers with mocked HTTP handlers.

**Success criteria (verifiable).**
- `dotnet test` adds **≥ 10** new passing tests across snapshot provider and resolver suites.
- Resolver test `PerAction_OverridesSubscription_OverridesGlobal` asserts exact provider identity returned for each tier.
- A configured `appsettings.Local.json` with `DefaultSnapshotProvider: "BlueIris"` and one subscription override to `"Frigate"` selects the correct provider verified via a debug-only log line emitted by the resolver.

---

## Phase 6 — Pushover Action

**Status:** complete (2026-04-26) — all 7 ROADMAP deliverables PASS; 124/124 tests across 7 suites (122 unit + 2 integration); 24 new tests vs ≥8 gate (+200%). 0 critical review findings. Security audit: PASS (Low) — 0 critical/important, 1 advisory (Polly explicit-predicate suggestion). Simplifier flagged 2 high-priority items deferred to Phase 7 prep (`CapturingLogger<T>` 4-copy extraction; `HostBootstrap` per-plugin QueueCapacity loop). Documenter flagged 2 CLAUDE.md gaps actionable now (IActionPlugin.ExecuteAsync 3-param signature; ID-12 object-form Actions warning). 1 architectural change: `EventTokenTemplate` extracted to Abstractions; `IActionPlugin.ExecuteAsync` signature gains `SnapshotContext` parameter.

**Goal.** Second action plugin, consuming `ISnapshotProvider`, end-to-end through the dispatcher.

**Dependencies.** Phase 5.

**Risk.** **Low** — pattern established by BlueIris; differs only in multipart POST and snapshot attachment.

**Estimate.** 2–3 hours.

**Deliverables.**
- `src/FrigateRelay.Plugins.Pushover/` — `PushoverActionPlugin : IActionPlugin`, `PushoverOptions` (`AppToken`, `UserKey`, default `""`, must be overridden via env vars or user-secrets; never in committed `appsettings.json`).
- Multipart POST to `https://api.pushover.net/1/messages.json` using `IHttpClientFactory`-managed client; snapshot bytes resolved via `SnapshotResolver`.
- `tests/FrigateRelay.Plugins.Pushover.Tests/` — unit tests with a `WireMock.Net` Pushover stub; assertions on multipart parts (`token`, `user`, `message`, `attachment`) and content types.
- Extend `MqttToBlueIrisSliceTests` fixture (or add `MqttToBothActionsTests`) asserting one Frigate event fans out to **both** actions independently.

**Success criteria (verifiable).**
- `dotnet test` adds **≥ 8** passing tests.
- Integration test `Event_FansOut_ToBlueIrisAndPushover` asserts both downstream stubs received exactly one request each per event.
- `git grep -E '[A-Za-z0-9]{28,}' src/ config/` returns zero matches (secret-shape tripwire clean).

---

## Phase 7 — CodeProject.AI Validator

**Status:** complete (2026-04-26) — 19 new tests vs ≥10 gate (+90% cushion); 143/143 across all suites. All 13 CONTEXT-7 decisions honored. Security audit: PASS / Low (0 critical/high/medium, 3 informational notes — all matching Phase 4-6 patterns). Simplifier: 3 Low findings, all deferred (Rule of Two / Rule of Three not yet hit). Documenter: 4 actionable CLAUDE.md gaps + 1 Phase 11 plugin-author-guide note — all 5 deferred to Phase 11 OSS-polish docs sprint per the established Phase 5+6 pattern. Phase 5 review-3.1 dead-code regression mode applied to ValidateValidators wiring (loud inline comment + grep acceptance criterion).

**Goal.** `IValidationPlugin` contract wired into per-action validator chains with proven short-circuit semantics.

**Dependencies.** Phase 6.

**Risk.** **Medium** — per-action validator short-circuit is a distinctive design feature (V3 from brainstorming); ordering, failure surfaces, and logging around it need to be right.

**Estimate.** 3–4 hours.

**Deliverables.**
- `src/FrigateRelay.Plugins.CodeProjectAi/` — `CodeProjectAiValidator : IValidationPlugin`, `CodeProjectAiOptions` (base URL, min confidence, allowed labels, optional zone-of-interest bbox).
- Dispatcher update: each queued `DispatchItem` carries an ordered `IReadOnlyList<IValidationPlugin>` attached to **that specific action**. On any `Verdict.Failed`, the action is skipped and a structured log `"validator_rejected"` with action name + validator name + reason is emitted. Other actions in the same event proceed independently.
- `tests/FrigateRelay.Plugins.CodeProjectAi.Tests/` — unit tests covering: confidence pass/fail, label allowlist, bbox zone filter, validator timeout → fail-closed.
- Integration test `Validator_ShortCircuits_OnlyAttachedAction` — one subscription with BlueIris (no validator) + Pushover (CodeProject.AI validator). CodeProject.AI stub returns low confidence. Assert BlueIris stub received 1 request, Pushover stub received 0, and a `"validator_rejected"` log event was emitted with `action="Pushover"`.

**Success criteria (verifiable).**
- Integration test `Validator_ShortCircuits_OnlyAttachedAction` passes.
- Unit tests: **≥ 8** passing across the validator suite.
- A second integration test `Validator_Pass_BothActionsFire` proves the positive path.

---

## Phase 8 — Profiles in Configuration — **COMPLETE (2026-04-27)**

**Goal.** Named action profiles that subscriptions reference, eliminating 9× repetition. Prove Success Criterion #2 quantitatively.

**Dependencies.** Phase 7.

**Risk.** **Low** — config-shape work; schema already decided (S2).

**Estimate.** 2–3 hours.

**Deliverables.**
- Config binding: `Profiles` dictionary keyed by profile name, each profile declaring an ordered action list (with per-action validators and per-action snapshot overrides). `Subscriptions` may set `Profile: "<name>"` **or** declare an inline `Actions` array.
- Validator: startup fails fast with a clear diagnostic if a subscription references an undefined profile, or if an action names a plugin with no registered `IActionPlugin`.
- A fixture `config/appsettings.Example.json` reproducing the author's 9-subscription production deployment (camera names, object labels, zones, BlueIris URLs — no secrets).
- `tests/FrigateRelay.Host.Tests/Config/ProfileResolutionTests.cs` — unit tests for profile resolution, inline-actions, mix of both in one config, and misconfiguration fail-fast.
- `tests/FrigateRelay.Host.Tests/Config/ConfigSizeParityTest.cs` — a test that loads both the reference INI (committed as a fixture) and `appsettings.Example.json`, computes character counts, and **asserts the JSON is ≤ 60% of the INI character count**.

**Success criteria (verifiable).**
- `ConfigSizeParityTest` passes — Success Criterion #2 is now a CI gate, not a claim.
- Undefined-profile reference test: host exits non-zero at startup with message `"Subscription '<name>' references undefined profile '<profile>'"`.
- Unit tests: **≥ 10** passing across the profile resolution suite.

---

## Phase 9 — Observability — **COMPLETE (2026-04-27)**

**Goal.** Full structured logging, OpenTelemetry traces and metrics that span the full pipeline. No App.Metrics, no OpenTracing.

**Dependencies.** Phase 8.

**Risk.** **Medium** — OTel wiring is fiddly; span parenting across `Channel<T>` boundaries requires explicit `Activity.Current` propagation.

**Estimate.** 3–4 hours.

**Deliverables.**
- `Microsoft.Extensions.Logging` with Serilog sinks (Console, rolling file, optional Seq) configured from `Logging` and `Serilog` sections of `appsettings.json`. No mixed `Log.Logger` static + injected `ILogger` pattern.
- OpenTelemetry registration: OTLP exporter for both traces and metrics; endpoint configurable via standard `OTEL_EXPORTER_OTLP_ENDPOINT` env var.
- Named `ActivitySource` `"FrigateRelay"` with spans: `mqtt.receive` → `event.match` → `dispatch.enqueue` → `action.<name>.execute` → `validator.<name>.check`. Parent-child relationships maintained across the channel hop via `Activity` propagation on the `DispatchItem`.
- Named `Meter` `"FrigateRelay"` exposing counters: `frigaterelay.events.received`, `frigaterelay.events.matched`, `frigaterelay.actions.dispatched`, `frigaterelay.actions.succeeded`, `frigaterelay.actions.failed`, `frigaterelay.validators.passed`, `frigaterelay.validators.rejected`, `frigaterelay.errors.unhandled`.
- `tests/FrigateRelay.Host.Tests/Observability/` — tests using `TracerProvider` with an in-memory exporter asserting span tree shape; tests using `MeterListener` asserting counter increments per event.

**Success criteria (verifiable).**
- Integration test `TraceSpans_CoverFullPipeline` asserts one root span per MQTT event with 4 expected child spans, all under the root activity id.
- Metric test asserts that one matched event dispatched to two actions (one validated, one not) increments `events.received=1`, `events.matched=1`, `actions.dispatched=2`, `actions.succeeded=2`, `validators.passed=1`.
- `git grep -nE 'App\.Metrics|OpenTracing|Jaeger\.' src/` returns zero matches.

**Risk reductions vs. CONCERNS.md.** Replaces archived OpenTracing/Jaeger stack (Medium), replaces App.Metrics where the text-file reporter bug hid (the inverted `Directory.Exists` guard; Low). Fixes the `_logger.Error(ex.Message, ex)` anti-pattern by enforcing `LogError(ex, "message")` shape via a unit-tested logging helper if needed.

---

## Phase 10 — Dockerfile and Multi-Arch Release Workflow  *[COMPLETE_WITH_GAPS 2026-04-28]*

**Status:** Build complete (5/5 plans PASS + 4 fix-ups). Verifier verdict COMPLETE_WITH_GAPS — 192/194 tests pass (2 pre-existing Phase 9 integration regressions, not Phase 10), build clean (0 warnings); 3/4 ROADMAP success criteria verified, 1 blocked (`docker build` image-size check requires a Docker daemon, deferred to first real release tag). Auditor PASS_WITH_NOTES (Low risk, 0 critical / 0 important / 4 advisory tracked as ID-24..27). Simplifier 0 High / 2 Medium (both applied inline) / 3 Low (deferred). Documenter ACCEPTABLE / DEFER_TO_DOCS_SPRINT.

**Goal.** One `docker pull` install path, signed images, multi-arch support.

**Dependencies.** Phase 9.

**Risk.** **Medium** — base-image selection (Alpine vs Debian-slim) and self-contained publish size are the main unknowns; multi-arch buildx occasionally surfaces platform-specific runtime bugs.

**Estimate.** 3–4 hours.

**Deliverables.**
- **Base-image decision locked in this phase**, documented inline in the Dockerfile with rationale. Starting preference: `mcr.microsoft.com/dotnet/runtime-deps:10.0-alpine` (smallest footprint); fall back to `debian-slim` if Alpine musl surfaces MQTTnet or OTel gRPC issues.
- `docker/Dockerfile` — multi-stage build, self-contained publish for `linux-musl-x64` (or `linux-x64`) and `linux-arm64`, non-root runtime user, `HEALTHCHECK` hitting a lightweight `/healthz` endpoint (added to the host in this phase) that returns 200 once MQTT is connected and all hosted services are started.
- `docker/docker-compose.example.yml` — references `ghcr.io/<owner>/frigaterelay:latest`, mounts `./config/appsettings.Local.json`, passes Pushover/BlueIris secrets via `.env`.
- `.github/workflows/release.yml` — triggers on tag `v*`. Uses `docker/setup-qemu-action` + `docker/setup-buildx-action`, builds `linux/amd64,linux/arm64`, logs in to GHCR via `GITHUB_TOKEN`, pushes `ghcr.io/<owner>/frigaterelay:<semver>` and `:latest`.
- Host adds `MapGet("/healthz", ...)` (if using minimal API) **or** a tiny TCP listener; decision documented in the phase.

**Success criteria (verifiable).**
- `docker build -f docker/Dockerfile .` succeeds locally on WSL; resulting image is **≤ 120 MB** uncompressed (verify with `docker image inspect`).
- A dry-run of `release.yml` via `act` (or a prerelease tag `v0.0.0-rc1`) produces a pushed GHCR image with two platform manifests visible via `docker buildx imagetools inspect ghcr.io/<owner>/frigaterelay:v0.0.0-rc1`.
- `docker compose -f docker/docker-compose.example.yml up` with a real Mosquitto and a WireMock BlueIris reproduces the Phase 4 integration behavior end-to-end.
- `/healthz` returns 200 after MQTT connect, 503 before — asserted via a new integration test.

---

## Phase 11 — Open-Source Polish  *[COMPLETE 2026-04-28]*

**Status:** Build complete (6/6 plans PASS across 3 waves, 18 tasks, ~17 implementation commits + 2 inline fix-ups). Verifier verdict COMPLETE — 192/192 tests, 0 build warnings, all 6 ROADMAP success criteria met, all 8 CONTEXT-11 decisions honored. Auditor PASS_WITH_NOTES (Low; 0 critical / 0 important / 3 advisory — A1 path-traversal applied inline, A2 ID-24 carry-over, A3 positive note). Simplifier 0 High / 0 Medium / 3 Low (all Rule-of-Three deferral notes). Documenter ACCEPTABLE (Phase 11 self-CHANGELOG entry added inline). Wave 1 closed the 2 Phase 9 integration regressions; ID-4 closed.

**Goal.** Make the repo contributor-ready and new-user-onboardable in under one hour (Success Criterion #5).

**Dependencies.** Phase 10.

**Risk.** **Low** — documentation and templates.

**Estimate.** 3–4 hours.

**Deliverables.**
- `README.md` — overview, quickstart (`docker run`-based), full config walkthrough against the Phase 8 example, "Adding a new action plugin" tutorial referencing the scaffold template below.
- `CONTRIBUTING.md` — coding standards, test expectations, PR checklist.
- `LICENSE` — MIT, with correct author and year 2026.
- `.github/ISSUE_TEMPLATE/bug_report.yml`, `.github/ISSUE_TEMPLATE/feature_request.yml`, `.github/pull_request_template.md`.
- `templates/FrigateRelay.Plugins.Template/` — a scaffoldable minimal action-plugin project with `ExampleActionPlugin`, a passing unit test, and a registrar. Documented in README.
- `docs/plugin-author-guide.md` — the contract reference: how `IActionPlugin`, `IValidationPlugin`, `ISnapshotProvider` are invoked, lifecycle, DI scope rules, and the "design for B" note that the same plugin shape will load from `AssemblyLoadContext` in a future phase.

**Success criteria (verifiable).**
- Scaffold template compiles as-is: `dotnet build templates/FrigateRelay.Plugins.Template -c Release` succeeds with zero warnings, and its bundled unit test passes under `dotnet test`.
- Scaffold is rename-safe: a `scaffold-smoke` step in `.github/workflows/build.yml` copies the template to a scratch path, renames namespace + project name + assembly name via `sed`, then builds and tests the copy. The job fails if any step fails — this is the structural proxy for Success Criterion #5 (onboarding), replacing the earlier timed-dry-run check that required an external volunteer.
- `docs/plugin-author-guide.md` has one code sample per contract interface; each sample is copied verbatim into a `samples/FrigateRelay.Samples.PluginGuide/` project that CI builds and tests on every push, so stale docs cannot silently ship.
- `README.md` contains no references to "File Transfer Server" or any reference-project boilerplate.

---

## Phase 12 — Parity Cutover

**Goal.** Run FrigateRelay side-by-side with `FrigateMQTTProcessingService` against the same MQTT broker, prove behavioral parity, document migration.

**Dependencies.** Phase 11.

**Risk.** **Medium** — real-world payloads reveal assumptions that stubs cannot. This is the phase where Success Criterion #1 is won or lost.

**Estimate.** 4–6 hours of **active work** (deployment, script, reconciliation) plus **~48 hours of passive wall-clock observation** during the parity window. The observation period runs hands-off in the background — no ongoing work required, just waiting for the window to close before reconciliation.

**Deliverables.**
- Side-by-side deployment: reference service and FrigateRelay both subscribed to `frigate/events` on the production broker, both triggering BlueIris (reference service to production URLs, FrigateRelay to a shadow/dev camera set or a logging-only stub depending on risk appetite) for **≥ 48 hours**.
- `docs/migration-from-frigatemqttprocessing.md` — field-by-field mapping from the INI `[ServerSettings]`/`[PushoverSettings]`/`[SubscriptionSettings]` blocks to the new `appsettings.json` profile+subscription shape. Includes an `sed`-or-Python conversion script committed under `scripts/migrate-conf.py` (or similar).
- A parity log reconciliation: CSV export from each service of `(timestamp, camera, label, action, outcome)` tuples across the 48-hour window, with a short `docs/parity-report.md` that compares them.

**Success criteria (verifiable).**
- Parity report shows **zero missed alerts** and **zero spurious alerts** across the 48-hour window. Any discrepancy is either (a) explained and documented as an intentional improvement, or (b) fixed before the cutover is declared.
- Cooldown behavior matches: for every camera+label combination firing within one minute, both services issued exactly one notification.
- Migration script converts the author's real `FrigateMQTTProcessingService.conf` into a valid `appsettings.json` that passes `ConfigSizeParityTest` from Phase 8.
- `README.md` migration section references the doc and script.
- Release tag `v1.0.0` cut on GHCR.

**Risk reductions vs. CONCERNS.md.** Addresses the highest-level concern: the reference service is unreviewable and untested. At parity cutover, we have behavior-proven, test-covered, CI-gated, Docker-deployable software with no hard-coded IPs, no global TLS bypass, no plaintext secrets, no silent drops.

---

## Phase Count

**12 phases.** Within the 8–14 range requested. Phases 1–2 parallelizable after the `.sln` exists; Phases 3–12 sequential.

## Questions Appendix

No speculative technology substitutions or scope changes. One intentionally deferred-decision call-out:

- **Alpine vs Debian-slim base image (Phase 10):** PROJECT.md says "locked in during the plan phase"; this roadmap defers the lock-in to the start of Phase 10 because it depends on empirical image-size and MQTTnet/OTel-gRPC compatibility on musl, which cannot be usefully decided earlier. Current lean: **Alpine**, with a documented fallback to Debian-slim. Flagging here rather than pre-deciding.
- **`/healthz` transport (Phase 10):** Minimal API pulls in ASP.NET Core; a raw TCP listener is heavier to maintain. Lean: minimal API, but this is worth a five-minute decision at the top of Phase 10 before committing.
