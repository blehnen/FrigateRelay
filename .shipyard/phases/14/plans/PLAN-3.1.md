---
phase: 14-v1.2-inference-engines-parallel-validation
plan: 3.1
wave: 3
dependencies: [1.1, 1.2, 2.1, 2.2, 2.3]
must_haves:
  - ParallelValidators bool field on ActionEntry (default false) with full back-compat
  - ParallelValidators bool field on DispatchItem (propagated from ActionEntry by EventPump)
  - ActionEntryJsonConverter + ActionEntryTypeConverter pass through new field
  - All existing 262 tests still pass — no behavioral change when flag is false
files_touched:
  - src/FrigateRelay.Host/Configuration/ActionEntry.cs
  - src/FrigateRelay.Host/Configuration/ActionEntryJsonConverter.cs
  - src/FrigateRelay.Host/Configuration/ActionEntryTypeConverter.cs
  - src/FrigateRelay.Host/Dispatch/DispatchItem.cs
  - src/FrigateRelay.Host/EventPump.cs
tdd: false
risk: low
---

# Plan 3.1: ParallelValidators field on ActionEntry + DispatchItem + converters (PR #23)

## Context

Issue #23 ships a per-action `ParallelValidators: bool` opt-in (CONTEXT-14 D5, OQ-3: `ActionEntry`-only, no per-subscription default). When `false` (default), existing sequential validator chain is unchanged. When `true`, the host runs validators concurrently via `Task.WhenAll` with strict-AND aggregation (CONTEXT-14 D6: no first-reject short-circuit).

This plan lands the **plumbing** — the field on `ActionEntry`, propagation onto `DispatchItem`, JSON + TypeConverter passthrough — without touching the dispatcher's execution loop. The execution-loop branch is PLAN-3.2; the integration test is PLAN-3.3. Splitting the plumbing into its own plan keeps the dispatcher diff in PLAN-3.2 minimal and reviewable.

`ActionEntry` lives at `src/FrigateRelay.Host/Configuration/ActionEntry.cs:30` per RESEARCH §2.5. `DispatchItem` lives at `src/FrigateRelay.Host/Dispatch/DispatchItem.cs:29-36` per RESEARCH §2.2. Both are host-internal (no abstractions impact). The two converters are at `ActionEntryJsonConverter.cs` and `ActionEntryTypeConverter.cs` — both must pass the new field through (CLAUDE.md "Subscriptions:N:Actions accepts both shapes" — JSON path uses `JsonConverter`; `IConfiguration.Bind` path uses `TypeConverter`; both code paths must work).

## Dependencies

- **PLAN-1.1, 1.2** (PR #13 merged) and **PLAN-2.1, 2.2, 2.3** (PR #14 merged). Wave 3 strictly follows Wave 2 per CONTEXT-14 D1 (sequential PRs against `main`). The integration test in PLAN-3.3 will exercise CPAI + Roboflow + DOODS2 in parallel — those plugins must already be on `main`.

## Tasks

### Task 1: Add ParallelValidators field to ActionEntry record + update converters

**Files:**
- `src/FrigateRelay.Host/Configuration/ActionEntry.cs`
- `src/FrigateRelay.Host/Configuration/ActionEntryJsonConverter.cs`
- `src/FrigateRelay.Host/Configuration/ActionEntryTypeConverter.cs`

**Action:** modify

**Description:**

**`ActionEntry.cs`** — extend the record signature per RESEARCH §2.5:
```csharp
internal sealed record ActionEntry(
    string Plugin,
    string? SnapshotProvider = null,
    IReadOnlyList<string>? Validators = null,
    bool ParallelValidators = false);   // NEW for #23 / CONTEXT-14 D5
```

Add an XML doc-comment block on the new parameter:
```xml
/// <param name="ParallelValidators">
/// When <see langword="true"/>, the action's validators run concurrently via
/// <see cref="System.Threading.Tasks.Task.WhenAll(System.Collections.Generic.IEnumerable{System.Threading.Tasks.Task})"/>
/// with strict-AND aggregation: ALL validators must return <c>Verdict.Pass()</c> for the action to fire.
/// First-reject does NOT short-circuit other in-flight validators (CONTEXT-14 D6) — operators
/// get full per-validator visibility on every dispatch via the existing
/// <c>validators.rejected</c> per-validator counter.
/// Default <see langword="false"/> preserves the v1.0 / v1.1 sequential-with-short-circuit behavior.
/// </param>
```

The default-`false` parameter at the end of the positional record means existing call sites that construct `ActionEntry` without specifying `ParallelValidators` continue to compile unchanged (back-compat invariant from CONTEXT-14).

**`ActionEntryJsonConverter.cs`** — update the dual-form `Read` method and the `Write` method:

In `Read` at the `JsonTokenType.StartObject` branch (lines 39-46): the private `ActionEntryDto` record (lines 69-72) gains a `bool ParallelValidators = false` parameter, and the constructor call at line 45 passes it through:
```csharp
return new ActionEntry(dto.Plugin, dto.SnapshotProvider, dto.Validators, dto.ParallelValidators);
```

In `Write` (lines 53-66): emit `ParallelValidators` only when non-default (matches the existing `Validators is { Count: > 0 }` pattern at line 59 — keeps round-trips compact and avoids polluting written JSON with the default value):
```csharp
if (value.ParallelValidators)
    writer.WriteBoolean("ParallelValidators", true);
```

The JSON-string-form branch (`JsonTokenType.String` at lines 32-37) creates `new ActionEntry(plugin)` — `ParallelValidators` defaults to `false` automatically. No change needed there; document the invariant in an inline comment so future readers don't introduce a per-string ParallelValidators encoding.

**`ActionEntryTypeConverter.cs`** — verify NO change is needed. The TypeConverter only converts a scalar string into `new ActionEntry(s)` (line 34). The default-`false` for `ParallelValidators` carries through. Leave the file unchanged but add a one-line inline `// Note:` comment confirming this — future contributors will look here when they touch the JSON converter.

**Acceptance Criteria:**
- `dotnet build FrigateRelay.sln -c Release` succeeds with zero warnings on Linux + Windows.
- The `ActionEntry` record's signature exactly matches: `internal sealed record ActionEntry(string Plugin, string? SnapshotProvider = null, IReadOnlyList<string>? Validators = null, bool ParallelValidators = false);`.
- Existing tests pass without modification: `dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build` exits 0 (the back-compat invariant — no test-side adjustments allowed).
- Round-trip test (add to `tests/FrigateRelay.Host.Tests` if a corresponding ActionEntry-converter test class exists; otherwise piggyback on the next phase): `JsonSerializer.Serialize(new ActionEntry("X", ParallelValidators: true))` includes `"ParallelValidators":true`; deserializing it back yields a record with `ParallelValidators == true`.
- Round-trip with default flag: `JsonSerializer.Serialize(new ActionEntry("X"))` does NOT include the `"ParallelValidators"` field (matches existing terseness for absent `SnapshotProvider`/`Validators`).

---

### Task 2: Add ParallelValidators field to DispatchItem + propagate from EventPump

**Files:**
- `src/FrigateRelay.Host/Dispatch/DispatchItem.cs`
- `src/FrigateRelay.Host/EventPump.cs`

**Action:** modify

**Description:**

**`DispatchItem.cs`** — extend the readonly-record-struct signature per RESEARCH §2.2:
```csharp
internal readonly record struct DispatchItem(
    EventContext Context,
    IActionPlugin Plugin,
    IReadOnlyList<IValidationPlugin> Validators,
    ActivityContext ParentContext,
    string Subscription = "",
    string? PerActionSnapshotProvider = null,
    string? SubscriptionSnapshotProvider = null,
    bool ParallelValidators = false);   // NEW for #23
```

Add an XML doc-comment paragraph on the new parameter mirroring the `ActionEntry` doc-comment from Task 1, and noting that `EventPump` populates this from `ActionEntry.ParallelValidators` at construction time. Default `false` keeps all existing `new DispatchItem(...)` call sites valid.

**`EventPump.cs`** — find every `new DispatchItem(` call site by running `git grep -n 'new DispatchItem' src/FrigateRelay.Host/` at execution time (RESEARCH §2.2 explicit guidance). At each construction site that has access to the originating `ActionEntry`, pass `action.ParallelValidators` to the new positional parameter.

Expected sites: typically one or two construction calls in `EventPump.cs` where an `ActionEntry` (variable name likely `action`) is matched and a `DispatchItem` is built. The construction is parameterized over `Plugin`, `Validators`, `Subscription`, `PerActionSnapshotProvider`, `SubscriptionSnapshotProvider`. Append `ParallelValidators: action.ParallelValidators` (named argument — the field is the new last positional, but using a named argument is more readable and avoids accidentally swapping arguments if a future refactor reorders).

If `git grep` reveals construction sites OUTSIDE `EventPump.cs` (e.g. test-only construction in `tests/FrigateRelay.Host.Tests`), DO NOT modify those — the default `false` parameter keeps test-side construction working unchanged. The plumbing is host-internal; tests for parallel mode are written explicitly in PLAN-3.2.

**Acceptance Criteria:**
- `dotnet build FrigateRelay.sln -c Release` succeeds with zero warnings.
- `git grep -n 'new DispatchItem' src/FrigateRelay.Host/EventPump.cs` shows EVERY production-code site passing `ParallelValidators: <something>` (likely `action.ParallelValidators`).
- All existing `EventPumpTests` pass: `dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build -- --filter "EventPump"` exits 0.
- All existing dispatcher tests still pass: `dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build -- --filter "ChannelActionDispatcher"` exits 0 (the dispatcher still runs the sequential path because `item.ParallelValidators` is `false` for all existing test inputs).
- `git grep -n 'ParallelValidators' src/FrigateRelay.Host/Dispatch/DispatchItem.cs` returns at least 2 matches (declaration + doc-comment).

## Verification

```bash
# Build clean
dotnet build FrigateRelay.sln -c Release

# Existing tests still pass — back-compat invariant (no behavioral change when flag is false)
dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build
dotnet run --project tests/FrigateRelay.Plugins.CodeProjectAi.Tests -c Release --no-build
dotnet run --project tests/FrigateRelay.Plugins.Roboflow.Tests -c Release --no-build
dotnet run --project tests/FrigateRelay.Plugins.Doods2.Tests -c Release --no-build

# Cumulative test count unchanged from end of Wave 2 (262 minimum)
TOTAL=$(bash .github/scripts/run-tests.sh --skip-integration 2>&1 | grep -E '^total:' | awk '{ sum += $2 } END { print sum }')
[ "$TOTAL" -ge 262 ] || { echo "test count regression: $TOTAL < 262"; exit 1; }

# Field is present on both types
git grep -n 'ParallelValidators' src/FrigateRelay.Host/Configuration/ActionEntry.cs   # must have at least 2 matches
git grep -n 'ParallelValidators' src/FrigateRelay.Host/Dispatch/DispatchItem.cs       # must have at least 2 matches

# JSON converter passes the field through
git grep -n 'ParallelValidators' src/FrigateRelay.Host/Configuration/ActionEntryJsonConverter.cs   # must have at least 2 matches (Read DTO + Write)

# EventPump propagates ActionEntry.ParallelValidators → DispatchItem.ParallelValidators
git grep -n 'ParallelValidators' src/FrigateRelay.Host/EventPump.cs                   # must have at least 1 match

# Architectural invariants
git grep -nE 'App\.Metrics|OpenTracing|Jaeger\.' src/                # must be empty
git grep -nE '\.(Result|Wait)\(' src/                                 # must be empty
```
