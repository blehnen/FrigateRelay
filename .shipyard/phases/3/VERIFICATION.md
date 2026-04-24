# Phase 3 Verification — Post-Revision

**Phase:** Frigate MQTT Ingestion and EventContext Projection  
**Date:** 2026-04-24  
**Type:** plan-review (post-revision)  
**Branch:** Initcheckin

---

## Verdict: READY

**Summary:** The revised Phase 3 plans **successfully remove the two speculative abstractions** (`ISubscriptionProvider`, `IEventMatchSink`) flagged in the feasibility critique. `FrigateRelay.Abstractions` surface is now **unchanged** from Phase 2. All four plans remain structurally sound, fully cover ROADMAP deliverables, and honor all five CONTEXT-3 decisions. Test-count gate remains ≥15 (planned delivery: ≥28). The simplification makes EventPump implementation more straightforward and defers multi-source federation design to Phase 4+, when real requirements emerge.

---

## Changes Verified

### 1. Abstractions Surface Remains Unchanged ✓

**Prior VERIFICATION.md claim:** Plans would add `ISubscriptionProvider` and `IEventMatchSink` to `FrigateRelay.Abstractions`.

**Revised reality:** Both abstractions are **explicitly dropped**.

| Plan | Evidence |
|------|----------|
| PLAN-2.1 §47 | "Any `ISubscriptionProvider` / `IEventMatchSink` — these abstractions are dropped entirely (per user decision following the feasibility critique). `FrigateRelay.Abstractions` receives **zero** new types in Phase 3." |
| PLAN-2.1 Task 3 verify | `git grep -nE "ISubscriptionProvider\|IEventMatchSink" src/FrigateRelay.Abstractions/ \|\| echo "OK: Abstractions surface unchanged"` |
| PLAN-3.1 §37 | "`ISubscriptionProvider` and `IEventMatchSink` are **dropped**. They are not added to `FrigateRelay.Abstractions`. The Abstractions surface in Phase 3 is unchanged." |
| PLAN-3.1 Task 1 verify | `git grep -nE "ISubscriptionProvider\|IEventMatchSink" src/ tests/ \|\| echo "OK: dropped abstractions absent"` |

**Verdict: PASS** — Abstractions surface is protected. No new types added.

---

### 2. Host Gains Matcher/Dedupe/Config Types (NOT Plugin) ✓

**Requirement:** Subscription matching logic must live in Host, not the plugin.

| Artifact | Location (Per PLAN-1.2) | Evidence |
|----------|-----------|----------|
| `SubscriptionOptions` | `src/FrigateRelay.Host/Configuration/` | `files_touched`: line 16 |
| `HostSubscriptionsOptions` | `src/FrigateRelay.Host/Configuration/` | `files_touched`: line 17 |
| `SubscriptionMatcher` | `src/FrigateRelay.Host/Matching/` | `files_touched`: line 18 |
| `DedupeCache` | `src/FrigateRelay.Host/Matching/` | `files_touched`: line 19 |

**Parallel-safe Wave 1:** PLAN-1.1 (plugin dirs) and PLAN-1.2 (Host dirs) touch **disjoint files**. No conflicts.

**Verdict: PASS** — Host owns the reusable matching logic; plugin stays minimal.

---

### 3. Plugin Registrar Does NOT Register Host Types ✓

**Requirement (PLAN-2.1 Task 3):** PluginRegistrar must explicitly avoid registering `IMemoryCache`, `DedupeCache`, `SubscriptionMatcher`, or the dropped `ISubscriptionProvider`/`IEventMatchSink`.

**Evidence:** PLAN-2.1 Task 3 action:
```
**Explicitly do NOT register**: `IMemoryCache`, `DedupeCache`, `SubscriptionMatcher`, 
`ISubscriptionProvider`, `IEventMatchSink`. The first three live in Host; the last two do not exist 
(dropped per simplification decision).
```

**Verification step:** PLAN-2.1 Task 3 verify:
```bash
grep -nE "AddKeyedSingleton|DedupeCache|SubscriptionMatcher|ISubscriptionProvider|IEventMatchSink|IMemoryCache" 
src/FrigateRelay.Sources.FrigateMqtt/PluginRegistrar.cs || echo "OK: registrar contains none of the forbidden registrations"
```

**Verdict: PASS** — Plugin registrar is minimalist (FrigateMqttOptions, MqttClientFactory, IMqttClient, FrigateMqttEventSource, IEventSource alias, hosted-service adapter only).

---

### 4. EventPump Injection Signature ✓

**Requirement (PLAN-3.1 must_haves):** EventPump injects exactly: `IEnumerable<IEventSource>`, `SubscriptionMatcher` (static — no injection), `DedupeCache`, `IOptions<HostSubscriptionsOptions>`, `ILogger<EventPump>`.

**Evidence:** PLAN-3.1 must_haves, line 7:
```
EventPump : BackgroundService in FrigateRelay.Host injects IEnumerable<IEventSource>, 
SubscriptionMatcher (static — invoked directly), DedupeCache, 
IOptions<HostSubscriptionsOptions>, ILogger<EventPump>
```

**Task 1 action pseudocode:**
```csharp
public sealed class EventPump : BackgroundService
{
    // Constructor injects:
    EventPump(IEnumerable<IEventSource> sources, 
              DedupeCache dedupe, 
              IOptions<HostSubscriptionsOptions> subsOptions, 
              ILogger<EventPump> logger)
    
    // SubscriptionMatcher is NOT injected — called directly (static)
    var matches = SubscriptionMatcher.Match(ctx, subs);
}
```

**Verdict: PASS** — No `ISubscriptionProvider` or `IEventMatchSink` injection. Static matcher avoids DI overhead.

---

### 5. FrigateMqttOptions: Transport-Only ✓

**Requirement (PLAN-1.1 must_haves):** `FrigateMqttOptions` has **no `Subscriptions` field**. Subscriptions are top-level host config.

**Evidence:** PLAN-1.1 Task 3 action:
```
**Do NOT add a `Subscriptions` member here** — subscriptions are host-level (PLAN-1.2 / PLAN-3.1). 
All members get XML doc comments. The XML doc on `FrigateMqttOptions` explicitly states 
"Subscription rules are bound separately from the top-level `Subscriptions` configuration section by the host; 
this options type covers MQTT transport only."
```

**Verdict: PASS** — Plugin options remain transport-only; host config is orthogonal.

---

### 6. Subscriptions Bind from Top-Level Config Section ✓

**Requirement (PLAN-1.2 must_haves):** `Subscriptions` is bound from **top-level** `appsettings.json`, not `FrigateMqtt:Subscriptions`.

**Evidence:**
- PLAN-1.2 must_haves, line 8: "`HostSubscriptionsOptions` wrapper record ... to bind the **top-level** `Subscriptions` config section"
- PLAN-1.1 Context: "The host section for subscription rules is the **top-level** `Subscriptions` array (config option (b) in the prompt)."
- PLAN-3.1 Task 1 action: "`services.Configure<HostSubscriptionsOptions>(builder.Configuration.GetSection("Subscriptions"))`"

**Verdict: PASS** — Config shape matches Phase 8's Profiles+Subscriptions pattern.

---

### 7. Test Counts ✓

| Plan | Requirement | Tests |
|------|-------------|-------|
| PLAN-1.1 | >= 6 deserialization tests | 6 |
| PLAN-1.2 | >= 9 matcher + dedupe tests | 9 (6 matcher, 3 dedupe) |
| PLAN-2.1 | >= 6 projector + source tests | 11 (7 projector, 4 source) |
| PLAN-3.1 | >= 2 EventPump tests | 2 (happy path, dedupe hit) |
| **Total** | **>= 15** | **28** |

**ROADMAP gate:** ≥15 passing tests.  
**Planned delivery:** 28 tests.  
**Verdict: PASS** — 87% above gate.

---

### 8. CI/Jenkins Expansion ✓

**Requirement (PLAN-3.1 Task 2):** Extract `.github/scripts/run-tests.sh`; both `ci.yml` and `Jenkinsfile` call it.

**Evidence:**
- PLAN-3.1 Task 2 action: "Create `.github/scripts/run-tests.sh` ... `chmod +x` the file"
- PLAN-3.1 Task 2 action: "Update `.github/workflows/ci.yml` so the test step calls `bash .github/scripts/run-tests.sh`"
- PLAN-3.1 Task 2 action: "Update `Jenkinsfile` similarly"
- PLAN-3.1 Task 2 verify: `bash -n .github/scripts/run-tests.sh` (syntax check)

**Verdict: PASS** — DRY consolidation preserves both pipelines' compatibility.

---

### 9. PlaceholderWorker Removal ✓

**Requirement (PLAN-3.1 must_haves):** PlaceholderWorker deleted; Host.Tests updated.

**Evidence:**
- PLAN-3.1 must_haves, line 10: "`PlaceholderWorker` removed; Host.Tests updated"
- PLAN-3.1 Task 1 action: "Delete `src/FrigateRelay.Host/PlaceholderWorker.cs`"
- PLAN-3.1 Task 1 verify: `test ! -f src/FrigateRelay.Host/PlaceholderWorker.cs` (confirms deletion)

**Verdict: PASS** — EventPump subsumes its purpose.

---

### 10. Wave Structure: Dependencies Acyclic ✓

| Wave | Plans | Dependencies | Status |
|------|-------|--------------|--------|
| 1 | PLAN-1.1, PLAN-1.2 | `dependencies: []` | Parallel-safe (disjoint files) |
| 2 | PLAN-2.1 | `dependencies: [1.1, 1.2]` | Depends on DTOs + matcher |
| 3 | PLAN-3.1 | `dependencies: [2.1]` | Depends on FrigateMqttEventSource |

**Verdict: PASS** — DAG is acyclic; Wave 1 can parallelize; Waves 2→3 are sequential.

---

## Residual Concerns

### (1) MQTTnet v5 API Surface — Mark for Build-Time Verification

The revised plans correctly reference the plain `IMqttClient` + manual reconnect loop per RESEARCH.md Cookbook. However, exact method signatures (e.g., `TryPingAsync` vs `PingAsync`, `WithTlsOptions` builder API) must be validated when PLAN-2.1 is executed.

**Recommendation:** At Wave 2 start, allocate 5–10 minutes to verify MQTTnet 5.1.0.1559 method names against RESEARCH lines 59–135. If any signature diverges, the build will fail immediately.

**Not a blocker:** The architectural shape is correct; only mechanical API binding remains.

### (2) EventPump Startup Race — Documented Mitigation

Both PLAN-3.1 Context and PLAN-2.1 Context note: plugin's hosted-service adapter may publish events before EventPump's `await foreach` begins reading.

**Mitigation:** Unbounded `Channel<EventContext>` with `SingleWriter=true` (per PLAN-2.1 Task 2) automatically buffers early events with no drops.

**Verification:** PLAN-3.1 Task 1 test #2 covers the dedupe hit scenario (two events in rapid succession); no test explicitly covers pre-reader events, but the channel's unbounded semantics guarantee no loss.

**Not a blocker:** The channel design is correct; Phase 4 integration tests will exercise the real timing.

---

## Checks Run

```bash
# All revised plans explicitly state dropped abstractions
grep -n "ISubscriptionProvider\|IEventMatchSink" \
  .shipyard/phases/3/plans/PLAN-2.1.md \
  .shipyard/phases/3/plans/PLAN-3.1.md | \
  grep -i "dropped\|zero.*new\|do not exist"
# Expected: 4+ matches affirming removal

# All Host-side artifacts correctly located
grep "src/FrigateRelay\.Host" \
  .shipyard/phases/3/plans/PLAN-1.2.md | \
  grep -E "Configuration|Matching"
# Expected: 4+ lines (SubscriptionOptions, HostSubscriptionsOptions, SubscriptionMatcher, DedupeCache)

# Plugin registrar excludes forbidden registrations
grep "Explicitly do NOT register" \
  .shipyard/phases/3/plans/PLAN-2.1.md
# Expected: confirmation text

# EventPump uses static SubscriptionMatcher (not injected)
grep -A10 "Constructor injects:" \
  .shipyard/phases/3/plans/PLAN-3.1.md | \
  grep "SubscriptionMatcher"
# Expected: line stating static, direct call

# Test counts meet gate
grep ">= [0-9]" .shipyard/phases/3/plans/PLAN-*.md | grep "tests"
# Expected: 6 + 9 + 11 + 2 = 28 total
```

---

## Verdict

**READY** — The revised Phase 3 plans successfully address the prior feasibility critique by removing the two speculative abstractions. `FrigateRelay.Abstractions` surface remains unchanged. All deliverables are accounted for, dependencies are correct, and test gates are exceeded. The simplification is architecturally sound: matcher/dedupe logic lives in Host (reusable across any `IEventSource`), and EventPump stays lean (4 injected dependencies, 1 static reference). Proceed to build execution.

---

**Report Author:** Verifier  
**Prior Verdict:** CAUTION (original plans with abstractions)  
**Revised Verdict:** READY (abstractions removed)
