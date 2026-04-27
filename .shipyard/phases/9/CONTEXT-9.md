# CONTEXT-9 — Phase 9 Discussion Decisions

**Phase.** 9 — Observability
**Status.** Decisions captured (2026-04-27). Authoritative input for the researcher and architect.

This document records user decisions on the gray areas surfaced before plan generation. Downstream agents (researcher, architect, builder, reviewer) MUST treat these as binding. Any deviation is a regression and must be surfaced as a question, not silently chosen otherwise.

---

## D1 — Activity propagation across the `Channel<T>` hop uses `ActivityContext` struct

**Decision.** `DispatchItem` gains a field of type `System.Diagnostics.ActivityContext` (NOT `Activity?`) to carry the parent trace context across the channel boundary.

**Implementation pattern.**
- Producer side (`EventPump.PumpAsync` after match): `var parentContext = Activity.Current?.Context ?? default;` and store on the `DispatchItem`.
- Consumer side (`ChannelActionDispatcher.ConsumeAsync`): `using var dispatchActivity = ActivitySource.StartActivity("dispatch.enqueue", ActivityKind.Consumer, item.ParentContext);`. The new activity is parented to the captured trace context regardless of whether `Activity.Current` exists on the consumer thread.

**Why this option.** `ActivityContext` is a 16-byte readonly struct (TraceId, SpanId, TraceFlags, TraceState). Designed for exactly this scenario — crossing async/queue boundaries without keeping the parent `Activity` object alive. No GC pressure, no risk of `Activity.Stop()` being called on a parent referenced by an in-flight dispatch item, no disposal-ordering surprises. `Activity?` would also work but introduces lifetime coupling and reference retention through the channel.

**Implication.** If `Activity.Current` is null at the producer site (e.g. tests with no active TracerProvider), `default(ActivityContext)` is captured and `StartActivity` produces a root span on the consumer side — which is fine, traces remain valid, just disconnected.

---

## D2 — OTel registers but emits no exporter when `OTEL_EXPORTER_OTLP_ENDPOINT` is unset

**Decision.** `OpenTelemetry.Sdk.CreateTracerProviderBuilder()` and `MeterProviderBuilder` always register the `ActivitySource` and `Meter`, but only call `AddOtlpExporter(...)` when the endpoint env var is set (or `Otel:OtlpEndpoint` configuration key is non-empty).

**Implementation pattern.**
- In `HostBootstrap.ConfigureServices`: read `Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")` (also accept `Otel:OtlpEndpoint` from `IConfiguration` for testability).
- If endpoint is non-empty, call `.AddOtlpExporter()` (uses default conventions for protocol + URL); if empty, skip the exporter call.
- ActivitySource and Meter remain wired regardless — they emit, but no exporter consumes.

**Why this option.** Unit tests use an in-memory exporter (`InMemoryExporter` from `OpenTelemetry.Exporter.InMemory`) that is wired separately in test fixtures, so they never need OTLP. Local dev with no Jaeger/Tempo running gets zero noise. Production registers the env var via Docker compose / k8s and gets real export. Console-fallback rejected (would conflate observability output with operational logs).

**Implication.** Operators who enable OTel via env var but mistype the endpoint will get silent failures (the OTLP exporter logs its own connection failures via the OTel SDK's internal logging). Acceptable trade-off for the ergonomics; document in the eventual operator-facing README.

---

## D3 — Counter tags: `subscription`, `action`, `validator`, `camera`, `label`

**Decision.** Each counter carries the dimensional tags appropriate to its semantics:

| Counter | Tags |
|---|---|
| `frigaterelay.events.received` | `camera`, `label` |
| `frigaterelay.events.matched` | `camera`, `label`, `subscription` |
| `frigaterelay.actions.dispatched` | `subscription`, `action` |
| `frigaterelay.actions.succeeded` | `subscription`, `action` |
| `frigaterelay.actions.failed` | `subscription`, `action` |
| `frigaterelay.validators.passed` | `subscription`, `action`, `validator` |
| `frigaterelay.validators.rejected` | `subscription`, `action`, `validator` |
| `frigaterelay.errors.unhandled` | (none — single series) |

**Why this option.** The author's deployment has ~9 subscriptions × ~3 actions × ~few validators × ~8 cameras × 2 labels = bounded cardinality (low thousands of series). Drilldown into "which camera misfires most" or "which validator rejects the most events" is a real operational need — flat counters force everything into traces. `errors.unhandled` is intentionally tagless so it's a single alarm-able series.

**Tag value source.** `subscription` from `SubscriptionOptions.Name`; `action` from `ActionEntry.Plugin`; `validator` from the named validator key; `camera` and `label` from `EventContext.Camera` / `EventContext.Label` (already source-agnostic per existing invariant).

**Implication.** Operators must keep subscription/profile/validator names short and stable — renaming explodes time series. Same operational discipline as Prometheus-style label conventions.

---

## D4 — ID-6 (OperationCanceledException → ActivityStatusCode.Error) bundled into Phase 9

**Decision.** While Phase 9 instruments the dispatcher with `Activity` lifecycle calls, fix the existing `ChannelActionDispatcher.ConsumeAsync` bug where `OperationCanceledException` (graceful shutdown) sets `ActivityStatusCode.Error`. Change to `ActivityStatusCode.Unset` (or `Ok` if a span was started and completed) when the cancellation token signals shutdown.

**Implementation pattern.**
- In the catch block for `OperationCanceledException`, check `ct.IsCancellationRequested`. If true, set `activity?.SetStatus(ActivityStatusCode.Unset)` (do not record exception, do not log Error). If false (unexpected cancellation), keep current behavior.

**Why this option.** Phase 9 is already touching every Activity touchpoint in the dispatcher. Bundling the one-line semantics fix is cheaper than a separate phase. Clean OTel traces during normal shutdown pay dividends in production monitoring.

**Implication.** Closes ID-6 without expanding scope. The fix arrives with new test coverage from the Phase 9 trace-shape assertions.

---

## D5 — Tests split: unit in `Host.Tests/Observability/`, integration in `IntegrationTests/Observability/`

**Decision.**
- **Unit tests** (`tests/FrigateRelay.Host.Tests/Observability/`): `ActivitySource` registration, individual span attribute shape, `Meter` registration, counter increment via `MeterListener`. Stub `IEventSource` feeding `EventPump` directly; no Mosquitto.
- **Integration tests** (`tests/FrigateRelay.IntegrationTests/Observability/`): `TraceSpans_CoverFullPipeline` with real Mosquitto (Testcontainers), WireMock-stubbed Blue Iris/Pushover/CodeProject.AI, in-memory `BatchActivityExportProcessor` exporter. Asserts span tree shape: 1 root `mqtt.receive` per published event with the expected child chain.

**Why this option.** Mirrors the Phase 6/7 split (Host.Tests for unit, IntegrationTests for end-to-end). The full-pipeline assertion only makes sense with the real `Channel<T>` boundary, so it must live in IntegrationTests. Counter-increment tests don't need Mosquitto and run faster as units.

**Implication.** New test directory in IntegrationTests. CI workflow does NOT need updating — `run-tests.sh` already globs `tests/*.Tests/*.csproj`.

---

## D6 — Keep hand-rolled `Action<ILogger,...>` delegates; new logging sites match

**Decision.** Do NOT migrate existing high-perf logging delegates (see `ChannelActionDispatcher.cs`, `EventPump.cs`, `SnapshotResolver.cs`, etc.) to `[LoggerMessage]` source generator partial methods. New logging sites added in Phase 9 use the same hand-rolled `Action<ILogger,...>` pattern for consistency.

**Why this option.** The existing pattern works, is allocation-free, and is widely understood. The `[LoggerMessage]` source generator is mechanically equivalent but introduces codegen surprises (event ID conflicts, partial-method visibility quirks). Mixed-pattern codebases age badly. A migration would be churn-only with zero functional benefit.

**Implication.** Phase 11/12 polish phase MAY revisit if `[LoggerMessage]` becomes the dominant idiom in the wider .NET ecosystem.

---

## D7 — Serilog Seq sink: included now, conditionally registered

**Decision.** Add `Serilog.Sinks.Seq` PackageReference. Wire registration via `Serilog:Seq:ServerUrl` config key — only call `loggerConfiguration.WriteTo.Seq(serverUrl)` when the value is non-empty.

**Implementation pattern.**
- `appsettings.json` ships with `Serilog:Seq:ServerUrl: ""` (empty default).
- `appsettings.Example.json` documents the key with a comment.
- `HostBootstrap.ConfigureLogging` checks `IConfiguration.GetValue<string>("Serilog:Seq:ServerUrl")` and conditionally registers the sink.

**Why this option.** Operators get free Seq integration when they want it; CI and local dev pay zero cost (no network calls, no extra log volume). Including the package now avoids a dependency-bump phase later.

**Implication.** Adds one PackageReference to `FrigateRelay.Host.csproj`. License (Apache 2.0) is MIT-compatible per project policy.

---

## D8 — Span attributes (architect to detail in plan)

**Decision (architect-recommended, finalized in PLAN-1.x).** Each span should carry attributes appropriate to its scope. Architect should propose the exact set per span in PLAN-1.x; expected starting point:

| Span | Attributes |
|---|---|
| `mqtt.receive` | `mqtt.topic`, `event.id` |
| `event.match` | `event.id`, `camera`, `label`, `subscription_count_matched` |
| `dispatch.enqueue` | `subscription`, `action_count`, `event.id` |
| `action.<name>.execute` | `action`, `subscription`, `event.id`, `outcome` (success/failure) |
| `validator.<name>.check` | `validator`, `action`, `subscription`, `verdict` (pass/fail), `event.id` |

**Why deferred.** Span attribute design is more cleanly handled by the architect with full context of the existing `EventContext`/`DispatchItem` shapes. This entry seeds the architect with reasonable defaults and the constraint that every span should be queryable by `event.id` for join-by-correlation in Tempo/Jaeger.

---

## D9 — `errors.unhandled` increment site

**Decision.** Increment `frigaterelay.errors.unhandled` from a single top-level catch in the host (likely `EventPump.PumpAsync`'s outermost handler and `ChannelActionDispatcher.ConsumeAsync`'s outermost handler), NOT from per-plugin catch blocks. Plugin-level retry-exhausted failures already increment `actions.failed` — `errors.unhandled` is reserved for unexpected exceptions that escape the pipeline's normal error handling.

**Why this option.** Avoids double-counting. `errors.unhandled` should be the "something is genuinely wrong" alarmable signal, not the "an action retry exhausted" expected-but-unfortunate signal.

---

## Out of scope (deferred)

- **`LoggerMessage` source generator migration** (D6 — keep existing).
- **Trace sampling** — phase 9 uses `AlwaysOnSampler`. Sampling configuration deferred to operator-docs phase.
- **Custom log enrichment via Serilog enrichers** — defaults are sufficient for v1 (machine name, thread id, source context).
- **Dashboards / Grafana provisioning** — operator-facing artifact, deferred to Phase 11/12.
- **Auditor advisories ID-13 (newline sanitization), ID-14 (whitespace plugin name), ID-15 (secret-scan RFC 1918 A/B)** — not Phase 9 scope.
