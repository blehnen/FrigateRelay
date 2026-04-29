# Research: Phase 10 — Dockerfile, Multi-Arch Release Workflow, /healthz, ID-21 Serilog-path Validation

## Context

FrigateRelay is a .NET 10 Worker-SDK host (`Microsoft.NET.Sdk.Worker`) using
`Host.CreateApplicationBuilder` (not `WebApplication.CreateBuilder`).  Phase 9
wired Serilog + OpenTelemetry fully.  Phase 10 adds: (1) a `docker/Dockerfile`
for self-contained Alpine multi-arch images, (2) a `release.yml` GHA workflow,
(3) a `/healthz` HTTP endpoint, and (4) the ID-21 Serilog-path startup-
validation pass.  All architectural decisions (D1–D6, B1–B4) are locked in
`CONTEXT-10.md`; this document answers HOW to implement them.

---

## A. Host Wiring for `/healthz`

### A1 — Current entrypoint shape

`src/FrigateRelay.Host/Program.cs` (lines 1–17):

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);
HostBootstrap.ConfigureServices(builder);
var app = builder.Build();
HostBootstrap.ValidateStartup(app.Services);
await app.RunAsync();
```

The project SDK is `Microsoft.NET.Sdk.Worker`
(`src/FrigateRelay.Host/FrigateRelay.Host.csproj` line 1).  There is no
`WebApplication`, no Kestrel, no HTTP pipeline today.

### A2 — Recommended approach for adding /healthz

**Recommendation: switch `Program.cs` to `WebApplication.CreateBuilder` and
change the project SDK to `Microsoft.NET.Sdk.Web`.**

Rationale and analysis of the three options:

**Option (a) — Switch to `WebApplication.CreateBuilder` + `Microsoft.NET.Sdk.Web`**

`WebApplication` is built on the same generic-host infrastructure as
`Host.CreateApplicationBuilder`.  All existing `IHostedService` registrations
(`EventPump`, `ChannelActionDispatcher`) work identically — the host calls
`IHostedService.StartAsync` on every registered service regardless of whether
the builder is `WebApplication` or `HostApplicationBuilder`.
(Source: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/generic-host?view=aspnetcore-10.0 —
"When a host starts, it calls StartAsync on each IHostedService registered in
the service container.")

`HostBootstrap.ConfigureServices` currently accepts `HostApplicationBuilder`.
`WebApplicationBuilder` exposes the same `.Services`, `.Configuration`, and
`.Logging` surface so the signature change is mechanical:
`ConfigureServices(HostApplicationBuilder builder)` → accept
`IHostApplicationBuilder` (the shared interface) or overload for
`WebApplicationBuilder`. The `AddSerilog(IServiceCollection, ...)` overload
works identically.

`MapHealthChecks("/healthz")` requires `Microsoft.AspNetCore.Diagnostics.HealthChecks`
which is part of the `Microsoft.AspNetCore.App` shared framework — available at
zero NuGet cost once the SDK is `Microsoft.NET.Sdk.Web`.

Dockerfile size impact: switching from `runtime-deps` + self-contained
(D1 + D3) means the ASP.NET Core runtime is bundled INTO the publish output
regardless of SDK choice — there is NO size difference between the two SDKs
when publishing self-contained, because the ASP.NET Core assemblies come from
the self-contained publish, not the base image.  The `runtime-deps:10.0-alpine`
base image remains correct for both worker and web SDK.

**Option (b) — Kestrel as a separate `IHostedService`**

Technically feasible: register a `KestrelHostedService` that manually
configures `KestrelServer` and a minimal `IApplicationBuilder` pipeline.
Complexity is high (manually wiring `IServer`, `IHttpContextFactory`,
`RequestDelegate`) and provides no benefit over option (a).  Reject.

**Option (c) — Raw `TcpListener` health channel**

Explicitly forbidden by CONTEXT-10.md D2: "Do not introduce a raw TcpListener
health channel."  Reject.

**Conclusion:** Change `<Project Sdk="...">` from `Microsoft.NET.Sdk.Worker`
to `Microsoft.NET.Sdk.Web` in `FrigateRelay.Host.csproj`.  Update `Program.cs`
to use `WebApplication.CreateBuilder`.  Add `builder.Services.AddHealthChecks()`
and `app.MapHealthChecks("/healthz", ...)`.

`HostBootstrap.ConfigureServices` signature must be updated; since
`WebApplicationBuilder` does not implement `HostApplicationBuilder`, the
cleanest change is to accept `WebApplicationBuilder` directly (the only caller
is `Program.cs`; integration tests using `HostApplicationBuilder` will need a
parallel or refactored path).

Kestrel listen port: configure `http://+:8080` via `appsettings.json`
(`Kestrel:Endpoints:Http:Url`) or environment variable `ASPNETCORE_URLS`.
CONTEXT-10.md D5 specifies port 8080 for the smoke curl step.  EXPOSE 8080
in the Dockerfile.  Alpine ships `wget` (not `curl`) so `HEALTHCHECK` uses
`wget -q --spider http://localhost:8080/healthz || exit 1` per CONTEXT-10.md
defaults table.

### A3 — MQTT connection state surface

`src/FrigateRelay.Sources.FrigateMqtt/FrigateMqttEventSource.cs` line 216:

```csharp
if (_client.IsConnected)
```

`IMqttClient.IsConnected` is a `bool` property exposed by MQTTnet v5's
`MqttClient`.  It is accessed today in `DisposeAsync` (line 216).

**There is no `IMqttConnectionStatus` service today.**  The healthcheck
implementation has two design options:

- **Option 1 (recommended):** Introduce a minimal `IMqttConnectionStatus`
  singleton (`bool IsConnected { get; }`) that `FrigateMqttEventSource` updates
  via event callbacks.  Register it in the plugin registrar.  The health check
  injects it.  This keeps the health-check layer decoupled from the MQTT client
  implementation.

- **Option 2:** Inject `FrigateMqttEventSource` directly into the health check
  and call `_client.IsConnected` via the event source.  Simpler but couples the
  health check to the concrete source type.

CONTEXT-10.md D4 says "avoid polling — react to the MQTT client's
connect/disconnect events."  `MQTTnet`'s `IMqttClient` exposes
`ConnectedAsync` and `DisconnectedAsync` events (already handled implicitly
through the reconnect loop in `FrigateMqttEventSource`).  The reconnect loop
at lines 99–135 calls `_client.ConnectAsync` and logs connected/disconnected;
`_client.IsConnected` is the authoritative flag.

**Recommendation:** Introduce `IMqttConnectionStatus` as a singleton updated by
`FrigateMqttEventSource` calling `SetConnected(true/false)` inside the
reconnect loop.  Register in `FrigateMqtt.PluginRegistrar`.  The `IHealthCheck`
implementation injects `IMqttConnectionStatus`.

### A4 — Startup completion and `IHostApplicationLifetime`

`IHostApplicationLifetime` is registered automatically by the generic host
(confirmed by Microsoft Learn docs: "The .NET Generic Host automatically
registers IHostApplicationLifetime").  No existing usage in the codebase today
(grep of `HostBootstrap.cs` and `Program.cs` shows no reference).

For the readiness check, CONTEXT-10.md D4 specifies:
1. `IsConnected == true` on the MQTT client, AND
2. Every `IHostedService` has completed `StartAsync` (host is past
   `ApplicationStarted`).

**Recommendation:** In the `MqttConnectionHealthCheck : IHealthCheck`, inject
`IHostApplicationLifetime` and check
`_lifetime.ApplicationStarted.IsCancellationRequested` (the token fires when
all `StartAsync` calls have completed).  Combine with the `IMqttConnectionStatus`
check.  Subscribe once (in a one-shot `ApplicationStarted.Register(...)`) or
simply query the cancellation token on each request — both are safe.  The
query-on-each-request approach is simpler (no state machine) and appropriate for
a `/healthz` endpoint that is polled at low frequency.

```csharp
// Pseudocode for the health check
var started = _lifetime.ApplicationStarted.IsCancellationRequested; // true once past startup
var connected = _mqttStatus.IsConnected;
if (started && connected)
    return HealthCheckResult.Healthy();
return HealthCheckResult.Unhealthy($"started={started} mqttConnected={connected}");
```

The `MapHealthChecks` response writer should return a short JSON body listing
which check failed (per CONTEXT-10.md D4).  Use a custom `ResponseWriter`
serializing `HealthReport` to JSON using `System.Text.Json` (no third-party UI
package — per CONTEXT-10.md D2 "Do not depend on HealthChecks.UI").

---

## B. Serilog Path Validation (B1 — closes ID-21)

### B1 — ID-21 problem statement

`.shipyard/ISSUES.md` ID-21 (lines 269–279):

> The file sink path is hard-coded `"logs/frigaterelay-.log"` (relative, safe)
> currently.  But `Serilog:File:Path` is honored by `ReadFrom.Configuration` if
> set in env vars or `appsettings.Local.json`.  On a container running as root
> (Phase 10 concern), this could redirect log output to arbitrary paths and
> overwrite system files.
>
> Mitigation: Phase 10 Dockerfile MUST run as non-root user (already in
> CLAUDE.md).  Optionally validate `Serilog:File:Path` for `..` segments at
> startup.

CONTEXT-10.md B1 extends this to also reject:
- Paths beginning with `/` outside an allowlist (`/var/log/frigaterelay/`,
  `/app/logs/`).
- Paths beginning with `\\` (UNC).

### B2 — Current Serilog configuration surface

`src/FrigateRelay.Host/appsettings.json` (lines 8–19): the file contains a
`Serilog` section with only `MinimumLevel` and `Seq:ServerUrl`.  **There is no
`Serilog:WriteTo` or `Serilog:File:Path` key in committed config.**

`src/FrigateRelay.Host/HostBootstrap.cs` lines 34–51: Serilog is configured
programmatically via the fluent API:

```csharp
builder.Services.AddSerilog((services, lc) =>
{
    lc.ReadFrom.Configuration(builder.Configuration)  // line 36
      ...
      .WriteTo.File(
          path: "logs/frigaterelay-.log",             // line 42
          rollingInterval: RollingInterval.Day,
          retainedFileCountLimit: 7,
          ...);
```

`ReadFrom.Configuration(builder.Configuration)` at line 36 will honor a
`Serilog:WriteTo` array in config if present, which could include a `File` sink
with an operator-controlled `Args:path`.  The `Serilog.Settings.Configuration`
package (already in `Host.csproj` line 32) supports this.

**The validation target is `builder.Configuration["Serilog:WriteTo:0:Args:path"]`
or more generically any `Serilog:WriteTo:*:Args:path` key.**  The validator
should iterate `configuration.GetSection("Serilog:WriteTo").GetChildren()` and
for each child that has `Args:path`, apply the rules.

### B3 — `StartupValidation.ValidateAll` location and collect-all pattern

The file was not found at
`src/FrigateRelay.Host/Configuration/StartupValidation.cs` or
`src/FrigateRelay.Host/Startup/StartupValidation.cs`.  Based on `HostBootstrap.cs`
line 143:

```csharp
StartupValidation.ValidateAll(services, subsOpts);
```

The type is `StartupValidation` in namespace `FrigateRelay.Host` (same
namespace as `HostBootstrap`).  The architect must locate the file with:

```bash
find src/FrigateRelay.Host -name 'StartupValidation.cs'
```

The collect-all pattern (CLAUDE.md D7, CONTEXT-10.md cross-cutting):
- `ValidateAll` allocates `List<string> errors`, calls each pass with it, then
  throws one `InvalidOperationException` if `errors.Count > 0`.
- Each pass takes `List<string> errors` as a parameter, calls `errors.Add(...)`,
  never throws inside the pass.

The new `ValidateSerilogPath` pass MUST follow the identical pattern:

```csharp
internal static void ValidateSerilogPath(IConfiguration configuration, List<string> errors)
{
    var writeTo = configuration.GetSection("Serilog:WriteTo");
    foreach (var sink in writeTo.GetChildren())
    {
        var path = sink["Args:path"];
        if (string.IsNullOrWhiteSpace(path)) continue;

        if (path.Contains(".."))
            errors.Add($"Serilog:WriteTo path '{path}' contains '..' path traversal segments.");
        else if (path.StartsWith('\\'))
            errors.Add($"Serilog:WriteTo path '{path}' is a UNC path and is not permitted.");
        else if (path.StartsWith('/'))
        {
            var allowlist = new[] { "/var/log/frigaterelay/", "/app/logs/" };
            if (!allowlist.Any(prefix => path.StartsWith(prefix, StringComparison.Ordinal)))
                errors.Add($"Serilog:WriteTo path '{path}' is an absolute path outside the allowed prefixes (/var/log/frigaterelay/, /app/logs/).");
        }
    }
}
```

`ValidateAll` must be updated to call `ValidateSerilogPath(configuration, errors)`.
This requires `IConfiguration` to be passed into `ValidateAll`, or extracted
from `IServiceProvider` via `services.GetRequiredService<IConfiguration>()`.
Check whether `ValidateAll`'s current signature already accepts `IConfiguration`
(see B3 above — the architect must read the actual file to confirm).

**Important:** The `ValidateObservability` pass already uses `IConfiguration`
(per ID-16/ID-17 resolution), so `ValidateAll` likely already receives it.
Confirm from the source file.

### B4 — Test location and pattern

`tests/FrigateRelay.Host.Tests/FrigateRelay.Host.Tests.csproj` confirmed at
line 1.  Based on Phase 8/9 work, the test layout is:
- `tests/FrigateRelay.Host.Tests/Configuration/` — profile resolution,
  `ConfigSizeParityTest`, action validation tests.
- `tests/FrigateRelay.Host.Tests/Observability/` — `ValidateObservabilityTests.cs`
  (ID-16 resolution, 3 tests using `new ConfigurationBuilder().AddInMemoryCollection(...).Build()`).

The new tests live at:
`tests/FrigateRelay.Host.Tests/Configuration/SerilogPathValidationTests.cs`

Pattern (mirror `ValidateObservabilityTests.cs`):
- Inject config via `new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Serilog:WriteTo:0:Name"] = "File", ["Serilog:WriteTo:0:Args:path"] = "../../etc/passwd" }).Build()`.
- Call `ValidateSerilogPath(config, errors)` (or `ValidateAll` with a minimal service provider).
- Assert `errors` contains the expected message.

`CapturingLogger<T>` shared helper lives in
`tests/FrigateRelay.TestHelpers/FrigateRelay.TestHelpers.csproj`
(ID-11 resolution).  Serilog-path validation tests do NOT require
`CapturingLogger<T>` — they are pure configuration-parse tests.

Test cases to cover (minimum):
1. Path with `..` → error containing `"path traversal"`.
2. Path starting with `\\` → error containing `"UNC"`.
3. Absolute path outside allowlist (`/etc/passwd`) → error.
4. Allowed absolute path (`/var/log/frigaterelay/app.log`) → no error.
5. Relative safe path (`logs/frigaterelay-.log`) → no error.
6. No `Serilog:WriteTo` section in config → no error.

---

## C. Publish and Dockerfile

### C1 — Current Host.csproj publish flags

`src/FrigateRelay.Host/FrigateRelay.Host.csproj` (full file read):

- `<Project Sdk="Microsoft.NET.Sdk.Worker">` — line 1 (must change to
  `Microsoft.NET.Sdk.Web` per A2)
- **No** `<RuntimeIdentifier>` present
- **No** `<SelfContained>` present
- **No** `<PublishSingleFile>` present
- **No** `<PublishTrimmed>` present
- **No** `<PublishAot>` present
- `<TargetFramework>` is NOT explicitly set — inherited from
  `Directory.Build.props` (`net10.0`).

Changes required for D3 (self-contained, untrimmed):

```xml
<SelfContained>true</SelfContained>
<PublishSingleFile>false</PublishSingleFile>
<PublishTrimmed>false</PublishTrimmed>
<PublishAot>false</PublishAot>
```

`<RuntimeIdentifier>` should NOT be set in the csproj because the Dockerfile
uses a multi-stage build with two `dotnet publish` calls, one per RID
(`linux-musl-x64` for amd64 stage, `linux-musl-arm64` for arm64 stage), passed
via `-r <rid>` on the command line.  Locking a single RID in the csproj would
break the multi-arch build.

Decision note: The switch from `Microsoft.NET.Sdk.Worker` to
`Microsoft.NET.Sdk.Web` does not change any publish behavior — both SDKs
produce the same publish output for self-contained, untrimmed.

### C2 — Package references and musl compatibility

**Host.csproj packages (lines 19–35):**

| Package | Version | musl note |
|---------|---------|-----------|
| `Microsoft.Extensions.Hosting` | 10.0.7 | musl-safe (managed only) |
| `Microsoft.Extensions.Configuration.UserSecrets` | 10.0.7 | musl-safe |
| `Microsoft.Extensions.Caching.Memory` | 10.0.7 | musl-safe |
| `OpenTelemetry.Extensions.Hosting` | 1.15.3 | musl-safe |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | 1.15.3 | **See note** |
| `OpenTelemetry.Instrumentation.Runtime` | 1.15.1 | musl-safe |
| `Serilog.Extensions.Hosting` | 10.0.0 | musl-safe (managed) |
| `Serilog.Settings.Configuration` | 10.0.0 | musl-safe |
| `Serilog.Sinks.Console` | 6.1.1 | musl-safe |
| `Serilog.Sinks.File` | 7.0.0 | musl-safe |
| `Serilog.Sinks.Seq` | 9.0.0 | musl-safe |

**OTel OTLP exporter musl note:** The historical Alpine incompatibility
(issue #1251, 2020) was caused by `grpc_csharp_ext.so` native library glibc
dependency.  As of OpenTelemetry.Exporter.OpenTelemetryProtocol 1.11.0+, the
SDK uses a **custom managed gRPC implementation** that removes dependencies on
`Google.Protobuf`, `Grpc`, and `Grpc.Net.Client`.  Version 1.15.3 (in use) is
well past this threshold.  **The OTLP exporter is expected to be musl-safe at
1.15.3.**  However, the default protocol is gRPC (HTTP/2).  If HTTP/2 causes
issues on Alpine (uncommon but possible with some kernel configurations), the
operator can set `OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf` to switch to HTTP/1.1.
Document this as a fallback in the Dockerfile comment block.

**MQTTnet (in `FrigateRelay.Sources.FrigateMqtt`):** MQTTnet v5 is confirmed to
run on Linux Docker (production reports in GitHub Discussion #1355).  MQTTnet
v5 uses managed TCP sockets via .NET's `TcpClient` / `Socket` — no native
library dependency.  **musl-safe.**

**Plugin projects referenced from Host.csproj (lines 45–50):**
- `FrigateRelay.Sources.FrigateMqtt` — MQTTnet v5, managed sockets, musl-safe.
- `FrigateRelay.Plugins.BlueIris` — `IHttpClientFactory`, managed, musl-safe.
- `FrigateRelay.Plugins.FrigateSnapshot` — `IHttpClientFactory`, managed, musl-safe.
- `FrigateRelay.Plugins.Pushover` — `IHttpClientFactory`, managed, musl-safe.
- `FrigateRelay.Plugins.CodeProjectAi` — `IHttpClientFactory`, managed, musl-safe.

No native library dependencies identified.  **All packages are expected to be
musl-compatible** at current versions.  The Docker smoke step (D5) is the
empirical proof gate.

### C3 — Base image tags and digests

The canonical tag for .NET 10 Alpine runtime-deps is:
`mcr.microsoft.com/dotnet/runtime-deps:10.0-alpine`

As of April 2026, the tag resolves to Alpine 3.23-based images
(tag variant `10.0.x-alpine3.23`).  The MCR portal confirms tags:
`10.0-alpine`, `10.0-alpine3.23`, `10.0.6-alpine3.23`.
(Source: https://mcr.microsoft.com/en-us/artifact/mar/dotnet/runtime-deps/tag/10.0-alpine)

**Exact sha256 digests could not be retrieved** without running
`docker pull` or a Docker Hub API call.  This is a known gap.

**Decision Required (architect):** Pin the digest in the Dockerfile after the
build agent runs `docker pull mcr.microsoft.com/dotnet/runtime-deps:10.0-alpine`
and captures the manifest digest.  Use the form:

```dockerfile
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-alpine@sha256:<FILL_IN_AT_BUILD_TIME> AS runtime
```

The readable tag is retained for humans; the digest ensures supply-chain
integrity.  Dependabot `docker` ecosystem (B2) will update both atomically.

For the SDK build stage:
`mcr.microsoft.com/dotnet/sdk:10.0-alpine` (for Alpine-native builds) or
`mcr.microsoft.com/dotnet/sdk:10.0` (Debian-based, cross-compiles to musl via RID).

**Recommendation:** Use `mcr.microsoft.com/dotnet/sdk:10.0` (Debian SDK) as the
build stage — cross-compiling to `linux-musl-x64` and `linux-musl-arm64` via
`-r linux-musl-x64` is fully supported and avoids Alpine-in-Alpine build issues.
The Jenkinsfile already uses `mcr.microsoft.com/dotnet/sdk:10.0` (line 32).

### C4 — Jenkinsfile digest pin scope

`Jenkinsfile` line 32:
```groovy
image 'mcr.microsoft.com/dotnet/sdk:10.0'
```

Currently **tag-pinned only** (no sha256 digest).  CONTEXT-10.md B3 requires
digest-pinning both the Dockerfile runtime base AND the Jenkinsfile SDK base.

Phase 10 must update `Jenkinsfile` line 32 to:
```groovy
image 'mcr.microsoft.com/dotnet/sdk:10.0@sha256:<FILL_IN>'
```

Same "fill in at build time" constraint as C3.  Dependabot `docker` ecosystem
(B2) watches `docker/Dockerfile` — the Jenkinsfile is NOT automatically watched
unless added to the `dependabot.yml` `docker` block.

**Decision Required (architect):** Decide whether to add the Jenkinsfile to the
Dependabot `docker` ecosystem watch.  Dependabot supports this via:
```yaml
- package-ecosystem: "docker"
  directory: "/"     # scans Dockerfile in root
```
But the Jenkinsfile is a Groovy file, not a Dockerfile.  Dependabot `docker`
ecosystem only scans files named `Dockerfile` (or matching patterns).  The
Jenkinsfile digest will need **manual update** via a project convention, or a
companion `docker/Jenkinsfile.Dockerfile` stub approach.  Flag for architect.

### C5 — .dockerignore

No `.dockerignore` exists at the repo root (confirmed — file was not found).
Phase 10 must create it.  Required exclusions based on repo contents:

```dockerignore
# Build outputs
**/bin/
**/obj/

# VCS
.git/
.gitignore

# Test projects (not needed in image)
tests/

# Shipyard planning docs (not shipped)
.shipyard/

# Developer overrides (secrets must not enter image)
**/appsettings.Local.json
**/*.user
.env
.env.*

# Editor/IDE
.vs/
.vscode/
*.DotSettings

# Coverage output
coverage/

# NuGet cache
.nuget-cache/

# CI scripts (not needed in image)
.github/
Jenkinsfile
```

The `docker/` directory itself is NOT excluded — the Dockerfile and compose
example live there.

---

## D. Release Workflow

### D1 — Current ci.yml shape

`.github/workflows/ci.yml` (full file read):

- **Trigger:** `push` and `pull_request` (all branches, all events).
- **Matrix:** `os: [ubuntu-latest, windows-latest]` (line 31).
- **Action versions:**
  - `actions/checkout@v4` (line 39)
  - `actions/setup-dotnet@v4` (line 42, with `global-json-file: global.json`)
- **Concurrency group:** `ci-${{ github.ref }}`, cancel-in-progress: true (lines 18–20).
- **Test step:** delegates to `.github/scripts/run-tests.sh` via `shell: bash`;
  integration tests skipped on Windows (line 57–61).
- No coverage, no artifact upload, no TRX — intentional per CLAUDE.md CI section.

### D2 — GHA action versions for release.yml

Confirmed current major versions as of April 2026:

| Action | Version | Rationale |
|--------|---------|-----------|
| `docker/setup-qemu-action` | `@v4` | Current major; enables ARM64 emulation for buildx cross-compile |
| `docker/setup-buildx-action` | `@v4` | Current major; required for `--platform linux/amd64,linux/arm64` |
| `docker/login-action` | `@v4` | Current major; GHCR login via `GITHUB_TOKEN` |
| `docker/build-push-action` | `@v7` | Current major; handles multi-arch manifest push |
| `docker/metadata-action` | `@v6` | Current major; generates semver tags + `latest` from git tag `v*` |
| `actions/checkout` | `@v4` | Matches ci.yml for consistency |

Source: https://docs.docker.com/build/ci/github-actions/multi-platform/

### D3 — Release smoke step detail

Per CONTEXT-10.md D5, the workflow shape after building the amd64 image:

```yaml
# Service container (runs for the lifetime of the job)
services:
  mosquitto:
    image: eclipse-mosquitto:2
    ports:
      - 1883:1883
    # Mosquitto 2.x requires explicit listener config; use options block:
    options: >-
      --health-cmd "mosquitto_sub -t '$$SYS/#' -C 1 -W 2 || exit 1"
      --health-interval 5s
      --health-retries 6
      --health-timeout 3s

steps:
  # ... build amd64 image first (docker/build-push-action with load: true) ...

  - name: Smoke — start FrigateRelay
    run: |
      docker run -d --name fr \
        --network host \
        -e FrigateMqtt__Server=localhost \
        -e FrigateMqtt__Port=1883 \
        -e ASPNETCORE_URLS=http://+:8080 \
        <image-tag>

  - name: Smoke — poll /healthz
    run: |
      for i in $(seq 1 30); do
        if curl -fsS http://localhost:8080/healthz; then
          echo "healthz OK"
          exit 0
        fi
        sleep 1
      done
      echo "healthz did not return 200 within 30 seconds"
      docker logs fr
      exit 1

  # On success: multi-arch buildx push
```

**GHA service containers** run as sibling containers on the job's Docker
network.  When `network: host` is used for the `docker run` step, both the
service container (Mosquitto) and the FrigateRelay container share the host
network namespace, so `FrigateMqtt__Server=localhost` resolves correctly.
Alternative: use the job's bridge network and the container name as hostname
(`FrigateMqtt__Server=mosquitto`) — this requires `--network <network-name>`
which requires knowing the GHA network name (typically `github_network_*`).

**Recommended approach:** use `--network host` for the `docker run` step and
`FrigateMqtt__Server=127.0.0.1` — avoids needing the GHA bridge network name.
The mosquitto service container must also bind to `0.0.0.0` (default for
eclipse-mosquitto:2).

Note: `eclipse-mosquitto:2` by default starts with no listener configured and
will not accept anonymous connections without a config file.  The smoke step
needs a minimal Mosquitto config:

```
listener 1883
allow_anonymous true
```

Mount this via a tmpdir or inline heredoc in the GHA step before starting
mosquitto, or use the service container's `options` to pass a command:
`--cmd "mosquitto -c /mosquitto/config/mosquitto.conf"` with the config
created by a prior step.

**Decision Required (architect):** Determine the exact mosquitto service
container config injection strategy (bind-mount of a config file vs.
`options` with inline config).

Port for `/healthz`: **8080** inside the container (CONTEXT-10.md D5, and
the HEALTHCHECK in the Dockerfile).

### D4 — appsettings.Example.json as smoke-config baseline

`config/appsettings.Example.json` (full file read): contains `Profiles` and
`Subscriptions` with production camera names.  It does NOT contain `FrigateMqtt`,
`BlueIris`, `Pushover`, or `Otel` sections.

For the smoke test, a minimal valid config that passes startup validation needs:
- A `FrigateMqtt` section (server, port — no secrets).
- At least one `Subscription` referencing a valid `Profile` or with an inline
  empty `Actions` array (empty actions pass `ValidateActions`).
- No `BlueIris`, `Pushover`, `CodeProjectAi` sections (plugin registrars gate
  on config section existence per `HostBootstrap.cs` lines 116–126).

A `docker/appsettings.Smoke.json` is warranted — it is a DIFFERENT concern
from `docker/appsettings.Docker.json` (B4, console-only logging override):

```json
{
  "FrigateMqtt": {
    "Server": "localhost",
    "Port": 1883,
    "Topic": "frigate/events"
  },
  "Subscriptions": [
    { "Name": "Smoke", "Camera": "test", "Label": "person", "Actions": [] }
  ]
}
```

This must NOT contain secrets and must pass `ValidateAll`.  Confirm whether
an empty `Actions` array satisfies `ValidateActions` — if not, provide one
inline action (e.g., `"Plugin": "BlueIris"`) AND the `BlueIris` section with
a stub URL.

---

## E. CI Mirror

### E1 — New test project assessment

Phase 10 adds:
1. `SerilogPathValidationTests.cs` → lands in
   `tests/FrigateRelay.Host.Tests/Configuration/` (existing project, no new project).
2. `/healthz` integration test (`HealthzEndpointTests`) → could land in
   `tests/FrigateRelay.IntegrationTests/` (existing project) — the integration
   test suite already uses Testcontainers and WireMock.

**No new test project is expected for Phase 10.**  Both new test classes fit
into existing projects.

### E2 — run-tests.sh project discovery

`.github/scripts/run-tests.sh` lines 44:

```bash
mapfile -t PROJECTS < <(find tests -maxdepth 2 -name '*Tests.csproj' -type f | sort)
```

**Auto-discovery via `find`.** Adding a new test project requires NO changes
to `run-tests.sh` or `Jenkinsfile` — the glob picks it up automatically.
(File header comment, lines 18–19, explicitly states this.)

If Phase 10 somehow requires a new test project (e.g., `FrigateRelay.Docker.Tests`),
both `ci.yml` (via `run-tests.sh`) and `Jenkinsfile` (via `run-tests.sh
--coverage`) would pick it up automatically.  No file edits needed in either.

---

## F. Open Issues and Cross-Cutting Greps

### F1 — Issue scope confirmation

Per CONTEXT-10.md "Out of scope" section (line 174):
Only **ID-21** is closed by Phase 10.  All other open IDs remain deferred:
ID-1, 3, 4, 7, 8, 9, 13, 14, 15, 18, 19, 20, 22.

Collision check — Phase 10 surfaces that touch existing issue areas:

| Issue | Phase 10 surface | Collision risk |
|-------|-----------------|----------------|
| ID-13 (log-spoofing via operator names) | `ValidateSerilogPath` adds operator-controlled path strings to error messages | **Low** — follow same sanitization note as ID-13; do not interpolate raw path into structured log, only into the `errors.Add()` string which goes into the aggregated `InvalidOperationException` |
| ID-15 (secret-scan RFC-1918 gaps) | Compose example must use service names not IPs | **Low** — use `mosquitto` hostname in compose; no IP literals |
| ID-22 (Task.Delay in observability tests) | Phase 10 adds no new polling-based tests | No collision |
| ID-8 (PASS_THROUGH_ARGS not forwarded in --coverage branch) | run-tests.sh unchanged | No collision |

### F2 — Cross-cutting grep status at Phase 10 start

**These greps must be run by the architect before plan approval and confirmed
zero:**

```bash
# Must return zero matches
git grep ServicePointManager src/
git grep -nE '\.(Result|Wait)\(' src/
git grep -nE 'App\.Metrics|OpenTracing|Jaeger\.' src/
git grep -rn '192\.168\.' src/ docker/ .github/
git grep -rn '10\.[0-9]\{1,3\}\.[0-9]\{1,3\}\.' src/ docker/
```

**Phase 10 new surface that risks introducing matches:**

- `docker/docker-compose.example.yml` — MUST use `ghcr.io/<owner>/frigaterelay:latest`
  with `FrigateMqtt__Server` pointing to a service name, NOT a hard-coded IP.
  Comment block (CONTEXT-10.md D6): "Point FrigateMqtt__Server at your existing
  Mosquitto / Frigate broker."  No IP example.
- `docker/appsettings.Docker.json` (B4) — Console-only Serilog config.  No IPs,
  no secrets.
- `docker/appsettings.Smoke.json` — smoke config.  `FrigateMqtt:Server` must be
  `"localhost"` or `"mosquitto"` (service name), not an RFC-1918 address.
- `.github/workflows/release.yml` — any `docker run` step uses service names or
  `localhost`; no hard-coded IPs.

---

## Comparison Matrix: `/healthz` Transport Options

| Criteria | (a) WebApplication.CreateBuilder | (b) KestrelHostedService | (c) TcpListener |
|----------|----------------------------------|--------------------------|-----------------|
| SDK change required | `Worker` → `Web` | No change | No change |
| IHostedService compat | Full (same generic host) | Full | Full |
| MapHealthChecks/AddHealthChecks | Native (`Microsoft.AspNetCore.Diagnostics.HealthChecks`) | Manual | Manual |
| Docker/K8s probe support | HTTP 200/503 — standard | HTTP 200/503 | TCP only — non-standard |
| Dockerfile size impact | Zero (self-contained bundles ASP.NET regardless) | Zero | Zero |
| Maintenance burden | Low (framework-managed) | High (manual wiring) | High |
| CONTEXT-10 compliant | Yes (D2) | Yes (D2) | **No — explicitly forbidden** |
| Recommended | **Yes** | No | No |

---

## Risks and Unknowns

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| OTel gRPC exporter fails on Alpine musl at runtime | Low (v1.15.3 is managed gRPC) | Med | Set `OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf` as default in `appsettings.Docker.json`; document gRPC as opt-in |
| sha256 digest pins unavailable until `docker pull` | Certain (can't retrieve without Docker daemon) | Low | Architect fills in digest after first build; Dependabot keeps it current |
| Mosquitto 2.x smoke container requires `allow_anonymous true` config | High (v2 default rejects anon) | High — smoke step fails silently | Inject `listener 1883\nallow_anonymous true` config before service container start |
| `WebApplicationBuilder` vs `HostApplicationBuilder` HostBootstrap.cs signature | Med (one caller to update) | Med | Update `ConfigureServices` parameter type; integration test host setup needs parallel fix |
| `ValidateAll` current signature may not accept `IConfiguration` | Unknown (file not found at searched paths) | Med | Architect greps: `find src/FrigateRelay.Host -name 'StartupValidation.cs'` and reads the method signature |
| `Actions: []` (empty list) may not pass `ValidateActions` | Unknown | Med — smoke config may fail startup | Test with empty actions list; if it fails, provide one stub BlueIris action in smoke config |
| Jenkinsfile digest pin not tracked by Dependabot | Med | Low (manual rot over time) | Document in CLAUDE.md; add a comment in Jenkinsfile with update instructions |

---

## Decision Required (Architect)

1. **Mosquitto smoke config injection** — bind-mount approach vs. inline heredoc
   in the GHA `release.yml` step.  Recommend: write a `docker/mosquitto-smoke.conf`
   file and mount it in the `docker run` step.

2. **`ValidateAll` current signature** — read `StartupValidation.cs` to confirm
   whether `IConfiguration` is already a parameter; if not, add it now (needed
   for B1 `ValidateSerilogPath` pass).

3. **Jenkinsfile Dependabot coverage** — decide whether to track Jenkinsfile SDK
   image digest manually (documented convention) or via a wrapper Dockerfile.

4. **Smoke config empty-actions behavior** — run `dotnet run --project src/FrigateRelay.Host`
   with the proposed `appsettings.Smoke.json` locally to confirm no startup validation
   failure before encoding it in the plan.

5. **`HostBootstrap.ConfigureServices` parameter type** — after switching to
   `WebApplicationBuilder`, the method signature must change.  The architect must
   decide whether to make it accept `IHostApplicationBuilder` (interface) for
   reuse by integration test setups, or accept `WebApplicationBuilder` (concrete)
   with a separate overload for tests.

---

## Sources

1. `.shipyard/phases/10/CONTEXT-10.md` — Phase 10 locked decisions (D1–D6, B1–B4)
2. `.shipyard/ROADMAP.md` — Phase 10 deliverables and success criteria
3. `.shipyard/ISSUES.md` — ID-21 problem statement (lines 269–279)
4. `src/FrigateRelay.Host/Program.cs` — current entrypoint shape
5. `src/FrigateRelay.Host/FrigateRelay.Host.csproj` — SDK, packages, references
6. `src/FrigateRelay.Host/HostBootstrap.cs` — `ConfigureServices`, Serilog wiring, `ValidateStartup`
7. `src/FrigateRelay.Host/appsettings.json` — committed Serilog config
8. `src/FrigateRelay.Sources.FrigateMqtt/FrigateMqttEventSource.cs` — `IMqttClient.IsConnected` usage
9. `config/appsettings.Example.json` — 9-subscription production fixture
10. `tests/FrigateRelay.Host.Tests/FrigateRelay.Host.Tests.csproj` — test project packages
11. `.github/workflows/ci.yml` — matrix, action versions, concurrency
12. `.github/scripts/run-tests.sh` — auto-discovery `find` glob (line 44)
13. `Jenkinsfile` — SDK image tag (line 32), coverage invocation
14. `.github/dependabot.yml` — current ecosystems (nuget + github-actions; docker absent)
15. https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/generic-host?view=aspnetcore-10.0 — IHostedService lifecycle, IHostApplicationLifetime
16. https://docs.docker.com/build/ci/github-actions/multi-platform/ — GHA action versions for multi-arch
17. https://opentelemetry.io/docs/languages/dotnet/exporters/ — OTLP default protocol (gRPC)
18. https://github.com/open-telemetry/opentelemetry-dotnet/issues/1251 — historical Alpine musl issue (resolved in managed gRPC era)
19. https://mcr.microsoft.com/en-us/artifact/mar/dotnet/runtime-deps/tag/10.0-alpine — runtime-deps tag reference

---

## Uncertainty Flags

- **StartupValidation.cs location** — file not found at either searched path.
  Architect must run `find src/FrigateRelay.Host -name 'StartupValidation.cs'`
  and read the `ValidateAll` signature before writing the plan.

- **OTel OTLP default protocol in 1.15.3** — the gRPC-vs-HTTP/protobuf default
  must be verified against `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.15.3
  changelog.  If still gRPC by default, recommend switching the Docker default to
  `http/protobuf` in `appsettings.Docker.json` to avoid Alpine HTTP/2 edge cases.

- **sha256 digest values** for `runtime-deps:10.0-alpine` and `sdk:10.0` — cannot
  be retrieved without a Docker daemon.  Architect must `docker pull` and capture
  digests before the Dockerfile is finalized.

- **`Actions: []` startup-validation behavior** — unknown without tracing
  `ValidateActions` implementation.  If the validator treats an empty list as
  "no actions configured" and emits an error, the smoke config needs adjustment.

- **`WebApplication.CreateBuilder` + `HostBootstrap.ConfigureServices` refactor
  scope** — the integration test suite (`FrigateRelay.IntegrationTests`) may
  directly call `HostBootstrap.ConfigureServices(HostApplicationBuilder builder)`.
  Architect must search integration test setup code for this call before deciding
  on the parameter type change.
