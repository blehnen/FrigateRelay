# Phase 7 — CodeProject.AI Validator — Discussion Capture

**Date:** 2026-04-26
**Phase goal (from ROADMAP):** `IValidationPlugin` contract wired into per-action validator chains with proven short-circuit semantics. First validator implementation: `CodeProjectAiValidator` with confidence + allowed-labels gates.

This file captures user decisions made during planning. Downstream agents (researcher, architect) work from these as locked-in constraints.

---

## D1 — Validator-snapshot relationship

**Decision:** Extend `IValidationPlugin.ValidateAsync` to take a `SnapshotContext`, mirroring Phase 6 ARCH-D2 for `IActionPlugin.ExecuteAsync`.

**New signature:**
```csharp
public interface IValidationPlugin
{
    string Name { get; }
    Task<Verdict> ValidateAsync(
        EventContext ctx,
        SnapshotContext snapshot,   // NEW
        CancellationToken ct);
}
```

**Dispatcher consumer body shape (target):**
```csharp
var snap = new SnapshotContext(_resolver, ctx, perAction, perSub);
foreach (var v in item.Validators)
{
    var verdict = await v.ValidateAsync(ctx, snap, ct);
    if (!verdict.Passed)
    {
        Log.ValidatorRejected(_logger, ctx.EventId, plugin.Name, v.Name, verdict.Reason);
        return; // short-circuit THIS action only; other actions in the same event proceed
    }
}
await plugin.ExecuteAsync(ctx, snap, ct);
```

**Rationale.** Validator and action share **one** resolved snapshot per dispatch — no double-fetch of the same JPEG. Tier semantics (per-action override → per-subscription default → global) flow through `SnapshotContext` exactly as they do for actions. Additive change to Abstractions; consistent with the established pattern.

**Implications.**
- `SnapshotContext` already exists in `FrigateRelay.Abstractions` (Phase 6 ARCH-D2). No new type.
- `IValidationPlugin` signature change is a breaking change at the `Abstractions` boundary, but the only existing implementation is the Phase 4 D4 placeholder (empty `IReadOnlyList<IValidationPlugin>`) — there are no real validators to migrate yet. Stub validators in tests will need a one-line update.
- The dispatcher resolves snapshot **once** per dispatch and shares the resulting `SnapshotContext`; `SnapshotContext.ResolveAsync` itself caches lazily on first call (existing behavior), so the validator's call and the action's call hit the same `SnapshotResult` even when both call `ResolveAsync(ctx, ct)`.

---

## D2 — Validator config shape

**Decision:** Top-level `Validators` dict + `ActionEntry.Validators` references by key.

**Config shape:**
```jsonc
{
  "Validators": {
    "strict-person": {
      "Type": "CodeProjectAi",
      "BaseUrl": "http://codeproject-ai:5000",
      "MinConfidence": 0.7,
      "AllowedLabels": ["person"],
      "OnError": "FailClosed",
      "Timeout": "00:00:05"
    },
    "lax-vehicle": {
      "Type": "CodeProjectAi",
      "BaseUrl": "http://codeproject-ai:5000",
      "MinConfidence": 0.4,
      "AllowedLabels": ["car", "truck"],
      "OnError": "FailClosed"
    }
  },
  "Subscriptions": [
    {
      "Camera": "front_door",
      "Labels": ["person"],
      "Actions": [
        { "Plugin": "BlueIris" },
        {
          "Plugin": "Pushover",
          "SnapshotProvider": "Frigate",
          "Validators": ["strict-person"]
        }
      ]
    }
  ]
}
```

**Implications.**
- `ActionEntry` gains `IReadOnlyList<string>? Validators = null` (optional, default empty). `ActionEntryJsonConverter` extends to read the `Validators` field.
- A new top-level `Validators: Dictionary<string, ValidatorInstanceOptions>` config section binds at host startup. Each entry has a required `Type` discriminator (e.g. `"CodeProjectAi"`) plus type-specific options.
- `IValidationPlugin.Name` returns the **instance key** (e.g. `"strict-person"`), not the validator type. The `Type` field selects which validator class to instantiate.
- DI registration: each plugin registrar (e.g. `FrigateRelay.Plugins.CodeProjectAi.PluginRegistrar`) iterates the top-level `Validators` dict, picks entries with its own `Type` value, and registers a keyed `IValidationPlugin` per entry. Multiple validator types can coexist; each registrar handles only its own `Type`.
- Startup fail-fast: every key referenced in any `ActionEntry.Validators` must exist in the top-level `Validators` dict, AND its `Type` must match a registered validator plugin. `StartupValidation.ValidateValidators` (new method, mirrors `ValidateActions` and `ValidateSnapshotProviders`) runs after `builder.Build()`.
- The keyed-instance pattern naturally supports per-label tuning (one named instance per label tier), so a per-label `MinConfidence` dictionary is not needed in v1.
- This shape anticipates Phase 8 Profiles cleanly — Profiles will reference validator instance keys the same way.

**Architect lock-in:** Per-instance options binding mirrors how `BlueIrisOptions` / `PushoverOptions` are bound today (`AddOptions<T>().Bind(...)`), but **keyed** (`builder.Services.AddKeyedSingleton<IValidationPlugin>(key, ...)`). Researcher must verify the .NET 10 keyed-services binding pattern for option groups.

---

## D3 — Confidence threshold scope (resolved by D2)

**Decision (architect lock-in, no user question needed):** Per-instance scalar `MinConfidence: double` + `AllowedLabels: string[]`. Per-label confidence dict is **not** part of v1 — operators express per-label thresholds by creating multiple named validator instances under the top-level `Validators` dict (D2).

**Rationale.** D2's keyed-instance shape obviates the need for a nested per-label dict. YAGNI: a single `MinConfidence` covers every legacy behavior (legacy code used a single 50% threshold for person/car/truck combined).

---

## D4 — Validator failure stance on HTTP timeout / network error

**Decision:** Configurable per validator instance via `OnError: ValidatorErrorMode { FailClosed, FailOpen }`. Default `FailClosed` (matches legacy intent: don't notify if you can't confirm).

**Behavior.**
- `OnError = FailClosed` (default): on `HttpRequestException`, `TaskCanceledException` (timeout), or non-success HTTP status, return `Verdict.Fail("validator_unavailable: {detail}")`. Action is skipped. `validator_rejected` log emitted at Warning.
- `OnError = FailOpen`: on the same conditions, return `Verdict.Pass()`. Action proceeds. `validator_unavailable` log emitted at Warning so the bypass is visible.
- Caller-driven `OperationCanceledException` (host shutdown via `ct.IsCancellationRequested`) is **not** treated as validator failure — it bubbles up. Only timeout (`TaskCanceledException` with `!ct.IsCancellationRequested`) maps to validator unavailability.

**Architect lock-in.** **No Polly retry handler** on the validator's `HttpClient`. Validators are pre-action gates; per-attempt retry latency (3+6+9=18s with the action plugin's policy) would systematically delay every notification. Single 5-second timeout, no retries, fail-closed/open per `OnError`. **This is asymmetric with `BlueIrisActionPlugin` and `PushoverActionPlugin`, which both retry** — surface it explicitly in the registrar code comment AND in CLAUDE.md observability section.

---

## D5 — Zone-of-interest bbox filter (deferred from v1)

**Decision:** Defer bbox zone-of-interest to a later phase. v1 ships with `MinConfidence` + `AllowedLabels` only.

**Rationale.**
- Phase 3 already implements **subscription-level** zone matching against Frigate's `current_zones` arrays — operators have a working zone gate.
- Pixel-bbox-on-snapshot at the validator layer is a different mechanism (snapshot dimensions, overlap geometry) requiring its own test surface.
- ROADMAP lists bbox as **optional**, so deferral is permitted. Tracked for revisit at Phase 11 OSS polish if a real operator request surfaces.

**v1 `CodeProjectAiOptions` shape (concrete):**
```csharp
public sealed class CodeProjectAiOptions
{
    [Required, Url]
    public string BaseUrl { get; set; } = "";

    [Range(0.0, 1.0)]
    public double MinConfidence { get; set; } = 0.5;

    public string[] AllowedLabels { get; set; } = [];

    public ValidatorErrorMode OnError { get; set; } = ValidatorErrorMode.FailClosed;

    [Range(typeof(TimeSpan), "00:00:01", "00:01:00")]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    public bool AllowInvalidCertificates { get; set; } = false;
}

public enum ValidatorErrorMode { FailClosed, FailOpen }
```

---

## Inherited from earlier phases (architect lock-in, no user question)

- **D6 — Short-circuit semantics:** First failing verdict in the chain stops the rest of the chain for that action; the rejection is logged and the action is skipped. **Other actions in the same event fire independently.** Matches PROJECT.md V3.
- **D7 — `validator_rejected` structured log fields:** `event_id`, `camera`, `label`, `action`, `validator`, `reason`. Distinct `EventId` (numeric) per log message; `LoggerMessage.Define` source-gen pattern (matches Pushover/BlueIris precedent).
- **D8 — TLS skipping:** `AllowInvalidCertificates` flag on `CodeProjectAiOptions`. When `true`, plugin's named `HttpClient` configures a per-plugin `SocketsHttpHandler.SslOptions.RemoteCertificateValidationCallback` returning `true`. **Never** set globally. Mirrors Phase 4 D7 / Phase 6 Pushover treatment.
- **D9 — HTTP client:** `IHttpClientFactory` named client (`"CodeProjectAi"`), instance-keyed where multiple validator instances exist (each with own BaseUrl). Architect resolves whether a single named client + per-call BaseAddress override is cleaner than per-instance named clients.
- **D10 — Endpoint shape (legacy reference):** `POST {BaseUrl}/v1/vision/detection`, `multipart/form-data` with single `image` field carrying snapshot bytes. Response is JSON with `predictions: [{label, confidence, x_min, y_min, x_max, y_max}]` (CodeProject.AI v2.x docs to be verified by researcher). Validator iterates predictions, applies AllowedLabels filter, applies MinConfidence threshold. Pass if **any** prediction passes both filters.
- **D11 — Dispatcher ordering inside `ChannelActionDispatcher.ConsumeAsync`:** snapshot resolve → validator chain → action execute. The Polly resilience pipeline wraps the action call only — validators bypass the action's retry policy (D4 lock-in).
- **D12 — Phase 6 `MultipartFormDataContent` quoting lesson applies:** .NET 10 default emits unquoted `name=` parameters (not `name="..."` like older versions). Tests asserting raw multipart wire format must use unquoted form. Mentioned in CLAUDE.md `## Conventions`.
- **D13 — `IConfiguration.Bind` does NOT invoke `[JsonConverter]`:** the existing `ActionEntryJsonConverter` only fires for direct `JsonSerializer.Deserialize` paths (ID-12 in ISSUES.md). Phase 7 must verify that `Validators` array binding works correctly through `IConfiguration.Bind` — likely requires extending `ActionEntryJsonConverter` AND a custom `TypeConverter` if the converter path matters. Researcher to confirm.

---

## Open questions for researcher

1. **Keyed validator-instance options binding pattern in .NET 10.** Confirm that `builder.Services.AddOptions<CodeProjectAiOptions>(name).Bind(section)` + `builder.Services.AddKeyedSingleton<IValidationPlugin>(name, ...)` is the canonical shape. Alternative: `IOptionsSnapshot` keyed by name. Which is the right primitive for per-instance plugin options?
2. **CodeProject.AI API surface.** Confirm `POST /v1/vision/detection` is current as of CodeProject.AI v2.x. Confirm response schema fields (`predictions`, `success`, `processMs`, `inferenceMs`). Does the API support a confidence-threshold query parameter (so we filter server-side) or must we filter client-side? (Client-side is fine for v1; server-side is an optimization.)
3. **`ActionEntryJsonConverter` extension for `Validators`.** Read converter source; confirm `Validators` array can be added without breaking the legacy `["BlueIris"]` string-array fallback path (which already silently fails per ID-12, but we don't want to make it worse).
4. **Discriminator-based validator instantiation.** What's the cleanest way to bind `Validators: { "strict-person": { "Type": "CodeProjectAi", ... }, ... }` so each plugin registrar can pick its own `Type` and bind options? Options pattern with named instance + manual `IConfigurationSection.GetSection("Validators").GetChildren()` enumeration in each registrar?
5. **`IValidationPlugin` test stubs.** Find every existing test stub of `IValidationPlugin` (likely zero, but confirm) and prepare a one-line migration for the new `SnapshotContext` parameter.
6. **`SnapshotContext.ResolveAsync` caching guarantee.** Verify that calling `ResolveAsync` twice on the same `SnapshotContext` value (validator + action) hits the underlying provider only once. If not, dispatcher must call once and pass the result around.

---

## Test count target

ROADMAP gates Phase 7 at:
- ≥ 1 integration test (`Validator_ShortCircuits_OnlyAttachedAction`) — locked in as success criterion #1.
- ≥ 8 unit tests across the validator suite.
- + 1 second integration test (`Validator_Pass_BothActionsFire`) — locked in as success criterion #3.

Architect target: **≥ 12 unit tests + 2 integration tests** (cushion above gate). Distribution likely:
- `CodeProjectAiValidator` unit tests: confidence pass/fail, allowed-label gate, no-prediction response, FailClosed timeout, FailOpen timeout, multipart wire shape, response parsing happy path, `Verdict.Reason` content, AllowInvalidCertificates honored.
- `StartupValidation.ValidateValidators` unit tests: undefined key fail-fast, unknown Type fail-fast, valid configuration accepts.
- Dispatcher unit test: validator chain short-circuit on first fail.
- Integration tests as above.

---

## Wave shape (architect-pending, suggested)

- **Wave 1 (parallel):** PLAN-1.1 `IValidationPlugin` signature + dispatcher consumer body + `ChannelActionDispatcher` validator-loop wiring; PLAN-1.2 `ActionEntry.Validators` field + `ActionEntryJsonConverter` extension + top-level `Validators` config binding + `ValidatorInstanceOptions` discriminator type.
- **Wave 2:** PLAN-2.1 `FrigateRelay.Plugins.CodeProjectAi/` project + `CodeProjectAiValidator` + `CodeProjectAiOptions` + plugin registrar reading top-level `Validators` and registering keyed instances + unit tests.
- **Wave 3:** PLAN-3.1 `StartupValidation.ValidateValidators` + `HostBootstrap` registrar wiring + `MqttToValidatorTests.Validator_ShortCircuits_OnlyAttachedAction` + `MqttToValidatorTests.Validator_Pass_BothActionsFire` integration tests.

Architect may restructure; this is a starting suggestion only.

---

**Decisions locked. Researcher dispatch next.**
