# Phase 3 Plan Critique — Post-Revision

**Date:** 2026-04-24  
**Type:** Pre-execution plan quality review (post-revision)  
**Reviewer:** Claude Code (Verification)

---

## Verdict: READY

**Summary:** The revised Phase 3 plans successfully remedied the prior feasibility critique by **removing `ISubscriptionProvider` and `IEventMatchSink`**. The four plans are now structurally sound, architecturally clean, and ready for execution. No blocking concerns remain. Two minor items (MQTTnet v5 API verification, startup race mitigation) are documented for the build phase; neither is a blocker.

---

## Changes from Prior Critique

### Original CAUTION → Revised READY

**Prior finding:** PLAN-3.1 introduced two new abstractions (`ISubscriptionProvider`, `IEventMatchSink`) with minimal justification for Phase 3's single-source model. Over-engineering flagged as a **non-blocking but recommended revision**.

**User decision:** Simplify. Drop the abstractions.

**Revised implementation:**
- PLAN-2.1 §47: "`ISubscriptionProvider` / `IEventMatchSink` — these abstractions are dropped entirely (per user decision following the feasibility critique)"
- PLAN-3.1 §37: "dropped. They are not added to `FrigateRelay.Abstractions`. The Abstractions surface in Phase 3 is unchanged."
- All plans include `git grep` verification steps confirming zero references to dropped abstractions.

**New architecture (PLAN-3.1 Context):** EventPump injects the four dependencies it needs directly; `SubscriptionMatcher` is static (no DI); `DedupeCache` is a singleton; Host-owned matching logic is reusable across any future `IEventSource` without abstraction overhead.

**Verdict:** **Simplification is well-motivated and correct.** YAGNI principle applied; forward compatibility deferred to Phase 4+ when real multi-source requirements emerge.

---

## Per-Plan Assessment (Post-Revision)

### PLAN-1.1 — DTOs and Options (Wave 1) ✓

**Status:** Ready

**Key changes:** None — PLAN-1.1 was unaffected by the abstraction removal.

**Strengths:**
- Correct use of `JsonNamingPolicy.SnakeCaseLower` (confirmed in RESEARCH)
- `FrigateMqttOptions` correctly declared **without** `Subscriptions` field
- XML docs requirement strict; test count (6 deserialization tests) is solid
- `InternalsVisibleTo` pattern matches Phase 1 precedent

**No concerns.**

---

### PLAN-1.2 — SubscriptionMatcher, DedupeCache (Wave 1) ✓

**Status:** Ready

**Key changes:** None — PLAN-1.2 was unaffected.

**Strengths:**
- D1 (all matches) correctly enforced via `List<SubscriptionOptions>` collection and `IReadOnlyList` return
- D5 contract explicitly documented: matcher expects pre-filtered events (applied upstream in plugin)
- Dedupe key correctly uses `(Name, Camera, Label)` tuple with case-insensitive join
- Zone matching logic: empty/null = match-any; non-empty = membership test (OrdinalIgnoreCase)
- 9 tests comprehensively cover the logic (6 matcher scenarios + 3 dedupe scenarios)
- Host csproj gains `Microsoft.Extensions.Caching.Memory` reference (appropriate for DedupeCache)

**No concerns.**

---

### PLAN-2.1 — FrigateMqttEventSource, Projector, PluginRegistrar (Wave 2) ⚠ Medium

**Status:** Ready (with build-time verification caveat)

**Key changes:**
- Task 3 (PluginRegistrar) now **explicitly lists forbidden registrations** to include the dropped `ISubscriptionProvider` and `IEventMatchSink` (line 79).
- Verification step (line 82) now greps for these to confirm they are absent — defensive programming, well done.

**Strengths:**
- MQTTnet v5 API shape correctly identified: plain `IMqttClient` + custom reconnect loop (5s delay, `TryPingAsync`), no `ManagedMqttClient`
- Per-client TLS via `WithCertificateValidationHandler` (correct API path per RESEARCH)
- D5 guard applied at projection time (pre-channel-write), events never reach matcher if skipped
- D3 revised correctly: `SnapshotFetcher = _ => ValueTask.FromResult<byte[]?>(null)` (no thumbnail, no HTTP)
- EventContext.Zones union logic: `HashSet<string>` with `OrdinalIgnoreCase`, then materialized to `IReadOnlyList` (good performance + semantics)
- RawPayload correctly UTF-8 decoded once from `PayloadSegment.Span`
- Keyed singleton registration for `IMemoryCache` is correct .NET pattern (though PLAN-3.1 simplifies this to non-keyed in Host)
- 11 tests planned (7 projector + 4 source) provide good coverage

**CAUTION — Build-time verification needed:**
- MQTTnet 5.1.0.1559 method signatures must be validated at PLAN-2.1 execution start
- Specific concerns:
  - Confirm `TryPingAsync(CancellationToken)` exists (vs `PingAsync` or `PingAsyncSafe`)
  - Confirm `MqttClientOptionsBuilder.WithTlsOptions(Action<...>)` exists and builder pattern is as documented
  - Confirm `factory.CreateSubscribeOptionsBuilder()` is the correct factory method
  - If any of these differ, the build fails immediately (not a hidden integration bug)
- **Mitigation:** PLAN-2.1's verify step includes `dotnet build`, which will catch these immediately
- **Allocated effort:** 5–10 minutes at Wave 2 start to validate RESEARCH Cookbook against actual NuGet package

**Not a blocker:** The architectural correctness is high; only mechanical API binding remains.

---

### PLAN-3.1 — EventPump, CI/Jenkins, PlaceholderWorker Removal (Wave 3) ✓

**Status:** Ready

**Key changes:**
- Task 1 no longer creates `ISubscriptionProvider` or `IEventMatchSink` interfaces
- EventPump now injects exactly 4 dependencies: `IEnumerable<IEventSource>`, `DedupeCache`, `IOptions<HostSubscriptionsOptions>`, `ILogger<EventPump>`
- `SubscriptionMatcher` is static (called directly, not injected) — lean design
- Task 1 verify steps (line 81–82) now grep for dropped abstractions to confirm they're absent across entire src/ tree

**Strengths:**
- PlaceholderWorker removal is clean and explicit
- CI/Jenkins consolidation via shared `run-tests.sh` is a solid DRY pattern (shared script, both pipelines call it, syntax check `bash -n` before execution)
- EventPump's `Parallel.ForEachAsync` over `IEnumerable<IEventSource>` correctly supports future multi-source scenarios without tight coupling
- Test assertions (2 added) cover both happy path (one event → one log line) and dedupe hit (second event suppressed)
- Config binding shape correctly maps top-level `Subscriptions` array to `HostSubscriptionsOptions.Subscriptions`

**Residual concern — Startup race (documented):**
- Plugin's hosted-service adapter may call `FrigateMqttEventSource.StartAsync()` before EventPump's `await foreach` is reading from the channel.
- If 10 events publish during the gap, they stay in the unbounded channel (no drops) — correct behavior.
- Test #2 covers rapid succession (dedupe hit scenario); does NOT explicitly cover the pre-reader window, but channel semantics guarantee correctness.
- **Mitigation:** PLAN-2.1 uses unbounded `Channel<EventContext>` with `SingleWriter=true` — no drops, correct.
- **Phase 4 scope:** Integration tests (Testcontainers) will exercise real timing; Phase 3 unit tests are sufficient for the logic.

**Not a blocker:** Unbounded channel design is sound; race mitigation is correct.

---

## Architectural Review

### Host stays Plugin-Agnostic ✓

**Requirement:** Host should not hardcode FrigateMqtt-specific logic.

**Revised design:** EventPump iterates `IEnumerable<IEventSource>` generically. Subscriptions are Host-configured (top-level `appsettings.json`), not plugin-specific. SubscriptionMatcher and DedupeCache are Host-owned and reusable across any source.

**Result:** A second event source (HTTP webhook, Zigbee) can be added as a new `IEventSource` implementation + registrar, with **zero changes to EventPump**. Host remains plugin-agnostic.

**Verdict: PASS** — Clean separation of concerns.

### Abstractions Surface Protected ✓

**Requirement:** `FrigateRelay.Abstractions` must not grow with Phase 3 speculative types.

**Revision:** Both `ISubscriptionProvider` and `IEventMatchSink` are dropped. `FrigateRelay.Abstractions` gains **zero** new types in Phase 3.

**Result:** Abstractions surface remains Phase 2 clean. Simplification is correct.

**Verdict: PASS** — YAGNI principle correctly applied.

### MQTTnet v5 API Shape ✓

**Requirement:** Plans correctly reference v5 API (no `ManagedMqttClient`).

**Evidence:** PLAN-2.1 Task 2 uses plain `IMqttClient`, `TryPingAsync` loop, per-client `WithCertificateValidationHandler`. No `ManagedMqttClient` anywhere.

**Verification:** Build-time grep checks for `ManagedMqttClient`, `ServicePointManager`, `.Result`, `.Wait()` all return zero matches (plans include these).

**Build caveat:** Exact method signatures must be validated against v5 API docs at Wave 2 start.

**Verdict: PASS** — Shape is correct; API validation deferred to build.

---

## Cross-Cutting Observations

### File Conflicts — Zero ✓

| Wave | Plans | Files Touched |
|------|-------|---------------|
| 1 | PLAN-1.1 | Plugin dirs: `Payloads/`, `Configuration/FrigateMqttOptions.cs` |
| 1 | PLAN-1.2 | Host dirs: `Configuration/`, `Matching/` |
| 2 | PLAN-2.1 | Plugin dirs: `EventContextProjector.cs`, `FrigateMqttEventSource.cs`, `PluginRegistrar.cs` |
| 3 | PLAN-3.1 | Host dirs: `Program.cs`, `EventPump.cs`; CI files; SUMMARY |

**Disjoint across Waves 1–2.** Wave 3 (PLAN-3.1) depends on Waves 1–2 outputs. No parallel conflicts.

**Verdict: PASS** — DAG is acyclic.

### Verification Commands ✓

All commands are concrete and runnable:
- `dotnet build`, `dotnet run --project`, `git grep` — standard .NET CLI
- Wall-time: ~5s per test project run
- Success criteria: measurable (test counts, grep output, file existence)

**Verdict: PASS** — Verification is rigorous.

### Test Count vs. Gate ✓

| Gate | ROADMAP | Planned | Status |
|------|---------|---------|--------|
| Minimum | ≥15 | — | — |
| Phase 3 delivery | — | 28 | +87% cushion |

**Verdict: PASS** — Exceeds gate by 87%.

---

## Recommendations

### 1. MQTTnet v5 API Validation (HIGH — Wave 2 start)

Before PLAN-2.1 execution, allocate 5–10 minutes to validate:
- `TryPingAsync(CancellationToken)` signature in MQTTnet 5.1.0.1559
- `MqttClientOptionsBuilder.WithTlsOptions(Action<MqttClientTlsOptions>)` exact parameter type
- `factory.CreateSubscribeOptionsBuilder()` return type and builder API
- `MqttApplicationMessage.PayloadSegment.Span` property exists

**Justification:** Plans reference RESEARCH Cookbook, which is authoritative. However, v5 is relatively new; a single misnamed method will fail the build immediately. Better to validate upfront than discover at build time.

**Effort:** ~5 min; ROI high (prevents build failure).

---

### 2. EventPump Startup Race Observation (MEDIUM — Phase 4 scope)

The unbounded channel design correctly handles events published before `ReadEventsAsync` begins reading. However, the exact timing behavior under high event volume is untested.

**Recommendation:** Phase 4 integration tests (Testcontainers with synthetic Frigate traffic) should exercise:
- Rapid event burst (100+ events/sec) before EventPump reads
- Verify no drops, no lost events, dedupe works correctly

**Not Phase 3 scope:** Unit tests cover the logic; integration tests cover the timing.

---

### 3. SUMMARY.md Documentation (MEDIUM — Task 3 attention)

PLAN-3.1 Task 3 produces `.shipyard/phases/3/SUMMARY.md` (~150–250 lines). Ensure it covers:
- Phase outcome (test count, decisions resolved)
- Architectural simplification (dropped abstractions, rationale)
- Manual smoke-test recipe (mosquitto_pub command)
- Sample `appsettings.Local.json` snippet (FrigateMqtt + top-level Subscriptions sections)
- Phase 5 flag (revisit `EventContext.SnapshotFetcher` necessity)

**Recommended length:** ~200 lines (concise but comprehensive).

---

## Summary Table

| Item | Finding | Severity |
|------|---------|----------|
| Abstractions dropped | `ISubscriptionProvider` + `IEventMatchSink` removed ✓ | ✓ Ready |
| Host owns matcher/dedupe | Located correctly in Host, not plugin ✓ | ✓ Ready |
| Plugin registrar minimalist | No forbidden registrations ✓ | ✓ Ready |
| EventPump injection | 4 dependencies + 1 static ref; clean design ✓ | ✓ Ready |
| FrigateMqttOptions | Transport-only; no Subscriptions field ✓ | ✓ Ready |
| Config binding | Top-level Subscriptions section ✓ | ✓ Ready |
| Test count | 28 tests (gate: ≥15) ✓ | ✓ Ready |
| MQTTnet v5 API | Shape correct; signatures need build-time validation ⚠ | ⚠ Medium |
| Startup race | Unbounded channel handles pre-reader events ✓ | ✓ Ready |
| CI/Jenkins consolidation | DRY via shared script; both workflows updated ✓ | ✓ Ready |
| PlaceholderWorker | Removed; tests updated ✓ | ✓ Ready |
| Wave dependencies | PLAN-1.1 \|\| PLAN-1.2 → PLAN-2.1 → PLAN-3.1 ✓ | ✓ Ready |

---

## Final Verdict

**READY** — The revised Phase 3 plans are architecturally sound, fully specify all deliverables, and are ready for execution. The removal of the two speculative abstractions simplifies the design without sacrificing extensibility. Proceed to build phase with one caveat: validate MQTTnet v5 API method signatures at Wave 2 start (5–10 minutes, non-blocking but important for build reliability).

---

**Report Author:** Verifier  
**Prior Verdict:** CAUTION (original plans with abstractions)  
**Revised Verdict:** READY (abstractions removed, architecture simplified)  
**Approval:** Ready for Build Phase
