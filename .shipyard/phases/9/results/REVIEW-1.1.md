# Review: Plan 1.1

## Verdict: PASS

## Findings

### Critical
None.

### Minor
None.

### Positive

- **Package references** (`Host.csproj` lines 24–34): exact 8-package set at exact RESEARCH.md-confirmed pinned versions (OTel core/OTLP/Runtime at 1.15.3/1.15.3/1.15.1; Serilog Extensions.Hosting/Settings.Configuration at 10.0.0; Sinks.Console 6.1.1, Sinks.File 7.0.0, Sinks.Seq 9.0.0). Forbidden packages (`Serilog.AspNetCore`, `OpenTelemetry.Exporter.Console`, `App.Metrics`, `OpenTracing`, `Jaeger.*`) absent.
- **Counter surface** (`DispatcherDiagnostics.cs`): all 8 new `Counter<long>` declarations match ROADMAP-exact names verbatim (`frigaterelay.events.received` / `events.matched` / `actions.dispatched` / `actions.succeeded` / `actions.failed` / `validators.passed` / `validators.rejected` / `errors.unhandled`). Pattern matches existing `Drops`/`Exhausted` (`internal static readonly`, inline `Meter.CreateCounter<long>`). XML docs name D3 tag keys per counter; `ErrorsUnhandled` correctly tagless per D9.
- **`DispatchItem` flip** (`DispatchItem.cs:29`): `ActivityContext ParentContext` replaces `Activity? Activity`. Producer side captures `Activity.Current?.Context ?? default` (`ChannelActionDispatcher.cs:150`); consumer side uses `item.ParentContext` + `ActivityKind.Consumer` (`ChannelActionDispatcher.cs:173–176`). No dangling `item.Activity` references.
- **Convention compliance**: all new types `internal static readonly`; no `public` introduced; no source-level `[InternalsVisibleTo]`; no `.Result`/`.Wait()`; no `ServicePointManager`; no hard-coded IPs. FluentAssertions still pinned 6.12.2 (no upgrade attempt).
- **ID-6 correctly deferred**: `OperationCanceledException` → `ActivityStatusCode.Error` at `ChannelActionDispatcher.cs:238` intentionally unmodified per plan; PLAN-2.1 owns the fix.
- **Span name `"ActionDispatch"` deferred**: rename to `"dispatch.enqueue"` reserved for PLAN-2.1 per plan instruction. Avoids file-conflict with PLAN-2.1.
- **Build & tests**: clean at every commit; 69/69 tests pass (foundation is non-test-affecting as expected).
- **Public surface guard**: `git grep -RnE '^public (sealed )?(class|record|interface) ' src/FrigateRelay.Host/` returns zero matches (Phase 8 invariant preserved).

## Summary
Critical: 0 | Minor: 0 | Suggestions: 0. Approved — Wave 2 (PLAN-2.1 + PLAN-2.2) cleared to dispatch in parallel.
