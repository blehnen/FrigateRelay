# Phase 7 — Plan Critique

**Initial verdict:** CAUTION
**Final verdict (post-orchestrator fixes):** READY
**Date:** 2026-04-26

---

## Resolution log (orchestrator applied fixes inline)

All three plan-text errors flagged below were **fixed in-place** by the orchestrator after critique completion (no architect revision cycle dispatched — the corrections were pinpoint string substitutions, not design changes):

1. **PLAN-1.1 line 154 (`_resolver` → `_snapshotResolver`):** Fixed. Dispatcher pseudo-code now uses the correct field name (`ChannelActionDispatcher.cs:43` confirms `_snapshotResolver`). Added an inline comment clarifying that `SnapshotContext`'s own backing field is `_resolver` (different file) so future readers don't introduce the same confusion. Also wrapped the construction in a `_snapshotResolver is null ? default : new SnapshotContext(...)` guard, matching the existing dispatcher's pattern at line 177–179.
2. **PLAN-3.1 path `Configuration/StartupValidation.cs` → `StartupValidation.cs`:** Fixed in 3 places — frontmatter `files_touched`, Task 2 `Files:` line, and the `git grep` acceptance criterion. The on-disk file is at `src/FrigateRelay.Host/StartupValidation.cs` (no `Configuration/` subdirectory, verified). Added a "note: NOT in `Configuration/` subdir" hint inline.
3. **PLAN-3.1 precedent file `MqttToActionsTests.cs` → `MqttToBothActionsTests.cs`:** Fixed in 3 places — Task 3 description, log-assertion criterion, and the "Notes for builder" precedent reference. Confirmed against `tests/FrigateRelay.IntegrationTests/MqttToBothActionsTests.cs` on disk.
4. **Spec-compliance minor gap (`FrigateRelay.Host.csproj` missing from PLAN-3.1 `files_touched`):** Fixed. The csproj edit (adding `<ProjectReference>` to `FrigateRelay.Plugins.CodeProjectAi`) is described in Task 3 prose; frontmatter now matches.

The original CAUTION findings are preserved below for audit-trail traceability — they describe the state of the plan files BEFORE the post-critique fixes.

---

## Per-plan findings

### PLAN-1.1

**File path checks**

| File | Status | Evidence |
|------|--------|----------|
| `src/FrigateRelay.Abstractions/IValidationPlugin.cs` | EXISTS | Read confirmed. 2-param signature: `ValidateAsync(EventContext ctx, CancellationToken ct)` — plan correctly targets this for the 3-param change. |
| `src/FrigateRelay.Abstractions/SnapshotContext.cs` | EXISTS | Read confirmed. Readonly struct, one existing constructor `(ISnapshotResolver, string?, string?)`. No `required init` properties — `[SetsRequiredMembers]` note in plan is moot but harmless. |
| `src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs` | EXISTS | Read confirmed. `ConsumeAsync` exists; current body does NOT run any validator chain — `Array.Empty<IValidationPlugin>()` is passed at the `EventPump` call site, not inside the dispatcher. |
| `tests/FrigateRelay.Abstractions.Tests/SnapshotContextTests.cs` | EXISTS | 5 tests present. Plan says "create file if absent" — file already exists with a `StubResolver` inner class. Builder must add 2 new tests WITHOUT touching the existing 5. |
| `tests/FrigateRelay.Host.Tests/Dispatch/ChannelActionDispatcherTests.cs` | EXISTS | 6 tests present. PLAN-1.1 Task 3 claims "existing 8 dispatcher tests" — only 6 exist (plus `RetryDelayGeneratorFormula` in the same file = 6 `[TestMethod]` attributes counted). The count discrepancy is minor but the plan's test-count claim is stale. |

**API surface checks**

- `IValidationPlugin.ValidateAsync` current signature: `(EventContext ctx, CancellationToken ct)` — confirmed. Plan adds `SnapshotContext snapshot` as second param. Correct target.
- `DispatchItem` already has `IReadOnlyList<IValidationPlugin> Validators` at position 3. PLAN-1.1 Task 3 states "verify by reading `DispatchItem.cs`" — field confirmed present.
- `SnapshotContext` has NO `_hasPreResolved` field yet. Plan adds it correctly as an additive change.
- `SnapshotResult` is a `sealed record` with `required` properties (`Bytes`, `ContentType`, `ProviderName`). The plan's test snippet `new SnapshotResult(/* …minimal valid */)` is shorthand — builder must use object-initializer form, e.g. `new SnapshotResult { Bytes = [], ContentType = "image/jpeg", ProviderName = "test" }`. The plan does not spell this out; builder must verify the precedent in `SnapshotContextTests.cs` line 68 (which already shows the correct form).
- `SnapshotContext.ResolveAsync` currently takes `(EventContext context, CancellationToken ct)` — PLAN-1.1 Task 1 snippets show the same signature. Consistent.

**ISSUE — SnapshotContext pre-resolve decision vs. current ConsumeAsync structure:**
The current `ConsumeAsync` builds the `SnapshotContext` inline:
```csharp
var snapshotCtx = _snapshotResolver is null
    ? default
    : new SnapshotContext(_snapshotResolver, item.PerActionSnapshotProvider, item.SubscriptionSnapshotProvider);
await plugin.ExecuteAsync(item.Context, snapshotCtx, ct).ConfigureAwait(false);
```
PLAN-1.1 Task 3 snippet replaces this whole block with the pre-resolve + validator loop logic. This is structurally correct. However, the plan's snippet uses `_resolver` (the field name in its pseudocode) but the actual field name in `ChannelActionDispatcher` is `_snapshotResolver`. Builder must use the correct field name.

**Verify command checks**

All commands are concrete, runnable, and reference correct paths. The `git grep -n "SnapshotContext snapshot" src/FrigateRelay.Abstractions/IValidationPlugin.cs` command correctly validates the new signature.

**Forward-ref checks**

PLAN-1.1 modifies `IValidationPlugin` and `SnapshotContext` (Abstractions) plus `ChannelActionDispatcher` (Host/Dispatch). PLAN-1.2 modifies `ActionEntry` (Host/Configuration) and `ActionEntryJsonConverter`. No file overlap — parallel-safe confirmed.

PLAN-1.1 Task 3 references `item.Validators` on `DispatchItem` — this field already exists (confirmed). No dependency on PLAN-1.2.

**Issues:**
1. Test count claim "existing 8 dispatcher tests" is stale — 6 exist. Minor; does not block.
2. Field name `_resolver` in Task 3 pseudocode should be `_snapshotResolver` (actual field name in `ChannelActionDispatcher`). Builder must correct.
3. `SnapshotContextTests.cs` already exists — plan says "create file if absent". No action needed but builder should not overwrite existing tests.

---

### PLAN-1.2

**File path checks**

| File | Status | Evidence |
|------|--------|----------|
| `src/FrigateRelay.Host/Configuration/ActionEntry.cs` | EXISTS | `public sealed record ActionEntry(string Plugin, string? SnapshotProvider = null)` — 2-param. Plan adds 3rd param. |
| `src/FrigateRelay.Host/Configuration/ActionEntryJsonConverter.cs` | EXISTS | Private DTO is `ActionEntryDto(string Plugin, string? SnapshotProvider = null)`. Plan extends both. Confirmed. |
| `tests/FrigateRelay.Host.Tests/Configuration/ActionEntryJsonConverterTests.cs` | EXISTS | 5 tests present (confirmed). Plan adds 2 more — additive. |

**API surface checks**

- The existing `ActionEntryDto` uses a positional record `(string Plugin, string? SnapshotProvider = null)`. Plan's extension to add `IReadOnlyList<string>? Validators = null` as third positional param is straightforward.
- `ActionEntryJsonConverter.Read` object-form path calls `JsonSerializer.Deserialize<ActionEntryDto>(ref reader, options)` — extending the DTO record is sufficient to pick up the new field automatically. Plan is correct.
- `ActionEntryJsonConverter.Write` currently omits `SnapshotProvider` when null; plan adds parallel logic for `Validators`. Structurally mirrors existing code.

**ID-12 mitigation check**

Plan CORRECTLY notes that `IConfiguration.Bind` does not invoke `[JsonConverter]` and that `IReadOnlyList<string>? Validators` binds via the record's primary-constructor reflection path. This is the correct analysis. No `TypeConverter` is needed for the Validators field because it's a `List<string>` which the configuration binder handles natively. Plan does not conflate ID-12's `ActionEntry`-level `TypeConverter` gap with the new `Validators` field binding — the scoping is correct.

**Verify command checks**

`dotnet run --project tests/FrigateRelay.Host.Tests -c Release` — correct. `git grep -n "Validators" src/FrigateRelay.Host/Configuration/` — correct.

**Forward-ref checks**

PLAN-1.2 is parallel with PLAN-1.1. No shared files. PLAN-1.2 does NOT reference `IValidationPlugin` or `SnapshotContext`. The `ActionEntry.Validators` field is a `IReadOnlyList<string>?` (keys), not `IReadOnlyList<IValidationPlugin>` — the resolution from key to plugin instance happens in PLAN-3.1. No implicit ordering dependency on PLAN-1.1.

**Issues:** None blocking. Plans are parallel-safe.

---

### PLAN-2.1

**File path checks**

All files are new creates:
- `src/FrigateRelay.Plugins.CodeProjectAi/` — directory does not exist; builder creates.
- `tests/FrigateRelay.Plugins.CodeProjectAi.Tests/` — directory does not exist; builder creates.
- `FrigateRelay.sln` — exists; builder adds both new projects.

**API surface checks**

- `IValidationPlugin.ValidateAsync(EventContext, SnapshotContext, CancellationToken)` — this is the NEW signature from PLAN-1.1. PLAN-2.1 depends on PLAN-1.1 completing first. Dependency correctly declared.
- `CodeProjectAiValidator.ValidateAsync` snippet uses `snapshot.ResolveAsync(ctx, ct)` — matches the `SnapshotContext.ResolveAsync(EventContext, CancellationToken)` signature. Correct.
- `Verdict.Pass()` and `Verdict.Fail(string)` — confirmed present and correct (private ctor, static factories only).
- `PluginRegistrar` uses `AddKeyedSingleton<IValidationPlugin>` — valid .NET 8+ API, available in .NET 10. Correct.
- `IOptionsMonitor<CodeProjectAiOptions>.Get(instanceKey)` pattern — standard named-options. Correct.
- `CodeProjectAiOptions.AllowedLabels` is typed as `string[]` in the plan code (`.Length > 0` check). This is fine but slightly inconsistent with `IReadOnlyList<string>` used elsewhere; not a blocking issue.

**CAUTION — `SnapshotResult.Bytes` is `byte[]`, not `ReadOnlyMemory<byte>`:**
PLAN-2.1 Task 2 `BuildMultipart` signature uses `ReadOnlyMemory<byte> bytes`, but `SnapshotResult.Bytes` is `required byte[] Bytes` (confirmed in `SnapshotResult.cs`). The plan's `EvaluatePredictions` call site passes `snap.Bytes` to `BuildMultipart(snap.Bytes)` where `snap.Bytes` is `byte[]`. `byte[]` is implicitly convertible to `ReadOnlyMemory<byte>`, so this compiles without error. Not a bug, but the type mismatch in the parameter declaration may confuse the builder. CAUTION only.

**CI auto-discovery check**

`.github/scripts/run-tests.sh` uses `find tests -maxdepth 2 -name '*Tests.csproj' -type f | sort` (line 44 confirmed). The new project `tests/FrigateRelay.Plugins.CodeProjectAi.Tests/FrigateRelay.Plugins.CodeProjectAi.Tests.csproj` matches `*Tests.csproj` at depth 2 — **auto-discovered automatically**. No edits to `ci.yml`, `Jenkinsfile`, or `run-tests.sh` are needed.

PLAN-2.1 Task 1 says "verify the auto-discovery glob... if hardcoded, append..." — the script is already auto-discovering (Phase 3 extraction confirmed). The plan's conditional instruction is correct and will result in a no-op edit being required. This is fine.

The `ci.yml` delegates entirely to `run-tests.sh` (confirmed lines 57-61) — no hardcoded project list in CI. Jenkinsfile also delegates to `run-tests.sh --coverage` (line 56). Both are correct.

**Issues:**

1. `ReadOnlyMemory<byte>` vs `byte[]` mismatch in `BuildMultipart` — compiles but the plan snippet is imprecise. CAUTION.
2. `CodeProjectAiOptions.AllowedLabels` typed as `string[]` in the validator snippet but plan text says it's from `CodeProjectAiOptions` (CONTEXT-7 D5). Builder must check the exact type when implementing `CodeProjectAiOptions.cs` to ensure consistency.

---

### PLAN-3.1

**File path checks**

| File | Status | Evidence |
|------|--------|----------|
| `src/FrigateRelay.Host/EventPump.cs` | EXISTS | `Array.Empty<IValidationPlugin>()` placeholder at line 97 confirmed. Plan says "verify line via Read" — line is 97. |
| `src/FrigateRelay.Host/Configuration/StartupValidation.cs` | EXISTS AT `src/FrigateRelay.Host/StartupValidation.cs` | **PATH MISMATCH.** Plan lists `src/FrigateRelay.Host/Configuration/StartupValidation.cs` in `files_touched` frontmatter AND in Task 2. Actual file is `src/FrigateRelay.Host/StartupValidation.cs` (no `Configuration/` subdirectory). The plan body text also says "Find existing `StartupValidation.cs` location via Read" but the frontmatter hardcodes the wrong path. Builder MUST use the correct path. |
| `src/FrigateRelay.Host/HostBootstrap.cs` | EXISTS | Read confirmed. `ValidateStartup` method exists and calls `ValidateActions` and `ValidateSnapshotProviders`. |
| `tests/FrigateRelay.Host.Tests/Configuration/StartupValidationTests.cs` | DOES NOT EXIST | No file at this path. The existing snapshot validation tests are in `tests/FrigateRelay.Host.Tests/Configuration/StartupValidationSnapshotTests.cs`. Plan should either create `StartupValidationTests.cs` (new file) or add to `StartupValidationSnapshotTests.cs`. The plan correctly says "create if absent" in Task 2 — just the naming needs to align. |
| `tests/FrigateRelay.IntegrationTests/MqttToValidatorTests.cs` | DOES NOT EXIST | New file; builder creates. The existing precedent is `MqttToBothActionsTests.cs` (NOT `MqttToActionsTests.cs` as the plan references in Task 3). Plan says "Read `MqttToActionsTests.cs` for the precedent" — that file does not exist. The correct precedent file is `tests/FrigateRelay.IntegrationTests/MqttToBothActionsTests.cs`. |

**API surface checks**

- `EventPump` constructor currently does NOT inject `IServiceProvider`. PLAN-3.1 Task 1 requires injecting it. The current constructor params are: `IEnumerable<IEventSource>`, `DedupeCache`, `IOptionsMonitor<HostSubscriptionsOptions>`, `IActionDispatcher`, `IEnumerable<IActionPlugin>`, `ILogger<EventPump>`. Adding `IServiceProvider` as a new param is a compile-time change that will also affect `EventPumpTests.cs` and `EventPumpDispatchTests.cs` constructors. Plan says "those callers must be updated" — this is the right note, but builder must actually find all callers. Let us verify:
  - `HostBootstrap.cs` does not manually construct `EventPump` (it registers it via `AddHostedService<EventPump>()`), so DI will inject `IServiceProvider` automatically.
  - `tests/FrigateRelay.Host.Tests/EventPumpTests.cs` and `EventPumpDispatchTests.cs` — these construct `EventPump` directly and will need the new param added. The plan acknowledges this ("if `EventPump` is constructed manually anywhere — including tests — those callers must be updated").
- `HostBootstrap.ValidateStartup` signature is `(IServiceProvider services)` — confirmed. Plan's wiring snippet `StartupValidation.ValidateValidators(host.Services, host.Services.GetRequiredService<IOptions<HostSubscriptionsOptions>>())` is consistent with how `ValidateActions` and `ValidateSnapshotProviders` are called today. However, the current `ValidateStartup` does NOT take `IHost` — it takes `IServiceProvider`. The plan's snippet uses `host.Services` — this is written as if the caller has an `IHost`. Looking at the current code: `HostBootstrap.ValidateStartup(IServiceProvider services)` (line 83). The plan's `host.Services.GetRequiredService<IOptions<HostSubscriptionsOptions>>()` — here `host` means the local variable in whatever calls `ValidateStartup`. The integration test precedent (`MqttToBothActionsTests.cs` line 71) calls `HostBootstrap.ValidateStartup(app.Services)` where `app` is the built `IHost`. So `host.Services` in the plan means the `services` parameter already in scope at the call site in `ValidateStartup`. The plan is correct — the call would be:
  ```csharp
  StartupValidation.ValidateValidators(services, services.GetRequiredService<IOptions<HostSubscriptionsOptions>>());
  ```
  This is the correct translation of the plan's intent.

**Phase 5 dead-code regression check**

PLAN-3.1 Task 2 explicitly flags the Phase 5 regression risk: "`ValidateSnapshotProviders` was DEAD CODE — defined but never called from `HostBootstrap.ValidateStartup`." The plan mandates:
1. Implement `ValidateValidators` in `StartupValidation.cs`.
2. Wire it in `HostBootstrap.ValidateStartup`.
3. Acceptance criterion: `git grep -n "ValidateValidators" src/FrigateRelay.Host/HostBootstrap.cs` must match.

This is a sufficient acceptance criterion — it will catch the dead-code failure mode. The plan also requires a manual trace after wiring. The regression guard is adequate.

**Integration test precedent file name:**
Plan Task 3 says "Mirror Phase 6's `tests/FrigateRelay.IntegrationTests/MqttToActionsTests.cs`" and "Read it first for Testcontainers + WireMock setup." The actual file is `MqttToBothActionsTests.cs`. This is a concrete incorrect file path. Builder will either find the file by browsing the directory or encounter a "file not found" error when following the plan literally.

**Verify command checks**

All verification commands are correct and runnable. `git grep -n "ValidateValidators" src/FrigateRelay.Host/` with a space at the end finds both definition and call sites in the host directory — correct. `dotnet run --project tests/FrigateRelay.IntegrationTests -c Release` requires Docker — plan correctly notes this.

**Issues:**

1. **BLOCKING PATH ERROR:** `StartupValidation.cs` is at `src/FrigateRelay.Host/StartupValidation.cs`, NOT `src/FrigateRelay.Host/Configuration/StartupValidation.cs`. Both `files_touched` frontmatter and Task 2 body carry the wrong path.
2. **WRONG PRECEDENT FILE:** Plan references `MqttToActionsTests.cs` as the integration-test precedent. Correct file is `MqttToBothActionsTests.cs`.
3. `EventPumpTests.cs` and `EventPumpDispatchTests.cs` callers of `EventPump` constructor will need updating when `IServiceProvider` is added. Plan acknowledges this but does not list those files in `files_touched`. Builder should add them.

---

## Cross-plan checks

### Wave 1 parallel safety

PLAN-1.1 touches: `IValidationPlugin.cs`, `SnapshotContext.cs`, `ChannelActionDispatcher.cs`, `SnapshotContextTests.cs`, `ChannelActionDispatcherTests.cs`.
PLAN-1.2 touches: `ActionEntry.cs`, `ActionEntryJsonConverter.cs`, `ActionEntryJsonConverterTests.cs`.

Zero file overlap — parallel-safe confirmed.

PLAN-1.1's dispatcher changes reference `item.Validators` (already on `DispatchItem`) — no dependency on PLAN-1.2's `ActionEntry.Validators`. The two `Validators` concepts (plugin instances in the dispatch item vs. key strings in the config record) are independent. Parallel execution is safe.

### Wave 2 dependency on Wave 1

PLAN-2.1 depends on:
- PLAN-1.1 for `IValidationPlugin.ValidateAsync(EventContext, SnapshotContext, CancellationToken)` signature. If PLAN-1.1 is not complete, PLAN-2.1's `CodeProjectAiValidator : IValidationPlugin` will not compile.
- PLAN-1.2 for `ActionEntry.Validators` (referenced by the registrar's startup-validation chain, but not by PLAN-2.1's code directly). Dependency correctly declared as advisory only.

Dependency ordering is correct.

### Wave 3 dependency on Wave 1 + 2

PLAN-3.1 depends on:
- PLAN-1.1: `ChannelActionDispatcher` validator chain wiring.
- PLAN-1.2: `ActionEntry.Validators` key field.
- PLAN-2.1: `FrigateRelay.Plugins.CodeProjectAi.PluginRegistrar` and keyed `IValidationPlugin` services.

All correctly declared. Wave 3 must not execute until Waves 1 and 2 are both merged and building clean.

### ID-12 mitigation

PLAN-1.2 correctly acknowledges ID-12 and correctly scopes the fix out. The `IReadOnlyList<string>? Validators` field on `ActionEntry` binds via `IConfiguration.Bind`'s primary-constructor reflection path (not through `[JsonConverter]`), because `List<string>` is a natively supported collection type for the binder. This is correctly analyzed. No action needed.

The plan's note that operators must use the object form (`{"Plugin":"…", "Validators":["…"]}`) is correct and consistent with ID-12's operator guidance.

### Phase 5 dead-code regression mode

PLAN-3.1 Task 2 has an explicit acceptance criterion requiring `git grep -n "ValidateValidators" src/FrigateRelay.Host/HostBootstrap.cs` to match. This provides a concrete grep-level guard against the Phase 5 regression. The verifier confirms this criterion is strong enough — a dead-code `ValidateValidators` defined in `StartupValidation.cs` but never called from `HostBootstrap.cs` would fail this grep.

Additionally, Task 3's integration test `Validator_ShortCircuits_OnlyAttachedAction` would fail if `ValidateValidators` were not wired (because the host would start with an undefined validator key and throw at startup, or not throw and then silently fire the vetoed action). This provides a second-layer behavioral guard.

### CI auto-discovery for new test project

Confirmed: `.github/scripts/run-tests.sh` uses `find tests -maxdepth 2 -name '*Tests.csproj' -type f | sort`. The new `tests/FrigateRelay.Plugins.CodeProjectAi.Tests/FrigateRelay.Plugins.CodeProjectAi.Tests.csproj` matches at depth 2. No CI/Jenkinsfile edits required. PLAN-2.1's conditional "if hardcoded, append..." instruction will result in a no-op — but this is harmless.

Note: `FrigateRelay.TestHelpers` (a Library project under `tests/`) does NOT match `*Tests.csproj` — it is correctly excluded from test discovery as intended.

### Snapshot double-fetch regression

PLAN-1.1 Task 3 correctly implements the conditional pre-resolve pattern:

```csharp
if (item.Validators.Count > 0)
{
    var preResolved = await initial.ResolveAsync(item.Context, ct).ConfigureAwait(false);
    shared = new SnapshotContext(preResolved);  // caches for validator + action
}
else
{
    shared = initial;  // action-only path: lazy resolve, no double-fetch risk
}
```

This is exactly the pattern from RESEARCH §5 Option A. For BlueIris-only subscriptions (zero validators), `shared = initial` — the `SnapshotContext` is resolver-backed and lazy. The snapshot fetch only occurs if/when `BlueIrisActionPlugin.ExecuteAsync` calls `snapshot.ResolveAsync(...)`. BlueIris ignores snapshots entirely, so the resolver is never called — no regression.

For subscriptions with validators, the snapshot is pre-resolved once, then shared via the `PreResolved` constructor. The `_hasPreResolved` guard in the new constructor ensures `default(SnapshotContext)` (which has `_resolver == null` AND `_hasPreResolved == false`) continues to return null — no accidental caching of null for the no-resolver case.

No double-fetch regression. Pattern is correct.

---

## Verdict rationale

The four plans are architecturally sound and correctly sequenced. Wave 1 is genuinely parallel-safe with no file overlap. The snapshot pre-resolve pattern correctly handles the no-validator path without regressing BlueIris-only subscriptions. CI auto-discovery is already in place. The ID-12 scoping and Phase 5 dead-code regression guards are adequate. Two concrete errors require builder attention before execution: (1) PLAN-3.1 hardcodes the wrong path for `StartupValidation.cs` (`Configuration/StartupValidation.cs` vs. the actual `StartupValidation.cs`) in both the frontmatter and the task body — if the builder follows the plan literally they will create a second file at the wrong location rather than modifying the existing one; (2) PLAN-3.1 references `MqttToActionsTests.cs` as the integration-test precedent but the actual file is `MqttToBothActionsTests.cs`. Additionally, PLAN-1.1's pseudocode uses the wrong field name `_resolver` (actual: `_snapshotResolver`) and its dispatcher test count claim is stale (claims 8, actual 6). These are CAUTION-level findings — all correctable at build time by a careful builder, but they are concrete errors in the plan text rather than vague risks.

---

## If CAUTION: documented risks with mitigations carried forward to builders

1. **PLAN-3.1 path error — `StartupValidation.cs`:** `files_touched` and Task 2 both list `src/FrigateRelay.Host/Configuration/StartupValidation.cs`. Correct path is `src/FrigateRelay.Host/StartupValidation.cs`. Builder MUST read the file at the correct path before editing. Do not create a second file.

2. **PLAN-3.1 wrong precedent file name:** Task 3 references `MqttToActionsTests.cs` as the integration-test precedent. Correct file is `tests/FrigateRelay.IntegrationTests/MqttToBothActionsTests.cs`. Builder should read the correct file for Testcontainers + WireMock setup patterns.

3. **PLAN-1.1 pseudocode field name typo:** Task 3 snippet uses `_resolver` in the `ConsumeAsync` rewrite. Actual field name in `ChannelActionDispatcher` is `_snapshotResolver`. Builder must use the correct name to avoid a compile error.

4. **PLAN-3.1 `files_touched` incomplete:** `EventPumpTests.cs` and `EventPumpDispatchTests.cs` will need constructor-call updates when `IServiceProvider` is injected into `EventPump`. These files are not listed in `files_touched`. Builder should anticipate and include these edits to keep the test suite green.

5. **PLAN-1.1 stale test count:** Task 3 says "existing 8 dispatcher tests" — 6 exist. Minor; does not block but may cause confusion when checking pass counts.

6. **PLAN-2.1 `SnapshotResult.Bytes` type:** `BuildMultipart` parameter is typed `ReadOnlyMemory<byte>` but `SnapshotResult.Bytes` is `byte[]`. The implicit conversion is valid in C#, but the plan snippet is imprecise. Builder should decide whether to use `byte[]` directly in the parameter signature for clarity.
