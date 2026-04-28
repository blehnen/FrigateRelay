---
phase: 12-parity-cutover
plan: 1.5
wave: 1
dependencies: []
must_haves:
  - HostBootstrap supports an opt-in NDJSON file sink (Logging:File:CompactJson=true) for parity-window audit logs
  - Default text format unchanged (production users see no behavior change)
  - New Serilog.Formatting.Compact PackageReference in FrigateRelay.Host (only added if not already transitive)
  - Unit test asserts: when CompactJson=true and a DryRun event flows, the file produces a valid NDJSON line containing event_id/Camera/Label
files_touched:
  - src/FrigateRelay.Host/HostBootstrap.cs
  - src/FrigateRelay.Host/FrigateRelay.Host.csproj
  - tests/FrigateRelay.Host.Tests/Logging/CompactJsonFileSinkTests.cs
tdd: true
risk: medium
---

# Plan 1.5: Parity-window NDJSON audit log (opt-in Serilog `CompactJsonFormatter`)

## Context

CONTEXT-12 D5 puts DryRun emissions into structured logs. Wave 3 PLAN-3.1's reconciliation tooling reads those logs to build the parity report. RESEARCH §5 enumerates two options; Option A is recommended (RESEARCH §5 explicit recommendation): enable `CompactJsonFormatter` on the existing `logs/frigaterelay-.log` rolling sink, gated behind a config flag so default deployments keep human-readable text format.

**Architect-discretion locked:**

- **Opt-in only.** New config key `Logging:File:CompactJson` (bool, default `false`). When `true`, the file sink uses `Serilog.Formatting.Compact.CompactJsonFormatter`; when `false`, the existing text template is preserved. **Default is unchanged behavior.** The operator flips this on for the parity window in `appsettings.Local.json`, then flips it off (or removes it) for production.
- **No new sink path.** The same `logs/frigaterelay-.log` rolling sink emits NDJSON when the flag is on. Reason: avoids a new config key for the file path; operators already know where the log lives.
- **Console sink is unchanged.** Console keeps the human-readable template even when CompactJson is on. Reason: humans read console; tools read files.
- **Package reference:** `Serilog.Formatting.Compact` (~2.0.0, MIT). RESEARCH §5 / "Uncertainty Flags" notes it may already be transitive — builder MUST verify with `dotnet list src/FrigateRelay.Host package --include-transitive | grep -i compact` BEFORE adding the explicit PackageReference. If already transitive, an explicit PackageReference is still added for clarity (current pattern in `FrigateRelay.Host.csproj` is to list every direct dep explicitly).

## Dependencies

- None (Wave 1 plan).
- File-disjoint: touches `src/FrigateRelay.Host/HostBootstrap.cs`, `src/FrigateRelay.Host/FrigateRelay.Host.csproj`, and ONE new test file `tests/FrigateRelay.Host.Tests/Logging/CompactJsonFileSinkTests.cs`. PLAN-1.1, 1.2, 1.3, 1.4 do not touch any of these.
- **Soft-coupling note:** Wave 3 PLAN-3.1's reconciler depends on the field shape this plan emits. Field names locked HERE: every Serilog log event automatically carries the `LoggerMessage.Define` template parameter names verbatim. PLAN-1.1 emits `Camera`, `Label`, `EventId`. PLAN-1.2 emits `Camera`, `Label`, `EventId`. The NDJSON line for a DryRun emission therefore contains `"@t"`, `"@m"`, `"@i"` (Serilog Compact format envelope) plus `"Camera"`, `"Label"`, `"EventId"`. PLAN-3.1 reads exactly these field names.

## Tasks

### Task 1: Add `Serilog.Formatting.Compact` PackageReference + NDJSON branch in `HostBootstrap`

**Files:**
- `src/FrigateRelay.Host/FrigateRelay.Host.csproj` (modify)
- `src/FrigateRelay.Host/HostBootstrap.cs` (modify)

**Action:** modify

**Description:**

1. **csproj change.** Inside the existing `<ItemGroup>` listing `PackageReference` entries, add (alphabetically positioned among Serilog.* entries):

   ```xml
   <PackageReference Include="Serilog.Formatting.Compact" Version="2.0.0" />
   ```

   If `dotnet list src/FrigateRelay.Host package --include-transitive | grep -qi 'Serilog.Formatting.Compact'` already shows the package as transitive, the explicit add is still made (matches the host's pattern of declaring every direct dep). Do NOT pin to anything other than `2.0.0` unless an explicit later release is required by package compatibility.

2. **`HostBootstrap.cs` change.** Locate the `WriteTo.File("logs/frigaterelay-.log", ...)` call (RESEARCH §5 confirms its existence). The current call passes a text-template `outputTemplate` argument. Refactor to read `IConfiguration["Logging:File:CompactJson"]` and branch:

   ```csharp
   var useCompactJson = string.Equals(
       configuration["Logging:File:CompactJson"],
       "true",
       StringComparison.OrdinalIgnoreCase);

   if (useCompactJson)
   {
       loggerConfig = loggerConfig.WriteTo.File(
           formatter: new Serilog.Formatting.Compact.CompactJsonFormatter(),
           path: "logs/frigaterelay-.log",
           rollingInterval: RollingInterval.Day,
           retainedFileCountLimit: 7);
   }
   else
   {
       loggerConfig = loggerConfig.WriteTo.File(
           "logs/frigaterelay-.log",
           rollingInterval: RollingInterval.Day,
           retainedFileCountLimit: 7,
           outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}{NewLine}{Message:lj}{NewLine}{Exception}");
   }
   ```

   **Builder MUST** read the existing `WriteTo.File` invocation in `HostBootstrap.cs` first to copy its exact non-formatter args (rolling interval, retention) verbatim — the snippet above is illustrative; the literal arg values must match what the file already uses.

3. **Console sink is unchanged.** Search for any `WriteTo.Console(...)` call and leave it untouched.

4. **No other behavior changes.** Specifically: do NOT change the global minimum level, do NOT add new property enrichers, do NOT change Activity-id propagation. The `using Serilog.Formatting.Compact;` import goes at the top of the file.

**Acceptance Criteria:**
- `grep -q 'Serilog.Formatting.Compact' src/FrigateRelay.Host/FrigateRelay.Host.csproj`
- `grep -q 'Logging:File:CompactJson' src/FrigateRelay.Host/HostBootstrap.cs`
- `grep -q 'CompactJsonFormatter' src/FrigateRelay.Host/HostBootstrap.cs`
- `dotnet build src/FrigateRelay.Host/FrigateRelay.Host.csproj -c Release` clean (warnings-as-errors).
- `dotnet build FrigateRelay.sln -c Release` clean.

### Task 2: Unit test — CompactJson=true emits valid NDJSON with `Camera`/`Label`/`EventId`

**Files:**
- `tests/FrigateRelay.Host.Tests/Logging/CompactJsonFileSinkTests.cs` (create)

**Action:** create (TDD: write before the HostBootstrap branch when possible — but the test depends on a public/internal hook into HostBootstrap)

**Description:**

The test invokes `HostBootstrap.ConfigureSerilog(...)` (or whatever the actual method name is — builder MUST `grep -n 'public.*static.*Logger\|internal.*static.*Logger' src/FrigateRelay.Host/HostBootstrap.cs` to find it; if no public hook exists, expose one as `internal` and add `<InternalsVisibleTo>` for the test project — the test project already has it per RESEARCH §6).

If exposing a public hook is too invasive, the test instead constructs a `LoggerConfiguration` inline mirroring HostBootstrap's logic and writes one event with named properties `Camera`, `Label`, `EventId`. The test then reads the NDJSON file and asserts:

```csharp
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Serilog;
using Serilog.Formatting.Compact;

namespace FrigateRelay.Host.Tests.Logging;

[TestClass]
public sealed class CompactJsonFileSinkTests
{
    [TestMethod]
    public void File_Sink_With_CompactJson_Emits_Ndjson_With_Camera_Label_EventId()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"frigaterelay-ndjson-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var logPath = Path.Combine(dir, "audit-.log");

        try
        {
            using (var logger = new LoggerConfiguration()
                .WriteTo.File(formatter: new CompactJsonFormatter(), path: logPath, rollingInterval: RollingInterval.Day)
                .CreateLogger())
            {
                logger.Information("DryRun would-execute for camera={Camera} label={Label} event_id={EventId}",
                    "DriveWayHD", "person", "ev-1");
                logger.Dispose();
            }

            var emitted = Directory.GetFiles(dir, "audit-*.log").Single();
            var line = File.ReadAllLines(emitted).Single(l => l.Contains("DriveWayHD"));

            using var doc = JsonDocument.Parse(line);
            doc.RootElement.GetProperty("Camera").GetString().Should().Be("DriveWayHD");
            doc.RootElement.GetProperty("Label").GetString().Should().Be("person");
            doc.RootElement.GetProperty("EventId").GetString().Should().Be("ev-1");
            doc.RootElement.GetProperty("@t").GetString().Should().NotBeNullOrEmpty();
            doc.RootElement.GetProperty("@m").GetString().Should().Contain("DryRun would-execute");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    [TestMethod]
    public void HostBootstrap_When_CompactJsonFlag_Set_Selects_CompactJsonFormatter_Branch()
    {
        // Builder: implement against HostBootstrap's real public/internal hook; if none exists,
        // mark this test [Ignore] with a TODO referencing PLAN-1.5 Task 1, OR refactor HostBootstrap
        // to expose a `BuildLoggerConfiguration(IConfiguration)` method that returns a
        // LoggerConfiguration the test can introspect. Architect prefers the refactor.
        //
        // The bare-minimum acceptable assertion: build a config with `Logging:File:CompactJson=true`,
        // call the hook, verify the resulting LoggerConfiguration writes JSON not text — easiest done
        // by invoking it, writing a known event, and reading the file (mirrors test 1 with a real
        // HostBootstrap call instead of a hand-built LoggerConfiguration).

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Logging:File:CompactJson"] = "true",
            })
            .Build();

        // Builder fills in: HostBootstrap.BuildLoggerConfiguration(config) or equivalent.
        // Then writes one event, reads the resulting file, asserts JSON shape.
        // For this plan, the refactor + assertion are explicitly part of Task 1 + Task 2.

        config["Logging:File:CompactJson"].Should().Be("true");
    }
}
```

The second test is a placeholder for the real HostBootstrap-integrated assertion. Builder is expected to: (a) refactor `HostBootstrap` to expose an internal `BuildLoggerConfiguration(IConfiguration)` method returning a `LoggerConfiguration`, (b) wire this method into the existing host startup, (c) replace the placeholder assertion with a real one that invokes the method and verifies the NDJSON output. **The first test stands alone** as a regression guard for the `CompactJsonFormatter` field shape PLAN-3.1 depends on.

**Acceptance Criteria:**
- `tests/FrigateRelay.Host.Tests/Logging/CompactJsonFileSinkTests.cs` exists.
- `grep -q 'CompactJsonFormatter' tests/FrigateRelay.Host.Tests/Logging/CompactJsonFileSinkTests.cs`
- `dotnet build tests/FrigateRelay.Host.Tests/FrigateRelay.Host.Tests.csproj -c Release` clean.
- `dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build` exits 0 with both new tests Passed.
- The first test asserts `doc.RootElement.GetProperty("Camera").GetString().Should().Be("DriveWayHD")` — locks the field name PLAN-3.1 depends on.

### Task 3: Document the new config flag in `appsettings.Example.json` comment block

**Files:**
- `config/appsettings.Example.json` (modify — add a single commented `Logging` block as a doc-only stub; **do NOT** introduce the flag as a default-on setting)

**Action:** modify

**Description:**

Add a top-level comment block (jsonc-style — the existing example file is already commented in places per Phase 8) describing the new flag without enabling it. If the existing example is strict-JSON (no comments), append the documentation **inline as a `_documentation` field** at the end of the JSON, keyed off a recognizable name and noted as ignored at runtime.

**Builder check:** `head -5 config/appsettings.Example.json` — if it starts with `//` or contains `/* */`, the file is jsonc and comments are safe; if strict JSON, use the `_documentation` field approach. **Do NOT** introduce a malformed JSON file under any circumstance.

Example (jsonc form):
```jsonc
// Logging:File:CompactJson — when true, the rolling file sink at logs/frigaterelay-.log
// emits NDJSON (Serilog.Formatting.Compact) instead of the human-readable text template.
// Used during the Phase 12 parity-window for audit-log reconciliation. Default: false.
```

This is an opportunistic documentation pass piggy-backing on PLAN-1.5's scope. It is the ONLY edit this plan makes to the example file. **If the existing example is strict JSON and adding a `_documentation` field would change `ConfigSizeParityTest`'s 60% character ratio enough to fail the test, builder MUST skip Task 3 and document the flag instead in PLAN-1.4's migration doc** (file-disjoint adjustment is acceptable since both files are this plan's responsibility, but a cross-plan touch is NOT — Task 3 is droppable).

**Acceptance Criteria:**
- `config/appsettings.Example.json` is valid JSON OR valid JSON-with-comments (depending on the existing format).
- `python3 -c 'import json,re; s=open("config/appsettings.Example.json").read(); s=re.sub(r"//.*", "", s); s=re.sub(r"/\*.*?\*/", "", s, flags=re.DOTALL); json.loads(s)'` exits 0 (handles both strict and jsonc).
- ConfigSizeParityTest still green: `dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build -- --filter-query "/*/*/ConfigSizeParityTest/*"` exits 0.
- If Task 3 is dropped per the size-budget guard above: `git diff --stat config/` shows zero touches in this plan's commit, and PLAN-1.4's migration doc must include the flag description (architect notes this risk in VERIFICATION.md).

## Verification

```bash
# 1. Solution + Host + Host.Tests build clean
dotnet build FrigateRelay.sln -c Release

# 2. New + existing Host tests green
dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build

# 3. Phase 8 parity test still green
dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build -- \
  --filter-query "/*/*/ConfigSizeParityTest/*"

# 4. Confirm flag is opt-in (default behavior unchanged)
grep -q 'Logging:File:CompactJson' src/FrigateRelay.Host/HostBootstrap.cs
grep -q 'Serilog.Formatting.Compact' src/FrigateRelay.Host/FrigateRelay.Host.csproj

# 5. Smoke: with default config (no flag), the Host still boots and writes text-format logs
dotnet run --project src/FrigateRelay.Host -c Release --no-build > /tmp/host-smoke.log 2>&1 &
SMOKE_PID="$(pgrep -f 'FrigateRelay.Host/bin/Release/net10.0/FrigateRelay.Host$' | head -1)"
sleep 3
kill -INT "$SMOKE_PID" 2>/dev/null
wait
grep -qE '\[[0-9]{2}:[0-9]{2}:[0-9]{2} (INF|WRN|ERR)\]' /tmp/host-smoke.log  # text format default

# 6. Secret-scan stays clean
.github/scripts/secret-scan.sh
```
