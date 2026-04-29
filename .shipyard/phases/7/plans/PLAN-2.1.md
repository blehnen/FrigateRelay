---
plan_id: 7.2.1
title: FrigateRelay.Plugins.CodeProjectAi project + validator + registrar
wave: 2
plan: 1
dependencies: ["1.1", "1.2"]
files_touched:
  - src/FrigateRelay.Plugins.CodeProjectAi/FrigateRelay.Plugins.CodeProjectAi.csproj
  - src/FrigateRelay.Plugins.CodeProjectAi/CodeProjectAiOptions.cs
  - src/FrigateRelay.Plugins.CodeProjectAi/CodeProjectAiResponse.cs
  - src/FrigateRelay.Plugins.CodeProjectAi/CodeProjectAiValidator.cs
  - src/FrigateRelay.Plugins.CodeProjectAi/PluginRegistrar.cs
  - tests/FrigateRelay.Plugins.CodeProjectAi.Tests/FrigateRelay.Plugins.CodeProjectAi.Tests.csproj
  - tests/FrigateRelay.Plugins.CodeProjectAi.Tests/CodeProjectAiValidatorTests.cs
  - FrigateRelay.sln
tdd: true
estimated_tasks: 3
---

# Plan 2.1: FrigateRelay.Plugins.CodeProjectAi project + validator + registrar

## Context
Wave 2 builds the first `IValidationPlugin` implementation. Spec is fully locked: CONTEXT-7 D5 fixes the options shape, D4 fixes the OnError stance (no Polly retry handler — asymmetric with action plugins), D8 fixes per-instance TLS skipping, D10/D12 fix the multipart wire format. RESEARCH §1 has the API cookbook, §2 has the keyed-services + named-options registrar pattern, §6 has the OnError catch-block recipe. The closest precedent is Phase 6's `FrigateRelay.Plugins.Pushover` (see `.shipyard/phases/6/results/SUMMARY-1.2.md` and `SUMMARY-2.1.md`).

## Dependencies
- PLAN-1.1: `IValidationPlugin.ValidateAsync(EventContext, SnapshotContext, CancellationToken)` signature must exist.
- PLAN-1.2: `ActionEntry.Validators` field exists (referenced by registrar startup-validation downstream — but not by this plan's code).

## Tasks

### Task 1: Create project shell + options + DTOs
**Files:**
- `src/FrigateRelay.Plugins.CodeProjectAi/FrigateRelay.Plugins.CodeProjectAi.csproj`
- `src/FrigateRelay.Plugins.CodeProjectAi/CodeProjectAiOptions.cs`
- `src/FrigateRelay.Plugins.CodeProjectAi/CodeProjectAiResponse.cs`
- `src/FrigateRelay.Plugins.CodeProjectAi/CodeProjectAiValidator.cs` (stub)
- `src/FrigateRelay.Plugins.CodeProjectAi/PluginRegistrar.cs` (stub)
- `tests/FrigateRelay.Plugins.CodeProjectAi.Tests/FrigateRelay.Plugins.CodeProjectAi.Tests.csproj`
- `FrigateRelay.sln`
**Action:** create
**Description:**

**csproj** (mirror `FrigateRelay.Plugins.Pushover.csproj`):
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\FrigateRelay.Abstractions\FrigateRelay.Abstractions.csproj" />
    <InternalsVisibleTo Include="FrigateRelay.Plugins.CodeProjectAi.Tests" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Http" />
    <PackageReference Include="Microsoft.Extensions.Options" />
    <PackageReference Include="Microsoft.Extensions.Options.DataAnnotations" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
  </ItemGroup>
</Project>
```

**Explicit `<TargetFramework>net10.0</TargetFramework>`** per ID-3 advisory (do NOT rely on `Directory.Build.props` alone for plugin projects).

**`CodeProjectAiOptions.cs`** — copy CONTEXT-7 D5 verbatim (BaseUrl, MinConfidence, AllowedLabels, OnError, Timeout, AllowInvalidCertificates) plus the `ValidatorErrorMode { FailClosed, FailOpen }` enum.

**`CodeProjectAiResponse.cs`** — internal DTOs per RESEARCH §7-2:
```csharp
internal sealed record CodeProjectAiResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("predictions")] IReadOnlyList<CodeProjectAiPrediction>? Predictions,
    [property: JsonPropertyName("code")] int Code);

internal sealed record CodeProjectAiPrediction(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("x_min")] int XMin,
    [property: JsonPropertyName("y_min")] int YMin,
    [property: JsonPropertyName("x_max")] int XMax,
    [property: JsonPropertyName("y_max")] int YMax);
```

Empty stubs for `CodeProjectAiValidator` and `PluginRegistrar` (just enough to compile).

**Test csproj** — mirror `tests/FrigateRelay.Plugins.Pushover.Tests/`. References:
- `<ProjectReference Include="..\..\src\FrigateRelay.Plugins.CodeProjectAi\FrigateRelay.Plugins.CodeProjectAi.csproj" />`
- `<ProjectReference Include="..\FrigateRelay.TestHelpers\FrigateRelay.TestHelpers.csproj" />`
- MSTest, MTP, FluentAssertions 6.12.2 (PINNED), NSubstitute, WireMock.Net.
- `<OutputType>Exe</OutputType>`.
- `Usings.cs` with `global using FrigateRelay.TestHelpers;` (CapturingLogger access).

Add both projects to `FrigateRelay.sln`.

**Verify the auto-discovery glob:**
- `.github/scripts/run-tests.sh` — if it does `find tests/*.Tests/*.Tests.csproj`, no edit needed (Phase 3 made this auto-discover).
- `.github/workflows/ci.yml` — confirm via Read; if it hardcodes per-project `dotnet run` lines, the architect was wrong about auto-discovery. CLAUDE.md says: "Both files currently hard-code the two test projects. When a third project lands (Phase 3), the architect should consider extracting a shared `.github/scripts/run-tests.sh`." Read both and act accordingly: if hardcoded, append a third `dotnet run --project tests/FrigateRelay.Plugins.CodeProjectAi.Tests -c Release` step to `ci.yml` AND a mirrored `sh 'dotnet run … -- --coverage …'` step to `Jenkinsfile`. (This is the third occurrence — Rule of Three triggers; consider extracting `run-tests.sh` if not already done.)

**Acceptance criteria:**
- `dotnet build FrigateRelay.sln -c Release` clean (with empty validator + registrar stubs).
- `dotnet run --project tests/FrigateRelay.Plugins.CodeProjectAi.Tests -c Release` runs (zero tests yet — exits 0).
- `git grep -n InternalsVisibleTo src/FrigateRelay.Plugins.CodeProjectAi/FrigateRelay.Plugins.CodeProjectAi.csproj` matches the test assembly.
- Solution loads in `dotnet sln list` showing both new projects.

### Task 2: Implement `CodeProjectAiValidator.ValidateAsync` (TDD — primary work of this plan)
**Files:** `src/FrigateRelay.Plugins.CodeProjectAi/CodeProjectAiValidator.cs`, `tests/FrigateRelay.Plugins.CodeProjectAi.Tests/CodeProjectAiValidatorTests.cs`
**Action:** create + test
**Description:**

**Tests first** (8 tests, WireMock.Net for HTTP stubbing — WireMock.Net pattern matches `PushoverActionPluginTests` from Phase 6 SUMMARY-1.2; Read it first):

1. `ValidateAsync_PredictionAboveThreshold_ReturnsPass` — stub returns `[{label:"person", confidence:0.87}]`, MinConfidence=0.5, AllowedLabels=["person"]. Assert `Verdict.Passed == true`.
2. `ValidateAsync_PredictionBelowThreshold_ReturnsFail` — confidence=0.3, MinConfidence=0.5. Assert `Verdict.Passed == false` and `Reason` contains "confidence".
3. `ValidateAsync_LabelNotInAllowedList_ReturnsFail` — confidence=0.9 but label="dog", AllowedLabels=["person"]. Fail.
4. `ValidateAsync_AllowedLabelsEmpty_AcceptsAnyLabel` — AllowedLabels=[], confidence=0.9, label="dog". Pass (per RESEARCH §1 decision rule: empty AllowedLabels = no filter).
5. `ValidateAsync_FailClosed_OnTimeout_ReturnsFail` — WireMock delays response 10s, opts.Timeout=1s, OnError=FailClosed. Assert `Verdict.Passed == false`, `Reason.StartsWith("validator_timeout")`. CapturingLogger entry for `ValidatorTimeout`.
6. `ValidateAsync_FailOpen_OnTimeout_ReturnsPass` — same setup, OnError=FailOpen. Assert `Verdict.Passed == true`, log entry for `validator_unavailable` style warning.
7. `ValidateAsync_MultipartWireFormat_UsesUnquotedNameImage` — assert WireMock-captured request body matches the unquoted-name regex `Content-Disposition: form-data; name=image; filename=snapshot\.jpg` (Phase 6 D12 — .NET 10 default).
8. `ValidateAsync_HappyPath_ParsesPredictionsArray` — verifies the JSON projection (success=true, 2 predictions, code=200) deserializes into `CodeProjectAiResponse` and the decision rule returns Pass.

Use `CapturingLogger<CodeProjectAiValidator>` from `FrigateRelay.TestHelpers`.

**Then implement `CodeProjectAiValidator`:**

```csharp
public sealed class CodeProjectAiValidator : IValidationPlugin
{
    private readonly string _name;
    private readonly CodeProjectAiOptions _opts;
    private readonly HttpClient _http;
    private readonly ILogger<CodeProjectAiValidator> _logger;

    [SetsRequiredMembers] // only if Name is required init; not strictly needed if private string field
    public CodeProjectAiValidator(string name, CodeProjectAiOptions opts, HttpClient http, ILogger<CodeProjectAiValidator> logger)
    {
        _name = name;
        _opts = opts;
        _http = http;
        _logger = logger;
    }

    public string Name => _name;

    public async Task<Verdict> ValidateAsync(EventContext ctx, SnapshotContext snapshot, CancellationToken ct)
    {
        var snap = await snapshot.ResolveAsync(ctx, ct).ConfigureAwait(false);
        if (snap is null)
            return Verdict.Fail("validator_no_snapshot");

        try
        {
            using var content = BuildMultipart(snap.Bytes);
            using var response = await _http.PostAsync("/v1/vision/detection", content, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<CodeProjectAiResponse>(ct).ConfigureAwait(false);
            return EvaluatePredictions(body);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // host shutdown — propagate
        }
        catch (TaskCanceledException ex)
        {
            Log.ValidatorTimeout(_logger, _name, ctx.EventId, ex);
            return _opts.OnError == ValidatorErrorMode.FailOpen
                ? Verdict.Pass()
                : Verdict.Fail("validator_timeout");
        }
        catch (HttpRequestException ex)
        {
            Log.ValidatorUnavailable(_logger, _name, ctx.EventId, ex);
            return _opts.OnError == ValidatorErrorMode.FailOpen
                ? Verdict.Pass()
                : Verdict.Fail($"validator_unavailable: {ex.Message}");
        }
    }

    private static MultipartFormDataContent BuildMultipart(ReadOnlyMemory<byte> bytes)
    {
        // Phase 6 D12: .NET 10 emits unquoted name= in multipart by default. DO NOT
        // manually quote — operator-side wire format must remain consistent with Pushover.
        var content = new MultipartFormDataContent();
        var image = new ByteArrayContent(bytes.ToArray());
        image.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(image, "image", "snapshot.jpg");
        return content;
    }

    private Verdict EvaluatePredictions(CodeProjectAiResponse? body)
    {
        if (body is null || !body.Success || body.Predictions is null || body.Predictions.Count == 0)
            return Verdict.Fail("validator_no_predictions");

        foreach (var p in body.Predictions)
        {
            if (p.Confidence < _opts.MinConfidence) continue;
            if (_opts.AllowedLabels.Length > 0 &&
                !_opts.AllowedLabels.Any(l => string.Equals(l, p.Label, StringComparison.OrdinalIgnoreCase)))
                continue;
            return Verdict.Pass();
        }
        return Verdict.Fail($"validator_no_match: minConfidence={_opts.MinConfidence}, allowedLabels=[{string.Join(",", _opts.AllowedLabels)}]");
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 7001, Level = LogLevel.Warning,
            Message = "CodeProject.AI validator '{Validator}' timed out for event {EventId}")]
        public static partial void ValidatorTimeout(ILogger logger, string validator, string eventId, Exception ex);

        [LoggerMessage(EventId = 7002, Level = LogLevel.Warning,
            Message = "CodeProject.AI validator '{Validator}' unavailable for event {EventId}")]
        public static partial void ValidatorUnavailable(ILogger logger, string validator, string eventId, Exception ex);
    }
}
```

**Catch order** matters (RESEARCH §6): `OperationCanceledException when ct.IsCancellationRequested` MUST come before `TaskCanceledException` (which derives from `OperationCanceledException`).

**`Log.ValidatorRejected` (the structured `validator_rejected` log fields per CONTEXT-7 D7) lives in the dispatcher, not here.** This logger only emits `validator_timeout` / `validator_unavailable` warnings. Do not duplicate.

**Acceptance criteria:**
- 8 unit tests pass.
- `git grep -n "ServicePointManager" src/FrigateRelay.Plugins.CodeProjectAi/` empty.
- `git grep -nE '192\.168\.[0-9]+\.[0-9]+' src/FrigateRelay.Plugins.CodeProjectAi/` empty (no hardcoded IPs in source or comments).
- `git grep -nE '\.(Result|Wait)\(' src/FrigateRelay.Plugins.CodeProjectAi/` empty.
- `git grep -n "LoggerMessage" src/FrigateRelay.Plugins.CodeProjectAi/CodeProjectAiValidator.cs` matches.

### Task 3: Implement `PluginRegistrar` (enumerate top-level `Validators`, register keyed instances)
**Files:** `src/FrigateRelay.Plugins.CodeProjectAi/PluginRegistrar.cs`
**Action:** create
**Description:**

Per RESEARCH §2 verbatim:

```csharp
public sealed class PluginRegistrar : IPluginRegistrar
{
    public void Register(PluginRegistrationContext context)
    {
        var validatorsSection = context.Configuration.GetSection("Validators");
        if (!validatorsSection.Exists()) return;

        foreach (var instance in validatorsSection.GetChildren())
        {
            var type = instance["Type"];
            if (!string.Equals(type, "CodeProjectAi", StringComparison.Ordinal))
                continue; // another plugin's registrar handles other Type values

            var instanceKey = instance.Key;

            // Bind named options instance (keyed on instanceKey via IOptionsMonitor.Get(name))
            context.Services
                .AddOptions<CodeProjectAiOptions>(instanceKey)
                .Bind(instance)
                .ValidateDataAnnotations()
                .ValidateOnStart();

            // Per-instance HttpClient with per-instance TLS handler.
            // No AddResilienceHandler — CONTEXT-7 D4 architect lock-in.
            // This is INTENTIONALLY asymmetric with BlueIris/Pushover (which DO retry):
            // pre-action gates that retry would systematically delay every notification by 18s.
            var capturedKey = instanceKey;
            context.Services
                .AddHttpClient($"CodeProjectAi:{capturedKey}")
                .ConfigurePrimaryHttpMessageHandler(sp =>
                {
                    var opts = sp.GetRequiredService<IOptionsMonitor<CodeProjectAiOptions>>().Get(capturedKey);
                    var handler = new SocketsHttpHandler
                    {
                        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                    };
                    if (opts.AllowInvalidCertificates)
                    {
                        handler.SslOptions.RemoteCertificateValidationCallback = static (_, _, _, _) => true;
                    }
                    return handler;
                });

            // Keyed validator plugin
            context.Services.AddKeyedSingleton<IValidationPlugin>(capturedKey, (sp, key) =>
            {
                var name = (string)key!;
                var opts = sp.GetRequiredService<IOptionsMonitor<CodeProjectAiOptions>>().Get(name);
                var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient($"CodeProjectAi:{name}");
                http.BaseAddress = new Uri(opts.BaseUrl);
                http.Timeout = opts.Timeout;
                var logger = sp.GetRequiredService<ILogger<CodeProjectAiValidator>>();
                return new CodeProjectAiValidator(name, opts, http, logger);
            });
        }
    }
}
```

**No new tests in this task** — registrar coverage comes via PLAN-3.1 integration tests (per orchestrator brief).

**Critical lessons captured in code comment (per CONTEXT-7 D4 lock-in):** "INTENTIONALLY no AddResilienceHandler. Asymmetric with BlueIris/Pushover (which retry 3/6/9s). Validators are pre-action gates; retry would systematically delay every notification by 18s. Single Timeout, fail-{closed,open} per OnError."

**Acceptance criteria:**
- Build clean.
- `git grep -n "AddResilienceHandler" src/FrigateRelay.Plugins.CodeProjectAi/` empty (intentional).
- `git grep -n "AddKeyedSingleton" src/FrigateRelay.Plugins.CodeProjectAi/PluginRegistrar.cs` matches.
- `git grep -n "ServicePointManager" src/FrigateRelay.Plugins.CodeProjectAi/` empty.

## Verification

```bash
dotnet build FrigateRelay.sln -c Release
dotnet run --project tests/FrigateRelay.Plugins.CodeProjectAi.Tests -c Release
git grep -nE '\.(Result|Wait)\(' src/FrigateRelay.Plugins.CodeProjectAi/                # must be empty
git grep -n "ServicePointManager" src/FrigateRelay.Plugins.CodeProjectAi/                # must be empty
git grep -nE 'App\.Metrics|OpenTracing|Jaeger\.' src/FrigateRelay.Plugins.CodeProjectAi/ # must be empty
git grep -nE '192\.168\.[0-9]+\.[0-9]+' src/FrigateRelay.Plugins.CodeProjectAi/          # must be empty
git grep -n "AddResilienceHandler" src/FrigateRelay.Plugins.CodeProjectAi/               # must be empty (D4 intentional)
```

## Notes for builder
- **Phase 6 D12 (multipart unquoting):** .NET 10 `MultipartFormDataContent` emits `name=image` UNQUOTED by default. Test 7 must assert the unquoted form. See `.shipyard/phases/6/results/SUMMARY-2.1.md` for the precedent.
- **Phase 6 SUMMARY-1.2.md** (Pushover) is the closest precedent — Read it first for the registrar style, options pattern, WireMock test setup. The keyed-singleton pattern here is NEW (Pushover registers a single non-keyed instance), but the HttpClient + options binding shape is identical.
- **RESEARCH §6** — read the full `OnError` switch + catch-block ORDER before writing Task 2's tests.
- **CapturingLogger:** use `tests/FrigateRelay.TestHelpers/CapturingLogger<T>` (CLAUDE.md Conventions). Do NOT redefine.
- **MTP runner:** test project must have `<OutputType>Exe</OutputType>` and run via `dotnet run --project … -c Release`, NOT `dotnet test`.
- **InternalsVisibleTo:** MSBuild item form in csproj only, never `[assembly: InternalsVisibleTo(...)]` source attribute (CLAUDE.md Conventions).
- **No hardcoded IPs in tests either** — WireMock binds to a localhost ephemeral port at runtime; never write `192.168.x.x` in fixtures or comments.
- **CI registration:** verify `.github/scripts/run-tests.sh`, `.github/workflows/ci.yml`, and `Jenkinsfile`. Per CLAUDE.md, the latter two were hardcoded as of Phase 3. With this third test project, the Rule of Three is satisfied — extracting a shared script is now justified. If you do extract, update both workflow and Jenkins call sites in this task.
