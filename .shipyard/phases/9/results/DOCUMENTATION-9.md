# Documentation Review — Phase 9

**Phase:** 9 — Observability (Spans, Counters, Serilog/OTel Wiring)
**Type:** Phase-level documentation report

---

## Verdict: ACCEPTABLE (with CLAUDE.md additions recommended)

All new internal types carry complete XML doc comments. No public API surface was added. The two codebase docs that need updating (`STACK.md`, `TESTING.md`) describe the **legacy** codebase only — they are behavioral reference documents for the old FrigateMQTTProcessingService, not live docs for the .NET 10 rewrite. They should not be modified here. New-rewrite observability knowledge belongs in `CLAUDE.md` conventions, which is the correct home for all FrigateRelay-.NET-10 conventions. Two targeted additions to CLAUDE.md are recommended below.

---

## Public API Surface

**Type:** Reference

No new `public` types were added in Phase 9. `DispatcherDiagnostics` remains `internal static`. `DispatchItem.ParentContext` is an internal record struct property. `StartupValidation.ValidateObservability` is `internal static`. The Phase 8 invariant (zero public types added to `FrigateRelay.Host`) is preserved.

Verified: `DispatcherDiagnostics.cs` carries class-level and member-level XML docs on all 10 counters plus the `Meter` and `ActivitySource` fields, each doc naming the increment site, tag dimensions (D3), and semantic meaning. `StartupValidation.cs` carries XML docs on all five methods.

No documentation action required.

---

## Architecture / Concept Docs

**Type:** Explanation

### Span tree (new architectural concept)

Phase 9 introduced a complete producer/consumer span tree. This is not documented anywhere outside the SUMMARYs. Future plugin authors and operators setting up an OTel backend need to know what to expect.

The canonical tree for one matched event with one action and one validator is:

```
mqtt.receive  (Server)
  └── event.match  (Internal)
        └── dispatch.enqueue  (Producer)
              └── action.<name>.execute  (Consumer, parented via DispatchItem.ParentContext)
                    └── validator.<name>.check  (Internal)
```

`dispatch.enqueue` is the producer span; `action.<name>.execute` is the consumer span. The link across the `Channel<T>` hop is `DispatchItem.ParentContext`, which captures `Activity.Current?.Context ?? default` at enqueue time and is passed to `ActivitySource.StartActivity(..., ActivityKind.Consumer, item.ParentContext)` on the consumer side. `default(ActivityContext)` produces a root span (no parent) when there was no ambient activity at enqueue — the `ActivityContext.IsValid` check inside the SDK handles this automatically.

**Recommended action:** Add this span tree to the `CLAUDE.md` conventions block (see section below). No separate architecture document is warranted at this stage; `docs/` does not exist yet (ID-9 deferred to Phase 11/12).

### Counter set and D3 tag dimensions

The 8 `frigaterelay.*` counters live in `DispatcherDiagnostics.cs` with XML docs per D3. The full reference table is:

| Counter | Tags | Increment site |
|---|---|---|
| `frigaterelay.events.received` | `camera`, `label` | `EventPump.PumpAsync`, before match |
| `frigaterelay.events.matched` | `camera`, `label`, `subscription` | `EventPump.PumpAsync`, after match |
| `frigaterelay.actions.dispatched` | `subscription`, `action` | `ChannelActionDispatcher.EnqueueAsync` |
| `frigaterelay.actions.succeeded` | `subscription`, `action` | `ChannelActionDispatcher.ConsumeAsync`, on success |
| `frigaterelay.actions.failed` | `subscription`, `action` | `ChannelActionDispatcher.ConsumeAsync`, after retry exhaustion |
| `frigaterelay.validators.passed` | `subscription`, `action`, `validator` | `ChannelActionDispatcher.ConsumeAsync`, per validator |
| `frigaterelay.validators.rejected` | `subscription`, `action`, `validator` | `ChannelActionDispatcher.ConsumeAsync`, per validator |
| `frigaterelay.errors.unhandled` | (none — single alarmable series, D9) | `EventPump.PumpAsync` outermost catch |
| `frigaterelay.dispatch.drops` | `action` | `ChannelActionDispatcher.EnqueueAsync`, channel full |
| `frigaterelay.dispatch.exhausted` | `action` | `ChannelActionDispatcher.ConsumeAsync`, Polly exhausted |

This table is already present as XML docs in `DispatcherDiagnostics.cs`. No separate reference doc required.

---

## CLAUDE.md Additions

**Type:** Reference (conventions)

Two additions are recommended to the `## Conventions` block. Both encode lessons that recurred across Plans 2.1, 2.2, and 3.1 and are not currently documented anywhere a future agent can find them.

### Addition 1 — Observability instrumentation pattern

Insert after the existing `IActionPlugin.ExecuteAsync` bullet:

> **Observability instrumentation uses `DispatcherDiagnostics.ActivitySource` and `DispatcherDiagnostics.Meter`.** New spans: `using var activity = DispatcherDiagnostics.ActivitySource.StartActivity("name", ActivityKind.X, parentContext)`. New counters: declare `internal static readonly Counter<long> X = DispatcherDiagnostics.Meter.CreateCounter<long>("frigaterelay.x")` on `DispatcherDiagnostics`. Tag conventions follow D3: `camera`/`label` at receive, `subscription`/`action` at dispatch, add `validator` at validation. `ErrorsUnhandled` (`frigaterelay.errors.unhandled`) is intentionally tagless (D9) — one alarmable series regardless of source. Producer/consumer channel hop: capture `Activity.Current?.Context ?? default` at the producer (`DispatchItem.ParentContext`) and pass to `StartActivity(..., ActivityKind.Consumer, item.ParentContext)` at the consumer.

### Addition 2 — Worker SDK Serilog wiring + test capture interaction

Insert after the observability bullet:

> **Worker SDK uses `builder.Services.AddSerilog(...)` from `Serilog.Extensions.Hosting`, NOT `builder.Host.UseSerilog(...)`.** `HostApplicationBuilder` in .NET 10 does not expose `.Host` as `IHostBuilder`. `AddSerilog` replaces the logging provider pipeline, which means any `ILoggerProvider` registered before `HostBootstrap.ConfigureServices` is called will be dropped. Integration test fixtures that need log capture must register their capture provider **after** `ConfigureServices` via `builder.Services.AddSingleton<ILoggerProvider>(capture)`, or use Serilog's own in-memory sink mechanism. The pre-existing `Validator_ShortCircuits_OnlyAttachedAction` failure (Phase 9 Wave 3) is caused by this ordering — tracked as an open regression, deferred to Phase 10.

---

## User-Facing Docs (and ID-9 decision)

**ID-9 status: deferred, no change.**

`appsettings.json` now has `Logging`, `Serilog`, and `Otel` sections that operators must configure. However, this is operator-facing configuration documentation, not developer documentation, and it belongs in a `docs/` directory that does not exist yet. The decision recorded in ID-9 (defer to Phase 11/12 docs pass) stands. No operator guide is produced here.

The `ValidateObservability` fail-fast messages are self-describing (`"Otel:OtlpEndpoint 'x' is not a valid absolute URI."`) and require no supplementary documentation.

---

## Code Documentation Gaps

**Type:** Reference

All gaps found are minor or already self-documented via XML:

1. `DispatcherDiagnostics.cs` — **Complete.** All 10 counters carry XML docs naming increment site, tag keys, and D3 reference. The class-level doc names both the meter and activity source. No gaps.

2. `StartupValidation.cs` — **Complete.** All five methods (`ValidateAll`, `ValidateObservability`, `ValidateActions`, `ValidateSnapshotProviders`, `ValidateValidators`) carry XML docs with parameter tags and `<exception>` docs on `ValidateAll`. No gaps.

3. `EventPump.cs` and `ChannelActionDispatcher.cs` — XML docs are not expected above individual `StartActivity` call sites (these are inline implementation details, not public or internal API surfaces). The class-level and method-level docs on these files are the correct documentation boundary. No action needed.

4. **D3 tag dimension table** — exists as XML in `DispatcherDiagnostics.cs`. Not duplicated in a separate reference doc. Acceptable at this stage; the XML is sufficient for tooling and future agents reading the file.

---

## Codebase Docs Recommendations

The two files noted in the task brief — `.shipyard/codebase/STACK.md` and `.shipyard/codebase/TESTING.md` — document the **legacy FrigateMQTTProcessingService** (.NET Framework 4.8). They are behavioral reference documents for the old codebase and explicitly scoped to it. Adding FrigateRelay .NET 10 content to them would create confusion by mixing two different codebases in one file. These files should not be modified.

The correct location for FrigateRelay .NET 10 stack and testing conventions is `CLAUDE.md`, where all such conventions are already collected. The two CLAUDE.md additions above cover the actionable Phase 9 lessons.

If a `docs/` directory is created in Phase 11/12 (per ID-9), a `docs/observability.md` (Explanation type) should cover:
- The span tree diagram
- The counter reference table with tag dimensions
- The `ActivityContext` channel-hop propagation pattern
- The Serilog/OTel config key reference (`Serilog:Seq:ServerUrl`, `Otel:OtlpEndpoint`) with the `ValidateObservability` fail-fast contract

---

## Suggested Actions

| Priority | Action | File |
|---|---|---|
| High | Add observability instrumentation convention bullet | `CLAUDE.md` |
| High | Add Worker SDK Serilog wiring + test capture convention bullet | `CLAUDE.md` |
| Deferred (Phase 11/12) | Create `docs/observability.md` with span tree, counter table, config key reference | `docs/observability.md` |
| Deferred (Phase 10) | Fix `Validator_ShortCircuits_OnlyAttachedAction` integration test — register capture provider after `ConfigureServices` | `tests/FrigateRelay.IntegrationTests/` |

All findings are non-blocking.
