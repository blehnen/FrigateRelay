# Phase 4 — Context & Decisions

**Phase:** 4 — Action Dispatcher + BlueIris (First Vertical Slice)
**Captured:** 2026-04-25
**Source:** `/shipyard:plan 4` discussion capture (AskUserQuestion x2 batches, 7 decisions)

This file records the design choices the user made BEFORE research/architecture so downstream agents (researcher, architect, builder) work from shared assumptions instead of inventing their own.

---

## D1 — Channel topology: per `IActionPlugin`

`ChannelActionDispatcher` keys its bounded `Channel<DispatchItem>` map by `IActionPlugin` instance (NOT by `(subscription, action)` pair, NOT a single shared channel).

```csharp
Dictionary<IActionPlugin, Channel<DispatchItem>>
├─ BlueIris   -> Channel(cap=256) + 2 consumer tasks
├─ Pushover   -> Channel(cap=256) + 2 consumer tasks   (lands Phase 6)
└─ ...
```

**Rationale.** Roadmap phrase "per action" maps cleanly to per-plugin granularity. Isolates blast radius — a slow Pushover handler can't starve BlueIris consumers (head-of-line blocking on a single shared channel was the legacy `DotNetWorkQueue` failure mode). Per-(subscription, action) was rejected as premature explosion of channel count that Phase 8 Profiles would have to refactor anyway.

**Concrete shape.**
- 2 consumer tasks per channel (roadmap-mandated).
- Channel created during `IHostedService.StartAsync`, completed during `StopAsync` so `await foreach` cleanly exits.
- Map populated from `IEnumerable<IActionPlugin>` injected by DI — registrars register each plugin as `services.AddSingleton<IActionPlugin, BlueIrisActionPlugin>()`.

---

## D2 — Subscription → action wiring: `Actions: [...]` array on `SubscriptionOptions`

`SubscriptionOptions` gains a new init-only property:

```csharp
public IReadOnlyList<string> Actions { get; init; } = Array.Empty<string>();
```

Each subscription declares which named action plugins fire. Empty/missing → **no actions fire** (fail-safe; not an error). Unknown plugin name → **startup fail-fast** with diagnostic listing the unknown name and the registered plugin names. Matches PROJECT.md decision **S2** (Profiles + Subscriptions, fail-fast on unknown plugin names).

```jsonc
{
  "Subscriptions": [
    {
      "Name": "FrontDoor",
      "Camera": "front",
      "Label": "person",
      "CooldownSeconds": 60,
      "Actions": ["BlueIris"]
    }
  ]
}
```

**Forward-compat with Phase 8.** Phase 8 introduces named profiles with inline action lists; subscriptions will then accept either a `Profile: "..."` reference OR an inline `Actions: [...]` (today's shape becomes the inline branch). No rewrite — additive only.

**Rejected alternatives.**
- **"Fire all registered IActionPlugins on every match"** rejected: when Pushover lands in Phase 6, every camera/label fires push notifications until Phase 8 Profiles arrive — a six-phase regression.
- **"Hard-code BlueIris in Phase 4"** rejected: same problem, just deferred one phase.

**EventPump touch.** EventPump must call `dispatcher.EnqueueAsync(ctx, plugin, ...)` for each name in the matched subscription's `Actions[]`. Plugin lookup is by `IActionPlugin.Name` (case-insensitive ordinal). The action-name → plugin map is built once at startup; absent at startup = fail-fast.

---

## D3 — BlueIris URL template: `{placeholder}` with fixed allowlist

`BlueIrisOptions.TriggerUrlTemplate` (string, required) accepts these placeholders **only**:

| Placeholder    | Source                        | Notes                                                                            |
| -------------- | ----------------------------- | -------------------------------------------------------------------------------- |
| `{camera}`     | `EventContext.Camera`         | URL-encoded                                                                      |
| `{label}`      | `EventContext.Label`          | URL-encoded                                                                      |
| `{event_id}`   | `EventContext.EventId`        | URL-encoded                                                                      |
| `{score}`      | `EventContext.Score`          | Invariant culture, no padding (e.g. `0.85`)                                      |
| `{zone}`       | First entry of `Zones`, or `""` if empty. URL-encoded. (Subscription's matched zone isn't carried on EventContext yet — first zone is the v1 approximation; Phase 8 may revisit.) |

**Unknown placeholder at startup = fail-fast.** Template parser scans for `{...}` tokens at registrar time, rejects any not in the allowlist. Error message lists the offending token + the supported set.

**Example.**
```
TriggerUrlTemplate: "https://bi.local/admin?camera={camera}&trigger=1&memo={label}"
Resolves to:        "https://bi.local/admin?camera=front&trigger=1&memo=person"
```

**Rejected.** Razor-style `${ctx.X.ToLower()}` was rejected — pulls in expression evaluation, opens injection surface, no user demand. Per-camera literal-URL map was rejected — explodes config and forces every plugin to invent its own templater.

**Reusability.** Phase 5's `BlueIrisSnapshot` will reuse the same templater; placement TBD by architect (likely `FrigateRelay.Plugins.BlueIris` internal helper rather than `Abstractions` — keep the Abstractions assembly free of templating logic per the "no third-party runtime deps" rule).

---

## D4 — Validators: empty seam now, smooth Phase-7 diff

`IActionDispatcher.EnqueueAsync` accepts an `IReadOnlyList<IValidationPlugin>` parameter today, even though the validator chain doesn't activate until Phase 7 (CodeProject.AI):

```csharp
ValueTask EnqueueAsync(
    EventContext ctx,
    IActionPlugin action,
    IReadOnlyList<IValidationPlugin> validators,  // empty in Phase 4
    CancellationToken ct);
```

Phase 4 always passes an empty list. Behavior is identical to "no validators" — dispatcher does not iterate or invoke them.

**Rationale.** PROJECT.md decision **V3** (validators are per-action, not global) means the dispatcher already needs to know which validators belong to which action. Adding the empty parameter now means Phase 7 implements *behavior* without changing *contract*. Risk acknowledged: if CodeProject.AI's `Verdict` shape forces a widening of `IValidationPlugin`, the contract may still need a second turn — accepted as low-probability since `Verdict` already carries score + reason from Phase 1.

**EventPump's responsibility (v1).** Pass `Array.Empty<IValidationPlugin>()`. Phase 7 will populate from per-action config.

---

## D5 — Channel capacity default: 256, drop-oldest on overflow

```csharp
new BoundedChannelOptions(256) {
    FullMode = BoundedChannelFullMode.DropOldest,
    SingleReader = false,   // 2 consumers
    SingleWriter = true     // EventPump is the sole producer
}
```

**Default 256.** Absorbs ~4 minutes of 1 evt/sec sustained traffic before drop-oldest activates — well above the worst-case Frigate burst (~64 events in a 60-second storm during a parade-of-people event). DispatchItem is a struct (event-context ref + plugin ref + Activity ref) so memory cost is negligible (<10 KB worst case).

**Configurable per plugin.** `BlueIrisOptions.QueueCapacity` (int, optional) overrides for the BlueIris-specific channel. Future plugins follow the same pattern (`PushoverOptions.QueueCapacity`, etc.). Architect may choose to expose this as a generic per-plugin convention OR inline it on each plugin's options — call at plan time.

**Why DropOldest.** Newer events are more actionable than stale ones — if BlueIris is unreachable, a 30-second-old "person at front door" matters less than the latest detection. Roadmap mandates it explicitly.

**`SingleWriter = true`** is correct because Phase 4 has only one EventPump producing into the dispatcher. If a future architecture introduces additional producers, this flag becomes a `false` change with no other ripple.

---

## D6 — Drop telemetry: counter + warning log (both)

When the channel is full and `DropOldest` evicts an event, emit BOTH:

1. **Meter counter** on the existing `Meter "FrigateRelay"` ActivitySource (already pinned in CLAUDE.md):
   ```
   frigaterelay.dispatch.drops { action = "BlueIris" } += 1
   ```
2. **`ILogger.LogWarning`** carrying the dropped event id, action name, and capacity in structured state (NOT message-formatted):
   ```
   Dropped event_id=ev-abc123 action=BlueIris queue_full capacity=256.
   Downstream may be unhealthy.
   ```

**Why both.** Phase 9 wires the OTel exporter — the counter goes live with zero Phase-4 effort. Until then, the log gives a developer-running-locally feedback loop. Roadmap explicitly mandates both ("drop-oldest **metric** on overflow" AND "post-exhaustion **log** at Warning"). Counter naming uses the `frigaterelay.*` prefix per CLAUDE.md observability invariant.

**Rejected.**
- **Log only** (skip counter for Phase 9): trivial wiring deferral with no benefit.
- **Counter only**: invisible to local dev until Phase 9 OTel exporter lands — bad signal-to-effort ratio.

---

## D7 — Polly v8 wiring: `AddResilienceHandler` on the named HttpClient

Each action plugin that does HTTP gets a named `HttpClient` registered through `IHttpClientFactory`, with the resilience pipeline attached at the **handler chain** layer (Microsoft-blessed pattern from `Microsoft.Extensions.Http.Resilience`):

```csharp
services.AddHttpClient("BlueIris", client =>
    {
        var opts = sp.GetRequiredService<IOptions<BlueIrisOptions>>().Value;
        client.Timeout = opts.RequestTimeout;
    })
    .AddResilienceHandler("retry", builder =>
        builder.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            DelayGenerator = args => ValueTask.FromResult<TimeSpan?>(
                TimeSpan.FromSeconds(3 * (args.AttemptNumber + 1)))
        }))
    .ConfigurePrimaryHttpMessageHandler(sp =>
    {
        var opts = sp.GetRequiredService<IOptions<BlueIrisOptions>>().Value;
        var handler = new SocketsHttpHandler();
        if (opts.AllowInvalidCertificates)
        {
            handler.SslOptions.RemoteCertificateValidationCallback =
                static (_, _, _, _) => true;
        }
        return handler;
    });
```

**Delay schedule.** `DelayGenerator` returns 3s on attempt 0, 6s on attempt 1, 9s on attempt 2 (matches reference behavior in legacy `FrigateMQTTProcessingService`). After 3 failed retries the pipeline surfaces the last exception; `BlueIrisActionPlugin.ExecuteAsync` does NOT swallow it — the dispatcher's consumer loop catches it and emits the "dropped after 3 retries" warning + counter (per **D6** above, but on a separate `frigaterelay.dispatch.exhausted` counter, NOT the queue-overflow `drops` counter — architect to disambiguate at plan time).

**TLS opt-in.** `AllowInvalidCertificates: true` per-plugin only, scoped to the plugin's own `SocketsHttpHandler`. Default is `false`. **Never** sets a global `ServicePointManager.ServerCertificateValidationCallback` — CLAUDE.md invariant `git grep ServicePointManager src/` must remain empty.

**Rejected.** `ResiliencePipelineBuilder` invoked inline by the dispatcher was rejected — would force the dispatcher to own per-plugin retry config, fights the M.E.Resilience design, and centralizes concerns that should live next to each plugin.

---

## Cross-cutting confirmations (re-stated for downstream agents)

- **CLAUDE.md invariants this phase exercises:** no `.Result`/`.Wait()`; no `ServicePointManager`; `frigaterelay.*` metric prefix; `ActivitySource "FrigateRelay"`; no hard-coded IPs/hostnames in source (including comments); `MemoryCache` is host-scoped, not `MemoryCache.Default`.
- **Test suite expectations** (from ROADMAP):
  - `tests/FrigateRelay.IntegrationTests/` is **new** — needs a csproj, MTP wiring, addition to `.github/scripts/run-tests.sh` (auto-discovers from `find tests/*.Tests`), and a Jenkinsfile cobertura step (mirrors existing pattern).
  - Dispatcher unit tests ≥ 6 in `tests/FrigateRelay.Host.Tests/Dispatch/`.
  - End-to-end `MqttToBlueIris_HappyPath` integration test using Testcontainers Mosquitto + WireMock.Net Blue Iris stub, < 30s.
- **Graceful shutdown.** `ChannelActionDispatcher.StopAsync` must complete the channel writer, drain in-flight items (or cancel their HTTP calls via the `CancellationToken` passed through `EnqueueAsync`), and let consumer tasks exit. Architect may optionally reuse the Phase-3 `Interlocked.Exchange` idempotency pattern from `FrigateMqttEventSource` if the consumer-task lifetime needs the same protection.
- **Issue ID-1 (Phase 3 PLAN-3.1 wording)** is non-blocking and unrelated to Phase 4; architect can ignore.

---

## Open questions for the architect (resolve inline in plans, do NOT bring back to the user)

- Whether to put the URL templater in `FrigateRelay.Plugins.BlueIris` (private) or in a new `FrigateRelay.Host` shared helper. **Recommendation:** plugin-private until a second consumer appears (Phase 5 `BlueIrisSnapshot` is in the same plugin assembly per ROADMAP, so it's still plugin-scope).
- Whether per-plugin `QueueCapacity` should be a property on each plugin's options class OR a generic shape. **Recommendation:** plugin-private property — keeps the abstraction free of dispatcher-specific config and matches D5.
- Where the second telemetry counter `frigaterelay.dispatch.exhausted` (retry-exhaustion vs queue-overflow `drops`) lives. **Recommendation:** dispatcher emits both; one counter per failure mode, both tagged with `action`.
- Wave/plan layout. The phase has natural seams: (a) abstraction + dispatcher, (b) BlueIris plugin, (c) integration test infrastructure, (d) wiring + EventPump update. Architect to decide parallelism — note that (a) and (c) can likely run in parallel since (c)'s test project only references abstractions + a fixture that mocks the dispatcher contract. (b) blocks on (a)'s contract; (d) blocks on (a) and (b).
