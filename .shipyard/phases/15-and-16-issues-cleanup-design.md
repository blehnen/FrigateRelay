# Phase 15 (v1.2.1) + Phase 16 (v1.3.0) — Issues Cleanup

**Date:** 2026-05-07
**Source:** Brainstorm output from `/shipyard:brainstorm` triaging `.shipyard/ISSUES.md`
**Status:** Scope agreed; awaiting architect to extend `ROADMAP.md` and planner to produce `PLAN-15.x` / `PLAN-16.x`.

## Triage outcome

18 open issues triaged on 2026-05-07. Five culled, thirteen kept. ISSUES.md updated in place.

### Closed during triage (5)

| ID | Disposition | Note |
|----|-------------|------|
| #1  | WONTFIX    | Clarity nit on PLAN-3.1 — frozen historical artifact, no readers. |
| #3  | WONTFIX    | `Directory.Build.props` inheritance is the documented project convention. |
| #7  | WONTFIX    | CONTEXT-4 D3 stale — Phase 5 reused templater without confusion through Phases 5–14. |
| #9  | DONE       | Phase 11 + Phase 12 shipped README, plugin-author guide, operator docs. |
| #17 (second entry) | DUPLICATE | Already fixed at `src/FrigateRelay.Host/StartupValidation.cs:75` — env-var fallback precedence applied. |

### Kept (13)

Bundled into two phases below.

---

## Phase 15 — v1.2.1 hardening patch

**Theme:** Validation + log hardening + CI/operator hygiene. Single-PR, single-tag patch release. No public API change, no config-shape change, no SemVer minor bump.

**Issues:** #13, #14, #19, #20, #8, #15, #24, #25, #26, #27 (10 items).

### Batch 1 — Validation & log hardening (4 items)

Live within the existing `StartupValidation.cs` D7 collect-all pattern.

| # | Surface | Description |
|---|---------|-------------|
| #13 | `StartupValidation.cs` | Newline (`\n`/`\r`) sanitization helper applied to all `errors.Add(...)` interpolations of operator-controlled values (subscription / profile / plugin / validator names). CWE-117 advisory. |
| #14 | `Configuration/ActionEntryTypeConverter.cs` | `string.IsNullOrWhiteSpace` guard — empty/whitespace plugin names now fail with a clear message instead of being silently coerced into an invalid `ActionEntry`. |
| #19 | `StartupValidation.cs` (new pass `ValidateNames`) | Enforce `[A-Za-z0-9_-]+` for subscription / plugin / validator names. CWE-117 OTel/log span tag injection. Bundles with #13 because both target the structured-logging boundary. |
| #20 | `StartupValidation.ValidateObservability` | Add scheme check after `Uri.TryCreate` — allow only `http` / `https` / `grpc`. CWE-183. Operator gets a structured diagnostic instead of an `ArgumentException` from the OTLP exporter at runtime. |

**Tests (~11 new):**
- `StartupValidationTests`: 2 newline sanitization (#13), 4 name-enforcement (#19), 3 scheme-restriction (#20).
- `ActionEntryTypeConverterTests`: 2 empty/whitespace rejection (#14).

### Batch 2 — CI & operator hygiene (6 items)

Repo-hygiene + supply-chain hardening + minor `StartupValidation` extension for #27.

| # | Surface | Description |
|---|---------|-------------|
| #8  | `.github/scripts/run-tests.sh` line 70 | Append `"${PASS_THROUGH_ARGS[@]}"` to `--coverage` branch. 1-line fix; restores arg parity between fast and coverage modes. |
| #15 | `.github/scripts/secret-scan.sh`, `.github/secret-scan-fixture.txt` | Add 2 RFC 1918 patterns (`10.x.x.x`, `172.16-31.x.x`) + matching fixture lines. Tripwire self-test enforces fixture coverage. |
| #24 | `.github/workflows/release.yml`, `.github/workflows/ci.yml` | Replace `uses: action@vN` → `uses: action@<full-SHA>  # vN` for all 3rd-party actions (`actions/checkout`, `docker/setup-qemu-action`, `docker/setup-buildx-action`, `docker/login-action`, `docker/metadata-action`, `docker/build-push-action`). CWE-829, SLSA L2+. Dependabot already configured to maintain SHAs. |
| #25 | `docker/mosquitto-smoke.conf` | Prominent multi-line `# WARNING` header. Documentation-only change, no behavioral impact. |
| #26 | `docker/docker-compose.example.yml` | Comment recommending `127.0.0.1:8080:8080` binding for untrusted networks. Documentation-only. |
| #27 | `src/FrigateRelay.Host/StartupValidation.cs` `ValidateSerilogPath` | Add `Path.IsPathRooted` + `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)` guard. Rejects Windows-style absolute paths (e.g. `C:\Windows\...`) on Windows hosts. Closes the residual CWE-22 gap left after Phase 10's Linux-allowlist hardening. |

**Tests (~2 new):**
- `SerilogPathValidationTests`: Windows-path rejection (#27) — implementation should accept an injected `OSPlatform`-style predicate to keep the test cross-platform.
- `secret-scan` workflow tripwire job already self-tests #15 fixtures.

### Phase 15 success criteria

- [ ] All 13 new tests pass; full suite remains green (currently 291).
- [ ] `git grep ServicePointManager` empty in `src/`.
- [ ] `git grep -nE '\.(Result|Wait)\(' src/` empty.
- [ ] Secret-scan workflow's `tripwire-self-test` job passes after fixture additions.
- [ ] `release.yml` runs through `smoke` + `push-multiarch` jobs successfully on a `v1.2.1-rc.0` tag (smoke gate verifies Mosquitto + `/healthz` polling unchanged).
- [ ] Operator-visible: malformed env-var-only `OTEL_EXPORTER_OTLP_ENDPOINT` is now caught by fail-fast diagnostic (already shipped in v1.2.0; covered by the closed first ID-17 entry — re-verify behavior unchanged after #20 scheme restriction lands).
- [ ] CHANGELOG: `[1.2.1]` section lists each issue ID + 1-line summary; `[Unreleased]` empty.

### Phase 15 estimate

~120 LOC + ~13 tests + 2 fixture lines + ~6 doc lines. One PR. ~3 days end-to-end (plan + build + review + audit + ship).

---

## Phase 16 — v1.3.0 minor release

**Theme:** Code-quality cleanup + new operator-visible config. Two refactors (#18 introduces a config key; #30 unifies registration shape across 3 plugin registrars) plus one test-quality cleanup (#22). Minor SemVer bump justified by #18.

**Issues:** #18, #22, #30 (3 items).

### Batch 3 — Style & test fragility (3 items)

| # | Surface | Description |
|---|---------|-------------|
| #18 | `EventPump.cs` (or wherever counter tags are written), new `MetricsTagsOptions.KnownCameras: string[]`, fallback tag value `"other"` | **Adds operator-facing config key.** Default = empty array → no behavior change for current operators. Cardinality DoS only mitigated when operator opts in. CWE-400 advisory mitigation. New config section: `Otel:MetricsTags:KnownCameras: ["Front", "Driveway", ...]`. |
| #22 | `tests/FrigateRelay.Host.Tests/Observability/EventPumpSpanTests.cs`, `CounterIncrementTests.cs` | Replace 4 `Task.Delay(100..400)` sites with bounded polling on a deterministic signal. Need to verify the `CapturingLogger<T>` field name first (the issue notes the inline-fix attempt failed because `Records` did not exist). Likely solution: extend the helper with `WaitForRecordsAsync(int count, TimeSpan timeout)` or expose the in-memory `MeterListener` measurement count. Test-only refactor — no shipping code changes. |
| #30 | `src/FrigateRelay.Plugins.{CodeProjectAi,Roboflow,Doods2}/PluginRegistrar.cs` ~lines 75–84 | Move `BaseAddress` + `Timeout` configuration from the keyed-singleton factory body into `AddHttpClient(name, (sp, client) => ...)` builder. Behavior identical; ergonomics + future-proof against silent loss-of-configuration if anyone later adds `ConfigureHttpClient` upstream. Atomic 3-file commit per CodeRabbit suggestion. Optional: backfill `*PluginRegistrarTests` for CPAI + DOODS2 (Roboflow already has 5 from PR #42). |

**Tests:**
- New `MetricsCardinalityTests`: 3 (#18) — known-camera passthrough, unknown-camera-folded-to-other, empty-allowlist-disabled.
- Refactored 4 sites in observability tests (#22) — same assertions, polling-based timing.
- Optional 5×2 = 10 backfill tests for CPAI + DOODS2 `PluginRegistrar` (#30) — verify `BaseAddress` + `Timeout` flow through `IHttpClientFactory` correctly.

### Phase 16 success criteria

- [ ] All new + refactored tests pass; full suite green.
- [ ] `Task.Delay` count in `tests/FrigateRelay.Host.Tests/Observability/` is **0** after #22 (greppable invariant).
- [ ] CHANGELOG `[1.3.0]` lists #18 as the operator-visible feature ("MetricsTags:KnownCameras allowlist for cardinality control"); #22 + #30 in "Internal" section.
- [ ] Documentation: README + relevant operator-docs page updated with the `MetricsTags:KnownCameras` example.
- [ ] No regression in `frigaterelay.events.received` / `frigaterelay.events.matched` counter tag shape when `KnownCameras` is empty (default).

### Phase 16 estimate

~80 LOC shipping code + ~50 LOC test code + docs. One PR. ~2 days. Lower risk than Phase 15 because #22 + #30 are zero-shipping-impact and #18 is gated behind an opt-in config key.

---

## Cross-phase notes

### Dependencies & ordering
- **Phase 15 must ship before Phase 16.** Phase 16's #22 test refactor depends on whatever `CapturingLogger<T>` extension is added; if Phase 15 happens to touch the helper (it doesn't currently), keep ordering strict.
- No issue depends on cross-batch work — Batch 1 + Batch 2 in Phase 15 can be planned and built in parallel.

### What we are *not* doing
- No new plugins. v1.2.0 trio (Roboflow + DOODS2 + ParallelValidators) is the feature high-water mark for the 1.x line.
- No public-API breakage. The Abstractions surface stays frozen.
- No durable-queue, hot-reload, AssemblyLoadContext-loader, or web-UI work — these remain v2 candidates per PROJECT.md Non-Goals.
- No issues from Closed Issues are reopened.

### Memory-only items not in scope
Per `~/.claude/projects/.../memory/v-1-0-1-backlog.md`, the only remaining v1.0.1 backlog items are `migrate-conf` ergonomics — not in `.shipyard/ISSUES.md` and not addressed here. They remain memory-only and tracked separately.

---

## Next steps

1. **Architect dispatch** — extend `.shipyard/ROADMAP.md` with Phase 15 and Phase 16 entries, mirroring the deliverables + success criteria above.
2. **Planner** — `/shipyard:plan 15` to produce `PLAN-15.1` (Batch 1) + `PLAN-15.2` (Batch 2). Then `/shipyard:plan 16` for Batch 3.
3. **Builder** — execute each plan via standard TDD + verification + audit + ship cycle.
4. **Tag** — `git tag v1.2.1` after Phase 15 ships; `git tag v1.3.0` after Phase 16 ships.
