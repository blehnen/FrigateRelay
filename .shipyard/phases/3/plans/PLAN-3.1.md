---
phase: frigate-mqtt-ingestion
plan: 3.1
wave: 3
dependencies: [1.2, 2.1]
must_haves:
  - EventPump : BackgroundService in FrigateRelay.Host injects IEnumerable<IEventSource>, SubscriptionMatcher (static — invoked directly), DedupeCache, IOptions<HostSubscriptionsOptions>, ILogger<EventPump>; for each source runs a Task that awaits foreach over ReadEventsAsync(stoppingToken); applies SubscriptionMatcher; applies DedupeCache.TryEnter; emits one Information log per surviving (sub, ctx) tuple via LoggerMessage.Define
  - FrigateRelay.Abstractions receives ZERO new types
  - Program.cs binds top-level `Subscriptions` config section to HostSubscriptionsOptions; registers IMemoryCache singleton; registers DedupeCache singleton; registers EventPump as IHostedService
  - PlaceholderWorker removed; Host.Tests updated so existing host tests still pass
  - PluginRegistrarRunner already loads FrigateMqtt.PluginRegistrar via the existing discovery mechanism (matches Phase 2's pattern)
  - sample appsettings.Local.json snippet documented in SUMMARY (FrigateMqtt section + top-level Subscriptions section) for the manual mosquitto_pub smoke
  - .github/workflows/ci.yml runs the new test project alongside existing ones via shared run-tests.sh
  - Jenkinsfile runs the same shared script
  - Phase 3 total new tests >= 15 (sum across PLAN-1.1, 1.2, 2.1, 3.1 contributions); this plan adds >= 2 EventPump tests using a fake IEventSource
files_touched:
  - src/FrigateRelay.Host/EventPump.cs
  - src/FrigateRelay.Host/Program.cs
  - src/FrigateRelay.Host/PlaceholderWorker.cs (deleted)
  - tests/FrigateRelay.Host.Tests/EventPumpTests.cs
  - tests/FrigateRelay.Host.Tests/HostStartupTests.cs (update if it references PlaceholderWorker)
  - .github/workflows/ci.yml
  - .github/scripts/run-tests.sh (new)
  - Jenkinsfile
  - .shipyard/phases/3/SUMMARY.md
tdd: true
risk: medium
---

# PLAN-3.1 — EventPump, PlaceholderWorker removal, CI/Jenkins extension

## Context

Final wiring plan. Replaces `PlaceholderWorker` with a real `EventPump : BackgroundService` that resolves all registered `IEventSource` instances and consumes them in parallel. Each consumed `EventContext` runs through the **Host-owned** `SubscriptionMatcher.Match(ctx, options.Subscriptions)` (D1 multi-match) then the **Host-owned** `DedupeCache.TryEnter(sub, ctx)` (per-subscription cooldown). Surviving tuples emit one Information-level log line: `"Matched event: subscription={Sub}, camera={Camera}, label={Label}, event_id={EventId}"` via `LoggerMessage.Define` (allocation-free per CONTEXT-3 §"hot path"). Phase 3 stops here — no dispatcher (Phase 4).

**Architectural simplification (per user decision following the feasibility critique).**
- `ISubscriptionProvider` and `IEventMatchSink` are **dropped**. They are not added to `FrigateRelay.Abstractions`. The Abstractions surface in Phase 3 is unchanged.
- `SubscriptionMatcher`, `DedupeCache`, and `SubscriptionOptions` live in `FrigateRelay.Host` (created in PLAN-1.2). EventPump references them directly — they are sibling Host types, no cross-assembly indirection needed.
- Subscriptions are bound from the **top-level `Subscriptions` config section** into `HostSubscriptionsOptions` (option (b) in the prompt). This matches Phase 8's Profiles+Subscriptions composition shape.
- EventPump iterates `IEnumerable<IEventSource>` generically. Adding a future source (HTTP webhook, Zigbee) is a no-code change to EventPump — the source registers itself as `IEventSource` and EventPump picks it up.

**PlaceholderWorker decision (per prompt).** Removed; `EventPump` subsumes its purpose. Update `HostStartupTests` if any test asserts `PlaceholderWorker` is registered — replace with assertion that `EventPump` is registered as an `IHostedService`.

**CI/Jenkins expansion strategy (decision).** Extract a shared `.github/scripts/run-tests.sh` that loops over a hard-coded list of test project paths (`tests/FrigateRelay.Host.Tests`, `tests/FrigateRelay.Sources.FrigateMqtt.Tests`, future tests/...) and runs `dotnet build -c Release` once at the solution level then `dotnet run --project <each> -c Release --no-build`. **Justification:** the per-project `dotnet run --project` lines repeat verbatim across `ci.yml` and `Jenkinsfile`; centralising eliminates drift when Phase 4 / 5 / 6 each adds a new test project. Both pipelines source from the script. The pipelines also `bash -n` the script as a pre-flight syntax check.

EventPump uses one `Task` per source (via `Parallel.ForEachAsync` over `IEnumerable<IEventSource>`) so multiple sources consume independently; today there's just one. `DedupeCache` is a singleton, `SubscriptionMatcher` is static. Subscriptions list is read once per event batch from `IOptions<HostSubscriptionsOptions>.Value.Subscriptions`.

## Dependencies

- PLAN-1.2 (SubscriptionOptions, HostSubscriptionsOptions, SubscriptionMatcher, DedupeCache — all in Host)
- PLAN-2.1 (FrigateMqttEventSource registered as `IEventSource`; PluginRegistrar)

## Tasks

<task id="1" files="src/FrigateRelay.Host/EventPump.cs, src/FrigateRelay.Host/Program.cs, src/FrigateRelay.Host/PlaceholderWorker.cs, tests/FrigateRelay.Host.Tests/EventPumpTests.cs, tests/FrigateRelay.Host.Tests/HostStartupTests.cs" tdd="true">
  <action>TDD: write 2 tests in `tests/FrigateRelay.Host.Tests/EventPumpTests.cs` first — (1) given a fake `IEventSource` that yields one matching `EventContext` and a `HostSubscriptionsOptions` with one matching `SubscriptionOptions`, the pump logs exactly one "Matched event" line (assert via in-memory `ILoggerProvider` test double); (2) given two events with the same camera+label and a subscription with a long cooldown, the pump logs only once (DedupeCache hit on the second). The fake `IEventSource` is a small private test class implementing `IEventSource.ReadEventsAsync` via a `Channel<EventContext>` the test writes into.

Implement `internal sealed class EventPump : BackgroundService` in `src/FrigateRelay.Host/EventPump.cs`. Constructor injects: `IEnumerable<IEventSource> sources`, `DedupeCache dedupe`, `IOptions<HostSubscriptionsOptions> subsOptions`, `ILogger<EventPump> logger`. (`SubscriptionMatcher` is static — called directly, not injected.) `ExecuteAsync(CancellationToken stoppingToken)` body:

```
await Parallel.ForEachAsync(_sources, stoppingToken, async (src, ct) =>
{
    await foreach (var ctx in src.ReadEventsAsync(ct).WithCancellation(ct))
    {
        var subs = _subsOptions.Value.Subscriptions;
        var matches = SubscriptionMatcher.Match(ctx, subs);
        foreach (var sub in matches)
        {
            if (_dedupe.TryEnter(sub, ctx))
                LogMatched(_logger, sub.Name, ctx.Camera, ctx.Label, ctx.EventId);
        }
    }
});
```

Use `LoggerMessage.Define&lt;string,string,string,string&gt;` for the matched log line. XML doc the class.

Update `src/FrigateRelay.Host/Program.cs`: (a) bind `HostSubscriptionsOptions` from `builder.Configuration.GetSection("Subscriptions")` — note the section is the **top-level array container**, so the binder pattern is `services.Configure<HostSubscriptionsOptions>(builder.Configuration)` reading the `Subscriptions` property, OR `services.Configure<HostSubscriptionsOptions>(builder.Configuration.GetSection(""))` if root-binding; pick whichever cleanly maps an `appsettings.json` snippet of `{ "Subscriptions": [ {...}, {...} ] }` to `HostSubscriptionsOptions.Subscriptions` — document the chosen shape in code comment; (b) register `IMemoryCache` singleton via `services.AddMemoryCache()`; (c) register `DedupeCache` singleton; (d) replace `services.AddHostedService&lt;PlaceholderWorker&gt;()` with `services.AddHostedService&lt;EventPump&gt;()`; (e) keep the existing `PluginRegistrarRunner` invocation so the FrigateMqtt plugin's registrar runs and registers its `IEventSource` + hosted-service adapter.

Delete `src/FrigateRelay.Host/PlaceholderWorker.cs`. Update `tests/FrigateRelay.Host.Tests/HostStartupTests.cs` if any test references `PlaceholderWorker` — replace with `EventPump` assertion.</action>
  <verify>dotnet build -c Release && dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build && dotnet run --project tests/FrigateRelay.Sources.FrigateMqtt.Tests -c Release --no-build && git grep -nE "ISubscriptionProvider|IEventMatchSink" src/FrigateRelay.Abstractions/ || echo "OK: Abstractions surface unchanged"</verify>
  <done>EventPump tests pass (2 added); existing Host.Tests still pass; PlaceholderWorker.cs is deleted (`test ! -f src/FrigateRelay.Host/PlaceholderWorker.cs`); `git grep -n "PlaceholderWorker" src/ tests/` returns zero matches; `git grep -nE "ISubscriptionProvider|IEventMatchSink" src/` returns zero matches across the entire src/ tree.</done>
</task>

<task id="2" files=".github/scripts/run-tests.sh, .github/workflows/ci.yml, Jenkinsfile" tdd="false">
  <action>Create `.github/scripts/run-tests.sh` (executable, `#!/usr/bin/env bash`, `set -euo pipefail`) with: a `TESTS=(tests/FrigateRelay.Host.Tests tests/FrigateRelay.Sources.FrigateMqtt.Tests)` array; one `dotnet build FrigateRelay.sln -c Release` invocation; then a loop running `dotnet run --project "$t" -c Release --no-build` for each. `chmod +x` the file via git (`git update-index --chmod=+x` or commit with executable bit).

Update `.github/workflows/ci.yml` so the test step calls `bash .github/scripts/run-tests.sh` instead of any per-project lines (preserve existing setup-dotnet, restore, lint, and grep-guard steps). Add a pre-flight `bash -n .github/scripts/run-tests.sh` step to catch shell typos. Update `Jenkinsfile` similarly — replace any per-project test stages with a single `sh '.github/scripts/run-tests.sh'` stage, preceded by `sh 'bash -n .github/scripts/run-tests.sh'`.

Both pipelines must continue to fail the build if `git grep -n "ServerCertificateValidationCallback\|MemoryCache\.Default\|Newtonsoft" src/` finds anything.</action>
  <verify>bash -n .github/scripts/run-tests.sh && bash .github/scripts/run-tests.sh && grep -n "run-tests.sh" .github/workflows/ci.yml && grep -n "run-tests.sh" Jenkinsfile</verify>
  <done>Local script run executes both test projects to green; both CI files reference the script and the syntax-check step; the executable bit is set (`ls -l .github/scripts/run-tests.sh` shows `x`).</done>
</task>

<task id="3" files=".shipyard/phases/3/SUMMARY.md" tdd="false">
  <action>Write `.shipyard/phases/3/SUMMARY.md` (~150–250 lines) documenting:
  - phase outcome (>= 15 new Phase 3 tests passing across two test projects);
  - the four decisions resolved (D1, D2, D3 revised, D5);
  - the deviations from ROADMAP.md (`ManagedMqttClient` -> plain `IMqttClient` + reconnect loop);
  - the **architectural simplification**: `ISubscriptionProvider` and `IEventMatchSink` were considered, then dropped; `SubscriptionMatcher` / `DedupeCache` / `SubscriptionOptions` live in Host (rationale: universal `EventContext` fields make these reusable across any future `IEventSource` with no abstraction tax; `FrigateRelay.Abstractions` surface is unchanged);
  - the manual smoke recipe (`docker run -p 1883:1883 eclipse-mosquitto` then `mosquitto_pub -t frigate/events -m "$(cat sample-payload.json)"`);
  - a sample `appsettings.Local.json` snippet showing both the `FrigateMqtt` section AND the top-level `Subscriptions` array (one entry: name, camera, label, zone, cooldownSeconds);
  - the `git grep -n ServerCertificateValidationCallback src/` invariant verification;
  - Phase 5 flag for revisiting whether `EventContext.SnapshotFetcher` should be removed entirely (per D3 revised).</action>
  <verify>test -f .shipyard/phases/3/SUMMARY.md && wc -l .shipyard/phases/3/SUMMARY.md</verify>
  <done>SUMMARY.md exists, length >= 100 lines, mentions all of: D1, D2, D3-revised, D5, ManagedMqttClient correction, the dropped abstractions, host-owned SubscriptionMatcher/DedupeCache, top-level Subscriptions config section, mosquitto_pub command.</done>
</task>

## Verification

```bash
cd /mnt/f/git/FrigateRelay
dotnet build FrigateRelay.sln -c Release
bash .github/scripts/run-tests.sh
git grep -n "ServerCertificateValidationCallback" src/ || echo "OK: no global TLS callback"
git grep -n "MemoryCache\.Default\|ServicePointManager\|Newtonsoft\|PlaceholderWorker" src/ tests/ || echo "OK: no banned APIs or stale references"
git grep -nE "ISubscriptionProvider|IEventMatchSink" src/ tests/ || echo "OK: dropped abstractions absent"
test ! -f src/FrigateRelay.Host/PlaceholderWorker.cs && echo "OK: PlaceholderWorker removed"
```

Expected: build green; all tests pass with combined Phase 3 new-test count >= 15; all three grep guards print "OK"; `PlaceholderWorker.cs` is gone.
