---
phase: phase-4-action-dispatcher-blueiris
plan: 2.2
wave: 2
dependencies: [1.2]
must_haves:
  - BlueIrisActionPlugin implements IActionPlugin (Name="BlueIris", uses named HttpClient via IHttpClientFactory)
  - BlueIris.PluginRegistrar wires named HttpClient + AddResilienceHandler (3/6/9s) + ConfigurePrimaryHttpMessageHandler (TLS opt-in via SocketsHttpHandler)
  - Options validation (TriggerUrlTemplate parsed via BlueIrisUrlTemplate.Parse, ValidateOnStart)
  - BlueIrisActionPlugin unit tests via WireMock.Net (success path, retry-then-success, retry-exhaustion, TLS opt-in)
files_touched:
  - src/FrigateRelay.Plugins.BlueIris/FrigateRelay.Plugins.BlueIris.csproj
  - src/FrigateRelay.Plugins.BlueIris/BlueIrisActionPlugin.cs
  - src/FrigateRelay.Plugins.BlueIris/PluginRegistrar.cs
  - tests/FrigateRelay.Plugins.BlueIris.Tests/FrigateRelay.Plugins.BlueIris.Tests.csproj
  - tests/FrigateRelay.Plugins.BlueIris.Tests/BlueIrisActionPluginTests.cs
tdd: true
risk: medium
---

# Plan 2.2: BlueIrisActionPlugin + registrar (HttpClient + Polly + TLS opt-in)

## Context

Implements the `BlueIrisActionPlugin` and its `PluginRegistrar`, wiring the named `HttpClient` with the Polly v8 `AddResilienceHandler` (3/6/9s — CONTEXT-4 D7 + RESEARCH §1) and `ConfigurePrimaryHttpMessageHandler` (per-plugin TLS opt-in via `SocketsHttpHandler.SslOptions.RemoteCertificateValidationCallback` — RESEARCH §2). The retry pipeline lives at the HttpClient layer, NOT inside the dispatcher (decision documented in PLAN-2.1).

This plan owns the rest of ROADMAP deliverable 4 and the BlueIris-specific tests of deliverable 9. Depends on PLAN-1.2's `BlueIrisOptions` and `BlueIrisUrlTemplate` types.

## Dependencies

- PLAN-1.2 (provides `BlueIrisOptions`, `BlueIrisUrlTemplate`, plugin csproj scaffold, test csproj scaffold).

## Files touched

- `src/FrigateRelay.Plugins.BlueIris/FrigateRelay.Plugins.BlueIris.csproj` (modify — add package refs for `Microsoft.Extensions.Http.Resilience`, `Microsoft.Extensions.Http`, `Microsoft.Extensions.Options.ConfigurationExtensions`, `Microsoft.Extensions.Hosting.Abstractions`)
- `src/FrigateRelay.Plugins.BlueIris/BlueIrisActionPlugin.cs` (create)
- `src/FrigateRelay.Plugins.BlueIris/PluginRegistrar.cs` (create)
- `tests/FrigateRelay.Plugins.BlueIris.Tests/FrigateRelay.Plugins.BlueIris.Tests.csproj` (modify — add `WireMock.Net` package ref)
- `tests/FrigateRelay.Plugins.BlueIris.Tests/BlueIrisActionPluginTests.cs` (create)

## Tasks

### Task 1: Implement BlueIrisActionPlugin
**Files:** `src/FrigateRelay.Plugins.BlueIris/BlueIrisActionPlugin.cs`, `src/FrigateRelay.Plugins.BlueIris/FrigateRelay.Plugins.BlueIris.csproj`
**Action:** create + modify
**Description:**

Add package references to `FrigateRelay.Plugins.BlueIris.csproj`:
- `Microsoft.Extensions.Http.Resilience` version `10.4.0` (per RESEARCH §1 — provides `HttpRetryStrategyOptions`, `AddResilienceHandler`).
- `Microsoft.Extensions.Http` (for `IHttpClientFactory` extensions).
- `Microsoft.Extensions.Options.ConfigurationExtensions` (for `.Bind(...)`).
- `Microsoft.Extensions.Hosting.Abstractions` is NOT needed in the plugin — the plugin must not depend on the host.

Create `BlueIrisActionPlugin`:

```csharp
namespace FrigateRelay.Plugins.BlueIris;

internal sealed class BlueIrisActionPlugin : IActionPlugin
{
    private static readonly Action<ILogger, string, string, Exception?> LogTriggerSuccess =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(201, "BlueIrisTriggerSuccess"),
            "BlueIris trigger fired event_id={EventId} url={Url}");

    private static readonly Action<ILogger, string, string, Exception?> LogTriggerFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(202, "BlueIrisTriggerFailed"),
            "BlueIris trigger failed event_id={EventId} url={Url}");

    private readonly IHttpClientFactory _httpFactory;
    private readonly BlueIrisUrlTemplate _template;
    private readonly ILogger<BlueIrisActionPlugin> _logger;

    public BlueIrisActionPlugin(
        IHttpClientFactory httpFactory,
        BlueIrisUrlTemplate template,
        ILogger<BlueIrisActionPlugin> logger)
    {
        _httpFactory = httpFactory;
        _template = template;
        _logger = logger;
    }

    public string Name => "BlueIris";

    public async Task ExecuteAsync(EventContext ctx, CancellationToken ct)
    {
        var url = _template.Resolve(ctx);
        using var client = _httpFactory.CreateClient("BlueIris");

        try
        {
            using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            LogTriggerSuccess(_logger, ctx.EventId, url, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            LogTriggerFailed(_logger, ctx.EventId, url, ex);
            throw; // Dispatcher's consumer catches, increments frigaterelay.dispatch.exhausted, logs Warning.
        }
    }
}
```

Notes:
- `Name => "BlueIris"` is **case-insensitive ordinal match** when EventPump (PLAN-3.1) builds its action-name → plugin map.
- Use `client.GetAsync(url, ct)` — note `url` is a string (the template returns a string after `Uri.EscapeDataString` of values). Calling `new Uri(url)` is unnecessary because `HttpClient.GetAsync(string, CancellationToken)` accepts a string.
- `EnsureSuccessStatusCode()` throws `HttpRequestException` on 4xx/5xx — the resilience handler's default `ShouldHandle` (RESEARCH §1) retries on 5xx; on 4xx the throw propagates immediately (4xx is non-transient, retrying is wasted effort). This matches the legacy reference behavior.
- The `throw` in the catch is essential — the dispatcher's consumer (PLAN-2.1) catches it and increments `frigaterelay.dispatch.exhausted`. Don't swallow.
- Plugin is `internal sealed` — registrar exposes it via `services.AddSingleton<IActionPlugin>(sp => sp.GetRequiredService<BlueIrisActionPlugin>())` (with the concrete type also registered as a singleton).

**Acceptance Criteria:**
- `BlueIrisActionPlugin` is `internal sealed`, implements `IActionPlugin`.
- `Name` returns the literal string `"BlueIris"`.
- `ExecuteAsync` uses `_httpFactory.CreateClient("BlueIris")` (the named-client name MUST match the registrar's `services.AddHttpClient("BlueIris", ...)` exactly — case-sensitive on the dictionary key).
- `git grep -nE '\.(Result|Wait)\(' src/FrigateRelay.Plugins.BlueIris/` returns zero matches.
- `git grep -n "ServicePointManager" src/FrigateRelay.Plugins.BlueIris/` returns zero matches.
- `git grep -nE '192\.168\.|10\.0\.|http://[a-zA-Z0-9.-]+:[0-9]+' src/FrigateRelay.Plugins.BlueIris/` returns zero matches (no hard-coded IPs/hostnames).
- `dotnet build FrigateRelay.sln -c Release` succeeds with zero warnings.

### Task 2: Implement BlueIris.PluginRegistrar
**Files:** `src/FrigateRelay.Plugins.BlueIris/PluginRegistrar.cs`
**Action:** create
**Description:**

Mirrors the Phase-3 `FrigateRelay.Sources.FrigateMqtt.PluginRegistrar` shape but with HttpClient + Resilience wiring. RESEARCH §1's working code is the source-of-truth pattern:

```csharp
namespace FrigateRelay.Plugins.BlueIris;

public sealed class PluginRegistrar : IPluginRegistrar
{
    public void Register(PluginRegistrationContext context)
    {
        context.Services
            .AddOptions<BlueIrisOptions>()
            .Bind(context.Configuration.GetSection("BlueIris"))
            .Validate(o => !string.IsNullOrWhiteSpace(o.TriggerUrlTemplate),
                      "BlueIris.TriggerUrlTemplate is required.")
            .Validate(o =>
            {
                // Force template parsing at startup; throws ArgumentException → Validate returns false.
                try { _ = BlueIrisUrlTemplate.Parse(o.TriggerUrlTemplate); return true; }
                catch (ArgumentException) { return false; }
            }, "BlueIris.TriggerUrlTemplate contains an unknown placeholder. Allowed: {camera}, {label}, {event_id}, {zone}.")
            .ValidateOnStart();

        // Singleton parsed template — reused across every BlueIrisActionPlugin invocation.
        context.Services.AddSingleton(sp =>
            BlueIrisUrlTemplate.Parse(sp.GetRequiredService<IOptions<BlueIrisOptions>>().Value.TriggerUrlTemplate));

        context.Services.AddHttpClient("BlueIris", (sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<BlueIrisOptions>>().Value;
            client.Timeout = opts.RequestTimeout;
        })
        .AddResilienceHandler("BlueIris-retry", builder =>
        {
            builder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                DelayGenerator = static args =>
                    ValueTask.FromResult<TimeSpan?>(
                        TimeSpan.FromSeconds(3 * (args.AttemptNumber + 1))),
                // ShouldHandle: keep default — handles 408/429/5xx + HttpRequestException + TimeoutRejectedException.
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

        // Plugin singleton + IActionPlugin alias.
        context.Services.AddSingleton<BlueIrisActionPlugin>();
        context.Services.AddSingleton<IActionPlugin>(sp =>
            sp.GetRequiredService<BlueIrisActionPlugin>());

        // Per-plugin queue capacity contribution: post-configure DispatcherOptions
        // so the dispatcher (PLAN-2.1) sees BlueIris's override via PerPluginQueueCapacity["BlueIris"].
        context.Services.PostConfigure<DispatcherOptions>(opts =>
        {
            // Snapshot at configure time — BlueIrisOptions has been bound by ValidateOnStart.
            // Use a transient IServiceProvider trick? No — simpler: read from IConfiguration directly.
            var capacity = context.Configuration.GetValue<int?>("BlueIris:QueueCapacity");
            if (capacity is { } c)
            {
                var dict = new Dictionary<string, int>(opts.PerPluginQueueCapacity, StringComparer.OrdinalIgnoreCase)
                {
                    ["BlueIris"] = c,
                };
                opts.PerPluginQueueCapacity = dict;
            }
        });
    }
}
```

**Important — DispatcherOptions namespace boundary:** `DispatcherOptions` lives in `FrigateRelay.Host.Dispatch` (PLAN-1.1). The plugin must NOT add a project reference to `FrigateRelay.Host` (CLAUDE.md invariant: host depends on abstractions only, plugins never depend on host). **Resolution:** PLAN-3.1 is responsible for wiring the per-plugin queue capacity in `Program.cs` (host-side), reading `BlueIris:QueueCapacity` from configuration. Remove the `PostConfigure<DispatcherOptions>` block from this registrar — keep the BlueIris plugin pure. Document this cross-plan handoff in the registrar's XML doc.

Revised registrar (final form):

```csharp
public sealed class PluginRegistrar : IPluginRegistrar
{
    public void Register(PluginRegistrationContext context)
    {
        context.Services
            .AddOptions<BlueIrisOptions>()
            .Bind(context.Configuration.GetSection("BlueIris"))
            .Validate(o => !string.IsNullOrWhiteSpace(o.TriggerUrlTemplate),
                      "BlueIris.TriggerUrlTemplate is required.")
            .Validate(o => { try { _ = BlueIrisUrlTemplate.Parse(o.TriggerUrlTemplate); return true; } catch { return false; } },
                      "BlueIris.TriggerUrlTemplate contains an unknown placeholder. Allowed: {camera}, {label}, {event_id}, {zone}.")
            .ValidateOnStart();

        context.Services.AddSingleton(sp =>
            BlueIrisUrlTemplate.Parse(sp.GetRequiredService<IOptions<BlueIrisOptions>>().Value.TriggerUrlTemplate));

        context.Services.AddHttpClient("BlueIris", /* ... as above ... */)
            .AddResilienceHandler("BlueIris-retry", /* ... as above ... */)
            .ConfigurePrimaryHttpMessageHandler(/* ... as above ... */);

        context.Services.AddSingleton<BlueIrisActionPlugin>();
        context.Services.AddSingleton<IActionPlugin>(sp => sp.GetRequiredService<BlueIrisActionPlugin>());
        // NOTE: BlueIris:QueueCapacity is consumed by Program.cs (PLAN-3.1) when configuring DispatcherOptions —
        //       NOT by this registrar, to keep this assembly free of FrigateRelay.Host dependencies.
    }
}
```

**Acceptance Criteria:**
- `PluginRegistrar` is `public sealed`, implements `IPluginRegistrar`.
- The expression `TimeSpan.FromSeconds(3 * (args.AttemptNumber + 1))` appears verbatim in the file (PLAN-2.1 Task 2's regression test depends on this exact formula).
- `services.AddHttpClient("BlueIris", ...)` — the literal `"BlueIris"` matches `BlueIrisActionPlugin._httpFactory.CreateClient("BlueIris")`.
- `ConfigurePrimaryHttpMessageHandler` builds a `SocketsHttpHandler` and ONLY sets `SslOptions.RemoteCertificateValidationCallback` when `opts.AllowInvalidCertificates == true`.
- `git grep -n "ServicePointManager" src/FrigateRelay.Plugins.BlueIris/` returns zero matches.
- `git grep -n "ProjectReference" src/FrigateRelay.Plugins.BlueIris/FrigateRelay.Plugins.BlueIris.csproj | grep -i "FrigateRelay.Host"` returns zero matches (plugin must not reference host).
- `.ValidateOnStart()` is called — host fails fast on missing/invalid TriggerUrlTemplate per PROJECT.md S2.

### Task 3: BlueIrisActionPlugin tests via WireMock.Net
**Files:** `tests/FrigateRelay.Plugins.BlueIris.Tests/FrigateRelay.Plugins.BlueIris.Tests.csproj`, `tests/FrigateRelay.Plugins.BlueIris.Tests/BlueIrisActionPluginTests.cs`
**Action:** modify + create
**Description:**

Add `<PackageReference Include="WireMock.Net" Version="*" />` (latest stable per RESEARCH §5) to the test csproj.

Create `BlueIrisActionPluginTests.cs` with these tests (names use underscores):

1. **`ExecuteAsync_HappyPath_FiresSingleGetWithResolvedUrl`** — start a `WireMockServer`, stub `GET /admin?camera=front&trigger=1` → 200. Build a `ServiceCollection`, run `BlueIris.PluginRegistrar.Register(...)` with in-memory config: `BlueIris:TriggerUrlTemplate = "{server.Urls[0]}/admin?camera={camera}&trigger=1"`. Resolve the plugin from the provider, call `ExecuteAsync(ctx, ct)` with `ctx.Camera = "front"`. Assert: `server.LogEntries.Count == 1`, the request path is `/admin`, the `camera` query param is `front`.

2. **`ExecuteAsync_TransientFailure_RetriesAndSucceeds`** — stub the endpoint to return 503 twice then 200 (use WireMock's `InScenario` / `WhenStateIs` API). Call `ExecuteAsync`. Assert: total 3 requests received (2 failures + 1 success). The plugin returns successfully (no throw). **Do NOT assert exact wall-clock — the 3s+6s real delays would make this test slow.** RESEARCH §1 footnote: Polly v8 supports a `TimeProvider` that can be injected to make tests fast. To keep the test fast, **swap the `DelayGenerator` to return `TimeSpan.Zero` in the test's registrar override** (rebuild the registrar in the test with a no-delay generator) OR use `args.AttemptNumber` to detect zero-only delays. **Recommendation: the test registers its OWN HttpClient + resilience handler bypassing the production registrar** for delay control. This is acceptable scope — the production registrar's delay schedule is verified separately in PLAN-2.1 Task 2.

3. **`ExecuteAsync_PersistentFailure_AfterAllRetries_ThrowsHttpRequestException`** — stub endpoint to always return 503. Call `ExecuteAsync`. Assert: throws `HttpRequestException`. Total request count is 4 (1 initial + 3 retries — RESEARCH TL;DR finding #3). Use the same fast-delay test registrar as test #2.

4. **`Register_WithUnknownPlaceholderInTemplate_FailsAtStartup`** — build a `ServiceCollection`, register with `BlueIris:TriggerUrlTemplate = "https://x/{score}"`. Build the host (`builder.Build()`). Assert: `OptionsValidationException` is thrown. (`{score}` is no longer in the allowlist per Q1 resolution.)

5. **`Register_WithMissingTemplate_FailsAtStartup`** — `BlueIris:TriggerUrlTemplate` absent from config. Building the host throws `OptionsValidationException` whose message contains `"BlueIris.TriggerUrlTemplate is required"`.

6. **`Register_AllowInvalidCertificatesTrue_PrimaryHandlerSkipsValidation`** — register with `BlueIris:AllowInvalidCertificates = true`. Resolve the named HttpClient via `IHttpClientFactory.CreateClient("BlueIris")`. Inspect: this is harder than it looks because the resilience handler chain wraps the primary handler. **Pragmatic test:** stand up a WireMock HTTPS server with a self-signed cert; assert the request succeeds when `AllowInvalidCertificates = true`, fails when `false`. WireMock supports `WithUrls("https://localhost:0")` per its docs. If HTTPS-with-self-signed-cert in WireMock is too fiddly, fall back to: assert via reflection that the constructed `SocketsHttpHandler.SslOptions.RemoteCertificateValidationCallback` is non-null when `AllowInvalidCertificates = true` and null when `false`. **Builder choice:** start with reflection assertion; upgrade to live HTTPS test only if it's straightforward. Both encode the invariant.

For the test EventContext use the same `NewCtx` helper from PLAN-1.2's `BlueIrisUrlTemplateTests` (extract to a shared fixture file or duplicate — duplication is fine for two test files).

WireMock anti-pattern from RESEARCH §5: **never** call `_httpClient.GetAsync(...).Wait()` — use `await` everywhere. CLAUDE.md `tests/` greps for `.Result/.Wait`.

**Acceptance Criteria:**
- All 6 test methods exist with the exact names above and pass:
  `dotnet run --project tests/FrigateRelay.Plugins.BlueIris.Tests -c Release -- --filter-query "/*/*/BlueIrisActionPluginTests/*"`
- Test #3 explicitly counts 4 total HTTP requests (1 initial + 3 retries) — RESEARCH TL;DR #3 invariant.
- Test #4 asserts startup failure for `{score}` (the Q1 deferral encoded as a runtime guard).
- `git grep -nE '\.(Result|Wait)\(' tests/FrigateRelay.Plugins.BlueIris.Tests/` returns zero matches.
- WireMock package reference present in csproj.
- Total wall-clock for the suite is **<10 seconds** (achieved via the fast-delay test registrar in tests #2 and #3).

## Verification

```bash
dotnet build FrigateRelay.sln -c Release
dotnet run --project tests/FrigateRelay.Plugins.BlueIris.Tests -c Release -- --filter-query "/*/*/BlueIrisActionPluginTests/*"
git grep -n "ServicePointManager" src/
git grep -n "FrigateRelay.Host" src/FrigateRelay.Plugins.BlueIris/FrigateRelay.Plugins.BlueIris.csproj
git grep -nE '\.(Result|Wait)\(' src/ tests/
git grep -nE '192\.168\.|10\.0\.0\.' src/FrigateRelay.Plugins.BlueIris/
```

Expected: build clean, 6 tests pass, no `ServicePointManager`, plugin csproj has no host reference, no `.Result/.Wait`, no hard-coded IPs.

## Notes for the builder

- The plugin **must not reference `FrigateRelay.Host`** — verified by Task 2 acceptance criterion. Per-plugin queue capacity is wired in PLAN-3.1's `Program.cs` (host-side reads `BlueIris:QueueCapacity`), not in this registrar. This keeps the plugin "transport-only" per the Phase-3 precedent.
- The retry pipeline lives at the HttpClient layer (this plan), NOT inside the dispatcher (PLAN-2.1's plan documents this). Do NOT also add a Polly pipeline inside the dispatcher consumer — that would multiply retries and silently violate D7.
- RESEARCH §1 confirms `MaxRetryAttempts = 3` means "3 retries after 1 initial attempt = 4 total HTTP calls". Test #3's count assertion encodes this.
- RESEARCH §1's working code includes `ShouldRetryAfterHeader = true` (the default). Don't override; BlueIris doesn't return `Retry-After` headers, so the default is harmless.
- The `BlueIrisUrlTemplate` is registered as a singleton in the registrar — parsing happens once at host startup, then `Resolve` is called per event.
- Test #6's reflection-based assertion is the pragmatic fallback if live-HTTPS WireMock is fiddly. Either approach proves the invariant; the decision is left to the builder based on what builds cleanly first.
- WireMock.Net's `LogEntries` collection is the canonical request-history surface for assertions. RESEARCH §5 has the exact API.
