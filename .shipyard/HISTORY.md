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
