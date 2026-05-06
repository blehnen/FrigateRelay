# Build Summary: Plan 1.1

## Status: complete

## Tasks Completed

- **Task 1: Scaffold `FrigateRelay.Plugins.Roboflow` project** — complete — files: `src/FrigateRelay.Plugins.Roboflow/{FrigateRelay.Plugins.Roboflow.csproj,RoboflowOptions.cs,RoboflowResponse.cs,RoboflowValidator.cs}`. Commit `b0048de`.
- **Task 2: Roboflow `PluginRegistrar`** — complete — file: `src/FrigateRelay.Plugins.Roboflow/PluginRegistrar.cs`. Commit `281aac2`.
- **Task 3: Wire into Host DI + solution** — complete — files: `FrigateRelay.sln` (added project), `src/FrigateRelay.Host/FrigateRelay.Host.csproj` (added ProjectReference), `src/FrigateRelay.Host/HostBootstrap.cs` (added registrar to the `Validators`-section-gated list at line 134 alongside CPAI). Commit `0340e5b`.

## Files Modified

- `src/FrigateRelay.Plugins.Roboflow/FrigateRelay.Plugins.Roboflow.csproj` (created) — Microsoft.NET.Sdk; same package set as CPAI (`Microsoft.Extensions.Http`, `Options.ConfigurationExtensions`, `Options.DataAnnotations`); `<InternalsVisibleTo Include="FrigateRelay.Plugins.Roboflow.Tests" />`.
- `src/FrigateRelay.Plugins.Roboflow/RoboflowOptions.cs` (created) — `[Required, Url] BaseUrl`, `[Required] ModelId`, `[Range(0.0, 1.0)] MinConfidence = 0.5`, `string[] AllowedLabels = []`, `RoboflowValidatorErrorMode OnError = FailClosed`, `[Range(typeof(TimeSpan), "00:00:01", "00:01:00")] Timeout = 5s`, `bool AllowInvalidCertificates`.
- `src/FrigateRelay.Plugins.Roboflow/RoboflowResponse.cs` (created) — DTOs for `{predictions: [{x,y,width,height,class,confidence,class_id}], image: {width,height}, time}`. Records used; `[JsonPropertyName]` per field.
- `src/FrigateRelay.Plugins.Roboflow/RoboflowValidator.cs` (created) — `IValidationPlugin` with `ValidateAsync(EventContext, SnapshotContext, CancellationToken)`; `POST /infer/object_detection` via `_http.PostAsJsonAsync`; canonical catch-block ordering (`OperationCanceledException when ct.IsCancellationRequested` → throw, then `TaskCanceledException` → timeout per OnError, then `HttpRequestException` → unavailable per OnError); `LoggerMessage` partial class with EventIds 7100–7101 (Roboflow's range per RESEARCH §1.4).
- `src/FrigateRelay.Plugins.Roboflow/PluginRegistrar.cs` (created) — clones the CPAI registrar pattern verbatim: enumerate `Validators` section, filter by `Type == "Roboflow"`, bind named options with `ValidateDataAnnotations().ValidateOnStart()`, register typed `HttpClient` with per-instance TLS-skip handler, register `IValidationPlugin` as `AddKeyedSingleton` keyed by the instance name. NO `AddResilienceHandler` (asymmetric with action plugins per CONTEXT-7 D4 — validator-pattern invariant).
- `FrigateRelay.sln` — added `src/FrigateRelay.Plugins.Roboflow/FrigateRelay.Plugins.Roboflow.csproj` to the solution.
- `src/FrigateRelay.Host/FrigateRelay.Host.csproj` — added `<ProjectReference Include="..\FrigateRelay.Plugins.Roboflow\FrigateRelay.Plugins.Roboflow.csproj" />`.
- `src/FrigateRelay.Host/HostBootstrap.cs:134` — added `registrars.Add(new FrigateRelay.Plugins.Roboflow.PluginRegistrar());` inside the existing `if (builder.Configuration.GetSection("Validators").Exists())` gate. CPAI and Roboflow share the gate; each registrar filters on its own `Type` discriminator.

## Decisions Made

- **`Type` discriminator value: "Roboflow"** (matches `appsettings.json` operator-facing config: `Validators:<name>:Type: "Roboflow"`). String-equality `Ordinal` to match CPAI's pattern (case-sensitive — operators must spell it exactly).
- **EventId range 7101–7102 for `LoggerMessage`s** — per RESEARCH §1.4 convention, CPAI uses 7001–7099, Roboflow uses 7100–7199. Picked 7101 (timeout) and 7102 (unavailable). DOODS2 (PR-2) should start at 7201/7202 in its 7200–7299 range.
- **API endpoint `POST /infer/object_detection`** verified against a live Roboflow Inference v1.2.7 instance (Apache 2.0) — the orchestrator probed `http://192.168.0.2:19004/openapi.json` and confirmed the request schema (`ObjectDetectionInferenceRequest`: `model_id`, `image: InferenceRequestImage{type,value}`, `confidence`, optional `api_key`) matches the validator's `RoboflowRequest` record exactly. No code adjustment needed from RESEARCH §7.1's documented shape.
- **`InferenceRequestImage.type = "base64"`** chosen over `"url"` or `"numpy"` because the validator already has the snapshot bytes in-hand (from `SnapshotContext.ResolveAsync`) — no need to host the image at a URL or pickle a numpy array.
- **Confidence scale 0.0–1.0** — Roboflow native scale, no normalization needed (unlike DOODS2 which uses 0–100).
- **No Roboflow-specific NuGet packages.** Hand-rolled HTTP DTOs via `System.Text.Json`. Matches the project's "no Newtonsoft.Json, no client SDKs" stance.

## Issues Encountered

- **Builder agent had no outbound HTTP access.** When PLAN-1.1 instructed verification of the API shape against the live Roboflow instance at `192.168.0.2:19004`, the builder's sandbox blocked `curl`. Workaround: orchestrator probed the live `/openapi.json` (239 KB), parsed via inline Python, confirmed the schema match, and forwarded the result via `SendMessage`. **Lesson seed:** for future plans that require live-API verification, the orchestrator should pre-fetch and embed the relevant API surface in the builder's prompt rather than instructing the builder to call out — sandbox restrictions are unpredictable.
- **Builder agent's bash also blocked at end of run.** After the HTTP block, the builder lost the ability to run any shell commands at all (couldn't `git commit`, `dotnet build`, or write files). The orchestrator finished Task 3, ran the full verification battery, and wrote this SUMMARY-1.1.md directly. **Lesson seed:** builder agents in this environment are unreliable for sustained multi-step work; the orchestrator should be prepared to take over verification + summary writing as a fallback.

## Verification Results

- `dotnet build FrigateRelay.sln -c Release` — **0 warnings, 0 errors**, 14.4s elapsed.
- `bash .github/scripts/run-tests.sh --skip-integration` — **242/242 passing, 0 failures**, baseline test count unchanged (PLAN-1.1 ships no tests; PLAN-1.2 will add them).
- `git grep -nE 'Grpc\.' src/` — empty ✓ (gRPC arrives only with PR-2 / DOODS2).
- `git grep -nE 'App\.Metrics|OpenTracing|Jaeger\.' src/` — empty ✓.
- `git grep -nE '\.(Result|Wait)\(' src/` — empty ✓.
- `git grep -n 'ServicePointManager' src/` — three doc-comment hits ("never use ServicePointManager") in `CodeProjectAiOptions.cs:75`, `RoboflowOptions.cs:71`, and `FrigateMqttEventSource.cs:26`. All are negative references in XML doc-comments enforcing the invariant — no actual usage. ✓
- `git grep -nE '192\.168\.|10\.0\.|172\.(1[6-9]|2[0-9]|3[0-1])\.' src/` — only NuGet package version-number false positives (e.g. `Version="10.0.7"`). No actual hard-coded IPs. ✓
- Three atomic commits on `feature/13-roboflow-validator`: `b0048de` (scaffold), `281aac2` (registrar), `0340e5b` (DI + solution wiring).
