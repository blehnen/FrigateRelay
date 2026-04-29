# Build Summary: Plan 7.1.2 — `ActionEntry.Validators` field + `ActionEntryJsonConverter` extension

## Status: complete

## Tasks Completed

- **Task 1** — `ActionEntry` record gains a third positional parameter `IReadOnlyList<string>? Validators = null`. Doc comment captures the "null and empty must be treated identically" contract for downstream consumers (dispatcher / EventPump). Commit `b021f3c`.
- **Task 2** — `ActionEntryJsonConverter`'s private `ActionEntryDto` mirror gains the same field. Read projection passes through; Write emits `Validators` only when `value.Validators is { Count: > 0 }`. Two new TDD tests cover roundtrip with/without Validators. Inline ID-12 disclaimer added to the converter's class-level docstring. Commit `f6996d2`.

## Files Modified

- `src/FrigateRelay.Host/Configuration/ActionEntry.cs` — added `Validators` parameter + doc.
- `src/FrigateRelay.Host/Configuration/ActionEntryJsonConverter.cs` — extended Read/Write/private DTO; added ID-12 disclaimer paragraph.
- `tests/FrigateRelay.Host.Tests/Configuration/ActionEntryJsonConverterTests.cs` — +2 tests + `_twoValidators` static field for CA1861.

## Decisions Made

- **Conditional Write emit (omit when empty):** When `Validators` is null OR empty, the Write path skips the array entirely. This keeps serialized fixtures terse and ensures back-compat — re-serializing a deserialized "old" `ActionEntry` produces byte-identical output for the existing Phase 4-6 fixtures.
- **Ordering inside the JSON object:** `Plugin` first, then `SnapshotProvider` (only if non-null), then `Validators` (only if non-empty). Matches the mental model of "primary key first, then optional refinements."
- **Tests on direct `JsonSerializer.Deserialize` paths only.** Per ID-12, `IConfiguration.Bind` does not invoke `[JsonConverter]` attributes — testing the converter through `IConfiguration` would not exercise the converter and would conflate two binding paths. Production binding via `IConfiguration` works because `IReadOnlyList<string>?` is supported by the configuration binder via primary-constructor reflection (verified at design time, RESEARCH §3).

## Issues Encountered

- **Builder agent failed on Bash permission** before any commit (same pattern as PLAN-1.1). Builder did make file edits (ActionEntry, JsonConverter, tests) but couldn't run `dotnet build` or `dotnet run` to verify, and couldn't `git commit`. Orchestrator validated the edits, fixed CA1861, ran the build/tests, and committed atomically per task.
- **CA1861 violation** on inline `new[] { "strict-person", "lax-vehicle" }` literal used twice in the same test method. Hoisted to `private static readonly string[] _twoValidators = ["strict-person", "lax-vehicle"]`.

## Verification Results

- `dotnet build FrigateRelay.sln -c Release` — **0 warnings, 0 errors**.
- `dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build` — **52/52 pass**.
- `Roundtrip_ObjectForm_WithValidators_PreservesAllFields` — green (round-trips `["strict-person", "lax-vehicle"]`).
- `Roundtrip_ObjectForm_WithoutValidators_ProducesNullValidators` — green (back-compat: deserialized `Validators` is null, Write omits the field, no `"Validators"` substring in re-serialized JSON).

## Lesson seeding (for `/shipyard:ship`)

- **Records with `IReadOnlyList<T>?` positional params bind correctly via `IConfiguration.Bind` AND via `JsonSerializer.Deserialize`.** The two binding paths are completely separate — converter customisation only affects the `JsonSerializer.Deserialize` path; configuration binding uses primary-constructor reflection and supports `IReadOnlyList<T>` array shapes natively. Phase 7 leverages both paths without writing a `TypeConverter`.
- **CA1861 is a real productivity hit on test-only literal arrays.** When the same literal is repeated across two `Should()` calls, hoist to `static readonly` even though it duplicates one variable. Collection expressions (`["a", "b"]`) bypass CA1861 — but only when assigned to a `static readonly` field, not when used inline as a method arg.