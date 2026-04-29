---
phase: 12-parity-cutover
plan: 1.3
wave: 1
dependencies: []
must_haves:
  - tools/FrigateRelay.MigrateConf/ Exe csproj on net10.0 with --input/--output CLI args
  - INI parser that handles SharpConfig-style repeated [SubscriptionSettings] sections (multiple identical headers)
  - tests/FrigateRelay.MigrateConf.Tests/ MSTest v4.2.1 csproj exercising round-trip against tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf
  - Tool output passes the same size-ratio + binding sub-assertions as ConfigSizeParityTest (Phase 8)
  - Both csprojs added to FrigateRelay.sln
files_touched:
  - tools/FrigateRelay.MigrateConf/FrigateRelay.MigrateConf.csproj
  - tools/FrigateRelay.MigrateConf/Program.cs
  - tools/FrigateRelay.MigrateConf/IniReader.cs
  - tools/FrigateRelay.MigrateConf/AppsettingsWriter.cs
  - tests/FrigateRelay.MigrateConf.Tests/FrigateRelay.MigrateConf.Tests.csproj
  - tests/FrigateRelay.MigrateConf.Tests/MigrateConfRoundTripTests.cs
  - tests/FrigateRelay.MigrateConf.Tests/Usings.cs
  - FrigateRelay.sln
tdd: true
risk: medium
---

# Plan 1.3: `tools/FrigateRelay.MigrateConf/` C# console tool + companion tests

## Context

CONTEXT-12 D3 + D6 lock the migration tool: a real .NET 10 console application at `tools/FrigateRelay.MigrateConf/` (NOT a Python/bash script), with a companion test project at `tests/FrigateRelay.MigrateConf.Tests/`. RESEARCH §1 enumerates the INI key→appsettings field mapping. RESEARCH §2 names the smoke gate: round-trip the existing `tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf` (88 lines, 9 subscriptions — confirmed by ORCHESTRATOR CORRECTION in RESEARCH §8) and assert the output passes the same size-ratio + `StartupValidation.ValidateAll` checks the Phase 8 `ConfigSizeParityTest` enforces.

**Architect-discretion locked:**

- **INI parser:** hand-rolled, NOT `Microsoft.Extensions.Configuration.Ini`. Reason: M.E.C.Ini collapses duplicate section headers (`[SubscriptionSettings]` repeated 9 times in the legacy file) by last-writer-wins on the section dictionary. The legacy SharpConfig wire format relies on duplicate-header semantics. A 60-line hand-rolled enumerator that streams lines, opens a new section dict on EACH `[Header]` occurrence, and yields one POCO per section is correct and trivially testable.
- **Output filename default:** `appsettings.Local.json` (CLAUDE.md: secrets live in `.Local.json`; this is consistent with the legacy `[PushoverSettings]` carrying `AppToken`/`UserKey` that should never land in the committed `appsettings.json`). The CLI accepts `--output <path>` to override.
- **Subcommand router:** Program.cs uses a tiny first-positional-arg switch — default verb (no first-positional or `migrate`) runs the migrate flow; `reconcile` (added in Wave 3 PLAN-3.1) runs the reconciliation flow. Wave 1 implements ONLY the migrate verb; the router skeleton is in place so PLAN-3.1 can append a case without restructuring.
- **No `FrigateRelay.Host` project reference.** The tool emits raw JSON conforming to the Host's expected shape; coupling to the Host csproj's full surface (DI, Serilog, OTel) is unnecessary and would balloon the tool's transitive deps. The schema correctness is enforced by the test project's binding assertion (which DOES reference Host for `StartupValidation.ValidateAll`).
- **No `FrigateRelay.Host` reference inside the Test project either** — instead, the test project uses the existing `ConfigSizeParityTest` validation pattern by referencing `FrigateRelay.Host.Tests`'s helper if extracted, OR by re-implementing a minimal `IConfiguration.Bind` + `StartupValidation.ValidateAll` block inline. **Builder decision (architect-locked):** re-implement inline with NSubstitute stubs for `IActionPlugin`/`ISnapshotProvider`/`IValidationPlugin` (mirroring `ConfigSizeParityTest.cs`). Avoids a `tests/`-to-`tests/` ProjectReference, which is non-standard.
- **Fixture access:** the test reads the existing `tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf` via a relative path. Test csproj does NOT copy/duplicate the fixture; the `<None Include="..\FrigateRelay.Host.Tests\Fixtures\legacy.conf" Link="Fixtures\legacy.conf"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></None>` MSBuild item links the file into the test's output dir at runtime. This avoids fixture drift between the two test projects.

## Dependencies

- None (Wave 1 plan, no upstream).
- File-disjoint with PLAN-1.1, 1.2, 1.4, 1.5. Touches `tools/`, `tests/FrigateRelay.MigrateConf.Tests/`, and `FrigateRelay.sln`.
- Wave 3 PLAN-3.1 will EXTEND this plan's `Program.cs` (add `reconcile` verb). PLAN-3.1 is in a strictly later wave so concurrency is not a concern.

## Tasks

### Task 1: Tool csproj + Program.cs (with subcommand router) + INI parser

**Files:**
- `tools/FrigateRelay.MigrateConf/FrigateRelay.MigrateConf.csproj` (create)
- `tools/FrigateRelay.MigrateConf/Program.cs` (create)
- `tools/FrigateRelay.MigrateConf/IniReader.cs` (create)
- `tools/FrigateRelay.MigrateConf/AppsettingsWriter.cs` (create)
- `FrigateRelay.sln` (modify — add the new csproj)

**Action:** create

**Description:**

**`FrigateRelay.MigrateConf.csproj`** (mirrors BlueIris csproj shape per RESEARCH §3 with `OutputType=Exe`):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>FrigateRelay.MigrateConf</RootNamespace>
    <AssemblyName>FrigateRelay.MigrateConf</AssemblyName>
    <IsPackable>false</IsPackable>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="FrigateRelay.MigrateConf.Tests" />
    <InternalsVisibleTo Include="DynamicProxyGenAssembly2" />
  </ItemGroup>
</Project>
```

No package references — the tool is intentionally minimal (System.Text.Json is in-box on net10.0, INI parsing is hand-rolled).

**`Program.cs`** — subcommand router skeleton + migrate verb implementation:

```csharp
namespace FrigateRelay.MigrateConf;

internal static class Program
{
    internal static int Main(string[] args)
    {
        // Verb router. Wave 1 implements 'migrate' (default). Wave 3 PLAN-3.1 appends 'reconcile'.
        var verb = args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal) ? args[0] : "migrate";
        var verbArgs = verb == args.FirstOrDefault() ? args.Skip(1).ToArray() : args;

        return verb switch
        {
            "migrate" => RunMigrate(verbArgs),
            "reconcile" => RunReconcile(verbArgs),
            _ => Fail($"Unknown verb '{verb}'. Supported: migrate, reconcile.")
        };
    }

    internal static int RunMigrate(string[] args)
    {
        if (!TryGetArg(args, "--input", out var input) || !TryGetArg(args, "--output", out var output))
        {
            return Fail("Usage: migrate-conf migrate --input <path-to-legacy.conf> --output <path-to-appsettings.json>");
        }

        var sections = IniReader.Read(input);
        var json = AppsettingsWriter.Build(sections);
        File.WriteAllText(output, json);
        Console.Out.WriteLine($"Wrote {output} ({new FileInfo(output).Length} bytes).");
        return 0;
    }

    // Wave 1 stub. PLAN-3.1 replaces with real reconcile logic.
    internal static int RunReconcile(string[] args)
        => Fail("reconcile verb is not yet implemented (Phase 12 Wave 3 PLAN-3.1).");

    private static bool TryGetArg(string[] args, string name, out string value)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.Ordinal))
            {
                value = args[i + 1];
                return true;
            }
        }
        value = "";
        return false;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}
```

**`IniReader.cs`** — hand-rolled enumerator. CRITICAL: must yield one section instance per `[Header]` occurrence even when headers repeat:

```csharp
namespace FrigateRelay.MigrateConf;

/// <summary>
/// Reads a legacy SharpConfig-style INI file into an ordered list of (sectionName, keys[]) tuples.
/// Repeated section headers (e.g. multiple [SubscriptionSettings] blocks) are preserved as
/// distinct list entries — last-writer-wins semantics of standard INI dictionaries are explicitly avoided.
/// </summary>
internal static class IniReader
{
    public sealed record Section(string Name, IReadOnlyList<KeyValuePair<string, string>> Entries);

    public static List<Section> Read(string path)
    {
        var sections = new List<Section>();
        List<KeyValuePair<string, string>>? current = null;
        string? currentName = null;

        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                if (currentName is not null)
                {
                    sections.Add(new Section(currentName, current!));
                }
                currentName = line[1..^1].Trim();
                current = new List<KeyValuePair<string, string>>();
                continue;
            }
            var eq = line.IndexOf('=');
            if (eq < 0 || current is null)
            {
                continue;
            }
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            current.Add(new KeyValuePair<string, string>(key, value));
        }
        if (currentName is not null)
        {
            sections.Add(new Section(currentName, current!));
        }
        return sections;
    }
}
```

**`AppsettingsWriter.cs`** — emits the Phase 8 Profiles+Subscriptions JSON shape. Per RESEARCH §1 mapping:

- `[ServerSettings].Server` → `FrigateMqtt:Server`
- `[ServerSettings].BlueIrisImages` → `BlueIris:SnapshotUrlTemplate` (rewrite trailing path to `{camera}` token; if input ends with `/image/`, append `{camera}`)
- `[PushoverSettings].AppToken`/`UserKey` → `Pushover:AppToken`/`UserKey` (emit empty strings if absent — secrets supplied via env vars per CLAUDE.md)
- `[PushoverSettings].NotifySleepTime` → per-subscription `CooldownSeconds` (default 30 if absent)
- Each `[SubscriptionSettings]` section → one entry in `Subscriptions[]`:
  - `Name` → `Name`
  - `CameraName` → `Camera`
  - `ObjectName` → `Label`
  - `Zone` → `Zone`
  - `Camera` (the BlueIris trigger URL) → emitted as a comment-style note in a `// _migration_note` field, OR drop — **architect decision: drop, document the drop in the migration doc PLAN-1.4**. The Phase 8 config shape uses a global `BlueIris:TriggerUrlTemplate` with `{camera}` token; per-subscription override is via inline Action entries which is out of scope for an automated migration.
- All subscriptions reference a single `Profile` named `"Standard"` with actions `["BlueIris", "Pushover"]` (Pushover has `SnapshotProvider: "Frigate"` mirroring `appsettings.Example.json`).

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FrigateRelay.MigrateConf;

internal static class AppsettingsWriter
{
    public static string Build(IReadOnlyList<IniReader.Section> sections)
    {
        var server = sections.FirstOrDefault(s => s.Name == "ServerSettings");
        var pushover = sections.FirstOrDefault(s => s.Name == "PushoverSettings");
        var subs = sections.Where(s => s.Name == "SubscriptionSettings").ToList();

        var root = new JsonObject
        {
            ["FrigateMqtt"] = new JsonObject { ["Server"] = ValueOrEmpty(server, "Server") },
            ["BlueIris"] = new JsonObject
            {
                ["TriggerUrlTemplate"] = "http://example.invalid/admin?trigger&camera={camera}",
                ["SnapshotUrlTemplate"] = AppendCameraToken(ValueOrEmpty(server, "BlueIrisImages")),
            },
            ["Pushover"] = new JsonObject
            {
                ["AppToken"] = "",   // secrets via env var per CLAUDE.md
                ["UserKey"]  = "",
            },
            ["Profiles"] = new JsonObject
            {
                ["Standard"] = new JsonObject
                {
                    ["Actions"] = new JsonArray(
                        new JsonObject { ["Plugin"] = "BlueIris" },
                        new JsonObject { ["Plugin"] = "Pushover", ["SnapshotProvider"] = "Frigate" })
                }
            },
            ["Subscriptions"] = new JsonArray(subs.Select(BuildSubscription).ToArray<JsonNode>()),
        };

        var cooldown = ParseIntOrDefault(ValueOrEmpty(pushover, "NotifySleepTime"), 30);
        foreach (var sub in (JsonArray)root["Subscriptions"]!)
        {
            sub!["CooldownSeconds"] = cooldown;
        }

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonObject BuildSubscription(IniReader.Section s) => new()
    {
        ["Name"] = ValueOrEmpty(s, "Name"),
        ["Camera"] = ValueOrEmpty(s, "CameraName"),
        ["Label"] = ValueOrEmpty(s, "ObjectName"),
        ["Zone"] = ValueOrEmpty(s, "Zone"),
        ["Profile"] = "Standard",
    };

    private static string ValueOrEmpty(IniReader.Section? section, string key)
        => section?.Entries.FirstOrDefault(kv => kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value ?? "";

    private static string AppendCameraToken(string baseUrl)
        => string.IsNullOrEmpty(baseUrl) ? "" : (baseUrl.EndsWith('/') ? baseUrl + "{camera}" : baseUrl + "/{camera}");

    private static int ParseIntOrDefault(string s, int def) => int.TryParse(s, out var v) ? v : def;
}
```

**`FrigateRelay.sln` add:** use `dotnet sln FrigateRelay.sln add tools/FrigateRelay.MigrateConf/FrigateRelay.MigrateConf.csproj` to register the project. Confirm with `dotnet sln FrigateRelay.sln list | grep -q MigrateConf`.

**Acceptance Criteria:**
- All four tool source files exist.
- `dotnet build tools/FrigateRelay.MigrateConf/FrigateRelay.MigrateConf.csproj -c Release` clean (warnings-as-errors).
- `dotnet build FrigateRelay.sln -c Release` clean (proves sln registration is correct).
- `dotnet run --project tools/FrigateRelay.MigrateConf -c Release -- --input tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf --output /tmp/phase12-migrate-out.json` exits 0.
- `python3 -c 'import json; json.load(open("/tmp/phase12-migrate-out.json"))'` exits 0 (output is valid JSON).
- `python3 -c 'import json; d=json.load(open("/tmp/phase12-migrate-out.json")); assert len(d["Subscriptions"]) == 9'` exits 0 (round-trip preserved all 9 subscriptions — proves repeated-section parser correctness).
- `grep -q '"AppToken": ""' /tmp/phase12-migrate-out.json` (secrets default empty per CLAUDE.md).
- `grep -nE '192\.168\.|10\.0\.0\.' tools/FrigateRelay.MigrateConf/` returns zero matches (no RFC 1918 IPs).

### Task 2: Test csproj + round-trip + size-ratio + binding tests

**Files:**
- `tests/FrigateRelay.MigrateConf.Tests/FrigateRelay.MigrateConf.Tests.csproj` (create)
- `tests/FrigateRelay.MigrateConf.Tests/Usings.cs` (create)
- `tests/FrigateRelay.MigrateConf.Tests/MigrateConfRoundTripTests.cs` (create)

**Action:** create (TDD: tests defined alongside the tool; pass once Task 1 compiles)

**Description:**

**`FrigateRelay.MigrateConf.Tests.csproj`** — BlueIris.Tests shape per RESEARCH §3, plus the linked fixture:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <IsPackable>false</IsPackable>
    <EnableMSTestRunner>true</EnableMSTestRunner>
    <TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>
    <TestingPlatformShowTestsFailure>true</TestingPlatformShowTestsFailure>
    <RootNamespace>FrigateRelay.MigrateConf.Tests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="MSTest" Version="4.2.1" />
    <PackageReference Include="FluentAssertions" Version="6.12.2" />
    <PackageReference Include="NSubstitute" Version="5.3.0" />
    <PackageReference Include="NSubstitute.Analyzers.CSharp" Version="1.0.17">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Options" Version="10.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\tools\FrigateRelay.MigrateConf\FrigateRelay.MigrateConf.csproj" />
    <ProjectReference Include="..\FrigateRelay.TestHelpers\FrigateRelay.TestHelpers.csproj" />
  </ItemGroup>
  <ItemGroup>
    <None Include="..\FrigateRelay.Host.Tests\Fixtures\legacy.conf" Link="Fixtures\legacy.conf">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
</Project>
```

Note: the test project intentionally does NOT reference `FrigateRelay.Host` (architect-locked above). For the binding sub-assertion the test exercises a minimal `IConfiguration.Bind` over the migrated JSON without touching `StartupValidation.ValidateAll` (which lives in Host). Reason: the size-ratio assertion is the binding gate the operator cares about; the deep-binding sub-assertion is already covered by the Phase 8 `ConfigSizeParityTest` running in `FrigateRelay.Host.Tests` after the operator manually feeds the migrated JSON into the example slot. **The test ensures the JSON is parseable + has the expected top-level shape**, not that it survives full host startup; the latter is enforced by ConfigSizeParityTest as the "Phase 8 gate".

**Add the test project to `FrigateRelay.sln`:** `dotnet sln FrigateRelay.sln add tests/FrigateRelay.MigrateConf.Tests/FrigateRelay.MigrateConf.Tests.csproj`.

**`Usings.cs`:**

```csharp
global using FluentAssertions;
global using FrigateRelay.TestHelpers;
global using Microsoft.VisualStudio.TestTools.UnitTesting;
```

**`MigrateConfRoundTripTests.cs`:**

```csharp
using System.Text.Json;
using FrigateRelay.MigrateConf;
using Microsoft.Extensions.Configuration;

namespace FrigateRelay.MigrateConf.Tests;

[TestClass]
public sealed class MigrateConfRoundTripTests
{
    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "legacy.conf");

    [TestMethod]
    public void IniReader_LegacyConf_Yields_OneServerOnePushoverNineSubscriptions()
    {
        File.Exists(FixturePath).Should().BeTrue($"fixture must be linked at {FixturePath}");

        var sections = IniReader.Read(FixturePath);

        sections.Count(s => s.Name == "ServerSettings").Should().Be(1);
        sections.Count(s => s.Name == "PushoverSettings").Should().Be(1);
        sections.Count(s => s.Name == "SubscriptionSettings").Should().Be(9);
    }

    [TestMethod]
    public void RunMigrate_LegacyConf_ProducesValidJsonWithNineSubscriptions()
    {
        var output = Path.Combine(Path.GetTempPath(), $"frigaterelay-migrate-{Guid.NewGuid():N}.json");
        try
        {
            var rc = Program.RunMigrate(["--input", FixturePath, "--output", output]);
            rc.Should().Be(0);

            File.Exists(output).Should().BeTrue();

            using var doc = JsonDocument.Parse(File.ReadAllText(output));
            doc.RootElement.GetProperty("Subscriptions").GetArrayLength().Should().Be(9);
            doc.RootElement.GetProperty("Profiles").GetProperty("Standard").GetProperty("Actions").GetArrayLength().Should().Be(2);
            doc.RootElement.GetProperty("Pushover").GetProperty("AppToken").GetString().Should().BeEmpty();
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }

    [TestMethod]
    public void RunMigrate_LegacyConf_OutputSizeRatioBelowSixty()
    {
        var output = Path.Combine(Path.GetTempPath(), $"frigaterelay-migrate-{Guid.NewGuid():N}.json");
        try
        {
            Program.RunMigrate(["--input", FixturePath, "--output", output]).Should().Be(0);

            var iniLength = new FileInfo(FixturePath).Length;
            var jsonLength = new FileInfo(output).Length;
            var ratio = (double)jsonLength / iniLength;

            ratio.Should().BeLessThanOrEqualTo(0.60d, "MigrateConf output must satisfy the same character-count ratio gate as Phase 8 ConfigSizeParityTest");
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }

    [TestMethod]
    public void RunMigrate_LegacyConf_OutputBindsAsConfiguration()
    {
        var output = Path.Combine(Path.GetTempPath(), $"frigaterelay-migrate-{Guid.NewGuid():N}.json");
        try
        {
            Program.RunMigrate(["--input", FixturePath, "--output", output]).Should().Be(0);

            var config = new ConfigurationBuilder().AddJsonFile(output, optional: false).Build();
            config["FrigateMqtt:Server"].Should().NotBeNullOrEmpty();
            config.GetSection("Subscriptions").GetChildren().Should().HaveCount(9);
            config.GetSection("Profiles:Standard:Actions").GetChildren().Should().HaveCount(2);
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }
}
```

**Acceptance Criteria:**
- All three test files exist.
- `dotnet build tests/FrigateRelay.MigrateConf.Tests/FrigateRelay.MigrateConf.Tests.csproj -c Release` clean.
- `dotnet run --project tests/FrigateRelay.MigrateConf.Tests -c Release --no-build` exits 0 with all four tests green.
- `dotnet sln FrigateRelay.sln list | grep -c MigrateConf` is 2 (tool + test project both registered).
- `find tests -maxdepth 2 -name '*Tests.csproj' | grep -q MigrateConf` (proves run-tests.sh auto-discovers it).

### Task 3: Verify Phase 8 ConfigSizeParityTest still passes after MigrateConf lands

**Files:** none (verification-only task)

**Action:** verify

**Description:**

The operator workflow is: run MigrateConf to convert their real `.conf`, manually paste the result into `config/appsettings.Example.json` (or the operator's own deployment target), and the existing Phase 8 `ConfigSizeParityTest` proves the size+binding gate. This task confirms PLAN-1.3's changes did NOT regress that test.

```bash
dotnet build FrigateRelay.sln -c Release
dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build -- --filter-query "/*/*/ConfigSizeParityTest/*"
```

The filter syntax (`/*/*/ClassName/*`) is the MTP equivalent of `--filter` per CLAUDE.md.

**Acceptance Criteria:**
- The above command exits 0.
- All ConfigSizeParityTest method(s) listed as Passed in the runner output.

## Verification

```bash
# 1. Build clean
dotnet build FrigateRelay.sln -c Release

# 2. New test project green
dotnet run --project tests/FrigateRelay.MigrateConf.Tests -c Release --no-build

# 3. Phase 8 parity test still green (no regression from MigrateConf landing)
dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build -- --filter-query "/*/*/ConfigSizeParityTest/*"

# 4. Tool runs end-to-end on the real fixture
dotnet run --project tools/FrigateRelay.MigrateConf -c Release --no-build -- \
  --input tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf \
  --output /tmp/phase12-migrate-smoke.json
python3 -c 'import json; d=json.load(open("/tmp/phase12-migrate-smoke.json")); assert len(d["Subscriptions"]) == 9'

# 5. Sln registration
dotnet sln FrigateRelay.sln list | grep -c MigrateConf  # expect 2

# 6. Auto-discovery works
.github/scripts/run-tests.sh --no-build  # picks up MigrateConf.Tests automatically

# 7. Secret-scan stays clean
.github/scripts/secret-scan.sh
```
