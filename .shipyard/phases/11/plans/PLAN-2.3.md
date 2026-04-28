---
phase: 11-oss-polish
plan: 2.3
wave: 2
dependencies: [1.1]
must_haves:
  - templates/FrigateRelay.Plugins.Template/.template.config/template.json (sourceName=FrigateRelay.Plugins.Example, shortName=frigaterelay-plugin)
  - Multi-project layout (src/ plugin + tests/ test) parameterized by sourceName
  - Out-of-the-box build clean and one passing unit test on scaffolded output
files_touched:
  - templates/FrigateRelay.Plugins.Template/.template.config/template.json
  - templates/FrigateRelay.Plugins.Template/src/FrigateRelay.Plugins.Example/FrigateRelay.Plugins.Example.csproj
  - templates/FrigateRelay.Plugins.Template/src/FrigateRelay.Plugins.Example/ExampleActionPlugin.cs
  - templates/FrigateRelay.Plugins.Template/src/FrigateRelay.Plugins.Example/ExampleOptions.cs
  - templates/FrigateRelay.Plugins.Template/src/FrigateRelay.Plugins.Example/ExamplePluginRegistrar.cs
  - templates/FrigateRelay.Plugins.Template/tests/FrigateRelay.Plugins.Example.Tests/FrigateRelay.Plugins.Example.Tests.csproj
  - templates/FrigateRelay.Plugins.Template/tests/FrigateRelay.Plugins.Example.Tests/ExampleActionPluginTests.cs
  - templates/FrigateRelay.Plugins.Template/tests/FrigateRelay.Plugins.Example.Tests/Usings.cs
tdd: false
risk: medium
---

# Plan 2.3: dotnet new template — `frigaterelay-plugin`

## Context

CONTEXT-11 D2 + D5 lock the scaffold mechanism: a `dotnet new` template with shortName `frigaterelay-plugin`. RESEARCH.md sec 4 documents the format; sec 3 documents the canonical existing-plugin csproj/test shape this template should mirror.

**Architect-discretion locked here:**

- **Template long identifier (D2 architect-discretion):** `FrigateRelay.Plugins.Template` (matches the directory name; matches RESEARCH.md sec 4 example).
- **`sourceName`:** `FrigateRelay.Plugins.Example`. The dotnet-new engine renames every occurrence in file names AND file contents to the user-supplied `-n` value (e.g. `dotnet new frigaterelay-plugin -n FrigateRelay.Plugins.MyCustom` → `FrigateRelay.Plugins.MyCustom` everywhere).
- **Scaffold scope (architect-discretion):** seed an `IActionPlugin` (BlueIris-shape, no snapshot consumption) plus its registrar, options, and ONE passing unit test. The plugin-author guide explains how to extend the scaffold to `IValidationPlugin` / `ISnapshotProvider`. Rationale: 80% of plugin authors will write actions; validators and snapshot providers are derivative shapes once the plugin contract is understood. Keeps the scaffold minimal (D2: "buildable plugin with one passing unit test out of the box").
- **Abstractions reference path (RESEARCH.md "Architect-Relevant Constraint #4"):** the scaffold's `.csproj` uses a **relative `<ProjectReference Include="../../../../src/FrigateRelay.Abstractions/FrigateRelay.Abstractions.csproj" />`** path. Works for the in-repo smoke test (the plugin-author-guide explicitly notes that out-of-repo authors will need to publish abstractions to NuGet first; that is Non-Goal v1 per PROJECT.md).
- **Explicit `<TargetFramework>net10.0</TargetFramework>`** in both csproj files (RESEARCH.md "Architect-Relevant Constraint #5", closing ID-3 by example).
- **Test csproj follows BlueIris.Tests shape** (RESEARCH.md sec 3): `OutputType=Exe`, `EnableMSTestRunner`, `TestingPlatformDotnetTestSupport`, MSTest 4.2.1 + FluentAssertions 6.12.2 + NSubstitute 5.3.0. Includes `<InternalsVisibleTo Include="DynamicProxyGenAssembly2" />` preemptively (RESEARCH.md "Architect-Relevant Constraint #6").

## Dependencies

- **Wave 1 gate:** PLAN-1.1 must complete (CONTEXT-11 D7).
- **File-disjoint with PLAN-2.1, 2.2, 2.4** — this plan touches only `templates/**`.

## Tasks

### Task 1: template.json + directory skeleton

**Files:**
- `templates/FrigateRelay.Plugins.Template/.template.config/template.json` (create)

**Action:** create

**Description:**
Create the `.template.config/template.json` file at the path above. Schema per RESEARCH.md sec 4:

```json
{
  "$schema": "http://json.schemastore.org/template",
  "author": "Brian Lehnen",
  "classifications": ["FrigateRelay", "Plugin"],
  "identity": "FrigateRelay.Plugins.Template",
  "name": "FrigateRelay Plugin",
  "shortName": "frigaterelay-plugin",
  "sourceName": "FrigateRelay.Plugins.Example",
  "tags": {
    "language": "C#",
    "type": "project"
  }
}
```

**Acceptance Criteria:**
- `test -f templates/FrigateRelay.Plugins.Template/.template.config/template.json`
- `grep -q '"shortName": "frigaterelay-plugin"' templates/FrigateRelay.Plugins.Template/.template.config/template.json`
- `grep -q '"sourceName": "FrigateRelay.Plugins.Example"' templates/FrigateRelay.Plugins.Template/.template.config/template.json`
- `grep -q '"identity": "FrigateRelay.Plugins.Template"' templates/FrigateRelay.Plugins.Template/.template.config/template.json`
- `python3 -c 'import json; json.load(open("templates/FrigateRelay.Plugins.Template/.template.config/template.json"))'` exits 0 (valid JSON).

### Task 2: Plugin csproj + source files (Example action plugin shape)

**Files:**
- `templates/FrigateRelay.Plugins.Template/src/FrigateRelay.Plugins.Example/FrigateRelay.Plugins.Example.csproj` (create)
- `templates/FrigateRelay.Plugins.Template/src/FrigateRelay.Plugins.Example/ExampleActionPlugin.cs` (create)
- `templates/FrigateRelay.Plugins.Template/src/FrigateRelay.Plugins.Example/ExampleOptions.cs` (create)
- `templates/FrigateRelay.Plugins.Template/src/FrigateRelay.Plugins.Example/ExamplePluginRegistrar.cs` (create)

**Action:** create

**Description:**

**`FrigateRelay.Plugins.Example.csproj`** — mirror BlueIris/Pushover shape (RESEARCH.md sec 3) with explicit TargetFramework:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Http" Version="10.0.4" />
    <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="10.0.4" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\..\..\src\FrigateRelay.Abstractions\FrigateRelay.Abstractions.csproj" />
  </ItemGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="FrigateRelay.Plugins.Example.Tests" />
  </ItemGroup>
</Project>
```

**`ExampleActionPlugin.cs`** — minimal `IActionPlugin` (BlueIris pattern, ignores `SnapshotContext`):

```csharp
using FrigateRelay.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FrigateRelay.Plugins.Example;

/// <summary>
/// Sample action plugin scaffolded by the FrigateRelay plugin template.
/// Replace the body of <see cref="ExecuteAsync"/> with your plugin's behavior.
/// </summary>
public sealed class ExampleActionPlugin : IActionPlugin
{
    private readonly ILogger<ExampleActionPlugin> _logger;
    private readonly ExampleOptions _options;

    public ExampleActionPlugin(ILogger<ExampleActionPlugin> logger, IOptions<ExampleOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public string Name => "Example";

    public Task ExecuteAsync(EventContext ctx, SnapshotContext snapshot, CancellationToken ct)
    {
        _logger.LogInformation("Example plugin received event {EventId} for camera {Camera}", ctx.EventId, ctx.Camera);
        return Task.CompletedTask;
    }
}
```

**`ExampleOptions.cs`** — minimal options class:

```csharp
namespace FrigateRelay.Plugins.Example;

public sealed class ExampleOptions
{
    public string Greeting { get; set; } = "hello";
}
```

**`ExamplePluginRegistrar.cs`** — `IPluginRegistrar` per RESEARCH.md sec 3 pattern:

```csharp
using FrigateRelay.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FrigateRelay.Plugins.Example;

public sealed class ExamplePluginRegistrar : IPluginRegistrar
{
    public void Register(PluginRegistrationContext context)
    {
        context.Services.AddSingleton<IActionPlugin, ExampleActionPlugin>();
        context.Services.AddOptions<ExampleOptions>()
            .Bind(context.Configuration.GetSection("Example"));
    }
}
```

**Acceptance Criteria:**
- All four files exist at the listed paths.
- `grep -q 'sourceName' templates/FrigateRelay.Plugins.Template/.template.config/template.json` (cross-Task sanity).
- `grep -q '<TargetFramework>net10.0</TargetFramework>' templates/FrigateRelay.Plugins.Template/src/FrigateRelay.Plugins.Example/FrigateRelay.Plugins.Example.csproj`
- `grep -q 'IActionPlugin' templates/FrigateRelay.Plugins.Template/src/FrigateRelay.Plugins.Example/ExampleActionPlugin.cs`
- `grep -q 'IPluginRegistrar' templates/FrigateRelay.Plugins.Template/src/FrigateRelay.Plugins.Example/ExamplePluginRegistrar.cs`
- `grep -nE '192\.168\.|AppToken=' templates/FrigateRelay.Plugins.Template/` returns zero matches.

### Task 3: Test csproj + ExampleActionPluginTests + scaffold smoke verification

**Files:**
- `templates/FrigateRelay.Plugins.Template/tests/FrigateRelay.Plugins.Example.Tests/FrigateRelay.Plugins.Example.Tests.csproj` (create)
- `templates/FrigateRelay.Plugins.Template/tests/FrigateRelay.Plugins.Example.Tests/ExampleActionPluginTests.cs` (create)
- `templates/FrigateRelay.Plugins.Template/tests/FrigateRelay.Plugins.Example.Tests/Usings.cs` (create)

**Action:** create

**Description:**

**`FrigateRelay.Plugins.Example.Tests.csproj`** — BlueIris.Tests shape (RESEARCH.md sec 3):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <IsPackable>false</IsPackable>
    <EnableMSTestRunner>true</EnableMSTestRunner>
    <TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>
    <TestingPlatformShowTestsFailure>true</TestingPlatformShowTestsFailure>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="MSTest" Version="4.2.1" />
    <PackageReference Include="FluentAssertions" Version="6.12.2" />
    <PackageReference Include="NSubstitute" Version="5.3.0" />
    <PackageReference Include="NSubstitute.Analyzers.CSharp" Version="1.0.17">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\FrigateRelay.Plugins.Example\FrigateRelay.Plugins.Example.csproj" />
    <ProjectReference Include="..\..\..\..\src\FrigateRelay.Abstractions\FrigateRelay.Abstractions.csproj" />
  </ItemGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="DynamicProxyGenAssembly2" />
  </ItemGroup>
</Project>
```

> Note: The scaffold deliberately does NOT reference `FrigateRelay.TestHelpers`. Reason: the helper project lives in the consumer repo's `tests/`, not in the template; an out-of-repo plugin author won't have it. The single scaffolded test does not require `CapturingLogger<T>` — it asserts the plugin's `Name` property and that `ExecuteAsync` returns a completed task. If the plugin author later wants `CapturingLogger`, the plugin-author-guide (PLAN-3.1) explains how to add the reference.

**`ExampleActionPluginTests.cs`** — one DAMP-named test:

```csharp
using FluentAssertions;
using FrigateRelay.Abstractions;
using FrigateRelay.Plugins.Example;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrigateRelay.Plugins.Example.Tests;

[TestClass]
public sealed class ExampleActionPluginTests
{
    [TestMethod]
    public async Task ExecuteAsync_LogsAndReturnsCompletedTask()
    {
        var plugin = new ExampleActionPlugin(
            NullLogger<ExampleActionPlugin>.Instance,
            Options.Create(new ExampleOptions()));

        plugin.Name.Should().Be("Example");

        var ctx = new EventContext
        {
            EventId = "ev-1",
            Camera = "cam-1",
            Label = "person",
            RawPayload = "{}",
            StartedAt = DateTimeOffset.UtcNow,
            SnapshotFetcher = _ => ValueTask.FromResult<byte[]?>(null),
        };

        await plugin.ExecuteAsync(ctx, default, CancellationToken.None);
        // No assertion needed beyond reaching here without throwing — the test verifies the plugin
        // contract is satisfied and the implementation doesn't blow up on a default SnapshotContext.
    }
}
```

**`Usings.cs`** — empty file with one global using if needed for parity with existing test projects (architect-discretion: prefer empty placeholder unless the test file requires it).

```csharp
// Add global usings here if your tests share many namespaces.
```

**Smoke-test path (builder verification, not a CI step in this plan):**

After creating all files, the builder runs the dotnet-new smoke locally to confirm the template works:

```bash
dotnet new install templates/FrigateRelay.Plugins.Template
SMOKE_DIR=$(mktemp -d)
dotnet new frigaterelay-plugin -n FrigateRelay.Plugins.SmokeScaffold -o "$SMOKE_DIR"
cd "$SMOKE_DIR"
# Edit ProjectReference paths in the scaffolded csproj if needed (relative paths assume in-repo location)
# For local smoke, copy the scaffolded src/tests into the actual repo's src/ + tests/ trees, then build.
# OR temporarily adjust the relative ../../../../src/FrigateRelay.Abstractions path.
# This smoke is a sanity check — the CI scaffold-smoke (PLAN-2.4) is the canonical gate.
```

The CI-side scaffold-smoke job lives in PLAN-2.4 (`docs.yml`). This task only confirms the template renders syntactically.

**Acceptance Criteria:**
- All three files exist.
- `grep -q '<EnableMSTestRunner>true</EnableMSTestRunner>' templates/FrigateRelay.Plugins.Template/tests/FrigateRelay.Plugins.Example.Tests/FrigateRelay.Plugins.Example.Tests.csproj`
- `grep -q 'DynamicProxyGenAssembly2' templates/FrigateRelay.Plugins.Template/tests/FrigateRelay.Plugins.Example.Tests/FrigateRelay.Plugins.Example.Tests.csproj`
- `grep -q 'FluentAssertions.*6.12.2' templates/FrigateRelay.Plugins.Template/tests/FrigateRelay.Plugins.Example.Tests/FrigateRelay.Plugins.Example.Tests.csproj`
- `grep -q '\[TestClass\]' templates/FrigateRelay.Plugins.Template/tests/FrigateRelay.Plugins.Example.Tests/ExampleActionPluginTests.cs`
- `grep -nE 'TestMethod\b' templates/FrigateRelay.Plugins.Template/tests/FrigateRelay.Plugins.Example.Tests/ExampleActionPluginTests.cs | wc -l` is at least 1.
- Smoke (builder, manual): `dotnet new install templates/FrigateRelay.Plugins.Template/` exits 0 and lists `frigaterelay-plugin` in the installed templates.

**Cleanup:** if the builder's local environment is dirty after `dotnet new install`, builder MUST `dotnet new uninstall templates/FrigateRelay.Plugins.Template/` before exiting so the smoke is repeatable.

## Verification

Run from repo root:

```bash
# 0. Template files exist
test -d templates/FrigateRelay.Plugins.Template/.template.config
test -f templates/FrigateRelay.Plugins.Template/.template.config/template.json
test -d templates/FrigateRelay.Plugins.Template/src/FrigateRelay.Plugins.Example
test -d templates/FrigateRelay.Plugins.Template/tests/FrigateRelay.Plugins.Example.Tests

# 1. JSON validity
python3 -c 'import json; json.load(open("templates/FrigateRelay.Plugins.Template/.template.config/template.json"))'

# 2. Local smoke — install, render, uninstall (does not build the rendered project; CI does that)
dotnet new install templates/FrigateRelay.Plugins.Template/
dotnet new list | grep -q 'frigaterelay-plugin'
SMOKE_DIR=$(mktemp -d)
dotnet new frigaterelay-plugin -n FrigateRelay.Plugins.SmokeScaffold -o "$SMOKE_DIR"
test -f "$SMOKE_DIR/src/FrigateRelay.Plugins.SmokeScaffold/FrigateRelay.Plugins.SmokeScaffold.csproj"
test -f "$SMOKE_DIR/src/FrigateRelay.Plugins.SmokeScaffold/SmokeScaffoldActionPlugin.cs" \
  || test -f "$SMOKE_DIR/src/FrigateRelay.Plugins.SmokeScaffold/ExampleActionPlugin.cs"
# Note: file-name renaming via sourceName replaces FrigateRelay.Plugins.Example → ...SmokeScaffold.
# The class-name part inside files is renamed identically; the per-token rename DOES NOT split
# "ExampleActionPlugin" into "SmokeScaffoldActionPlugin" — only the full sourceName string is replaced.
# Both file shapes are acceptable; CI smoke (PLAN-2.4) is the binding gate.
dotnet new uninstall templates/FrigateRelay.Plugins.Template/
rm -rf "$SMOKE_DIR"

# 3. Solution builds (template files are not in the solution; sanity check no regressions)
dotnet build FrigateRelay.sln -c Release

# 4. Secret + IP scan
grep -nE '192\.168\.|10\.[0-9]+\.[0-9]+\.[0-9]+\.[0-9]|AppToken=[A-Za-z0-9]{20,}' templates/ \
  && exit 1 || true
```
