---
plan_id: 7.1.2
title: ActionEntry.Validators field + ActionEntryJsonConverter extension
wave: 1
plan: 2
dependencies: []
files_touched:
  - src/FrigateRelay.Host/Configuration/ActionEntry.cs
  - src/FrigateRelay.Host/Configuration/ActionEntryJsonConverter.cs
  - tests/FrigateRelay.Host.Tests/Configuration/ActionEntryJsonConverterTests.cs
tdd: false
estimated_tasks: 2
---

# Plan 1.2: ActionEntry.Validators field + ActionEntryJsonConverter extension

## Context
CONTEXT-7 D2 locked the validator config shape: top-level `Validators: { key: { Type, ...opts } }` referenced by `ActionEntry.Validators: ["key", ...]`. This plan only extends the ActionEntry record + its JSON converter. Wave 2 (PLAN-2.1) handles the per-instance binding inside the CodeProjectAi registrar. Wave 3 (PLAN-3.1) does the EventPump key→`IValidationPlugin` resolution.

**ID-12 (open issue) is directly relevant:** `IConfiguration.Bind` does NOT invoke `[JsonConverter]`. RESEARCH §3 verified that adding `IReadOnlyList<string>? Validators` as a third positional record parameter binds correctly through `IConfiguration.Bind` via the record's primary-constructor reflection path — the converter only matters for direct `JsonSerializer.Deserialize` paths (which the integration test fixtures use). Operators must continue using the OBJECT form `{"Plugin":"…", "Validators":["…"]}`; the legacy string-array form remains silently broken (tracked separately as ID-12 — do NOT attempt a fix in this plan).

## Dependencies
None — Wave 1 parallel with PLAN-1.1.

## Tasks

### Task 1: Extend `ActionEntry` record with `Validators` parameter
**Files:** `src/FrigateRelay.Host/Configuration/ActionEntry.cs`
**Action:** modify
**Description:**

Add `IReadOnlyList<string>? Validators = null` as the **third positional parameter**:

```csharp
[JsonConverter(typeof(ActionEntryJsonConverter))]
public sealed record ActionEntry(
    string Plugin,
    string? SnapshotProvider = null,
    IReadOnlyList<string>? Validators = null);
```

Update XML doc on `Validators` to call out:
- "Optional list of named validator instance keys to gate this action. Each key must be defined in the top-level `Validators` configuration section. Empty/null = no validators."
- "Validators run BEFORE the action's Polly retry pipeline; a failing verdict short-circuits this action only."

**Acceptance criteria:**
- `dotnet build FrigateRelay.sln -c Release` clean.
- All existing 2-arg `new ActionEntry(...)` call sites in tests/fixtures still compile (default `null` for new param).
- `git grep -nE 'new ActionEntry\(' src/ tests/` produces no compile breaks.

### Task 2: Extend `ActionEntryJsonConverter` for `Validators` (round-trip)
**Files:** `src/FrigateRelay.Host/Configuration/ActionEntryJsonConverter.cs`, `tests/FrigateRelay.Host.Tests/Configuration/ActionEntryJsonConverterTests.cs`
**Action:** modify + test
**Description:**

Per RESEARCH §3 verbatim:

**Private DTO extension:**
```csharp
private sealed record ActionEntryDto(
    string Plugin,
    string? SnapshotProvider = null,
    IReadOnlyList<string>? Validators = null);
```

**Read (object form) projection:**
```csharp
return new ActionEntry(dto.Plugin, dto.SnapshotProvider, dto.Validators);
```

**Read (string form) — UNCHANGED:**
```csharp
return new ActionEntry(reader.GetString()!);   // legacy fallback; Validators stays null
```

**Write:**
```csharp
writer.WriteStartObject();
writer.WriteString("Plugin", value.Plugin);
if (value.SnapshotProvider is not null)
    writer.WriteString("SnapshotProvider", value.SnapshotProvider);
if (value.Validators is { Count: > 0 })
{
    writer.WriteStartArray("Validators");
    foreach (var v in value.Validators) writer.WriteStringValue(v);
    writer.WriteEndArray();
}
writer.WriteEndObject();
```

Add 2 tests (additive — find existing `ActionEntryJsonConverterTests.cs` location via `git grep -l ActionEntryJsonConverter tests/`; if no test file yet, create one in `tests/FrigateRelay.Host.Tests/Configuration/`):

```csharp
[TestMethod]
public void Roundtrip_ObjectForm_WithValidators_PreservesAllFields()
{
    var entry = new ActionEntry("Pushover", "Frigate", new[] { "strict-person", "lax-vehicle" });
    var json = JsonSerializer.Serialize(entry);
    var roundtrip = JsonSerializer.Deserialize<ActionEntry>(json)!;
    roundtrip.Plugin.Should().Be("Pushover");
    roundtrip.SnapshotProvider.Should().Be("Frigate");
    roundtrip.Validators.Should().BeEquivalentTo(new[] { "strict-person", "lax-vehicle" });
}

[TestMethod]
public void Roundtrip_ObjectForm_WithoutValidators_ProducesNullValidators()
{
    var json = """{"Plugin":"BlueIris"}""";
    var entry = JsonSerializer.Deserialize<ActionEntry>(json)!;
    entry.Plugin.Should().Be("BlueIris");
    entry.Validators.Should().BeNull(); // back-compat — operators without Validators see null, not empty
    var rewritten = JsonSerializer.Serialize(entry);
    rewritten.Should().NotContain("Validators"); // omit when null
}
```

**Document the back-compat contract** in the converter's class XML doc:
- "When `Validators` is absent OR empty, deserialized `ActionEntry.Validators` is `null`."
- "Consumers (dispatcher, EventPump) MUST treat `null` and empty-list identically — `(action.Validators?.Count ?? 0) == 0`."

**Acceptance criteria:**
- 2 new converter tests pass.
- All existing converter tests (object form Plugin-only, object form Plugin+Snapshot, string form fallback) remain green.
- `git grep -n "Validators" src/FrigateRelay.Host/Configuration/` shows the new field in both `ActionEntry` and converter.
- Build clean.

## Verification

```bash
dotnet build FrigateRelay.sln -c Release
dotnet run --project tests/FrigateRelay.Host.Tests -c Release
git grep -n "Validators" src/FrigateRelay.Host/Configuration/
```

## Notes for builder
- **ID-12 is NOT in scope here.** Do NOT attempt to fix the `IConfiguration.Bind` ≠ `[JsonConverter]` regression. The string-array shape `["BlueIris"]` remains silently broken via `IConfiguration.Bind`; operators must use object form. Document this in Task 2's class-level XML comment if not already noted.
- **RESEARCH §3** verified `IReadOnlyList<string>?` binds correctly through `IConfiguration.Bind` via the record's primary-constructor reflection path. No `TypeConverter` needed for this plan.
- **No fixture migrations in this plan.** PLAN-2.1 + PLAN-3.1 will add `Validators` to integration-test fixtures.
- **Test naming convention:** `Method_Condition_Expected` with underscores (CA1707 silenced for tests/**.cs).
