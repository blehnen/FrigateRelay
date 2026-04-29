# Research: Phase 9 — Observability

## Context

FrigateRelay is a .NET 10 Worker SDK host. Phase 9 wires full structured logging
(Serilog sinks), OpenTelemetry traces, and metrics across the pipeline that was
completed in Phases 1–8. The CONTEXT-9.md decisions (D1–D9) are binding; this
document surfaces the concrete integration points and package facts that the
architect needs to implement them.

---

## §1 — Existing Instrumentation Surface

### `ActivitySource` / `Meter` references (pre-Phase-9)

`DispatcherDiagnostics` is already referenced in `ChannelActionDispatcher.cs` (lines
100–103, 243–244) for a `Drops` counter and an `Exhausted` counter, and an
`ActivitySource` for the `"ActionDispatch"` span (line 173). The class file does not
exist at `src/FrigateRelay.Host/Diagnostics/DispatcherDiagnostics.cs` — it is
referenced but the file is absent, meaning **this is dead / stub code that will not
compile**. The architect must create `DispatcherDiagnostics.cs` as a priority-zero
task in Phase 9.

Existing references in `ChannelActionDispatcher.cs`:
- Line 100: `DispatcherDiagnostics.Drops.Add(1, ...)`
- Line 173: `DispatcherDiagnostics.ActivitySource.StartActivity("ActionDispatch", ...)`
- Line 243: `DispatcherDiagnostics.Exhausted.Add(1, ...)`

No other `ActivitySource`, `StartActivity`, `new Meter`, or `new ActivitySource`
references exist anywhere else in `src/`.

### `ILogger<T>` typed consumers

| File | Logger type |
|------|-------------|
| `src/FrigateRelay.Host/EventPump.cs` | `ILogger<EventPump>` |
| `src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs` | `ILogger<ChannelActionDispatcher>` |
| `src/FrigateRelay.Sources.FrigateMqtt/FrigateMqttEventSource.cs` | `ILogger<FrigateMqttEventSource>` |
| Snapshot / Validator / Plugin classes | `ILogger<T>` per class (verify at plan time) |

All use the hand-rolled `Action<ILogger, ...>` delegate pattern via
`LoggerMessage.Define<...>()` (D6 — keep as-is, new Phase 9 sites must match).

### Existing `Action<ILogger,...>` delegates (template for new Phase 9 delegates)

**EventPump.cs:**
- `LogMatchedEvent` — `(string source, string subscription, string camera, string label, string eventId)` → Info
- `LogPumpStopped` — `(string source)` → Info
- `LogPumpFaulted` — `(string source, Exception error)` → Error
- `LogDispatchEnqueued` — `(string action, string subscription, string eventId)` → Debug

**ChannelActionDispatcher.cs:**
- `LogDropped` — `(string eventId, string action, int capacity)` → Warning, EventId 10
- `LogPluginNotRegistered` — `(string pluginName)` → Error, EventId 11
- `LogRetryExhausted` — `(string eventId, string action)` → Warning, EventId 101
- `LogValidatorRejected` — `(string eventId, string camera, string label, string action, string validator, string reason)` → Warning, EventId 20

**FrigateMqttEventSource.cs:**
- `LogMqttConnected` — no args → Info, EventId 1
- `LogMqttDisconnected` — no args → Info, EventId 2
- `LogMqttConnectFailed` — `(Exception error)` → Warning, EventId 3
- `LogMqttReceiveFailed` — `(string reason)` → Warning, EventId 4

New Phase 9 delegates must use EventIds that do not collide with the above.
Suggested Phase 9 EventId range: **500–599** (observability-specific).

---

## §2 — Pipeline Shape (for span hierarchy)

The full producer-to-consumer flow:

```
FrigateMqttEventSource.OnMessageReceivedAsync()   [MQTT push callback, any thread]
  → TryPublishAsync(rawPayload)                   [JSON parse, EventContext construct]
  → _channel.Writer.WriteAsync(context)           [internal unbounded Channel<EventContext>]

FrigateMqttEventSource.ReadEventsAsync()
  → _channel.Reader.ReadAllAsync(ct)              [pull side — yielded to EventPump]

EventPump.PumpAsync(source, ct)                   [BackgroundService task, one per IEventSource]
  → await foreach context in source.ReadEventsAsync(ct)
  → SubscriptionMatcher.Match(context, subs)      [sync, in-memory]
  → DedupeCache.TryEnter(sub, context)            [scoped IMemoryCache]
  → _dispatcher.EnqueueAsync(context, plugin, ...) [for each matched sub×action]

ChannelActionDispatcher.EnqueueAsync()
  → channel.Writer.WriteAsync(new DispatchItem(..., Activity.Current, ...))
                                                  [producer side — captures Activity.Current]

ChannelActionDispatcher.ConsumeAsync()            [2 consumer Tasks per plugin, Task.Run]
  → reader.ReadAllAsync(ct)                       [consumer side]
  → ActivitySource.StartActivity("ActionDispatch", ..., parentContext: item.Activity?.Context)
  → validator chain (per item.Validators)
  → plugin.ExecuteAsync(context, snapshotContext, ct)
```

**Span boundary decisions for architect:**

| Span | Where it starts | Where it ends |
|------|-----------------|---------------|
| `mqtt.receive` | `FrigateMqttEventSource.TryPublishAsync` entry (covers JSON parse + project) OR `EventPump.PumpAsync` receive site (simpler, no ActivitySource in source plugin) | On `channel.Writer.WriteAsync` completion |
| `event.match` | After `source.ReadEventsAsync` yields in `PumpAsync` | After `SubscriptionMatcher.Match` completes |
| `dispatch.enqueue` | Before `_dispatcher.EnqueueAsync` loop | After all `EnqueueAsync` calls for one event |
| `action.<name>.execute` | In `ConsumeAsync` after `StartActivity("ActionDispatch", ...)` | On `plugin.ExecuteAsync` completion or exception |
| `validator.<name>.check` | Inside validator loop in `ConsumeAsync` | After `validator.ValidateAsync` returns |

**Recommendation for `mqtt.receive` boundary:** Start the span inside `EventPump.PumpAsync`
(at the `await foreach` receive site) rather than inside `FrigateMqttEventSource`. This
avoids giving `FrigateMqttEventSource` (a source plugin in a separate assembly) a
dependency on the host's `ActivitySource`. The span then covers matching, dedup, and
enqueue — which is the semantically meaningful unit of "we received and processed one
event."

---

## §3 — `DispatchItem` Shape and `ParentContext` Field

**Current shape** (`src/FrigateRelay.Host/Dispatch/DispatchItem.cs`, lines 25–31):

```csharp
internal readonly record struct DispatchItem(
    EventContext Context,
    IActionPlugin Plugin,
    IReadOnlyList<IValidationPlugin> Validators,
    Activity? Activity,                        // <-- currently Activity? object reference
    string? PerActionSnapshotProvider = null,
    string? SubscriptionSnapshotProvider = null);
```

**Critical finding:** The field is currently typed `Activity?` (an object reference),
NOT `ActivityContext` (the 16-byte struct mandated by D1). The architect must change
this field to `ActivityContext` and update the one write site in
`ChannelActionDispatcher.EnqueueAsync` (line 150) and the one read site in
`ConsumeAsync` (line 176).

**D1-compliant field declaration:**

```csharp
public ActivityContext ParentContext { get; init; } = default;
```

Or as a positional record parameter (matching existing style):

```csharp
internal readonly record struct DispatchItem(
    EventContext Context,
    IActionPlugin Plugin,
    IReadOnlyList<IValidationPlugin> Validators,
    ActivityContext ParentContext,              // was Activity?
    string? PerActionSnapshotProvider = null,
    string? SubscriptionSnapshotProvider = null);
```

**Write site fix** (`EnqueueAsync`, line 150):
```csharp
// Before:
new DispatchItem(ctx, action, validators, Activity.Current, ...)
// After (D1):
new DispatchItem(ctx, action, validators, Activity.Current?.Context ?? default, ...)
```

**Read site fix** (`ConsumeAsync`, line 173–176):
```csharp
// Before:
DispatcherDiagnostics.ActivitySource.StartActivity(
    "ActionDispatch", ActivityKind.Internal,
    parentContext: item.Activity?.Context ?? default);
// After:
DispatcherDiagnostics.ActivitySource.StartActivity(
    "ActionDispatch", ActivityKind.Consumer,
    parentContext: item.ParentContext);        // ActivityContext default = no parent → root span
```

Note: CONTEXT-9.md D1 specifies `ActivityKind.Consumer` for the dispatch span, but
the current code uses `ActivityKind.Internal`. Change to `Consumer` per D1.

---

## §4 — ID-6: `OperationCanceledException` → `ActivityStatusCode.Error` Location

**Location:** `ChannelActionDispatcher.cs`, lines 235–239.

```csharp
catch (OperationCanceledException) when (ct.IsCancellationRequested)
{
    // Graceful shutdown — do not log, do not increment counter.
    dispatchActivity?.SetStatus(ActivityStatusCode.Error, "Cancelled");  // <-- BUG (line 238)
    return;
}
```

The catch guard `when (ct.IsCancellationRequested)` already correctly identifies
graceful shutdown. Only the `SetStatus` call is wrong — it sets `Error` instead of
`Unset`. Fix per D4:

```csharp
catch (OperationCanceledException) when (ct.IsCancellationRequested)
{
    dispatchActivity?.SetStatus(ActivityStatusCode.Unset);  // graceful shutdown is not an error
    return;
}
```

No other `OperationCanceledException` catch blocks set `ActivityStatusCode` in the
codebase.

---

## §5 — OpenTelemetry Package Landscape (.NET 10)

All packages listed here target `net8.0` or later and are confirmed compatible with
.NET 10. Versions are latest stable as of 2026-04-27 per NuGet.org.

| Package | Version | Notes |
|---------|---------|-------|
| `OpenTelemetry.Extensions.Hosting` | **1.15.3** | DI entry point; `services.AddOpenTelemetry()` |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | **1.15.3** | OTLP for traces + metrics |
| `OpenTelemetry.Exporter.InMemory` | **1.15.3** | Testing only; `AddInMemoryExporter(list)` |
| `OpenTelemetry.Instrumentation.Runtime` | **1.15.1** | Stable; GC/JIT/thread pool metrics |
| `OpenTelemetry.Instrumentation.Process` | **1.15.1-beta.1** | Still beta — see note below |

**`OpenTelemetry.Instrumentation.Process` is still in beta.** D3 does not list any
process-level counters in the required set, so this package should be omitted from
Phase 9. `OpenTelemetry.Instrumentation.Runtime` is stable and adds useful GC/JIT
metrics with `.AddRuntimeInstrumentation()` on the metrics builder — include it as a
low-risk addition (one line, no project-specific coupling).

**Registration pattern (per D2 — conditional OTLP):**

```csharp
var otlpEndpoint = builder.Configuration["Otel:OtlpEndpoint"]
    ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("FrigateRelay"))
    .WithTracing(b =>
    {
        b.AddSource("FrigateRelay");
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            b.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
    })
    .WithMetrics(b =>
    {
        b.AddMeter("FrigateRelay");
        b.AddRuntimeInstrumentation();
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            b.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
    });
```

Do NOT call `AddOtlpExporter()` without the endpoint check — the exporter will
attempt a gRPC connection on startup and log errors when no collector is running.

**`OpenTelemetry.Exporter.Console` is excluded** per D2 (no console-fallback).

**`InMemoryExporter` for tests — trace pattern:**

```csharp
var exportedActivities = new List<Activity>();
using var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .AddSource("FrigateRelay")
    .AddInMemoryExporter(exportedActivities,
        options => options.ExportProcessorType = ExportProcessorType.Simple)
    .Build();
// run SUT...
// assert exportedActivities contains expected spans
```

`ExportProcessorType.Simple` is critical in tests — `Batch` (the default) buffers
spans and they may not appear in `exportedActivities` synchronously after `Activity.Stop()`.

**`InMemoryExporter` for tests — metrics pattern:** Use `MeterListener` directly
(§8 below) rather than `AddInMemoryExporter` for counters. The InMemory metrics
exporter requires a full `MeterProvider` with a collection interval, making it
awkward for unit tests where you want synchronous assertion.

---

## §6 — Serilog Package Landscape

All packages are Apache-2.0 licensed (MIT-compatible per project policy).

| Package | Version | License | Notes |
|---------|---------|---------|-------|
| `Serilog` | **4.3.1** | Apache-2.0 | Core |
| `Serilog.Extensions.Hosting` | **10.0.0** | Apache-2.0 | `host.UseSerilog(...)` for Worker SDK |
| `Serilog.Settings.Configuration` | **10.0.0** | Apache-2.0 | Binds from `IConfiguration` |
| `Serilog.Sinks.Console` | **6.1.1** | Apache-2.0 | Colored/themed console output |
| `Serilog.Sinks.File` | **7.0.0** | Apache-2.0 | Rolling file sink |
| `Serilog.Sinks.Seq` | **9.0.0** | Apache-2.0 | Per D7 — conditional on `Serilog:Seq:ServerUrl` |

**Use `Serilog.Extensions.Hosting`, NOT `Serilog.AspNetCore`.** The project uses
`Microsoft.NET.Sdk.Worker`, not `Microsoft.NET.Sdk.Web`. `Serilog.AspNetCore`
pulls in ASP.NET Core middleware and request-logging enrichers that are irrelevant
and add unnecessary weight.

**Registration pattern (Worker SDK, D7-compliant):**

```csharp
// In Program.cs, before builder.Build():
builder.Host.UseSerilog((ctx, services, lc) =>
{
    lc.ReadFrom.Configuration(ctx.Configuration)
      .ReadFrom.Services(services)
      .Enrich.FromLogContext()
      .WriteTo.Console(outputTemplate:
          "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}{NewLine}{Message:lj}{NewLine}{Exception}")
      .WriteTo.File("logs/frigaterelay-.log",
          rollingInterval: RollingInterval.Day,
          retainedFileCountLimit: 7);

    var seqUrl = ctx.Configuration["Serilog:Seq:ServerUrl"];
    if (!string.IsNullOrWhiteSpace(seqUrl))
        lc.WriteTo.Seq(seqUrl);
});
```

**Sharp edge — dual pipeline:** `Microsoft.Extensions.Logging` and Serilog must not
both write to the same sink. The `UseSerilog()` call replaces M.E.L.'s default
providers. Ensure `builder.Logging.ClearProviders()` is NOT called separately (it
would clear Serilog too if called after `UseSerilog`). The `UseSerilog` extension
handles provider replacement internally.

**Sharp edge — bootstrap logger:** `HostBootstrap.ConfigureServices` currently uses
`LoggerFactory.Create(lb => lb.AddConsole())` for a bootstrap logger during plugin
registration (line 78). This bootstrap logger lives before Serilog is wired and
produces plain console output. This is acceptable — it only runs at startup before
`builder.Build()`. No change needed unless the architect wants to replace it with a
Serilog `Log.Logger` bootstrap instance (deferred to Phase 11 per D6).

**`Serilog:Seq:ServerUrl` config key:** `appsettings.json` currently only has
`Logging:LogLevel`. The architect must add the `Serilog` config section and the
`Otel` section as new top-level keys. See §11.

---

## §7 — Counter Registration and Tag Patterns

The `DispatcherDiagnostics` class (which must be created) should own both the
`ActivitySource` and the `Meter`, consolidating all OTel instrumentation in one
place. The ROADMAP specifies the name `"FrigateRelay"` for both.

**Proposed `FrigateRelayDiagnostics.cs`** (or rename existing stub target):

```csharp
internal static class FrigateRelayDiagnostics
{
    public static readonly ActivitySource ActivitySource =
        new("FrigateRelay", version: "1.0.0");

    private static readonly Meter Meter = new("FrigateRelay", version: "1.0.0");

    // frigaterelay.events.received  tags: camera, label
    public static readonly Counter<long> EventsReceived =
        Meter.CreateCounter<long>("frigaterelay.events.received");

    // frigaterelay.events.matched  tags: camera, label, subscription
    public static readonly Counter<long> EventsMatched =
        Meter.CreateCounter<long>("frigaterelay.events.matched");

    // frigaterelay.actions.dispatched  tags: subscription, action
    public static readonly Counter<long> ActionsDispatched =
        Meter.CreateCounter<long>("frigaterelay.actions.dispatched");

    // frigaterelay.actions.succeeded  tags: subscription, action
    public static readonly Counter<long> ActionsSucceeded =
        Meter.CreateCounter<long>("frigaterelay.actions.succeeded");

    // frigaterelay.actions.failed  tags: subscription, action
    public static readonly Counter<long> ActionsFailed =
        Meter.CreateCounter<long>("frigaterelay.actions.failed");

    // frigaterelay.validators.passed  tags: subscription, action, validator
    public static readonly Counter<long> ValidatorsPassed =
        Meter.CreateCounter<long>("frigaterelay.validators.passed");

    // frigaterelay.validators.rejected  tags: subscription, action, validator
    public static readonly Counter<long> ValidatorsRejected =
        Meter.CreateCounter<long>("frigaterelay.validators.rejected");

    // frigaterelay.errors.unhandled  tags: none (D9 — single tagless series)
    public static readonly Counter<long> ErrorsUnhandled =
        Meter.CreateCounter<long>("frigaterelay.errors.unhandled");

    // Pre-existing (already referenced in ChannelActionDispatcher — migrate to this class):
    // frigaterelay.dispatch.drops  tags: action
    public static readonly Counter<long> Drops =
        Meter.CreateCounter<long>("frigaterelay.dispatch.drops");

    // frigaterelay.dispatch.exhausted  tags: action
    public static readonly Counter<long> Exhausted =
        Meter.CreateCounter<long>("frigaterelay.dispatch.exhausted");
}
```

**Tag passing — hot path.** For counters called per-event in the dispatch loop, use
`TagList` (a stack-allocated struct) to avoid heap allocations:

```csharp
// Two tags (common case):
var tags = new TagList
{
    { "subscription", sub.Name },
    { "action", plugin.Name }
};
FrigateRelayDiagnostics.ActionsDispatched.Add(1, tags);

// Three tags (validator):
var tags = new TagList
{
    { "subscription", sub.Name },
    { "action", plugin.Name },
    { "validator", validator.Name }
};
FrigateRelayDiagnostics.ValidatorsPassed.Add(1, tags);
```

Alternatively, `counter.Add(1, new KeyValuePair<string, object?>("subscription", sub.Name), ...)` is
acceptable for up to 3 tags — the BCL overload avoids `TagList` construction for
small fixed counts.

**`errors.unhandled` increment sites** (D9):
- `EventPump.PumpAsync` outermost `catch (Exception ex)` block (line 121) — already exists, add `FrigateRelayDiagnostics.ErrorsUnhandled.Add(1)`.
- `ChannelActionDispatcher.ConsumeAsync` general `catch (Exception ex)` block (line 241) — already increments `Exhausted`; `ErrorsUnhandled` is conceptually different (unexpected escape) — architect should decide whether `ConsumeAsync`'s catch (which already handles retry-exhausted) also counts as `unhandled`. Per D9, it should NOT — that catch is the expected retry-exhausted path, which increments `ActionsFailed`. Reserve `ErrorsUnhandled` for truly unexpected escapes (i.e., exceptions that reach `PumpFaulted`).

---

## §8 — Test Wiring: `MeterListener` for Unit Tests

The `System.Diagnostics.Metrics.MeterListener` API is available in .NET 6+ with no
additional NuGet packages (part of the BCL). It is the correct tool for asserting
counter increments in unit tests without spinning up a full `MeterProvider`.

**Pattern:**

```csharp
// Arrange
var measurements = new List<(string name, long value, IReadOnlyDictionary<string, object?> tags)>();
using var listener = new MeterListener();
listener.InstrumentPublished = (instrument, l) =>
{
    if (instrument.Meter.Name == "FrigateRelay")
        l.EnableMeasurementEvents(instrument);
};
listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
{
    var tagDict = new Dictionary<string, object?>();
    foreach (var tag in tags)
        tagDict[tag.Key] = tag.Value;
    measurements.Add((instrument.Name, measurement, tagDict));
});
listener.Start();

// Act — run SUT that calls FrigateRelayDiagnostics.EventsReceived.Add(1, ...)

// Assert
var received = measurements.Single(m => m.name == "frigaterelay.events.received");
Assert.AreEqual(1L, received.value);
Assert.AreEqual("front_door", received.tags["camera"]);
```

**Thread-safety note:** `MeterListener` callbacks execute on the thread that calls
`counter.Add()`. In tests where the SUT runs on the test thread, this is synchronous
and safe. For tests that use `Task.Run` or background tasks, add a small
`Task.Delay` or a `SemaphoreSlim` before asserting, or use
`listener.RecordObservableInstruments()` to force a snapshot for observable gauges
(not needed for `Counter<T>` which fires synchronously on `Add`).

---

## §9 — Test Wiring: In-Memory Activity Exporter for Integration Tests

**Package:** `OpenTelemetry.Exporter.InMemory` v1.15.3 (testing-only, do not add to
production Host project — add to `IntegrationTests.csproj` only).

**Pattern for `IntegrationTests` setup** (alongside existing Testcontainers +
WireMock wiring):

```csharp
var exportedActivities = new List<Activity>();

// In test host setup (WebApplicationFactory or HostApplicationBuilder override):
services.AddOpenTelemetry()
    .WithTracing(b =>
    {
        b.AddSource("FrigateRelay")
         .AddInMemoryExporter(exportedActivities,
             o => o.ExportProcessorType = ExportProcessorType.Simple);
    });
```

`ExportProcessorType.Simple` is mandatory for integration tests — it exports each
span synchronously when `Activity.Stop()` is called, so assertions after `await
Task.Delay(...)` or after a WireMock stub completes will see the spans immediately.

**Span tree assertion pattern:**

```csharp
// After publishing one MQTT message and waiting for pipeline completion:
var root = exportedActivities.Single(a => a.DisplayName == "mqtt.receive");
var children = exportedActivities.Where(a => a.ParentSpanId == root.SpanId).ToList();
Assert.AreEqual(4, children.Count); // event.match, dispatch.enqueue, action.X.execute, validator.Y.check
```

**Sharp edge — `Activity.Current` flow:** OTel's `Activity.Current` flows via
`AsyncLocal<T>`. When `EventPump.PumpAsync` starts a span and then calls
`_dispatcher.EnqueueAsync`, `Activity.Current` is correctly the pump span. The
`DispatchItem.ParentContext` capture (`Activity.Current?.Context ?? default`) happens
on the producer thread before the item crosses the channel — this is correct.
On the consumer thread (a `Task.Run` task), `Activity.Current` is null, so
`StartActivity(..., parentContext: item.ParentContext)` correctly re-parents
the consumer span to the captured context. This is the entire value of D1.

---

## §10 — Existing `IEventSource` / `EventPump` Shape Summary

**Where MQTT messages are received:**
`FrigateMqttEventSource.OnMessageReceivedAsync` (MQTTnet callback, push-based) →
`TryPublishAsync(rawPayload)` which parses JSON and calls
`_channel.Writer.WriteAsync(context)`.

**Where `EventPump` receives events:**
`EventPump.PumpAsync` → `await foreach context in source.ReadEventsAsync(ct)` which
reads from `FrigateMqttEventSource`'s internal `Channel<EventContext>`.

**`mqtt.receive` span boundary decision:**
The architect should start `mqtt.receive` at `EventPump.PumpAsync`'s receive site
(inside `await foreach`), NOT inside `FrigateMqttEventSource`. Rationale:
1. `FrigateMqttEventSource` lives in `FrigateRelay.Sources.FrigateMqtt` — giving it
   a dependency on the host's `FrigateRelayDiagnostics` class would couple a plugin
   assembly to the host's instrumentation implementation.
2. The span starting at the `EventPump` receive site cleanly covers the
   semantically meaningful unit: "one event was received from a source, matched
   against subscriptions, and dispatched."
3. The channel hop between MQTT callback and `EventPump` reader is internal to
   `FrigateMqttEventSource` and is not a semantically interesting boundary for
   tracing purposes.

If the architect decides the span should cover JSON parsing too (starting in the
source plugin), `IEventSource` would need an `ActivityContext` output parameter or
similar — defer to Phase 12+ as an additive change, not a Phase 9 requirement.

---

## §11 — Configuration Shape

**Current `appsettings.json`** (`src/FrigateRelay.Host/appsettings.json`):

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  }
}
```

**Proposed additions for Phase 9:**

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    },
    "Seq": {
      "ServerUrl": ""
    }
  },
  "Otel": {
    "OtlpEndpoint": ""
  }
}
```

Notes:
- `Serilog:MinimumLevel` is the key read by `Serilog.Settings.Configuration` when
  `lc.ReadFrom.Configuration(ctx.Configuration)` is called. The `Logging:LogLevel`
  section controls M.E.L. before Serilog takes over — both can coexist, but Serilog's
  `MinimumLevel` takes precedence once `UseSerilog` is called.
- `Otel:OtlpEndpoint` is read in `HostBootstrap.ConfigureServices` per D2. The
  standard env var `OTEL_EXPORTER_OTLP_ENDPOINT` is also honored because
  `builder.Configuration` already chains `AddEnvironmentVariables()` (the Worker SDK
  does this by default).
- `Serilog:Seq:ServerUrl: ""` ships as empty default per D7; operators set it via
  `appsettings.Local.json` or env var override.
- Do NOT commit any real URL or IP in `appsettings.json` — the secret-scan CI job
  will fail on RFC-1918 addresses.

---

## Comparison Matrix — OTel Package Options

| Criteria | `OpenTelemetry.Exporter.OpenTelemetryProtocol` | Console Exporter | InMemory Exporter |
|----------|-----------------------------------------------|-----------------|-------------------|
| Purpose | Production OTLP export | Dev/debug fallback | Testing only |
| Stable | Yes (1.15.3) | Yes (1.15.3) | Yes (1.15.3) |
| Phase 9 use | Host project (conditional) | EXCLUDED (D2) | Test projects only |
| License | Apache-2.0 | Apache-2.0 | Apache-2.0 |

---

## Recommendation

No competing options to evaluate — the decisions are locked in CONTEXT-9.md. The
research confirms:

1. **Package versions** are stable and .NET 10 compatible.
2. **`DispatcherDiagnostics.cs` is missing** — must be created as Phase 9 task 0.
3. **`DispatchItem.Activity?` must change to `ActivityContext`** per D1 — the field
   type is currently wrong relative to the decision.
4. **ID-6 fix is one line** in `ChannelActionDispatcher.cs:238` — change
   `ActivityStatusCode.Error` to `ActivityStatusCode.Unset`.
5. **`Serilog.Extensions.Hosting`** (not `Serilog.AspNetCore`) is the correct
   package for a Worker SDK host.
6. **`OpenTelemetry.Instrumentation.Process` is still beta** — omit from Phase 9;
   `OpenTelemetry.Instrumentation.Runtime` is stable and safe to include.

---

## Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| `DispatcherDiagnostics` missing file causes compile failure | High (file absent) | High | Create file as first task in Phase 9 plan |
| `DispatchItem.Activity?` vs `ActivityContext` type mismatch | High | Med | Rename field + update write/read sites in same PR |
| `ExportProcessorType.Batch` in tests causes flaky span assertions | Med | Med | Always set `ExportProcessorType.Simple` in test TracerProvider setup |
| `MeterListener` race in async tests | Med | Low | Gate assertions on completion signal (channel drain) before asserting |
| Serilog + M.E.L. dual logging (both active simultaneously) | Low | Med | `UseSerilog()` replaces providers; do not call `ClearProviders()` separately |
| `OTEL_EXPORTER_OTLP_ENDPOINT` typo causes silent export failure | Low | Low | Document in operator README; the OTel SDK logs its own connection errors internally |
| `OpenTelemetry.Instrumentation.Runtime` adds unexpected metrics to OTLP stream | Low | Low | Metrics are prefixed `dotnet.*` — no naming conflict with `frigaterelay.*` counters |

---

## Implementation Considerations

- `FrigateRelayDiagnostics` (the new consolidated class) should be `internal static`
  in `FrigateRelay.Host` and `InternalsVisibleTo` exposed to `Host.Tests` and
  `IntegrationTests` — both already listed in `Host.csproj`.
- The `Meter` instance in `FrigateRelayDiagnostics` must be a module-level static
  (not disposed during the application lifetime). `Meter.Dispose()` stops all
  instruments — only call it in tests after assertion is complete.
- `HostBootstrap.ConfigureServices` is the single wiring point for OTel and Serilog
  registration. Integration tests that call `HostBootstrap.ConfigureServices` directly
  will get OTel wired with the real `MeterProvider`; test-local overrides must be
  applied AFTER `ConfigureServices` completes (standard `IServiceCollection`
  replacement semantics).
- When adding new `Action<ILogger,...>` delegates for Phase 9 spans, use the
  EventId range 500–599 to avoid collisions with existing delegates (max existing
  EventId observed: 101).
- **`errors.unhandled` increment site:** Add `FrigateRelayDiagnostics.ErrorsUnhandled.Add(1)`
  in `EventPump.PumpAsync`'s `catch (Exception ex)` block (line 121) only. Do NOT
  add it to `ChannelActionDispatcher.ConsumeAsync`'s general catch (that path
  increments `ActionsFailed` / `Exhausted`, which is the expected retry-exhausted
  signal, not an unhandled error per D9).

---

## Sources

1. [NuGet — OpenTelemetry.Extensions.Hosting 1.15.3](https://www.nuget.org/packages/OpenTelemetry.Extensions.Hosting) (2026-04-27)
2. [NuGet — OpenTelemetry.Exporter.OpenTelemetryProtocol 1.15.3](https://www.nuget.org/packages/OpenTelemetry.Exporter.OpenTelemetryProtocol) (2026-04-27)
3. [NuGet — OpenTelemetry.Exporter.InMemory 1.15.3](https://www.nuget.org/packages/OpenTelemetry.Exporter.InMemory) (2026-04-27)
4. [NuGet — OpenTelemetry.Instrumentation.Runtime 1.15.1](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.Runtime/) (2026-04-27)
5. [NuGet — OpenTelemetry.Instrumentation.Process 1.15.1-beta.1](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.Process) (2026-04-27)
6. [NuGet — Serilog.Extensions.Hosting 10.0.0](https://www.nuget.org/packages/Serilog.Extensions.Hosting) (2026-04-27)
7. [NuGet — Serilog.Settings.Configuration 10.0.0](https://www.nuget.org/packages/Serilog.Settings.Configuration) (2026-04-27)
8. [NuGet — Serilog.Sinks.Console 6.1.1](https://www.nuget.org/packages/Serilog.Sinks.Console) (2026-04-27)
9. [NuGet — Serilog.Sinks.File 7.0.0](https://www.nuget.org/packages/Serilog.Sinks.File) (2026-04-27)
10. [NuGet — Serilog.Sinks.Seq 9.0.0](https://www.nuget.org/packages/Serilog.Sinks.Seq/) (2026-04-27)
11. [Microsoft Learn — Collect metrics .NET / MeterListener API](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-collection) (updated 2026-03-30)
12. [OpenTelemetry .NET — InMemory Exporter README](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/src/OpenTelemetry.Exporter.InMemory/README.md)
13. [serilog-sinks-seq LICENSE — Apache-2.0](https://github.com/datalust/serilog-sinks-seq/blob/dev/LICENSE)

---

## Uncertainty Flags

- **Decision Required:** The architect must decide whether `mqtt.receive` starts inside
  `FrigateMqttEventSource.TryPublishAsync` (covers JSON parse; requires giving the
  source plugin an `ActivitySource` reference or a callback) or inside
  `EventPump.PumpAsync` (simpler, recommended above). This choice affects whether
  `FrigateRelay.Sources.FrigateMqtt.csproj` needs a new dependency.

- **Verify:** `DispatcherDiagnostics` is referenced in `ChannelActionDispatcher.cs`
  but the file does not exist on disk. Confirm the file was never created (not just
  in a different path) by searching the full `src/` tree for `DispatcherDiagnostics`
  at plan time: `grep -rn 'DispatcherDiagnostics' src/`.

- **Verify at plan time:** OTel package versions via Context7 or Microsoft Learn MCP
  to confirm 1.15.3 is still the latest stable before pinning in `.csproj` files.

- **Verify at plan time:** Whether the existing `Serilog` and `OpenTelemetry`
  NuGet packages version-align with .NET 10's `Microsoft.Extensions.*` v10.0.7
  packages already in `Host.csproj`. The OTel packages target `netstandard2.0` /
  `net8.0` and have no hard M.E.* version ceiling — should be fine, but confirm
  with `dotnet list package --include-transitive` after adding refs.

- **`ExportProcessorType` enum location:** In OTel .NET 1.x it lives in
  `OpenTelemetry` namespace (the core package, transitively pulled by
  `OpenTelemetry.Exporter.InMemory`). Confirm the exact using directive at plan time
  if there are any ambiguity compiler errors.
