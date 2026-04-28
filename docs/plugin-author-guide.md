# FrigateRelay Plugin Author Guide

This guide walks you through writing a custom plugin for FrigateRelay. It covers
every plugin contract — `IActionPlugin`, `IValidationPlugin`, `ISnapshotProvider`,
and `IPluginRegistrar` — from scaffolding to a working, tested, and DI-registered
implementation.

## 1. Audience and scope

This guide is for developers who want to extend FrigateRelay with:

- A custom **action** (send a notification, call an API, trigger an NVR)
- A custom **validator** (filter events by object class, confidence, time-of-day, etc.)
- A custom **snapshot provider** (fetch image bytes from a non-standard source)

It does NOT cover:

- Changing the host, the dispatcher, or `IEventSource` — those are core internals.
- Runtime DLL plugin discovery — v1 uses build-time DI. See
  [Section 10](#10-forward-compat-design-for-b) for the forward-compat note.

Hard constraints are locked in `CLAUDE.md` under "Architecture invariants". The
most plugin-relevant ones:

- Plugin contracts live in `FrigateRelay.Abstractions`; never reference host-internal
  types from a plugin assembly.
- All plugin service registrations use `AddSingleton`. Transient/scoped lifetimes are
  not supported.
- No `.Result` / `.Wait()` calls — use `await`.
- No hard-coded IPs or hostnames. No secrets in source.

---

## 2. Scaffold with `dotnet new`

Install the template (run once from the repo root):

```bash
dotnet new install templates/FrigateRelay.Plugins.Template/
```

Render a new plugin project:

```bash
dotnet new frigaterelay-plugin -n FrigateRelay.Plugins.MyPlugin -o src/FrigateRelay.Plugins.MyPlugin
```

The template generates:

```
src/FrigateRelay.Plugins.MyPlugin/
  FrigateRelay.Plugins.MyPlugin.csproj   # references FrigateRelay.Abstractions
  ExampleActionPlugin.cs                 # placeholder IActionPlugin
  ExamplePluginRegistrar.cs              # placeholder IPluginRegistrar
tests/FrigateRelay.Plugins.MyPlugin.Tests/
  FrigateRelay.Plugins.MyPlugin.Tests.csproj
  ExampleActionPluginTests.cs            # starter test
```

Wire the projects into the solution:

```bash
dotnet sln FrigateRelay.sln add src/FrigateRelay.Plugins.MyPlugin/FrigateRelay.Plugins.MyPlugin.csproj
dotnet sln FrigateRelay.sln add tests/FrigateRelay.Plugins.MyPlugin.Tests/FrigateRelay.Plugins.MyPlugin.Tests.csproj
```

Register the plugin in the host by adding your `IPluginRegistrar` to the host's DI
bootstrap (see [Section 6](#6-ipluginregistrar-walkthrough)).

---

## 3. `IActionPlugin` walkthrough

An action plugin receives a dispatched `EventContext` and performs some side effect —
sending a push notification, calling an NVR, writing a record to a database.

**Contract surface:**

- `string Name` — unique identifier; must match the name used in `appsettings.json`
  `Subscriptions:N:Actions`.
- `Task ExecuteAsync(EventContext ctx, SnapshotContext snapshot, CancellationToken ct)`
  — the work method.

**Snapshot parameter:**

The `SnapshotContext snapshot` parameter is pre-wired by the dispatcher using
three-tier resolution (per-action override → per-subscription default → global
`DefaultSnapshotProvider`). Call `await snapshot.ResolveAsync(ctx, ct)` to obtain
image bytes. Plugins that do not need snapshots (e.g. a camera-trigger plugin) accept
the parameter and ignore it — there is no compile-time opt-out, by design.

Calling `ResolveAsync` on `default(SnapshotContext)` is safe — it returns `null`
immediately without a network call, so unit tests can pass `default` without setting
up a provider.

```csharp filename=SampleActionPlugin.cs
using FrigateRelay.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FrigateRelay.Samples.PluginGuide;

/// <summary>
/// Demonstrates a minimal <see cref="IActionPlugin"/> implementation.
/// This plugin logs the event and, when configured to do so, resolves
/// a snapshot via the pre-wired <see cref="SnapshotContext"/> parameter.
/// </summary>
/// <remarks>
/// Snapshot-consuming pattern: call <see cref="SnapshotContext.ResolveAsync"/> to obtain image
/// bytes. Plugins that do not need snapshot images (e.g. camera-trigger plugins like BlueIris)
/// accept the snapshot parameter and ignore it — no compile-time opt-out exists.
/// </remarks>
public sealed partial class SampleActionPlugin : IActionPlugin
{
    private readonly ILogger<SampleActionPlugin> _logger;
    private readonly SamplePluginOptions _options;

    /// <summary>
    /// Initializes a new instance of <see cref="SampleActionPlugin"/>.
    /// </summary>
    /// <param name="logger">The logger provided by the host DI container.</param>
    /// <param name="options">Bound configuration options for this plugin.</param>
    public SampleActionPlugin(
        ILogger<SampleActionPlugin> logger,
        IOptions<SamplePluginOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    /// <inheritdoc />
    public string Name => "Sample";

    /// <inheritdoc />
    public async Task ExecuteAsync(
        EventContext ctx,
        SnapshotContext snapshot,
        CancellationToken ct)
    {
        LogEventReceived(_logger, ctx.EventId, ctx.Camera, ctx.Label);

        if (_options.FetchSnapshot)
        {
            // Resolve the snapshot via the three-tier resolver pre-wired by the dispatcher.
            // Calling ResolveAsync on default(SnapshotContext) is safe — it returns null.
            var result = await snapshot.ResolveAsync(ctx, ct).ConfigureAwait(false);
            if (result is not null)
            {
                LogSnapshotReceived(_logger, ctx.EventId, result.Bytes.Length, result.ContentType);
            }
            else
            {
                LogSnapshotUnavailable(_logger, ctx.EventId);
            }
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "sample_action_plugin event_id={EventId} camera={Camera} label={Label}")]
    private static partial void LogEventReceived(
        ILogger logger, string eventId, string camera, string label);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "sample_action_plugin snapshot_received event_id={EventId} bytes={Bytes} content_type={ContentType}")]
    private static partial void LogSnapshotReceived(
        ILogger logger, string eventId, int bytes, string contentType);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "sample_action_plugin snapshot_unavailable event_id={EventId}")]
    private static partial void LogSnapshotUnavailable(ILogger logger, string eventId);
}
```

**Logging pattern:** use `[LoggerMessage]` source-gen delegates (shown above) rather
than direct `_logger.LogInformation(...)` calls. This avoids boxing allocations on
the hot path and is the pattern used throughout the FrigateRelay codebase. Do NOT use
the legacy `_logger.Error(ex.Message, ex)` anti-pattern — use
`ILogger.LogError(ex, "message {Field}", value)` instead.

---

## 4. `IValidationPlugin` walkthrough

A validation plugin runs before its paired action and returns a `Verdict`. A failing
verdict short-circuits **that action only** — other actions in the same event
continue independently (CLAUDE.md V3).

**Contract surface:**

- `string Name` — identifier used in `Subscriptions:N:Actions[i].Validators`.
- `Task<Verdict> ValidateAsync(EventContext ctx, SnapshotContext snapshot, CancellationToken ct)`

**Verdict factory methods:**

- `Verdict.Pass()` — allow the action to proceed.
- `Verdict.Fail(string reason)` — reject the action; the host emits a structured
  `validator_rejected` log entry with the action name, validator name, and reason.

**Snapshot sharing:**

When validators are configured, the dispatcher pre-resolves the snapshot once and
shares the same `SnapshotContext` across the entire validator chain and the action.
This means a validator that calls `snapshot.ResolveAsync(ctx, ct)` does NOT trigger
a second HTTP request — the bytes are cached on the `SnapshotContext`. Validators
that only inspect `EventContext` metadata (label, camera, score) can ignore the
snapshot parameter entirely.

```csharp filename=SampleValidationPlugin.cs
using FrigateRelay.Abstractions;
using Microsoft.Extensions.Logging;

namespace FrigateRelay.Samples.PluginGuide;

/// <summary>
/// Demonstrates a minimal <see cref="IValidationPlugin"/> implementation.
/// Returns <see cref="Verdict.Pass()"/> when the detected label is "person";
/// returns <see cref="Verdict.Fail(string)"/> for any other label.
/// </summary>
/// <remarks>
/// <para>
/// Validators run per-action, not globally (CLAUDE.md V3). A failing verdict
/// short-circuits THAT action only — other actions in the same event continue
/// independently.
/// </para>
/// <para>
/// The snapshot parameter mirrors the action plugin signature.
/// When validators are present, the dispatcher pre-resolves the snapshot ONCE and
/// shares it across the validator chain and the action, avoiding redundant HTTP
/// fetches. Metadata-only validators (like this one) can ignore the parameter.
/// </para>
/// </remarks>
public sealed partial class SampleValidationPlugin : IValidationPlugin
{
    private readonly ILogger<SampleValidationPlugin> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="SampleValidationPlugin"/>.
    /// </summary>
    /// <param name="logger">The logger provided by the host DI container.</param>
    public SampleValidationPlugin(ILogger<SampleValidationPlugin> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "SampleValidator";

    /// <inheritdoc />
    public Task<Verdict> ValidateAsync(
        EventContext ctx,
        SnapshotContext snapshot,
        CancellationToken ct)
    {
        if (ctx.Label == "person")
        {
            LogPass(_logger, ctx.EventId, ctx.Label);
            return Task.FromResult(Verdict.Pass());
        }

        LogFail(_logger, ctx.EventId, ctx.Label);
        return Task.FromResult(Verdict.Fail($"label '{ctx.Label}' is not 'person'"));
    }

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "sample_validator pass event_id={EventId} label={Label}")]
    private static partial void LogPass(ILogger logger, string eventId, string label);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "sample_validator fail event_id={EventId} label={Label}")]
    private static partial void LogFail(ILogger logger, string eventId, string label);
}
```

---

## 5. `ISnapshotProvider` walkthrough

A snapshot provider fetches image bytes for an event. The dispatcher uses a
three-tier name-based lookup to select which provider to use for each action:

1. **Per-action override** — `Subscriptions:N:Actions[i].SnapshotProvider`
2. **Per-subscription default** — `Subscriptions:N:SnapshotProvider`
3. **Global default** — `DefaultSnapshotProvider` in host options

**Contract surface:**

- `string Name` — must match the name used in config.
- `Task<SnapshotResult?> FetchAsync(SnapshotRequest request, CancellationToken ct)`

**Fail-open rule:** return `null` on network errors or timeouts rather than
throwing. The dispatcher treats `null` as "no snapshot available" and continues
dispatching the action without image data. Throwing propagates up through the
resilience pipeline and may cause retries.

**HttpClient:** use `IHttpClientFactory` (injected via the registrar's
`AddHttpClient` call) rather than constructing `HttpClient` directly. This gives
you connection pooling, Polly resilience pipelines, and per-named-client TLS
configuration (including the `AllowInvalidCertificates` opt-in scope, if needed).

```csharp filename=SampleSnapshotProvider.cs
using FrigateRelay.Abstractions;
using Microsoft.Extensions.Logging;

namespace FrigateRelay.Samples.PluginGuide;

/// <summary>
/// Demonstrates a minimal <see cref="ISnapshotProvider"/> implementation.
/// Returns a stub four-byte payload tagged with the request's event id rather
/// than performing a real HTTP fetch.
/// </summary>
/// <remarks>
/// <para>
/// Snapshot providers are resolved by the dispatcher using a three-tier lookup:
/// <list type="number">
///   <item>Per-action provider name override (in <c>Subscriptions:N:Actions</c>)</item>
///   <item>Per-subscription default provider name (in <c>Subscriptions:N:SnapshotProvider</c>)</item>
///   <item>Global <c>DefaultSnapshotProvider</c> from host options</item>
/// </list>
/// </para>
/// <para>
/// Providers should be fail-open: return <see langword="null"/> on network errors or
/// timeouts rather than throwing — the dispatcher treats <see langword="null"/> as
/// "no snapshot available" and continues dispatching the action without image data.
/// </para>
/// </remarks>
public sealed partial class SampleSnapshotProvider : ISnapshotProvider
{
    private readonly ILogger<SampleSnapshotProvider> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="SampleSnapshotProvider"/>.
    /// </summary>
    /// <param name="logger">The logger provided by the host DI container.</param>
    public SampleSnapshotProvider(ILogger<SampleSnapshotProvider> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "SampleSnapshot";

    /// <inheritdoc />
    public Task<SnapshotResult?> FetchAsync(SnapshotRequest request, CancellationToken ct)
    {
        // Stub: return four zero bytes to prove the contract without a real HTTP call.
        // In a real provider: use IHttpClientFactory to create a named HttpClient and
        // fetch the image from the camera or NVR endpoint. Return null on failure (fail-open).
        LogFetching(_logger, request.Context.EventId);

        var result = new SnapshotResult
        {
            Bytes = [0x00, 0x00, 0x00, 0x00],
            ContentType = "image/jpeg",
            ProviderName = Name,
        };

        return Task.FromResult<SnapshotResult?>(result);
    }

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "sample_snapshot_provider fetch event_id={EventId}")]
    private static partial void LogFetching(ILogger logger, string eventId);
}
```

---

## 6. `IPluginRegistrar` walkthrough

The registrar is the entry point the host discovers at startup. Every plugin
assembly exposes exactly one `IPluginRegistrar`. The host resolves all registrars
from DI and calls `Register` once per registrar during application startup.

**Contract surface:**

- `void Register(PluginRegistrationContext context)` — receives `context.Services`
  (`IServiceCollection`) and `context.Configuration` (`IConfiguration`).

**Registration rules:**

- Use `AddSingleton` for all plugin types. The dispatcher resolves action, validation,
  and snapshot providers once at startup.
- Bind options with `AddOptions<TOptions>().Bind(context.Configuration.GetSection("..."))`.
- Use `AddHttpClient` (never `new HttpClient()`) for any plugin that makes HTTP calls.
- If your plugin needs to skip TLS verification (e.g. a self-signed NVR cert), scope
  it to your named `HttpClient` via `SocketsHttpHandler.SslOptions` and gate it behind
  an explicit `AllowInvalidCertificates: true` config flag — never set a global
  `ServicePointManager.ServerCertificateValidationCallback`.

```csharp filename=SamplePluginRegistrar.cs
using FrigateRelay.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace FrigateRelay.Samples.PluginGuide;

/// <summary>
/// Registers all sample plugin services into the host DI container.
/// The host discovers this class and calls <see cref="Register"/> at startup.
/// </summary>
/// <remarks>
/// <para>
/// Every plugin assembly exposes exactly one <see cref="IPluginRegistrar"/> implementation.
/// The host resolves all registrars from DI and invokes <see cref="Register"/> once per
/// registrar during application startup.
/// </para>
/// <para>
/// All plugin service registrations use <c>AddSingleton</c>. The dispatcher resolves
/// action, validation, and snapshot provider plugins once at startup — transient or
/// scoped lifetimes are not supported.
/// </para>
/// <para>
/// Prefer <see cref="IHttpClientFactory"/> (via <c>AddHttpClient</c>) over constructing
/// <see cref="System.Net.Http.HttpClient"/> instances directly, to benefit from connection
/// pooling, Polly resilience pipelines, and per-named-client TLS configuration.
/// </para>
/// </remarks>
public sealed class SamplePluginRegistrar : IPluginRegistrar
{
    /// <inheritdoc />
    public void Register(PluginRegistrationContext context)
    {
        // Bind plugin options from the "Sample" configuration section.
        context.Services
            .AddOptions<SamplePluginOptions>()
            .Bind(context.Configuration.GetSection("Sample"));

        // Register the action plugin.
        context.Services.AddSingleton<IActionPlugin, SampleActionPlugin>();

        // Register the validation plugin.
        context.Services.AddSingleton<IValidationPlugin, SampleValidationPlugin>();

        // Register the snapshot provider.
        context.Services.AddSingleton<ISnapshotProvider, SampleSnapshotProvider>();
    }
}
```

---

## 7. Configuration binding

FrigateRelay uses a **Profiles + Subscriptions** config shape (CLAUDE.md S2).

### Options class

Create a POCO options class for your plugin's settings:

```csharp
public sealed class MyPluginOptions
{
    public string ApiUrl { get; set; } = string.Empty;
    public bool AllowInvalidCertificates { get; set; }
}
```

Bind it in the registrar:

```csharp
context.Services
    .AddOptions<MyPluginOptions>()
    .Bind(context.Configuration.GetSection("MyPlugin"));
```

### appsettings.json shape

```json
{
  "FrigateRelay": {
    "DefaultSnapshotProvider": "Frigate",
    "Profiles": {
      "standard": {
        "Actions": ["MyPlugin"]
      }
    },
    "Subscriptions": [
      {
        "Camera": "front-door",
        "Profile": "standard"
      }
    ]
  },
  "MyPlugin": {
    "ApiUrl": ""
  }
}
```

Secret fields (API keys, tokens, passwords) default to `""` in `appsettings.json`
and must be supplied via environment variables or user-secrets:

```bash
# Environment variable override (Docker / systemd)
MyPlugin__ApiToken=your-token-here

# .NET user-secrets (local dev)
dotnet user-secrets set "MyPlugin:ApiToken" "your-token-here" \
  --project src/FrigateRelay.Host
```

### Actions: both config shapes

`Subscriptions:N:Actions` accepts both an object form and a string-array shorthand
(ID-12 closure). Your plugin's `Name` must match in both forms:

```json
"Actions": ["MyPlugin"]
```

```json
"Actions": [
  { "Plugin": "MyPlugin" },
  { "Plugin": "MyPlugin", "SnapshotProvider": "Frigate", "Validators": ["MyValidator"] }
]
```

---

## 8. Lifecycle and DI scope rules

| Rule | Detail |
|------|--------|
| **Singleton only** | `IActionPlugin`, `IValidationPlugin`, `ISnapshotProvider` are resolved once at startup by the dispatcher. Do not register them as `Transient` or `Scoped`. |
| **HttpClient via factory** | Inject `IHttpClientFactory`; create a named client in `FetchAsync`/`ExecuteAsync`. Never capture an `HttpClient` in a field (connection exhaustion). |
| **No `.Result` / `.Wait()`** | All async paths must `await`. Blocking on a `Task` in a singleton will deadlock under back-pressure. |
| **Structured logging** | Use `ILogger.LogError(ex, "message {Field}", value)`. Do NOT use `_logger.Error(ex.Message, ex)`. |
| **Startup validation** | If your plugin options have required fields, validate them in your registrar using the options-validation pattern — `AddOptions<T>().Validate(o => ..., "reason")`. The host's startup validation is collect-all: it gathers every error before throwing so operators see all problems at once. |

---

## 9. Testing your plugin

FrigateRelay tests use **MSTest v3** with the **Microsoft.Testing.Platform** runner.
Run tests with `dotnet run`, not `dotnet test`:

```bash
dotnet run --project tests/FrigateRelay.Plugins.MyPlugin.Tests -c Release
```

See the `BlueIris.Tests` and `Pushover.Tests` projects for established patterns:

- **NSubstitute** for mocking interfaces.
- **WireMock.Net** for stubbing HTTP endpoints (NVR, notification APIs).
- **`FrigateRelay.TestHelpers.CapturingLogger<T>`** for asserting log output without
  fragile `ILogger<T>` NSubstitute mocks. Reference
  `tests/FrigateRelay.TestHelpers/CapturingLogger.cs` and add a project reference:

```xml
<ProjectReference Include="..\..\tests\FrigateRelay.TestHelpers\FrigateRelay.TestHelpers.csproj" />
```

Then in `Usings.cs`:

```csharp
global using FrigateRelay.TestHelpers;
```

- **Test naming convention:** `Method_Condition_Expected` (underscores; `CA1707`
  is silenced for test files via `.editorconfig`).

- **FluentAssertions** is pinned to `6.12.2` (Apache-2.0). Do not upgrade to 7.x —
  license constraint.

---

## 10. Forward-compat: "design for B"

FrigateRelay v1 uses **build-time DI** — plugin assemblies are compiled into the
same binary as the host, and the `IPluginRegistrar` is wired up manually in
`Program.cs` or a startup extension.

**PROJECT.md Goal #3** states that the same plugin contract must remain compatible
with a future `AssemblyLoadContext`-based runtime loader — this is an additive
phase, not a rewrite. Practical implications for plugin authors:

- Only reference `FrigateRelay.Abstractions` types in your public API surface (the
  contract assembly). Never take a direct dependency on `FrigateRelay.Host` or any
  other internal assembly from a plugin.
- Keep your `IPluginRegistrar` stateless. The future loader will instantiate it via
  `Activator.CreateInstance`; constructors with required DI arguments will fail.
- Avoid reflection on `AppDomain.CurrentDomain` or static cross-assembly coupling
  that assumes a single load context.

When the `AssemblyLoadContext` loader lands, the plugin binary will be loaded
into an isolated context; the build-time registration path will remain available as
an opt-in for monorepo consumers.

---

## 11. Putting it together

The `samples/FrigateRelay.Samples.PluginGuide` project wires up all three plugin
shapes via in-process DI and exercises each contract with a synthetic `EventContext`.
This is the live proof that the sample code compiles, resolves from DI, and runs
without throwing.

The `docs.yml` CI workflow runs:

```bash
dotnet run --project samples/FrigateRelay.Samples.PluginGuide -c Release --no-build
```

A zero exit code means all sample plugins are healthy.

```csharp filename=Program.cs
using FrigateRelay.Abstractions;
using FrigateRelay.Samples.PluginGuide;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// -----------------------------------------------------------------------
// FrigateRelay.Samples.PluginGuide — "putting it together" entry point
//
// This program wires up the three sample plugin implementations via
// in-process DI (no full Generic Host) and exercises each contract once
// with a synthetic EventContext.  It is the live proof that the sample
// code compiles, resolves from DI, and runs without throwing.
//
// The docs.yml CI workflow runs `dotnet run --project samples/... --no-build`
// and treats a zero exit code as "samples healthy."
// -----------------------------------------------------------------------

var configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Sample:FetchSnapshot"] = "true",
    })
    .Build();

var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));

// Simulate how the host invokes plugin registrars at startup.
var registrar = new SamplePluginRegistrar();
registrar.Register(new PluginRegistrationContext(services, configuration));

var provider = services.BuildServiceProvider();

// Synthetic event — no live Frigate instance needed.
var ctx = new EventContext
{
    EventId = "guide-demo-001",
    Camera = "front-door",
    Label = "person",
    StartedAt = DateTimeOffset.UtcNow,
    RawPayload = "{}",
    SnapshotFetcher = static _ => ValueTask.FromResult<byte[]?>(null),
};

var logger = provider.GetRequiredService<ILogger<Program>>();

// --- IActionPlugin ---
var actionPlugin = provider.GetRequiredService<IActionPlugin>();
Log.RunningPlugin(logger, "IActionPlugin", actionPlugin.Name);
await actionPlugin.ExecuteAsync(ctx, default, CancellationToken.None).ConfigureAwait(false);

// --- IValidationPlugin (person → Pass) ---
var validationPlugin = provider.GetRequiredService<IValidationPlugin>();
Log.RunningValidator(logger, validationPlugin.Name, "person");
var pass = await validationPlugin.ValidateAsync(ctx, default, CancellationToken.None).ConfigureAwait(false);
if (!pass.Passed)
{
    Console.Error.WriteLine($"ERROR: validator expected Pass for label=person but got Fail({pass.Reason})");
    return 1;
}

// --- IValidationPlugin (car → Fail) ---
var carCtx = ctx with { Label = "car" };
Log.RunningValidator(logger, validationPlugin.Name, "car");
var fail = await validationPlugin.ValidateAsync(carCtx, default, CancellationToken.None).ConfigureAwait(false);
if (fail.Passed)
{
    Console.Error.WriteLine("ERROR: validator expected Fail for label=car but got Pass");
    return 1;
}

// --- ISnapshotProvider ---
var snapshotProvider = provider.GetRequiredService<ISnapshotProvider>();
Log.RunningProvider(logger, snapshotProvider.Name);
var request = new SnapshotRequest { Context = ctx };
var snapshot = await snapshotProvider.FetchAsync(request, CancellationToken.None).ConfigureAwait(false);
if (snapshot is null)
{
    Console.Error.WriteLine("ERROR: snapshot provider returned null but was expected to return a stub result");
    return 1;
}

Log.AllSucceeded(logger, snapshot.Bytes.Length);
return 0;

/// <summary>LoggerMessage delegates for the entry-point program.</summary>
internal static partial class Log
{
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Running {Contract} '{Name}'")]
    internal static partial void RunningPlugin(ILogger logger, string contract, string name);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Running IValidationPlugin '{Name}' with label={Label}")]
    internal static partial void RunningValidator(ILogger logger, string name, string label);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Running ISnapshotProvider '{Name}'")]
    internal static partial void RunningProvider(ILogger logger, string name);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "All sample plugins exercised successfully. snapshot bytes={Bytes}")]
    internal static partial void AllSucceeded(ILogger logger, int bytes);
}
```
