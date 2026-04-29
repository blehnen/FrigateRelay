---
phase: phase-8-profiles
plan: 1.2
wave: 1
dependencies:
  - PLAN-1.1 (commit-order only — CS0053 cascade; see Dependencies section)
must_haves:
  - ActionEntry type internalized and decorated with [TypeConverter]
  - ActionEntryTypeConverter created with CanConvertFrom(string) + ConvertFrom(string)
  - ID-12 closed at the binder layer: Actions:["BlueIris"] no longer silently drops
  - >= 3 unit tests proving string, object, and mixed-array binds via IConfiguration.Bind
files_touched:
  - src/FrigateRelay.Host/Configuration/ActionEntry.cs
  - src/FrigateRelay.Host/Configuration/ActionEntryTypeConverter.cs
  - tests/FrigateRelay.Host.Tests/Configuration/ActionEntryTypeConverterTests.cs
tdd: true
risk: low
---

# Plan 1.2: ActionEntry TypeConverter (ID-12 Fix)

## Context

Per **D2**, ID-12 (`Actions: ["BlueIris"]` silently dropped under `IConfiguration.Bind`) is fixed in Phase 8 by introducing an `ActionEntryTypeConverter` and decorating the `ActionEntry` record with `[TypeConverter(typeof(ActionEntryTypeConverter))]`. The root cause (RESEARCH.md §5) is that `ConfigurationBinder` calls `TypeDescriptor.GetConverter(typeof(ActionEntry))` for scalar paths and never consults `[JsonConverter]`. Once a `TypeConverter` is registered on the type, the binder converts the scalar string into a populated `ActionEntry` via `new ActionEntry(stringValue)`.

This plan also internalizes `ActionEntry` itself per **D8**. The visibility flip is grouped with the attribute addition because both edit the same file — sequencing them as separate plans would force serialization. PLAN-1.1 deliberately does not touch `ActionEntry.cs` so Wave 1 plans have disjoint file sets.

The existing `ActionEntryJsonConverter` is **not** modified here (RESEARCH.md §5 — both converters operate on disjoint paths and both are needed).

## Dependencies

**Commit-order dependency on PLAN-1.1** — CS0053 cascade.

PLAN-1.1 flips `SubscriptionOptions` to `internal` while leaving `ActionEntry` `public`. PLAN-1.2 flips `ActionEntry` to `internal`. The two plans touch disjoint files (no merge conflict), but the **commit order matters** for build greenness:

- **PLAN-1.1 commits first** (recommended) → `SubscriptionOptions: internal` referencing `ActionEntry: public` is legal. Build stays green between commits. PLAN-1.2 then internalizes `ActionEntry` — still green.
- **PLAN-1.2 commits first** → `ActionEntry: internal` referenced by `SubscriptionOptions: public IReadOnlyList<ActionEntry>` triggers **CS0053** ("Inconsistent accessibility: property type is less accessible than property"). Build breaks until PLAN-1.1 lands.

**Builder must enforce one of:**
1. Land PLAN-1.1 first, then PLAN-1.2 (separate commits, sequential).
2. Squash both plans into a single commit so the intermediate state is never observable to CI.

Wave 1 plans CAN execute in parallel by different builders working on disjoint files; the constraint is on commit/merge ordering, not on edit ordering. Verify the build is green after each commit lands on the integration branch.

## Tasks

### Task 1: Add `ActionEntryTypeConverterTests` (TDD red phase)
**Files:**
- `tests/FrigateRelay.Host.Tests/Configuration/ActionEntryTypeConverterTests.cs` (new)

**Action:** create
**TDD:** true (test written before the converter exists; first run must fail)

**Description:**
Create three tests under the class `ActionEntryTypeConverterTests`, each loading an in-memory JSON config via `ConfigurationBuilder().AddJsonStream(...)` and binding to a small POCO `{ public IReadOnlyList<ActionEntry> Actions { get; init; } = []; }` (or directly to `HostSubscriptionsOptions`):

1. `Bind_StringArrayActions_PopulatesEntries` — JSON `{"Actions":["BlueIris","Pushover"]}` binds to two `ActionEntry` items with `Plugin == "BlueIris"` and `Plugin == "Pushover"`, both with default snapshot provider/validators.
2. `Bind_ObjectArrayActions_PopulatesEntries` — JSON `{"Actions":[{"Plugin":"BlueIris"},{"Plugin":"Pushover","SnapshotProvider":"Frigate"}]}` binds with the second entry's `SnapshotProvider == "Frigate"`.
3. `Bind_MixedStringAndObjectActions_PopulatesEntries` — JSON `{"Actions":["BlueIris",{"Plugin":"Pushover","SnapshotProvider":"Frigate","Validators":["CodeProjectAi"]}]}` binds with the string element producing a default-fielded `ActionEntry` and the object element producing a fully-populated entry including the validator key. Use `FluentAssertions.Should().Be(...)` / `.Should().BeEquivalentTo(...)` assertions. Test naming follows the underscore convention (CLAUDE.md "Conventions" block). Tests live under `tests/FrigateRelay.Host.Tests/Configuration/` to mirror the existing `ActionEntryJsonConverterTests.cs` location (RESEARCH.md §6).

**Acceptance Criteria:**
- `dotnet run --project tests/FrigateRelay.Host.Tests -c Release -- --filter-query "/*/*/ActionEntryTypeConverterTests/*"` runs and reports exactly 3 tests, all FAILING with messages indicating the bind produced empty/default entries (red phase confirms ID-12 reproduces).
- File exists at `tests/FrigateRelay.Host.Tests/Configuration/ActionEntryTypeConverterTests.cs`.

### Task 2: Create `ActionEntryTypeConverter` and decorate `ActionEntry`
**Files:**
- `src/FrigateRelay.Host/Configuration/ActionEntryTypeConverter.cs` (new)
- `src/FrigateRelay.Host/Configuration/ActionEntry.cs` (modify: add attribute, flip visibility)

**Action:** create + modify
**TDD:** true (implementing against red tests from Task 1)

**Description:**
Create `ActionEntryTypeConverter` as `internal sealed class ActionEntryTypeConverter : System.ComponentModel.TypeConverter` per RESEARCH.md §5. Implement:

- `public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);`
- `public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) => value is string s ? new ActionEntry(s) : base.ConvertFrom(context, culture, value)!;`

In `src/FrigateRelay.Host/Configuration/ActionEntry.cs`:
- Add `[System.ComponentModel.TypeConverter(typeof(ActionEntryTypeConverter))]` directly above the existing `[JsonConverter(typeof(ActionEntryJsonConverter))]` attribute. Both attributes coexist — they fire on disjoint code paths.
- Flip `public sealed record ActionEntry(...)` to `internal sealed record ActionEntry(...)` per **D8** (visibility sweep). With `SubscriptionOptions` already `internal` after PLAN-1.1, the CS0053 cascade no longer pins `ActionEntry` public.
- Do NOT modify the primary constructor signature — it already accepts `(string Plugin, string? SnapshotProvider = null, IReadOnlyList<string>? Validators = null)`, which is exactly what `new ActionEntry(stringValue)` needs (RESEARCH.md §1).

No DI wiring or `TypeDescriptor.AddAttributes` call is needed — the type-level attribute is consumed automatically by `ConfigurationBinder` via `TypeDescriptor.GetConverter`.

**Acceptance Criteria:**
- `git grep -nE 'public sealed record ActionEntry|public class ActionEntry' src/` returns zero matches.
- `git grep -nE 'internal sealed record ActionEntry' src/` returns exactly one match.
- `git grep -nE '\[TypeConverter\(typeof\(ActionEntryTypeConverter\)\)\]' src/` returns exactly one match (on `ActionEntry`).
- `git grep -nE 'internal sealed class ActionEntryTypeConverter' src/` returns exactly one match.
- `dotnet build FrigateRelay.sln -c Release` exits 0 with zero warnings.
- `dotnet run --project tests/FrigateRelay.Host.Tests -c Release -- --filter-query "/*/*/ActionEntryTypeConverterTests/*"` — all 3 tests pass (green phase).
- Pre-existing `ActionEntryJsonConverterTests` still pass: `dotnet run --project tests/FrigateRelay.Host.Tests -c Release -- --filter-query "/*/*/ActionEntryJsonConverterTests/*"`.

## Verification

- `dotnet build FrigateRelay.sln -c Release` — zero warnings.
- `dotnet run --project tests/FrigateRelay.Host.Tests -c Release -- --filter-query "/*/*/ActionEntryTypeConverterTests/*"` — 3 pass, 0 fail.
- `dotnet run --project tests/FrigateRelay.Host.Tests -c Release` — full suite passes.
- `git grep -nE 'public sealed record ActionEntry' src/` — empty.
- `git grep -nE 'internal sealed record ActionEntry' src/` — exactly one match.
- `git grep -nE '\[TypeConverter' src/` — exactly one match.
