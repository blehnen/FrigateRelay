# Review: Plan 1.1

## Verdict: PASS

## Stage 1 (Correctness) check results

- **HTTP shape — endpoint, request body, response DTO.** PASS. `RoboflowValidator.cs:72` posts to `/infer/object_detection` (relative; `BaseAddress` set by registrar). `RoboflowRequest` record in `RoboflowResponse.cs:31-34` carries `model_id`, `image {type, value}`, `confidence` matching RESEARCH §7.1's `ObjectDetectionInferenceRequest` schema. `RoboflowPrediction` maps `class → Label` via `[JsonPropertyName("class")]` at `RoboflowResponse.cs:22`. All field names match.

- **Catch-block ordering.** PASS. `RoboflowValidator.cs:77-95`: `OperationCanceledException when ct.IsCancellationRequested` (rethrow) is first; `TaskCanceledException` (timeout) is second; `HttpRequestException` (unavailable) is third. Exact parity with CPAI `CodeProjectAiValidator.cs:66-84`. Host-shutdown propagation is correct.

- **Confidence scale — no normalization.** PASS. `RoboflowValidator.cs:105` compares `p.Confidence < _opts.MinConfidence` directly. No division by 100. `RoboflowResponse.cs` doc-comment explicitly states 0.0–1.0 range; no normalization applied.

- **LoggerMessage EventId range 7100s.** PASS (code differs from SUMMARY, spec is correct). `RoboflowValidator.cs:117` = `EventId = 7101` (Timeout); `RoboflowValidator.cs:121` = `EventId = 7102` (Unavailable). This matches the spec's "7101/7102" language exactly. The SUMMARY-1.1 incorrectly states "7100–7101" — the summary has a transcription error but the code is correct. No overlap with CPAI's 7001/7002.

- **PluginRegistrar — CPAI ritual cloned correctly.** PASS. `PluginRegistrar.cs:32-33` bails if `Validators` section absent. `PluginRegistrar.cs:38` filters `string.Equals(type, "Roboflow", StringComparison.Ordinal)`. `PluginRegistrar.cs:43` captures `instanceKey` local (closure-safety). Named options wired at `PluginRegistrar.cs:46-50`. Per-instance `HttpClient` with TLS-skip at `PluginRegistrar.cs:54-72`. `AddKeyedSingleton` at `PluginRegistrar.cs:75-84`. All seven steps present.

- **`#pragma warning disable CA5359` for opt-in TLS bypass.** PASS. `PluginRegistrar.cs:66-69` wraps the `RemoteCertificateValidationCallback` assignment with `#pragma warning disable/restore CA5359`. Matches CPAI registrar pattern.

- **No `AddResilienceHandler`.** PASS. `PluginRegistrar.cs` contains no `AddResilienceHandler` call. `git grep -n 'AddResilienceHandler' src/FrigateRelay.Plugins.Roboflow/` per SUMMARY returns empty.

- **`HttpClient.BaseAddress` and `Timeout` set in keyed-singleton factory, not handler config.** PASS. `PluginRegistrar.cs:80-81`: `http.BaseAddress = new Uri(opts.BaseUrl); http.Timeout = opts.Timeout;` inside the `AddKeyedSingleton` factory. Matches RESEARCH §1.5 critical detail.

- **Options class annotations.** PASS. `RoboflowOptions.cs`: `[Required, Url] BaseUrl`, `[Required] ModelId`, `[Range(0.0, 1.0)] MinConfidence = 0.5`, `AllowedLabels = []`, `OnError = FailClosed`, `[Range(typeof(TimeSpan), "00:00:01", "00:01:00")] Timeout = 5s`, `AllowInvalidCertificates`. All seven fields per spec, all annotations present.

- **DI wiring — HostBootstrap.cs.** PASS. `HostBootstrap.cs:132-136`: both CPAI and Roboflow registrars added inside the existing `if (builder.Configuration.GetSection("Validators").Exists())` gate. Single-line `if` correctly expanded to a brace block. No separate top-level `"Roboflow"` section gate introduced.

- **csproj — solution + Host.csproj reference.** PASS. `FrigateRelay.Plugins.Roboflow.csproj` follows the CPAI shape verbatim: `Microsoft.Extensions.Http`, `Options.ConfigurationExtensions`, `Options.DataAnnotations` all at `Version="10.0.7"`. `InternalsVisibleTo` points at `FrigateRelay.Plugins.Roboflow.Tests`. `Host.csproj:43` contains the new `ProjectReference`.

- **`snap.Bytes` usage — `Convert.ToBase64String`.** PASS. `SnapshotResult.Bytes` is `byte[]` (not `ReadOnlyMemory<byte>`). The spec cited `.Span` but that was for a `ReadOnlyMemory<byte>` variant; the actual type supports `Convert.ToBase64String(snap.Bytes)` directly with no allocation overhead from `.ToArray()`. Correct.

- **`RoboflowValidator.Name` returns constructor argument.** PASS. `RoboflowValidator.cs:55`: `public string Name => _name;` where `_name` is set in ctor from the `name` parameter.

- **No `Grpc.*`, `App.Metrics`, `OpenTracing`, `Jaeger.*`, `.Result`, `.Wait`, `ServicePointManager` (as usage).** PASS. `ServicePointManager` appears only in XML doc-comments at `RoboflowOptions.cs:71` as a prohibition, not as code. SUMMARY confirms all invariant greps pass.

- **`EvaluatePredictions` logic.** PASS. `RoboflowValidator.cs:98-113`: null/empty predictions → `Verdict.Fail("validator_no_predictions")`; confidence gate + label gate (case-insensitive ordinal `Any`); first match → `Verdict.Pass(p.Confidence)`; no match → `Verdict.Fail(...)` with `minConfidence` and `allowedLabels` in message. Matches spec exactly.

- **Build + test gate.** PASS per SUMMARY: 0 warnings, 0 errors; 242/242 tests passing (unchanged from baseline — PLAN-1.1 adds no tests, correct).

- **Type discriminator collision check.** PASS. `"Roboflow"` (ordinal) does not collide with CPAI's `"CodeProjectAi"`.

- **XML doc-comments reference CONTEXT-14 D2/D3 and no-retry contract.** PASS. `RoboflowOptions.cs:20-27` covers D2/D3. `RoboflowValidator.cs:19-26` covers no-retry and catch-block ordering rationale.

## Stage 2 (Integration) check results

- **SOLID — Single Responsibility.** PASS. `RoboflowValidator` does one thing (HTTP call + prediction eval). `PluginRegistrar` does one thing (DI registration). `RoboflowOptions` is pure data.

- **Error handling and edge cases.** PASS. Null snapshot → early return. Null/empty predictions list handled. `response.EnsureSuccessStatusCode()` before deserialization means non-2xx status codes surface as `HttpRequestException` and are caught by the third catch-block. `ReadFromJsonAsync` returns `null` on empty body, handled by null-check in `EvaluatePredictions`. All catch paths honor `OnError`.

- **`InternalsVisibleTo` form.** PASS. `FrigateRelay.Plugins.Roboflow.csproj:21` uses the MSBuild item form, not source-level attribute.

- **No `DynamicProxyGenAssembly2` entry needed yet.** PASS. No internal types are mocked in PLAN-1.1 (no tests ship); the entry will be needed in PLAN-1.2.

- **Package versions match CPAI.** PASS. All three `Microsoft.Extensions.*` packages at `10.0.7`.

- **No hard-coded IPs/hostnames.** PASS. No IP literals anywhere in the new files. Doc-comments use the placeholder `http://roboflow:9001` as an example only (not a real host, acceptable).

- **`PostAsJsonAsync` vs CPAI's `PostAsync`.** PASS variant. Roboflow uses JSON body (not multipart); `PostAsJsonAsync` is the correct method. CPAI uses `MultipartFormDataContent` because the CPAI endpoint requires it. The difference is intentional and correct.

## Findings

### Critical

None.

### Minor

- **`EvaluatePredictions` uses `LINQ .Any()` on `AllowedLabels` inside a hot loop** (`RoboflowValidator.cs:107`). For the common case of a small `AllowedLabels` array (2–5 labels) this is fine, but it creates an enumerator allocation per prediction per call. CPAI has the same pattern — this is a pre-existing minor perf note, not a regression. A `HashSet<string>` built once in the constructor (with `StringComparer.OrdinalIgnoreCase`) would eliminate the allocations. Not worth blocking this PR; flag for PLAN-1.2 or later when tests can cover the change.

- **SUMMARY-1.1 EventId transcription error** (`SUMMARY-1.1.md:16`). States "EventIds 7100–7101" but the code correctly uses 7101/7102. The summary is the wrong artifact to trust; the code is authoritative. No code action needed, but a future builder reading only the summary would start DOODS2 at 7102 and collide with Roboflow's `ValidatorUnavailable`. The summary should be corrected or DOODS2's PLAN should be explicit about using 7201/7202.

### Positive

- Catch-block ordering is exactly right and the XML doc-comment explicitly explains WHY the ordering matters (`RoboflowValidator.cs:23-26`). This is the kind of defensive documentation that prevents a future refactor from silently breaking host-shutdown propagation.
- Closure-safety local capture (`instanceKey` at `PluginRegistrar.cs:43`) is present and commented — future maintainers will not introduce the classic `foreach` closure bug.
- `#pragma warning disable CA5359` is scoped tightly (two lines only, not the whole method or file) with a comment referencing the architectural invariant. Correct pattern for optional-TLS-bypass in this codebase.
- `ValidateOnStart()` on all named-options bindings means misconfigured validators surface at host startup (instant operator feedback), not at first dispatch.

## Final verdict

All 18 spec checks pass. The implementation is a faithful clone of the CPAI pattern with the correct Roboflow-specific differences: JSON body instead of multipart, per-instance `ModelId`, EventIds 7101/7102, and direct 0-1 confidence comparison. The only items worth noting are a pre-existing LINQ allocation pattern inherited from CPAI and a transcription error in the summary document — neither blocks merge.
