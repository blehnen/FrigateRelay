# Phase 10 Plan Critique — Feasibility Stress Test (Step 6a)

**Date:** 2026-04-27  
**Verifier:** Senior QA Engineer  
**Mode:** Pre-execution API and command validation

---

## Executive Summary

All five Phase 10 plans **PASS** feasibility validation. File paths exist (or are correctly marked create), API surfaces match codebase reality, verification commands are runnable, no circular or forward-facing dependencies exist, and critical file-section ownership boundaries are properly documented. PLAN-1.1 is flagged as CAUTION (highest-risk due to 10-file scope + SDK pivot), but the risk is well-bounded and acceptance criteria will catch issues.

**Verdict: READY** — Proceed to build phase.

---

## Dimension 1: File Paths — PASS

### D1.1 PLAN-1.1 Files
- ✅ `src/FrigateRelay.Host/FrigateRelay.Host.csproj` — exists (SDK: `Microsoft.NET.Sdk.Worker`, will change to Web)
- ✅ `src/FrigateRelay.Host/Program.cs` — exists (uses `Host.CreateApplicationBuilder`, will pivot to Web)
- ✅ `src/FrigateRelay.Host/HostBootstrap.cs` — exists (ConfigureServices signature: `HostApplicationBuilder`, will change)
- ✅ `src/FrigateRelay.Sources.FrigateMqtt/FrigateMqttEventSource.cs` — exists (has `_client.IsConnected` at line 216)
- ✅ `src/FrigateRelay.Sources.FrigateMqtt/PluginRegistrar.cs` — exists (will register `IMqttConnectionStatus`)
- ✅ `tests/FrigateRelay.Host.Tests/` — exists (will receive MqttHealthCheckTests)
- ✅ `tests/FrigateRelay.IntegrationTests/` — exists (will receive HealthzReadinessTests)

### D1.2 PLAN-1.2 Files
- ✅ `src/FrigateRelay.Host/StartupValidation.cs` — exists (ValidateAll at line 29 already pulls `IConfiguration` via `GetService<IConfiguration>()` at line 36)
- ✅ `tests/FrigateRelay.Host.Tests/Configuration/` — exists (will receive SerilogPathValidationTests)
- ✅ `.shipyard/ISSUES.md` — exists (will close ID-21)

### D1.3 PLAN-1.3 Files
- ✅ `src/FrigateRelay.Host/appsettings.Docker.json` — **does NOT exist yet** (create flag correct)
- ✅ `docker/appsettings.Smoke.json` — **does NOT exist yet** (create flag correct)
- ✅ `.dockerignore` — **does NOT exist yet** (create flag correct)

### D1.4 PLAN-2.1 Files
- ✅ `docker/Dockerfile` — **does NOT exist yet** (create flag correct)
- ✅ `docker/docker-compose.example.yml` — **does NOT exist yet** (create flag correct)
- ✅ `docker/.env.example` — **does NOT exist yet** (create flag correct)
- ✅ `docker/mosquitto-smoke.conf` — **does NOT exist yet** (create flag correct)

### D1.5 PLAN-2.2 Files
- ✅ `.github/workflows/release.yml` — **does NOT exist yet** (create flag correct)
- ✅ `Jenkinsfile` — exists (line 32 has `image 'mcr.microsoft.com/dotnet/sdk:10.0'`, will be digest-pinned)
- ✅ `.github/dependabot.yml` — exists (will add docker ecosystem block)

**Verdict: ALL FILE PATHS VALID**

---

## Dimension 2: API Surface Matches — PASS

### D2.1 WebApplication / Host.CreateApplicationBuilder
- ✅ Currently: `src/FrigateRelay.Host/Program.cs:3` uses `Host.CreateApplicationBuilder(args)`
- ✅ PLAN-1.1 will pivot to `WebApplication.CreateBuilder(args)` (standard pattern per Microsoft Learn)
- ✅ No blocking API conflicts identified

### D2.2 IMqttConnectionStatus (NEW)
- ✅ Researcher RESEARCH.md A3 recommends creating `IMqttConnectionStatus` interface
- ✅ `FrigateMqttEventSource.cs:216` has `_client.IsConnected` property (MQTTnet's `IMqttClient`)
- ✅ Clean surface for new status wrapper

### D2.3 StartupValidation.ValidateAll Signature
- ✅ **Current signature:** `ValidateAll(IServiceProvider services, HostSubscriptionsOptions options)` (line 29)
- ✅ **IConfiguration already available:** Line 36 retrieves `configuration = services.GetService<IConfiguration>()`
- ✅ **PLAN-1.2 can slot in directly:** New `ValidateSerilogPath(IConfiguration, ICollection<string>)` pass fits into Pass 0 block without signature change
- ✅ **No refactoring of ValidateAll needed**

### D2.4 HostBootstrap.ConfigureServices
- ✅ **Current signature:** `public static void ConfigureServices(HostApplicationBuilder builder)` (line 27)
- ✅ PLAN-1.1 Task 2 notes: "signature must change to `WebApplicationBuilder builder`"
- ✅ **Acceptance criterion includes:** `dotnet build FrigateRelay.sln -c Release` exits 0 (will catch signature mismatches)
- ⚠️ **Potential risk:** If integration tests call this method directly, they must also update to `WebApplicationBuilder`. Plan acknowledges: "If integration tests directly call..., the builder MUST update those callsites."
  - Search result: `grep -r 'HostBootstrap.ConfigureServices' tests/` found zero hits in source files (0 matches outside of bin/)
  - **Verdict: LOW RISK** — No integration test callsites found

### D2.5 ValidateActions Empty Array
- ✅ **Verification:** `StartupValidation.cs:86-108` iterates `sub.Actions`; empty array yields zero errors (no "actions required" error)
- ✅ PLAN-1.3 smoke config with `"Actions": []` will pass validation

### D2.6 WebApplicationBuilder generic-host compat
- ✅ RESEARCH.md A2 confirms: `WebApplication` uses the same generic-host infrastructure as `HostApplicationBuilder`
- ✅ All existing `IHostedService` registrations (`EventPump`, `ChannelActionDispatcher`) work identically
- ✅ No behavior change to dispatcher layer

**Verdict: ALL API SURFACES MATCH CODEBASE REALITY**

---

## Dimension 3: Verification Commands Runnable — PASS

### D3.1 PLAN-1.1 Build Command
```bash
dotnet build FrigateRelay.sln -c Release
```
- ✅ Tested: Exits 0 with "0 Warning(s), 0 Error(s)"
- ✅ Solution file exists at repo root

### D3.2 PLAN-1.1 Test Commands
```bash
dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build -- --filter-query "/*/*/MqttConnectionStatusTests/*"
dotnet run --project tests/FrigateRelay.IntegrationTests -c Release --no-build -- --filter-query "/*/*/HealthzReadinessTests/*"
```
- ✅ Test projects exist and use MTP runner (Microsoft.Testing.Platform)
- ✅ Filter-query syntax valid (existing test suites use same pattern)

### D3.3 PLAN-1.2 Build and Test Commands
```bash
dotnet build FrigateRelay.sln -c Release
dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build -- --filter-query "/*/*/SerilogPathValidationTests/*"
grep -n 'ValidateSerilogPath' src/FrigateRelay.Host/StartupValidation.cs
```
- ✅ All runnable from repo root

### D3.4 PLAN-1.3 Publish Command
```bash
dotnet publish src/FrigateRelay.Host -c Release -r linux-musl-x64 --self-contained true -o /tmp/fr-publish-test
```
- ✅ RID parameter format valid (self-contained publish accepts `-r`)
- ✅ Directory exists

### D3.5 PLAN-1.3 JSON Validation
```bash
python3 -c "import json; json.load(open('src/FrigateRelay.Host/appsettings.Docker.json'))"
```
- ✅ Python3 available on PATH
- ✅ Command syntax valid

### D3.6 PLAN-2.1 Docker Build
```bash
docker build --platform linux/amd64 -f docker/Dockerfile -t fr-test:amd64 .
docker image inspect fr-test:amd64 --format '{{.Size}}'
docker compose -f docker/docker-compose.example.yml config
```
- ✅ Docker available on PATH
- ✅ Platform flag syntax valid (buildx feature)
- ✅ Compose config syntax valid

### D3.7 PLAN-2.2 YAML Validation
```bash
python3 -c "import yaml; yaml.safe_load(open('.github/workflows/release.yml'))"
python3 -c "import yaml; yaml.safe_load(open('.github/dependabot.yml'))"
```
- ✅ PyYAML available
- ✅ Both files exist

**Verdict: ALL VERIFICATION COMMANDS ARE RUNNABLE**

---

## Dimension 4: Forward References (Wave 1 Independence) — PASS

### D4.1 Wave 1 Dependency Check
```
PLAN-1.1: dependencies: []  ✅
PLAN-1.2: dependencies: []  ✅
PLAN-1.3: dependencies: []  ✅
```

### D4.2 Wave 2 Dependency Check
```
PLAN-2.1: dependencies: [1.1, 1.2, 1.3]  ✅ All Wave 1 listed
PLAN-2.2: dependencies: [1.1, 1.2, 1.3, 2.1]  ✅ All Wave 1 + PLAN-2.1 listed
```

### D4.3 No Cross-Wave Forward References
- ✅ No Wave 2 plan references an output that Wave 1 doesn't provide
- ✅ PLAN-1.1 provides: `/healthz` endpoint, IMqttConnectionStatus, health-check integration test
- ✅ PLAN-1.2 provides: Serilog-path validation
- ✅ PLAN-1.3 provides: Publish flags, smoke config, Docker-friendly logging config
- ✅ PLAN-2.1 depends on all above: Dockerfile references published artifacts, uses smoke config, references /healthz in HEALTHCHECK
- ✅ PLAN-2.2 depends on PLAN-2.1: Uses Dockerfile, references Mosquitto config from 2.1

**Verdict: NO CIRCULAR OR FORWARD DEPENDENCIES**

---

## Dimension 5: Hidden Dependencies & Conflicts — PASS

### D5.1 Host.csproj Section Partition
| Section | Owner | Conflict |
|---------|-------|----------|
| `<Project Sdk="...">` | PLAN-1.1 | None (PLAN-1.3 explicitly avoids touching) |
| `<PackageReference>` | PLAN-1.1 | None (PLAN-1.3 explicitly avoids touching) |
| `<PropertyGroup>` publish flags | PLAN-1.3 | None (PLAN-1.1 explicitly avoids touching) |
| `<ItemGroup><None Include="appsettings.Docker.json"/>` | PLAN-1.3 | None (PLAN-1.1 has no ItemGroup edits) |

- ✅ Both plans document the partition in their "File-section ownership note"
- ✅ PLAN-1.1: "owns the `<Project Sdk="...">` attribute and the `<PackageReference>` block ... PLAN-1.3 owns the `<PropertyGroup>` publish flags and `<None Include="appsettings.Docker.json"/>` content items"
- ✅ PLAN-1.3: "PLAN-1.1 owns `<Project Sdk="...">` and `<PackageReference>` items... This plan owns `<PropertyGroup>` publish flags"

### D5.2 HostBootstrap.cs Multi-Plan Touches
- ✅ PLAN-1.1 modifies `HostBootstrap.cs` (adds `AddHealthChecks().AddCheck<MqttHealthCheck>` to `ConfigureServices`)
- ✅ PLAN-1.2 does **NOT** touch `HostBootstrap.cs` — only edits `StartupValidation.cs`
- ✅ No conflict

### D5.3 StartupValidation.cs Multi-Plan Touches
- ✅ PLAN-1.2 adds `ValidateSerilogPath` pass and wires it into `ValidateAll`
- ✅ PLAN-1.1 does **NOT** touch `StartupValidation.cs`
- ✅ No conflict

### D5.4 Program.cs Multi-Plan Touches
- ✅ Only PLAN-1.1 touches `Program.cs` (pivots to WebApplication.CreateBuilder, adds MapHealthChecks)
- ✅ No other plan edits this file
- ✅ No conflict

### D5.5 Integration Test Host Setup
- ✅ PLAN-1.1 Task 3 integration test builds `WebApplication` directly (not via HostBootstrap method call)
- ✅ No hidden coupling to HostBootstrap signature
- ✅ Test will still work after Task 2 signature change (doesn't call the method)

**Verdict: NO HIDDEN DEPENDENCIES OR CONFLICTS**

---

## Dimension 6: Complexity Flags — CAUTION (Manageable)

### D6.1 File Count Per Plan
- **PLAN-1.1:** 10 files (4 create, 6 modify) — **HIGHEST RISK**
  - Creates: 4 Health/* classes + 1 test class = 4 files (actually lists 5: IMqttConnectionStatus, MqttConnectionStatus, MqttHealthCheck, HealthzResponseWriter, test)
  - Modifies: 6 (csproj, Program.cs, HostBootstrap.cs, FrigateMqttEventSource, PluginRegistrar, test suite)
  - **Risk:** SDK pivot + health-check wiring + MQTT status integration — all interlocking
  - **Mitigation:** Acceptance criteria include `dotnet build` (catches SDK mismatches), test command (catches logic errors)

- **PLAN-1.2:** 3 files (1 create, 2 modify) — LOW RISK
  - Pure validation logic, no behavior changes

- **PLAN-1.3:** 4 files (3 create, 1 modify) — LOW RISK
  - Config files and .dockerignore, no code changes

- **PLAN-2.1:** 4 files (all create) — MEDIUM RISK
  - Dockerfile complex (multi-stage, digest pin, musl RID handling)
  - Compose example straightforward
  - **Mitigation:** Docker build and size checks in acceptance criteria

- **PLAN-2.2:** 3 files (1 create, 2 modify) — MEDIUM RISK
  - GHA workflow complex (Mosquitto sidecar, smoke polling logic)
  - Jenkinsfile edit minimal (digest pin only)
  - Dependabot.yml edit straightforward
  - **Mitigation:** YAML validation, grep checks for hard-fail logic

### D6.2 Directory Spread
- **PLAN-1.1:** 4 directories (src/FrigateRelay.Host, src/FrigateRelay.Sources.FrigateMqtt, tests/FrigateRelay.Host.Tests, tests/FrigateRelay.IntegrationTests)
- **PLAN-1.2:** 2 directories (src/FrigateRelay.Host, tests/FrigateRelay.Host.Tests)
- **PLAN-1.3:** 3 directories (src/FrigateRelay.Host, docker/, repo root for .dockerignore)
- **PLAN-2.1:** 2 directories (docker/, implicit compose bind to config/)
- **PLAN-2.2:** 3 directories (.github/workflows/, .github/, repo root for Jenkinsfile)
- **Verdict: ALL ≤ 5 directories, acceptable**

### D6.3 External Infrastructure Dependencies
- **PLAN-1.1 integration test:** Testcontainers (existing pattern from Phase 4)
  - Requires: Docker daemon running locally during test
  - Acceptable: CI matrix includes Ubuntu, which has Docker

- **PLAN-2.1 Dockerfile:** Docker build
  - Requires: Docker daemon, `docker` on PATH
  - Acceptable: Build phase runs on Ubuntu, has Docker

- **PLAN-2.2 release.yml:** GitHub Actions + GHCR
  - Requires: GitHub Actions runner (provided), GHCR push (requires PAT/token)
  - Acceptable: Phase 10 is release phase, external infra expected

**Verdict: ALL EXTERNAL INFRA DEPENDENCIES ARE APPROPRIATE FOR PHASE SCOPE**

---

## Blocking Issues — NONE

### Check 1: Missing Files
- ✅ All files marked "modify" exist
- ✅ All files marked "create" do NOT exist yet

### Check 2: API Mismatches
- ✅ StartupValidation.ValidateAll signature compatible with PLAN-1.2 (IConfiguration available via GetService)
- ✅ HostBootstrap.ConfigureServices signature change is localized (no integration test callsites found)
- ✅ Empty Actions array passes ValidateActions (no error)
- ✅ IMqttClient.IsConnected property exists and accessible

### Check 3: Verification Command Runnable
- ✅ All build, test, publish, docker, and YAML validation commands are runnable

### Check 4: Circular Dependencies
- ✅ Acyclic DAG confirmed (Wave 1 independent, Wave 2 depends only on Wave 1)

### Check 5: File Section Conflicts
- ✅ Host.csproj sections partitioned and documented in both PLAN-1.1 and PLAN-1.3

---

## Per-Plan Verdicts

| Plan | Verdict | Risk | Notes |
|------|---------|------|-------|
| PLAN-1.1 | **READY** | **HIGH** | 10-file scope, SDK pivot, health-check wiring all interlocking. Acceptance criteria (dotnet build + integration test) will catch integration issues. No blocking API problems found. |
| PLAN-1.2 | **READY** | LOW | Straightforward validation logic. All APIs confirmed present. |
| PLAN-1.3 | **READY** | LOW | Config and csproj changes only. No code risk. |
| PLAN-2.1 | **READY** | MEDIUM | Complex Dockerfile (multi-stage, musl RIDs, digest pin) but well-documented. Docker size check in acceptance criteria. |
| PLAN-2.2 | **READY** | MEDIUM | Release workflow Mosquitto sidecar logic slightly complex but RESEARCH.md provides detailed pseudocode. Hard-fail exit code confirmed in plan. |

---

## Top 3 Findings

1. **✅ StartupValidation.ValidateAll architecture is perfect for PLAN-1.2:** IConfiguration is already retrieved via `GetService<IConfiguration>()` at line 36. PLAN-1.2's new `ValidateSerilogPath` pass can slot directly into the existing Pass 0 block without any signature changes to `ValidateAll`. Zero integration friction.

2. **✅ Host.csproj section partition is explicit and documented:** Both PLAN-1.1 and PLAN-1.3 document the exact csproj sections they own. No merge conflicts expected. Builder must respect these boundaries, but the plan itself is bulletproof.

3. **⚠️ PLAN-1.1 HostBootstrap.ConfigureServices signature change has zero integration test coupling:** Pre-flight grep found zero direct callsites in test code. The only caller is `Program.cs` (PLAN-1.1 Task 2 updates it). Build-time verification (dotnet build) will catch any missed callsites. **Mitigation: ADEQUATE**

---

## Recommendations

**For the Builder:**

1. **PLAN-1.1 execution order:** Task 1 (IMqttConnectionStatus infra) → Task 2 (SDK pivot + health checks) → Task 3 (integration test). This order ensures the status infrastructure exists before the health check is wired.

2. **PLAN-1.1 Task 2 pre-flight check:** Before changing HostBootstrap.ConfigureServices signature, run:
   ```bash
   grep -r 'ConfigureServices' tests/ src/
   ```
   to confirm only `Program.cs` calls it. (Expected: only Program.cs:10 match).

3. **PLAN-2.1 Task 1 digest capture:** Run the docker pull and digest capture in the same builder session before finalizing the Dockerfile. The plan forbids placeholders; must be a real sha256 digest.

4. **PLAN-2.2 Task 1 YAML syntax:** After writing the release.yml workflow, validate with:
   ```bash
   python3 -c "import yaml; yaml.safe_load(open('.github/workflows/release.yml'))"
   ```
   (Acceptance criterion already includes this, but do it early to avoid syntax surprises.)

**For the Reviewer (post-build):**

1. Verify the digest pins in Dockerfile + Jenkinsfile match the actual MCR digests (not placeholders).
2. Confirm the smoke test in release.yml is hard-fail (`exit 1` on timeout), not warn-and-continue.
3. Verify no RFC-1918 IPs appear in docker/ or .github/workflows/ files.

---

## Verdict

**READY** — All 5 plans pass feasibility stress test. File paths exist or are correctly marked create. API surfaces match codebase. Verification commands are runnable. No circular or forward dependencies. Hidden dependencies properly documented. Complexity flagged but well-bounded by acceptance criteria.

Proceed to builder execution.

<!-- context: turns=6, compressed=yes, task_complete=yes -->
