# Build Summary: Plan 1.1 — Diagnostics Foundation

## Status: complete

Build clean (0 warnings / 0 errors), 69/69 tests passing before and after.

## Tasks Completed

| Task | Description | Commit |
|---|---|---|
| Task 1 | Add OpenTelemetry + Serilog package references to Host csproj | `32704a6` |
| Task 2 | Extend `DispatcherDiagnostics` with Phase 9 counter set (D3) | `277ef64` |
| Task 3 | Flip `DispatchItem.Activity` → `ActivityContext ParentContext` (D1) | `26f6c2a` |

## Files Modified

- `src/FrigateRelay.Host/FrigateRelay.Host.csproj` — 8 new `PackageReference` items (5 OTel + 3 Serilog) at exact pinned versions per RESEARCH.md.
- `src/FrigateRelay.Host/Dispatch/DispatcherDiagnostics.cs` — 8 new `Counter<long>` fields (total now 10). Existing `Drops` and `Exhausted` retained.
- `src/FrigateRelay.Host/Dispatch/DispatchItem.cs` — positional record-struct parameter `Activity? Activity` → `ActivityContext ParentContext` (line 29).
- `src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs` — write site uses `Activity.Current?.Context ?? default`; read site uses `item.ParentContext` + `ActivityKind.Consumer` for span parenting.

## Decisions Made

- **Package versions:** Exact as PLAN-1.1 specified (RESEARCH.md confirmed 2026-04-27). No deviation.
- **Counter style:** Matched existing `Drops` / `Exhausted` pattern — `internal static readonly Counter<long>` inline-initialized from `Meter.CreateCounter<long>()`. XML docs name the tag keys per D3 and reference the increment-site file (no line numbers — PLAN-2.1 owns the actual `.Add()` calls).
- **`ActivityKind.Consumer`** — changed from `ActivityKind.Internal` per D1 (the dispatch span semantically consumes from a channel). Span name `"ActionDispatch"` left untouched — PLAN-2.1 owns the rename to `"dispatch.enqueue"`.

## Issues Encountered

- None substantive. `DispatchItem` is a positional `record struct`, so renaming the parameter automatically renames the property; no callers needed updating beyond the two explicit `ChannelActionDispatcher.cs` sites.
- `default(ActivityContext)` correctly produces a root span on the consumer side when `Activity.Current` was null at producer time — no special-casing needed. The `ActivityContext.IsValid` check inside `ActivitySource.StartActivity` handles the empty-context case as "start a new root."

## Verification Results

- **Build:** Clean at every commit (0 warnings, 0 errors).
- **Tests:** 69/69 passing baseline; 69/69 passing after all three tasks.

**Acceptance criteria confirmed:**
- 8 new packages at exact pinned versions; zero matches for excluded `App.Metrics` / `OpenTracing` / `Jaeger.*` / `Serilog.AspNetCore` / `OpenTelemetry.Exporter.Console`.
- `CreateCounter<long>` count = 10 in `DispatcherDiagnostics.cs`; 8 new counters added; zero `public` members in `DispatcherDiagnostics`.
- `Activity? Activity` in `DispatchItem.cs`: 0 matches; `ActivityContext ParentContext`: 1 match (line 29).
- `item.Activity` in `ChannelActionDispatcher`: 0 matches; `item.ParentContext`: 1 match.
- Producer-side capture pattern `Activity.Current?.Context ?? default`: 1 match.

## Wave 2 Readiness

PLAN-2.1 and PLAN-2.2 may proceed in parallel:
- Full counter + ActivitySource surface available in `DispatcherDiagnostics`.
- `DispatchItem.ParentContext` is the `ActivityContext` struct PLAN-2.1 expects.
- All 8 OTel + Serilog packages resolved; `dotnet restore` clean.
