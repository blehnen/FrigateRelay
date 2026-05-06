# Phase 14 RESEARCH

Codebase patterns and external-API shapes the architect needs to plan #13 (Roboflow), #14 (DOODS2), and #23 (parallel validators).

---

## 1. CPAI plugin reference (clone shape for Roboflow + DOODS2)

The architect should clone this shape verbatim — CPAI is the canonical `IValidationPlugin` exemplar.

### 1.1 Project layout

`src/FrigateRelay.Plugins.CodeProjectAi/`:
- `CodeProjectAiOptions.cs` — DataAnnotations-decorated options class (1 file, ~80 lines).
- `CodeProjectAiResponse.cs` — DTO for HTTP response deserialization.
- `CodeProjectAiValidator.cs` — `IValidationPlugin` implementation (~125 lines).
- `PluginRegistrar.cs` — `IPluginRegistrar` implementation (~88 lines).
- `FrigateRelay.Plugins.CodeProjectAi.csproj` — minimal csproj.

### 1.2 csproj shape (clone for Roboflow + DOODS2 HTTP-only path)

`src/FrigateRelay.Plugins.CodeProjectAi/FrigateRelay.Plugins.CodeProjectAi.csproj:1-24`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>FrigateRelay.Plugins.CodeProjectAi</RootNamespace>
    <AssemblyName>FrigateRelay.Plugins.CodeProjectAi</AssemblyName>
    <IsPackable>false</IsPackable>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\FrigateRelay.Abstractions\FrigateRelay.Abstractions.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Http" Version="10.0.7" />
    <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="10.0.7" />
    <PackageReference Include="Microsoft.Extensions.Options.DataAnnotations" Version="10.0.7" />
  </ItemGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="FrigateRelay.Plugins.CodeProjectAi.Tests" />
  </ItemGroup>
</Project>
```

### 1.3 Options class shape

`src/FrigateRelay.Plugins.CodeProjectAi/CodeProjectAiOptions.cs:43-78`:
- `[Required, Url] string BaseUrl = ""`
- `[Range(0.0, 1.0)] double MinConfidence = 0.5`
- `string[] AllowedLabels = []` (empty = any label passes)
- `ValidatorErrorMode OnError = FailClosed` (enum FailClosed/FailOpen)
- `[Range(typeof(TimeSpan), "00:00:01", "00:01:00")] TimeSpan Timeout = TimeSpan.FromSeconds(5)`
- `bool AllowInvalidCertificates = false` (per-instance TLS skip; opt-in)

`ValidatorErrorMode` enum lives at `CodeProjectAiOptions.cs:81-88` — Roboflow + DOODS2 should each define their own enum with the same shape (or factor a shared type later, but not v1.2).

### 1.4 Validator class shape

`src/FrigateRelay.Plugins.CodeProjectAi/CodeProjectAiValidator.cs`:
- `public sealed partial class CodeProjectAiValidator : IValidationPlugin` (line 24)
- Constructor signature (line 36): `(string name, CodeProjectAiOptions opts, HttpClient http, ILogger<CodeProjectAiValidator> logger)`
- `public string Name => _name;` (line 49) — `IValidationPlugin.Name` getter
- `public async Task<Verdict> ValidateAsync(EventContext ctx, SnapshotContext snapshot, CancellationToken ct)` (line 52)
- **Snapshot resolution at line 54:** `var snap = await snapshot.ResolveAsync(ctx, ct).ConfigureAwait(false);` — returns `null` if no snapshot, validator returns `Verdict.Fail("validator_no_snapshot")`.
- **HTTP call at line 61:** `_http.PostAsync("/v1/vision/detection", content, ct)` — `HttpClient.BaseAddress` set by registrar; relative path here.
- **Catch-block ordering (lines 66-84) MUST be preserved verbatim:**
  - First: `catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }` — host shutdown propagation, NOT a validator failure.
  - Second: `catch (TaskCanceledException ex)` — timeout. Honors `OnError`: `FailOpen` → `Verdict.Pass()`, otherwise `Verdict.Fail("validator_timeout")`.
  - Third: `catch (HttpRequestException ex)` — network/non-2xx. Honors `OnError` same way; reason: `validator_unavailable: {ex.Message}`.
- **`LoggerMessage` source-generated logging at lines 115-124** — partial class `Log` with `[LoggerMessage(EventId = 7001|7002, Level=Warning)]`. Roboflow should use a different EventId range (suggest 7100s); DOODS2 a different range (7200s). Coordinate at PR-1 / PR-2 planning.

### 1.5 PluginRegistrar shape — the registration ritual

`src/FrigateRelay.Plugins.CodeProjectAi/PluginRegistrar.cs:30-86`:
1. Get `Validators` config section; bail if not present (line 32-33).
2. For each child (`instance` = one named entry like `cpai`, `roboflow_persons`, etc.):
   - Filter by `Type` discriminator (line 37-39): `if (!string.Equals(type, "CodeProjectAi", StringComparison.Ordinal)) continue;`
   - Capture `instance.Key` to a local — closure-safety (line 43).
   - `AddOptions<CodeProjectAiOptions>(instanceKey).Bind(instance).ValidateDataAnnotations().ValidateOnStart()` — named options bound to that section (lines 46-50).
   - `AddHttpClient($"CodeProjectAi:{instanceKey}").ConfigurePrimaryHttpMessageHandler(sp => ...)` — per-instance `HttpClient` with optional TLS bypass via per-handler `SocketsHttpHandler.SslOptions.RemoteCertificateValidationCallback` (lines 53-72). NEVER use `ServicePointManager` — CLAUDE.md invariant.
   - `AddKeyedSingleton<IValidationPlugin>(instanceKey, (sp, key) => new CodeProjectAiValidator(...))` — keyed by the named instance key (lines 75-84). The constructor receives the bound options + the named `HttpClient` with `BaseAddress` and `Timeout` set from `opts`.
3. Critical detail: `BaseAddress` and `Timeout` are set on the `HttpClient` at the keyed-singleton factory (lines 80-81), NOT inside the registrar's `ConfigurePrimaryHttpMessageHandler`. Roboflow/DOODS2 plugins must do the same — use the named-options pattern, not constructor injection of the options into the registrar.

### 1.6 Test shape (clone for Roboflow + DOODS2 unit tests)

`tests/FrigateRelay.Plugins.CodeProjectAi.Tests/`:
- `CodeProjectAiValidatorTests.cs` — single test file with WireMock stubs for the upstream HTTP. Log assertions use the shared `FrigateRelay.TestHelpers.CapturingLogger<T>` per CLAUDE.md "Conventions" — NOT NSubstitute on `ILogger<T>`, which is fragile around generic-`TState` matching for the `Log<TState>` method.
- Tests assertions follow the contract: allow / reject (low confidence) / reject (label not allowed) / no snapshot / timeout (Fail-Closed default) / timeout (Fail-Open) / network unavailable (Fail-Closed) / network unavailable (Fail-Open) / cancellation (host shutdown propagates `OperationCanceledException`).
- **Test count baseline:** the existing CPAI test project ships 24 tests (per the CI run output of v1.1.0). Roboflow + DOODS2 should each ship at minimum 6 tests for the same paths; suggest 8-10 each.

---

## 2. Validator execution loop (the host code #23 modifies)

### 2.1 Where the validator chain lives

`src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs:200-246` — the per-action validator loop. Inside `ConsumeAsync` (the channel-reader loop, one task per consumer per plugin), per `DispatchItem`:

```csharp
if (item.Validators.Count > 0)
{
    var preResolved = await initial.ResolveAsync(item.Context, ct).ConfigureAwait(false);
    shared = new SnapshotContext(preResolved);

    foreach (var validator in item.Validators)
    {
        // validator.<name>.check span
        var validatorSpanName = $"validator.{validator.Name.ToLowerInvariant()}.check";
        using var vActivity = DispatcherDiagnostics.ActivitySource.StartActivity(
            validatorSpanName, ActivityKind.Internal);
        vActivity?.SetTag("event.id", item.Context.EventId);
        vActivity?.SetTag("validator", validator.Name);
        vActivity?.SetTag("action", plugin.Name);
        vActivity?.SetTag("subscription", item.Subscription);

        var verdict = await validator.ValidateAsync(item.Context, shared, ct).ConfigureAwait(false);
        // ... per-verdict tag/counter update
        if (verdict.Passed) DispatcherDiagnostics.IncrementValidatorsPassed(item, validator.Name);
        else {
            DispatcherDiagnostics.IncrementValidatorsRejected(item, validator.Name);
            LogValidatorRejected(...);
            goto NextItem;   // short-circuits THIS action only.
        }
    }
}
```

### 2.2 What #23 must change

The plan must:
1. Add a `bool ParallelValidators` field to `DispatchItem` (line 29-36 today) — populated from the new `ActionEntry.ParallelValidators` field at the EventPump match-time (`EventPump` constructs `DispatchItem` from `ActionEntry`). Find the EventPump construction site at PR-3 planning via `grep -n "new DispatchItem" src/FrigateRelay.Host/`.
2. Branch in `ChannelActionDispatcher.cs:209` based on `item.ParallelValidators`:
   - When `false`: existing sequential `foreach` with `goto NextItem` short-circuit.
   - When `true`: `Task.WhenAll(item.Validators.Select(v => RunOneValidator(v, item, shared, ct)))` — each `RunOneValidator` returns its own `Verdict` (or rethrows on host-shutdown cancellation). After all complete, AND the verdicts; if any rejected, increment counters for each rejecting validator (preserves per-validator visibility per CONTEXT-14 OQ-4) and `goto NextItem`. Activity spans for each validator still emit individually.

### 2.3 Per-validator timeout in parallel mode

Each validator's `HttpClient.Timeout` already enforces its own timeout — the dispatcher does NOT need to layer a `CancellationTokenSource` per validator. `Task.WhenAll` will surface each task's `TaskCanceledException` as the validator's individual `Verdict.Fail("validator_timeout")` per its own `OnError` config (the validator's own `catch (TaskCanceledException)` block returns the verdict; it does not throw to the dispatcher). This means parallel mode is *purely* a scheduling change — no new failure semantics.

### 2.4 Counter increments (no inventory change)

`DispatcherDiagnostics.IncrementValidatorsPassed/Rejected(item, validator.Name)` already exists (PLAN-1.1 / Phase 13). Each parallel branch calls these per-validator after `Task.WhenAll` completes. Counter inventory unchanged from Phase 13 — `CounterInventoryDriftTests` still passes without doc changes. Per CONTEXT-14 OQ-4, no aggregate `actions.rejected_by_validators` counter is added.

### 2.5 `ActionEntry` location

`src/FrigateRelay.Host/Configuration/ActionEntry.cs:30` — declared as `internal sealed record` in `FrigateRelay.Host.Configuration`. NOT in `FrigateRelay.Abstractions`. So `ParallelValidators` field lives in the host project, NOT the abstractions assembly. This is a host-internal concern; plugin authors don't need to know about it. Add the new field as an additional `record` parameter:

```csharp
internal sealed record ActionEntry(
    string Plugin,
    string? SnapshotProvider = null,
    IReadOnlyList<string>? Validators = null,
    bool ParallelValidators = false);   // NEW for #23
```

`ActionEntryJsonConverter` and `ActionEntryTypeConverter` (decorating the record at lines 28-29) must also be updated to pass through the new field. Find them at PR-3 planning via `find src/FrigateRelay.Host/Configuration -name "ActionEntry*.cs"`.

`DispatchItem` (in `Host.Dispatch`, internal readonly record struct) gets the same field — `EventPump` propagates `ActionEntry.ParallelValidators` → `DispatchItem.ParallelValidators` at construction time.

---

## 3. Plugin project structure & DI wiring

### 3.1 csproj location

All plugins go under `src/FrigateRelay.Plugins.<Name>/`. New projects:
- `src/FrigateRelay.Plugins.Roboflow/FrigateRelay.Plugins.Roboflow.csproj`
- `src/FrigateRelay.Plugins.Doods2/FrigateRelay.Plugins.Doods2.csproj`

Test projects:
- `tests/FrigateRelay.Plugins.Roboflow.Tests/FrigateRelay.Plugins.Roboflow.Tests.csproj`
- `tests/FrigateRelay.Plugins.Doods2.Tests/FrigateRelay.Plugins.Doods2.Tests.csproj`

### 3.2 Solution wiring

`FrigateRelay.sln` at repo root — new projects must be added via `dotnet sln add` for each. CI auto-discovers test projects via `find tests -maxdepth 2 -name '*Tests.csproj'` (per `.github/scripts/run-tests.sh`), so just dropping the test csproj at the right path is sufficient for CI.

### 3.3 DI wiring in `HostBootstrap.cs`

`src/FrigateRelay.Host/HostBootstrap.cs:120-138` — registrars are added explicitly to the list, gated by config-section presence. NOT reflection-based discovery. Plan must edit this file:

```csharp
List<IPluginRegistrar> registrars = [new FrigateRelay.Sources.FrigateMqtt.PluginRegistrar()];
if (builder.Configuration.GetSection("BlueIris").Exists())
    registrars.Add(new FrigateRelay.Plugins.BlueIris.PluginRegistrar());
if (builder.Configuration.GetSection("FrigateSnapshot").Exists())
    registrars.Add(new FrigateRelay.Plugins.FrigateSnapshot.PluginRegistrar());
if (builder.Configuration.GetSection("Pushover").Exists())
    registrars.Add(new FrigateRelay.Plugins.Pushover.PluginRegistrar());
// CPAI/Roboflow/DOODS2: only register when the top-level Validators section is present.
// Each registrar iterates that section and only acts on its own Type discriminator.
if (builder.Configuration.GetSection("Validators").Exists())
{
    registrars.Add(new FrigateRelay.Plugins.CodeProjectAi.PluginRegistrar());
    registrars.Add(new FrigateRelay.Plugins.Roboflow.PluginRegistrar());      // NEW for #13
    registrars.Add(new FrigateRelay.Plugins.Doods2.PluginRegistrar());        // NEW for #14
}
```

The `Validators` section gate is shared — all three validator plugins are registered together, each filters on its own `Type`. This avoids needing separate config sections for each validator type.

`FrigateRelay.Host.csproj` must add `<ProjectReference Include="..\FrigateRelay.Plugins.Roboflow\..." />` and same for DOODS2.

### 3.4 Config-shape contract

`appsettings.json` for the new validators:
```json
"Validators": {
    "roboflow_persons": {
        "Type": "Roboflow",
        "BaseUrl": "http://roboflow:9001",
        "ModelId": "rfdetr-base",
        "MinConfidence": 0.50,
        "AllowedLabels": ["person"],
        "OnError": "FailClosed",
        "Timeout": "00:00:05"
    },
    "doods_persons": {
        "Type": "Doods2",
        "Transport": "Http",
        "BaseUrl": "http://doods2:8080",
        "DetectorName": "default",
        "MinConfidence": 0.50,
        "AllowedLabels": ["person"],
        "OnError": "FailClosed",
        "Timeout": "00:00:05"
    }
}
```

`ActionEntry.Validators` references these by key: `"Validators": ["cpai", "roboflow_persons"]` etc.

---

## 4. gRPC integration plan (DOODS2 #14, NEW for this codebase)

### 4.1 Vendored .proto file

Per CONTEXT-14 OQ-1: vendor the upstream `.proto` at a pinned commit.

DOODS2 upstream: `https://github.com/snowzach/doods2`. The relevant proto is `odrpc/odrpc.proto` (Object Detection RPC). MIT-licensed (verify at PR-2 planning by reading `LICENSE` in the repo).

Place at: `src/FrigateRelay.Plugins.Doods2/Protos/odrpc.proto`. Add a header comment:
```proto
// Source: https://github.com/snowzach/doods2/blob/<COMMIT_SHA>/odrpc/odrpc.proto
// License: MIT (see https://github.com/snowzach/doods2/blob/<COMMIT_SHA>/LICENSE)
// Vendored at <DATE> for FrigateRelay.Plugins.Doods2.
```

PR-2 planning must capture the actual upstream commit SHA being vendored.

### 4.2 csproj for DOODS2 (gRPC additions)

`src/FrigateRelay.Plugins.Doods2/FrigateRelay.Plugins.Doods2.csproj` (extends the CPAI csproj shape):
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>FrigateRelay.Plugins.Doods2</RootNamespace>
    <AssemblyName>FrigateRelay.Plugins.Doods2</AssemblyName>
    <IsPackable>false</IsPackable>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\FrigateRelay.Abstractions\FrigateRelay.Abstractions.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Http" Version="10.0.7" />
    <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="10.0.7" />
    <PackageReference Include="Microsoft.Extensions.Options.DataAnnotations" Version="10.0.7" />
    <!-- gRPC, contained to this plugin only per CONTEXT-14 D4 + CLAUDE.md invariant -->
    <PackageReference Include="Grpc.Net.Client" Version="2.66.0" />
    <PackageReference Include="Grpc.Tools" Version="2.66.0">
        <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
        <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Google.Protobuf" Version="3.28.0" />
  </ItemGroup>
  <ItemGroup>
    <Protobuf Include="Protos\odrpc.proto" GrpcServices="Client" />
  </ItemGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="FrigateRelay.Plugins.Doods2.Tests" />
  </ItemGroup>
</Project>
```

`<Protobuf Include="..." GrpcServices="Client" />` triggers `Grpc.Tools` MSBuild codegen at build time. Generated client classes are placed in the project's namespace + the proto's `option csharp_namespace` (or `package` if none). PR-2 planning should add `option csharp_namespace = "FrigateRelay.Plugins.Doods2.Grpc";` to the vendored .proto if not already there.

`PrivateAssets="all"` on `Grpc.Tools` ensures the codegen package is build-time only — does NOT propagate to consumers (here, the host). Combined with the plugin-only project reference, the host's transitive package list stays gRPC-free. The architectural invariant is verified by:
```bash
dotnet list src/FrigateRelay.Host/FrigateRelay.Host.csproj package --include-transitive | grep -E 'Grpc\.' || echo "PASS"
```

### 4.3 In-process gRPC test server pattern

`Grpc.AspNetCore.Server` (NOT `Grpc.Net.Client`) plus `Microsoft.AspNetCore.TestHost` is the canonical .NET 10 in-process test pattern. Test csproj adds:
```xml
<PackageReference Include="Grpc.AspNetCore.Server" Version="2.66.0" />
<PackageReference Include="Microsoft.AspNetCore.TestHost" Version="10.0.7" />
<Protobuf Include="..\..\src\FrigateRelay.Plugins.Doods2\Protos\odrpc.proto" GrpcServices="Server" />
```

Test setup pattern: `WebApplicationFactory<Program>` is heavyweight; for plugin tests, use `Grpc.AspNetCore.Web.GrpcWebExtensions` + minimal API host that registers a fake `Detector.DetectorBase` (the auto-generated server-base abstract class), then connect the validator's `GrpcChannel` to the in-process `HttpClient` from `WebApplicationFactory.CreateClient()` configured with the test server's `BaseAddress`.

PR-2 planning should sketch this concretely with one full test as exemplar — the architect should NOT pre-decide the exact testing harness without verifying the .NET 10 ergonomics first.

---

## 5. Testcontainers usage in this codebase

`tests/FrigateRelay.IntegrationTests/Fixtures/MosquittoFixture.cs:1-31` — exemplar pattern:

```csharp
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

internal sealed class MosquittoFixture : IAsyncDisposable
{
    private readonly IContainer _container;

    public MosquittoFixture()
    {
        var conf = "listener 1883\nallow_anonymous true\n";
        _container = new ContainerBuilder("eclipse-mosquitto:2")
            .WithPortBinding(1883, true)
            .WithResourceMapping(Encoding.UTF8.GetBytes(conf), "/mosquitto/config/mosquitto.conf")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilExternalTcpPortIsAvailable(1883))
            .Build();
    }

    public string Hostname => _container.Hostname;
    public int Port => _container.GetMappedPublicPort(1883);

    public ValueTask InitializeAsync() => new(_container.StartAsync());
    public async ValueTask DisposeAsync() => await _container.DisposeAsync().ConfigureAwait(false);
}
```

Notes:
- Testcontainers 4.10+ requires explicit image at `ContainerBuilder` construction (parameterless ctor + chained `.WithImage(...)` is obsolete and slated for removal).
- `Wait.ForUnixContainer().UntilExternalTcpPortIsAvailable(...)` is the current API (renamed from `UntilPortIsAvailable` in 4.7.0; old name removed in a later patch).
- Image must be on Docker Hub (or another registry); CI runners pull at test-start time. Each test class should own its fixture or use class-fixture sharing — Mosquitto's startup is ~3-5s.

`tests/FrigateRelay.IntegrationTests/MqttToBlueIrisSliceTests.cs` and similar use `MosquittoFixture` paired with WireMock for the downstream HTTP. PR-3's parallel-validator integration test should follow this pattern: real Mosquitto + WireMock for CPAI + WireMock for Roboflow (DOODS2 likely WireMock + in-process gRPC depending on which transport).

---

## 6. OQ-2 result — Roboflow Testcontainers feasibility: **NOT VIABLE for CI integration tests**

### Evidence

- **`docker pull roboflow/inference`** → fails: `pull access denied for roboflow/inference, repository does not exist`. The plain name does not exist; the modern image is `roboflow/roboflow-inference-server-cpu`.
- **`docker pull roboflow/roboflow-inference-server-cpu:latest`** → succeeds, **16.7 GB**. Pull alone exceeds GitHub Actions Linux runner default disk-free budget (~14 GB on `ubuntu-latest`). The image is a fully-loaded ML inference server (PyTorch + ONNX runtime + multiple model archives + a web UI).
- **Boot time**: not measured — image size alone disqualifies it. A typical FastAPI+PyTorch model-loaded boot is 30-90 seconds, but the dominant cost in CI is the pull, not the boot.
- **The image likely requires API-key authentication** for Roboflow Cloud model downloads even in self-hosted mode (the published "self-hosted" images bundle a license-server check by default). This was not verified — the operator-facing config shape captured in CONTEXT-14 D2 explicitly excludes the Cloud API surface; this PR-1 should not gate on it either.

### Recommendation

**PR-1 ships WireMock-only unit + integration tests; no Testcontainers integration test for Roboflow.**

PR-1's `README.md` (or the CHANGELOG entry under `Added`) should document a manual-smoke recipe for operators who want to verify against a real Roboflow Inference container locally. Suggest:

```bash
# Example manual smoke (operator runs locally — not in CI):
docker run --rm -p 9001:9001 \
    -e ROBOFLOW_API_KEY=... \
    roboflow/roboflow-inference-server-cpu:latest

# In another terminal:
curl -X POST http://localhost:9001/<model_id>/<version>?api_key=... \
    -F file=@test.jpg
```

This keeps PR-1 small and CI-fast; defers any real-container coverage to operator-driven local validation.

---

## 7. External APIs

### 7.1 Roboflow Inference HTTP API (#13)

Self-hosted endpoint: `http://<host>:9001/<workspace>/<model_id>/<version>` for hosted-model inference, or `/infer/object_detection` for direct ONNX/RF-DETR routing.

Modern API (post-2024): `POST /infer/object_detection` with body:
```json
{
    "model_id": "rfdetr-base/1",
    "image": { "type": "base64", "value": "<base64-jpeg>" },
    "confidence": 0.5
}
```

Response shape:
```json
{
    "image": { "width": 640, "height": 480 },
    "predictions": [
        { "x": 320, "y": 240, "width": 50, "height": 100, "class": "person", "confidence": 0.92, "class_id": 0 }
    ],
    "time": 0.143
}
```

PR-1 planning must verify the exact endpoint shape against the upstream docs at `https://inference.roboflow.com/` (or whichever URL the architect picks at PR time) — the API surface evolves and v0.x → v1.x had a path change. WireMock stubs in PR-1 should mock whatever shape the implementation calls.

### 7.2 DOODS2 HTTP API (#14)

`POST /detect` with body:
```json
{
    "detector_name": "default",
    "data": "<base64-jpeg>",
    "preprocess": [],
    "detect": { "*": 0.5 }
}
```

Response:
```json
{
    "id": "...",
    "detections": [
        { "top": 100, "left": 200, "bottom": 300, "right": 400, "label": "person", "confidence": 92.5 }
    ]
}
```

Note: confidence is **0-100** scale in DOODS2, not 0-1. The plugin must normalize `confidence / 100.0` before comparing to `MinConfidence` (which is 0-1 per CPAI's contract).

### 7.3 DOODS2 gRPC API (#14)

The vendored `.proto` at `src/FrigateRelay.Plugins.Doods2/Protos/odrpc.proto` defines a service:
```proto
service Detector {
    rpc Detect (DetectRequest) returns (DetectResponse);
}
```
with `DetectRequest` carrying `bytes data`, `string detector_name`, `map<string,float> detect` (label-confidence threshold map), and `DetectResponse` carrying `repeated Detection detections` with the same field shape as the HTTP response.

The `Grpc.Tools` MSBuild codegen produces a `Detector.DetectorClient` typed client. The plugin's gRPC code path:
```csharp
using var channel = GrpcChannel.ForAddress(opts.BaseUrl);   // or reused per-instance
var client = new Detector.DetectorClient(channel);
var response = await client.DetectAsync(request, cancellationToken: ct);
```

Per-instance `GrpcChannel` lifetime should match the validator instance lifetime (singleton — `AddKeyedSingleton`). `GrpcChannel` is thread-safe and HTTP/2-multiplexed; reuse is correct.

---

## 8. Test count baseline + per-PR new-test minimums

### Baseline

Post-Phase 13 (commit `5197492` after ID-29 hotfix): **242 tests**, 0 warnings, all passing.

Verified via:
```bash
bash .github/scripts/run-tests.sh --skip-integration | grep total: | awk '{ sum += $2 } END { print sum }'
# → 242
```

### Per-PR minimum suggestions

| PR | New project | New tests minimum | Cumulative total |
|---|---|---|---|
| PR-1 (#13 Roboflow) | `tests/FrigateRelay.Plugins.Roboflow.Tests/` | **8** (allow / reject low-confidence / reject not-allowed-label / no-snapshot / timeout-FailClosed / timeout-FailOpen / unavailable-FailClosed / cancellation) | 250 |
| PR-2 (#14 DOODS2) | `tests/FrigateRelay.Plugins.Doods2.Tests/` | **12** (HTTP path: 6 from PR-1 list × 1 + transport-config-validation × 1 = 7; gRPC path: 5 covering happy + reject + timeout + unavailable + cancellation) | 262 |
| PR-3 (#23 parallel) | `tests/FrigateRelay.Host.Tests/` (existing) + `tests/FrigateRelay.IntegrationTests/` (existing) | **6** (sequential default unchanged / parallel happy / parallel any-reject / parallel any-timeout-FailClosed / parallel cancellation propagates / per-validator counter still emitted in parallel mode) plus **1** integration test exercising ≥ 2 validators concurrently end-to-end | 269 |

**Phase 14 final test target: 269 tests** (242 + 8 + 12 + 7).

Architect should treat these as floors, not ceilings — additional defensive tests welcome, but each plan must hit at least its row's count.

---

## Concerns flagged for the architect

1. **gRPC test harness ergonomics in .NET 10 are unverified.** The `Grpc.AspNetCore.Server` + `WebApplicationFactory` pattern is the canonical .NET 10 approach but may need adjustment for this codebase's MSTest v3 + class-fixture pattern. PR-2 planning should sketch one complete in-process gRPC test as a proof-of-shape before fanning out to the full test set.
2. **DOODS2 `.proto` upstream commit not yet pinned.** PR-2 planning must capture the actual upstream commit SHA being vendored (`https://github.com/snowzach/doods2`, `odrpc/odrpc.proto`). Recommend the most recent commit on `master` at PR-2 planning time.
3. **Roboflow API surface volatility.** The exact endpoint shape (hosted-model URL vs direct-infer URL) and request body have changed across versions. PR-1 planning should pin against the latest stable docs at planning time and document the version in `RoboflowOptions` XML doc-comments.
4. **OQ-2 fallback documentation.** PR-1's CHANGELOG entry should explicitly note "Roboflow integration test uses WireMock; manual-smoke recipe in README" so operators understand the coverage scope.
