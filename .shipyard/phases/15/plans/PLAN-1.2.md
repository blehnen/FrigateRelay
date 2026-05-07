---
phase: 15-v1.2.1-hardening
plan: 1.2
wave: 1
dependencies: []
must_haves:
  - ActionEntryTypeConverter rejects empty / whitespace-only plugin names with FormatException at the converter boundary
  - Diagnostic message identifies the offending value clearly
files_touched:
  - src/FrigateRelay.Host/Configuration/ActionEntryTypeConverter.cs
  - tests/FrigateRelay.Host.Tests/Configuration/ActionEntryTypeConverterTests.cs
  - CHANGELOG.md
tdd: true
risk: low
---

# Plan 1.2: ActionEntryTypeConverter empty/whitespace plugin-name guard (#14)

## Context

`ActionEntryTypeConverter.ConvertFrom` (`src/FrigateRelay.Host/Configuration/ActionEntryTypeConverter.cs` lines 32–40 per RESEARCH.md §1 #14) currently coerces any `string` into `new ActionEntry(s)` with no guard, so an empty `""` or whitespace-only `"   "` from `IConfiguration.Bind` produces a plausible-looking `ActionEntry` whose `Plugin` field is empty. Downstream `StartupValidation.ValidateActions` line 135 surfaces this as a confusing "unknown plugin ''" error one indirection removed from the actual cause. The JSON-side counterpart `ActionEntryJsonConverter.cs` lines 44–46 already throws `JsonException` for empty input. This plan brings the `[TypeConverter]` path into parity using the stricter `IsNullOrWhiteSpace` (RESEARCH.md §1 #14 gotcha — JSON path uses `IsNullOrEmpty`; binding path is stricter on purpose because `IConfiguration.Bind` can hand us whitespace).

## Dependencies

None — Wave 1 root. Sole owner of `ActionEntryTypeConverter.cs` and its test file; no overlap with PLAN-1.1, PLAN-1.3, or PLAN-1.4.

## Tasks

### Task 1: Add IsNullOrWhiteSpace guard with FormatException (#14)
**Files:** `src/FrigateRelay.Host/Configuration/ActionEntryTypeConverter.cs`, `tests/FrigateRelay.Host.Tests/Configuration/ActionEntryTypeConverterTests.cs`, `CHANGELOG.md`
**Action:** modify
**Description:**
Write 2 failing MSTest tests first in the existing `ActionEntryTypeConverterTests.cs` (currently 6 tests per RESEARCH.md §1 #14). Use the established `Method_Condition_Expected` underscore convention. Direct-invocation pattern matches existing tests.

Test 1 — `ConvertFrom_EmptyString_ThrowsFormatException`:
- Construct converter; call `ConvertFrom(null, CultureInfo.InvariantCulture, "")`.
- Assert: throws `FormatException`. Message contains the literal substring `"empty"` or `"whitespace"`. Use FluentAssertions 6.12.2 `.Should().Throw<FormatException>().WithMessage("*empty*")` style.

Test 2 — `ConvertFrom_WhitespaceOnlyString_ThrowsFormatException`:
- Same shape with input `"   "` (3 spaces).
- Assert: throws `FormatException`. Message identifies the offending value (single-quoted in the message so the operator can see the whitespace count).

Implementation in `ActionEntryTypeConverter.ConvertFrom` (RESEARCH.md §1 #14 quotes the current 4-line shape). Replace with: when `value is string s`, first `if (string.IsNullOrWhiteSpace(s)) throw new FormatException($"ActionEntry plugin name cannot be empty or whitespace (received: '{s}').")`, then `return new ActionEntry(s)`. Otherwise fall through to `base.ConvertFrom`. Use `IsNullOrWhiteSpace` (NOT `IsNullOrEmpty`) — stricter than the JSON path on purpose.

`FormatException` is the idiomatic exception type for `TypeConverter.ConvertFrom` failures and `ConfigurationBinder` surfaces it cleanly as a binding error (RESEARCH.md §1 #14).

`CHANGELOG.md`: Append 1 line under `[Unreleased]` `### Security` (or `### Fixed`) section: `- #14 — \`ActionEntryTypeConverter\` rejects empty / whitespace plugin names at the converter boundary with \`FormatException\`, matching the JSON path.`

**TDD:** true
**Acceptance Criteria:**
- `ActionEntryTypeConverter.ConvertFrom("")` throws `FormatException` (NOT `ArgumentException`, NOT `InvalidOperationException`, NOT bubbling to a downstream "unknown plugin" message).
- `ActionEntryTypeConverter.ConvertFrom("   ")` throws `FormatException`.
- Exception message contains the offending value verbatim, single-quoted.
- All 6 pre-existing `ActionEntryTypeConverterTests` continue to pass (no regression on `Bind_StringArrayActions_PopulatesEntries` etc.).
- 2 net-new tests pass.
- `CHANGELOG.md` `[Unreleased]` lists `#14`.

## Verification

```bash
dotnet build FrigateRelay.sln -c Release
dotnet run --project tests/FrigateRelay.Host.Tests -c Release -- --filter "ActionEntryTypeConverterTests"
grep -n '#14' CHANGELOG.md
```
