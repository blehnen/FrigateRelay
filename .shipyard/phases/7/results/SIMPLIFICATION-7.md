# Phase 7 — Simplification Review

**Verdict:** Low — 3 minor findings, all candidates for follow-up not blocking.
**Date:** 2026-04-26
**Scope:** All 25 files changed between `pre-build-phase-7` and `acc3de4`.

## Findings

### Low-1 — `CapturingLoggerProvider` is local to `MqttToValidatorTests.cs`

`tests/FrigateRelay.IntegrationTests/MqttToValidatorTests.cs` includes a private `CapturingLoggerProvider` (~30 LoC) for capturing log entries across categories. This is the second piece of log-capture infrastructure in the codebase — unit tests use `CapturingLogger<T>` from `FrigateRelay.TestHelpers` (extracted Phase 6) but it's per-T and unsuited for integration log assertion.

**Status:** Rule of Two — **defer**. If a third integration test needs cross-category log capture (e.g. Phase 9 observability tests), simplifier should promote this to `FrigateRelay.TestHelpers` as `CapturingLoggerProvider`. Premature extraction now would violate the project's Rule of Three discipline.

### Low-2 — Catch-block ordering in `CodeProjectAiValidator.ValidateAsync` could be a shared helper

The `try / catch (OperationCanceledException) when ct.IsCancellationRequested → throw / catch (TaskCanceledException) → timeout / catch (HttpRequestException) → unavailable` pattern is now Phase 6's BlueIris and Pushover, plus Phase 7's CodeProjectAi — three implementations.

**Status:** Rule of Three is technically met. But the three implementations differ subtly: BlueIris/Pushover map the catch to retry-then-rethrow Polly behavior; CodeProjectAi maps to `Verdict.Fail/Pass` per `OnError`. The shared shape is the catch-block ORDER, not the body — extracting to a helper would fragment the readability of each plugin. **Recommend documenting the pattern in CLAUDE.md `## Conventions` rather than extracting code** — DOCUMENTATION-7 captures this.

### Low-3 — `EventPump` validator-resolution lambda allocates per dispatch

```csharp
IReadOnlyList<IValidationPlugin> validators = entry.Validators is { Count: > 0 } keys
    ? keys.Select(k => _services.GetRequiredKeyedService<IValidationPlugin>(k)).ToArray()
    : Array.Empty<IValidationPlugin>();
```

The `keys.Select(...).ToArray()` allocates a new array AND a Linq enumerator wrapper per dispatch when validators are present. For high-event-rate Frigate setups (10-20 events/sec), this is a measurable allocation hot path.

**Status:** **Defer**. Modern .NET tier-2 JIT may elide some of this, but if Phase 9 observability metrics show GC pressure at the `IValidationPlugin[]` allocation site, swap to a small per-event pooled buffer or cache-per-`ActionEntry` (validators are resolved at startup-time and don't change at runtime). Track for Phase 9 / 12 perf pass; not worth the complexity now.

## Cumulative complexity assessment

- New code is **295 LoC across 4 production files** (CodeProjectAi plugin) — lean.
- Largest method: `CodeProjectAiValidator.ValidateAsync` at ~30 LoC including all error-handling branches. Below the 40-LoC red flag.
- `PluginRegistrar.Register` at ~50 LoC — within the registrar precedent for BlueIris/Pushover.
- `ChannelActionDispatcher.ConsumeAsync` grew from 38 → 65 LoC (validator chain + pre-resolve branch). Still single-responsibility (consume-and-dispatch). Nesting depth max 4. Below the simplifier red flag at 50 LoC / depth 5.

## Dead code

None introduced. `Array.Empty<IValidationPlugin>()` placeholder removed from `EventPump.cs`. The `RecordingPlugin` / `StubValidator` / `SnapshotReadingValidator` / `SnapshotReadingPlugin` / `CountingResolver` test helpers in `ChannelActionDispatcherTests.cs` are all USED by the 2 new tests; not dead.

## Verdict rationale

Phase 7 is structurally clean. The 3 Low findings are deferral candidates, none material. Recommend closing the phase without follow-up commits — defer all 3 to natural future triggers (Phase 9 perf pass / third integration suite / Rule-of-Two reactivation).