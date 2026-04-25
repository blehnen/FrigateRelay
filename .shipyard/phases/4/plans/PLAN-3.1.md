---
phase: phase-4-action-dispatcher-blueiris
plan: 3.1
wave: 3
dependencies: [2.1, 2.2]
must_haves:
  - SubscriptionOptions.Actions[] init-only property (D2)
  - EventPump dispatches to IActionDispatcher per matched (sub, ctx) for each name in sub.Actions
  - Action-name → IActionPlugin map built at startup; unknown names fail fast (S2 + D2)
  - Program.cs registers ChannelActionDispatcher as singleton + IHostedService
  - Program.cs adds BlueIris.PluginRegistrar to the registrar list BEFORE builder.Build()
  - Program.cs surfaces BlueIris:QueueCapacity into DispatcherOptions.PerPluginQueueCapacity
  - Tests for unknown-action-name fail-fast and EventPump dispatch path
files_touched:
  - src/FrigateRelay.Host/Configuration/SubscriptionOptions.cs
  - src/FrigateRelay.Host/EventPump.cs
  - src/FrigateRelay.Host/Program.cs
  - tests/FrigateRelay.Host.Tests/Dispatch/SubscriptionActionWiringTests.cs
  - tests/FrigateRelay.Host.Tests/EventPumpDispatchTests.cs
tdd: true
risk: medium
---

# Plan 3.1: Subscription Actions[] + EventPump dispatch wiring + Program.cs

## Context

Closes the loop: adds the `Actions[]` array to `SubscriptionOptions` (CONTEXT-4 D2), wires `EventPump` to dispatch each matched (sub, ctx) to the named action plugins via `IActionDispatcher.EnqueueAsync`, builds the action-name → plugin map at startup with fail-fast on unknown names (PROJECT.md S2 + CONTEXT-4 D2), and finalizes `Program.cs` to register `ChannelActionDispatcher` and the `BlueIris.PluginRegistrar`. Owns ROADMAP deliverables 5, 6, 7 and the startup-validation tests of deliverable 8.

Wave 3 because it touches files (`EventPump.cs`, `Program.cs`, `SubscriptionOptions.cs`) that depend on contracts from PLAN-1.1 (`IActionDispatcher`, `DispatcherOptions`) AND from PLAN-2.2 (`BlueIris.PluginRegistrar`). Cannot land before both Wave-2 plans complete.

## Dependencies

- PLAN-2.1 (final `ChannelActionDispatcher` with `DispatcherOptions.PerPluginQueueCapacity`).
- PLAN-2.2 (`BlueIris.PluginRegistrar` to add to the registrar list).

## Files touched

- `src/FrigateRelay.Host/Configuration/SubscriptionOptions.cs` (modify — add `Actions` property)
- `src/FrigateRelay.Host/EventPump.cs` (modify — inject `IActionDispatcher` + `IEnumerable<IActionPlugin>`, build action-name map at startup, dispatch per matched action)
- `src/FrigateRelay.Host/Program.cs` (modify — register dispatcher, add BlueIris registrar, wire BlueIris:QueueCapacity into DispatcherOptions, add startup validation for unknown action names)
- `tests/FrigateRelay.Host.Tests/Dispatch/SubscriptionActionWiringTests.cs` (create)
- `tests/FrigateRelay.Host.Tests/EventPumpDispatchTests.cs` (create)

## Tasks

### Task 1: Add Actions[] to SubscriptionOptions + EventPump dispatch wiring
**Files:** `src/FrigateRelay.Host/Configuration/SubscriptionOptions.cs`, `src/FrigateRelay.Host/EventPump.cs`
**Action:** modify
**Description:**

**SubscriptionOptions.cs** — add the new property:

```csharp
/// <summary>
/// Gets the list of action plugin names that fire for this subscription. Empty (default)
/// means no actions fire — fail-safe per CONTEXT-4 D2. Unknown plugin names cause startup
/// failure (PROJECT.md S2). Plugin name match is case-insensitive ordinal.
/// </summary>
public IReadOnlyList<string> Actions { get; init; } = Array.Empty<string>();
```

Place this as the last property on the record so the existing init-order remains stable.

**EventPump.cs** — inject `IActionDispatcher` and `IEnumerable<IActionPlugin>`. Build a case-insensitive `Dictionary<string, IActionPlugin>` once in the constructor (NOT in `ExecuteAsync` — must fail fast at host startup, not at first event). Modify `PumpAsync` so that after a successful `_dedupe.TryEnter`, it dispatches to each named action:

```csharp
internal sealed class EventPump : BackgroundService
{
    // ... existing LogMatchedEvent / LogPumpStopped / LogPumpFaulted ...

    private static readonly Action<ILogger, string, string, string, Exception?> LogDispatchEnqueued =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Debug,
            new EventId(4, "DispatchEnqueued"),
            "Enqueued action={Action} subscription={Subscription} event_id={EventId}");

    private readonly List<IEventSource> _sources;
    private readonly DedupeCache _dedupe;
    private readonly IOptionsMonitor<HostSubscriptionsOptions> _subsMonitor;
    private readonly IActionDispatcher _dispatcher;
    private readonly IReadOnlyDictionary<string, IActionPlugin> _actionsByName;
    private readonly ILogger<EventPump> _logger;

    public EventPump(
        IEnumerable<IEventSource> sources,
        DedupeCache dedupe,
        IOptionsMonitor<HostSubscriptionsOptions> subsMonitor,
        IActionDispatcher dispatcher,
        IEnumerable<IActionPlugin> actionPlugins,
        ILogger<EventPump> logger)
    {
        _sources = sources.ToList();
        _dedupe = dedupe;
        _subsMonitor = subsMonitor;
        _dispatcher = dispatcher;
        _actionsByName = actionPlugins.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    // ... ExecuteAsync unchanged ...

    private async Task PumpAsync(IEventSource source, CancellationToken ct)
    {
        try
        {
            await foreach (var context in source.ReadEventsAsync(ct).WithCancellation(ct).ConfigureAwait(false))
            {
                var subs = _subsMonitor.CurrentValue.Subscriptions;
                var matches = SubscriptionMatcher.Match(context, subs);
                foreach (var sub in matches)
                {
                    if (!_dedupe.TryEnter(sub, context)) continue;
                    LogMatchedEvent(_logger, source.Name, sub.Name, context.Camera, context.Label, context.EventId, null);

                    foreach (var actionName in sub.Actions)
                    {
                        // Lookup is guaranteed to succeed because Program.cs validated all sub.Actions
                        // against _actionsByName at startup. If we ever miss a key here, it's a bug —
                        // throw rather than silently drop.
                        var plugin = _actionsByName[actionName];
                        await _dispatcher.EnqueueAsync(
                            context, plugin, Array.Empty<IValidationPlugin>(), ct).ConfigureAwait(false);
                        LogDispatchEnqueued(_logger, plugin.Name, sub.Name, context.EventId, null);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { /* graceful */ }
        catch (Exception ex) { LogPumpFaulted(_logger, source.Name, ex, null); }
        finally { LogPumpStopped(_logger, source.Name, null); }
    }
}
```

The `Array.Empty<IValidationPlugin>()` is the D4 empty seam — Phase 7 fills it from per-action config.

**Acceptance Criteria:**
- `SubscriptionOptions.Actions` property exists, type is `IReadOnlyList<string>`, default is `Array.Empty<string>()`, init-only.
- `EventPump` constructor accepts `IActionDispatcher` and `IEnumerable<IActionPlugin>` parameters; the dictionary is built with `StringComparer.OrdinalIgnoreCase`.
- `PumpAsync` calls `_dispatcher.EnqueueAsync(context, plugin, Array.Empty<IValidationPlugin>(), ct)` for each name in `sub.Actions` after a successful dedupe pass.
- The `_actionsByName[actionName]` lookup uses the indexer (NOT `TryGetValue`) — at this point an unknown name is a programming error (Program.cs validated startup) and an exception is the right outcome.
- `dotnet build FrigateRelay.sln -c Release` succeeds.
- `git grep -nE '\.(Result|Wait)\(' src/FrigateRelay.Host/EventPump.cs` returns zero matches.

### Task 2: Wire dispatcher + BlueIris registrar + startup action-name validation in Program.cs
**Files:** `src/FrigateRelay.Host/Program.cs`
**Action:** modify
**Description:**

Modify the existing `Program.cs` (currently registers DedupeCache, IMemoryCache, EventPump, and the FrigateMqtt registrar). Add:

1. **DispatcherOptions binding** — bind from configuration section `Dispatcher` (optional; defaults are fine if missing). Per-plugin capacity is contributed by reading `BlueIris:QueueCapacity` (CLAUDE.md note: this is intentionally host-side wiring, not plugin-side, to keep the plugin assembly free of host references).

```csharp
builder.Services.AddOptions<DispatcherOptions>()
    .Configure(opts =>
    {
        var blueIrisCapacity = builder.Configuration.GetValue<int?>("BlueIris:QueueCapacity");
        if (blueIrisCapacity is { } c)
        {
            opts.PerPluginQueueCapacity = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["BlueIris"] = c,
            };
        }
    });
```

2. **ChannelActionDispatcher registration** — singleton + IHostedService forwarders (per the architect note: same instance fills both roles).

```csharp
builder.Services.AddSingleton<ChannelActionDispatcher>();
builder.Services.AddSingleton<IActionDispatcher>(sp => sp.GetRequiredService<ChannelActionDispatcher>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<ChannelActionDispatcher>());
```

3. **BlueIris registrar in the list** — add `new FrigateRelay.Plugins.BlueIris.PluginRegistrar()` to the `registrars` collection. **MUST be before `builder.Build()`** (RESEARCH §9 risk note + Phase-3 latent-bug pattern in CLAUDE.md).

```csharp
IEnumerable<IPluginRegistrar> registrars =
[
    new FrigateRelay.Sources.FrigateMqtt.PluginRegistrar(),
    new FrigateRelay.Plugins.BlueIris.PluginRegistrar(),
];
```

4. **Action-name validation at startup (S2 + D2)** — after `var app = builder.Build();`, before `await app.RunAsync();`, validate that every `sub.Actions` name is registered:

```csharp
// Validate that every subscription's Actions[] reference a registered plugin (PROJECT.md S2 + CONTEXT-4 D2).
// Fail-fast at startup with a diagnostic listing the unknown name and the registered plugins.
var subsOpts = app.Services.GetRequiredService<IOptions<HostSubscriptionsOptions>>().Value;
var actionPlugins = app.Services.GetRequiredService<IEnumerable<IActionPlugin>>().ToList();
var registeredNames = actionPlugins.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

foreach (var sub in subsOpts.Subscriptions)
{
    foreach (var actionName in sub.Actions)
    {
        if (!registeredNames.Contains(actionName))
        {
            throw new InvalidOperationException(
                $"Subscription '{sub.Name}' references unknown action plugin '{actionName}'. " +
                $"Registered plugins: [{string.Join(", ", registeredNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))}]. " +
                $"Either register the plugin or remove the reference from appsettings.");
        }
    }
}

await app.RunAsync();
```

The error message lists the offending subscription name, the unknown action name, AND the registered plugin names — matches D2's "diagnostic listing the unknown name and the registered plugin names".

**Acceptance Criteria:**
- `Program.cs` registers `ChannelActionDispatcher` as a singleton AND wires it as `IHostedService` AND aliases it to `IActionDispatcher` — same instance for all three roles (a single `AddSingleton<ChannelActionDispatcher>` plus two forwarder lambdas).
- `BlueIris.PluginRegistrar` is added to the `registrars` collection BEFORE `builder.Build()`.
- The startup validation block exists between `builder.Build()` and `app.RunAsync()`, throws `InvalidOperationException` listing both the unknown name and the registered plugin names.
- `git grep -n "BlueIris.PluginRegistrar" src/FrigateRelay.Host/Program.cs` returns at least one match.
- `git grep -n "ChannelActionDispatcher" src/FrigateRelay.Host/Program.cs` returns at least three matches (the AddSingleton + two forwarders).
- `dotnet build FrigateRelay.sln -c Release` succeeds with zero warnings.
- Running the host with no `appsettings.json` (no subscriptions) starts successfully (empty Subscriptions yields no validation work).

### Task 3: Tests for action-name fail-fast + EventPump dispatch path
**Files:** `tests/FrigateRelay.Host.Tests/Dispatch/SubscriptionActionWiringTests.cs`, `tests/FrigateRelay.Host.Tests/EventPumpDispatchTests.cs`
**Action:** create
**Description:**

**`SubscriptionActionWiringTests.cs`** — covers Program.cs's startup validation. Since `Program.cs` is the entry point (top-level statements), invoke its logic from a test by replicating the validation block in a helper method or by extracting it to an `internal static StartupValidation.ValidateActions(...)` helper called from both `Program.cs` and the tests. **Recommendation:** extract to `src/FrigateRelay.Host/StartupValidation.cs` as `internal static class`, then test it directly. Update Program.cs Task 2 acceptance to expect the call site delegates to `StartupValidation.ValidateActions(...)`.

Tests:
1. **`ValidateActions_WithUnknownActionName_ThrowsInvalidOperationException_ListingNameAndRegisteredPlugins`** — given a subscription with `Actions = ["DoesNotExist"]` and one registered plugin `"BlueIris"`, calling `StartupValidation.ValidateActions(subs, actionPlugins)` throws `InvalidOperationException` whose message contains BOTH `"DoesNotExist"` and `"BlueIris"` and `"FrontDoor"` (the subscription name).
2. **`ValidateActions_WithKnownActionName_ReturnsWithoutThrowing`** — given `Actions = ["BlueIris"]` and plugin `"BlueIris"` registered, no exception.
3. **`ValidateActions_CaseInsensitiveOrdinalMatch_AcceptsMixedCase`** — given `Actions = ["blueiris"]` and plugin `"BlueIris"` registered, no exception.
4. **`ValidateActions_WithEmptyActions_DoesNothing`** — `Actions = []` (default) is fail-safe (D2), no exception even if no plugins are registered.

**`EventPumpDispatchTests.cs`** — covers the EventPump → IActionDispatcher hop. Use an in-memory `IEventSource` stub (yields a single `EventContext`), an NSubstitute `IActionDispatcher` mock, an NSubstitute `IActionPlugin` (`Name => "BlueIris"`), and a `HostSubscriptionsOptions` with one subscription whose `Actions = ["BlueIris"]`.

5. **`PumpAsync_OnMatchedEvent_CallsDispatcherEnqueueAsync_OncePerActionName`** — assert `dispatcher.Received(1).EnqueueAsync(Arg.Is<EventContext>(c => c.EventId == "ev-1"), Arg.Is<IActionPlugin>(p => p.Name == "BlueIris"), Arg.Is<IReadOnlyList<IValidationPlugin>>(v => v.Count == 0), Arg.Any<CancellationToken>())`.
6. **`PumpAsync_OnDedupeSuppressed_DoesNotCallDispatcher`** — second event with same key is suppressed by DedupeCache; `dispatcher.DidNotReceive().EnqueueAsync(...)` for the second event (NSubstitute call-count assertion).
7. **`PumpAsync_WithMultipleActionsInSubscription_DispatchesEachOnce`** — `Actions = ["BlueIris", "Pushover"]`, two registered plugins; dispatcher receives two distinct `EnqueueAsync` calls (one per plugin name).

**Acceptance Criteria:**
- All 7 tests exist with the exact names above and pass.
- Test 1 asserts the diagnostic message contains the subscription name AND the unknown action name AND the registered plugin name (FluentAssertions `.WithMessage("*FrontDoor*DoesNotExist*BlueIris*")`).
- Test 5 asserts the validators parameter is empty (Phase 4 D4 invariant).
- `dotnet run --project tests/FrigateRelay.Host.Tests -c Release -- --filter-query "/*/*/SubscriptionActionWiringTests/*|/*/*/EventPumpDispatchTests/*"` reports all 7 passing.
- `git grep -nE '\.(Result|Wait)\(' tests/FrigateRelay.Host.Tests/Dispatch/ tests/FrigateRelay.Host.Tests/EventPumpDispatchTests.cs` returns zero matches.

## Verification

```bash
dotnet build FrigateRelay.sln -c Release
dotnet run --project tests/FrigateRelay.Host.Tests -c Release
git grep -n "BlueIris.PluginRegistrar" src/FrigateRelay.Host/Program.cs
git grep -nE '\.(Result|Wait)\(' src/ tests/
git grep -n "ServicePointManager" src/

# Smoke test: running with empty subscriptions must NOT fail
dotnet run --project src/FrigateRelay.Host -c Release > /tmp/host.log 2>&1 &
sleep 3
kill -INT "$(pgrep -f 'FrigateRelay.Host/bin/Release/net10.0/FrigateRelay.Host$' | head -1)"
wait
grep "Application is shutting down" /tmp/host.log
```

Expected: build clean, all Host.Tests pass, BlueIris.PluginRegistrar wired in Program.cs, no `.Result/.Wait`, smoke test exits gracefully.

## Notes for the builder

- The DispatcherOptions wiring in Program.cs Task 2 reads `BlueIris:QueueCapacity` from configuration directly — it's a one-off cross-plugin convention. If a future plugin (e.g., Pushover in Phase 6) wants its own queue capacity, this block extends with another `if`. Refactoring to a generic loop is premature optimization (Rule of Three) — defer until the third plugin lands.
- `_actionsByName[actionName]` in EventPump uses the indexer not `TryGetValue` — by the time we get here, Program.cs has validated every name. An exception means the host startup validation has a bug.
- The `IEnumerable<IActionPlugin>` injected into both `EventPump` and `ChannelActionDispatcher` is the SAME enumeration (DI delivers the same singleton instances). Both use `StringComparer.OrdinalIgnoreCase` for the name dictionary — case-insensitive match across the whole pipeline (D2).
- `StartupValidation` extraction is a small refactor; preferred over copy-pasting the validation logic into tests because it makes the validation testable in isolation and keeps `Program.cs` slim.
- The smoke test in Verification confirms that a host with no subscriptions and no BlueIris config still starts cleanly. With `BlueIris.PluginRegistrar` always in the registrar list, **the BlueIris options validation `.ValidateOnStart()` would fire at host start even without a subscription using BlueIris** — this is a foot-gun. **Mitigation:** document in `appsettings.json.example` that `BlueIris:TriggerUrlTemplate` is required when the plugin is registered. **Alternative:** make the BlueIris registrar conditional on the `BlueIris` config section being present. **Recommendation:** make the validation conditional — modify the registrar (PLAN-2.2 retroactively, or note in this plan) so that `.ValidateOnStart()` is gated by `context.Configuration.GetSection("BlueIris").Exists()`. **DECISION (architect):** apply this guard in **THIS plan's Task 2** by wrapping the BlueIris registrar's add-to-list in `if (builder.Configuration.GetSection("BlueIris").Exists())`. That keeps PLAN-2.2 unchanged and centralizes the conditionality at the host's wiring layer.

```csharp
List<IPluginRegistrar> registrars = [new FrigateRelay.Sources.FrigateMqtt.PluginRegistrar()];
if (builder.Configuration.GetSection("BlueIris").Exists())
    registrars.Add(new FrigateRelay.Plugins.BlueIris.PluginRegistrar());
```

This pattern generalizes for future plugins (Pushover, etc.) — each plugin's registrar adds itself only when its config section is present. Document this convention in a code comment.
