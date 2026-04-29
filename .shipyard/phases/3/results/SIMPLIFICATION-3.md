# Simplification Report
**Phase:** 3 (FrigateMqttEventSource + PluginRegistrar + EventPump)
**Date:** 2026-04-24
**Files analyzed:** 7
**Findings:** 0 high, 3 medium, 3 low

---

## Medium Priority

### `DisposeAsync` has three separate `catch (ObjectDisposedException)` guards
- **Type:** Refactor
- **Effort:** Trivial
- **Locations:** `src/FrigateRelay.Sources.FrigateMqtt/FrigateMqttEventSource.cs:198`, `:207`, `:209`
- **Description:** Three consecutive try/catch blocks each catch `ObjectDisposedException` independently. The CTS cancel, the reconnect-task await, and the CTS dispose are all guarded separately. The cancel+await+dispose sequence on a single CTS can be wrapped in one helper or one try block covering all three steps.
- **Suggestion:** Collapse lines 197–210 into a single try block: cancel, await task, dispose — one shared `catch (ObjectDisposedException)` and `catch (OperationCanceledException)` at the bottom.
- **Impact:** ~8 lines removed, improved readability, single point to adjust if shutdown order changes.

### `HostSubscriptionsOptions` wrapper adds one hop for a single-property record
- **Type:** Remove / Consolidate
- **Effort:** Trivial
- **Locations:** `src/FrigateRelay.Host/Configuration/HostSubscriptionsOptions.cs:1-19`, `tests/FrigateRelay.Host.Tests/EventPumpTests.cs:23`
- **Description:** The wrapper record exists solely to hold `IReadOnlyList<SubscriptionOptions> Subscriptions`. `IOptionsMonitor<HostSubscriptionsOptions>` means every call-site reads `.CurrentValue.Subscriptions` — two dereferences. `IOptionsMonitor<IReadOnlyList<SubscriptionOptions>>` bound directly would remove the intermediate type.
- **Suggestion:** If `HostSubscriptionsOptions` will never grow a second property, replace with `IOptionsMonitor<IReadOnlyList<SubscriptionOptions>>` and bind `builder.Configuration.GetSection("Subscriptions")` directly. If additional host-level config is planned (Phase 4+), keep the wrapper and close this finding.
- **Impact:** Removes one file, one indirection per `_subsMonitor.CurrentValue` access, one test helper instantiation.

### `FakeSource` / `StaticMonitor` / `CapturingLogger` inline in `EventPumpTests.cs` — Rule of Three not yet met
- **Type:** Refactor
- **Effort:** Trivial
- **Locations:** `tests/FrigateRelay.Host.Tests/EventPumpTests.cs:104-147`
- **Description:** Three test helpers are private nested classes in `EventPumpTests.cs`. Currently there is only one test class consuming them, so Rule of Three is not met. However `FakeSource` is generic enough to be useful in any future host test and `CapturingLogger<T>` is a pattern that will recur.
- **Suggestion:** Defer extraction to `TestHelpers/` until a second test class in the same project needs them. Note the location so the next builder knows where to hoist.
- **Impact:** No lines saved now; flagged to prevent a second near-duplicate appearing in Phase 4.

---

## Low Priority

- **`EventContextProjector.cs:31-44` XML doc on `TryProject`** is more verbose than needed (5 `<param>` blocks + `<returns>` block totalling ~15 lines for a 50-line internal static method). The `<remarks>` on the class already explain D5 and OQ4. The method-level doc could be collapsed to a two-line summary. Low value since it is `internal`.

- **`PluginRegistrar.cs:9-21` XML `<remarks>`** describes registration exclusions ("never touches matcher / dedupe / cache") that are already covered in `SUMMARY-2.1.md`. Fine for onboarding but adds ~12 lines of doc that will drift if the scope changes. Consider a single `// host-scope items registered by Program.cs` inline comment instead.

- **`run-tests.sh:57-69` fallback copy block** is well-commented but the comment explains the *symptom* (WSL/Ubuntu vs container) without stating the underlying MTP version where the bug was observed. A `# Tested against Microsoft.Testing.Extensions.CodeCoverage 17.x` note would help a future maintainer decide when the fallback is safe to remove.

---

## Summary
- **Duplication found:** 0 cross-file duplicates
- **Dead code found:** 0 unused definitions
- **Complexity hotspots:** 0 functions exceeding thresholds (longest is `DisposeAsync` at 29 lines, deepest nesting is 3 levels)
- **AI bloat patterns:** 1 instance (`DisposeAsync` over-decomposed catch blocks); XML doc verbosity on `internal` code is minor
- **Estimated cleanup impact:** ~20 lines removable (DisposeAsync consolidation + optional wrapper removal)

## Recommendation

No simplification is required before shipping Phase 3. The reconnect loop (`EnsureStarted` + `Interlocked` + separate Task + Channel) is justified by SUMMARY-2.1 decision 2 and is correctly documented — do not collapse it. The three medium findings are clean-up opportunities for the Phase 4 builder to pick up opportunistically, not blockers. The `HostSubscriptionsOptions` wrapper should be revisited when Phase 4 host configuration scope is defined.
