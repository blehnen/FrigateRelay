# Phase 4 — Research Findings

**Phase:** 4 — Action Dispatcher + BlueIris (First Vertical Slice)
**Author:** Orchestrator (researcher subagent truncated at ~35 tool uses on two attempts; orchestrator completed inline using Microsoft Learn MCP + Context7 MCP — same fallback pattern that finished Phases 1, 2, and 3 builders).
**Inputs honored:** `CONTEXT-4.md` (7 user-locked decisions), `CLAUDE.md` invariants, ROADMAP Phase 4 deliverables, `PROJECT.md` decisions S2/V3.

---

## TL;DR — top three findings the architect MUST know

1. **`Channel.CreateBounded<T>` has a built-in `itemDropped` callback** (`Action<T>?`). CONTEXT-4.md was conservative — we do **not** need to wrap `TryWrite` ourselves to detect drop-oldest evictions. Use the overload `Channel.CreateBounded<DispatchItem>(BoundedChannelOptions, Action<DispatchItem>? itemDropped)` and emit the `frigaterelay.dispatch.drops` counter + Warning log directly from the callback. Cleaner than the wrapper pattern.

2. **Polly v8 `DelayGenerator.AttemptNumber` is zero-indexed for the FIRST RETRY.** Verified against Polly's official docs (`/app-vnext/polly`): `AttemptNumber == 0` is the delay before the first retry, `1` before the second, `2` before the third. So CONTEXT-4.md's formula `TimeSpan.FromSeconds(3 * (AttemptNumber + 1))` is **correct** — produces 3s / 6s / 9s exactly as the legacy reference behavior demands. **Do not** use `AttemptNumber` directly without the `+1` offset; that would yield 0/3/6.

3. **`HttpRetryStrategyOptions.MaxRetryAttempts` counts retries, not total attempts.** Default is 3. So `MaxRetryAttempts = 3` means **1 initial attempt + 3 retries = 4 total HTTP calls** before the pipeline surfaces the final exception. Test assertions for "dropped after 3 retries" should observe four `OnRetry`-callable points (initial fail, retries 0/1/2 firing, then exhaustion).

---

## 1. `Microsoft.Extensions.Http.Resilience` v8 — exact wiring for fixed 3/6/9s delays

### Package

| Package | Version | Note |
| --- | --- | --- |
| `Microsoft.Extensions.Http.Resilience` | **10.4.0** (stable, .NET 10 target) | Source: `dotnet/extensions` repo. Provides `HttpRetryStrategyOptions`, `AddResilienceHandler`, `AddStandardResilienceHandler`. |

Pulls in `Polly.Core` and `Polly.RateLimiting` transitively. Do **not** add `Polly` (the v7 façade) — Polly v8 is the API the resilience package is built against.

### Type surface (verified at `learn.microsoft.com/dotnet/api/microsoft.extensions.http.resilience.httpretrystrategyoptions?view=net-10.0-pp`)

```csharp
public class HttpRetryStrategyOptions : Polly.Retry.RetryStrategyOptions<HttpResponseMessage>
{
    public bool ShouldRetryAfterHeader { get; set; } // default: true
    // Inherited from RetryStrategyOptions<HttpResponseMessage>:
    //   MaxRetryAttempts (default 3), Delay (default 2s), BackoffType (default Exponential),
    //   UseJitter (default true), DelayGenerator, OnRetry, ShouldHandle, MaxDelay
}
```

**Default `ShouldHandle` predicate** (from MS Learn doc page on `HttpRetryStrategyOptions` constructor remarks):
> "By default, the options are configured to handle only transient failures. Specifically, this includes HTTP status codes 408, 429, 500 and above, as well as `HttpRequestException` and `TimeoutRejectedException` exceptions."

We do not need to override `ShouldHandle` for Phase 4 — the default exactly matches the BlueIris failure surface (network errors + 5xx).

### Working code sample (BlueIris registrar)

```csharp
// In FrigateRelay.Plugins.BlueIris/PluginRegistrar.cs
services.AddOptions<BlueIrisOptions>()
    .Bind(configuration.GetSection("BlueIris"))
    .ValidateDataAnnotations()
    .Validate(o => !string.IsNullOrWhiteSpace(o.TriggerUrlTemplate),
              "BlueIris.TriggerUrlTemplate is required.")
    .ValidateOnStart();

services.AddHttpClient("BlueIris", (sp, client) =>
    {
        var opts = sp.GetRequiredService<IOptions<BlueIrisOptions>>().Value;
        client.Timeout = opts.RequestTimeout;        // e.g. 10s default
    })
    .AddResilienceHandler("BlueIris-retry", builder =>
    {
        builder.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            // BackoffType is irrelevant when DelayGenerator is set — DelayGenerator wins.
            DelayGenerator = static args =>
                ValueTask.FromResult<TimeSpan?>(
                    TimeSpan.FromSeconds(3 * (args.AttemptNumber + 1))),
            // ShouldHandle: keep default (HTTP 408/429/5xx + HttpRequestException + TimeoutRejectedException).
        });
    })
    .ConfigurePrimaryHttpMessageHandler(sp =>
    {
        var opts = sp.GetRequiredService<IOptions<BlueIrisOptions>>().Value;
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        };
        if (opts.AllowInvalidCertificates)
        {
            handler.SslOptions.RemoteCertificateValidationCallback =
                static (_, _, _, _) => true;
        }
        return handler;
    });
```

**Citations:**
- Configure custom resilience handlers: `learn.microsoft.com/dotnet/core/resilience/http-resilience#add-custom-resilience-handlers`
- `HttpRetryStrategyOptions` defaults: `learn.microsoft.com/dotnet/api/microsoft.extensions.http.resilience.httpretrystrategyoptions.-ctor?view=net-10.0-pp`
- DelayGenerator + AttemptNumber zero-based pattern: Polly v8 official docs (Context7 `/app-vnext/polly`, file `docs/strategies/retry.md`)

### Subtle gotcha: `BackoffType` vs `DelayGenerator` precedence

Polly docs (Hedging defaults page, applies to retry too):
> "The optional `DelayGenerator` delegate enables dynamic calculation of the delay based on runtime information like the attempt number. **If both `Delay` and `DelayGenerator` are specified, `Delay` is ignored.**"

So we set `DelayGenerator` and leave `BackoffType`/`Delay`/`UseJitter`/`MaxDelay` at defaults — they're not consulted. Architect should NOT also set `Delay = TimeSpan.FromSeconds(3)` thinking it makes the intent clearer; it's just dead config.

### `ShouldRetryAfterHeader = true` (the default) — do we override?

Default `true` means if BlueIris ever returns a `Retry-After` header, the retry strategy honors it instead of our 3/6/9s schedule. BlueIris does NOT return `Retry-After` on its trigger endpoint — not a real risk. Leave the default alone for v1; if it bites in production we can flip it later.

### `OnRetry` for telemetry — recommended addition

The architect should add `OnRetry` to emit a structured log on each retry so operators can see the schedule fire:

```csharp
OnRetry = static args =>
{
    args.Context.Properties.Set(...);   // optional
    return default;   // ValueTask.CompletedTask
}
```

Phase 4 may keep this simple (LogDebug) — Phase 9 will add OTel tracing via the existing `ActivitySource "FrigateRelay"`.

---

## 2. `IHttpClientFactory` + per-plugin TLS opt-in

### `ConfigurePrimaryHttpMessageHandler` overload — verified

The overload accepting `Func<IServiceProvider, HttpMessageHandler>` is the right one for our use (we need `IOptions<BlueIrisOptions>` from DI to read `AllowInvalidCertificates`):

```csharp
public static IHttpClientBuilder ConfigurePrimaryHttpMessageHandler(
    this IHttpClientBuilder builder,
    Func<IServiceProvider, HttpMessageHandler> configureHandler);
```

### `SocketsHttpHandler.SslOptions.RemoteCertificateValidationCallback` is the .NET 10 API

Confirmed via the migration table at `learn.microsoft.com/dotnet/fundamentals/networking/http/httpclient-migrate-from-httpwebrequest`:

| Old (`HttpWebRequest`) | New (`HttpClient` + `SocketsHttpHandler`) |
| --- | --- |
| `ServerCertificateValidationCallback` | `SslOptions.RemoteCertificateValidationCallback` |

**Why not `HttpClientHandler.ServerCertificateCustomValidationCallback`?** Because in .NET 9+ the default primary handler in `IHttpClientFactory` is `SocketsHttpHandler`, not `HttpClientHandler`. Casting to `HttpClientHandler` throws `InvalidCastException` (`learn.microsoft.com/dotnet/core/compatibility/networking/9.0/default-handler`). We must use `SocketsHttpHandler` directly.

### Named-client scoping — confirmed isolation

Each `services.AddHttpClient("name")` registration owns its own `IHttpMessageHandlerFactory` slot. The handler tree built for `"BlueIris"` has **no effect** on any other named or typed client. CLAUDE.md's "TLS skipping is opt-in per-plugin only" invariant is satisfied by this scoping; the architect just needs to ensure each plugin uses a distinct name.

### `PooledConnectionLifetime` — set to 2 min (already standard)

When an `IHttpClientFactory` consumer specifies `SocketsHttpHandler` as the primary handler, the factory's default `HandlerLifetime` (also 2 min) and `SocketsHttpHandler.PooledConnectionLifetime` should align. `learn.microsoft.com/dotnet/core/extensions/httpclient-factory#using-ihttpclientfactory-together-with-socketshttphandler` confirms the pattern. Phase 4 sets it explicitly for clarity.

### **Do not** set `LocalCertificateSelectionCallback` or `ClientCertificates`

CLAUDE.md does not require client-cert auth and BlueIris doesn't use it. Avoid wiring properties that can become attack surface (an empty cert collection is safer than a null callback).

---

## 3. `System.Threading.Channels` — DropOldest with built-in callback

### **Updated finding (corrects CONTEXT-4.md):** there IS a built-in dropped-item callback

```csharp
public static Channel<T> CreateBounded<T>(
    BoundedChannelOptions options,
    Action<T>? itemDropped);   // <-- callback fires when DropOldest/DropNewest evicts
```

Source: `learn.microsoft.com/dotnet/api/system.threading.channels.channel.createbounded?view=net-10.0`
Sample from MS Learn doc:
```csharp
var channel = Channel.CreateBounded<Coordinates>(
    new BoundedChannelOptions(10)
    {
        AllowSynchronousContinuations = true,
        FullMode = BoundedChannelFullMode.DropOldest
    },
    static void (Coordinates dropped) =>
        Console.WriteLine($"Coordinates dropped: {dropped}"));
```

**Architect should use this directly** — no `TryWrite` wrapper, no `Reader.Count` polling, no race-condition concerns. The callback runs on the writing thread when `TryWrite`/`WriteAsync` evicts an item.

### Recommended dispatcher channel construction

```csharp
// Per-plugin, inside ChannelActionDispatcher.StartAsync:
var capacity = _opts.QueueCapacityFor(plugin) ?? 256;
var channel = Channel.CreateBounded<DispatchItem>(
    new BoundedChannelOptions(capacity)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleWriter = true,    // EventPump is the sole producer
        SingleReader = false,   // 2 consumer tasks
        AllowSynchronousContinuations = false,
    },
    itemDropped: static evicted =>
    {
        // Architect: thread the meter + logger via the closure on this lambda.
        // Captures keep this allocation off the hot path; the lambda runs only on overflow.
    });
```

Architect note: the `itemDropped` callback closes over `Meter` + `ILogger` references; since channels are constructed once per plugin at startup, the per-overflow closure cost is negligible (one allocation per channel, not per drop).

### Consumer pattern

```csharp
private async Task ConsumeAsync(IActionPlugin plugin, ChannelReader<DispatchItem> reader, CancellationToken ct)
{
    await foreach (var item in reader.ReadAllAsync(ct).ConfigureAwait(false))
    {
        try
        {
            await plugin.ExecuteAsync(item.Context, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Resilience pipeline already retried 3x; this is post-exhaustion.
            // Emit `frigaterelay.dispatch.exhausted` counter + LogWarning here.
            // Activity ends with status=error.
        }
    }
}
```

`ReadAllAsync` correctly handles `writer.Complete()` — the `await foreach` exits cleanly when the channel is completed and drained, allowing graceful shutdown.

### Graceful shutdown sequence (`IHostedService`)

```csharp
public Task StopAsync(CancellationToken ct)
{
    foreach (var (plugin, channel) in _channels)
    {
        channel.Writer.Complete();   // signals "no more writes"; consumers drain remaining items
    }
    return Task.WhenAll(_consumerTasks).WaitAsync(ct);
}
```

The `await foreach` in each consumer task exits when the channel is empty AND the writer is completed. If `ct` fires during draining, `WaitAsync` propagates the cancellation, and the consumer's `OperationCanceledException` flows out of `ExecuteAsync` mid-call. Phase-3 `FrigateMqttEventSource.DisposeAsync` precedent: wrap with `try { ... } catch (OperationCanceledException) when (ct.IsCancellationRequested) { /* graceful */ }` if needed.

---

## 4. Testcontainers.NET Mosquitto

### Package

| Package | Version | Note |
| --- | --- | --- |
| `Testcontainers` | latest stable (verify on NuGet at plan time — current major is 4.x) | The umbrella package. Brings in `DotNet.Testcontainers.Builders` + `DotNet.Testcontainers.Containers`. |

There is **no dedicated `Testcontainers.Mosquitto` module**. Use the generic `ContainerBuilder` pattern (Context7 `/testcontainers/testcontainers-dotnet`).

### Working code sample

```csharp
// tests/FrigateRelay.IntegrationTests/Fixtures/MosquittoFixture.cs
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

internal sealed class MosquittoFixture : IAsyncDisposable
{
    private readonly IContainer _container = new ContainerBuilder()
        .WithImage("eclipse-mosquitto:2")
        .WithPortBinding(1883, true)        // random host port
        .WithBindMount(...)                  // OPTIONAL: see config note below
        .WithEnvironment("MOSQUITTO_USER", "")
        .WithWaitStrategy(Wait.ForUnixContainer()
            .UntilInternalTcpPortIsAvailable(1883))
        .Build();

    public string Hostname => _container.Hostname;
    public int    Port     => _container.GetMappedPublicPort(1883);

    public async ValueTask InitializeAsync() => await _container.StartAsync().ConfigureAwait(false);
    public async ValueTask DisposeAsync()   => await _container.DisposeAsync().ConfigureAwait(false);
}
```

### Mosquitto config — anonymous auth

The default `eclipse-mosquitto:2` image **disallows anonymous connections**. Two options:

**Option A — `WithResourceMapping` (preferred, no host filesystem dependency):**
```csharp
.WithResourceMapping(
    Encoding.UTF8.GetBytes("listener 1883\nallow_anonymous true\n"),
    "/mosquitto/config/mosquitto.conf")
```

**Option B — env-var conf is NOT supported by Mosquitto.** The image reads `/mosquitto/config/mosquitto.conf` literally; there's no `MOSQUITTO_*` env-var passthrough. Don't attempt it.

### Wait strategy

`Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(1883)` is the right choice — Mosquitto opens 1883 only after fully booting. Log-message wait would also work (`UntilMessageIsLogged("mosquitto version 2")`), but TCP-port availability is more robust across image versions.

### Startup time budget

Mosquitto cold start on a typical CI runner: **~2-4 seconds** (image is 9 MB, Alpine-based, no JVM, no DB). The 30-second integration-test SLO in the ROADMAP is comfortable — Mosquitto + WireMock startup + MQTT connect + publish + WireMock assertion fits well within 10s on Linux runners.

### Citation

`learn.microsoft.com` doesn't have first-party Testcontainers docs; Context7's `/testcontainers/testcontainers-dotnet` is the canonical source (211 code snippets, benchmark 79.3, source reputation High).

---

## 5. WireMock.Net for the Blue Iris stub

### Package

| Package | Version | Note |
| --- | --- | --- |
| `WireMock.Net` | latest stable on NuGet | Source: `wiremock/wiremock.net`. Used by 4000+ packages on NuGet. |

### Working code sample — stub + verify exactly-one-call

```csharp
// In MqttToBlueIrisSliceTests
using var server = WireMockServer.Start();   // random port; server.Urls[0] is the base URL

server
    .Given(Request.Create()
        .UsingGet()
        .WithPath("/admin")
        .WithParam("camera", "front")
        .WithParam("trigger", "1"))
    .RespondWith(Response.Create().WithStatusCode(200));

// Configure the host under test to point at server.Urls[0]:
//   "BlueIris": { "TriggerUrlTemplate": "{server.Urls[0]}/admin?camera={camera}&trigger=1" }

// ... publish Frigate event to Mosquitto, await dispatch ...

var matchingRequests = server.FindLogEntries(
    Request.Create().UsingGet().WithPath("/admin").WithParam("camera", "front"));
Assert.AreEqual(1, matchingRequests.Count(),
    "Expected exactly one BlueIris trigger to fire for the published Frigate event.");
```

### Wiring `server.Urls[0]` into the host config

The integration test must override the running host's `BlueIrisOptions.TriggerUrlTemplate` with `server.Urls[0]/...`. Do this with `Host.CreateApplicationBuilder` + in-memory configuration:

```csharp
var builder = Host.CreateApplicationBuilder([]);
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["BlueIris:TriggerUrlTemplate"] = $"{server.Urls[0]}/admin?camera={{camera}}&trigger=1",
    ["BlueIris:AllowInvalidCertificates"] = "false",
    ["Subscriptions:0:Name"]     = "FrontCam",
    ["Subscriptions:0:Camera"]   = "front",
    ["Subscriptions:0:Label"]    = "person",
    ["Subscriptions:0:Actions:0"] = "BlueIris",
    ["Mqtt:Host"]                = mosquitto.Hostname,
    ["Mqtt:Port"]                = mosquitto.Port.ToString(),
});
// Build host, run it briefly, publish an MQTT event, await dispatch + WireMock assertion.
```

### **WARNING — anti-pattern in the Context7 sample**

The Context7 sample shows `_httpClient.GetAsync(...).Wait()`. **Do NOT replicate** — CLAUDE.md invariant: `git grep -nE '\.(Result|Wait)\(' src/` AND `tests/` must stay empty. Use `await` everywhere, including in test setup.

---

## 6. Frigate MQTT event payload shape (verified from `EventContextProjector.cs`)

### Path that fires BlueIris

A Frigate event projects through `EventContextProjector.TryProject` and produces a non-suppressed `EventContext` when:
- `type` is `"new"` (the D5 stationary/false-positive guard does NOT apply to `"new"` — see comment in projector lines 1-11).
- OR `type` is `"update"` / `"end"` AND `after.stationary == false` AND `after.false_positive == false`.

For the integration test, **publish a `"new"` event** — simplest path, no D5 guard concerns.

### Minimal test payload (matches the JSON shape `EventContextProjector` deserializes)

```json
{
  "type": "new",
  "before": null,
  "after": {
    "id": "ev-test-001",
    "camera": "front",
    "label": "person",
    "stationary": false,
    "false_positive": false,
    "score": 0.91,
    "start_time": 1745558400.0,
    "current_zones": ["driveway"],
    "entered_zones": ["driveway"]
  }
}
```

### MQTT topic

The Frigate convention is `frigate/events`. Phase 3's `FrigateMqttEventSource` subscribes to this topic by default. Architect should confirm the exact `MqttOptions.EventTopic` default (likely `"frigate/events"`) by reading `src/FrigateRelay.Sources.FrigateMqtt/MqttOptions.cs` at plan time.

### Subscription that matches

```jsonc
{
  "Subscriptions": [
    {
      "Name": "FrontCam",
      "Camera": "front",
      "Label": "person",
      "Actions": ["BlueIris"]   // <-- the new D2 field
    }
  ]
}
```

`SubscriptionMatcher.Match` (from Phase 3) matches case-insensitive. `Zone` is null/missing → matches any event regardless of zone (per `SubscriptionOptions` remarks).

---

## 7. URL template parser — concrete recommendation

### Class shape

```csharp
// src/FrigateRelay.Plugins.BlueIris/BlueIrisUrlTemplate.cs
internal sealed partial class BlueIrisUrlTemplate
{
    [GeneratedRegex(@"\{(?<name>[a-z_]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    private static readonly FrozenSet<string> AllowedTokens =
        new[] { "camera", "label", "event_id", "score", "zone" }.ToFrozenSet();

    private readonly string _template;

    /// <summary>
    /// Parses and validates the template at startup. Throws <see cref="OptionsValidationException"/>
    /// (via .Validate(...).ValidateOnStart()) when an unknown placeholder is present.
    /// </summary>
    public static BlueIrisUrlTemplate Parse(string template)
    {
        foreach (Match m in TokenRegex().Matches(template))
        {
            var name = m.Groups["name"].Value;
            if (!AllowedTokens.Contains(name))
                throw new ArgumentException(
                    $"BlueIris.TriggerUrlTemplate contains unknown placeholder '{{{name}}}'. " +
                    $"Allowed: {{camera}}, {{label}}, {{event_id}}, {{score}}, {{zone}}.",
                    nameof(template));
        }
        return new BlueIrisUrlTemplate(template);
    }

    public string Resolve(EventContext ctx)
    {
        return TokenRegex().Replace(_template, m => m.Groups["name"].Value switch
        {
            "camera"   => Uri.EscapeDataString(ctx.Camera),
            "label"    => Uri.EscapeDataString(ctx.Label),
            "event_id" => Uri.EscapeDataString(ctx.EventId),
            "score"    => ctx.Score.ToString("F2", CultureInfo.InvariantCulture),
            "zone"     => Uri.EscapeDataString(ctx.Zones.Count > 0 ? ctx.Zones[0] : ""),
            _          => m.Value,   // unreachable — Parse() guards
        });
    }
}
```

### Score format

`{score}` resolves via `F2` invariant culture (e.g., `0.91`). NOT `G` (which can produce `0.91000000000000003`). NOT `R` (round-trippable, but ugly). `F2` matches the legacy reference formatting and is human-readable.

### **Note:** `EventContext.Score` may not exist yet

Read `EventContext.cs` at plan time. If `Score` is absent, the architect must either:
(a) extend `EventContext` to carry score (touches `Abstractions` — cross-phase), or
(b) drop `{score}` from the allowlist and document the deferral.

Recommendation: option (b) for v1. `Score` was not in CONTEXT-4.md's gray-areas — re-checking PROJECT.md, score is mentioned as a future field but not marked as Phase 4 scope. Architect to make the call.

### `{{` literal-brace escaping

Defer for v1. None of the BlueIris trigger URL patterns we expect to handle contain literal braces. If a user ever needs `{{`, we can add it without breaking changes (regex stays the same, `{{` simply doesn't match the token pattern, and we'd add an explicit unescape step).

### Where it lives

`FrigateRelay.Plugins.BlueIris/BlueIrisUrlTemplate.cs`, `internal sealed`. **Not** in `Abstractions` — keeps `Abstractions` free of templating logic per CLAUDE.md "no third-party runtime deps" rule (and Regex is technically built-in but the templating concept is plugin-specific).

Phase 5 `BlueIrisSnapshot` lives in the **same plugin assembly** (`FrigateRelay.Plugins.BlueIris/` per ROADMAP), so it can reuse this internal helper without a public-API change.

---

## 8. CI integration — **zero edits required**

### Reading `.github/scripts/run-tests.sh`

Line 41: `find tests -maxdepth 2 -name '*.Tests.csproj' -type f`. Any new project at `tests/<Name>.Tests/<Name>.Tests.csproj` is auto-discovered. **`tests/FrigateRelay.IntegrationTests/FrigateRelay.IntegrationTests.csproj` qualifies** (suffix `.Tests` matches the glob).

### Reading `Jenkinsfile`

Line 47: `sh 'bash .github/scripts/run-tests.sh --coverage'`. Already delegates to the shared script — also auto-picks up the new project.

### Conclusion

Phase 4 needs **no edits** to `.github/scripts/run-tests.sh`, `.github/workflows/ci.yml`, or `Jenkinsfile`. Adding the integration test project is fully transparent to CI.

This invalidates one of CONTEXT-4.md's "cross-cutting confirmations" notes — that note was conservative based on the ROADMAP language, but the actual scripts already handle it.

### Caveat — Windows runner

`.github/workflows/ci.yml` uses matrix `[ubuntu-latest, windows-latest]`. **Testcontainers requires Linux containers**, which `windows-latest` runners (Windows Server) cannot run by default. Two options:

**Option A — skip integration tests on Windows.** Wrap the integration suite step with `if: runner.os == 'Linux'` in `ci.yml`. Simple, conventional.

**Option B — use `[Trait("Category", "Integration")]` + filter.** Requires test attribute plumbing per MTP/MSTest. More work for the same outcome.

**Recommendation: Option A.** Architect adds a single `if: runner.os == 'Linux'` to a NEW workflow step that runs `bash .github/scripts/run-tests.sh` filtered to just the integration project — OR splits `run-tests.sh` into "always-run" + "integration-only" passes. Reading the script: it currently runs ALL test projects in one invocation. **Recommendation:** Architect adds a `--integration-only` / `--skip-integration` flag to `run-tests.sh` (one-line bash filter), then GH workflow's Windows leg passes `--skip-integration`. Minimal blast radius.

### Caveat — Jenkins agent Docker-in-Docker

The Jenkinsfile runs inside `mcr.microsoft.com/dotnet/sdk:10.0` on a Docker agent. Testcontainers needs **either** the host Docker socket mounted (`-v /var/run/docker.sock:/var/run/docker.sock`) OR a sibling/DinD setup. **Flag this as a precondition** — the user's Jenkins host must be configured for Docker socket access. If not, the Jenkins coverage run for the integration suite will fail at container start with "Cannot connect to the Docker daemon".

**Recommendation:** architect adds a doc note in `CLAUDE.md` (or just SUMMARY-4.x) about the Jenkins requirement. **Do not** silently skip the integration suite in Jenkins — that defeats the coverage gate.

---

## 9. Risks & Mitigations (top 3 for the builder)

### Risk 1: `AttemptNumber` off-by-one produces 6/9/12s instead of 3/6/9s

**Mitigation:** Test it. Add a unit test in `tests/FrigateRelay.Host.Tests/Dispatch/` that:
1. Stubs an `IActionPlugin` that always throws.
2. Captures the `OnRetry` callback's `args.RetryDelay` for each attempt.
3. Asserts the sequence is **exactly** `[3s, 6s, 9s]`.

Without this test, an `args.AttemptNumber` typo silently changes the schedule with no visible failure.

### Risk 2: Windows CI matrix leg fails because Testcontainers can't start

**Mitigation:** As noted above, gate the integration test step on `runner.os == 'Linux'` OR teach `run-tests.sh` a `--skip-integration` flag. Builder MUST verify the GH PR run goes green on BOTH ubuntu and windows after the Phase 4 change — Phase 2's reviewer caught this kind of cross-platform bug.

### Risk 3: `IActionDispatcher.EnqueueAsync` ordering — channel writes before consumers start

**Mitigation:** `IHostedService.StartAsync` is the only safe time to construct channels and start consumer tasks. `EventPump` (also a `BackgroundService`) and `ChannelActionDispatcher` (the new `IHostedService`) BOTH start during host startup, but `BackgroundService.ExecuteAsync` runs AFTER all `IHostedService.StartAsync` complete. This is correct — but if the architect mistakenly puts channel construction in `ExecuteAsync` of a hypothetical new `BackgroundService`-based dispatcher, EventPump could race ahead.

**Builder action:** make `ChannelActionDispatcher` implement `IHostedService` directly (NOT `BackgroundService`), construct channels + start consumer tasks inside `StartAsync`, await `WhenAll(consumers)` inside `StopAsync` after `Writer.Complete()`. This is the documented pattern in MS Learn `dotnet/core/extensions/channels` and matches the ROADMAP wording ("`ChannelActionDispatcher : IActionDispatcher, IHostedService`").

---

## 10. Open questions for the architect (capped at 2)

### Q1: Does `EventContext` carry `Score`?

If `EventContext.cs` doesn't have a `Score` property, architect chooses:
- **(a)** Drop `{score}` from D3's allowlist for v1 (preferred — defers cross-phase Abstractions edit).
- **(b)** Extend `EventContext` to add `Score` (touches Abstractions; check that `EventContextProjector` populates it from `after.score`).

### Q2: How does the architect want the `frigaterelay.dispatch.exhausted` counter (retry-exhaustion) wired?

D6 mandated the `frigaterelay.dispatch.drops` counter for queue-overflow drops. The retry-exhaustion case (Polly fired all 3 retries and BlueIris still failed) is a separate failure mode that ALSO needs a counter per the ROADMAP wording ("retry-exhaust-with-warning"). Suggested: `frigaterelay.dispatch.exhausted{action}`. Architect to confirm name + emit point (most natural is the `catch` block in the consumer task wrapping `plugin.ExecuteAsync`).

---

## Cross-cutting confirmations (re-stated for the architect)

- **CLAUDE.md invariants exercised:** no `.Result`/`.Wait()` (also banned in `tests/`); `frigaterelay.*` metric prefix; no `ServicePointManager`; no hard-coded IPs (use Testcontainers' randomized hostnames + WireMock's `server.Urls[0]`); plugin-private `internal` types where possible (BlueIrisUrlTemplate, MosquittoFixture).
- **Test-name underscore convention** (`Method_Condition_Expected`) — `.editorconfig` already silences `CA1707` for `tests/**.cs`. Don't re-enable it on the new project.
- **`<InternalsVisibleTo>` for test access** — the convention. If `BlueIrisActionPlugin` needs internals visible to `FrigateRelay.Plugins.BlueIris.Tests` (a NEW test project for Phase 4 dispatcher unit tests, mirroring how Phase 3 added `FrigateRelay.Sources.FrigateMqtt.Tests`), use the MSBuild item form.
- **MSTest v3 + MTP** — projects are `OutputType=Exe`, run via `dotnet run --project`, NOT `dotnet test`. Already documented in CLAUDE.md.
- **FluentAssertions 6.12.2** — pinned. Do not upgrade.
- **Phase-3 latent-bug pattern** — Phase 1's "`RunAll`-after-`Build()`" simplification became latent. Phase 4 introduces `ChannelActionDispatcher` as a new `IHostedService` — verify the registrar adds it BEFORE `builder.Build()`, just like every other `services.AddX(...)` call.

---

## Summary

`/mnt/f/git/frigaterelay/.shipyard/phases/4/RESEARCH.md` written. The most consequential finding is that `Channel.CreateBounded<T>` already has a built-in `itemDropped` callback, so the dispatcher's drop-telemetry path is one line of closure capture instead of a `TryWrite` wrapper. Polly v8's `AttemptNumber` is zero-based for the first retry, confirming CONTEXT-4.md's `3 * (AttemptNumber + 1)` formula produces exactly 3/6/9-second delays. CI scripts auto-discover the new integration test project with **zero edits** — but the Windows matrix leg needs an `if: runner.os == 'Linux'` skip because Testcontainers cannot run Linux containers on Windows runners.
