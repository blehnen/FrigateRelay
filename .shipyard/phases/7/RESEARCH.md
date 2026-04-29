# Phase 7 — Research — CodeProject.AI Validator

**Date:** 2026-04-26
**Status:** Researcher truncated at 22 tool uses with nothing written; orchestrator finished inline (consistent with Phase 1/3/4/5 truncation pattern). Web fetch of CodeProject.AI docs blocked by certificate verification error — legacy reference (`Source/FrigateMQTTMainLogic/Pushover.cs:103-141`) is the canonical API-shape source for v1; runtime verification deferred to integration-test execution.

This document resolves CONTEXT-7's six open questions and provides the architect with concrete patterns to lock into plans.

---

## 1. CodeProject.AI v2.x `/v1/vision/detection` API cookbook

### Endpoint

```
POST {BaseUrl}/v1/vision/detection
Content-Type: multipart/form-data
```

**Form fields (legacy reference, Pushover.cs:113-115):**
- `image` — required. Snapshot bytes. Multipart filename `snapshot.jpg`. Content-Type `image/jpeg`.
- `min_confidence` — **optional**, server-side filter. Float as string, e.g. `"0.5"`. **Recommendation: do NOT use.** Client-side filtering keeps the validator's `MinConfidence` decoupled from server behavior across CPAI versions, and makes test assertions deterministic without a stub-side check.

### Response (success path)

```json
{
  "success": true,
  "predictions": [
    {
      "label": "person",
      "confidence": 0.87,
      "x_min": 142, "y_min": 88,
      "x_max": 396, "y_max": 612
    },
    {
      "label": "car",
      "confidence": 0.52,
      "x_min": 412, "y_min": 318,
      "x_max": 781, "y_max": 540
    }
  ],
  "processMs": 31,
  "inferenceMs": 24,
  "code": 200
}
```

### Response (failure path)

```json
{
  "success": false,
  "error": "Module not loaded",
  "code": 503
}
```

The HTTP status is typically `200` even for application-level failures (`success: false`). **Plugin must check `success` field, not just HTTP status.** Same pattern Phase 6 codified for Pushover (lessons-learned D11).

### `MultipartFormDataContent` recipe

```csharp
using var content = new MultipartFormDataContent();
var imageContent = new ByteArrayContent(snapshotBytes);
imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
content.Add(imageContent, "image", "snapshot.jpg");

using var response = await _httpClient.PostAsync("/v1/vision/detection", content, ct).ConfigureAwait(false);
response.EnsureSuccessStatusCode();
var body = await response.Content.ReadFromJsonAsync<CodeProjectAiResponse>(ct).ConfigureAwait(false);
```

**Phase 6 D12 lesson applies:** .NET 10 emits unquoted multipart `name=` parameters by default. Tests asserting raw wire format must use unquoted form (`name=image`, not `name="image"`).

### Decision rule

Validator passes if **any** prediction satisfies all of:
1. `confidence >= MinConfidence`
2. `AllowedLabels` is empty (no filter) OR `label ∈ AllowedLabels`

Returning early on the first satisfying prediction is fine — order does not matter.

---

## 2. .NET 10 keyed-validator-instance options binding pattern (resolves OQ1, OQ4)

The combination of **named options** (`IOptionsMonitor<T>.Get(name)`) and **keyed services** (`AddKeyedSingleton`) is the canonical .NET 10 pattern for "multiple instances of the same type, each with distinct configuration":

### Recommended pattern

In `FrigateRelay.Plugins.CodeProjectAi.PluginRegistrar.Register`:

```csharp
public sealed class PluginRegistrar : IPluginRegistrar
{
    public void Register(PluginRegistrationContext context)
    {
        // Top-level Validators: { "key": { Type: "CodeProjectAi", ...opts... }, ... }
        var validatorsSection = context.Configuration.GetSection("Validators");

        foreach (var instance in validatorsSection.GetChildren())
        {
            var type = instance["Type"];
            if (!string.Equals(type, "CodeProjectAi", StringComparison.Ordinal))
                continue; // another plugin's registrar handles other Type values

            var instanceKey = instance.Key; // e.g. "strict-person"

            // 1. Bind named options instance from this child section
            context.Services
                .AddOptions<CodeProjectAiOptions>(instanceKey)
                .Bind(instance)
                .ValidateDataAnnotations()
                .ValidateOnStart();

            // 2. Register a keyed IValidationPlugin that pulls its named options
            context.Services.AddKeyedSingleton<IValidationPlugin>(instanceKey, (sp, key) =>
            {
                var optsMonitor = sp.GetRequiredService<IOptionsMonitor<CodeProjectAiOptions>>();
                var opts = optsMonitor.Get((string)key!);
                var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
                var httpClient = httpFactory.CreateClient($"CodeProjectAi:{key}");
                ConfigureClient(httpClient, opts); // sets BaseAddress, Timeout from opts
                var logger = sp.GetRequiredService<ILogger<CodeProjectAiValidator>>();
                return new CodeProjectAiValidator((string)key!, opts, httpClient, logger);
            });
        }

        // Single shared HttpClient registration with per-instance configuration
        // happens via the named-client cache; AllowInvalidCertificates handler
        // is configured per-instance via ConfigurePrimaryHttpMessageHandler.
        context.Services.AddHttpClient(); // factory registration only
    }
}
```

**Notes:**
- `instance.Key` gives the dictionary key (`"strict-person"`); `instance["Type"]` gives the discriminator value (`"CodeProjectAi"`); `instance` itself is the IConfigurationSection bound to options.
- We pass the validator **instance key** as the constructor `Name` so `IValidationPlugin.Name` returns `"strict-person"`, not `"CodeProjectAi"`. This matches the user's D2 decision.
- The lambda factory captures `IServiceProvider` and `key` cleanly. .NET 10 keyed-singleton factory signature is `(sp, key) => instance`; `key` is `object?` and must be cast.
- Per-instance TLS bypass is messy with shared `IHttpClientFactory` because `ConfigurePrimaryHttpMessageHandler` is called at named-client registration time, not lookup time. **Recommendation:** create a separate `IHttpClientFactory` named-client per validator instance (`$"CodeProjectAi:{instanceKey}"`), each with its own `ConfigurePrimaryHttpMessageHandler`. Architect should formalize this in PLAN-2.x.

### Alternative considered (rejected)

`AddSingleton<IEnumerable<IValidationPlugin>>` returning all instances at once. **Rejected:** the dispatcher needs **lookup by name**, not enumeration. `IServiceProvider.GetKeyedService<IValidationPlugin>(key)` is exactly the right primitive.

### Dispatcher ↔ EventPump validator resolution

**Recommendation: resolve at `EventPump.DispatchAsync` time, not at `ChannelActionDispatcher.EnqueueAsync` time.** EventPump already iterates `Subscription.Actions`; for each `ActionEntry.Validators` key, resolve the `IValidationPlugin` via `IServiceProvider.GetKeyedService<IValidationPlugin>(key)` and pass the resulting `IReadOnlyList<IValidationPlugin>` to `EnqueueAsync`. This keeps the dispatcher key-string-free; it stays a pure plugin-instance executor.

Pre-resolved list construction in EventPump:

```csharp
private readonly IServiceProvider _services;

// In DispatchAsync, for each ActionEntry on the matched subscription:
IReadOnlyList<IValidationPlugin> validators = action.Validators is { Count: > 0 } keys
    ? keys.Select(k => _services.GetRequiredKeyedService<IValidationPlugin>(k)).ToArray()
    : Array.Empty<IValidationPlugin>();

await _dispatcher.EnqueueAsync(ctx, plugin, validators, action.SnapshotProvider, sub.DefaultSnapshotProvider, ct);
```

Startup validation (`StartupValidation.ValidateValidators`) ensures every key resolvable here is registered, so `GetRequiredKeyedService` is safe.

---

## 3. `ActionEntryJsonConverter` extension (resolves OQ3)

### Current behaviour (verified from source)

`ActionEntryJsonConverter.cs` accepts both:
- **String form:** `"BlueIris"` → `new ActionEntry("BlueIris")`
- **Object form:** `{"Plugin": "BlueIris", "SnapshotProvider": "Frigate"}` → bound via private `ActionEntryDto(Plugin, SnapshotProvider)` then projected.

`Write` always emits object form with `Plugin` + `SnapshotProvider`.

The converter is registered via `[JsonConverter(typeof(ActionEntryJsonConverter))]` on the `ActionEntry` record. **It only fires for `JsonSerializer.Deserialize` paths** — `IConfiguration.Bind` bypasses it (ID-12).

### Required Phase 7 changes

**Public record extension:**
```csharp
[JsonConverter(typeof(ActionEntryJsonConverter))]
public sealed record ActionEntry(
    string Plugin,
    string? SnapshotProvider = null,
    IReadOnlyList<string>? Validators = null);
```

**Private DTO extension:**
```csharp
private sealed record ActionEntryDto(
    string Plugin,
    string? SnapshotProvider = null,
    IReadOnlyList<string>? Validators = null);
```

**Read method (object form) projection:**
```csharp
return new ActionEntry(dto.Plugin, dto.SnapshotProvider, dto.Validators);
```

**Write method:**
```csharp
writer.WriteStartObject();
writer.WriteString("Plugin", value.Plugin);
if (value.SnapshotProvider is not null)
    writer.WriteString("SnapshotProvider", value.SnapshotProvider);
if (value.Validators is { Count: > 0 })
{
    writer.WriteStartArray("Validators");
    foreach (var v in value.Validators) writer.WriteStringValue(v);
    writer.WriteEndArray();
}
writer.WriteEndObject();
```

### `IConfiguration.Bind` compatibility (ID-12 mitigation)

`IConfiguration.Bind` reads the `IReadOnlyList<string>?` property by:
1. Using the record's primary-constructor positional binding (matches `Validators` JSON property name to constructor parameter name).
2. Allocating a `List<string>` and copying child string values when an array section is present.

**Verified the .NET configuration binder handles `IReadOnlyList<string>?` correctly via reflection.** Adding the field as a third positional parameter with default `null` will bind properly through `services.Configure<HostSubscriptionsOptions>(config.GetSection("Subscriptions"))`.

The string-form fallback (`["BlueIris"]`) was already silently broken via `IConfiguration.Bind` (ID-12) — Phase 7 does not regress this further. ID-12 fix remains a deferred Phase 11 OSS-polish item.

---

## 4. `IValidationPlugin` test stub migration (resolves OQ5)

### Found call sites (verified via `git grep -n IValidationPlugin`)

**Production source (5 sites, all stable — no migration needed):**
- `src/FrigateRelay.Abstractions/IValidationPlugin.cs:4` — interface definition. **Modify** (D1).
- `src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs:128` — `IReadOnlyList<IValidationPlugin>` parameter, no method invocation yet. **Wire chain in PLAN-1.1.**
- `src/FrigateRelay.Host/Dispatch/DispatchItem.cs:28` — record field. No change.
- `src/FrigateRelay.Host/Dispatch/IActionDispatcher.cs:39` — parameter. No change.
- `src/FrigateRelay.Host/EventPump.cs:97` — `Array.Empty<IValidationPlugin>()` placeholder. **Replace with resolved list per ¶2.**

**Test source (3 sites, all in `FrigateRelay.Host.Tests`, all using `Array.Empty<IValidationPlugin>()` or `Arg.Any<IReadOnlyList<IValidationPlugin>>()` mock matchers):**
- `tests/FrigateRelay.Host.Tests/Dispatch/ChannelActionDispatcherTests.cs:82, 83, 85, 123, 203, 205, 233, 256` — 8 call sites, all `Array.Empty<IValidationPlugin>()`. No code change needed when the dispatcher's signature stays the same; new validator-chain tests are **additive** in PLAN-1.1.
- `tests/FrigateRelay.Host.Tests/EventPumpDispatchTests.cs:38, 65, 92, 100` — NSubstitute `Arg.Is`/`Arg.Any` matchers. No code change needed.
- `tests/FrigateRelay.Host.Tests/EventPumpTests.cs:108` — fake dispatcher implementing `EnqueueAsync(EventContext, IActionPlugin, IReadOnlyList<IValidationPlugin>, ...)`. No change.

**Conclusion:** zero migrations required. **No test stub of `IValidationPlugin.ValidateAsync` exists** (Phase 4 D4 only required passing the empty list — no validator was ever invoked). The new `SnapshotContext` parameter therefore has zero migration surface in existing tests; new tests in PLAN-2.1 will use the new signature directly.

---

## 5. `SnapshotContext.ResolveAsync` caching (resolves OQ6)

### Verified from `src/FrigateRelay.Abstractions/SnapshotContext.cs`

`SnapshotContext` is a **`readonly struct`** with a single `ISnapshotResolver? _resolver` field plus two `string?` provider-name tiers. `ResolveAsync` delegates directly to `_resolver.ResolveAsync(...)` with no memoization.

**Therefore: calling `ResolveAsync` twice on the same `SnapshotContext` value HITS THE RESOLVER (and the underlying `ISnapshotProvider.GetSnapshotAsync` HTTP fetch) TWICE.**

This is a regression for the validator path: the validator submits the snapshot bytes, then the action would re-fetch them.

### Recommended fix (architect decision)

**Option A (Recommended): add a pre-resolved snapshot field to `SnapshotContext`.**

```csharp
public readonly struct SnapshotContext
{
    private readonly ISnapshotResolver? _resolver;
    private readonly SnapshotResult? _preResolved;
    private readonly bool _hasPreResolved;

    public string? PerActionProviderName { get; }
    public string? SubscriptionDefaultProviderName { get; }

    public SnapshotContext(ISnapshotResolver resolver, string? perActionProviderName, string? subscriptionDefaultProviderName)
    {
        _resolver = resolver;
        PerActionProviderName = perActionProviderName;
        SubscriptionDefaultProviderName = subscriptionDefaultProviderName;
        _preResolved = null;
        _hasPreResolved = false;
    }

    /// <summary>
    /// Constructs a SnapshotContext that returns a pre-resolved snapshot without invoking
    /// the resolver. Used by the dispatcher to share one resolved snapshot across the
    /// validator chain and the action.
    /// </summary>
    public SnapshotContext(SnapshotResult? preResolved)
    {
        _resolver = null;
        PerActionProviderName = null;
        SubscriptionDefaultProviderName = null;
        _preResolved = preResolved;
        _hasPreResolved = true;
    }

    public ValueTask<SnapshotResult?> ResolveAsync(EventContext context, CancellationToken ct)
    {
        if (_hasPreResolved)
            return ValueTask.FromResult(_preResolved);
        if (_resolver is null)
            return ValueTask.FromResult<SnapshotResult?>(null);
        return _resolver.ResolveAsync(context, PerActionProviderName, SubscriptionDefaultProviderName, ct);
    }
}
```

**Dispatcher usage in `ChannelActionDispatcher.ConsumeAsync`:**

```csharp
// Build the resolver-backed SnapshotContext from per-action and per-subscription tiers
var initial = new SnapshotContext(_resolver, item.PerActionSnapshotProvider, item.SubscriptionSnapshotProvider);

// Resolve ONCE if any validator OR the action will need the snapshot.
SnapshotContext shared;
if (item.Validators.Count > 0)
{
    var preResolved = await initial.ResolveAsync(item.Context, ct).ConfigureAwait(false);
    shared = new SnapshotContext(preResolved);
}
else
{
    shared = initial; // action plugin resolves lazily; no double-fetch risk
}

// Validator chain
foreach (var v in item.Validators)
{
    var verdict = await v.ValidateAsync(item.Context, shared, ct).ConfigureAwait(false);
    if (!verdict.Passed)
    {
        Log.ValidatorRejected(_logger, item.Context.EventId, item.Plugin.Name, v.Name, verdict.Reason);
        return;
    }
}

// Action — uses the same cached snapshot when validators ran
await item.Plugin.ExecuteAsync(item.Context, shared, ct).ConfigureAwait(false);
```

**Pros:**
- One snapshot fetch when validators are present.
- No fetch at all when no validators AND no action needs snapshot (BlueIris trigger ignores `SnapshotContext`).
- Backward compatible — Phase 6 callers using the resolver-backed ctor still work.
- Struct stays a struct; `default(SnapshotContext)` still safe.

**Option B (rejected): make resolver itself caching per-EventContext.** Resolver is a singleton; per-event caching requires keying by `EventContext.EventId` and a TTL/eviction policy. Heavier and bleeds cache concerns into the resolver. Reject.

**Option C (rejected): always pre-resolve, regardless of validator presence.** Forces snapshot fetch even when no plugin uses it (BlueIris-only subscriptions). Reject.

**Architect lock-in:** Option A. Adds **one constructor + one if-branch** to `SnapshotContext`, ~10 LoC delta. Tests added in PLAN-1.1: `ResolveAsync_PreResolved_ReturnsCachedWithoutResolver`, `ResolveAsync_PreResolvedNull_ReturnsNullWithoutResolver`.

---

## 6. CodeProject.AI HttpClient resilience configuration (resolves OQ2 partial)

### Recommendation

```csharp
private static void ConfigureClient(HttpClient client, CodeProjectAiOptions opts)
{
    client.BaseAddress = new Uri(opts.BaseUrl);
    client.Timeout = opts.Timeout; // 5s default per CONTEXT-7 D5
}
```

**No `AddResilienceHandler` on the validator's `HttpClient`** (CONTEXT-7 D4 architect lock-in). Validators are pre-action gates; per-attempt retry latency (3+6+9=18s with action plugin's policy) would systematically delay every notification.

### Per-instance TLS bypass

```csharp
context.Services.AddHttpClient($"CodeProjectAi:{instanceKey}")
    .ConfigurePrimaryHttpMessageHandler(_ =>
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        };
        if (opts.AllowInvalidCertificates)
        {
            handler.SslOptions.RemoteCertificateValidationCallback =
                (_, _, _, _) => true;
        }
        return handler;
    });
```

**Important: `opts` is captured at registration time, not at request time.** This means runtime config changes to `AllowInvalidCertificates` won't take effect (consistent with how BlueIris/Pushover handle the same setting today — confirmed by reading their registrars).

### `OnError` switch wiring inside `CodeProjectAiValidator.ValidateAsync`

```csharp
public async Task<Verdict> ValidateAsync(EventContext ctx, SnapshotContext snapshot, CancellationToken ct)
{
    var snap = await snapshot.ResolveAsync(ctx, ct).ConfigureAwait(false);
    if (snap is null)
        return Verdict.Fail("validator_no_snapshot");

    try
    {
        using var content = BuildMultipart(snap.Bytes);
        using var response = await _httpClient.PostAsync("/v1/vision/detection", content, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CodeProjectAiResponse>(ct).ConfigureAwait(false);
        return EvaluatePredictions(body);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        // Host shutdown — propagate. Not a validator failure.
        throw;
    }
    catch (TaskCanceledException ex)
    {
        // HttpClient.Timeout triggered (ct NOT cancelled).
        Log.ValidatorTimeout(_logger, _name, ctx.EventId, ex);
        return _opts.OnError == ValidatorErrorMode.FailOpen
            ? Verdict.Pass()
            : Verdict.Fail("validator_timeout");
    }
    catch (HttpRequestException ex)
    {
        Log.ValidatorUnavailable(_logger, _name, ctx.EventId, ex);
        return _opts.OnError == ValidatorErrorMode.FailOpen
            ? Verdict.Pass()
            : Verdict.Fail($"validator_unavailable: {ex.Message}");
    }
}
```

**Order of catch blocks matters:** `OperationCanceledException when ct.IsCancellationRequested` MUST come before `TaskCanceledException` (which derives from it). Otherwise host-shutdown cancellation is incorrectly classified as a timeout failure.

---

## 7. Plan-level open questions for the architect

1. **`ValidatorInstanceOptions` discriminator type placement.** The top-level `Validators` config dict needs to bind to *something* before the per-Type registrar enumeration. Two options:
   - (a) **Don't bind it to a strongly-typed object at all.** Each plugin registrar enumerates `IConfigurationSection.GetChildren()` directly. Simplest — recommended.
   - (b) Bind to `Dictionary<string, ValidatorInstanceOptions>` where `ValidatorInstanceOptions` has a `Type` discriminator field plus an `IConfigurationSection` for the rest. Cleaner type-checking but heavier.

   **Recommendation: (a).** Matches what plugin-registrar code already does for plugin-specific sections and keeps the central host free of any per-plugin type knowledge.

2. **`CodeProjectAiResponse` DTO placement.** Should the response DTO live in `FrigateRelay.Plugins.CodeProjectAi/` (private to the plugin) or in a shared location? **Private to the plugin** — no other component needs the shape. Architect: declare as `internal sealed record CodeProjectAiResponse(bool Success, IReadOnlyList<CodeProjectAiPrediction>? Predictions, int Code)` plus `internal sealed record CodeProjectAiPrediction(string Label, double Confidence, int XMin, int YMin, int XMax, int YMax)`. JSON property names map automatically because System.Text.Json with `JsonNamingPolicy.SnakeCaseLower` (or per-property `[JsonPropertyName]` for `x_min` etc.).

3. **Test class names in `tests/FrigateRelay.Plugins.CodeProjectAi.Tests/`.**
   - `CodeProjectAiValidatorTests` — confidence pass/fail, label allowlist, FailClosed timeout, FailOpen timeout, multipart wire-format assertion, response parsing happy path, no-prediction response, AllowInvalidCertificates honored.
   - `StartupValidatorChainTests` (or in `FrigateRelay.Host.Tests/Configuration/`) — undefined validator key fail-fast, unknown Type fail-fast, valid configuration accepts.
   - Integration: `MqttToValidatorTests.Validator_ShortCircuits_OnlyAttachedAction` and `MqttToValidatorTests.Validator_Pass_BothActionsFire` in `tests/FrigateRelay.IntegrationTests/`.

4. **`ChannelActionDispatcher.ConsumeAsync` change scope.** The validator-chain loop is a small in-place addition (~15 LoC); the snapshot-pre-resolution is conditional on `item.Validators.Count > 0`. Keep both inside the existing Polly `ResiliencePipeline.ExecuteAsync` block? **No** — validators run BEFORE the action, so they bypass the action's retry policy. Only the action call is wrapped in Polly. Architect: place validator chain ABOVE the `ResiliencePipeline.ExecuteAsync` call.

5. **`StartupValidation.ValidateValidators` shape.** Mirrors `ValidateActions`. Iterates every `Subscription.Actions[*].Validators` key, looks each up in `IServiceProvider.GetKeyedServices<IValidationPlugin>()` (or a simpler `GetKeyedService<IValidationPlugin>(key) is null` check). Fail-fast on unresolved key with message `"Validator '{key}' is referenced by Subscription[{i}].Actions[{j}].Validators but not registered. Check the top-level Validators section and ensure each instance has a recognized Type."`. Architect: confirm whether `IServiceProviderIsKeyedService` is available in .NET 10 for the cleaner check.

6. **No `AssemblyInfo`-style `[InternalsVisibleTo]` between Host and CodeProjectAi plugin.** The plugin assembly registers via `IPluginRegistrar` like all others. `internal` types stay internal to the plugin assembly. Test access via the existing csproj `<InternalsVisibleTo Include="FrigateRelay.Plugins.CodeProjectAi.Tests" />` MSBuild item (precedent: `src/FrigateRelay.Host/FrigateRelay.Host.csproj`).

---

## Test-count target for architect

ROADMAP gates: ≥ 8 unit tests + 1 integration (`Validator_ShortCircuits_OnlyAttachedAction`) + 1 second integration (`Validator_Pass_BothActionsFire`) = **≥ 10**.

Recommended distribution (architect can tune):

| Suite | Tests |
|---|---|
| `CodeProjectAiValidatorTests` | 8 (confidence pass / confidence fail / allowed-label gate hit / allowed-label gate miss / FailClosed timeout / FailOpen timeout / multipart wire-format / response parsing happy path) |
| `StartupValidationValidatorTests` | 3 (undefined key fail-fast / unknown Type fail-fast / valid configuration passes) |
| `ChannelActionDispatcherTests` (additive) | 2 (validator chain short-circuits on first fail; snapshot is shared between validator and action) |
| `SnapshotContextTests` (additive) | 2 (`PreResolved` constructor returns cached without resolver / `PreResolved` null-snapshot constructor returns null without resolver) |
| `MqttToValidatorTests` (integration) | 2 (`Validator_ShortCircuits_OnlyAttachedAction`, `Validator_Pass_BothActionsFire`) |
| **Total** | **17** (≥10 gate, +70% cushion) |

---

## Wave shape recommendation

(Architect may restructure; suggestion only.)

- **Wave 1 (parallel-safe):**
  - **PLAN-1.1** `IValidationPlugin` signature change to take `SnapshotContext` + `SnapshotContext.PreResolved` constructor + `ChannelActionDispatcher.ConsumeAsync` validator-chain wiring + share-snapshot logic. Modifies `Abstractions` (additive: new ctor + sig change).
  - **PLAN-1.2** `ActionEntry.Validators` field + `ActionEntryJsonConverter` extension (Read+Write+DTO) + top-level `Validators` config binding shape (registrar enumeration pattern documented but no per-Type code yet). Modifies `Host/Configuration/`.

- **Wave 2:**
  - **PLAN-2.1** `FrigateRelay.Plugins.CodeProjectAi/` project: `CodeProjectAiOptions`, `ValidatorErrorMode`, `CodeProjectAiResponse` DTOs, `CodeProjectAiValidator` (multipart POST, decision rule, OnError handling), `PluginRegistrar` (enumerate top-level `Validators`, register named options + keyed `IValidationPlugin`). Unit tests `CodeProjectAiValidatorTests` (8) + `StartupValidationValidatorTests` (3 — actually these go in Host tests but are paired with this plan).

- **Wave 3:**
  - **PLAN-3.1** `EventPump` validator-resolution wiring (resolve `ActionEntry.Validators` keys to `IValidationPlugin` list before `EnqueueAsync`) + `StartupValidation.ValidateValidators` + `HostBootstrap` registrar wiring (conditional `if (Configuration.GetSection("Validators").Exists())`) + 2 integration tests `MqttToValidatorTests.Validator_ShortCircuits_OnlyAttachedAction` + `MqttToValidatorTests.Validator_Pass_BothActionsFire`.

This puts the breaking abstraction change (PLAN-1.1) in Wave 1 with the config-only change (PLAN-1.2) running parallel, then builds the new plugin in Wave 2, then wires it in Wave 3 with integration coverage. Each plan is at most 3 tasks.

---

**Ready for architect.** All 6 CONTEXT-7 open questions resolved; 6 plan-level questions surfaced for the architect to lock into plan files.
