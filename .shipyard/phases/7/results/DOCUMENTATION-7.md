# Phase 7 — Documentation Review

**Verdict:** 4 actionable CLAUDE.md gaps, 1 README/migration doc note for Phase 11.
**Date:** 2026-04-26

## Code documentation

All new public types in `FrigateRelay.Plugins.CodeProjectAi` carry XML doc comments suitable for the generated `bin/Release/net10.0/FrigateRelay.Plugins.CodeProjectAi.xml` (per the csproj's `<GenerateDocumentationFile>true</GenerateDocumentationFile>`). The `validator_rejected` log message structure is documented inline in the `LogValidatorRejected` definition. `SnapshotContext`'s new `PreResolved` ctor has remarks explaining the "explicitly null" vs `default(SnapshotContext)` distinction.

**No code-doc gaps.**

## CLAUDE.md gaps (actionable now or at Phase 11)

### CLAUDE-1 (HIGH — actionable now) — `## Conventions` should capture the validator/action retry asymmetry

Add to `## Conventions`:

> - **Action plugins retry; validators do not.** `IActionPlugin` HttpClients use `AddResilienceHandler` with the 3/6/9-second Polly delay generator (Phase 4 D7 / Phase 6 Pushover precedent). `IValidationPlugin` HttpClients **must NOT** add a resilience handler — pre-action gates with retries would systematically delay every notification by 18s. Validators use a single timeout and fail-closed/open per their `OnError` option (Phase 7 CONTEXT-7 D4). When adding a new plugin, decide its category first; the registrar shape diverges right at the `AddHttpClient(...)` chain.

### CLAUDE-2 (HIGH — actionable now) — `## Conventions` should capture the keyed-validator-instance pattern

Add to `## Conventions`:

> - **Validator instances use `AddKeyedSingleton<IValidationPlugin>(key, factory)` + `AddOptions<T>(name).Bind(section)`.** Each named entry under the top-level `Validators` config dict becomes one keyed instance with its own bound options; the per-plugin registrar enumerates `Configuration.GetSection("Validators").GetChildren()` and filters by `Type == "{ownType}"` so multiple validator types coexist without collision. Resolution is via `IServiceProvider.GetRequiredKeyedService<IValidationPlugin>(key)` at `EventPump` dispatch time (Phase 7 CONTEXT-7 D2 / RESEARCH §2). Anticipates the Phase 8 Profiles config shape — the dictionary form ports forward unchanged.

### CLAUDE-3 (HIGH — actionable now) — `## Conventions` should capture the `partial class` requirement for `[LoggerMessage]` source-gen

Add to `## Conventions`:

> - **Modern logger source-gen requires `partial class`.** Plugins using `[LoggerMessage]` attributes on partial methods must declare the OUTER class as `partial` (not just the nested `Log` class). The compiler error CS0260 ("Missing partial modifier") fires if a non-partial class hosts a partial nested class. Precedent: `CodeProjectAiValidator` is `sealed partial class` for exactly this reason. Plugins using the older `LoggerMessage.Define<...>` static-field style do NOT need partial (precedent: BlueIris, Pushover) — that style is fine and comparable; pick one consistently per plugin.

### CLAUDE-4 (MEDIUM — actionable now) — `## Architecture invariants` should reference the `SnapshotContext.PreResolved` sharing path

Add to `## Architecture invariants`:

> - **The dispatcher pre-resolves snapshots once when validators are present.** `ChannelActionDispatcher.ConsumeAsync` resolves the `SnapshotContext` ONE time via the resolver-backed ctor, then constructs a `new SnapshotContext(SnapshotResult? preResolved)` and passes it to BOTH the validator chain and the action plugin. `SnapshotContext.ResolveAsync` returns the cached result without re-invoking the underlying provider in this case. When NO validators are present, the dispatcher passes the resolver-backed `SnapshotContext` directly — action plugins resolve lazily, and BlueIris-only subscriptions pay zero snapshot fetch cost. Plugins MUST always go through `SnapshotContext.ResolveAsync` (never store an `ISnapshotProvider` reference and call it directly).

### CLAUDE-5 (LOW — defer to Phase 11) — README plugin-author guide for `IValidationPlugin`

The Phase 11 OSS-polish plan calls for a `docs/plugin-author-guide.md` covering each contract interface with a code sample. Phase 7 introduces the `IValidationPlugin` SnapshotContext extension, the keyed-services + named-options registrar pattern, the validator/action retry asymmetry, and the FailClosed/FailOpen stance. These all need to land in the plugin-author guide when Phase 11 ships.

**Track in `.shipyard/ISSUES.md` if not already covered by Phase 11 ROADMAP.**

## Migration / operator docs (deferred to Phase 12)

Phase 12 parity cutover doc must include:
- The new top-level `Validators` config block shape.
- The `ActionEntry.Validators` referencing pattern.
- The `Snapshots:DefaultProviderName = "Frigate"` requirement when using a CodeProjectAi validator on Pushover (so the validator gets snapshot bytes).
- The retry-asymmetry note (operators expect Pushover to retry on transient failures, but the validator gate fails-closed in 5s — they should size CodeProject.AI for predictable response time).

## Verdict rationale

CLAUDE-1 through CLAUDE-4 capture conventions that future contributors WILL trip over (the `partial class` requirement is exactly the CS0260 I hit during the build; the validator/action retry asymmetry is operator-visible). All are non-trivial to re-derive from the code. **Recommend applying CLAUDE-1, -2, -3, -4 inline now** as part of the phase-close commit — they are short, factual additions matching the existing CLAUDE.md style. CLAUDE-5 + Phase 12 migration items: defer to their respective phase plans.

## Recommended action

User decision per Phase 5/6 precedent — apply now or defer to Phase 11 OSS-polish docs sprint. Phase 5 + Phase 6 deferred their CLAUDE.md edits to Phase 11; Phase 7 has 4 high-priority items, two of which (CLAUDE-1, CLAUDE-3) are operator/contributor productivity hits (retry-asymmetry behavior + the CS0260 `partial class` trap). Suggest applying these two now, deferring CLAUDE-2 and CLAUDE-4 to the Phase 11 docs sprint.