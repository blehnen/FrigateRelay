# CONTEXT-14 — User Decisions for Phase 14 Planning

These decisions were captured in the `/shipyard:brainstorm` session that produced the v1.2 scope in `PROJECT.md` (commit `b072b0b`) plus the three OQ resolutions captured at the start of `/shipyard:plan 14`. They are authoritative for the architect — do not silently revisit.

## Scope decisions (from brainstorm)

### D1 — Release shape: 3 sequential PRs, semver minor (v1.2.0)
PR order is **#13 → #14 → #23** against `main`. Each PR fully reviewed and merged before the next opens. Rationale: same rhythm as v1.1; cleaner blame, easier rollback, and #23's integration test gets richer per-PR (CPAI alone → CPAI+Roboflow → CPAI+Roboflow+DOODS2). All three classified as additive.

### D2 — Roboflow scope: self-hosted Inference only
No Roboflow Hosted Cloud API in v1.2. Auth surface and quota error handling deferred. Endpoint shape: `http://roboflow:9001`-style.

### D3 — Roboflow model identification: per-instance `ModelId`
`Validators:<name>:Type: "Roboflow"` with `BaseUrl`, `ModelId` (e.g. `rfdetr-base`), `MinConfidence`, `AllowedLabels`, `OnError`, `Timeout`. Operators declare multiple validator instances if they need different models per camera (e.g. `roboflow_persons`, `roboflow_vehicles`). No per-action model override; matches CPAI's per-instance config pattern.

### D4 — DOODS2 transports: HTTP only ⚠️ REVERSED 2026-05-06
**Original decision:** "Both HTTP and gRPC, operator-selectable" with `Doods2Options.Transport: "Http" | "Grpc"`. User chose both despite the extra surface; rationale: gRPC is "quite quicker than http" on the hot path.

**Reversal:** During PLAN-2.1 build (commit `1963657`), the orchestrator probed the live DOODS2 v2 server at `192.168.0.2:10200` and verified upstream's README:

> "DOODS2 drops support for gRPC as I doubt very much anyone used it anyways."
> — `snowzach/doods2` README

The Python `snowzach/doods2` rewrite is HTTP-only by design. gRPC was a feature of the legacy Go-based `snowzach/doods` server and was deliberately removed in v2. User confirmed the reversal: maintaining a gRPC code path that targets a hypothetical legacy Go server adds dep family, ~300 LOC, and ~5 tests for a feature with zero real-world users.

**Current decision:** HTTP only. `Doods2Options` has no `Transport` property; the validator always uses `POST /detect`. PLAN-2.3 was REMOVED accordingly. PLAN-2.2 absorbs PLAN-2.3's residual scope (CHANGELOG bullet, cancellation test).

Operators on the legacy Go server who want gRPC should use the original `snowzach/doods` gRPC client; this plugin targets v2.

### D5 — Parallel mode opt-in: `ActionEntry.ParallelValidators: bool` (default `false`)
- When `false` (default): existing sequential validator chain unchanged.
- When `true`: validators run concurrently via `Task.WhenAll`; each validator's own `Timeout` applies; aggregate fails closed if any times out (matches existing per-validator `OnError: FailClosed` semantics — "parallel" changes scheduling, not failure semantics).

### D6 — Parallel aggregation: strict AND, no first-reject short-circuit
All validators in the parallel set must `Verdict.Allow` for the action to fire. First reject does **not** cancel other in-flight validators — operators get full per-validator visibility on every dispatch. Documented as a deliberate cost-of-information tradeoff and intentionally simpler than the cancellation-token plumbing the alternative would require.

## OQ resolutions (locked at /plan dispatch)

### OQ-1 — DOODS2 `.proto` sourcing: **vendor at a pinned commit**
Copy the upstream DOODS2 `.proto` into the plugin project (`src/FrigateRelay.Plugins.Doods2/Protos/`). Add the source repo URL + commit SHA + LICENSE attribution at the top of the `.proto` file as a comment. Dependabot's reach stays scoped to NuGet only. Updates are deliberate file-level swaps. Submodule rejected — second update path alongside Dependabot adds repo-shape weight for one file.

### OQ-2 — Roboflow Testcontainers feasibility: deferred to researcher
The architect should NOT decide this. The researcher must run a five-minute `docker pull roboflow/inference` + boot check during research and document findings:
- Image exists on a public registry (yes/no, what tag).
- Boot time is under 30s (current integration-test SLO from Phase 4).
- If both pass: PR-1 ships a Testcontainers integration test; if either fails: PR-1 ships WireMock-only with a documented manual-smoke recipe in the PR description.

### OQ-3 — `ParallelValidators` flag location: **ActionEntry only**
Per-action exclusively. No per-subscription default. Smallest surface; aligns with V3 (per-action validator scope). If an operator with many parallel-mode actions hits config bloat, revisit in v1.3 with concrete evidence — not now.

### OQ-4 — Reject counter in parallel mode: **per-validator emission only**
Each rejecting validator emits its own `validators.rejected` counter — same shape as today's sequential mode. Dashboards already pivot by `validator` tag; no new counter needed. No aggregate `actions.rejected_by_validators` counter — keeps the v1.1 counter inventory drift-test clean.

## Constraints reaffirmed (not negotiable)

- **No gRPC anywhere** (D4 reversed 2026-05-06). `git grep -nE 'Grpc\.' src/` returns empty across the entire repo; no gRPC packages in any csproj.
- **Backward compat.** `ParallelValidators: false` is the default; all existing v1.0/v1.1 `appsettings.json` configs work unchanged on v1.2.
- **Counter inventory.** No new counters added to `DispatcherDiagnostics` for #23. The existing `validators.rejected` already carries `validator`, `subscription`, `camera`, `action` tags.
- **Architectural invariants from CLAUDE.md.** No `App.Metrics`, `OpenTracing`, `Jaeger.*`. No `ServicePointManager`. No `.Result`/`.Wait()`. TLS skipping per-plugin only. No hard-coded IPs/hostnames. Warnings-as-errors. Test names use underscores. `[SetsRequiredMembers]` on ctors with `required init` properties.

## What the architect should produce

Three sequential waves, one PR per wave (mirrors Phase 13's "wave = PR" pattern):

- **Wave 1: PR for #13** — Roboflow plugin scaffold + tests + DI registration.
- **Wave 2: PR for #14** — DOODS2 plugin scaffold + HTTP path + tests + DI registration. (gRPC scope reverted post-PLAN-2.1; see D4 reversal note above.)
- **Wave 3: PR for #23** — `ParallelValidators` field + host validator-execution-loop changes + integration test exercising ≥ 2 validators concurrently.

Each wave should keep individual plans to ≤ 3 tasks each per Shipyard's standard. Phase 13's pattern produced 1–2 plans per wave; Phase 14 likely runs similar (scaffold + tests per validator; field + execution-loop + integration test for #23).
