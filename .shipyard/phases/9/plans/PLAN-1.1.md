---
phase: phase-9-observability
plan: 1.1
wave: 1
dependencies: []
must_haves:
  - Host csproj gains OpenTelemetry + Serilog package references at versions verified by RESEARCH.md
  - DispatcherDiagnostics.cs is extended with the full Phase 9 ActivitySource + Meter + Counter set (D3 names/tags)
  - DispatchItem.Activity (Activity?) is replaced with ParentContext (ActivityContext) per D1
  - All new types remain internal (Phase 8 visibility invariant); zero public types added in src/FrigateRelay.Host/
files_touched:
  - src/FrigateRelay.Host/FrigateRelay.Host.csproj
  - src/FrigateRelay.Host/Dispatch/DispatcherDiagnostics.cs
  - src/FrigateRelay.Host/Dispatch/DispatchItem.cs
  - src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs
tdd: false
risk: medium
---

# Plan 1.1: Diagnostics Foundation — Packages, Counter Surface, ActivityContext Flip

## Context

This plan lays the foundation that PLAN-2.1 (instrumentation sites) and PLAN-2.2 (Serilog/OTel host wiring) consume. It implements three CONTEXT-9 decisions whose source-of-truth is shared state: package additions (D2/D7), the consolidated counter set (D3), and the `DispatchItem` field flip (D1). Bundling these into one plan keeps the breaking change to `DispatchItem` co-located with the call-site update in `ChannelActionDispatcher`, so the build never sees an inconsistent intermediate state.

ID-6 (D4) is fixed in PLAN-2.1, NOT here — that change is logically a producer/consumer-instrumentation concern and rides with the new Activity lifecycle calls.

The existing `DispatcherDiagnostics.cs` already declares `Meter`, `ActivitySource`, `Drops`, and `Exhausted` (verified at `src/FrigateRelay.Host/Dispatch/DispatcherDiagnostics.cs:11-37`). This plan extends — does not replace — that file. RESEARCH.md's claim that the file is missing is incorrect; verified at the start of Phase 9.

## Dependencies

None (Wave 1).

## Tasks

### Task 1: Add OpenTelemetry + Serilog package references to Host csproj
**Files:** `src/FrigateRelay.Host/FrigateRelay.Host.csproj`
**Action:** modify
**Description:**
Append the following `<PackageReference>` items inside the existing `<ItemGroup>` containing runtime package references (NOT inside the `InternalsVisibleTo` ItemGroup). Versions are verified stable on .NET 10 per RESEARCH.md §5–§6.

```xml
<!-- OpenTelemetry (D2 — registration always; export conditional on Otel:OtlpEndpoint / OTEL_EXPORTER_OTLP_ENDPOINT) -->
<PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.15.3" />
<PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.15.3" />
<PackageReference Include="OpenTelemetry.Instrumentation.Runtime" Version="1.15.1" />

<!-- Serilog Worker SDK host wiring (D7 — Seq is conditional on Serilog:Seq:ServerUrl) -->
<PackageReference Include="Serilog.Extensions.Hosting" Version="10.0.0" />
<PackageReference Include="Serilog.Settings.Configuration" Version="10.0.0" />
<PackageReference Include="Serilog.Sinks.Console" Version="6.1.1" />
<PackageReference Include="Serilog.Sinks.File" Version="7.0.0" />
<PackageReference Include="Serilog.Sinks.Seq" Version="9.0.0" />
```

Hard rules:
- Do NOT add `OpenTelemetry.Exporter.Console` (D2 excluded).
- Do NOT add `OpenTelemetry.Instrumentation.Process` (still beta per RESEARCH.md §5; not in Phase 9 scope).
- Do NOT add `OpenTelemetry.Exporter.InMemory` here — that goes in test csprojs only (PLAN-3.1).
- Do NOT add `Serilog.AspNetCore` — Worker SDK uses `Serilog.Extensions.Hosting`.

**Acceptance Criteria:**
- `dotnet build src/FrigateRelay.Host/FrigateRelay.Host.csproj -c Release` succeeds with zero warnings.
- `dotnet list src/FrigateRelay.Host/FrigateRelay.Host.csproj package` shows all 8 new packages at the exact pinned versions.
- `git grep -nE 'App\.Metrics|OpenTracing|Jaeger\.|Serilog\.AspNetCore|OpenTelemetry\.Exporter\.Console' src/` returns zero matches.

### Task 2: Extend DispatcherDiagnostics with the Phase 9 counter set (D3)
**Files:** `src/FrigateRelay.Host/Dispatch/DispatcherDiagnostics.cs`
**Action:** modify
**Description:**
Add eight new `internal static readonly Counter<long>` declarations to the existing `DispatcherDiagnostics` class. Names and tag dimensions are FROZEN per CONTEXT-9 D3:

| Counter field | Counter name | Tag keys (emitted at Add-site, not declared here) |
|---|---|---|
| `EventsReceived` | `frigaterelay.events.received` | `camera`, `label` |
| `EventsMatched` | `frigaterelay.events.matched` | `camera`, `label`, `subscription` |
| `ActionsDispatched` | `frigaterelay.actions.dispatched` | `subscription`, `action` |
| `ActionsSucceeded` | `frigaterelay.actions.succeeded` | `subscription`, `action` |
| `ActionsFailed` | `frigaterelay.actions.failed` | `subscription`, `action` |
| `ValidatorsPassed` | `frigaterelay.validators.passed` | `subscription`, `action`, `validator` |
| `ValidatorsRejected` | `frigaterelay.validators.rejected` | `subscription`, `action`, `validator` |
| `ErrorsUnhandled` | `frigaterelay.errors.unhandled` | (none — D3 single tagless series) |

Each declaration follows the existing `Drops`/`Exhausted` pattern (verified at lines 27–37):
```csharp
/// <summary>
/// Incremented at <FILE:LINE-RANGE> when ... .
/// Tagged with <c>camera</c>, <c>label</c> per CONTEXT-9 D3.
/// </summary>
internal static readonly Counter<long> EventsReceived =
    Meter.CreateCounter<long>("frigaterelay.events.received");
```

Each XML doc summary MUST name the tag keys per D3 (so a reader of `DispatcherDiagnostics.cs` alone can audit tag conformance without cross-referencing CONTEXT-9). Increment-site file references can read "Incremented by EventPump.PumpAsync" / "Incremented by ChannelActionDispatcher.ConsumeAsync" — exact line numbers come in PLAN-2.1 and don't need backfilling here.

Do NOT rename the class to `FrigateRelayDiagnostics` (RESEARCH.md §7 suggests this — REJECTED, since the existing name is already wired into `ChannelActionDispatcher.cs` and a rename is churn-only). Keep `DispatcherDiagnostics`.

The `Meter` and `ActivitySource` instances at lines 16/21 are reused unchanged — single Meter named `"FrigateRelay"`, single ActivitySource named `"FrigateRelay"` (CLAUDE.md observability invariant).

**Acceptance Criteria:**
- `dotnet build src/FrigateRelay.Host -c Release` is clean.
- `grep -c 'CreateCounter<long>' src/FrigateRelay.Host/Dispatch/DispatcherDiagnostics.cs` returns `10` (2 pre-existing + 8 new).
- `grep -nE '^internal static readonly Counter<long> (EventsReceived|EventsMatched|ActionsDispatched|ActionsSucceeded|ActionsFailed|ValidatorsPassed|ValidatorsRejected|ErrorsUnhandled)' src/FrigateRelay.Host/Dispatch/DispatcherDiagnostics.cs | wc -l` returns `8`.
- `grep -E '^(public|protected) ' src/FrigateRelay.Host/Dispatch/DispatcherDiagnostics.cs` returns zero (Phase 8 visibility invariant: no `public` types in Host).

### Task 3: Flip DispatchItem.Activity to DispatchItem.ParentContext + update call sites
**Files:** `src/FrigateRelay.Host/Dispatch/DispatchItem.cs`, `src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs`
**Action:** modify
**Description:**
Per CONTEXT-9 D1 the channel-hop trace context must travel as a 16-byte `ActivityContext` struct, not an `Activity?` reference. This eliminates parent-Activity lifetime coupling across the channel.

Step 3a — `DispatchItem.cs` (currently lines 25–31):
- Replace positional parameter `Activity? Activity` with `ActivityContext ParentContext`.
- Update XML doc on the parameter to: `/// <param name="ParentContext">The captured trace context of the producing Activity (per CONTEXT-9 D1). Default = no parent (root span on consumer).</param>`
- Keep `using System.Diagnostics;` — `ActivityContext` lives there.

Step 3b — `ChannelActionDispatcher.cs` write site (RESEARCH.md §3 cites line 150 for `EnqueueAsync`):
- Change `new DispatchItem(ctx, action, validators, Activity.Current, ...)` to `new DispatchItem(ctx, action, validators, Activity.Current?.Context ?? default, ...)`.

Step 3c — `ChannelActionDispatcher.cs` read site (RESEARCH.md §3 cites lines 173–176 for `ConsumeAsync`):
- Change `parentContext: item.Activity?.Context ?? default` to `parentContext: item.ParentContext`.
- Per D1 also change `ActivityKind.Internal` to `ActivityKind.Consumer` on this `StartActivity` call (the dispatch span is semantically a consumer span — it consumes from the channel).
- Span name `"ActionDispatch"` is REPLACED at this site by `"dispatch.enqueue"` per CONTEXT-9 §D8 / ROADMAP Phase 9 span naming. Note: this rename is a behavior change in PLAN-2.1's span work, NOT here; in this plan, leave the span name `"ActionDispatch"` untouched. Only change the parent-context source and the `ActivityKind`.

**Acceptance Criteria:**
- `grep -nE 'Activity\?\s+Activity' src/FrigateRelay.Host/Dispatch/DispatchItem.cs` returns zero.
- `grep -nE 'ActivityContext\s+ParentContext' src/FrigateRelay.Host/Dispatch/DispatchItem.cs` returns one match.
- `grep -nE 'item\.Activity\b' src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs` returns zero (no remaining old-shape references).
- `grep -n 'item.ParentContext' src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs` returns at least one match.
- `grep -n 'Activity.Current?.Context ?? default' src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs` returns at least one match (write site).
- `dotnet build FrigateRelay.sln -c Release` is clean (zero warnings as errors).
- Existing dispatcher unit tests in `tests/FrigateRelay.Host.Tests/Dispatch/` continue to pass: `dotnet run --project tests/FrigateRelay.Host.Tests -c Release` reports zero failures.

## Verification

```bash
# Build clean
dotnet build /mnt/f/git/frigaterelay/FrigateRelay.sln -c Release 2>&1 | tail -5

# Package versions pinned
dotnet list /mnt/f/git/frigaterelay/src/FrigateRelay.Host/FrigateRelay.Host.csproj package | grep -E 'OpenTelemetry|Serilog'

# Counter surface complete
grep -c 'CreateCounter<long>' /mnt/f/git/frigaterelay/src/FrigateRelay.Host/Dispatch/DispatcherDiagnostics.cs
# expect: 10

# DispatchItem flipped
grep -n 'ActivityContext ParentContext' /mnt/f/git/frigaterelay/src/FrigateRelay.Host/Dispatch/DispatchItem.cs
grep -n 'Activity? Activity' /mnt/f/git/frigaterelay/src/FrigateRelay.Host/Dispatch/DispatchItem.cs
# expect first match, zero second matches

# Excluded packages stay excluded
git grep -nE 'App\.Metrics|OpenTracing|Jaeger\.|Serilog\.AspNetCore|OpenTelemetry\.Exporter\.Console' /mnt/f/git/frigaterelay/src/
# expect: zero matches

# Visibility invariant (Phase 8 sweep)
grep -E '^(public|protected) ' /mnt/f/git/frigaterelay/src/FrigateRelay.Host/Dispatch/DispatcherDiagnostics.cs /mnt/f/git/frigaterelay/src/FrigateRelay.Host/Dispatch/DispatchItem.cs
# expect: zero matches

# Existing tests still green
cd /mnt/f/git/frigaterelay && dotnet run --project tests/FrigateRelay.Host.Tests -c Release 2>&1 | tail -5
```
