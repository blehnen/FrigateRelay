---
phase: 10-docker-release
plan: 1.3
wave: 1
dependencies: []
must_haves:
  - SelfContained=true, PublishSingleFile=false, PublishTrimmed=false, PublishAot=false in Host.csproj
  - appsettings.Docker.json overrides Serilog WriteTo to Console-only
  - appsettings.Smoke.json provides minimal config that passes ValidateAll
  - .dockerignore excludes bin/obj/tests/.shipyard/.git/secrets
files_touched:
  - src/FrigateRelay.Host/FrigateRelay.Host.csproj  # owns: <PropertyGroup> publish flags + new <ItemGroup><None Include="appsettings.Docker.json"/> ONLY
  - src/FrigateRelay.Host/appsettings.Docker.json
  - docker/appsettings.Smoke.json
  - .dockerignore
tdd: false
risk: low
---

# Plan 1.3: Self-contained publish flags + Docker/Smoke configs + .dockerignore

## Context

Implements CONTEXT-10 D3 (self-contained, untrimmed publish), B4 (container Console-only logging), and the smoke-config baseline needed by PLAN-2.2's release workflow. Pure additive config and csproj changes — no behavior change to host code. Deliberately separate from PLAN-1.1 so the two plans can run in parallel without merge conflict, with strict file-section ownership (this plan owns `<PropertyGroup>` publish flags and any new `<ItemGroup><None Include="appsettings.Docker.json"/>` entries; PLAN-1.1 owns the SDK attribute and `<PackageReference>` block).

`ValidateActions` (read at architect time from `src/FrigateRelay.Host/StartupValidation.cs`) iterates `sub.Actions` and emits errors only for unknown plugin names — an empty `Actions: []` list produces zero errors. Smoke config exploits this: a single subscription with empty actions passes startup validation without needing any plugin sections (no `BlueIris`, `Pushover`, etc.) and therefore no secrets.

## Dependencies

None (Wave 1).

**File-section ownership note (cross-plan):** PLAN-1.1 owns `<Project Sdk="...">` and `<PackageReference>` items in `Host.csproj`. This plan owns `<PropertyGroup>` publish flags and any new `<ItemGroup><None>` content items. Builders MUST keep these sections distinct; if either plan accidentally edits the other's section, the second plan's commit will need to rebase.

## Tasks

### Task 1: Add publish flags to Host.csproj + appsettings.Docker.json (Console-only Serilog)

**Files:**
- `src/FrigateRelay.Host/FrigateRelay.Host.csproj` (modify — add a NEW `<PropertyGroup>` block (do not merge into the existing one owned by PLAN-1.1's edits) with `<SelfContained>true</SelfContained>`, `<PublishSingleFile>false</PublishSingleFile>`, `<PublishTrimmed>false</PublishTrimmed>`, `<PublishAot>false</PublishAot>`, `<InvariantGlobalization>false</InvariantGlobalization>`. Also add a `<ItemGroup>` with `<None Update="appsettings.Docker.json"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></None>`. Do NOT set `<RuntimeIdentifier>` — multi-arch publish passes RID per `dotnet publish -r` invocation.)
- `src/FrigateRelay.Host/appsettings.Docker.json` (create)

**Action:** create + modify

**Description:**
The publish flags are required to land on the `runtime-deps:10.0-alpine` base (the runtime is bundled into the publish output). Trimming and AOT are explicitly out per CONTEXT-10 D3 (reflection-heavy deps: MQTTnet codec discovery, OpenTelemetry exporter discovery, Serilog config binding, `IConfiguration.Bind`, plugin registrar discovery loop).

`appsettings.Docker.json` content — Console-only Serilog override (B4) so logs go to `stdout` for `docker logs` collection, not into the writable container layer:

```json
{
  "Serilog": {
    "Using": [ "Serilog.Sinks.Console" ],
    "WriteTo": [
      { "Name": "Console" }
    ]
  }
}
```

This file is loaded automatically by `WebApplication.CreateBuilder` when `ASPNETCORE_ENVIRONMENT=Docker`. The existing programmatic Serilog `.WriteTo.File(...)` configuration in `HostBootstrap.cs` remains active by default; `ReadFrom.Configuration(builder.Configuration)` does NOT remove sinks added programmatically — operators wanting strict console-only must omit programmatic File sink in a Docker code path. **For Phase 10 the simpler answer is: the file sink in HostBootstrap remains, but in the container it writes to a path inside the writable layer (`logs/frigaterelay-.log` relative to working dir) which is acceptable for v1 since stdout still receives Console output AND the path validation from PLAN-1.2 prevents abuse.** Document this trade-off as a one-line comment at the top of `appsettings.Docker.json`.

**Acceptance Criteria:**
- `grep -n '<SelfContained>true</SelfContained>' src/FrigateRelay.Host/FrigateRelay.Host.csproj` returns one match.
- `grep -nE '<PublishTrimmed>true|<PublishAot>true|<PublishSingleFile>true' src/FrigateRelay.Host/FrigateRelay.Host.csproj` returns zero matches.
- `test -f src/FrigateRelay.Host/appsettings.Docker.json`
- `python3 -c "import json; json.load(open('src/FrigateRelay.Host/appsettings.Docker.json'))"` exits 0 (valid JSON).
- `grep -n 'RuntimeIdentifier' src/FrigateRelay.Host/FrigateRelay.Host.csproj` returns zero matches.
- `dotnet build FrigateRelay.sln -c Release` exits 0.
- `dotnet publish src/FrigateRelay.Host -c Release -r linux-musl-x64 --self-contained true -o /tmp/fr-publish-test` exits 0 (smoke that the publish pipeline accepts the new flags).

### Task 2: Create docker/appsettings.Smoke.json

**Files:**
- `docker/appsettings.Smoke.json` (create)

**Action:** create

**Description:**
Minimal config that passes `StartupValidation.ValidateAll` end-to-end with no plugin sections, no secrets, no IPs. The smoke step in PLAN-2.2 will mount this file into the container via `-v` and start the host pointing at the Mosquitto sidecar.

```json
{
  "FrigateMqtt": {
    "Server": "localhost",
    "Port": 1883,
    "Topic": "frigate/events"
  },
  "Subscriptions": [
    {
      "Name": "Smoke",
      "Camera": "test",
      "Label": "person",
      "Actions": []
    }
  ]
}
```

`Actions: []` is intentionally empty: `ValidateActions` only emits errors for unknown plugin names, never for empty arrays — verified by reading `StartupValidation.ValidateActions` at architect time. No `BlueIris`, `Pushover`, `Validators` sections means the corresponding `PluginRegistrar` instances are never added (gated by `builder.Configuration.GetSection(...).Exists()` in `HostBootstrap.ConfigureServices`), so no secrets are required.

**Acceptance Criteria:**
- `test -f docker/appsettings.Smoke.json`
- `python3 -c "import json; json.load(open('docker/appsettings.Smoke.json'))"` exits 0.
- `grep -nE '192\.168\.|10\.[0-9]+\.[0-9]+\.[0-9]+|AppToken|UserKey|ApiKey' docker/appsettings.Smoke.json` returns zero matches (no secrets, no RFC-1918 IPs).

### Task 3: Create .dockerignore at repo root

**Files:**
- `.dockerignore` (create)

**Action:** create

**Description:**
Excludes build outputs, planning docs, secrets, IDE state, and CI scripts from Docker build context. Keeps the build context small (faster `docker build`) and prevents accidental secret ingestion (e.g., a developer's `appsettings.Local.json` containing a real Pushover token).

Required content:

```
# Build outputs
**/bin/
**/obj/

# VCS
.git/
.gitignore
.gitattributes

# Test projects (not needed in image)
tests/

# Shipyard planning docs (not shipped)
.shipyard/

# Developer overrides — secrets must not enter image
**/appsettings.Local.json
**/*.user
.env
.env.*

# Editor / IDE
.vs/
.vscode/
*.DotSettings*

# Coverage output
coverage/

# NuGet caches
.nuget-cache/

# CI scripts (not needed in image)
.github/
Jenkinsfile
```

Note: `docker/` is NOT excluded — Dockerfile + compose example live there.

**Acceptance Criteria:**
- `test -f .dockerignore`
- `grep -q '^\.shipyard/' .dockerignore`
- `grep -q '^\*\*/appsettings\.Local\.json' .dockerignore`
- `grep -q '^tests/' .dockerignore`
- `grep -q '^\.git/' .dockerignore`

## Verification

Run from repo root:

```bash
dotnet build FrigateRelay.sln -c Release
dotnet publish src/FrigateRelay.Host -c Release -r linux-musl-x64 --self-contained true -o /tmp/fr-publish-test

# JSON files valid
python3 -c "import json; json.load(open('src/FrigateRelay.Host/appsettings.Docker.json'))"
python3 -c "import json; json.load(open('docker/appsettings.Smoke.json'))"

# No secrets / IPs in committed config
git grep -nE '192\.168\.|AppToken=|UserKey=|ApiKey=' src/FrigateRelay.Host/appsettings.Docker.json docker/appsettings.Smoke.json && exit 1 || true

# Publish flags honored
grep -q '<SelfContained>true</SelfContained>' src/FrigateRelay.Host/FrigateRelay.Host.csproj
test -f .dockerignore
```
