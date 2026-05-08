# Simplification Report

**Phase:** 16 (v1.3.0 — metrics cardinality + parallel validators)
**Date:** 2026-05-08
**Files analyzed:** 13 source + test files changed in phase
**Findings:** 1 high, 2 medium, 2 low

---

## High Priority

### Duplicate `StaticOptionsMonitor<T>` / `StaticMonitor<T>` nested class across 7 test files

- **Type:** Consolidate
- **Effort:** Trivial
- **Locations:**
  - `tests/FrigateRelay.Host.Tests/Observability/CounterIncrementTests.cs:545` (`StaticMonitor<T>`) and `:555` (`StaticOptionsMonitor<T>`)
  - `tests/FrigateRelay.Host.Tests/Observability/EventPumpSpanTests.cs:342` (`StaticMonitor<T>`) and `:352` (`StaticOptionsMonitor<T>`)
  - `tests/FrigateRelay.Host.Tests/Observability/MetricsCardinalityTests.cs:72` (`StaticMonitor<T>`)
  - `tests/FrigateRelay.Host.Tests/EventPumpDispatchTests.cs:185` (`StaticMonitor<T>`) and `:195` (`StaticOptionsMonitor<T>`)
  - `tests/FrigateRelay.Host.Tests/EventPumpTests.cs:291` (`StaticMonitor<T>`, classic ctor form) and `:313` (`StaticOptionsMonitor<T>`)
  - `tests/FrigateRelay.Host.Tests/Dispatch/ChannelActionDispatcherTests.cs:405` (`StaticOptionsMonitor<T>`)
  - `tests/FrigateRelay.Host.Tests/Dispatch/ChannelActionDispatcherParallelValidatorsTests.cs:403` (`StaticOptionsMonitor<T>`)
- **Description:** Every test file that exercises `MetricsTagWriter` defines its own private nested `IOptionsMonitor<T>` stub, varying only by name (`StaticMonitor` vs `StaticOptionsMonitor`) and constructor style (primary ctor vs explicit ctor). Two files (`EventPumpDispatchTests`, `EventPumpTests`, `EventPumpSpanTests`, `CounterIncrementTests`) define BOTH names, creating redundancy within the same file. The body in all variants is identical: `CurrentValue { get; } = value`, `Get(name) => CurrentValue`, `OnChange(...) => null`. This is the exact same extraction that was applied to `CapturingLogger<T>` after Phase 6 (ID-11). The precedent and the `FrigateRelay.TestHelpers` library already exist.
- **Suggestion:** Add `tests/FrigateRelay.TestHelpers/StaticOptionsMonitor.cs`:
  ```csharp
  using Microsoft.Extensions.Options;
  namespace FrigateRelay.TestHelpers;

  public sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
  {
      public T CurrentValue { get; } = value;
      public T Get(string? name) => CurrentValue;
      public IDisposable? OnChange(Action<T, string?> listener) => null;
  }
  ```
  Then add `<PackageReference Include="Microsoft.Extensions.Options" Version="10.0.x" />` to `tests/FrigateRelay.TestHelpers/FrigateRelay.TestHelpers.csproj` (it currently has only `Microsoft.Extensions.Logging.Abstractions`). Delete all 11 nested class definitions from the 7 files and replace with the shared type. The `global using FrigateRelay.TestHelpers;` in `tests/FrigateRelay.Host.Tests/Usings.cs` means no per-file using is required. Also update `CreatePassthroughTagWriter()` in the 6 files (`:310`, `:192`, `:400`, `:349`, `:552`, `:402`) to use the now-shared type.
- **Impact:** Removes ~44 lines of definition boilerplate across 7 files. Establishes a single spelling (`StaticOptionsMonitor<T>`) and eliminates the `StaticMonitor` / `StaticOptionsMonitor` naming split that already confuses within-file readers. Follows the exact same pattern as the `CapturingLogger` extraction.

---

## Medium Priority

### `NormalizeCameraTag` called twice on the same value in the failure catch block

- **Type:** Refactor
- **Effort:** Trivial
- **Locations:** `src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs:279-286`
- **Description:** In `ConsumeAsync`'s outer `catch (Exception ex)` block, `_metricsTagWriter.NormalizeCameraTag(item.Context.Camera)` is called twice in immediate succession — once for `IncrementExhausted` (line 280) and once for `IncrementActionsFailed` (line 284) — on the same item, before any other code executes between the two calls. `NormalizeCameraTag` rebuilds a `HashSet<string>` on every invocation (acknowledged in the source comment), so this is a double allocation on the hot error path.
- **Suggestion:** Hoist the result into a local before the two increment calls:
  ```csharp
  catch (Exception ex)
  {
      var normalizedCamera = _metricsTagWriter.NormalizeCameraTag(item.Context.Camera);
      DispatcherDiagnostics.IncrementExhausted(normalizedCamera, item.Subscription, item.Plugin.Name);
      DispatcherDiagnostics.IncrementActionsFailed(normalizedCamera, item.Subscription, item.Plugin.Name);
      ...
  }
  ```
  The same pattern applies in `RunValidatorsSequentiallyAsync` and `RunValidatorsInParallelAsync` where the same `item.Context.Camera` is normalized for both `IncrementValidatorsPassed`/`IncrementValidatorsRejected` calls on the same validator result, though those are in separate branches (pass vs reject) so only one fires per validator, making that case benign. The catch block at line 279 is the only true double-call.
- **Impact:** One fewer `HashSet<string>` allocation per failed dispatch item. Trivial on the happy path, modest on degraded-state (when `Exhausted` and `ActionsFailed` are firing frequently). Cleaner intent.

### `NormalizeCameraTag` rebuilds `HashSet<string>` on every call — caching opportunity

- **Type:** Refactor
- **Effort:** Moderate
- **Locations:** `src/FrigateRelay.Host/Observability/MetricsTagWriter.cs:58-64`
- **Description:** The source comment at line 58 explicitly defers a cached-with-`OnChange` approach, noting the set is small and config reloads are infrequent. This is a documented, intentional trade-off for v1.3.0 scope, not an oversight. The finding is recorded here for tracking, not as a blocker. At production scale (Frigate event rates of hundreds per second, each event triggering 4-12 `NormalizeCameraTag` calls from `EventPump` and `ChannelActionDispatcher`), every call allocates and populates a `HashSet<string>` from the `string[]` in `MetricsTagsOptions.KnownCameras`.
- **Suggestion:** Cache the built set as a `volatile HashSet<string>?` field, invalidated via `IOptionsMonitor.OnChange`. On each call, read the cached set; if null (first call or invalidated), rebuild from `_monitor.CurrentValue.KnownCameras`, store, and use. This is a standard options-monitor caching pattern. Alternatively, since `MetricsTagsOptions` is `internal sealed record`, the set could be cached on the record itself via a lazy property.
- **Impact:** Eliminates per-call heap allocation for the common case where `KnownCameras` is non-empty and config is not actively reloading. At 10 events/sec with 8 normalize calls per event that is 80 allocations/sec eliminated. The source author already identified this; this finding confirms the deferral is visible in the report for future action.

---

## Low Priority

- **Two internal naming variants for the same stub type** — `StaticMonitor<T>` and `StaticOptionsMonitor<T>` coexist in the same files (`EventPumpDispatchTests.cs:185+195`, `EventPumpTests.cs:291+313`, `EventPumpSpanTests.cs:342+352`, `CounterIncrementTests.cs:545+555`). Both names survive in the same test class because different helpers use each. The High-priority extraction above resolves this by establishing one canonical spelling.

- **`MetricsTagsOptions.KnownCameras` init-only property uses `Array.Empty<string>()` as default** (`src/FrigateRelay.Host/Observability/MetricsTagsOptions.cs:20`) while the language-level equivalent `[]` is used in test instantiation. Either is correct; `[]` is more idiomatic in C# 12 / .NET 10 targets. One-line change if consistency is desired.

---

## Summary

- **Duplication found:** 11 nested class definitions across 7 files (all variants of the same 4-line `IOptionsMonitor<T>` stub)
- **Dead code found:** 0 unused definitions in phase-touched source files
- **Complexity hotspots:** 0 functions exceeding thresholds (largest touched method is `ConsumeAsync` at ~110 lines but it was pre-existing and not introduced in this phase)
- **AI bloat patterns:** 0 instances — XML doc comments on new types are restrained and functional; test method bodies are clean
- **Estimated cleanup impact:** ~44 lines removable, 1 double allocation eliminated

## Recommended Action

The `StaticOptionsMonitor<T>` extraction (High) is a mechanical, low-risk improvement that directly parallels the `CapturingLogger` extraction already in the project's conventions. It should be done before the next phase adds more test files that will inevitably copy the pattern again. The two Medium findings (double-call hoist and HashSet caching) are genuine improvements but non-urgent; the HashSet caching deferral is already documented in the source.

## Verdict: MINOR_BLOAT

The duplication is real and growing (7 files, 11 definitions this phase alone) but does not affect correctness or production behavior. Ship Phase 16; schedule the `StaticOptionsMonitor` extraction as the first task of Phase 17 cleanup, before new test files proliferate the pattern further.
