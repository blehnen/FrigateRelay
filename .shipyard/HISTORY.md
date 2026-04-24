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
