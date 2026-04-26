# Shipyard History

## 2026-04-24 — Project initialized

- Phase: 1
- Status: ready
- Message: Project initialized
- Settings: interactive mode, manual git, detailed review, security audit on, simplification on, IaC auto, docs gen on, codebase docs at `.shipyard/codebase`, default model routing, auto context tier.
- Detected as **brownfield** (existing .NET Framework 4.8 source in `Source/`).
- Repository is **not** a git repository at init time — no commit created.

## 2026-04-24 — Codebase mapped (all 4 focus areas)

- 4 mapper agents dispatched in parallel (agent mode).
- Files written to `.shipyard/codebase/`:
  - `STACK.md` (12.6 KB) — languages, runtime, NuGet deps by purpose
  - `INTEGRATIONS.md` (11.3 KB) — MQTT, Blue Iris, Pushover, Jaeger, Seq, (dead) CodeProject.AI; TLS bypass called out
  - `ARCHITECTURE.md` (14.1 KB) — 5-project layering, end-to-end event flow, lifecycle
  - `STRUCTURE.md` (in ARCHITECTURE.md bundle) — on-disk map
  - `CONVENTIONS.md` (14.2 KB) — style patterns including the Serilog wrong-overload bug at `Main.cs:269`
  - `TESTING.md` (5.6 KB) — no tests exist; manual validation only
  - `CONCERNS.md` (16.4 KB) — 24 concerns; top 3: TLS bypass, plaintext Pushover creds, no tests/no git
- Bonus finding: inverted directory guard in `SetupMetrics` at `FrigateMain.cs:118-119`.
- Mapper noted `SharedAssemblyInfo.cs` presence discrepancy vs CLAUDE.md — worth spot-checking.

## 2026-04-24 — Brainstorm complete, PROJECT.md + ROADMAP.md written

- 8 Socratic questions resolved the greenfield design:
  - Plugin loading: **A3** (ship A, design for B / DLL-drop loader).
  - Validator scope: **V3** (per-action).
  - Config shape: **S2** (Profiles + subscriptions).
  - Async pipeline: **P1** (Channel + Polly) with **P3** escape hatch (`IActionDispatcher` seam).
  - Input: **I2** (`IEventSource` abstraction, Frigate-only v1).
  - Snapshots: `ISnapshotProvider` as its own plugin type with per-action override.
  - Observability: M.E.L. + Serilog sinks + OpenTelemetry OTLP (drop OpenTracing + App.Metrics).
  - Test stack: MSTest v3 + MSTest `Assert` + FluentAssertions 6.12.2 (pre-commercial, pinned) + NSubstitute + Testcontainers.NET.
  - License: MIT.
  - Name: **FrigateRelay**.
- Positioning: differentiates from `0x2142/frigate-notify` via first-class BlueIris support (action + snapshot source).
- `PROJECT.md` written with all 9 decision rows locked in a summary table.
- `shipyard:architect` agent dispatched; produced `ROADMAP.md` with 12 phases, dependency-ordered, risk front-loaded, every success criterion verifiable by the author alone.
- Revision round 1 applied: Phase 11 onboarding criterion swapped from external-volunteer timed dry-run to a `scaffold-smoke` CI job; Phase 12 estimate clarified to separate ~6h active work from ~48h passive observation.
- Git strategy is **manual**; repo is not a git repo. No commit created.
- Deferred decisions surfaced (documented in ROADMAP Questions appendix): Alpine vs Debian-slim base image (decided in Phase 10); `/healthz` transport (minimal API vs raw TCP, decided in Phase 10).
- Open workflow question: FrigateRelay greenfield will live in a separate folder (likely `/mnt/f/git/FrigateRelay/`). The physical target location will be decided at the first `/shipyard:plan` step.

## 2026-04-24 — CLAUDE.md written (`/init`)

- Root `CLAUDE.md` created, derived from `.shipyard/PROJECT.md` + `ROADMAP.md`. Captures architecture invariants, planned repo shape, commands, testing stack, and the "deliberately excluded" list (DotNetWorkQueue, App.Metrics, OpenTracing, SharpConfig, Topshelf, Newtonsoft.Json).
- User appended a "Project Instructions" note pinning Context7 MCP for library/API docs lookups.

## 2026-04-24 — Phase 1 planned (`/shipyard:plan 1`)

- Working directory: `/mnt/f/git/FrigateRelay/` (git repo, branch `Initcheckin`). Physical target location resolved.
- Discussion capture: 4 decisions recorded in `.shipyard/phases/1/CONTEXT-1.md`:
  - **D1** — `PluginRegistrationContext` carries `IServiceCollection` + `IConfiguration`.
  - **D2** — Phase 1 uses M.E.L. console provider only (Serilog + OTel deferred to Phase 9).
  - **D3** — `Verdict` uses static factories (`Pass()` / `Pass(score)` / `Fail(reason)`) with non-public ctor.
  - **D4** — `global.json` pins `10.0.203` with `rollForward: latestFeature`.
- Researcher agent produced `.shipyard/phases/1/RESEARCH.md`: .NET 10.0.203 SDK (2026-04-21), MSTest 4.2.1 (2026-04-07), FluentAssertions 6.12.2 (license-safe, works on net10.0), NSubstitute 5.3.0. Flagged 4 open questions for the architect.
- Architect resolved all 4 open questions inline in the plans:
  - MSTest `PackageReference` (not `MSTest.Sdk` project SDK) — Dependabot can update PackageReferences but not `msbuild-sdks`.
  - `TreatWarningsAsErrors` applied globally incl. tests; per-project `<NoWarn>` is the escape valve.
  - `appsettings.Local.json` copied via explicit `<Content CopyToOutputDirectory="PreserveNewest" Condition="Exists(...)">`.
  - `UserSecretsId` hard-coded to a stable GUID (`9a7f6e02-3c8b-4d2e-9b17-afb4c6e03a10`) for contributor consistency.
- 3 plans written across 3 waves (linear chain, 1 plan per wave):
  - **Wave 1 — PLAN-1.1** Repo Tooling and Empty Solution.
  - **Wave 2 — PLAN-2.1** `FrigateRelay.Abstractions` and Contract-Shape Tests.
  - **Wave 3 — PLAN-3.1** `FrigateRelay.Host`, Registrar Loop, and Host Tests.
- Verifier (spec-compliance): **READY** — all Phase 1 ROADMAP deliverables owned by exactly one plan, 4 CONTEXT decisions honored, 13 planned tests exceed the ≥6 gate. Report: `.shipyard/phases/1/VERIFICATION.md`.
- Verifier (feasibility critique): **READY** — no cross-wave forward references, verify commands valid on Linux + Windows, three minor risks flagged (NSubstitute.Analyzers + WAE warnings, `appsettings.Local.json` copy behavior, Windows SIGINT parity) all with documented mitigations. Report: `.shipyard/phases/1/CRITIQUE.md`.
- Zero revision cycles needed.
- [2026-04-24T18:10:36Z] Session ended during build (may need /shipyard:resume)

## 2026-04-24 — Phase 1 built (`/shipyard:build 1`)

- **Waves executed sequentially** — all plans 1 plan per wave (linear chain).
- **Wave 1 (PLAN-1.1)** — builder agent (Sonnet 4.6) delivered 3 atomic commits (`b480f12` → `5de3227`) clean. Reviewer: **PASS**. SDK pin corrected from fabricated `10.0.203` to real `10.0.100` + `rollForward: latestFeature` (user installed `10.0.107` via `apt`; original RESEARCH.md claim was wrong). Caveats captured: `dotnet new sln --format sln` required vs new `.slnx` default; empty-solution NuGet warning expected until Wave 2.
- **Wave 2 (PLAN-2.1)** — builder truncated mid-task-3; orchestrator finished inline. 3 commits. Reviewer: **PASS**. Ripples surfaced: `[SetsRequiredMembers]` required on `PluginRegistrationContext` ctor; `.NET 10 dotnet test` blocked against MTP (use `dotnet run --project` against `OutputType=Exe` test exe); `dotnet list` flag order changed; test-project `[tests/**.cs]` editorconfig suppression for CA1707 + IDE0005.
- **Wave 3 (PLAN-3.1)** — builder truncated after task 1; orchestrator finished task 2 but initially missed `PlaceholderWorkerTests.cs`. Reviewer caught gap → orchestrator gap-fix at `ef68446` added the missing test file (2 tests) + 3 explicit `Microsoft.Extensions.*` PackageReferences. Re-review: **PASS**. Phase 1 total: **17 tests pass**.
- **Step 5 Phase verification** — COMPLETE. All 12 closeout criteria met.
- **Step 5a Security audit** — PASS / Low risk. 0 critical/high/medium, 1 low (`.gitignore` `**/` prefix — applied), 2 info (CI deferred to Phase 2; FluentAssertions 6.12.2 license pin intended).
- **Step 5b Simplification** — 1 medium (bootstrap `LoggerFactory` in `Program.cs` over-ceremony) — **applied**; 2 low (single-use `LoggerMessage.Define`, `CapturingLogger<T>` private-nested) — deferred.
- **Step 5c Documentation** — 3 medium CLAUDE.md gaps — **all applied**: `.NET 10 dotnet test` caveat + new `## Conventions` section capturing `[SetsRequiredMembers]`, `CapturingLogger<T>`, `<InternalsVisibleTo>` MSBuild item, test-name underscore convention.
- **Lessons-learned draft** (captured in SUMMARY files for ship-time):
  - SDK-version research for unreleased feature bands is unreliable — cross-check `builds.dotnet.microsoft.com` release-metadata JSON before pinning.
  - Verification must grep each plan's `files_touched:` frontmatter against `git log --name-only`, not just the ROADMAP test-count gate (the orchestrator shipped Wave 3 initially missing a whole test file because the phase-wide test count was already clear).
  - Builder agents truncate on 30+ tool-use runs; consider a "checkpoint after each task + commit" style to make resumption cheap.
  - WSL `timeout --signal=SIGINT` does not propagate through `dotnet run`; use `pgrep | kill -INT` or publish self-contained.
- Checkpoint tags: `pre-build-phase-1`, `post-build-phase-1`.

## 2026-04-24 — Phase 2 planned (`/shipyard:plan 2`)

- **5 decisions captured** in `CONTEXT-2.md`:
  - D1 — Mirror DotNetWorkQueue's CI topology (GH Actions `ci.yml` + `secret-scan.yml` + `dependabot.yml` for fast PR gating; `Jenkinsfile` for coverage on self-hosted Jenkins). User-confirmed after surveying DNWQ's actual 68-line `ci.yml` / 385-line `Jenkinsfile` split at `F:\Git\DotNetWorkQueue`.
  - D2 — Test invocation is `dotnet run --project tests/<project> -c Release` in GH, same + `-- --coverage --coverage-output-format cobertura` on Jenkins (.NET 10 SDK blocks `dotnet test` against MTP).
  - D3 — Defer graceful-shutdown smoke to Phase 4 Testcontainers integration tests. GH matrix Windows parity for a SIGINT dance isn't worth the complexity.
  - D4 — Secret-scan tripwire has a self-test job against a committed fixture (`.github/secret-scan-fixture.txt`). Ongoing regex-drift detection without manual one-shot poison-branch verification.
  - D5 — Dependabot covers `nuget` + `github-actions` ecosystems (not `docker` — Phase 10).
- **Researcher** produced `RESEARCH.md` with MTP code-coverage CLI flags, `setup-dotnet@v4` + `global-json-file` cookbook, Dependabot v2 schema template, scripted Jenkinsfile skeleton adapted from DNWQ, 7-pattern secret-scan regex set with FP risk assessment, fixture draft, DNWQ-copy/don't-copy list. Flagged 6 open questions for the architect.
- **Architect** resolved all 6 open questions inline and produced 4 plans across 3 waves:
  - Wave 1: `PLAN-1.1` Dependabot; `PLAN-1.2` secret-scan workflow + fixture + self-test. Parallel-safe.
  - Wave 2: `PLAN-2.1` GH Actions `ci.yml` (matrix ubuntu+windows, setup-dotnet@v4, build + `dotnet run` per test project, no coverage).
  - Wave 3: `PLAN-3.1` `Jenkinsfile` (scripted, Docker `sdk:10.0`, MTP cobertura per project, workspace-local NuGet cache, modern Coverage plugin `recordCoverage`).
- **Verifier (spec-compliance)**: **READY** — all ROADMAP Phase 2 deliverables owned, all 5 decisions honored, all plan structural rules met.
- **Verifier (feasibility critique)**: **READY** with one caveat carried forward to PLAN-3.1 builder: MSTest 4.2.1's MTP code-coverage extension writes output XML to `TestResults/coverage/*.cobertura.xml` subdirectories of each test project's assembly output path — NOT the `--coverage-output` path the researcher initially assumed. Jenkinsfile archive glob needs to be `tests/**/TestResults/coverage/**/*.cobertura.xml` or a post-test copy step. Critic actually ran the MTP CLI on this machine to confirm.
- Zero revision cycles needed.
- Checkpoint tag: `post-plan-phase-2`.

## 2026-04-24 — Phase 2 built (`/shipyard:build 2`)

- **Wave 1 (parallel, PLAN-1.1 + PLAN-1.2)** — 5 commits (`3172d9f`, `01c051f`, `fea2d9a`, `dec0494`). PLAN-1.1 (Dependabot): builder PASS clean. PLAN-1.2 (secret-scan): builder committed all 3 tasks; reviewer flagged MINOR — the `\x27` escape in Pattern 4's character class was unsupported in ERE, silently making the regex match `\`, `x`, `2`, `7` instead of a single quote. Fixed in `579126e` using bash's `["'"'"']?` escape idiom; added a second fixture line to exercise both quote branches.
- **Wave 2 (PLAN-2.1 GH CI)** — commit `13e3ea2`. Reviewer PASS. Matrix ubuntu+windows, `dotnet run --project` per test project (NOT `dotnet test`), `shell: bash` on test steps for Windows consistency, no coverage flags (coverage is Jenkins-side per D1).
- **Wave 3 (PLAN-3.1 Jenkinsfile)** — commit `3070ac6`. Reviewer PASS. **Important finding**: the feasibility CRITIQUE had warned `--coverage-output <path>` might not be honored by MSTest 4.2.1's MTP coverage extension on .NET 10 (based on WSL-host observation, files landed in `TestResults/` subdirs). Builder's Docker simulation against `mcr.microsoft.com/dotnet/sdk:10.0` produced 11,609 bytes of cobertura XML at exactly the explicit path — the caveat was WSL-host-specific, not container behavior. Archive glob simplified to `coverage/**/*.cobertura.xml`.
- **Step 5 Verification** — COMPLETE. All 3 ROADMAP success criteria met (via structural proxies pre-merge). Build clean, 17 tests pass, scope discipline honoured (no Dockerfile, no release.yml).
- **Step 5a Auditor** — PASS / Medium risk. 1 MEDIUM (Docker image tag-pinned, not digest — deferred to Phase 10 when Dependabot docker ecosystem lands); 1 LOW (RFC-1918 regex over-broad — deferred until it actually bites a real README example); all INFO items confirm no new secrets, no cache-poisoning vector, no third-party actions, no ReDoS risk.
- **Step 5b Simplifier** — 1 MEDIUM (test-project list duplicated across `ci.yml` + `Jenkinsfile`) deferred to Phase 3 per Rule of Three; 2 LOW (stale Dependabot comment + Jenkinsfile no-op top-level post block) **applied** — ~15 LoC delta, no behavior change.
- **Step 5c Documenter** — 2 MEDIUM (CLAUDE.md missing the "CI-coverage-split" rule and the secret-scan self-test mechanism) **applied**. Rewrote CLAUDE.md's `## CI` section: explains the D1 split, documents the self-test tripwire, notes `--coverage-output` is honored in the SDK container, flags the hard-coded test-project list as a Phase 3 consideration with the Rule of Three reasoning.
- **Lessons-learned drafts** (for `/shipyard:ship`):
  - **Regex authoring in bash single-quoted strings needs care**: `\x27` is valid in PCRE but NOT in ERE. When mixing bash quoting + regex escape, always exercise BOTH branches in the fixture. If a character class doesn't get matched by fixtures, the tripwire can rot silently.
  - **Container vs host environment diverges**: a feasibility caveat observed on the WSL host (MTP coverage writes to `TestResults/` subdir) did not reproduce in `mcr.microsoft.com/dotnet/sdk:10.0`. Always re-verify platform-sensitive behavior in the actual CI container, not on the dev box.
  - **DotNetWorkQueue's CI split is a good template** (GH for PR gate, Jenkins for coverage). The structural pattern survives the .NET 8 → .NET 10 transition; only the test-invocation shape (`dotnet run` not `dotnet test`) needs adapting.
  - **Reviewer agent does not self-persist REVIEW-*.md files**; the orchestrator must write them from the inline report. Consider adjusting the reviewer agent's system prompt in Shipyard config (flagged in Phase 1 history, still unresolved).
- Checkpoint tags: `pre-build-phase-2`, `post-build-phase-2`.

## 2026-04-24 — Phase 3 planned (`/shipyard:plan 3`)

- **Discussion capture** produced CONTEXT-3.md with 5 decisions, **updated mid-planning** after researcher findings:
  - **D1** — Fire ALL matching subscriptions (deviates from legacy first-match-wins; flagged for Phase 12 parity docs).
  - **D2** — Keep `string RawPayload` (no contract change — YAGNI).
  - **D3** — **Revised** from "extract base64 thumbnail" to **no-op returning null**. Researcher found Frigate's `thumbnail` in `frigate/events` is always null per official docs; thumbnails are on separate per-camera MQTT topics. `SnapshotFetcher` on `EventContext` is flagged for simplifier review at Phase 5 (may be removed from contract).
  - **D4** — Defer Testcontainers to Phase 4; Phase 3 is unit tests only.
  - **D5** — **Added** a `false_positive` skip alongside the stationary guard. Small deviation from legacy; flagged for Phase 12 docs.
  - Correction: `ManagedMqttClient` doesn't exist in MQTTnet v5 — use plain `IMqttClient` + custom 5s reconnect loop.
- **Researcher** produced RESEARCH.md (638 lines): MQTTnet v5 cookbook (correct v5 APIs, TLS via `WithTlsOptions`), Frigate payload schema with annotated samples, DTO templates, `Channel<T>` push→pull pattern, .NET 10 keyed-singleton `IMemoryCache` (Option A), EventPump `BackgroundService` recommendation. Flagged 5 open questions (D3 reality check, false-positive filter, DTO record shape, zones aggregation, ManagedMqttClient correction).
- **Architect** produced 4 plans; flagged self as potentially speculative about two new interfaces.
- **Spec verifier**: PASS. **Critique verifier**: CAUTION — two new abstractions `ISubscriptionProvider` + `IEventMatchSink` in `FrigateRelay.Abstractions` judged as YAGNI over-abstraction.
- **User chose revision cycle.** Architect rewrote all 4 plans:
  - `FrigateRelay.Abstractions` receives **zero new types** — the assembly's surface does not widen in Phase 3.
  - `SubscriptionMatcher`, `DedupeCache`, `SubscriptionOptions`, `HostSubscriptionsOptions` moved from plugin to `src/FrigateRelay.Host/Matching/` and `src/FrigateRelay.Host/Configuration/`. Matcher + dedupe are universal across any future `IEventSource` (camera/label/zone are on `EventContext` top-level).
  - `FrigateMqttOptions` becomes transport-only (no `Subscriptions` member).
  - `Subscriptions` config binds from a **top-level** section (matches Phase 8 Profiles+Subscriptions shape).
  - `EventPump` takes 4 DI deps: `IEnumerable<IEventSource>`, `DedupeCache`, `IOptions<HostSubscriptionsOptions>`, `ILogger<EventPump>`. Calls static `SubscriptionMatcher` directly.
- **Post-revision re-verification**: both verdicts READY. Residual concerns are mechanical (MQTTnet v5 method-signature validation at Wave 2 start; startup race documented as mitigated by unbounded channel).
- **Test count plan**: PLAN-1.1=6, PLAN-1.2=9, PLAN-2.1=11, PLAN-3.1=2 → 28 total (≥15 gate, +87% cushion).
- Architect also consolidates CI: extracts `.github/scripts/run-tests.sh` used by both `ci.yml` and `Jenkinsfile` — addresses the Phase 2 advisory about hard-coded test-project lists now that Phase 3 adds a third test project.
- Other architect decisions: single shared `FrigateEventObject` record (DRY per OQ3), union all four zone arrays into `EventContext.Zones` during projection (OQ4), `PlaceholderWorker` removed in favor of `EventPump`.
- Checkpoint tag: `post-plan-phase-3`.

## 2026-04-24 — Phase 3 built (`/shipyard:build 3`)

- **Wave 1 (parallel)** — PLAN-1.1 Frigate DTOs (6 tests) + PLAN-1.2 matcher/dedupe in Host (9 tests). Both reviewed with Important findings applied in-place:
  - PLAN-1.1 R1: `FrigateEvent.Before/After` required-non-nullable → defensive nullable; tests updated to `evt!.After!.X` pattern.
  - PLAN-1.1 R2: `FrigateJsonOptions.Default` sealed via `MakeReadOnly(populateMissingResolver: true)` (plain `MakeReadOnly()` throws on .NET 10 without a TypeInfoResolver).
  - PLAN-1.2 R1: `DedupeCache.TryEnter` TOCTOU race → single-lock serialisation.
- **Wave 2 (PLAN-2.1)** — EventContextProjector (7 tests), FrigateMqttEventSource (5 tests), PluginRegistrar. Plain `IMqttClient` + custom 5s reconnect loop (MQTTnet v5 has no ManagedMqttClient). Channel<EventContext> unbounded bridge. Per-client TLS via `WithTlsOptions`.
- **Wave 3 (PLAN-3.1)** — EventPump `BackgroundService` wiring `IEnumerable<IEventSource>`, DedupeCache, HostSubscriptionsOptions, Program.cs rewrite, PlaceholderWorker removal, `.github/scripts/run-tests.sh` consolidation (2 Host tests).
- **Two real bugs found and fixed during Phase 3 build**:
  - `PluginRegistrarRunner.RunAll` was moved AFTER `builder.Build()` by Phase 1's simplification. Registrars mutate `builder.Services`, which has no effect on an already-built provider. With Phase 1's empty registrar list this was latent; Phase 3's real registrar would have silently dropped plugin registration. **Fixed**: moved back to pre-Build with a minimal bootstrap LoggerFactory; inline comment warns future contributors.
  - `FrigateMqttEventSource.DisposeAsync` threw `ObjectDisposedException` during host shutdown because the linked CTS was cancelled against an already-disposed source token. **Fixed**: `Interlocked.Exchange` idempotency guard + targeted `catch (ObjectDisposedException)` wrappers. Exposed by the graceful-shutdown smoke test, not by any unit test.
- **Builder agent truncation** was pervasive in Phase 3 (Wave 1 ×2, Wave 2 ×1, Wave 3 partially). Orchestrator completed each task inline. Two Important reviewer findings were applied by the orchestrator. All 13 Phase-3 commits are atomic; git history is clean.
- **44 tests pass** (10 Abstractions + 16 Host + 18 Sources.FrigateMqtt). Phase-3 new tests: 29 (≥15 gate; +93% cushion).
- **Graceful shutdown smoke (no broker)**: exit 0; log shows "Application is shutting down..." → "Event pump stopped for source=FrigateMqtt" → "MQTT disconnected".
- **CI shared `run-tests.sh` discharges Phase-2 advisory.** Script auto-discovers test projects; a future test project needs zero workflow edit. Canonical-path fallback copy handles the MTP `--coverage-output` WSL/container divergence.
- **Phase 3 gate results**:
  - Verifier: **COMPLETE** — all ROADMAP criteria met, all D1–D5 honored, zero Abstractions diff.
  - Auditor: **PASS / Low** — 5 advisory notes (MQTT creds later, unbounded channel acceptable, CancellationToken.None usage intentional, NuGet lockfile suggestion, no CVEs in MQTTnet 5.1.0.1559).
  - Simplifier: 3 Medium + 3 Low. User deferred both Med items (DisposeAsync triple catches, HostSubscriptionsOptions wrapper) — the wrapper is intentionally preserved for Phase 8 expansion.
  - Documenter: 3 HIGH + 2 MEDIUM + 1 LOW CLAUDE.md gaps. User deferred all to **Phase 11 OSS polish**. SUMMARY-3.1 captures the facts for ship-time lessons extraction.
- **Lessons-learned drafts** (for `/shipyard:ship`):
  - Phase-1 simplifications can be latent bugs — the `RunAll`-after-`Build()` trap was invisible until Phase 3 added a real registrar. Inline comments about ordering invariants are cheap insurance.
  - MQTT ecosystem moves fast — ROADMAP wrote "`ManagedMqttClient`" assuming MQTTnet v4; v5 removed it. Always cross-check ROADMAP product references at phase start.
  - Environment divergence (WSL vs SDK container): `--coverage-output` is honored in one and not the other. Scripts that paper over this become the right shape.
  - Graceful shutdown paths need real smoke tests — the `ObjectDisposedException` in `DisposeAsync` would never have been caught by unit tests; it took a `pgrep | kill -INT` end-to-end run to surface.
- Checkpoint tags: `pre-build-phase-3`, `post-build-phase-3`.

## 2026-04-25 — Phase 4 planning started (`/shipyard:plan 4`)

- Discussion capture (`CONTEXT-4.md`) recorded **7 decisions** before research dispatch:
  - **D1** — Channel topology: per `IActionPlugin` (not shared, not per-(sub, action)). 2 consumer tasks per channel.
  - **D2** — Subscription→action wiring: new `Actions: ["BlueIris"]` array on `SubscriptionOptions`. Empty = no fire (fail-safe). Unknown name = startup fail-fast (matches PROJECT.md S2).
  - **D3** — BlueIris URL template: `{placeholder}` syntax with fixed allowlist (`{camera}`, `{label}`, `{event_id}`, `{score}`, `{zone}`). Unknown placeholder = startup fail-fast. Values URL-encoded.
  - **D4** — Validators: empty `IReadOnlyList<IValidationPlugin>` parameter on dispatcher NOW (smooths Phase-7 diff; behavior identical to "no validators" in v1).
  - **D5** — Default channel capacity: 256 (configurable per plugin via `BlueIrisOptions.QueueCapacity`). `BoundedChannelFullMode.DropOldest`. `SingleWriter = true` (EventPump is sole producer).
  - **D6** — Drop telemetry: BOTH `frigaterelay.dispatch.drops` Meter counter (tagged `action`) AND `LogWarning` carrying event_id + action + capacity. Roadmap mandates both.
  - **D7** — Polly v8: `AddResilienceHandler` on the named `HttpClient` (Microsoft-blessed pattern, NOT inline `ResiliencePipelineBuilder` in dispatcher). `HttpRetryStrategyOptions.DelayGenerator` returns 3/6/9-second fixed delays. Per-plugin TLS opt-in via `ConfigurePrimaryHttpMessageHandler` + `SocketsHttpHandler.SslOptions.RemoteCertificateValidationCallback`, gated by `AllowInvalidCertificates` flag.
- Phase 4 directory scaffolded (`plans/`, `results/`).
- Next: researcher agent dispatch (M.E.Resilience HttpRetryStrategyOptions surface, Testcontainers Mosquitto, WireMock.Net stub patterns, IHttpClientFactory + per-plugin TLS handler, Channel<T> drop-oldest semantics).

## 2026-04-25 — Phase 4 planned (`/shipyard:plan 4`)

- **Researcher agent truncated twice** at ~35-tool-use cap without writing `RESEARCH.md` (same pattern as Phase 1 builder Wave 2/3 truncations). Orchestrator completed inline using Microsoft Learn MCP + Context7 MCP — same fallback pattern that finished prior phases.
- **Three high-value research findings corrected CONTEXT-4.md drafts:**
  - **`Channel.CreateBounded<T>` has a built-in `itemDropped: Action<T>?` callback** — no `TryWrite` wrapper needed. Drop telemetry is one closure capture instead of a synchronisation-prone polling pattern.
  - **Polly v8 `DelayGenerator.AttemptNumber` is zero-indexed for the first retry** — formula `3 * (AttemptNumber + 1)` produces 3/6/9s exactly. Documented in PLAN-2.1 Task 2 as a regression test.
  - **CI scripts auto-discover via `find tests/*.Tests/*.Tests.csproj`** — adding `tests/FrigateRelay.IntegrationTests/` requires zero `run-tests.sh` / `Jenkinsfile` edits. Only the GH Actions Windows leg needs a `--skip-integration` flag (Testcontainers cannot run Linux containers on `windows-latest`).
- **Architect** (opus) wrote 6 plan files in 18 tool uses (clean, no truncation). Resolved both RESEARCH.md open questions inline:
  - **Q1**: Read `EventContext.cs`, found no `Score` property → dropped `{score}` from D3 allowlist. Final allowlist is `{camera}, {label}, {event_id}, {zone}`. Encoded as regression test in PLAN-1.2 Task 3 (`Parse_WithScorePlaceholder_ThrowsBecauseScoreIsNotInAllowlist`).
  - **Q2**: Confirmed `frigaterelay.dispatch.exhausted` (tagged `action`) for retry-exhaustion telemetry, emitted from the consumer `catch` block in PLAN-2.1 Task 1. Distinct from queue-overflow `frigaterelay.dispatch.drops`.
- **Wave structure (6 plans, 18 tasks):**
  - **Wave 1** (parallel): PLAN-1.1 IActionDispatcher + DispatchItem + ChannelActionDispatcher skeleton; PLAN-1.2 BlueIris csproj + BlueIrisOptions + BlueIrisUrlTemplate.
  - **Wave 2** (parallel): PLAN-2.1 dispatcher consumer body + Polly retries + retry-exhaustion telemetry; PLAN-2.2 BlueIrisActionPlugin + registrar (HttpClient + AddResilienceHandler + per-plugin TLS).
  - **Wave 3** (parallel): PLAN-3.1 SubscriptionOptions.Actions[] + EventPump dispatch wiring + Program.cs registrar + startup fail-fast on unknown action names; PLAN-3.2 IntegrationTests project + MqttToBlueIris_HappyPath (Testcontainers + WireMock) + CI Windows-skip flag + Jenkinsfile doc-comment.
- **Verifier (spec compliance)**: **READY** — all 13 ROADMAP deliverables owned, all 7 D1–D7 decisions honored, all CLAUDE.md invariants enforced via grep verification commands. 2 non-blocking caveats: PLAN-3.1 should gate BlueIris registrar on `Configuration.GetSection("BlueIris").Exists()`; PLAN-3.2's HostBootstrap extraction is a small ordering ripple if 3.1 ships first.
- **Verifier (feasibility critique)**: **READY** — all modify-target files exist, no same-wave forward refs, ≥6 dispatcher tests + 1 integration test gates met. Top mitigated risks: Polly AttemptNumber off-by-one (test-encoded), Testcontainers on Windows runner (`--skip-integration` flag), parallel sln edits (standard git merge).
- Zero revision cycles needed.
- **Cross-cutting decisions captured for builder:**
  - `IActionDispatcher` lives in `src/FrigateRelay.Host/Dispatch/`, NOT `Abstractions` (host-internal seam; plugins consume via DI).
  - `DispatchItem` is `readonly record struct` with EventContext + IActionPlugin + IReadOnlyList<IValidationPlugin> + Activity? (cheap enqueue, no GC pressure).
  - `ChannelActionDispatcher` implements `IHostedService` directly (NOT `BackgroundService`) — channel construction in `StartAsync`, drain in `StopAsync` after `Writer.Complete()`.
  - Per-plugin queue capacity is read host-side from `BlueIris:QueueCapacity`, NOT inside the BlueIris plugin assembly (keeps `FrigateRelay.Plugins.BlueIris` free of any `FrigateRelay.Host` reference).
  - `tests/FrigateRelay.Plugins.BlueIris.Tests/` is a NEW test project (matches per-source-project precedent from Phase 3).
- Next: `/shipyard:build 4`.

- [2026-04-25T17:36:33Z] Phase 4: Building phase 4 (building)
- [2026-04-25T17:44:02Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T17:55:00Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T17:55:31Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T17:59:17Z] Phase 4: Phase 4 wave 1 partial: PLAN-1.2 complete, PLAN-1.1 Tasks 1+2 committed (713065a, ed9e9a9), Task 3 (3 unit tests) + SUMMARY-1.1.md pending. Wave 2+3 unstarted. Restart session to enable agent teams (CLAUDE_CODE_EXPERIMENTAL_AGENT_TEAMS=1) then resume via /shipyard:build 4. (paused)
- [2026-04-25T18:02:21Z] Phase ?: Phase 4 build resumed (team mode): completing wave 1 remainder (PLAN-1.1 task 3) (building)
- [2026-04-25T18:04:59Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T18:08:26Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T18:09:02Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T18:14:04Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T18:19:43Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T18:22:39Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T18:22:46Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T18:24:00Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T19:10:05Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T19:11:20Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T19:14:37Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T19:18:48Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T19:19:57Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T19:44:53Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T19:46:20Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T19:50:53Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T19:54:25Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T19:56:38Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T19:57:19Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T19:59:34Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T19:59:45Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T20:23:28Z] Session ended during build (may need /shipyard:resume)
- [2026-04-25T20:38:32Z] Phase ?: Phase 4 build complete. 71/71 tests passing. 8 gaps in ISSUES.md, 1 resolved (ID-5). (complete_with_gaps)
- [2026-04-25T20:47:30Z] Phase ?: Planning phase 5 (Snapshot Providers): CONTEXT-5 captured, dispatching researcher (planning)

## 2026-04-26 — Phase 5 planned (`/shipyard:plan 5`)

- Resumed mid-planning. STATE.json was stale ("dispatching researcher") but research, 5 plans, and CRITIQUE.md were already written 2026-04-25 — verdict: **CAUTION** (6 items, no critical issues).
- User chose **targeted revision cycle** over full re-plan or accept-and-build.
- **Architect revision** (1 cycle, 11 tool uses, surgical edits to 5 plan files):
  - PLAN-1.1: object-initializer for `SnapshotRequest` (no positional ctor exists); builder note added.
  - PLAN-1.2: `files_touched` now includes 5 test files that compile-break on `Actions: IReadOnlyList<string> → IReadOnlyList<ActionEntry>` migration; `StartupValidation.ValidateActions` iteration update made explicit (`entry.Plugin`); `dependencies: [1.1]` frontmatter + header note locking Wave 1 sequential execution; folded fixture updates into Task 2 (no 4th task).
  - PLAN-2.1: committed to wrapper-record DI strategy (`BlueIrisSnapshotUrlTemplate(BlueIrisUrlTemplate Template)`) over keyed services — concrete registrar code snippet provided; constructor injection updated; verification grep updated.
  - PLAN-2.2: removed `Jenkinsfile` from `files_touched` and Task 3 Step 4 — `run-tests.sh` auto-discovers via `find tests/*.Tests/*.Tests.csproj` glob; `git diff -- Jenkinsfile` empty added as acceptance criterion.
  - PLAN-3.1: `tests/FrigateRelay.Host.Tests/FrigateRelay.Host.Tests.csproj` added to `files_touched` with exact `<ProjectReference>` entries for BlueIris + FrigateSnapshot plugins; registrar invocation pattern clarified (`registrars.Add` + `PluginRegistrarRunner.RunAll`).
- **Verifier re-run**: **READY**. All 6 cautions fixed and verified (CRITIQUE.md overwritten). No new issues introduced by the revisions.
- Final wave/test layout: 3 waves, 5 plans, 29 planned tests (≥10 gate, +190% cushion).
- Planning interruption observation: STATE.json was stale by 2 ROADMAP-pipeline steps (verifier had run but state-write didn't fire). Worth investigating shipyard hook reliability — but not a Phase 5 concern.
- Next: `/shipyard:build 5`.

- [2026-04-26T00:10:00Z] Phase 5: Build started (agent mode, sequential Wave 1 then parallel Wave 2 then Wave 3) (building)
- [2026-04-26T14:10:28Z] Session ended during build (may need /shipyard:resume)
- [2026-04-26T14:47:18Z] Session ended during build (may need /shipyard:resume)
- [2026-04-26T14:47:55Z] Session ended during build (may need /shipyard:resume)
- [2026-04-26T14:48:48Z] Session ended during build (may need /shipyard:resume)

## 2026-04-26 — Phase 5 built (`/shipyard:build 5`)

- **12 commits** across 3 waves: PLAN-1.1 (3 commits, 7 SnapshotResolver tests), PLAN-1.2 (2 commits, 9 ActionEntry/StartupValidationSnapshot tests + 5 fixture migrations), PLAN-2.1 (3 commits, 4 BlueIrisSnapshotProvider tests), PLAN-2.2 (2 commits, 6 FrigateSnapshotProvider tests + new plugin assembly), PLAN-3.1 (2 commits, 3 SnapshotResolutionEndToEnd tests + HostBootstrap wiring + ProjectReferences). Cleanup commit `26e8fc2` resolves the REVIEW-3.1 Critical.
- **100/100 tests pass** (99 unit + 1 integration). Build clean, 0 warnings.
- **Builder truncation pattern continued** — every wave's builder agent truncated at ~30-40 tool uses. Orchestrator finished each plan inline. Per-task atomic commits made resumption cheap. Reviewer agent ALSO doesn't self-persist REVIEW-*.md files (Phase 1 lesson still unresolved); orchestrator transcribed inline reports for REVIEW-1.1/1.2 only — REVIEW-2.1/2.2/3.1 captured in this entry.
- **Critical fix (inline)**: REVIEW-3.1 found `StartupValidation.ValidateSnapshotProviders` was dead code — defined in PLAN-1.2 but never called from `HostBootstrap.ValidateStartup`. Fixed in commit `26e8fc2`.
- **Real Phase-4 → Phase-5 regression discovered**: `IConfiguration.Bind` for `IReadOnlyList<ActionEntry>` does NOT fire `[JsonConverter]`. The plan's promise of back-compat for legacy `appsettings.json` `"Actions": ["BlueIris"]` shape was incorrect. Surfaced when integration test failed with "found 0" trigger fires. Fixture migrated to object form. Tracked as **ID-12**. Operator upgrade implication: existing Phase-4 deployments with string-array shape silently lose action firing.
- **Architectural cascade**: `ActionEntry`, `ActionEntryJsonConverter`, `SnapshotResolverOptions` raised from `internal` to `public` (CS0053 from public `SubscriptionOptions`/`HostSubscriptionsOptions`). Tracked as **ID-10** for the future ID-2 internalization sweep.
- **Phase verification**: COMPLETE_WITH_GAPS. All 7 ROADMAP deliverables met; D1–D4 honored; CLAUDE.md invariant greps clean.
- **Security audit**: PASS (Low). 0 critical, 0 important, 2 advisory (`EventId` not URL-encoded in FrigateSnapshot path — benign for UUIDs; `BaseUrl` no URI format validation — fail-fast violation).
- **Simplifier**: 5 actionable findings, all trivial (4 are reviewer Important items: dead `IOptions<BlueIrisOptions>` injection, hard-coded port 19999, duplicate `EventId(3)`, 62 LoC dead test scaffolding). Recommended as one `chore(phase-5): cleanup` commit before Phase 6.
- **Documenter**: 5 CLAUDE.md gap edits proposed; deferred to Phase 11 docs sprint per Phase-3 user decision.
- **Reviewer verdicts**: 1.1 PASS, 1.2 PASS, 2.1 REQUEST_CHANGES (3 important / 0 critical), 2.2 APPROVE (2 important / 0 critical), 3.1 REQUEST_CHANGES → resolved (1 critical fixed inline).
- **Lessons-learned drafts** (for `/shipyard:ship`):
  - **`[JsonConverter]` ≠ Configuration binding**: `Microsoft.Extensions.Configuration.Binder` does not call System.Text.Json. Plans promising dual-form binding via `JsonConverter` are wrong. Use `TypeConverter`, `IConfigureOptions`, or a custom binder for scalar-or-object polymorphism in `IConfiguration`.
  - **Accessibility cascade is a real planning hazard**: CS0053 forces consumers to track outer-type modifiers. Architect should grep `public.*<NewType>` consumers before locking the new type's accessibility.
  - **Reviewer agent doesn't self-persist** (since Phase 1). Orchestrator must transcribe inline reports — or the REVIEW files just don't land on disk.
  - **Builder truncation is a steady-state cost** (~30-40 tool uses). Per-task atomic commits + clear failure protocols make every truncation cheap to recover from.
- Checkpoint tags: `pre-build-phase-5`, `post-build-phase-5`.

## 2026-04-26 — Phase 5 cleanup pass (`chore(phase-5)`)

- Single commit `5f90d2c` applies all 5 simplifier findings + 2 auditor advisory items + the PLAN-2.1 reviewer log typo. Net **-60 LoC** across 5 files (16 insertions / 76 deletions). 100/100 tests still passing.
- **Source fixes**: dead `IOptions<BlueIrisOptions>` ctor param dropped; `bluiris_->blueiris_` log typo; `EventId` URL-encoded in `FrigateSnapshotProvider` (auditor A1); `ProviderName` literal → `Name` property; distinct `EventId(4)` for `_snapshotFailedMessage`; `[Url]` DataAnnotation on `FrigateSnapshotOptions.BaseUrl` (auditor A2).
- **Test fixes**: hard-coded port 19999 → `TcpListener` ephemeral port; 62 LoC of dead `BuildProvider`/`OptionsMutator`/`ApplyOverrides` scaffolding deleted from FrigateSnapshotProviderTests.
- **Deferred** (out of scope): PLAN-2.1 reviewer Important #3 (`PluginRegistrar` raw `IConfiguration` read — track for ID-2 sweep), PLAN-2.2 reviewer Important #2 (`IncludeBoundingBox` OR-merge — needs a dedicated test, not a fix).

## 2026-04-26 — Phase 6 planned (`/shipyard:plan 6`)

- **Discussion capture (CONTEXT-6.md)**: 4 user decisions (D1–D4) + 6 inherited (D5–D10). Configurable `MessageTemplate` with default; text-only when no snapshot; global `Priority` default; new `MqttToBothActionsTests` class.
- **Researcher** (no truncation, 17 tool uses): produced `RESEARCH.md` (~420 lines) with Pushover API cookbook, `MultipartFormDataContent` recipe, Polly retry semantics for non-idempotent endpoints. Recommended **template extraction** to Abstractions (`EventTokenTemplate`) since Rule of Three is crossed (BlueIris trigger + BlueIris snapshot + Pushover message). Recommended **Option D for snapshot resolver call site**: `SnapshotContext` readonly struct passed via extended `IActionPlugin.ExecuteAsync` signature.
- **Architect** (clean, 7 tool uses): locked **ARCH-D1 = (b)** `Resolve(EventContext, bool urlEncode = true)` — single method with default. **ARCH-D2 = Option D** confirmed. **ARCH-D3** — `Title` defaults to `null` (Pushover renders user's app name). 4 plans / 3 waves / 24 new tests (8 + 5 + 10 + 1 integration).
- **Verifier (compliance)**: **READY**. All 7 ROADMAP deliverables owned. All 10 CONTEXT decisions honored. Test count exceeds gate by 200%.
- **Verifier (critique)**: **READY** with 2 cautions:
  1. **PLAN-1.2 blast radius audit**: FrigateSnapshot and FrigateMqtt test projects need spot-check for direct `IActionPlugin.ExecuteAsync` call sites (verifier confirmed none exist before truncating; cross-checked clean).
  2. **PLAN-3.1 secret-scan tripwire**: `tests/` is NOT excluded in `.github/scripts/secret-scan.sh`. Builder must use short fake credentials (<20 chars) to pass the regex.
  3. **PLAN-2.1/3.1 BaseAddress seam** (architect-flagged): integration test needs `PushoverOptions.BaseAddress` override for WireMock redirect; should land in PLAN-2.1 task 2 if discovered, otherwise PLAN-3.1 inline.
- **Wave shape**:
  - Wave 1 (parallel-safe): PLAN-1.1 `EventTokenTemplate` extraction + BlueIris migration; PLAN-1.2 `SnapshotContext` plumbing through `IActionPlugin.ExecuteAsync` + DispatchItem.
  - Wave 2: PLAN-2.1 — new `FrigateRelay.Plugins.Pushover` project with multipart POST, snapshot attachment, 10 unit tests.
  - Wave 3: PLAN-3.1 — HostBootstrap conditional registrar + `MqttToBothActionsTests` integration test.
- Zero revision cycles needed. Checkpoint tag: `post-plan-phase-6`.
- [2026-04-26T16:14:28Z] Session ended during build (may need /shipyard:resume)
- [2026-04-26T16:15:38Z] Session ended during build (may need /shipyard:resume)
- [2026-04-26T16:15:41Z] Session ended during build (may need /shipyard:resume)
- [2026-04-26T16:15:55Z] Session ended during build (may need /shipyard:resume)

## 2026-04-26 — Phase 6 built (`/shipyard:build 6`)

- **5 commits** across 3 waves: Wave 1 (combined commit `f859251` after 2 builder truncations — `EventTokenTemplate` extraction + `SnapshotContext` struct + `IActionPlugin.ExecuteAsync` signature change + dispatcher plumbing + 13 tests); Wave 2 (`607687b` Pushover scaffold + 4 startup-validation tests, `dff4828` `PushoverActionPlugin` multipart POST + 6 behavioral tests); Wave 3 (`dd160ef` HostBootstrap registrar + QueueCapacity merge, `b2eef39` `MqttToBothActionsTests` integration + Pushover BaseAddress seam).
- **124/124 tests** (122 unit + 2 integration). Build clean, 0 warnings.
- **6 builder truncations** at the steady ~30-40 tool-use boundary. Orchestrator finished each plan inline: 6 stub plugin signature updates (CS0535 cascade), DispatchItem/IActionDispatcher/EventPump plumbing, full `PushoverActionPlugin.ExecuteAsync` implementation, multipart-quoting + WireMock-binary fixture fixes, BaseAddress seam wiring.
- **Critical findings**: 0. Reviewer files weren't written for Wave 2/3 (chronic agent issue from Phase 5).
- **Phase verification**: COMPLETE. All 7 ROADMAP deliverables met. All 10 CONTEXT decisions + 3 ARCH decisions honored.
- **Security audit**: PASS (Low). 0 critical/important. 1 advisory: Polly `AddResilienceHandler` doesn't have an explicit `ShouldHandle` 4xx-skip predicate — relies on `Microsoft.Extensions.Http.Resilience` defaults; recommend adding for documentation value.
- **Simplifier**: 2 High (CapturingLogger<T> 4-copy → shared TestHelpers project; HostBootstrap per-plugin QueueCapacity if-let → loop), 2 Medium (BlueIrisUrlTemplate intentional duplication — needs doc comment; StubResolver Rule of Two), 2 Low (BuildFastRetryProvider dead code; HostBootstrap registrar conditionals approaching Rule of Three at Phase 7).
- **Documenter**: 2 actionable for CLAUDE.md (IActionPlugin.ExecuteAsync 3-param + accept-and-ignore convention; ID-12 object-form Actions warning); 3 deferred to Phase 11.
- **Lessons-learned drafts**:
  - **Interface signature changes cascade through stub/test plugins**. CS0535 errors are mechanical but tool-budget-eating. Future `IActionPlugin` changes should be accompanied by an automated stub-update grep.
  - **`MultipartFormDataContent.name=` is unquoted in .NET 10 default** (was quoted in older versions). Tests asserting `name="token"` form fail; use `name=token`.
  - **WireMock returns null `Body` (string) for binary multipart**. Use `BodyAsBytes` + UTF-8 decode.
  - **Pushover returns HTTP 200 with `{"status":0}` for app-level rejections** (e.g. bad token). Plugin must parse body, not just trust HTTP status.
  - **`HttpClient.BaseAddress` is required** when plugin uses relative URIs. Both production registrar AND test helpers must set it from options.
- Checkpoint tags: `pre-build-phase-6`, `post-build-phase-6`.
