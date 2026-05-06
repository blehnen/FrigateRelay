---
phase: 14-v1.2-inference-engines-parallel-validation
plan: 2.1
wave: 2
dependencies: [1.1, 1.2]
must_haves:
  - New project src/FrigateRelay.Plugins.Doods2 with Options/Response/Validator/Registrar + vendored .proto
  - Both transports (HTTP and gRPC) implemented and operator-selectable via Doods2Options.Transport
  - Grpc.Net.Client + Google.Protobuf + Grpc.Tools contained to this plugin only (architectural invariant)
  - DI wiring in HostBootstrap.cs (registrar added under existing Validators-section gate)
  - Build clean (warnings-as-errors); zero new warnings; gRPC codegen runs at build time
files_touched:
  - src/FrigateRelay.Plugins.Doods2/FrigateRelay.Plugins.Doods2.csproj
  - src/FrigateRelay.Plugins.Doods2/Protos/odrpc.proto
  - src/FrigateRelay.Plugins.Doods2/Doods2Options.cs
  - src/FrigateRelay.Plugins.Doods2/Doods2Response.cs
  - src/FrigateRelay.Plugins.Doods2/Doods2Validator.cs
  - src/FrigateRelay.Plugins.Doods2/PluginRegistrar.cs
  - src/FrigateRelay.Host/HostBootstrap.cs
  - src/FrigateRelay.Host/FrigateRelay.Host.csproj
  - FrigateRelay.sln
tdd: false
risk: medium
---

# Plan 2.1: DOODS2 plugin scaffold + dual-transport implementation + DI wiring (PR #14 — DOODS2 validator)

## Context

Issue #14 ships a self-hosted DOODS2 `IValidationPlugin` with **both** HTTP and gRPC transports, operator-selectable per validator instance via `Doods2Options.Transport: "Http" | "Grpc"` (CONTEXT-14 D4). User explicitly chose both transports despite extra surface — gRPC is "quite quicker than http" on the hot path.

This is the highest-risk plan in Phase 14: gRPC dependencies (`Grpc.Net.Client`, `Google.Protobuf`, `Grpc.Tools` codegen) are a NEW dep family for this codebase, and the architectural invariant from CLAUDE.md / CONTEXT-14 is that they live **only** in `FrigateRelay.Plugins.Doods2` — never in `FrigateRelay.Abstractions`, never in `FrigateRelay.Host`. Verification commands in this plan's Verification section are non-negotiable gates.

The vendored `.proto` (CONTEXT-14 OQ-1: vendor at a pinned commit, NOT submodule) is sourced from `https://github.com/snowzach/doods2`, file `odrpc/odrpc.proto`. The DOODS2 HTTP API (`POST /detect`) and gRPC service (`Detector.Detect`) carry the same conceptual payload (RESEARCH §7.2 / §7.3). DOODS2 confidence values are **0-100 scale**, NOT 0-1 — the validator must normalize `confidence / 100.0` before comparing to `MinConfidence` (RESEARCH §7.2 final note). This is the chief implementation gotcha vs. CPAI/Roboflow.

## Dependencies

- **PLAN-1.1, PLAN-1.2** — Wave 2 strictly follows Wave 1 per CONTEXT-14 D1 (sequential PRs against `main`). PR-1 must be merged before PR-2 opens.

## Tasks

### Task 1: Vendor odrpc.proto + create DOODS2 csproj with gRPC codegen

**Files:**
- `src/FrigateRelay.Plugins.Doods2/FrigateRelay.Plugins.Doods2.csproj` (new)
- `src/FrigateRelay.Plugins.Doods2/Protos/odrpc.proto` (new — vendored)

**Action:** create

**Description:**

**Vendor the .proto** per CONTEXT-14 OQ-1: copy `odrpc/odrpc.proto` from `https://github.com/snowzach/doods2` at the most recent commit on `master` at PR time. The vendored file MUST start with this header comment block (RESEARCH §4.1):

```proto
// Source: https://github.com/snowzach/doods2/blob/<COMMIT_SHA>/odrpc/odrpc.proto
// License: MIT (see https://github.com/snowzach/doods2/blob/<COMMIT_SHA>/LICENSE)
// Vendored at <YYYY-MM-DD> for FrigateRelay.Plugins.Doods2.
//
// This file is the contract between FrigateRelay's DOODS2 validator and the upstream
// DOODS2 server. Updates are deliberate: re-vendor at a newer commit when the upstream
// API evolves. Dependabot does not watch this file (NuGet only).

syntax = "proto3";
option csharp_namespace = "FrigateRelay.Plugins.Doods2.Grpc";
package odrpc;
// ... rest of upstream content verbatim
```

The `option csharp_namespace = "FrigateRelay.Plugins.Doods2.Grpc";` line MUST be present so generated client code lives in a stable namespace under the plugin. If the upstream file already defines a different `csharp_namespace`, replace it with this one. The builder must capture the actual `<COMMIT_SHA>` and `<YYYY-MM-DD>` at execution time (no placeholders left in the committed file).

Also vendor the upstream `LICENSE` text snippet (or a clear MIT attribution) inline at the bottom of the proto-header block — single-source license attribution (CONTEXT-14 OQ-1 rationale: vendoring is simpler for license attribution).

**Create the csproj** matching the shape in RESEARCH §4.2:

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
    <!-- gRPC, contained to this plugin only per CONTEXT-14 D4 + CLAUDE.md invariant.
         Grpc.Tools is build-only (PrivateAssets=all) so codegen does NOT propagate to consumers. -->
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

Verify the actual current versions for `Grpc.Net.Client`, `Grpc.Tools`, and `Google.Protobuf` at PR-2 execution time (use Context7 MCP if needed). The versions in this plan are RESEARCH-time guesses; the builder picks current stable.

`PrivateAssets="all"` on `Grpc.Tools` is critical (RESEARCH §4.2): build-time codegen does NOT propagate transitively, so the host's `dotnet list package --include-transitive` stays gRPC-free.

Add to solution: `dotnet sln FrigateRelay.sln add src/FrigateRelay.Plugins.Doods2/FrigateRelay.Plugins.Doods2.csproj`.

**Acceptance Criteria:**
- `dotnet build src/FrigateRelay.Plugins.Doods2/FrigateRelay.Plugins.Doods2.csproj -c Release` succeeds with zero warnings; build output shows `Grpc.Tools` codegen ran (look for `Detector.cs` / `Odrpc.cs` in obj/Release/).
- The vendored proto file's first comment lines name a real commit SHA (40 hex chars) and a real date (`grep -E '^// Source: https://github.com/snowzach/doods2/blob/[0-9a-f]{40}/' src/FrigateRelay.Plugins.Doods2/Protos/odrpc.proto`).
- `option csharp_namespace = "FrigateRelay.Plugins.Doods2.Grpc";` is present in the proto.
- Generated gRPC types are accessible from C# at namespace `FrigateRelay.Plugins.Doods2.Grpc.Detector` (the auto-generated client class).
- **gRPC dep containment** (architectural invariant — load-bearing):
  ```bash
  dotnet list src/FrigateRelay.Abstractions/FrigateRelay.Abstractions.csproj package --include-transitive | grep -E '^\s*(>|>) Grpc\.' && exit 1 || true
  dotnet list src/FrigateRelay.Host/FrigateRelay.Host.csproj package --include-transitive | grep -E '^\s*(>|>) Grpc\.' && exit 1 || true
  ```
  Both must return zero `Grpc.*` matches.

---

### Task 2: Implement Doods2Options + Doods2Response + Doods2Validator (HTTP and gRPC paths)

**Files:**
- `src/FrigateRelay.Plugins.Doods2/Doods2Options.cs` (new)
- `src/FrigateRelay.Plugins.Doods2/Doods2Response.cs` (new — HTTP DTO)
- `src/FrigateRelay.Plugins.Doods2/Doods2Validator.cs` (new)

**Action:** create

**Description:**

**`Doods2Options.cs`** — clone `RoboflowOptions.cs` (PLAN-1.1 Task 1) shape and add:
- `[Required, Url] string BaseUrl = ""`
- **`Doods2Transport Transport = Doods2Transport.Http`** (NEW — selects HTTP vs gRPC; per CONTEXT-14 D4)
- `[Required] string DetectorName = "default"` (DOODS2-specific; `default` matches the `detector_name` field in RESEARCH §7.2)
- `[Range(0.0, 1.0)] double MinConfidence = 0.5` (operator-facing; 0-1 scale — the validator handles the DOODS2 0-100 normalization internally per RESEARCH §7.2)
- `string[] AllowedLabels = []`
- `Doods2ValidatorErrorMode OnError = FailClosed`
- `[Range(typeof(TimeSpan), "00:00:01", "00:01:00")] TimeSpan Timeout = TimeSpan.FromSeconds(5)`
- `bool AllowInvalidCertificates = false` (HTTP transport only; documented as such in XML doc-comments — gRPC transport ignores this in v1.2; if operators need TLS-skip on gRPC, file a follow-up)

Define `enum Doods2Transport { Http, Grpc }` and `enum Doods2ValidatorErrorMode { FailClosed, FailOpen }` in the same file (both `public`).

**`Doods2Response.cs`** — DTO for HTTP transport per RESEARCH §7.2:
```csharp
internal sealed record Doods2HttpResponse(
    string? Id,
    IReadOnlyList<Doods2Detection>? Detections);

internal sealed record Doods2Detection(
    int Top, int Left, int Bottom, int Right,
    string Label,
    double Confidence);   // 0-100 scale per DOODS2; validator normalizes
```

**`Doods2Validator.cs`** — `public sealed partial class Doods2Validator : IValidationPlugin`. Constructor:
```csharp
public Doods2Validator(
    string name,
    Doods2Options opts,
    HttpClient http,                                  // used by HTTP transport
    Grpc.Detector.DetectorClient grpcClient,          // used by gRPC transport
    ILogger<Doods2Validator> logger)
```

Both transport clients are constructor-injected. The registrar (Task 3) builds whichever is needed; for instances in HTTP mode the gRPC client is constructed with a no-op channel (or null with nullable reference annotation) and never used. **Cleaner alternative the builder may pick instead:** two private factory methods, one per transport, that read `opts.Transport` and dispatch — keep both clients on the type to avoid runtime branching cost. Pick whichever produces less ceremony; document the choice in a class-level remark.

`ValidateAsync`:

```csharp
public async Task<Verdict> ValidateAsync(EventContext ctx, SnapshotContext snapshot, CancellationToken ct)
{
    var snap = await snapshot.ResolveAsync(ctx, ct).ConfigureAwait(false);
    if (snap is null) return Verdict.Fail("validator_no_snapshot");

    try
    {
        var detections = _opts.Transport == Doods2Transport.Grpc
            ? await DetectGrpcAsync(snap.Bytes, ct).ConfigureAwait(false)
            : await DetectHttpAsync(snap.Bytes, ct).ConfigureAwait(false);
        return EvaluateDetections(detections);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
    catch (TaskCanceledException ex) { Log.Doods2ValidatorTimeout(_logger, _name, ctx.EventId, ex); return _opts.OnError == Doods2ValidatorErrorMode.FailOpen ? Verdict.Pass() : Verdict.Fail("validator_timeout"); }
    catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.DeadlineExceeded) { /* same as TaskCanceledException — gRPC's deadline-exceeded is the gRPC equivalent of HTTP timeout */ Log.Doods2ValidatorTimeout(_logger, _name, ctx.EventId, ex); return _opts.OnError == Doods2ValidatorErrorMode.FailOpen ? Verdict.Pass() : Verdict.Fail("validator_timeout"); }
    catch (Grpc.Core.RpcException ex) { Log.Doods2ValidatorUnavailable(_logger, _name, ctx.EventId, ex); return _opts.OnError == Doods2ValidatorErrorMode.FailOpen ? Verdict.Pass() : Verdict.Fail($"validator_unavailable: {ex.Message}"); }
    catch (HttpRequestException ex) { Log.Doods2ValidatorUnavailable(_logger, _name, ctx.EventId, ex); return _opts.OnError == Doods2ValidatorErrorMode.FailOpen ? Verdict.Pass() : Verdict.Fail($"validator_unavailable: {ex.Message}"); }
}
```

Catch-block ordering MUST be preserved: `OperationCanceledException when ct.IsCancellationRequested` first (host shutdown rethrow); then `TaskCanceledException` (HTTP timeout); then `RpcException` with `StatusCode.DeadlineExceeded` (gRPC timeout — same OnError mapping); then generic `RpcException` (gRPC unavailable); then `HttpRequestException` (HTTP unavailable). RESEARCH §1.4 catch-ordering invariant carries forward verbatim with the gRPC additions.

`DetectHttpAsync(ReadOnlyMemory<byte> bytes, ct)`:
- Build JSON body: `{"detector_name": opts.DetectorName, "data": Convert.ToBase64String(bytes.Span), "preprocess": [], "detect": {"*": opts.MinConfidence}}` (RESEARCH §7.2).
- `_http.PostAsJsonAsync("/detect", body, ct)`; `EnsureSuccessStatusCode`; `ReadFromJsonAsync<Doods2HttpResponse>`.
- Return `body?.Detections ?? Array.Empty<Doods2Detection>()` (or equivalent).

`DetectGrpcAsync(ReadOnlyMemory<byte> bytes, ct)`:
- Construct `DetectRequest` per RESEARCH §7.3: `Data = ByteString.CopyFrom(bytes.Span)`, `DetectorName = opts.DetectorName`, `Detect.Add("*", (float)opts.MinConfidence)`.
- Call `await _grpcClient.DetectAsync(request, deadline: DateTime.UtcNow.Add(opts.Timeout), cancellationToken: ct)`.
- Project the response's `Detections` list onto the same `Doods2Detection` shape so `EvaluateDetections` is transport-agnostic. (Field names in the generated proto types may differ slightly from the C# DTO — map them explicitly.)

`EvaluateDetections(IReadOnlyList<Doods2Detection> detections)`:
- If empty: `Verdict.Fail("validator_no_predictions")`.
- For each detection: **normalize confidence**: `var normalized = d.Confidence / 100.0;` (DOODS2 returns 0-100 per RESEARCH §7.2 — this is the chief gotcha). If `normalized >= _opts.MinConfidence` and (`_opts.AllowedLabels.Length == 0` || allowed-list contains `d.Label` ordinal-ignore-case): `return Verdict.Pass(normalized)`.
- On no match: `Verdict.Fail($"validator_no_match: minConfidence={...}, allowedLabels=[{...}]")`.

`LoggerMessage` — use **EventId range 7200s** (RESEARCH §1.4): `Doods2ValidatorTimeout` (7201) and `Doods2ValidatorUnavailable` (7202), both `LogLevel.Warning`. The gRPC-deadline-exceeded path reuses `Doods2ValidatorTimeout` (semantically equivalent).

**Acceptance Criteria:**
- `dotnet build src/FrigateRelay.Plugins.Doods2/FrigateRelay.Plugins.Doods2.csproj -c Release` succeeds with zero warnings.
- `Doods2Validator.ValidateAsync` exists with `EventContext`, `SnapshotContext`, `CancellationToken` signature (matches `IValidationPlugin` contract).
- The DOODS2 0-100 → 0-1 confidence normalization is present: `git grep -n 'Confidence.*/.*100' src/FrigateRelay.Plugins.Doods2/Doods2Validator.cs` returns at least one match.
- `LoggerMessage` event IDs are 7201 and 7202 (no overlap with CPAI 7001/7002 or Roboflow 7101/7102).
- Catch-block ordering: `OperationCanceledException` first, `TaskCanceledException` second, `RpcException(DeadlineExceeded)` third, generic `RpcException` fourth, `HttpRequestException` fifth (verifiable by reading the file or via an analyzer; primary check is that the test suite in PLAN-2.2 + PLAN-2.3 passes).

---

### Task 3: Implement Doods2 PluginRegistrar + wire into HostBootstrap + Host.csproj

**Files:**
- `src/FrigateRelay.Plugins.Doods2/PluginRegistrar.cs` (new)
- `src/FrigateRelay.Host/HostBootstrap.cs`
- `src/FrigateRelay.Host/FrigateRelay.Host.csproj`

**Action:** create + modify

**Description:**

**`PluginRegistrar.cs`** — the seven-step CPAI ritual (RESEARCH §1.5) with `Type == "Doods2"` discriminator and these transport-specific additions:

1. Filter on `Type == "Doods2"` ordinal compare.
2. Capture `instanceKey = instance.Key`.
3. `AddOptions<Doods2Options>(instanceKey).Bind(instance).ValidateDataAnnotations().ValidateOnStart()`.
4. **HTTP client registration** (always registered, used in HTTP mode):
   ```csharp
   var httpClientName = $"Doods2:{instanceKey}";
   context.Services.AddHttpClient(httpClientName)
       .ConfigurePrimaryHttpMessageHandler(sp => /* same TLS-skip pattern as CPAI/Roboflow */);
   ```
5. **gRPC channel registration** (always registered, used in gRPC mode). Register a singleton factory keyed on `instanceKey` that builds a `GrpcChannel` and a `Detector.DetectorClient`:
   ```csharp
   context.Services.AddKeyedSingleton<Grpc.Detector.DetectorClient>(instanceKey, (sp, key) =>
   {
       var name = (string)key!;
       var opts = sp.GetRequiredService<IOptionsMonitor<Doods2Options>>().Get(name);
       var channel = GrpcChannel.ForAddress(opts.BaseUrl);   // GrpcChannel is thread-safe + HTTP/2-multiplexed; reuse-as-singleton is correct (RESEARCH §7.3 final note).
       return new Grpc.Detector.DetectorClient(channel);
   });
   ```
   Even in HTTP mode the gRPC client is constructed (cheap — `GrpcChannel.ForAddress` is lazy until first call). The validator never invokes it. This keeps the registrar branch-free; PR-2 builder may instead conditionally register only the chosen transport's client based on `opts.Transport` if they prefer — both shapes are acceptable.
6. **Validator keyed singleton:**
   ```csharp
   context.Services.AddKeyedSingleton<IValidationPlugin>(instanceKey, (sp, key) =>
   {
       var name = (string)key!;
       var opts = sp.GetRequiredService<IOptionsMonitor<Doods2Options>>().Get(name);
       var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient($"Doods2:{name}");
       http.BaseAddress = new Uri(opts.BaseUrl);
       http.Timeout = opts.Timeout;
       var grpcClient = sp.GetRequiredKeyedService<Grpc.Detector.DetectorClient>(name);
       var logger = sp.GetRequiredService<ILogger<Doods2Validator>>();
       return new Doods2Validator(name, opts, http, grpcClient, logger);
   });
   ```

XML doc-comments mirror CPAI's (no retry, fail-open/closed semantics, transport-selection rationale per CONTEXT-14 D4).

**`FrigateRelay.Host.csproj`** — add `<ProjectReference Include="..\FrigateRelay.Plugins.Doods2\FrigateRelay.Plugins.Doods2.csproj" />` in the same `<ItemGroup>` as the existing CPAI/Roboflow ProjectReferences.

**`HostBootstrap.cs`** — extend the `if (builder.Configuration.GetSection("Validators").Exists())` block (now at the position from PLAN-1.1 Task 3) to register DOODS2 after Roboflow:
```csharp
if (builder.Configuration.GetSection("Validators").Exists())
{
    registrars.Add(new FrigateRelay.Plugins.CodeProjectAi.PluginRegistrar());
    registrars.Add(new FrigateRelay.Plugins.Roboflow.PluginRegistrar());
    registrars.Add(new FrigateRelay.Plugins.Doods2.PluginRegistrar());   // NEW for #14
}
```

DO NOT add a separate top-level config section gate. All validator-type plugins share the `Validators` section gate; each registrar filters on its own `Type` discriminator.

**Acceptance Criteria:**
- `dotnet build FrigateRelay.sln -c Release` zero warnings.
- `dotnet sln FrigateRelay.sln list | grep -i Doods2` returns the new project path.
- `git grep -n 'FrigateRelay.Plugins.Doods2.PluginRegistrar' src/FrigateRelay.Host/HostBootstrap.cs` returns one match.
- The registrar uses `GrpcChannel.ForAddress(opts.BaseUrl)` (single use site; reuse via keyed singleton).
- **gRPC dep containment — non-negotiable**:
  ```bash
  dotnet list src/FrigateRelay.Abstractions/FrigateRelay.Abstractions.csproj package --include-transitive | grep -E 'Grpc\.' && exit 1 || true
  dotnet list src/FrigateRelay.Host/FrigateRelay.Host.csproj package --include-transitive | grep -E 'Grpc\.' && exit 1 || true
  ```
  Both must show ZERO `Grpc.*` entries. The gRPC dep is plugin-local only.

## Verification

```bash
# Build clean — gRPC codegen must run and succeed
dotnet build FrigateRelay.sln -c Release

# gRPC dep containment — load-bearing architectural invariant (CONTEXT-14 + CLAUDE.md)
dotnet list src/FrigateRelay.Abstractions/FrigateRelay.Abstractions.csproj package --include-transitive | grep -E 'Grpc\.' && { echo "FAIL: Grpc leaked into Abstractions"; exit 1; } || echo "PASS: Abstractions gRPC-free"
dotnet list src/FrigateRelay.Host/FrigateRelay.Host.csproj package --include-transitive | grep -E 'Grpc\.' && { echo "FAIL: Grpc leaked into Host"; exit 1; } || echo "PASS: Host gRPC-free"

# Source-level gRPC containment
git grep -nE 'Grpc\.' src/FrigateRelay.Host src/FrigateRelay.Abstractions   # must be empty
git grep -nE 'Grpc\.' src/FrigateRelay.Plugins.Doods2/                      # MUST have matches (this is where it lives)

# Vendored proto attribution
grep -E '^// Source: https://github.com/snowzach/doods2/blob/[0-9a-f]{40}/' src/FrigateRelay.Plugins.Doods2/Protos/odrpc.proto
grep -E '^// License: MIT' src/FrigateRelay.Plugins.Doods2/Protos/odrpc.proto
grep -E 'option csharp_namespace = "FrigateRelay.Plugins.Doods2.Grpc"' src/FrigateRelay.Plugins.Doods2/Protos/odrpc.proto

# Architectural invariants
git grep -nE 'App\.Metrics|OpenTracing|Jaeger\.' src/                # must be empty
git grep -nE '\.(Result|Wait)\(' src/                                 # must be empty
git grep -n 'ServicePointManager' src/                                # must be empty
git grep -nE '192\.168\.|10\.0\.0\.|172\.(1[6-9]|2[0-9]|3[01])\.' src/   # must be empty

# Solution + DI wiring
dotnet sln FrigateRelay.sln list | grep -i 'FrigateRelay.Plugins.Doods2'
git grep -n 'FrigateRelay.Plugins.Doods2.PluginRegistrar' src/FrigateRelay.Host/HostBootstrap.cs

# Existing tests still pass — host + earlier plugins unaffected
dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build
dotnet run --project tests/FrigateRelay.Plugins.CodeProjectAi.Tests -c Release --no-build
dotnet run --project tests/FrigateRelay.Plugins.Roboflow.Tests -c Release --no-build
```
