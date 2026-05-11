# Research: Phase 16 — v1.3.0 Surface Inventory

**Date:** 2026-05-07
**Phase:** 16
**Feeds:** PLAN-16.1.1 (#18), PLAN-16.1.2 (#22), PLAN-16.1.3 (#30)

---

## 1. Surface Inventory

### Issue #18 — `Otel:MetricsTags:KnownCameras` + Central `MetricsTagWriter`

#### Camera-tag write surface

The `camera` tag is written at **8 of 10 counters**, all via `DispatcherDiagnostics` helper methods in
`src/FrigateRelay.Host/Dispatch/DispatcherDiagnostics.cs`. The two counters that do **not** carry `camera` are:

| Counter | `camera` tag? | Helper method | Line approx |
|---|---|---|---|
| `frigaterelay.events.received` | YES | `IncrementEventsReceived(EventContext)` | line 132 |
| `frigaterelay.events.matched` | YES | `IncrementEventsMatched(EventContext, string)` | line 145 |
| `frigaterelay.actions.dispatched` | YES | `IncrementActionsDispatched(DispatchItem)` | line 158 |
| `frigaterelay.actions.succeeded` | YES | `IncrementActionsSucceeded(DispatchItem)` | line 171 |
| `frigaterelay.actions.failed` | YES | `IncrementActionsFailed(DispatchItem)` | line 184 |
| `frigaterelay.validators.passed` | YES | `IncrementValidatorsPassed(DispatchItem, string)` | line 198 |
| `frigaterelay.validators.rejected` | YES | `IncrementValidatorsRejected(DispatchItem, string)` | line 212 |
| `frigaterelay.dispatch.drops` | YES | `IncrementDrops(DispatchItem, string)` | line 228 |
| `frigaterelay.dispatch.exhausted` | YES | `IncrementExhausted(DispatchItem)` | line 241 |
| `frigaterelay.errors.unhandled` | NO | `IncrementErrorsUnhandled(string)` | line 254 |

`IncrementErrorsUnhandled` only carries `component` — it is unaffected by #18.

#### Tag-list construction shape

All camera-carrying helpers use `TagList` struct (BCL, no allocations):

```csharp
EventsReceived.Add(1, new TagList
{
    { "camera", ctx.Camera },
    { "label", ctx.Label },
});
```

`DispatchItem`-taking helpers read `item.Context.Camera`. `IncrementEventsReceived` and `IncrementEventsMatched` read `ctx.Camera` directly from `EventContext`. The new `MetricsTagWriter.NormalizeCameraTag(string camera)` helper needs to intercept the string value before it enters the `TagList`.

#### Existing `MetricsTagsOptions` / observability namespace

- **No `MetricsTagsOptions` class exists anywhere in the codebase.** Confirmed via grep — greenfield.
- **No `src/FrigateRelay.Host/Observability/` directory exists.** The only observability-related file in the Host source tree is `src/FrigateRelay.Host/Dispatch/DispatcherDiagnostics.cs`. Tests have `tests/FrigateRelay.Host.Tests/Observability/` but the source does not. The architect must create the directory.

#### Observability config wiring location

`src/FrigateRelay.Host/HostBootstrap.cs` line 44–61 wires OTel. The `Otel:OtlpEndpoint` config key is read at line 44. The new `services.Configure<MetricsTagsOptions>(config.GetSection("Otel:MetricsTags"))` registration should go in `ConfigureServices` immediately after or alongside the OTel block — same method, same logical cluster.

#### Existing cardinality-test pattern to mirror

`tests/FrigateRelay.Host.Tests/Observability/CounterTagMatrixTests.cs` — directly invokes `Increment*` helpers with sentinel values, uses `MeterListener` to capture emitted tags, asserts exact key set + no `event_id`. New `MetricsCardinalityTests` should follow this pattern (direct helper invocation, not full pipeline).

`tests/FrigateRelay.Host.Tests/Observability/CounterIncrementTests.cs` — full-pipeline tests (EventPump + dispatcher). The three new #18 tests (known-camera passthrough, unknown-camera folded, empty-allowlist passthrough) are more like `CounterTagMatrixTests` in scope (helper-level, not pipeline-level).

#### DI lifetime note (D1 / OQ-2)

`MetricsTagWriter` should be registered as a **singleton** injected with `IOptionsMonitor<MetricsTagsOptions>` (supports future hot-reload without restart). The `DispatcherDiagnostics` static helpers will need to call into `MetricsTagWriter`; since `DispatcherDiagnostics` is currently a `static class` with static `Counter<long>` fields, the architect must decide whether to inject `MetricsTagWriter` into the static class (not idiomatic) or refactor the increment call sites to accept it as a parameter. The cleanest approach: make `MetricsTagWriter` a singleton DI service and pass it to `EventPump` and `ChannelActionDispatcher` constructors so they call `tagWriter.NormalizeCameraTag(camera)` before passing the value to the existing static helpers. This avoids touching the `DispatcherDiagnostics` static surface.

#### `InternalsVisibleTo` for new internal type

`src/FrigateRelay.Host/FrigateRelay.Host.csproj` already has:
```xml
<InternalsVisibleTo Include="FrigateRelay.Host.Tests" />
<InternalsVisibleTo Include="DynamicProxyGenAssembly2" />
```
New `internal sealed class MetricsTagWriter` is covered by the existing entries — no csproj change required unless NSubstitute mocking of `MetricsTagWriter` is needed (unlikely; it is a simple normalization helper, not an interface).

---

### Issue #22 — Replace `Task.Delay` Sites with Deterministic Polling

#### Confirmed sites (4 timing delays, 2 fake-source `Timeout.Infinite` — excluded)

The grep `Task\.Delay` in `tests/FrigateRelay.Host.Tests/Observability/` returns 6 matches, but 2 are `Task.Delay(Timeout.Infinite, ct)` inside fake `IEventSource` implementations (the standard "stay alive until cancelled" idiom). Those are correct usage, not fragility sites. The 4 fragility sites are:

| File | Line | Delay | Context | What it waits for |
|---|---|---|---|---|
| `EventPumpSpanTests.cs` | 285 | `Task.Delay(400)` | Inside `RunPumpAsync` helper, between `pump.StartAsync` and `cts.CancelAsync` | Pump to process the single event from `FakeSource` and emit OTel spans (Activity completion) |
| `CounterIncrementTests.cs` | 359 | `Task.Delay(400)` | Inside `RunPumpAsync` helper, between `pump.StartAsync` and `cts.CancelAsync` | Pump to process events from `BatchSource` and emit counter measurements |
| `CounterIncrementTests.cs` | 393 | `Task.Delay(300)` | Inside `RunPumpAsyncWithSource` helper, between `pump.StartAsync` and `cts.CancelAsync` | `FaultingSource` to throw and pump to emit `errors.unhandled` counter |
| `CounterIncrementTests.cs` | 425 | `Task.Delay(shouldThrow ? 200 : 100)` | Inside `RunDispatcherAsync` helper, between `dispatcher.EnqueueAsync` and `dispatcher.StopAsync` | `ChannelActionDispatcher` consumer loop to process the enqueued item and emit action counters |

#### What each site needs (D2 signal classification)

- **Lines 285, 359, 393** — waiting for the `EventPump` to emit log messages (`LogMatchedEvent`, `LogPumpStopped`, `LogPumpFaulted`). These use `CapturingLogger<EventPump>` already constructed in the same helper. **Signal: log-record count on `CapturingLogger<EventPump>`** → use new `WaitForRecordsAsync(int count, TimeSpan timeout)` on `CapturingLogger<T>`.
  - Line 285: pump processes 1 event → `LogMatchedEvent` fires → wait for `Entries.Count >= 1` (or use `LogPumpStopped` which fires in `finally` after cancellation — more reliable).
  - Line 359: same as 285 but in `CounterIncrementTests.RunPumpAsync`.
  - Line 393: `FaultingSource` path → `LogPumpFaulted` fires → wait for at least 1 `Error`-level entry.
- **Line 425** — waiting for `ChannelActionDispatcher` consumer loop. The dispatcher's `CapturingLogger<ChannelActionDispatcher>` receives logs when action succeeds/fails. **Signal: log-record count on `CapturingLogger<ChannelActionDispatcher>`** → also use `WaitForRecordsAsync`. `StubPlugin` succeeds → `LogActionSucceeded` fires; `ThrowingPlugin` fails → retry logs + `LogActionExhausted` fire.

**Conclusion:** All 4 sites are log-record-polling cases, not `MeterListener`-count cases. The `CapturingLogger<T>.WaitForRecordsAsync` extension covers all 4. No per-fixture MeterListener polling helper is needed for these specific sites (though the architect may add one as a forward-looking pattern per D2).

#### `CapturingLogger<T>` — actual field name

`tests/FrigateRelay.TestHelpers/CapturingLogger.cs` exposes:

```csharp
public List<LogEntry> Entries { get; } = new();
```

The field is **`Entries`** (not `Records`, not `LogEntries`, not `Captured`). The ISSUES.md ID-22 note "initial inline-fix attempt failed because `Records` did not exist" is confirmed — `Records` does not exist. The new `WaitForRecordsAsync` method must poll `Entries.Count`.

`LogEntry` is a nested record: `(LogLevel Level, EventId Id, string Message, IReadOnlyList<KeyValuePair<string, object?>>? State)`.

Recommended signature for the extension:
```csharp
public async Task WaitForRecordsAsync(int count, TimeSpan timeout, CancellationToken ct = default)
```
Polls `Entries.Count >= count` with a small interval (25ms per OQ-3), throws `TimeoutException` on expiry.

#### Greppable invariant confirmation

After Phase 16, `git grep -nE 'Task\.Delay' tests/FrigateRelay.Host.Tests/Observability/` must return **empty**. Current count: 4 fragility sites + 2 correct `Timeout.Infinite` usages. The `Timeout.Infinite` usages are in nested private `FakeSource`/`BatchSource` classes, which live inside the observability test files — those lines will remain. **The invariant as stated will NOT be achievable without also removing the `Timeout.Infinite` lines**, which are correct idiom. The architect must either:
  - Scope the grep more tightly: `grep -nE 'Task\.Delay\([0-9]'` (matches numeric delay only), or
  - Accept that the invariant must be phrased as "no fixed-time `Task.Delay` calls" rather than "no `Task.Delay`".

**This is a scope risk the architect must decide.** The `Timeout.Infinite` lines in `BatchSource.ReadEventsAsync` and `FakeSource.ReadEventsAsync` are structurally correct and should not be removed.

---

### Issue #30 — `PluginRegistrar.cs` HttpClient Registration Unification

#### Current registrar shape — all three files are byte-for-byte symmetric

All three registrars (`CodeProjectAi`, `Roboflow`, `Doods2`) follow the identical structure. The `BaseAddress` + `Timeout` mutation in the keyed-singleton factory body:

```csharp
// CPAI: src/FrigateRelay.Plugins.CodeProjectAi/PluginRegistrar.cs lines 75-84
context.Services.AddKeyedSingleton<IValidationPlugin>(instanceKey, (sp, key) =>
{
    var name = (string)key!;
    var opts = sp.GetRequiredService<IOptionsMonitor<CodeProjectAiOptions>>().Get(name);
    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient($"CodeProjectAi:{name}");
    http.BaseAddress = new Uri(opts.BaseUrl);   // line 80
    http.Timeout = opts.Timeout;                // line 81
    var logger = sp.GetRequiredService<ILogger<CodeProjectAiValidator>>();
    return new CodeProjectAiValidator(name, opts, http, logger);
});
```

Roboflow (lines 75-84) and DOODS2 (lines 75-84) are structurally identical — same line range, same mutation pattern, only type names differ (`RoboflowOptions`/`RoboflowValidator`, `Doods2Options`/`Doods2Validator`). There are **no per-plugin variants** the architect needs to preserve. The three registrars are truly symmetric — CodeRabbit's claim is confirmed.

The `AddHttpClient` builder already exists in each registrar (lines ~54–72) and configures `SocketsHttpHandler` + `PooledConnectionLifetime` + optional TLS bypass. The `BaseAddress` / `Timeout` moves from the keyed-singleton factory into a `(sp, client) =>` lambda on that existing `AddHttpClient` call.

#### Target shape (same for all three):

```csharp
var clientName = $"CodeProjectAi:{instanceKey}";
context.Services
    .AddHttpClient(clientName, (sp, client) =>
    {
        var opts = sp.GetRequiredService<IOptionsMonitor<CodeProjectAiOptions>>().Get(instanceKey);
        client.BaseAddress = new Uri(opts.BaseUrl);
        client.Timeout = opts.Timeout;
    })
    .ConfigurePrimaryHttpMessageHandler(sp => { /* unchanged TLS handler */ });
```

The keyed-singleton factory body then simplifies to just the constructor call (no `http.BaseAddress`/`http.Timeout` mutations).

#### Existing tests — DOODS2 already has 5 registrar tests

The ISSUES.md note "Roboflow already has 5 tests from PR #42; CPAI + DOODS2 do not yet" is **partially stale**. DOODS2 now has `tests/FrigateRelay.Plugins.Doods2.Tests/Doods2PluginRegistrarTests.cs` with **5 tests** (added during Phase 14 build, confirmed by reading the file):

1. `Register_OneDoods2Entry_RegistersKeyedValidatorAndNamedHttpClient`
2. `Register_TwoDoods2Entries_RegistersBothIndependently`
3. `Register_NonDoods2Entry_DoesNotRegisterIt`
4. `Register_NoValidatorsSection_ReturnsCleanly`
5. `Register_AllowInvalidCertificatesTrue_ConfiguresTlsBypassHandler`

**CPAI is the only registrar without `*PluginRegistrarTests`.** `tests/FrigateRelay.Plugins.CodeProjectAi.Tests/` contains only `CodeProjectAiValidatorTests.cs` — no registrar tests. The optional backfill (D6) is **5 tests for CPAI only**, not 10. Roboflow and DOODS2 are already at parity.

The Roboflow registrar tests do **not** assert `BaseAddress` or `Timeout` flow through `IHttpClientFactory.CreateClient`. They assert: keyed service resolves, named `HttpClient` resolves, two entries are independent, non-matching type is skipped, no-section returns cleanly, TLS bypass handler builds without throw. The DOODS2 registrar tests follow the exact same 5-test pattern. Any new CPAI registrar tests should mirror this shape exactly.

#### Consumer impact of moving to builder pattern

No consumers break. `IHttpClientFactory.CreateClient(name)` returns an `HttpClient` that has already been configured by the builder lambda before the factory returns it — the resolved client is fully prepared at the point the keyed-singleton factory body calls `CreateClient`. The keyed-singleton is a singleton; the factory body runs exactly once per instance key. Behavior is identical; the only change is where `BaseAddress`/`Timeout` are set (builder lambda vs. factory body).

---

## 2. Convention Confirmations

- **MTP test invocation:** `dotnet run --project tests/<project> -c Release` (NOT `dotnet test`). Confirmed active for all test projects.
- **Test naming:** `Method_Condition_Expected` underscores. `CA1707` silenced in `.editorconfig` for `tests/`. New tests must follow this pattern.
- **`CapturingLogger<T>` from `tests/FrigateRelay.TestHelpers/`:** Exposed via `global using FrigateRelay.TestHelpers;` in `Usings.cs` per test project. The field is `Entries` (not `Records`). Do NOT redefine per-assembly.
- **`<InternalsVisibleTo Include="DynamicProxyGenAssembly2" />`:** Already present in `FrigateRelay.Host.csproj`. New `internal sealed class MetricsTagWriter` is covered. No csproj change needed unless NSubstitute mocking of `MetricsTagWriter` is planned (not expected).
- **Warnings-as-errors:** `Directory.Build.props`. All new code must compile clean on Linux + Windows.
- **No `event_id` tag:** `MetricsTagWriter.NormalizeCameraTag` only normalizes the `camera` tag value — it does not add any tag keys. The new helper cannot accidentally introduce `event_id`.
- **FluentAssertions pinned to 6.12.2:** Apache-2.0. Do not upgrade. New tests use the same version.
- **`src/FrigateRelay.Host/Observability/` directory:** Does not exist yet. Must be created by the architect for `MetricsTagWriter.cs` and `MetricsTagsOptions.cs`.

---

## 3. Test-Count Baseline

**151 Host tests** as of v1.2.1 (confirmed in CONTEXT-16.md operator confirmation). Phase 16 success criterion: all 151 existing pass + Phase 16 net-new.

Estimated net-new by issue:
- **#18:** 3 new `MetricsCardinalityTests` (known-camera passthrough, unknown-camera-to-`"other"`, empty-allowlist passthrough).
- **#22:** 0 new tests. 4 existing test helper methods refactored in-place; test count unchanged.
- **#30:** 5 new `CodeProjectAiPluginRegistrarTests` (CPAI backfill, optional per D6); 0 for Roboflow (already exists); 0 for DOODS2 (already exists). Note: tests for CPAI and DOODS2 registrar tests live in separate test projects (`FrigateRelay.Plugins.CodeProjectAi.Tests`, `FrigateRelay.Plugins.Doods2.Tests`), not in `FrigateRelay.Host.Tests`. The 151 baseline is Host.Tests only.

**Phase 16 Host.Tests target:** 154 (151 + 3 from #18). CPAI registrar tests add 5 to `FrigateRelay.Plugins.CodeProjectAi.Tests` (separate counter, not part of the 151 baseline).

---

## 4. Risk Callouts

### RC-1: `Task.Delay(Timeout.Infinite, ct)` lines will survive the #22 cleanup

The ROADMAP greppable invariant `git grep -nE 'Task\.Delay' tests/FrigateRelay.Host.Tests/Observability/` returning **empty** after Phase 16 is **not achievable** without removing the structurally correct `Task.Delay(Timeout.Infinite, ct)` calls in `BatchSource.ReadEventsAsync` (CounterIncrementTests.cs line 481) and `FakeSource.ReadEventsAsync` (EventPumpSpanTests.cs line 337). These are the standard "stay alive until cancelled" idiom and are not fragility sites. The architect must tighten the invariant grep to `Task\.Delay\([0-9]` or accept that the 2 `Timeout.Infinite` lines are explicitly excluded. This is a documentation fix, not a code change.

### RC-2: DOODS2 already has registrar tests — ISSUES.md description is stale

ISSUES.md ID-30 says "CPAI + DOODS2 do not yet" have `*PluginRegistrarTests`. That was accurate at the time of filing (during PR #43 review). DOODS2 now has 5 registrar tests in `tests/FrigateRelay.Plugins.Doods2.Tests/Doods2PluginRegistrarTests.cs`. The #30 optional backfill is **CPAI only (5 tests)**, not 10. The architect should not plan 10 new tests.

### RC-3: `DispatcherDiagnostics` is a static class — injection path needs design

`DispatcherDiagnostics` is `internal static class` with static `Counter<long>` fields and static helper methods. It cannot be injected. The `MetricsTagWriter` must be injected into the callers of `DispatcherDiagnostics` (`EventPump` and `ChannelActionDispatcher`), and those callers normalize the `camera` value before passing it to the existing static helpers. This is additive — no changes to `DispatcherDiagnostics` static signatures are required. The architect should plan `EventPump` and `ChannelActionDispatcher` constructor changes to accept `MetricsTagWriter` and update `CounterIncrementTests`/`EventPumpSpanTests` helper builders accordingly (these tests `new` the classes directly).

### RC-4: `RunDispatcherAsync` in `CounterIncrementTests` omits `parallelValidators` parameter

`CounterIncrementTests.RunDispatcherAsync` (line 402) calls `dispatcher.EnqueueAsync` with explicit `parallelValidators: false` omitted — the parameter is named and has no default in the interface. Looking at line 419–421:
```csharp
await dispatcher.EnqueueAsync(ctx, plugin, validators, subscription,
    perActionSnapshotProvider: null,
    subscriptionDefaultSnapshotProvider: null,
    ct: cts.Token);
```
The `parallelValidators` parameter is missing from this call — this will be a compile error if the signature requires it. Checking line 509 of `CounterIncrementTests.cs` (the `NoOpDispatcher` stub): `string? subscriptionDefaultSnapshotProvider, bool parallelValidators,` — the stub has the parameter but the `RunDispatcherAsync` call site may not pass it. The architect must verify this compiles at baseline before adding new tests. If it does compile today (tests pass at 151), the interface may have a default or the call site does pass it positionally. This should be a quick check before PLAN-16 build begins.

### RC-5: `MetricsTagWriter` singleton DI + `EventPump`/`ChannelActionDispatcher` test helpers

The ~10 test helper methods in `CounterIncrementTests.cs` and `EventPumpSpanTests.cs` that `new` `EventPump` and `ChannelActionDispatcher` directly will need to supply a `MetricsTagWriter` instance. For tests that do not care about the normalization (i.e., all existing tests with empty `KnownCameras`), a `MetricsTagWriter` constructed with an empty/passthrough `IOptionsMonitor<MetricsTagsOptions>` stub is sufficient. The architect should plan a small static factory method or a `StaticMonitor<MetricsTagsOptions>` stub (the pattern already exists in both test files) to keep test boilerplate low.

---

## Sources

All findings from direct codebase inspection. No external URLs consulted for this research (pure code-surface mapping).

- `src/FrigateRelay.Host/Dispatch/DispatcherDiagnostics.cs` — full read (10 counters, 9 helper methods, `TagList` pattern)
- `src/FrigateRelay.Host/EventPump.cs` — full read (counter call sites, constructor, no camera-tag normalization)
- `src/FrigateRelay.Host/HostBootstrap.cs` — full read (OTel wiring location, no existing `MetricsTagsOptions` registration)
- `src/FrigateRelay.Host/FrigateRelay.Host.csproj` — full read (`InternalsVisibleTo` entries confirmed)
- `src/FrigateRelay.Plugins.CodeProjectAi/PluginRegistrar.cs` — full read (lines 75-84 mutation shape)
- `src/FrigateRelay.Plugins.Roboflow/PluginRegistrar.cs` — full read (lines 75-84 mutation shape, symmetric)
- `src/FrigateRelay.Plugins.Doods2/PluginRegistrar.cs` — full read (lines 75-84 mutation shape, symmetric)
- `tests/FrigateRelay.TestHelpers/CapturingLogger.cs` — full read (`Entries` field confirmed, no `Records`)
- `tests/FrigateRelay.Host.Tests/Observability/CounterIncrementTests.cs` — full read (3 `Task.Delay` sites at lines 359/393/425, 2 `Timeout.Infinite` excluded)
- `tests/FrigateRelay.Host.Tests/Observability/EventPumpSpanTests.cs` — full read (1 `Task.Delay` site at line 285, 1 `Timeout.Infinite` excluded)
- `tests/FrigateRelay.Host.Tests/Observability/CounterTagMatrixTests.cs` — partial read (test pattern confirmed)
- `tests/FrigateRelay.Plugins.Roboflow.Tests/RoboflowPluginRegistrarTests.cs` — full read (5 tests, no `BaseAddress`/`Timeout` assertions)
- `tests/FrigateRelay.Plugins.Doods2.Tests/Doods2PluginRegistrarTests.cs` — full read (5 tests, same pattern as Roboflow)
- `tests/FrigateRelay.Plugins.CodeProjectAi.Tests/` — glob confirmed no `*PluginRegistrarTests.cs` exists
- `.shipyard/ROADMAP.md` lines 525–596 — Phase 16 deliverables and success criteria
- `.shipyard/phases/16/CONTEXT-16.md` — D1–D9 design decisions
- `.shipyard/ISSUES.md` — ID-18, ID-22, ID-30 full text

## Uncertainty Flags

- **RC-4 (compile check):** The `RunDispatcherAsync` call at line 419–421 of `CounterIncrementTests.cs` may be missing the `parallelValidators` named argument. Tests pass at baseline (151), so either the call compiles positionally or the interface has a default value. Architect should verify before PLAN-16.1.2 build step, since adding `MetricsTagWriter` to the dispatcher constructor will also touch these call sites.
- **All-log signal assumption for #22 line 425:** The `RunDispatcherAsync` site waits for `ChannelActionDispatcher` consumer to emit action counters. The log-record approach works only if the dispatcher emits at least one log message per enqueued item (succeeded or failed). This should be confirmed by reading `ChannelActionDispatcher.cs` consumer loop — not done in this research pass. If no log is emitted for the success path, the MeterListener-count approach is needed for that one site.
