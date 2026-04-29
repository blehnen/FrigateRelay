---
plan_id: 7.3.1
title: EventPump validator-resolution + StartupValidation + HostBootstrap wiring + integration tests
wave: 3
plan: 1
dependencies: ["1.1", "1.2", "2.1"]
files_touched:
  - src/FrigateRelay.Host/EventPump.cs
  - src/FrigateRelay.Host/StartupValidation.cs
  - src/FrigateRelay.Host/HostBootstrap.cs
  - src/FrigateRelay.Host/FrigateRelay.Host.csproj
  - tests/FrigateRelay.Host.Tests/Configuration/StartupValidationTests.cs
  - tests/FrigateRelay.IntegrationTests/MqttToValidatorTests.cs
  - tests/FrigateRelay.IntegrationTests/Fixtures/appsettings.validators.json (or equivalent)
tdd: false
estimated_tasks: 3
---

# Plan 3.1: EventPump validator-resolution + StartupValidation + HostBootstrap wiring + integration tests

## Context
Wave 3 connects everything. PLAN-1.1 changed the dispatcher to consume an `IReadOnlyList<IValidationPlugin>` per `DispatchItem`. PLAN-1.2 added `ActionEntry.Validators` keys. PLAN-2.1 registered keyed `IValidationPlugin` instances. This plan resolves keys → plugin instances at dispatch time (RESEARCH §2 final snippet — at `EventPump.DispatchAsync`, NOT at `EnqueueAsync`), adds startup fail-fast for unresolved keys, conditionally registers the CodeProjectAi registrar in `HostBootstrap`, and adds 2 integration tests proving end-to-end behavior.

**CRITICAL precedent — DO NOT REPEAT:** Phase 5 review-3.1 caught that `StartupValidation.ValidateSnapshotProviders` was DEAD CODE — defined but never called from `HostBootstrap.ValidateStartup`. The new `ValidateValidators` method MUST be wired in. Verify after writing by running the host with bad config and confirming a fail-fast.

## Dependencies
- PLAN-1.1: `ChannelActionDispatcher` consumes the validator list.
- PLAN-1.2: `ActionEntry.Validators` field exists.
- PLAN-2.1: `FrigateRelay.Plugins.CodeProjectAi.PluginRegistrar` exists and registers keyed `IValidationPlugin`.

## Tasks

### Task 1: Wire validator resolution in `EventPump.DispatchAsync`
**Files:** `src/FrigateRelay.Host/EventPump.cs`
**Action:** modify
**Description:**

Per RESEARCH §2 final snippet. Replace the `Array.Empty<IValidationPlugin>()` placeholder at line 97 (verify line via Read) with:

```csharp
IReadOnlyList<IValidationPlugin> validators = action.Validators is { Count: > 0 } keys
    ? keys.Select(k => _services.GetRequiredKeyedService<IValidationPlugin>(k)).ToArray()
    : Array.Empty<IValidationPlugin>();

await _dispatcher.EnqueueAsync(ctx, plugin, validators, action.SnapshotProvider, sub.DefaultSnapshotProvider, ct);
```

**Verify constructor signature first** — Read `EventPump.cs` to determine whether `IServiceProvider` is already a constructor parameter. If not, inject it. If `EventPump` is constructed manually (not via DI) anywhere — including tests — those callers must be updated. RESEARCH §4 lists the test fakes that already work with the current `EnqueueAsync` signature; the new field is `_services`.

**Use `GetRequiredKeyedService<IValidationPlugin>(k)`** — `StartupValidation.ValidateValidators` (Task 2) guarantees keys resolve. If a key fails to resolve here, that's an internal error and throwing is correct.

`using Microsoft.Extensions.DependencyInjection;` — required for `GetRequiredKeyedService` extension.

**Acceptance criteria:**
- Build clean.
- Existing `EventPumpDispatchTests.cs` and `EventPumpTests.cs` still pass (they pass `Array.Empty<>()` semantically — RESEARCH §4).
- `git grep -n "GetRequiredKeyedService<IValidationPlugin>" src/FrigateRelay.Host/EventPump.cs` matches.
- `git grep -n "Array.Empty<IValidationPlugin>" src/FrigateRelay.Host/EventPump.cs` empty (placeholder removed).

### Task 2: `StartupValidation.ValidateValidators` + wire into `HostBootstrap.ValidateStartup`
**Files:** `src/FrigateRelay.Host/StartupValidation.cs` (note: NOT in `Configuration/` subdir — verified against on-disk path), `src/FrigateRelay.Host/HostBootstrap.cs`, `tests/FrigateRelay.Host.Tests/Configuration/StartupValidationTests.cs`
**Action:** modify + test
**Description:**

**`StartupValidation.ValidateValidators`** — mirrors `ValidateActions` and `ValidateSnapshotProviders`. RESEARCH §7-5 message:

```csharp
public static void ValidateValidators(IServiceProvider services, IOptions<HostSubscriptionsOptions> subs)
{
    var subscriptions = subs.Value.Subscriptions;
    for (int i = 0; i < subscriptions.Count; i++)
    {
        var sub = subscriptions[i];
        for (int j = 0; j < sub.Actions.Count; j++)
        {
            var action = sub.Actions[j];
            if (action.Validators is null || action.Validators.Count == 0) continue;
            foreach (var key in action.Validators)
            {
                var plugin = services.GetKeyedService<IValidationPlugin>(key);
                if (plugin is null)
                {
                    throw new InvalidOperationException(
                        $"Validator '{key}' is referenced by Subscription[{i}].Actions[{j}].Validators " +
                        $"but not registered. Check the top-level Validators section and ensure each " +
                        $"instance has a recognized Type.");
                }
            }
        }
    }
}
```

`GetKeyedService<T>(key)` returns `null` when nothing is registered — this covers BOTH "undefined key" and "unknown Type" cases (the per-plugin registrar's `Type` filter silently skips unknown types, so they never produce a registration → key resolves to null here). Document the chain:

> Note: An `ActionEntry.Validators` key referencing a top-level `Validators` instance with an UNKNOWN `Type` value is detected here as "not registered" — no plugin registrar claimed it. The error message points operators to "ensure each instance has a recognized Type" exactly because of this chain.

**Wire into `HostBootstrap.ValidateStartup`** — Read the method first. Add the call alongside `ValidateActions` and `ValidateSnapshotProviders`:

```csharp
StartupValidation.ValidateValidators(host.Services, host.Services.GetRequiredService<IOptions<HostSubscriptionsOptions>>());
```

**This is the Phase 5 review-3.1 hot zone.** After wiring, manually trace `HostBootstrap.ValidateStartup` from bootstrap entry to confirm `ValidateValidators` is in the call chain. CI lacks a "did you actually call this method" check — only integration tests catch wiring regressions.

**Tests** — extend `tests/FrigateRelay.Host.Tests/Configuration/StartupValidationTests.cs` (or create if absent) with 3 tests:

1. `ValidateValidators_UndefinedKey_Throws` — config has `Subscriptions[0].Actions[0].Validators = ["nonexistent"]` and top-level `Validators` empty. Assert throws `InvalidOperationException` with message containing `"'nonexistent'"` and `"Subscription[0].Actions[0]"`.
2. `ValidateValidators_UnknownType_Throws` — config has `Validators: { "weird": { "Type": "MysteryAi", ... } }` and `Actions[0].Validators = ["weird"]`. Since no registrar claims `MysteryAi`, key resolves to null in DI → throws. Assert error message references `'weird'`.
3. `ValidateValidators_AllKeysResolve_DoesNotThrow` — register a fake `IValidationPlugin` keyed `"strict-person"`, set `Actions[0].Validators = ["strict-person"]`. Assert no throw.

**Acceptance criteria:**
- 3 new tests pass.
- `git grep -n "ValidateValidators" src/FrigateRelay.Host/HostBootstrap.cs` matches (proves wiring).
- `git grep -n "ValidateValidators" src/FrigateRelay.Host/StartupValidation.cs` matches.
- Build clean.

### Task 3: `HostBootstrap` registrar wiring + 2 integration tests
**Files:** `src/FrigateRelay.Host/HostBootstrap.cs`, `tests/FrigateRelay.IntegrationTests/MqttToValidatorTests.cs`, `tests/FrigateRelay.IntegrationTests/Fixtures/appsettings.validators.json` (or use existing fixture file pattern)
**Action:** modify + create
**Description:**

**`HostBootstrap.RegisterPlugins`** — add the CodeProjectAi registrar conditionally:

```csharp
// Conditional: only register if any Validators are configured. Plugin registrar itself
// also no-ops when Validators section is missing, but checking here keeps the registrar
// list clean during inspection / logs.
if (configuration.GetSection("Validators").Exists())
{
    registrars.Add(new FrigateRelay.Plugins.CodeProjectAi.PluginRegistrar());
}
```

Verify exact site by reading `HostBootstrap.cs` — the Pushover/BlueIris registrars are added unconditionally (Phase 6). Mirror their construction style.

Add a `<ProjectReference>` to `FrigateRelay.Plugins.CodeProjectAi` from `FrigateRelay.Host.csproj`. Per CLAUDE.md — the host depends on Abstractions in principle, but registrars are concrete and must be referenced for `new …PluginRegistrar()` to compile. (BlueIris and Pushover are already referenced this way.)

**Integration tests** — `tests/FrigateRelay.IntegrationTests/MqttToValidatorTests.cs`. Mirror Phase 6's `tests/FrigateRelay.IntegrationTests/MqttToBothActionsTests.cs` (the fan-out integration test from PLAN-3.1 of Phase 6 — read the file first for Testcontainers + WireMock setup).

**Test 1: `Validator_ShortCircuits_OnlyAttachedAction`**
- Mosquitto via Testcontainers; WireMock for Blue Iris, Pushover, AND CodeProject.AI endpoints.
- Subscription has 2 actions: `[{ "Plugin": "BlueIris" }, { "Plugin": "Pushover", "Validators": ["strict-person"] }]`.
- CodeProject.AI WireMock stub returns low confidence (e.g. `{success:true, predictions:[{label:"person", confidence:0.2}], code:200}`) — below MinConfidence=0.7.
- Publish a Frigate `events` MQTT message matching the subscription.
- Assert WireMock: BlueIris endpoint received exactly 1 request (action fired), Pushover endpoint received 0 requests (validator short-circuited).
- Assert host log contains structured `validator_rejected` entry with `action="Pushover"`, `validator="strict-person"`. Use the same Serilog test sink pattern Phase 6 used (Read `MqttToBothActionsTests.cs` for the precedent).

**Test 2: `Validator_Pass_BothActionsFire`**
- Same setup, but CodeProject.AI WireMock stub returns matching prediction (`confidence=0.9`, `label="person"`).
- Assert WireMock: BlueIris received 1 request, Pushover received 1 request (validator passed).
- Assert log contains NO `validator_rejected` entry for the Pushover action.

**Fixture file** — extend the integration-test fixture appsettings to include the top-level `Validators` block:

```jsonc
{
  "Validators": {
    "strict-person": {
      "Type": "CodeProjectAi",
      "BaseUrl": "{wiremock-url-injected-at-runtime}",
      "MinConfidence": 0.7,
      "AllowedLabels": ["person"],
      "OnError": "FailClosed",
      "Timeout": "00:00:05"
    }
  },
  "Subscriptions": [
    {
      "Camera": "front_door",
      "Labels": ["person"],
      "Actions": [
        { "Plugin": "BlueIris" },
        { "Plugin": "Pushover", "Validators": ["strict-person"] }
      ]
    }
  ]
}
```

`BaseUrl` is injected at test runtime to the WireMock instance URL (no hardcoded IPs in source — CLAUDE.md invariant). Use the same env-var or `IConfigurationBuilder.AddInMemoryCollection` override pattern Phase 6 used for Pushover/BlueIris stubbing.

**MUST use the OBJECT FORM** for `Actions` entries (`{"Plugin":…, "Validators":[…]}`). The string-array form `["BlueIris"]` silently fails per ID-12. ID-12 is NOT in scope for this plan.

**Acceptance criteria:**
- Both integration tests pass: `dotnet run --project tests/FrigateRelay.IntegrationTests -c Release`.
- WireMock verification calls confirm action firing counts.
- Log inspection confirms `validator_rejected` structured fields per CONTEXT-7 D7.
- `git grep -nE '192\.168\.[0-9]+\.[0-9]+' tests/FrigateRelay.IntegrationTests/` empty (no hardcoded IPs in fixtures).
- Host start-up succeeds with the new fixture; fails fast (per Task 2) when Validators reference an undefined key.

## Verification

```bash
dotnet build FrigateRelay.sln -c Release
dotnet run --project tests/FrigateRelay.Host.Tests -c Release
dotnet run --project tests/FrigateRelay.IntegrationTests -c Release   # requires Docker
git grep -n "ValidateValidators" src/FrigateRelay.Host/                        # both definition AND call
git grep -n "FrigateRelay.Plugins.CodeProjectAi.PluginRegistrar" src/FrigateRelay.Host/HostBootstrap.cs
git grep -n "GetRequiredKeyedService<IValidationPlugin>" src/FrigateRelay.Host/EventPump.cs
git grep -nE '192\.168\.[0-9]+\.[0-9]+' src/ tests/                            # empty
```

## Notes for builder
- **Phase 5 review-3.1 lesson:** `ValidateSnapshotProviders` was wired-but-dead. After implementing `ValidateValidators`, verify wiring with a deliberately bad fixture and run the host briefly — it must crash at startup, not silently accept.
- **`IServiceProviderIsKeyedService`** is available in .NET 10 if you want a slightly cleaner check in Task 2. `GetKeyedService<T>(key) is null` is equivalent and works without resolving — both are fine.
- **RESEARCH §2 final snippet** is the canonical resolution shape — Read it before Task 1.
- **Phase 6 `MqttToBothActionsTests.cs` is the integration-test precedent** — Read it for Testcontainers + WireMock + log capture style. The new `MqttToValidatorTests.cs` follows the same shape; you should not need to invent new infrastructure.
- **CapturingLogger / log capture in integration tests:** Phase 6 used a Serilog sink pattern (not the unit-test `CapturingLogger<T>`); follow that precedent. CapturingLogger is for unit tests only.
- **Object form is mandatory** in fixtures (ID-12 — string-form `["BlueIris"]` silently produces empty Actions via `IConfiguration.Bind`).
- **No hardcoded IPs in fixtures or comments.** WireMock URL is injected at test runtime.
- **Asymmetric retry behavior** (CONTEXT-7 D4): the validator does NOT retry, while BlueIris/Pushover do. Mention this in `CLAUDE.md ## Architecture invariants` observability section as part of Task 3 — this is a permanent operator-facing fact worth documenting.
