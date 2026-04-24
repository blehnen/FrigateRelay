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
