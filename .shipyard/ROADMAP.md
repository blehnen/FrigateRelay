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

## Phase 12 — Parity Cutover  *[COMPLETE 2026-04-28]*

**Status:** Build complete (8/8 plans PASS across 3 waves, 18 tasks, ~14 implementation commits + 2 inline fix-ups). Verifier verdict COMPLETE — 208/208 tests, 0 build warnings, all ROADMAP success criteria delivered, all 7 CONTEXT-12 D-decisions honored. Auditor PASS_WITH_NOTES (Low; 0 critical / 0 important / 3 advisory — A1 path-traversal applied inline + closed as ID-28, A2 ID-19 carry-over, A3 informational). Simplifier 0H/0M/2L (cross-doc duplication note + CLI dispatch confirmation). Documenter NEEDS_DOCS verdict resolved inline — parity-window-checklist.md `@i` / `@mt` mix-up corrected. **v1.0.0 release tag is the operator's manual step per CONTEXT-12 D7** (see `RELEASING.md`); release.yml auto-builds + pushes multi-arch GHCR images on `v*` tag push.

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

## Phase 13 — v1.1 Observability + Cleanup  *[COMPLETE 2026-05-04]*

**Status:** Complete. v1.1.0 shipped 2026-05-04 (PR #38 / #35 counter tags, PR #39 / #36 docs + compose stack, PR #40 / #34 BlueIrisUrlTemplate refactor). 242/242 tests, 0 warnings. Tag `v1.1.0` pushed to origin (`e53defc`). Source of truth for scope: `PROJECT.md` "Post-v1.0 Scope — v1.1" section. Issues #13, #14, #23 are explicitly v1.2 and out of scope here.

**Goal.** Ship v1.1.0 with (a) every counter on `Meter "FrigateRelay"` carrying the structured tags an operator needs to pivot a dashboard by camera/subscription/action/validator/component; (b) end-to-end observability docs + a reference Grafana dashboard + a `docker/observability/` stack; (c) `BlueIrisUrlTemplate` reduced to a thin wrapper around `EventTokenTemplate` so a future allowed-token additive (e.g. `{score}`) requires editing exactly one allowlist.

**Dependencies.** Phase 12 (v1.0.0 GA on `main`; release.yml + RELEASING.md tag-cut policy in place).

**Risk.** **Low–Medium**. Composite breakdown:
- **#34 — Low.** Pure refactor with strong existing test coverage (23 `BlueIrisUrlTemplateTests`). Failure modes are caught at compile or test time.
- **#35 — Low–Medium.** Touches every counter increment site in `DispatcherDiagnostics.cs` (10 counters). Mistakes are observable (missing tags show up immediately in any consumer) and recoverable (additive change; fix-forward in a patch). The CHANGELOG-additive semver-minor classification is correct because metric *names* persist — only series cardinality grows.
- **#36 — Low.** Docs + reference compose stack, no production-path changes. Worst case: dashboard imports cleanly but a panel pivots wrong, fixed in a follow-up patch.

**Estimate.** 4–6 hours of active work + the operator's manual `make verify-observability` smoke time (one-shot `docker compose up` + scrape + dashboard import + tear down).

**PR sequencing (decided).** Three sequential PRs against `main`:
1. **#35 first.** Tags must land before #36's dashboard panels can pivot by them; otherwise the v1.1 dashboard would only work at system-aggregate and need an immediate follow-up.
2. **#36 second.** Depends on #35's tags. Bundles `docs/observability.md`, `docs/grafana/frigaterelay-dashboard.json`, `docker/observability/`, the `Makefile` target, the `RELEASING.md` line, and the README link in one diff so reviewers see the full observability story together.
3. **#34 in any slot.** Independent of the other two — pure refactor, zero behavior change. Can land before, between, or after the observability pair.

**Deliverables.**

- **#35 — Tag every counter (`src/FrigateRelay.Host/Dispatch/DispatcherDiagnostics.cs`).** Add structured tags to all 10 counters per the issue inventory: `subscription`, `camera`, `label`, `action`, `validator`, `reason`, `component`. **Hard rule: no `event_id` tag** (cardinality-bomb forbidden; CI grep gate enforces). Per-counter XML doc-comment lists the tag set + cardinality rule so a future contributor adding a counter cannot ship without filling in the tag-set field. New `MeterListener`-based unit tests in `tests/FrigateRelay.Host.Tests/` assert tag presence — one test per counter is sufficient. CHANGELOG entry classifies as additive (semver minor): aggregate Prometheus queries that don't filter on tags continue to return the same totals.
- **#36 — Observability docs + reference stack.** Depends on #35 landing first. Ships:
  - `docs/observability.md` — counter inventory matching `DispatcherDiagnostics.cs` exactly (drift would fail the doc-vs-code parity test if added; at minimum, the comment in `DispatcherDiagnostics.cs` reads "if you add a counter here, update `docs/observability.md`"), OTLP export config, end-to-end docker-compose recipes, cardinality rules for plugin authors.
  - `docs/grafana/frigaterelay-dashboard.json` — panels covering: events received/matched per camera, actions succeeded/failed per camera+action, validator rejection rate per validator, dispatch drops + exhaustion.
  - `docker/observability/` directory with two compose stacks: (a) FrigateRelay → OTel Collector → Prometheus + Grafana, (b) FrigateRelay → Seq for logs.
  - `make verify-observability` Makefile target — hosts the operator's manual smoke-test ritual: `docker compose up`, scrape FrigateRelay, assert at least one tagged counter sample lands in Prometheus, assert the dashboard imports cleanly into vanilla Grafana, tear down.
  - One line in `RELEASING.md` invoking `make verify-observability` as a pre-tag-push step.
  - README.md gains a new "Observability" section linking to `docs/observability.md`.
- **#34 — Collapse the BlueIris template allowlist (`src/FrigateRelay.Plugins.BlueIris/BlueIrisUrlTemplate.cs` + `src/FrigateRelay.Abstractions/EventTokenTemplate.cs`).** Reduce `BlueIrisUrlTemplate` to a thin wrapper that delegates to `EventTokenTemplate`. Single source of truth for `AllowedTokens`. Add a test that asserts `BlueIrisUrlTemplate.AllowedTokens.SetEquals(EventTokenTemplate.AllowedTokens)` so a future divergence fails CI loudly. All 23 existing `BlueIrisUrlTemplateTests` must pass unchanged — or with only the unknown-placeholder error-message contract loosened to match `EventTokenTemplate`'s caller-name pattern (test assertions updated minimally; no behavioral regression).

**Success criteria (verifiable).**
- `dotnet build FrigateRelay.sln -c Release` zero warnings on both Linux and Windows (warnings-as-errors invariant unchanged).
- All existing tests pass; new `MeterListener` tests assert tag inventory per counter (one per counter); new `BlueIrisUrlTemplate.AllowedTokens.SetEquals(EventTokenTemplate.AllowedTokens)` test passes.
- `git grep '"event_id"' src/FrigateRelay.Host/Dispatch/` returns empty (cardinality-bomb tripwire).
- `git grep -nE 'App\.Metrics|OpenTracing|Jaeger\.' src/` still empty (architectural invariant unchanged).
- `make verify-observability` succeeds locally before tag push: reference stack scrapes a tagged counter sample into Prometheus and imports the dashboard cleanly into vanilla Grafana, then tears down without leaking containers.
- `RELEASING.md` updated with the new pre-release smoke step.
- A new operator can go from zero to a working Grafana dashboard in under fifteen minutes following `docs/observability.md` + the `docker/observability/` stack.
- Three merged PRs on `main` (one per issue), then `v1.1.0` tag — operator-cut per the CONTEXT-12 D7 manual tag-cut policy from v1.0; release.yml auto-builds + pushes multi-arch GHCR images on the tag push.

**Risk reductions.**
- **Closes the v1.0.2→v1.0.3 P0 root cause.** The `{camera_shortname}` allowlist drift was a duplicated `AllowedTokens` set across `BlueIrisUrlTemplate` and `EventTokenTemplate`. After #34, adding a future token (e.g. `{score}`) requires editing `EventTokenTemplate.AllowedTokens` only — the BlueIris-side allowlist no longer exists as a parallel surface that can drift.
- **Eliminates aggregate-only-counter blindness.** Pre-#35, dashboards can only show system totals; a single misbehaving camera or a single failing action is invisible to the operator without code-level log inspection. After #35, every counter is pivotable by camera, subscription, action, validator, and component — the v1.1 dashboard makes per-camera action-failure rates visible at a glance.
- **Forces the per-counter discipline forward.** The XML doc-comment template + the "if you add a counter here, update `docs/observability.md`" comment means a future contributor adding a new counter must choose a tag set deliberately, not by omission — the docs and the code stay in lockstep by construction.
- **No production-path code change for #36.** The reference stack and docs land without touching any dispatch, plugin, or host runtime code, isolating risk to the observability surface only.

---

## Phase 14 — v1.2 Inference Engines + Parallel Validation  *[COMPLETE 2026-05-07]*

**Status.** Complete. PR #42 (Roboflow), PR #43 (DOODS2), PR #44 (ParallelValidators + StopAsync hardening) merged to `main`; `v1.2.0` tag cut 2026-05-07; `release.yml` smoke + push-multiarch GHCR publish completed. 291/291 tests passing across the suite (49 net-new tests for the phase: 242 → 291). Operator deployment confirmed live with the Driveway profile running CPAI + DOODS2(pytorch) ParallelValidators. Phase 14 is the implementation phase for the v1.2 scope captured in `PROJECT.md` "Post-v1.1 Scope — v1.2 (more inference engines + parallel-AND validation)" (PROJECT.md:200–261). Three sequential PRs cover issues #13 (Roboflow Inference validator), #14 (DOODS2 validator with HTTP + gRPC transports — gRPC reverted to HTTP-only mid-phase), and #23 (per-action `ParallelValidators: true` opt-in). Decision rationale and the in-/out-of-scope boundary live in PROJECT.md; this section captures only the verifiable execution shape.

**Goal.** Ship v1.2.0 with (a) two new self-hosted `IValidationPlugin` implementations — Roboflow Inference (`FrigateRelay.Plugins.Roboflow`) and DOODS2 (`FrigateRelay.Plugins.Doods2`, HTTP only — gRPC scope reverted post-PLAN-2.1 because DOODS2 v2 is HTTP-only upstream; CONTEXT-14 D4 amended); (b) a per-`ActionEntry` `ParallelValidators: true` opt-in that runs the action's validators concurrently under a strict-AND aggregation, with default-false preserving today's sequential behavior; (c) at least one integration test that exercises ≥ 2 validators in parallel under a single `ActionEntry` so the multi-engine story is operationally proven, not hypothetical.

**Dependencies.** Phase 13 (v1.1.0 GA on `main`; tagged counters and observability stack in place so any new counters introduced by #23 inherit the v1.1 tag matrix). The `[Unreleased]` ID-29 hotfix (eviction-callback log staleness) will likely roll out as v1.1.1 before or alongside Phase 14 — Phase 14 is **not** gated on it; the hotfix and v1.2 scope are independent.

**Risk.** **Low–Medium**. Composite breakdown:
- **#13 — Low.** HTTP-only validator that mirrors the established CPAI pattern (typed `HttpClient` per `IPluginRegistrar`, WireMock unit tests). No new dep families. Smaller surface than #14.
- **#14 — Low** (was Medium pre-reversal). HTTP-only validator mirroring CPAI/Roboflow patterns. The original gRPC scope (vendored proto + Grpc.Tools + new dep family) was reverted during PLAN-2.1 build because DOODS2 v2 dropped gRPC upstream — see CONTEXT-14 D4 reversal note and PLAN-2.3.md.
- **#23 — Low–Medium.** Touches the validator execution loop in the host (the per-action chain that today runs sequentially). Feature-flagged with `ParallelValidators: false` as the default, so existing configs are unaffected by a regression here. Failure surface is bounded to actions that opt in.

**Estimate.** 6–9 hours of active work across the three PRs. #14 dominates (proto vendoring, gRPC client wiring, transport-selection plumbing); #13 and #23 are smaller.

**PR sequencing (decided).** Three sequential PRs against `main`, in the order #13 → #14 → #23 (PROJECT.md:237–245):
1. **#13 first.** Smaller surface (HTTP-only); establishes the second-validator pattern that #14 reuses for its HTTP transport.
2. **#14 second.** Builds on #13's pattern; HTTP-only after the PLAN-2.1 reversal (see CONTEXT-14 D4 reversal note + PLAN-2.3.md).
3. **#23 last.** Lands after both #13 and #14 are merged so its integration test can exercise three validator types (CPAI + Roboflow + DOODS2) in a single AND chain — proves the parallel design holds beyond a CPAI-only toy case.

CHANGELOG classifies all three as additive (semver minor). #23's `ParallelValidators` defaults to `false`, so existing `ActionEntry` configs are unaffected on upgrade.

**Deliverables.**

- **#13 — Roboflow Inference validator.** New `src/FrigateRelay.Plugins.Roboflow/` project: `RoboflowValidator : IValidationPlugin`, `RoboflowOptions` (`BaseUrl`, `ModelId`, `MinConfidence`, `AllowedLabels`, `OnError`, `Timeout`), `Roboflow.PluginRegistrar` registering a typed `HttpClient`. Self-hosted Roboflow Inference only — no Roboflow Hosted Cloud API in v1.2 (PROJECT.md:213–214). Per-instance `ModelId` so operators declare multiple validator instances if they need different models per camera. New `tests/FrigateRelay.Plugins.Roboflow.Tests/` with WireMock-driven coverage of allow/reject/timeout/OnError-FailClosed/OnError-FailOpen/cancellation. Optional Testcontainers integration test if `roboflow/inference` exists on a public registry with acceptable boot time; otherwise WireMock-only with a documented manual-smoke recipe.
- **#14 — DOODS2 validator (HTTP only).** New `src/FrigateRelay.Plugins.Doods2/` project: `Doods2Validator : IValidationPlugin`, `Doods2Options` (`BaseUrl`, `DetectorName`, `MinConfidence`, `AllowedLabels`, `OnError`, `Timeout`, `AllowInvalidCertificates`), `Doods2.PluginRegistrar`. `POST /detect` with base64-encoded image + JSON detections back. DOODS2 returns confidence in 0-100 scale; validator normalizes to 0-1 internally. WireMock-driven unit tests. **gRPC scope reverted 2026-05-06** during PLAN-2.1 build — DOODS2 v2 (Python rewrite) is HTTP-only upstream per the project README ("DOODS2 drops support for gRPC as I doubt very much anyone used it anyways"). PLAN-2.3 was REMOVED accordingly. See CONTEXT-14 D4 reversal note for full rationale.
- **#23 — Per-action `ParallelValidators: true`.** New boolean field on `ActionEntry` (default `false`). When `true`, the host's per-action validator chain runs concurrently via `Task.WhenAll`; each validator's own `Timeout` applies; aggregate fails closed if any validator times out (matches existing per-validator `OnError: FailClosed` semantics — "parallel" changes scheduling, not failure semantics). Aggregation: strict AND. First-validator-rejects does **not** short-circuit other in-flight validators — operators get full per-validator visibility on every dispatch (PROJECT.md:228). Each rejecting validator still emits its own `validators.rejected` counter (no behavioral change to the v1.1 counter tag matrix). Affected files: `ActionEntry` (host-internal, declared in `src/FrigateRelay.Host/Configuration/ActionEntry.cs` per RESEARCH §2.5 — NOT in `FrigateRelay.Abstractions`), the validator-execution path in `src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs:200-246`, plus a new integration test exercising ≥ 2 validators in parallel under a single `ActionEntry`.

**Verification (gates reproduced from PROJECT.md:248–256).**
- `dotnet build FrigateRelay.sln -c Release` zero warnings on both Linux and Windows (warnings-as-errors invariant unchanged). New plugin projects compile clean.
- **No gRPC anywhere** (D4 reversed 2026-05-06). `git grep -nE 'Grpc\.' src/` returns empty across the entire repo; no csproj declares Grpc.* packages.
- All existing tests pass. **Test-count gate:** 242 baseline (post-Phase 13) → 242+N expected; architect to determine N during Phase 14 PLAN dispatch (per-PR new-test minimums will be set at PR-1 / PR-2 / PR-3 planning briefs).
- New unit tests per validator, at minimum: allow / reject / timeout / OnError-FailClosed / OnError-FailOpen / cancellation, all WireMock-driven (DOODS2 gRPC scope reverted; no in-process gRPC server harness needed).
- New integration test demonstrating ≥ 2 validators running in parallel under a single `ActionEntry` with `ParallelValidators: true`. WireMock or Testcontainers as available; the CPAI + Roboflow combination is the smallest meaningful coverage, the CPAI + Roboflow + DOODS2 trio is the target if PR-3's test infrastructure allows it.
- `git grep -nE 'App\.Metrics|OpenTracing|Jaeger\.' src/` still empty (architectural invariant unchanged).
- Three merged PRs on `main` (one per issue), then `v1.2.0` tag — operator-cut per the CONTEXT-12 D7 manual tag-cut policy; release.yml auto-builds + pushes multi-arch GHCR images on the tag push.

**Success criteria (reproduced from PROJECT.md:258–261).**
- An operator can declare `Validators: ["cpai", "roboflow", "doods2"]` with `ParallelValidators: true` on a single action and see all three validators contribute decisions in production logs and counters.
- Adding a hypothetical fourth validator follows the #13/#14 pattern: a new plugin project + `IPluginRegistrar` registration, no host changes required.
- Existing v1.0/v1.1 deployments upgrade to v1.2 with no config changes — sequential validation remains the default. Operators opting into parallel mode flip exactly one boolean per action.

### Phase 14 open questions (surface, do not decide silently)

The v1.2 scope in `PROJECT.md` is closed; these are clarifications a planner will need at the start of Phase 14 PR dispatch but that the roadmap does not pre-decide:

- **OQ-1 — DOODS2 `.proto` sourcing.** Vendor a copy of the upstream DOODS2 `.proto` at a pinned commit, or pull it via `git submodule`? Implications: vendoring is simpler for license attribution (single LICENSE-attribution line in the plugin's project notes) and keeps Dependabot's reach scoped to NuGet only; submodule requires a `.gitmodules` entry, recursive clone for contributors, and adds a second update path. Lean: vendor at a pinned commit with the upstream commit hash + license noted at the top of the `.proto`. Confirm at PR-2 planning.
- **OQ-2 — Roboflow Inference Testcontainers image.** Does `roboflow/inference` exist on a public registry, and is its boot time acceptable for CI (target: under the existing 30s integration-test SLO from Phase 4)? If yes, ship a Testcontainers-driven integration test alongside #13's WireMock unit suite; if no, fall back to WireMock-only with a documented manual-smoke recipe in the PR description. Decide at PR-1 planning, after a five-minute `docker pull` + boot check.
- **OQ-3 — `ParallelValidators` flag location precedence.** PROJECT.md:224 places the flag on `ActionEntry` only — no per-subscription default that the action could override. Surface here in case PR-3 planning reconsiders adding a per-subscription default for ergonomic reasons (operator with 9 actions all wanting parallel mode would otherwise repeat the boolean 9 times). Default position: `ActionEntry`-only as PROJECT.md states; only revisit if PR-3 planning surfaces concrete config-bloat evidence from the example fixtures.
- **OQ-4 — First-validator-rejects logging behavior in parallel mode.** PROJECT.md:227–228 is explicit: each rejecting validator emits its own `validators.rejected` counter for per-validator dashboard visibility, and there is no aggregate "action_rejected_by_parallel_validators" counter. Surface in case PR-3 planning wants to add an aggregate counter for dashboard ergonomics. Default position: per-validator emission only as PROJECT.md states; if an aggregate counter is added later, it goes through the v1.1 tag-set discipline (no `event_id`; tags include `action`, `subscription`, `camera`, `label`).

---

## Phase 15 — v1.2.1 Hardening Patch  *[COMPLETE 2026-05-07]*

**Status.** Complete on `feature/phase-15-v1.2.1` (10 plan commits + 1 review-followup commit). All 10 issue IDs (#8/#13/#14/#15/#19/#20/#24/#25/#26/#27) closed. 13 net-new tests; Host suite 133 → 146. All architectural invariants green. VERIFICATION.md verdict PASS; AUDIT-15.md NO_CRITICAL_FINDINGS; SIMPLIFICATION-15.md CLEAN; DOCUMENTATION-15.md GAPS_NON_BLOCKING (3 small operator-doc additions for #19/#20/#27 — addressable at ship time or as a Phase 15.x doc patch). Awaiting PR merge to `main` and operator-cut `git tag v1.2.1` per CONTEXT-15 D7. Originally planned scope captured below. Phase 15 is the implementation phase for the v1.2.1 scope captured in `.shipyard/phases/15-and-16-issues-cleanup-design.md` (Phase 15 section). Two sequenced batches cover ten ISSUES.md entries: Batch 1 hardens the structured-logging boundary and the startup-validation collect-all pipeline (#13, #14, #19, #20); Batch 2 closes CI / supply-chain / operator-doc hygiene (#8, #15, #24, #25, #26, #27). Patch release: no public API change, no config-shape change, no SemVer minor bump.

**Goal.** Ship v1.2.1 with (a) every operator-controlled string flowing into a structured log call sanitized for `\n`/`\r` and conformant to a `[A-Za-z0-9_-]+` name allowlist (CWE-117 closed); (b) `OTEL_EXPORTER_OTLP_ENDPOINT` URI scheme restricted to `http`/`https`/`grpc` at startup with a structured diagnostic instead of an `ArgumentException` from the OTLP exporter at runtime (CWE-183 closed); (c) `ActionEntryTypeConverter` rejecting empty/whitespace plugin names with a clear message; (d) the `release.yml` + `ci.yml` 3rd-party `uses: action@vN` references pinned to full commit SHAs (CWE-829 / SLSA L2+); (e) `secret-scan` tripwire fixture extended to cover RFC 1918 ranges beyond `192.168.x.x`; (f) Serilog rolling-file path validation rejecting Windows-style absolute paths on Windows hosts (residual CWE-22 gap from Phase 10 Linux-allowlist hardening); (g) operator-doc warnings on `docker/mosquitto-smoke.conf` and `docker/docker-compose.example.yml` clarifying that the smoke broker is anonymous-only and that the example compose binds publicly by default. The `.github/scripts/run-tests.sh` `--coverage` branch regains full pass-through-arg parity with the fast-mode branch.

**Dependencies.** Phase 14 (v1.2.0 GA on `main`; the Roboflow + DOODS2 plugins are present so `StartupValidation` extensions reaching into validator-name enumeration cover the full v1.2 plugin matrix). Per `.shipyard/phases/15-and-16-issues-cleanup-design.md` "Cross-phase notes" — **Phase 15 must ship before Phase 16**.

**Risk.** **Low**. Composite breakdown:
- **Batch 1 (#13, #14, #19, #20) — Low.** All four changes live within the existing `StartupValidation.cs` D7 collect-all pattern (one shared `List<string> errors`, all passes called from `ValidateAll`). #13 and #19 are additive sanitization passes on already-collected error strings; #14 is a guard added to a single `TypeConverter` entry point; #20 extends `ValidateObservability` with a scheme allowlist. No public-API change, no config-shape change. Failure modes are operator-visible at startup time and recoverable by config edit.
- **Batch 2 (#8, #15, #24, #25, #26, #27) — Low.** #8 is a one-line shell fix. #15 is two regex additions + matching fixture lines (the tripwire self-test enforces fixture coverage by construction). #24 is a mechanical `action@vN` → `action@<full-SHA>  # vN` substitution across 6 actions in 2 workflow files; Dependabot is already configured to maintain SHAs. #25 + #26 are documentation-only edits. #27 extends `ValidateSerilogPath` with a `Path.IsPathRooted` + `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)` guard; the implementation must accept an injected platform predicate so the test stays cross-platform.

**Estimate.** ~3 days end-to-end (plan + build + review + audit + ship): ~120 LOC + 13 new tests + 2 fixture lines + ~6 doc lines. One PR.

**PR sequencing (decided).** Single PR against `main`. Batch 1 and Batch 2 may be planned and built as parallel waves within the PR — no cross-batch dependencies — but they ship in one tag (`v1.2.1`).

**Deliverables.**

### Batch 1 — Validation & log hardening

- **#13 — Newline sanitization for operator-controlled values in `StartupValidation.cs` (CWE-117).** Add a small private helper that strips/escapes `\n` and `\r` from operator-controlled values (subscription / profile / plugin / validator names) before they are interpolated into `errors.Add(...)` strings. Apply at every existing `errors.Add` site that currently interpolates one of those names. Defensive against future contributors — the helper exists once, every call site uses it.
- **#14 — Empty/whitespace plugin-name guard in `src/FrigateRelay.Host/Configuration/ActionEntryTypeConverter.cs`.** `ConvertFrom` currently coerces an empty or whitespace-only string into a `new ActionEntry("")` that downstream `StartupValidation.ValidateActions` flags as "unknown plugin". Add a `string.IsNullOrWhiteSpace` guard at the converter entry point that throws a clear `FormatException` (or equivalent typed exception) with a message naming the offending subscription index, so the operator gets a diagnostic at the converter boundary instead of one layer down. The matching JSON path (`ActionEntryJsonConverter`) already rejects empty strings — this brings the `[TypeConverter]`/`IConfiguration.Bind` path into parity.
- **#19 — Name-allowlist enforcement (`StartupValidation.cs`, new `ValidateNames` pass).** Enforce `^[A-Za-z0-9_-]+$` for subscription, plugin, and validator names. Bundles with #13 because both target the structured-logging boundary; the name allowlist is the *structural* fix (illegal characters never reach a log/span tag) and the sanitization helper is the *defensive* fix (operator-controlled strings flowing through future code paths cannot inject newlines). New pass appends to the shared `errors` list per the D7 collect-all pattern; called from `ValidateAll`.
- **#20 — OTLP endpoint scheme restriction in `StartupValidation.ValidateObservability` (CWE-183).** After the existing `Uri.TryCreate` parse, assert the scheme is one of `http` / `https` / `grpc`. Anything else (e.g. `file:`, `ftp:`) appends a structured diagnostic to `errors`. Operator gets a one-shot list-of-problems failure at startup instead of a runtime `ArgumentException` from the OpenTelemetry OTLP exporter.

### Batch 2 — CI & operator hygiene

- **#8 — `--coverage` branch arg parity in `.github/scripts/run-tests.sh:70`.** Append `"${PASS_THROUGH_ARGS[@]}"` to the `--coverage` branch's `dotnet run` invocation. One-line fix; restores arg parity between fast and coverage modes so a Jenkins coverage run can accept the same `--filter "<class>"` passthrough args that PR-fast mode already accepts.
- **#15 — RFC 1918 fixture coverage in `.github/scripts/secret-scan.sh` + `.github/secret-scan-fixture.txt`.** Add 2 new patterns to the secret-scan grep (`10\.[0-9]+\.[0-9]+\.[0-9]+` and `172\.(1[6-9]|2[0-9]|3[0-1])\.[0-9]+\.[0-9]+`) and matching fixture lines. The existing `tripwire-self-test` job in `.github/workflows/secret-scan.yml` enforces fixture coverage — if the fixture is missing a pattern that the regex set claims to detect, the tripwire fails and prevents the PR from merging. This is the existing self-test contract; #15 just exercises it.
- **#24 — Pin 3rd-party GitHub Actions to full commit SHAs (CWE-829, SLSA L2+).** Replace `uses: action@vN` → `uses: action@<full-SHA>  # vN` for the 6 3rd-party action references across `.github/workflows/release.yml` and `.github/workflows/ci.yml`: `actions/checkout`, `docker/setup-qemu-action`, `docker/setup-buildx-action`, `docker/login-action`, `docker/metadata-action`, `docker/build-push-action`. Dependabot's `github-actions` ecosystem (already configured weekly) maintains SHA bumps going forward — this is a one-time bootstrap.
- **#25 — Anonymous-broker WARNING header in `docker/mosquitto-smoke.conf`.** Add a prominent multi-line `# WARNING` header at the top of the file documenting that the smoke broker is anonymous-only and not suitable for any non-CI use. Documentation-only change; no behavioral impact on the `release.yml` smoke job.
- **#26 — Localhost-binding recommendation in `docker/docker-compose.example.yml`.** Add a comment recommending `127.0.0.1:8080:8080` binding for untrusted networks. Documentation-only change; the example default remains the `0.0.0.0` binding it already publishes.
- **#27 — Windows-path rejection in `StartupValidation.ValidateSerilogPath` (residual CWE-22).** After Phase 10's Linux-allowlist hardening, a Serilog rolling-file path like `C:\Windows\System32\evil.log` still passes on a Windows host because the existing guard is Linux-shaped. Add a `Path.IsPathRooted` + `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)` check that rejects Windows-style absolute paths when the host is Windows. The implementation must accept an injected `OSPlatform`-style predicate (or equivalent seam) so the test asserting Windows-rejection runs cross-platform — the unit test must not require a Windows test agent.

**Success criteria (verifiable).**
- `dotnet build FrigateRelay.sln -c Release` zero warnings on both Linux and Windows (warnings-as-errors invariant unchanged).
- All existing tests pass; **13 new tests** added — `StartupValidationTests`: 2 newline-sanitization (closes ID-13), 4 name-allowlist (closes ID-19), 3 OTLP-scheme (closes ID-20); `ActionEntryTypeConverterTests`: 2 empty/whitespace rejection (closes ID-14); `SerilogPathValidationTests`: 2 Windows-path rejection cross-platform (closes ID-27). Test-count gate: post-Phase 14 baseline + 13. Architect to confirm the post-Phase 14 baseline at PLAN-15.x dispatch.
- `git grep ServicePointManager src/` returns empty (architectural invariant unchanged).
- `git grep -nE '\.(Result|Wait)\(' src/` returns empty (architectural invariant unchanged).
- `git grep -nE 'App\.Metrics|OpenTracing|Jaeger\.' src/` returns empty (architectural invariant unchanged).
- `secret-scan.yml` `tripwire-self-test` job passes after the #15 fixture additions — confirms the new RFC 1918 patterns are enforced by the existing self-test contract.
- `release.yml` runs through the `smoke` + `push-multiarch` jobs successfully on a `v1.2.1-rc.0` prerelease tag — the SHA-pinned action references resolve, the Mosquitto + `/healthz` smoke gate passes, and multi-arch images push to GHCR (closes ID-24 in production).
- `git grep -nE 'uses:\s*[^@\s]+@v[0-9]' .github/workflows/` returns empty across `release.yml` + `ci.yml` (closes ID-24 structurally — every 3rd-party action is SHA-pinned).
- `StartupValidation` rejects malformed env-var-only `OTEL_EXPORTER_OTLP_ENDPOINT` schemes (e.g. `file:///tmp/x`) with a structured diagnostic at startup instead of an `ArgumentException` from the OTLP exporter — verified by a new `StartupValidationTests` case.
- `CHANGELOG.md` `[1.2.1]` section lists each closed ID with a 1-line summary; `[Unreleased]` empty after the release commit.
- One merged PR on `main`, then `v1.2.1` tag — operator-cut per the CONTEXT-12 D7 manual tag-cut policy; `release.yml` auto-builds + pushes multi-arch GHCR images on the tag push.

**Risk reductions.**
- **Closes the structured-logging injection surface end-to-end.** Pre-#13/#19, an operator-controlled subscription / profile / plugin name containing a literal `\n` could split a single Serilog/OTel log line into two — corrupting downstream log-aggregation and dashboard pivots. After Phase 15 the structural fix (#19 name allowlist) prevents illegal characters reaching the boundary at all, and the defensive fix (#13 sanitization) ensures any future code path that interpolates an operator string into a log/span tag is safe by default.
- **Eliminates the runtime-`ArgumentException` failure mode for OTLP scheme typos.** Pre-#20, a typo like `OTEL_EXPORTER_OTLP_ENDPOINT=file:///tmp/x` produced an `ArgumentException` from deep inside the OTLP exporter at first metric/span flush, with a stack trace that did not name the env var. Post-#20, the operator sees a one-line structured diagnostic at startup naming the offending value.
- **Brings the `[TypeConverter]` config path into parity with the JSON path.** Pre-#14, an empty `Actions: [""]` entry bound to `IConfiguration` produced a confusing "unknown plugin" error one layer down in `ValidateActions`; the JSON path already rejects empty strings at the converter. Post-#14 the `[TypeConverter]` path matches.
- **Closes the SLSA L2+ supply-chain gap on 3rd-party Actions.** Pre-#24, a compromised tag on any of the 6 3rd-party actions could re-point at malicious code with no merge-time signal. Post-#24 the SHAs are pinned and Dependabot maintains the bumps as PRs that humans review.
- **Extends the secret-scan tripwire to cover RFC 1918 beyond `192.168.x.x`.** Pre-#15 the regex set only caught the `192.168.x.x` shape; an accidentally-committed `10.x.x.x` or `172.16-31.x.x` IP slipped through. Post-#15 the full RFC 1918 range is covered, and the existing `tripwire-self-test` job enforces fixture parity by construction so the regex set cannot rot silently.
- **Closes the residual CWE-22 gap on Windows hosts.** Phase 10's Linux-allowlist hardening did not consider Windows-style absolute paths; #27 closes that gap. The injected-predicate seam keeps the unit test cross-platform — no Windows test agent required.

---

## Phase 16 — v1.3.0 Minor Release  *[NOT STARTED]*

**Status.** Not started. Phase 16 is the implementation phase for the v1.3.0 scope captured in `.shipyard/phases/15-and-16-issues-cleanup-design.md` (Phase 16 section). Single batch covers three ISSUES.md entries: #18 (operator-visible `MetricsTags:KnownCameras` config key — the SemVer-minor justification), #22 (test-fragility cleanup in `tests/FrigateRelay.Host.Tests/Observability/`), and #30 (atomic 3-file `PluginRegistrar` registration-shape unification across CPAI + Roboflow + DOODS2). Lower risk than Phase 15 because #22 + #30 are zero-shipping-impact and #18 is gated behind a default-empty opt-in config key.

**Goal.** Ship v1.3.0 with (a) a new operator-visible `Otel:MetricsTags:KnownCameras: string[]` config key (default `[]`) that, when populated, folds counter `camera` tag values not present in the allowlist into a single `"other"` bucket — additive cardinality control matching the v1.1 tag-matrix discipline, no behavior change for current operators (CWE-400 advisory mitigation per `.shipyard/phases/15-and-16-issues-cleanup-design.md` #18); (b) the four `Task.Delay(100..400)` sites in `tests/FrigateRelay.Host.Tests/Observability/` (`EventPumpSpanTests.cs` and `CounterIncrementTests.cs`) replaced with bounded polling on a deterministic signal — likely a new `WaitForRecordsAsync(int count, TimeSpan timeout)` helper on `CapturingLogger<T>` or an in-memory `MeterListener` measurement-count seam; (c) the `BaseAddress` + `Timeout` configuration in the CPAI, Roboflow, and DOODS2 `PluginRegistrar.cs` files moved from the keyed-singleton factory body into the `AddHttpClient(name, (sp, client) => ...)` builder — atomic 3-file commit, behavior identical, ergonomically future-proof against silent loss-of-configuration if anyone later adds `ConfigureHttpClient` upstream.

**Dependencies.** Phase 15 (v1.2.1 GA on `main`) — per `.shipyard/phases/15-and-16-issues-cleanup-design.md` "Cross-phase notes", Phase 15 must ship before Phase 16.

**Risk.** **Low**. Composite breakdown:
- **#18 — Low.** New optional config key with default `[]`. When the array is empty the counter-tag write path is unchanged (no `"other"` folding, no allowlist lookup). When populated the write path adds one set-membership lookup per increment site — `HashSet<string>` ordinal lookup, microsecond cost. No public-API change. Failure mode: operator misspells a camera name in the allowlist, that camera's counter samples land in `"other"` instead of the camera-specific bucket; recoverable by config edit.
- **#22 — Low.** Test-only refactor; no shipping code changes. Risk surface is bounded to the 4 modified test files. The issue notes the inline-fix attempt failed because `Records` did not exist on `CapturingLogger<T>`; PLAN-16.x must verify the helper field name first and either extend the helper with `WaitForRecordsAsync(int count, TimeSpan timeout)` or expose the in-memory `MeterListener` measurement-count via an analogous polling helper.
- **#30 — Low.** Behavior identical (the `(sp, client) => ...` builder runs at the same DI-resolution point as the existing keyed-singleton factory body). Atomic 3-file commit per CodeRabbit's recommendation — landing CPAI alone would leave a lint-style inconsistency with Roboflow + DOODS2; landing all three at once keeps the registrars uniform. Optional 5×2 backfill tests for CPAI + DOODS2 (Roboflow already has 5 from PR #42 per the design doc) verify `BaseAddress` + `Timeout` flow through `IHttpClientFactory`.

**Estimate.** ~2 days end-to-end (plan + build + review + audit + ship): ~80 LOC shipping code + ~50 LOC test code + README/docs updates. One PR.

**PR sequencing (decided).** Single PR against `main`. The three deliverables are independent and may be built in any order, but ship together in one tag (`v1.3.0`).

**Deliverables.**

### Batch 3 — Style & test fragility

- **#18 — `Otel:MetricsTags:KnownCameras: string[]` operator-visible config key.** New `MetricsTagsOptions` record with a single `KnownCameras` field (default `Array.Empty<string>()`). Bound to the `Otel:MetricsTags` config section. At each counter-increment site that writes a `camera` tag (today: every counter in `DispatcherDiagnostics.cs` that carries the `camera` tag per the Phase 13 v1.1 inventory), the write path consults the bound options: if `KnownCameras` is empty the camera value passes through unchanged (current behavior preserved); if `KnownCameras` is non-empty and contains the camera value the value passes through unchanged; otherwise the tag value is written as `"other"`. **Default empty array → no behavior change for current operators.** Operator opts in by adding e.g. `"Otel": { "MetricsTags": { "KnownCameras": ["Front", "Driveway", "Backyard"] } }` to `appsettings.json`. CWE-400 advisory mitigated only when the operator opts in. Surface choice (`EventPump.cs` vs. `DispatcherDiagnostics.cs` vs. a new `MetricsTagWriter` helper) is a PLAN-16.x decision; the design doc says "EventPump.cs (or wherever counter tags are written)". The SemVer-minor classification rests on this addition: a new operator-visible config key is additive but visible.
- **#22 — Replace `Task.Delay` sites in `tests/FrigateRelay.Host.Tests/Observability/` with deterministic polling.** Audit `tests/FrigateRelay.Host.Tests/Observability/EventPumpSpanTests.cs` and `tests/FrigateRelay.Host.Tests/Observability/CounterIncrementTests.cs` for the 4 `Task.Delay(100..400)` sites called out in the design doc. Replace each with bounded polling on a deterministic signal — likely options: (a) extend `CapturingLogger<T>` (in `tests/FrigateRelay.TestHelpers/`) with `WaitForRecordsAsync(int count, TimeSpan timeout)` that returns when the captured-record count reaches `count` or throws on timeout; (b) expose the in-memory `MeterListener` measurement count via an analogous polling helper. PLAN-16.x must verify the existing helper's field name first — the design doc notes the inline-fix attempt failed because `Records` did not exist. Test-only refactor; no shipping code changes. Same assertions, polling-based timing.
- **#30 — Atomic 3-file `PluginRegistrar` registration-shape unification.** Move the `BaseAddress` + `Timeout` configuration in three plugin registrar files from the keyed-singleton factory body into the `AddHttpClient(name, (sp, client) => ...)` builder lambda. Affected files: `src/FrigateRelay.Plugins.CodeProjectAi/CodeProjectAiPluginRegistrar.cs`, `src/FrigateRelay.Plugins.Roboflow/RoboflowPluginRegistrar.cs`, `src/FrigateRelay.Plugins.Doods2/Doods2PluginRegistrar.cs` (line ranges per design doc: ~75–84 each). Behavior identical; ergonomics + future-proof against silent loss-of-configuration if anyone later adds `ConfigureHttpClient` upstream. Single atomic commit per CodeRabbit recommendation — landing one in isolation creates lint-style drift across the three registrars. Optional: backfill `*PluginRegistrarTests` for CPAI + DOODS2 (5 tests each) verifying `BaseAddress` + `Timeout` flow through `IHttpClientFactory`. Roboflow already has 5 such tests from PR #42 per the design doc — the 10-test backfill brings the trio to parity.

**Success criteria (verifiable).**
- `dotnet build FrigateRelay.sln -c Release` zero warnings on both Linux and Windows (warnings-as-errors invariant unchanged).
- All existing tests pass; 3 new `MetricsCardinalityTests` pass — known-camera passthrough (closes ID-18a), unknown-camera-folded-to-`"other"` (closes ID-18b), empty-allowlist-disabled-passthrough (closes ID-18c). Optional 10 `PluginRegistrar` backfill tests pass (closes ID-30 surface coverage). 4 polling-refactored test sites pass (closes ID-22).
- **Greppable invariant for #22:** `git grep -nE 'Task\.Delay' tests/FrigateRelay.Host.Tests/Observability/` returns **empty** after Phase 16 — zero `Task.Delay` calls remain in the observability test directory. CI gate, not a stylistic preference.
- `git grep -nE 'App\.Metrics|OpenTracing|Jaeger\.' src/` returns empty (architectural invariant unchanged).
- `git grep '"event_id"' src/FrigateRelay.Host/Dispatch/` returns empty (cardinality-bomb tripwire unchanged from Phase 13).
- No regression in `frigaterelay.events.received` / `frigaterelay.events.matched` counter tag shape when `KnownCameras` is empty (default) — verified by an existing v1.1 `MeterListener` tag-presence test continuing to pass.
- Atomic 3-file commit for #30 — `git log --oneline -1 -- src/FrigateRelay.Plugins.CodeProjectAi/CodeProjectAiPluginRegistrar.cs src/FrigateRelay.Plugins.Roboflow/RoboflowPluginRegistrar.cs src/FrigateRelay.Plugins.Doods2/Doods2PluginRegistrar.cs` shows a single commit touching all three registrars.
- `README.md` and the relevant operator-docs page (likely `docs/observability.md`, given Phase 13 introduced the cardinality-rules section there) updated with the `Otel:MetricsTags:KnownCameras` example.
- `CHANGELOG.md` `[1.3.0]` section lists #18 as the operator-visible feature ("MetricsTags:KnownCameras allowlist for cardinality control") in the user-facing area; #22 + #30 in an "Internal" section. `[Unreleased]` empty after the release commit.
- One merged PR on `main`, then `v1.3.0` tag — operator-cut per the CONTEXT-12 D7 manual tag-cut policy; `release.yml` auto-builds + pushes multi-arch GHCR images on the tag push.

**Risk reductions.**
- **Gives operators a knob for the cardinality-DoS surface left open since v1.1.** Phase 13 #35 added the `camera` tag to every relevant counter and PROJECT.md fixed the never-`event_id` rule, but a misbehaving Frigate publisher (or a typo'd camera name flooding the broker) could still inflate the `camera` tag's series cardinality. #18 gives operators the explicit allowlist they need — opt-in, default-off, no behavior change for anyone who doesn't configure it.
- **Removes flake from the observability test surface.** `Task.Delay` sites are the textbook source of CI flake on shared runners under load; the Phase 9 + Phase 13 observability test suite was the last surface in the host tests still using sleep-based timing. After Phase 16 every observability test is deterministic-polling, and the greppable invariant prevents future contributors from re-introducing sleep-based timing without an explicit decision.
- **Brings the v1.2 plugin trio's registrar shape into uniform alignment.** Pre-#30, CPAI / Roboflow / DOODS2 each express `BaseAddress` + `Timeout` differently (some in the keyed-singleton factory body, some in the `AddHttpClient` builder). A future contributor adding a fourth validator would copy whichever they happened to look at first. Post-#30 the three registrars match exactly, and the convention is locked in for future plugins.

### Phase 16 open questions (surface, do not decide silently)

The v1.3 scope is closed; these are clarifications a planner will need at the start of Phase 16 PLAN dispatch but that the roadmap does not pre-decide:

- **OQ-1 — `KnownCameras` allowlist surface location.** The design doc says "EventPump.cs (or wherever counter tags are written)". A pure refactor would put the allowlist lookup at every counter-increment site that writes a `camera` tag — high coverage but distributed. A central `MetricsTagWriter` helper sitting between the increment sites and `Counter<T>.Add` would be a single seam to test but introduces a new internal type. Lean: the central helper, because the v1.1 cardinality discipline is more easily enforced when there is one place to read the rule. Decide at PLAN-16.x.
- **OQ-2 — `CapturingLogger<T>` polling helper signature.** The design doc says "extend the helper with `WaitForRecordsAsync(int count, TimeSpan timeout)` or expose the in-memory `MeterListener` measurement count". The two options are not mutually exclusive — `CapturingLogger<T>` lives in `tests/FrigateRelay.TestHelpers/` already and is the natural home for a logger-records polling helper, while the `MeterListener` count is owned by the test fixture itself. Lean: add `WaitForRecordsAsync` to `CapturingLogger<T>` for the logger-side cases, and add an inline polling helper to whichever fixture owns the `MeterListener`. Decide at PLAN-16.x after auditing each of the 4 sites individually.
- **OQ-3 — `KnownCameras` casing semantics.** Should the allowlist match camera names case-sensitively (`"Front"` ≠ `"front"`) or case-insensitively? Frigate camera names are operator-defined and conventionally PascalCase, but nothing in PROJECT.md pins this. Lean: case-sensitive — matches the `subscription` / `profile` name discipline elsewhere. Decide at PLAN-16.x.

---

## Phase Count

**16 phases.** Phases 1–12 delivered v1.0; Phase 13 delivered v1.1; Phase 14 delivers v1.2 (more inference engines + parallel-AND validation); Phase 15 delivers v1.2.1 (validation + log hardening + CI/operator hygiene patch); Phase 16 delivers v1.3.0 (`MetricsTags:KnownCameras` operator-visible config + test-fragility cleanup + plugin-registrar shape unification). Phases 1–2 parallelizable after the `.sln` exists; Phases 3–16 sequential. Phase 15 must ship before Phase 16.

## Questions Appendix

No speculative technology substitutions or scope changes. One intentionally deferred-decision call-out:

- **Alpine vs Debian-slim base image (Phase 10):** PROJECT.md says "locked in during the plan phase"; this roadmap defers the lock-in to the start of Phase 10 because it depends on empirical image-size and MQTTnet/OTel-gRPC compatibility on musl, which cannot be usefully decided earlier. Current lean: **Alpine**, with a documented fallback to Debian-slim. Flagging here rather than pre-deciding.
- **`/healthz` transport (Phase 10):** Minimal API pulls in ASP.NET Core; a raw TCP listener is heavier to maintain. Lean: minimal API, but this is worth a five-minute decision at the top of Phase 10 before committing.

### Phase 13 open questions (surface, do not decide silently)

The v1.1 scope in `PROJECT.md` is closed; these are clarifications a planner will need at the start of Phase 13 PR-1 (#35) and PR-2 (#36) but that the roadmap does not pre-decide:

- **Tag-vs-counter matrix.** `PROJECT.md` lists the tag inventory (`subscription`, `camera`, `label`, `action`, `validator`, `reason`, `component`) and the rule "never `event_id`", but does not pin which counters carry which tags. The issue (#35) is the source of truth; PR-1's planning brief should resolve the per-counter tag selection before implementation begins.
- **`make verify-observability` host requirements.** The Makefile target encodes the operator's manual ritual; whether it shells out to `docker compose` directly or wraps a small Python/bash helper script is a planning-phase choice. Either is acceptable provided the target is idempotent and tears down cleanly on failure.
- **Dashboard JSON Grafana version target.** The dashboard must "import cleanly into vanilla Grafana", but the specific Grafana version pinned in `docker/observability/` (and therefore the dashboard's `schemaVersion`) is a five-minute decision at the start of PR-2. Lean: latest stable Grafana OSS at the time of PR-2.
- **`#34` error-message contract loosening.** The 23 existing `BlueIrisUrlTemplateTests` may need their unknown-placeholder error-message assertions loosened to match `EventTokenTemplate`'s caller-name pattern. Whether to update the tests vs. preserve the exact prior message via wrapper-side formatting is a small planning call — preference is updating the tests (single source of truth wins) but worth flagging at PR-3 time so the reviewer is not surprised.
