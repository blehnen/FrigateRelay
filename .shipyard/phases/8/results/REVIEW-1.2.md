# Review: Plan 1.2

## Verdict: MINOR_ISSUES

## Findings

### Critical
None.

### Minor

- **`ActionEntryJsonConverter` doc-comment still describes ID-12 as open** (`src/FrigateRelay.Host/Configuration/ActionEntryJsonConverter.cs`, lines 20–28). The `<para><strong>ID-12 (open):</strong>` block is now stale — the `TypeConverter` fix landed in the same phase and ID-12 is closed. A future reader will be misled into thinking `IConfiguration.Bind` still silently drops the string form.
  - Remediation: Replace the ID-12 paragraph with: `IConfiguration.Bind handles scalar strings via <see cref="ActionEntryTypeConverter"/> (ID-12 closed, Phase 8).`

- **ID-12 not marked Closed in `.shipyard/ISSUES.md`**. ID-12 still appears under Open Issues at lines 166–186. The SUMMARY claims "ID-12 closed," but the tracker was not updated.
  - Remediation: Move ID-12 to Closed Issues with `Status: Closed (commit 6264154, Phase 8 PLAN-1.2)`. While there, ID-2 and ID-10 are also closed by the Phase 8 visibility sweep — move those too.

- **`ConvertFrom` null-forgiving operator on `base.ConvertFrom` lacks comment** (`ActionEntryTypeConverter.cs:35`). The `!` suppresses a nullable warning on the base call which returns `object?`. Correct (the base throws for unsupported types) but a one-line comment would help future readers.

- **`Bind_ObjectArrayActions_PopulatesEntries` doesn't assert default `Validators` on the first entry** (`ActionEntryTypeConverterTests.cs:57–61`). The string-array and mixed-array tests check `Validators.Should().BeNullOrEmpty()` on defaults; the object-form test omits this. Coverage gap is minor but symmetry aids maintenance.

### Positive

- `ActionEntryTypeConverter` correctly implemented: `internal sealed class`, `CanConvertFrom(string)`, `ConvertFrom(string s) => new ActionEntry(s)`. Disjoint from `ActionEntryJsonConverter` per RESEARCH.md §5.
- Dual decoration on `ActionEntry` (`[TypeConverter]` + `[JsonConverter]`) is correct and both paths covered.
- `ActionEntry` cleanly internalized; no ripple-effect compile errors thanks to PLAN-1.1's prior visibility sweep.
- TDD discipline confirmed — red-phase commit `4357fd6` exists with failing tests proving ID-12 reproduces; green-phase commit `6264154` made them pass.
- 3 tests use `IConfiguration.Bind` directly (not `JsonSerializer.Deserialize`) — proves ID-12 closure path, not a parallel converter path.
- Build clean (0 warnings, 0 errors); tests 58/58 pass (55 baseline + 3 new).
- No `[assembly: InternalsVisibleTo(...)]` source attributes; no `.Result`/`.Wait()`; no `ServicePointManager`; no hard-coded IPs introduced.
- Visibility sweep complete across PLAN-1.1 + PLAN-1.2: `git grep -RnE '^public (sealed )?(class|record|interface) ' src/FrigateRelay.Host/` returns zero matches.

## Summary
Critical: 0 | Minor: 4 | Positive: 9. APPROVE — implementation correct and complete; the two doc-bookkeeping items are non-blocking and will be closed by the orchestrator before phase verification.
