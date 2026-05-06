---
phase: 14-v1.2-inference-engines-parallel-validation
plan: 1.1
wave: 1
dependencies: []
must_haves:
  - New project src/FrigateRelay.Plugins.Roboflow with RoboflowOptions, RoboflowResponse, RoboflowValidator, PluginRegistrar
  - DI wiring in HostBootstrap.cs (registrar added under existing Validators-section gate)
  - Project + reference added to FrigateRelay.sln and FrigateRelay.Host.csproj
  - Build clean (warnings-as-errors) on Linux + Windows; zero new warnings
files_touched:
  - src/FrigateRelay.Plugins.Roboflow/FrigateRelay.Plugins.Roboflow.csproj
  - src/FrigateRelay.Plugins.Roboflow/RoboflowOptions.cs
  - src/FrigateRelay.Plugins.Roboflow/RoboflowResponse.cs
  - src/FrigateRelay.Plugins.Roboflow/RoboflowValidator.cs
  - src/FrigateRelay.Plugins.Roboflow/PluginRegistrar.cs
  - src/FrigateRelay.Host/HostBootstrap.cs
  - src/FrigateRelay.Host/FrigateRelay.Host.csproj
  - FrigateRelay.sln
tdd: false
risk: low
---

# Plan 1.1: Roboflow plugin scaffold + DI wiring (PR #13 — Roboflow Inference validator)

## Context

Issue #13 ships a self-hosted Roboflow Inference `IValidationPlugin`. CONTEXT-14 D2 locks scope to the self-hosted endpoint shape (`http://roboflow:9001`) — no Roboflow Hosted Cloud API in v1.2. CONTEXT-14 D3 locks per-instance `ModelId` (operators declare multiple validator instances, e.g. `roboflow_persons`, `roboflow_vehicles`, for per-camera model selection).

The CPAI plugin is the canonical clone target (RESEARCH §1). Project layout (RESEARCH §1.1), csproj shape (RESEARCH §1.2), options shape (RESEARCH §1.3), validator shape (RESEARCH §1.4), and registrar ritual (RESEARCH §1.5) all transfer verbatim. The HTTP API surface for Roboflow Inference is `POST /infer/object_detection` with a base64-encoded image body and a `predictions` response array (RESEARCH §7.1). DI wiring lives in `HostBootstrap.cs:120-138` (RESEARCH §3.3) — the registrar is added under the existing `Validators` config-section gate so the three validator plugins (CPAI, Roboflow, DOODS2) coexist.

Per CONTEXT-14 OQ-2 (resolved by RESEARCH §6: NOT VIABLE), this PR ships **WireMock-only** unit tests with no Testcontainers integration test — the upstream `roboflow/roboflow-inference-server-cpu` image is 16.7 GB and exceeds the GitHub Actions Linux runner's disk-free budget. PLAN-1.2 captures the manual-smoke recipe in the CHANGELOG bullet.

## Dependencies

None. Wave 1 plan; depends on no other Phase 14 work.

## Tasks

### Task 1: Create FrigateRelay.Plugins.Roboflow project + Options/Response/Validator

**Files:**
- `src/FrigateRelay.Plugins.Roboflow/FrigateRelay.Plugins.Roboflow.csproj` (new)
- `src/FrigateRelay.Plugins.Roboflow/RoboflowOptions.cs` (new)
- `src/FrigateRelay.Plugins.Roboflow/RoboflowResponse.cs` (new)
- `src/FrigateRelay.Plugins.Roboflow/RoboflowValidator.cs` (new)

**Action:** create

**Description:**

Clone the CPAI shape verbatim (RESEARCH §1.2 — §1.4) with these Roboflow-specific differences:

**`FrigateRelay.Plugins.Roboflow.csproj`** — identical to `src/FrigateRelay.Plugins.CodeProjectAi/FrigateRelay.Plugins.CodeProjectAi.csproj:1-24` except for `RootNamespace`, `AssemblyName`, and `InternalsVisibleTo` (point at `FrigateRelay.Plugins.Roboflow.Tests`). Same three `Microsoft.Extensions.*` package references at `Version="10.0.7"`. NO `Grpc.*` packages — Roboflow is HTTP-only.

**`RoboflowOptions.cs`** — clone `CodeProjectAiOptions.cs:43-78` (RESEARCH §1.3) and add per-instance `ModelId` per CONTEXT-14 D3:
- `[Required, Url] string BaseUrl = ""` (e.g. `"http://roboflow:9001"`)
- `[Required] string ModelId = ""` (e.g. `"rfdetr-base/1"` — model id + version, slash-delimited, per RESEARCH §7.1)
- `[Range(0.0, 1.0)] double MinConfidence = 0.5`
- `string[] AllowedLabels = []` (empty = any label passes)
- `RoboflowValidatorErrorMode OnError = FailClosed`
- `[Range(typeof(TimeSpan), "00:00:01", "00:01:00")] TimeSpan Timeout = TimeSpan.FromSeconds(5)`
- `bool AllowInvalidCertificates = false`

Define `enum RoboflowValidatorErrorMode { FailClosed, FailOpen }` (per-plugin enum — no shared abstraction in v1.2 per RESEARCH §1.3).

XML doc-comments must capture the v1.2 API contract: self-hosted only (link CONTEXT-14 D2), `ModelId` is per-instance and includes the version suffix.

**`RoboflowResponse.cs`** — DTO matching the response shape in RESEARCH §7.1:
```csharp
internal sealed record RoboflowResponse(
    RoboflowImage? Image,
    IReadOnlyList<RoboflowPrediction>? Predictions,
    double Time);

internal sealed record RoboflowImage(int Width, int Height);

internal sealed record RoboflowPrediction(
    [property: JsonPropertyName("class")] string Label,
    double Confidence,
    int? ClassId,
    double X, double Y, double Width, double Height);
```
Use `System.Text.Json.Serialization.JsonPropertyName` to map the protobuf-style `class` field name onto the C# `Label` property (avoids the C# keyword collision).

**`RoboflowValidator.cs`** — clone `CodeProjectAiValidator.cs` verbatim with these differences:
- Constructor signature identical: `(string name, RoboflowOptions opts, HttpClient http, ILogger<RoboflowValidator> logger)` (RESEARCH §1.4 line 36).
- Snapshot resolution at the entry: `var snap = await snapshot.ResolveAsync(ctx, ct).ConfigureAwait(false); if (snap is null) return Verdict.Fail("validator_no_snapshot");` (RESEARCH §1.4).
- HTTP body: build a JSON `RoboflowRequest` with `model_id = opts.ModelId`, `image = { type: "base64", value: <Convert.ToBase64String(snap.Bytes.Span)> }`, `confidence = opts.MinConfidence` (RESEARCH §7.1). POST to `/infer/object_detection` (relative path; `BaseAddress` set by registrar).
- Catch-block ordering identical to CPAI (RESEARCH §1.4 lines 66-84): `OperationCanceledException when ct.IsCancellationRequested` first → rethrow; `TaskCanceledException` second → timeout, honors `OnError`; `HttpRequestException` third → unavailable, honors `OnError`.
- `LoggerMessage` source-generated logging — use **EventId range 7100s** (RESEARCH §1.4 — CPAI uses 7001/7002, Roboflow takes 7101/7102, DOODS2 will take 7201/7202). Two events: `RoboflowValidatorTimeout` (EventId=7101) and `RoboflowValidatorUnavailable` (EventId=7102), both `LogLevel.Warning`.
- Confidence is already 0-1 in Roboflow's response (per RESEARCH §7.1 — `0.92` for a 92% prediction); compare directly to `opts.MinConfidence`. **No `/100.0` normalization** (that's DOODS2's quirk in PLAN-2.x).
- `EvaluatePredictions` filters predictions by `MinConfidence` and `AllowedLabels` (case-insensitive ordinal); on first match returns `Verdict.Pass(p.Confidence)`. On no match: `Verdict.Fail($"validator_no_match: minConfidence={...}, allowedLabels=[{...}]")`. On null/empty predictions list: `Verdict.Fail("validator_no_predictions")`.

The class is `public sealed partial class RoboflowValidator : IValidationPlugin` to match CPAI's visibility convention (RESEARCH §1.4 line 24). XML doc-comments mention CONTEXT-14 D2/D3 and the no-retry contract (RESEARCH §1.4 / CPAI XML doc lines 14-18).

**Acceptance Criteria:**
- New csproj compiles standalone: `dotnet build src/FrigateRelay.Plugins.Roboflow/FrigateRelay.Plugins.Roboflow.csproj -c Release` succeeds with zero warnings.
- `RoboflowValidator.Name` returns the constructor `name` argument (matches `IValidationPlugin.Name` contract).
- No `Grpc.*` package references in the new csproj (`grep -i 'Grpc' src/FrigateRelay.Plugins.Roboflow/FrigateRelay.Plugins.Roboflow.csproj` returns empty).
- No `ServicePointManager`, no `.Result`/`.Wait()`, no `App.Metrics`/`OpenTracing`/`Jaeger.` references in any new file.
- LoggerMessage event IDs are 7101 and 7102 (no overlap with CPAI's 7001/7002).

---

### Task 2: Create PluginRegistrar with named-options + keyed-singleton ritual

**Files:** `src/FrigateRelay.Plugins.Roboflow/PluginRegistrar.cs` (new)

**Action:** create

**Description:**

Clone `src/FrigateRelay.Plugins.CodeProjectAi/PluginRegistrar.cs:30-86` verbatim (RESEARCH §1.5) with the type discriminator changed from `"CodeProjectAi"` to `"Roboflow"` and the named `HttpClient` prefix changed from `"CodeProjectAi:"` to `"Roboflow:"`.

The registration ritual MUST follow the seven-step CPAI pattern exactly:
1. Get `Validators` config section; bail if not present.
2. Iterate children; filter by `Type == "Roboflow"` (ordinal compare per RESEARCH §1.5 line 38-39).
3. Capture `instance.Key` to a local for closure-safety (RESEARCH §1.5 line 43).
4. `AddOptions<RoboflowOptions>(instanceKey).Bind(instance).ValidateDataAnnotations().ValidateOnStart()`.
5. `AddHttpClient($"Roboflow:{instanceKey}").ConfigurePrimaryHttpMessageHandler(sp => ...)` — per-instance `SocketsHttpHandler` with optional TLS bypass via `SslOptions.RemoteCertificateValidationCallback` (CPAI registrar lines 53-72). The `#pragma warning disable CA5359` block is required for the opt-in callback (CPAI lines 66-69).
6. `AddKeyedSingleton<IValidationPlugin>(instanceKey, (sp, key) => ...)` — factory resolves `IOptionsMonitor<RoboflowOptions>.Get(name)`, `IHttpClientFactory.CreateClient($"Roboflow:{name}")`, sets `http.BaseAddress = new Uri(opts.BaseUrl); http.Timeout = opts.Timeout;` (RESEARCH §1.5 lines 80-81), constructs `RoboflowValidator`.

The registrar class is `public sealed class PluginRegistrar : IPluginRegistrar`. XML doc-comments mention the asymmetric no-retry contract (RESEARCH §1.5 / CPAI registrar lines 19-25). NEVER use `ServicePointManager` (CLAUDE.md invariant).

**Acceptance Criteria:**
- `dotnet build src/FrigateRelay.Plugins.Roboflow/FrigateRelay.Plugins.Roboflow.csproj -c Release` succeeds.
- `git grep -n 'ServicePointManager' src/FrigateRelay.Plugins.Roboflow/` returns empty.
- `git grep -n 'AddResilienceHandler' src/FrigateRelay.Plugins.Roboflow/` returns empty (validators do NOT retry — CPAI parity).
- Registrar filters on `Type == "Roboflow"` ordinal-compare (one `string.Equals(type, "Roboflow", StringComparison.Ordinal)` call).

---

### Task 3: Wire Roboflow into solution + HostBootstrap + Host.csproj reference

**Files:**
- `FrigateRelay.sln`
- `src/FrigateRelay.Host/FrigateRelay.Host.csproj`
- `src/FrigateRelay.Host/HostBootstrap.cs`

**Action:** modify

**Description:**

1. Add the new project to the solution: `dotnet sln FrigateRelay.sln add src/FrigateRelay.Plugins.Roboflow/FrigateRelay.Plugins.Roboflow.csproj`.
2. Add a `<ProjectReference Include="..\FrigateRelay.Plugins.Roboflow\FrigateRelay.Plugins.Roboflow.csproj" />` to `src/FrigateRelay.Host/FrigateRelay.Host.csproj` in the same `<ItemGroup>` block as the existing CPAI ProjectReference. Match its style verbatim.
3. In `src/FrigateRelay.Host/HostBootstrap.cs:133-134` (the `if (builder.Configuration.GetSection("Validators").Exists())` block — RESEARCH §3.3), add the Roboflow registrar **after** the CPAI registrar inside the same `if` block:
   ```csharp
   if (builder.Configuration.GetSection("Validators").Exists())
   {
       registrars.Add(new FrigateRelay.Plugins.CodeProjectAi.PluginRegistrar());
       registrars.Add(new FrigateRelay.Plugins.Roboflow.PluginRegistrar());   // NEW for #13
   }
   ```
   Convert the single-line `if` (currently `if (...) registrars.Add(...);`) to a brace block. Update the surrounding inline comment to match the multi-validator pattern in RESEARCH §3.3 lines 190-191.

DO NOT add a separate top-level config section gate (e.g. `GetSection("Roboflow")`). All validator-type plugins share the `Validators` section gate; each registrar filters on its own `Type` discriminator (RESEARCH §3.3 explanation lines 196-198).

**Acceptance Criteria:**
- `dotnet build FrigateRelay.sln -c Release` succeeds with zero warnings.
- `dotnet sln FrigateRelay.sln list | grep -i Roboflow` returns the new project path.
- `git grep -n 'FrigateRelay.Plugins.Roboflow' src/FrigateRelay.Host/HostBootstrap.cs` returns one match (the registrars.Add line).
- Existing CPAI registration remains intact: `git grep -n 'FrigateRelay.Plugins.CodeProjectAi.PluginRegistrar' src/FrigateRelay.Host/HostBootstrap.cs` still returns one match.
- Existing host tests pass: `dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build` exits 0.

## Verification

```bash
# Build clean
dotnet build FrigateRelay.sln -c Release

# Architectural invariants — no new violations introduced
git grep -nE 'App\.Metrics|OpenTracing|Jaeger\.' src/                   # must be empty
git grep -nE '\.(Result|Wait)\(' src/                                    # must be empty
git grep -n 'ServicePointManager' src/                                   # must be empty
git grep -nE '192\.168\.|10\.0\.0\.|172\.(1[6-9]|2[0-9]|3[01])\.' src/   # must be empty (no hard-coded IPs)

# gRPC dep containment — Roboflow is HTTP-only; no Grpc references anywhere new
git grep -n 'Grpc\.' src/FrigateRelay.Plugins.Roboflow/                  # must be empty
git grep -n 'Grpc\.' src/FrigateRelay.Host/                              # must still be empty (host stays gRPC-free)

# Solution wiring
dotnet sln FrigateRelay.sln list | grep -i 'FrigateRelay.Plugins.Roboflow'

# DI wiring
git grep -n 'FrigateRelay.Plugins.Roboflow.PluginRegistrar' src/FrigateRelay.Host/HostBootstrap.cs

# Existing tests still pass — no regressions to the host or CPAI from the wiring change
dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build
dotnet run --project tests/FrigateRelay.Plugins.CodeProjectAi.Tests -c Release --no-build
```
