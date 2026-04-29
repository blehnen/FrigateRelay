---
phase: phase-9-observability
plan: 2.2
wave: 2
dependencies: [PLAN-1.1]
must_haves:
  - HostBootstrap wires Serilog via UseSerilog (Worker SDK pattern, not AspNetCore)
  - HostBootstrap wires AddOpenTelemetry with conditional OTLP exporter (D2)
  - Seq sink registered conditionally on Serilog:Seq:ServerUrl (D7)
  - appsettings.json gains Serilog and Otel sections with empty-string defaults (no leaked URLs/IPs)
  - Startup fails fast on malformed Otel:OtlpEndpoint (Phase 8 ValidateAll discipline carried forward)
files_touched:
  - src/FrigateRelay.Host/HostBootstrap.cs
  - src/FrigateRelay.Host/Program.cs
  - src/FrigateRelay.Host/appsettings.json
  - src/FrigateRelay.Host/Configuration/StartupValidation.cs
tdd: false
risk: medium
---

# Plan 2.2: Host Wiring — Serilog UseSerilog, OpenTelemetry registration, config additions

## Context

Implements CONTEXT-9 D2 (conditional OTLP exporter), D7 (conditional Seq sink), and the Serilog bootstrap shape from RESEARCH.md §6. Adds `Otel` and `Serilog` configuration sections so operators can override per-environment without touching code.

This plan runs in PARALLEL with PLAN-2.1 in Wave 2. Disjoint file sets:
- PLAN-2.1 touches: `Pipeline/EventPump.cs`, `Dispatch/ChannelActionDispatcher.cs`.
- PLAN-2.2 touches: `HostBootstrap.cs`, `Program.cs`, `appsettings.json`, `Configuration/StartupValidation.cs`.

No file overlap, so no commit-order constraint within Wave 2.

## Dependencies

PLAN-1.1 — needs the OpenTelemetry / Serilog package references on `FrigateRelay.Host.csproj`.

## Tasks

### Task 1: Add Otel + Serilog configuration sections to appsettings.json
**Files:** `src/FrigateRelay.Host/appsettings.json`
**Action:** modify
**Description:**

Add two new top-level sections to `appsettings.json`. Existing `Logging:LogLevel` block stays as-is for M.E.L. compatibility (Serilog's `MinimumLevel` overrides once `UseSerilog` is wired):

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    },
    "Seq": {
      "ServerUrl": ""
    }
  },
  "Otel": {
    "OtlpEndpoint": ""
  }
}
```

Hard rules:
- `Serilog:Seq:ServerUrl` MUST default to `""` (D7 — conditional registration).
- `Otel:OtlpEndpoint` MUST default to `""` (D2 — conditional exporter).
- Do NOT include any URL, IP, or hostname in `appsettings.json` (CLAUDE.md — secret-scan CI greps for `192.168.x.x` and would reject any concrete value here).
- Do NOT add `Serilog:WriteTo` keys here — sinks are wired in code (Console, File always; Seq conditionally) for explicit control. The `MinimumLevel` keys ARE read by `Serilog.Settings.Configuration` via `lc.ReadFrom.Configuration(...)`.

**Acceptance Criteria:**
- `cat src/FrigateRelay.Host/appsettings.json | python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["Serilog"]["Seq"]["ServerUrl"]=="" and d["Otel"]["OtlpEndpoint"]==""; print("OK")'` prints `OK`.
- `bash .github/scripts/secret-scan.sh` (the existing Phase 2 secret-scan tripwire) succeeds — no IP, no AppToken-shaped value introduced.
- `dotnet build src/FrigateRelay.Host -c Release` clean.

### Task 2: Wire Serilog via UseSerilog in Program.cs/HostBootstrap.cs
**Files:** `src/FrigateRelay.Host/Program.cs`, `src/FrigateRelay.Host/HostBootstrap.cs`
**Action:** modify
**Description:**

Per RESEARCH.md §6 — Worker SDK uses `Serilog.Extensions.Hosting`, NOT `Serilog.AspNetCore`.

In `Program.cs` (or `HostBootstrap.ConfigureLogging` if that helper exists — verify file shape at build time and place the call wherever `HostApplicationBuilder` is being configured BEFORE `builder.Build()`):

```csharp
using Serilog;

builder.Host.UseSerilog((ctx, services, lc) =>
{
    lc.ReadFrom.Configuration(ctx.Configuration)
      .ReadFrom.Services(services)
      .Enrich.FromLogContext()
      .WriteTo.Console(outputTemplate:
          "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}{NewLine}{Message:lj}{NewLine}{Exception}")
      .WriteTo.File("logs/frigaterelay-.log",
          rollingInterval: RollingInterval.Day,
          retainedFileCountLimit: 7);

    var seqUrl = ctx.Configuration["Serilog:Seq:ServerUrl"];
    if (!string.IsNullOrWhiteSpace(seqUrl))
        lc.WriteTo.Seq(seqUrl);
});
```

Hard rules:
- Do NOT call `builder.Logging.ClearProviders()` separately — `UseSerilog()` performs provider replacement internally (RESEARCH.md §6 sharp edge).
- Do NOT introduce a `Log.Logger` static + injected `ILogger` mixed pattern. Use only the host-injected `ILogger<T>` everywhere (CLAUDE.md observability invariant).
- Do NOT introduce an enricher beyond `FromLogContext` (CONTEXT-9 "Out of scope" — custom Serilog enrichers deferred).
- The bootstrap logger (`HostBootstrap.cs:78` `LoggerFactory.Create(lb => lb.AddConsole())`) used during plugin registration BEFORE `builder.Build()` runs stays as-is — RESEARCH.md §6 sharp edge confirms this is acceptable.

**Acceptance Criteria:**
- `grep -n 'UseSerilog' src/FrigateRelay.Host/Program.cs src/FrigateRelay.Host/HostBootstrap.cs` returns at least one match.
- `grep -n 'WriteTo.Console' src/FrigateRelay.Host/Program.cs src/FrigateRelay.Host/HostBootstrap.cs` returns at least one match.
- `grep -n 'WriteTo.File' src/FrigateRelay.Host/Program.cs src/FrigateRelay.Host/HostBootstrap.cs` returns at least one match.
- `grep -n 'WriteTo.Seq' src/FrigateRelay.Host/Program.cs src/FrigateRelay.Host/HostBootstrap.cs` returns at least one match (D7 conditional).
- `grep -n 'Serilog.AspNetCore' src/FrigateRelay.Host/` returns zero (Worker SDK rule).
- `grep -nE 'Log\.Logger\s*=' src/FrigateRelay.Host/` returns zero (no static logger).
- `grep -n 'ClearProviders' src/FrigateRelay.Host/` returns zero (RESEARCH.md §6 sharp edge — UseSerilog handles it).
- `dotnet build FrigateRelay.sln -c Release` clean.
- Smoke run: `dotnet run --project src/FrigateRelay.Host -c Release` starts, emits at least one Serilog-formatted log line to console matching `[HH:MM:SS INF]`, exits 0 on Ctrl-C within 5 seconds (use the `pgrep + kill -INT` pattern from CLAUDE.md "Graceful shutdown smoke" section).

### Task 3: Wire AddOpenTelemetry in HostBootstrap with conditional OTLP + StartupValidation
**Files:** `src/FrigateRelay.Host/HostBootstrap.cs`, `src/FrigateRelay.Host/Configuration/StartupValidation.cs`
**Action:** modify
**Description:**

In `HostBootstrap.ConfigureServices` (after existing service registrations, before `Build()`):

```csharp
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var otlpEndpoint = configuration["Otel:OtlpEndpoint"]
    ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");

services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(serviceName: "FrigateRelay"))
    .WithTracing(b =>
    {
        b.AddSource("FrigateRelay");
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            b.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
    })
    .WithMetrics(b =>
    {
        b.AddMeter("FrigateRelay");
        b.AddRuntimeInstrumentation();
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            b.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
    });
```

Hard rules per D2:
- Always register `AddSource("FrigateRelay")` and `AddMeter("FrigateRelay")` so the diagnostics counters/spans flow even without an OTLP collector.
- Only call `AddOtlpExporter(...)` when the endpoint is non-empty — RESEARCH.md §5 confirms calling it without a running collector spams gRPC connection errors at startup.
- Do NOT call `AddConsoleExporter` (D2 excluded).
- `AddRuntimeInstrumentation()` is the ONE additional instrumentation included (RESEARCH.md §5 — stable, low-risk dotnet.* GC/JIT counters; no naming collision with `frigaterelay.*`).

**StartupValidation extension** (Phase 8 carry-forward — `Configuration/StartupValidation.cs` already exists with `ValidateAll` collect-all pattern). Add a method `ValidateObservability(IConfiguration config, ICollection<string> errors)`:

```csharp
internal static void ValidateObservability(IConfiguration config, ICollection<string> errors)
{
    var endpoint = config["Otel:OtlpEndpoint"];
    if (!string.IsNullOrWhiteSpace(endpoint) && !Uri.TryCreate(endpoint, UriKind.Absolute, out _))
        errors.Add($"Otel:OtlpEndpoint '{endpoint}' is not a valid absolute URI.");

    var seq = config["Serilog:Seq:ServerUrl"];
    if (!string.IsNullOrWhiteSpace(seq) && !Uri.TryCreate(seq, UriKind.Absolute, out _))
        errors.Add($"Serilog:Seq:ServerUrl '{seq}' is not a valid absolute URI.");
}
```

Wire `ValidateObservability` into the existing `ValidateAll` aggregation site so a malformed endpoint fails startup with a clear message rather than a silent silent-failure-on-export (D2 implication line — the trade-off was specifically called out in CONTEXT-9 for misconfigured endpoints; this validator catches the structurally-wrong subset of typos).

**Acceptance Criteria:**
- `grep -n 'AddOpenTelemetry' src/FrigateRelay.Host/HostBootstrap.cs` returns one match.
- `grep -n 'AddSource("FrigateRelay")' src/FrigateRelay.Host/HostBootstrap.cs` returns one match.
- `grep -n 'AddMeter("FrigateRelay")' src/FrigateRelay.Host/HostBootstrap.cs` returns one match.
- `grep -n 'AddRuntimeInstrumentation' src/FrigateRelay.Host/HostBootstrap.cs` returns one match.
- `grep -n 'AddOtlpExporter' src/FrigateRelay.Host/HostBootstrap.cs` returns AT LEAST one match (within `if (!string.IsNullOrWhiteSpace(otlpEndpoint))` guard — manual review).
- `grep -n 'AddConsoleExporter\|OpenTelemetry.Exporter.Console' src/FrigateRelay.Host/` returns zero (D2).
- `grep -n 'ValidateObservability' src/FrigateRelay.Host/Configuration/StartupValidation.cs` returns at least one match.
- `dotnet build FrigateRelay.sln -c Release` clean.
- Negative test (manual sanity): set `Otel__OtlpEndpoint=not-a-uri` env var and run `dotnet run --project src/FrigateRelay.Host -c Release`; process exits non-zero with stderr containing `Otel:OtlpEndpoint 'not-a-uri' is not a valid absolute URI`.

## Verification

```bash
cd /mnt/f/git/frigaterelay

# Build clean
dotnet build FrigateRelay.sln -c Release 2>&1 | tail -5

# Serilog wiring shape
grep -hn 'UseSerilog\|WriteTo.Console\|WriteTo.File\|WriteTo.Seq' \
  src/FrigateRelay.Host/Program.cs src/FrigateRelay.Host/HostBootstrap.cs

# OpenTelemetry wiring shape
grep -hn 'AddOpenTelemetry\|AddSource("FrigateRelay")\|AddMeter("FrigateRelay")\|AddRuntimeInstrumentation\|AddOtlpExporter' \
  src/FrigateRelay.Host/HostBootstrap.cs

# Excluded packages remain excluded
git grep -nE 'Serilog\.AspNetCore|OpenTelemetry\.Exporter\.Console|App\.Metrics|OpenTracing|Jaeger\.' src/
# expect: zero

# No static Log.Logger leak
git grep -nE 'Log\.Logger\s*=' src/
# expect: zero

# appsettings.json shape (no leaked secrets/IPs)
python3 -c "import json; d=json.load(open('src/FrigateRelay.Host/appsettings.json')); assert d['Serilog']['Seq']['ServerUrl']=='' and d['Otel']['OtlpEndpoint']==''; print('OK')"

# Phase 2 secret-scan still clean
bash .github/scripts/secret-scan.sh

# Smoke (graceful shutdown via CLAUDE.md pattern)
dotnet run --project src/FrigateRelay.Host -c Release --no-build > /tmp/host.log 2>&1 &
sleep 3
kill -INT "$(pgrep -f 'FrigateRelay.Host/bin/Release/net10.0/FrigateRelay.Host$' | head -1)"
wait
grep -E 'Application is shutting down|^\[' /tmp/host.log | head -5
```
