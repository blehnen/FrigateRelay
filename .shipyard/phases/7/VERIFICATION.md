# Phase 7 — Phase Verification (post-build)

**Verdict:** COMPLETE
**Date:** 2026-04-26
**Type:** post-build
**Total tests:** 143/143 across all suites (Phase 6 baseline 124, Phase 7 added 19).

> Note: This file replaces the plan-time spec-compliance verifier output that existed at this path. That earlier verdict (READY) gated `/shipyard:build 7`; this verdict gates phase closure and is what `/shipyard:ship` will read.

## ROADMAP success-criteria coverage

| Phase 7 deliverable | Owning plan | Evidence |
|---|---|---|
| `src/FrigateRelay.Plugins.CodeProjectAi/` project (Validator, Options, Registrar) | PLAN-2.1 | 4 source files (`CodeProjectAiOptions`, `CodeProjectAiResponse`, `CodeProjectAiValidator`, `PluginRegistrar`); 295 LoC. csproj has explicit `<TargetFramework>net10.0</TargetFramework>` + `<InternalsVisibleTo>` MSBuild item. |
| `IValidationPlugin` extended with `SnapshotContext` (CONTEXT-7 D1 / Phase 6 ARCH-D2 mirror) | PLAN-1.1 | `IValidationPlugin.ValidateAsync(EventContext, SnapshotContext, CancellationToken)`. |
| `SnapshotContext.PreResolved` ctor (RESEARCH §5) | PLAN-1.1 | New `SnapshotContext(SnapshotResult? preResolved)` + `_hasPreResolved` flag; verified by 2 unit tests. |
| Dispatcher per-action validator chain | PLAN-1.1 | `ChannelActionDispatcher.ConsumeAsync` runs validator chain ABOVE Polly; first failing verdict short-circuits THIS action only. |
| `validator_rejected` structured log (CONTEXT-7 D7) | PLAN-1.1 | `LogValidatorRejected` `LoggerMessage.Define` carries `event_id, camera, label, action, validator, reason`; `EventId(20, "ValidatorRejected")`. |
| Other actions fire independently (PROJECT.md V3) | PLAN-1.1 | Verified by `Validator_ShortCircuits_OnlyAttachedAction` integration test: BlueIris fires (1 GET), Pushover skipped (0 POSTs). |
| `ActionEntry.Validators` keyed reference (CONTEXT-7 D2) | PLAN-1.2 | Third positional record param; `ActionEntryJsonConverter` extended; 2 roundtrip tests. |
| Top-level `Validators` config dict + keyed-services pattern | PLAN-2.1 / PLAN-3.1 | `PluginRegistrar` enumerates `IConfigurationSection.GetChildren()` filtered by `Type == "CodeProjectAi"`; registers named options + keyed `IValidationPlugin` per RESEARCH §2. |
| Per-instance HttpClient with TLS bypass option (CONTEXT-7 D8) | PLAN-2.1 | `AddHttpClient($"CodeProjectAi:{instanceKey}").ConfigurePrimaryHttpMessageHandler` honoring `AllowInvalidCertificates` via CA5359-suppressed callback. |
| `OnError {FailClosed, FailOpen}` (CONTEXT-7 D4) | PLAN-2.1 | 2 unit tests cover both branches; catch-block ORDER per RESEARCH §6 verified. |
| **No retry handler on validator HttpClient (CONTEXT-7 D4)** | PLAN-2.1 | `git grep -n "AddResilienceHandler" src/FrigateRelay.Plugins.CodeProjectAi/` matches **only doc comments explaining the intentional absence**. |
| Bbox `ZoneOfInterest` deferred (CONTEXT-7 D5) | PLAN-2.1 | `CodeProjectAiOptions` ships without `ZoneOfInterest`; deferred. |
| Multipart unquoted `name=` (Phase 6 D12) | PLAN-2.1 | Test 7 (`ValidateAsync_MultipartWireFormat_UsesUnquotedNameImage`) asserts both presence of unquoted form and absence of quoted form. |
| `EventPump` resolves keyed validators per dispatch | PLAN-3.1 | `IServiceProvider.GetRequiredKeyedService<IValidationPlugin>(key)` per `ActionEntry.Validators` key. Placeholder `Array.Empty<IValidationPlugin>()` removed. |
| `StartupValidation.ValidateValidators` + wiring (Phase 5 review-3.1 lesson) | PLAN-3.1 | Method exists in StartupValidation.cs; called from `HostBootstrap.ValidateStartup` with explicit "Phase 5 dead-code lesson" inline comment. 3 unit tests cover undefined-key, unknown-Type, and happy-path. |
| `HostBootstrap` registrar conditional | PLAN-3.1 | `if (builder.Configuration.GetSection("Validators").Exists()) registrars.Add(...)`. |
| Integration: `Validator_ShortCircuits_OnlyAttachedAction` | PLAN-3.1 | Pass. WireMock confirms BlueIris=1, Pushover=0; `validator_rejected` log captured by `CapturingLoggerProvider`. |
| Integration: `Validator_Pass_BothActionsFire` | PLAN-3.1 | Pass. WireMock confirms both fire once; no `validator_rejected` log. |
| ≥ 8 unit tests | gate | **16 unit tests added** (8 CodeProjectAiValidatorTests + 3 StartupValidationValidatorsTests + 2 ChannelActionDispatcher new + 2 SnapshotContext PreResolved + 1 ActionEntryJsonConverter Validators-related). +100% over gate. |
| ≥ 1 integration test (`Validator_ShortCircuits_OnlyAttachedAction`) | gate | 2 integration tests added. |
| ≥ 1 integration test (`Validator_Pass_BothActionsFire`) | gate | Met. |

## Architecture invariant greps

```
git grep -nE '\.(Result|Wait)\(' src/                      → empty ✓
git grep -n  'ServicePointManager' src/                    → 2 doc-only mentions (both saying "no global … callback") ✓
git grep -nE 'App\.Metrics|OpenTracing|Jaeger\.' src/      → empty ✓
git grep -nE '192\.168\.[0-9]+\.[0-9]+' src/ tests/        → empty ✓
git grep -n  'AddResilienceHandler' src/.../CodeProjectAi/ → 2 doc-only mentions of intentional absence ✓
```

## Diff stats

- 25 files changed (15 source/test + 10 docs/csproj/sln/Usings/etc.)
- +1473 insertions / −15 deletions
- 10 atomic per-task commits between `pre-build-phase-7` and `acc3de4`

## Outstanding gaps

**None blocking phase closure.** Two notes for follow-up:

1. **`CapturingLoggerProvider` in `MqttToValidatorTests.cs`** — Rule of Two trigger if a third integration test needs cross-category log capture. If Phase 8 introduces another integration suite, simplifier should extract to `FrigateRelay.TestHelpers`.
2. **ID-12 still open** — `IConfiguration.Bind` does not invoke `[JsonConverter]`. Phase 7 did not regress this; `Validators: ["..."]` array binding works via primary-constructor reflection on `ActionEntry`, which is the documented operator-facing path. Fix deferred per CONTEXT-7 D13.

## Verdict rationale

All ROADMAP-listed Phase 7 deliverables met or exceeded. All 13 CONTEXT-7 decisions (D1–D13) honored. All architecture invariants hold. Test cushion (143/143, +19 over Phase 6 baseline) clears the +30% bar above the ≥10 ROADMAP gate by 90%. Phase verifier verdict: **COMPLETE — proceed to security audit + simplifier + documenter (DONE) and to phase-close commit + checkpoint.**
