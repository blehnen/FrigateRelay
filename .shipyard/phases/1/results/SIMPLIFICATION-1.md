# Simplification Review — Phase 1

**Phase:** 1 (Foundation + Abstractions + Placeholder Host)
**Date:** 2026-04-24
**Files analyzed:** 12 source + test files
**Findings:** 0 High, 1 Medium, 2 Low

---

## Overall complexity assessment

Phase 1 is well-scoped. The abstractions assembly is lean and intentional — every interface and value type maps directly to a v1 plugin contract documented in PROJECT.md. The host has almost no logic yet, which is correct. The one structural issue is a bootstrapping ceremony in `Program.cs` that adds cost for no current benefit; everything else is noise-level.

---

## Findings

### HIGH
None.

---

### MEDIUM

#### Bootstrap logger factory is over-engineered for an always-empty loop

- **Type:** Refactor
- **Effort:** Trivial
- **Locations:** `src/FrigateRelay.Host/Program.cs:40-43`
- **Description:** `LoggerFactory.Create(lb => lb.AddConsole())` allocates and immediately disposes a full `ILoggerFactory` solely to produce a logger for `PluginRegistrarRunner.RunAll`. In Phase 1 `registrars` is always the empty array literal `[]`, so `RunAll` iterates zero times and the logger is never used. Even in Phase 3+ when real registrars exist, `PluginRegistrarRunner.RunAll` will log at most a handful of lines during startup — the full factory construction is disproportionate overhead.
- **Suggestion:** Replace the three-line factory block with `builder.Logging`'s already-configured logger obtained after `builder.Build()`, or — simpler — pass `builder.Services.BuildServiceProvider().GetRequiredService<ILogger<PluginRegistrarRunner>>()` only when the registrar list is non-empty, or restructure `RunAll` to accept `ILoggerFactory` (available from `builder.Services`) so the `using` block and the manual category-name string both disappear. The simplest fix for Phase 2: move `PluginRegistrarRunner.RunAll` to after `builder.Build()` and pass `app.Services.GetRequiredService<ILogger<PluginRegistrarRunner>>()` — the built host's logger is already console-wired.
- **Impact:** Removes 4 lines, eliminates the disposable factory allocation, removes the stringly-typed category name `"FrigateRelay.Host.PluginRegistrarRunner"`.

---

### LOW

1. **`LoggerMessage.Define` on a once-per-lifetime log call** — `src/FrigateRelay.Host/PlaceholderWorker.cs:9-10`. `LoggerMessage.Define` is the right pattern for high-frequency hot paths; `PlaceholderWorker.ExecuteAsync` emits exactly one log line per process lifetime. A plain `_logger.LogInformation("Host started")` is sufficient and more readable. This class is explicitly temporary (replaced in a later phase), so the investment in allocation-free logging is wasted. Low priority because the pattern itself is not wrong, just disproportionate.

2. **`CapturingLogger<T>` is private to `PlaceholderWorkerTests`** — `tests/FrigateRelay.Host.Tests/PlaceholderWorkerTests.cs:49-68`. SUMMARY-3.1 already flags this: "should be hoisted before Phase 3 gets a second worker." It is currently a private nested class, so the moment a second worker test file needs it a duplicate will appear. This is a note only — SUMMARY-3.1 correctly identifies it; no additional action needed beyond what is already tracked.

---

## Recommended next steps

The only change worth making before Phase 2 begins is the bootstrap logger refactor (Medium finding). It is a 4-line mechanical fix that also removes the stringly-typed category name — a maintenance hazard if `PluginRegistrarRunner` is ever renamed. The two Low findings are deferred: `LoggerMessage.Define` disappears when `PlaceholderWorker` is replaced, and `CapturingLogger<T>` hoisting is already on the Phase 3 radar per SUMMARY-3.1.

**Overall verdict:** Phase 1 is appropriately minimal. No blocking simplification work required before shipping to Phase 2.
