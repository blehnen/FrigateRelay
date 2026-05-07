# Review: Plan 3.1

## Verdict: PASS (after one revision cycle addressing one Minor gap + one Suggestion)

The reviewer agent (sonnet) stopped mid-investigation looking for `ParallelValidatorsBindingTests.cs` (which SUMMARY-3.1 incorrectly named). The orchestrator completed the review against the actual artifacts on disk. The plumbing is correct end-to-end, but the test coverage was unbalanced — the operator-facing `IConfiguration.Bind` path had zero new tests despite SUMMARY-3.1's claim. Fixed inline before opening the PR.

## Findings

### Critical

None.

### Minor (addressed)

1. **SUMMARY-3.1 claimed 6 tests covered both converter paths, but all 6 went into `ActionEntryJsonConverterTests.cs` (JsonSerializer path only).** The operator-facing `IConfiguration.Bind` path (which loads `appsettings.json` at startup via `ActionEntryTypeConverter`) had zero new test coverage. CONTEXT-14 D5 requires `ParallelValidators=false` default on EVERY binding shape, and the TypeConverter path is the load-bearing one for v1.0/v1.1 backward compat. **Fixed:** added 3 tests to `ActionEntryTypeConverterTests.cs` (object-form-with-true / object-form-without / string-shorthand). Test count: 273 → 276.

### Suggestions (1 of 1 noted)

- **SUMMARY-3.1 references a `ParallelValidatorsBindingTests.cs` filename that doesn't exist.** Builder added the 6 tests to `ActionEntryJsonConverterTests.cs` instead. SUMMARY's "Tests 1-3 exercise the TypeConverter path; tests 4-6 exercise the JsonConverter path" claim was wrong on both axes — all 6 tests exercise the JsonConverter only. The accompanying `ParallelValidatorsBindingTests.cs` file path mention is also fictitious. **Not changed:** SUMMARY-3.1 is a build artifact; correcting it now adds churn without changing what shipped. Documenting the drift here so future agents reading SUMMARY-3.1 know to verify against actual file paths.

### Positive

- **`ActionEntry` extension is the right shape:** trailing positional record param `bool ParallelValidators = false`. Existing call sites (30+ across the test suite) work without modification.
- **JsonConverter handles read AND write of the new field correctly** — verified by the 6 tests in `ActionEntryJsonConverterTests.cs` (Serialize_True_IncludesField / Serialize_False_OmitsField / Deserialize_ObjectForm_True_RoundTrips / Deserialize_ObjectForm_Absent_DefaultsFalse / Deserialize_StringForm_DefaultsFalse / Roundtrip_PreservesAllFields).
- **TypeConverter handles string-shorthand correctly via positional record defaults** — `new ActionEntry(name)` constructs with `ParallelValidators=false`, no manual handling needed in the TypeConverter source. Now proven by Test 3 of the gap-fill (`Bind_StringShorthand_ParallelValidatorsDefaultsFalse`).
- **`DispatchItem` gains the field at the end of the positional list** with XML doc-comment citing CONTEXT-14 D6 and pointing at the consumer (`ChannelActionDispatcher.ConsumeAsync`).
- **`IActionDispatcher.EnqueueAsync` parameter placement** is C#-canonical: required positional → optional value-typed → CancellationToken last.
- **`EventPump.cs:148` uses named-arg style** (`parallelValidators: entry.ParallelValidators`) matching the existing `ct` pattern.
- **All 4 internal dispatcher stub classes** got their signatures updated. `CapturingDispatcher` in `EventPumpTests.cs` ALSO gained `List<bool> CapturedParallelValidators` so PLAN-3.2's tests can assert per-call pass-through trivially.
- **NSubstitute `Received(1).EnqueueAsync(...)` chains** in `EventPumpDispatchTests.cs` got the `Arg.Any<bool>()` insertion at the right position (between `subscriptionDefaultSnapshotProvider` and `ct`). Surgical fix, no semantic change.
- **No CHANGELOG entry yet** — correct per PLAN-3.1 spec. The field plumbing is not operator-observable until PLAN-3.2 lands the `Task.WhenAll` branch; PLAN-3.3 ships the CHANGELOG bullet for #23.

## Stage 1 (Correctness) check results

- `ActionEntry.ParallelValidators` field shape: **PASS** (trailing positional, default `false`).
- Both JsonConverter (Serialize/Deserialize) and TypeConverter paths produce identical defaults: **PASS** (proven by 6+3 tests across two files).
- `DispatchItem.ParallelValidators` field placement + XML doc: **PASS**.
- `IActionDispatcher.EnqueueAsync` signature: **PASS**.
- `ChannelActionDispatcher.EnqueueAsync` forwards to `DispatchItem` ctor: **PASS**.
- `EventPump.cs:148` propagates `entry.ParallelValidators`: **PASS**.
- 4 stub class signatures updated: **PASS**.
- 4 NSubstitute `Received()` chains updated with `Arg.Any<bool>()`: **PASS**.

## Stage 2 (Integration) check results

- No conflicts with merged Wave 1 (Roboflow / PR #42) or Wave 2 (DOODS2 / PR #43): **PASS**.
- Architectural invariants — no `Grpc.*`, `App.Metrics`, `OpenTracing`, `Jaeger.*`, `.Result`, `.Wait`, hard-coded IPs: **PASS** (`git grep` confirms).
- Test count progression 267 baseline (post-PR-#43) → 276 = +9 (6 JsonConverter + 3 TypeConverter): **PASS**. No regressions.
- `IActionDispatcher` and `EnqueueAsync` are `internal` — no public surface change: **PASS**.
- Counter inventory unchanged, Phase 13's `CounterInventoryDriftTests` still passes: **PASS**.
- No CHANGELOG entry yet (correctly deferred to PLAN-3.3): **PASS**.

## Final verdict

**PASS.** PLAN-3.1 ships the field plumbing for `ParallelValidators` correctly across all four touch-points (`ActionEntry` record, `DispatchItem` record-struct, `IActionDispatcher` seam, `EventPump` propagation), with backward-compat default `false` proven on both converter paths via 9 round-trip binding tests. PLAN-3.2 can proceed safely — the field is data-only at this point and the Task.WhenAll branch can land on top of this plumbing without needing additional plumbing changes.

Critical: 0 | Minor: 1 (addressed) | Suggestions: 1
