# Phase 11 Research

_Researcher: shipyard:researcher, 2026-04-28_

---

## 1. Existing Surface Inventory

| Deliverable | Status | Notes |
|---|---|---|
| `README.md` (repo root) | **MISSING** | Does not exist |
| `LICENSE` (repo root) | **MISSING** | Does not exist |
| `CONTRIBUTING.md` (root or `.github/`) | **MISSING** | Does not exist |
| `SECURITY.md` (root or `.github/`) | **MISSING** | Does not exist |
| `CHANGELOG.md` (repo root) | **MISSING** | Does not exist |
| `.github/ISSUE_TEMPLATE/` | **MISSING** | Directory does not exist |
| `.github/pull_request_template.md` | **MISSING** | Does not exist |
| `templates/` (repo root) | **MISSING** | Directory does not exist |
| `docs/` (repo root) | **MISSING** | Directory does not exist |
| `samples/` (repo root) | **MISSING** | Directory does not exist |
| `.github/workflows/docs.yml` | **MISSING** | New workflow per D4 |
| `.github/workflows/ci.yml` | **EXISTS** | Phase 2; builds + tests; see Section 7 |
| `.github/workflows/secret-scan.yml` | **EXISTS** | Phase 2; scan + tripwire-self-test jobs |
| `.github/workflows/release.yml` | **EXISTS** | Phase 10; multi-arch GHCR push |
| `src/FrigateRelay.Abstractions/` | **EXISTS** | Full public contract surface — see Section 2 |
| All `src/FrigateRelay.Plugins.*/` | **EXISTS** | 4 plugins — see Section 3 |

All Phase 11 deliverables are net-new. Zero collision with existing files.

---

## 2. Plugin Contract Surface (for plugin-author-guide)

All types are in namespace `FrigateRelay.Abstractions`, assembly `FrigateRelay.Abstractions`. All are **public**.

| Type | Kind | Visibility | Signature / Key notes |
|---|---|---|---|
| `IEventSource` | interface | public | `string Name { get; }` · `IAsyncEnumerable<EventContext> ReadEventsAsync(CancellationToken ct)` — source authors only; plugins do not implement this |
| `IActionPlugin` | interface | public | `string Name { get; }` · `Task ExecuteAsync(EventContext ctx, SnapshotContext snapshot, CancellationToken ct)` — 3 params, established Phase 6 ARCH-D2 |
| `IValidationPlugin` | interface | public | `string Name { get; }` · `Task<Verdict> ValidateAsync(EventContext ctx, SnapshotContext snapshot, CancellationToken ct)` — snapshot shared with action (pre-resolved once per dispatch) |
| `ISnapshotProvider` | interface | public | `string Name { get; }` · `Task<SnapshotResult?> FetchAsync(SnapshotRequest request, CancellationToken ct)` — returns null when no image available |
| `IPluginRegistrar` | interface | public | `void Register(PluginRegistrationContext context)` — implemented by each plugin project |
| `PluginRegistrationContext` | class (sealed) | public | `required IServiceCollection Services` · `required IConfiguration Configuration` · ctor `[SetsRequiredMembers]` |
| `EventContext` | record (sealed) | public | `required string EventId/Camera/Label/RawPayload` · `IReadOnlyList<string> Zones` · `required DateTimeOffset StartedAt` · `required Func<CancellationToken, ValueTask<byte[]?>> SnapshotFetcher` — immutable (all `init`) |
| `Verdict` | readonly record struct | public | Private ctor. Static factories: `Verdict.Pass()`, `Verdict.Pass(double score)`, `Verdict.Fail(string reason)` — invalid states unrepresentable |
| `SnapshotContext` | readonly struct | public | Two ctors: (1) `(ISnapshotResolver, perActionName?, subscriptionDefaultName?)` — live resolver path; (2) `(SnapshotResult?)` — pre-resolved path. `default(SnapshotContext).ResolveAsync()` short-circuits to null safely |
| `ISnapshotResolver` | interface | **internal** (host-internal) | Not part of plugin-author contract; plugins only see `SnapshotContext` |

**Plugin-author-guide scope:** `IActionPlugin`, `IValidationPlugin`, `ISnapshotProvider`, `IPluginRegistrar`, `PluginRegistrationContext`, `EventContext`, `Verdict`, `SnapshotContext` (public API only — not `ISnapshotResolver`).

**Lifecycle notes:**
- `IActionPlugin.ExecuteAsync` — plugins that don't use snapshots accept `SnapshotContext` and ignore it (BlueIris pattern). Plugins that do call `await snapshot.ResolveAsync(ctx, ct)`.
- `IValidationPlugin.ValidateAsync` — dispatcher pre-resolves snapshot once, passes same pre-resolved `SnapshotContext` to validator chain AND action. HTTP fetch happens at most once per dispatch.
- `IPluginRegistrar.Register` — called once at startup; registers into `PluginRegistrationContext.Services` and binds from `PluginRegistrationContext.Configuration`.
- Contract designed for AssemblyLoadContext future load path — no rewrites needed (PROJECT.md Goal #3).

---

## 3. Existing Plugin csproj Patterns (Canonical Scaffold Reference)

### Common across all 4 plugin projects

| Property | Value |
|---|---|
| SDK | `Microsoft.NET.Sdk` |
| `<IsPackable>` | `false` |
| `<GenerateDocumentationFile>` | `true` |
| `<ProjectReference>` | `../FrigateRelay.Abstractions/FrigateRelay.Abstractions.csproj` |
| `<InternalsVisibleTo>` | `FrigateRelay.Plugins.<Name>.Tests` (MSBuild item form) |
| `<TargetFramework>` | Inherited from `Directory.Build.props` (only `FrigateSnapshot` sets it explicitly — ID-3 tracks BlueIris missing explicit declaration) |

**Note on ID-3:** BlueIris, Pushover, CodeProjectAi omit explicit `<TargetFramework>` (relies on `Directory.Build.props`). FrigateSnapshot sets it explicitly. Scaffold should include explicit `<TargetFramework>net10.0</TargetFramework>` for clarity.

### Package references by plugin

| Package | BlueIris | Pushover | CodeProjectAi | FrigateSnapshot |
|---|---|---|---|---|
| `Microsoft.Extensions.Http` 10.0.4 | ✓ | ✓ | ✓ | ✓ |
| `Microsoft.Extensions.Http.Resilience` 10.4.0 | ✓ | ✓ | — | ✓ |
| `Microsoft.Extensions.Options.ConfigurationExtensions` 10.0.4 | ✓ | ✓ | ✓ | ✓ |
| `Microsoft.Extensions.Options.DataAnnotations` 10.0.4 | — | ✓ | ✓ | ✓ |

**Minimal scaffold minimum:** `Microsoft.Extensions.Http` + `Microsoft.Extensions.Options.ConfigurationExtensions`. Add `Http.Resilience` and `DataAnnotations` if the scaffold targets IActionPlugin (BlueIris pattern). Architect to decide minimal vs. full-set for scaffold.

### IPluginRegistrar pattern

Each plugin exposes a `<PluginName>PluginRegistrar : IPluginRegistrar` class. Pattern (verified from PluginRegistrationContext signature):
```csharp
public sealed class ExamplePluginRegistrar : IPluginRegistrar
{
    public void Register(PluginRegistrationContext context)
    {
        context.Services.AddHttpClient<ExampleActionPlugin>();
        context.Services.AddSingleton<IActionPlugin, ExampleActionPlugin>();
        context.Services.Configure<ExampleOptions>(
            context.Configuration.GetSection("Example"));
    }
}
```
The host discovers all `IPluginRegistrar` implementations from DI via `IEnumerable<IPluginRegistrar>`.

### Canonical test csproj shape (from BlueIris.Tests)

```xml
<OutputType>Exe</OutputType>
<EnableMSTestRunner>true</EnableMSTestRunner>
<TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>
<TestingPlatformShowTestsFailure>true</TestingPlatformShowTestsFailure>
```
Packages: `MSTest 4.2.1`, `FluentAssertions 6.12.2`, `NSubstitute 5.3.0` + Analyzers, `WireMock.Net 2.4.0`, `Microsoft.Extensions.Hosting 10.0.0`.
ProjectReferences: plugin under test + `FrigateRelay.Abstractions` + `FrigateRelay.TestHelpers`.

**Scaffold test project must use `dotnet run --project ... -c Release` not `dotnet test`** (MTP runner, per CLAUDE.md).

---

## 4. dotnet new Template Format

### Decision: D2 mandates `dotnet new` template, short-name `frigaterelay-plugin` (D5)

### template.json required fields

From Microsoft Learn ([Custom templates for dotnet new](https://learn.microsoft.com/en-us/dotnet/core/tools/custom-templates)) and the dotnet/templating wiki ([template.json reference](https://github.com/dotnet/templating/wiki/Reference-for-template.json)):

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

### `sourceName` mechanism

- The engine searches every **file name** and **file content** for the literal string value of `sourceName`.
- When user runs `dotnet new frigaterelay-plugin -n FrigateRelay.Plugins.MyCustom`, every occurrence of `FrigateRelay.Plugins.Example` is replaced with `FrigateRelay.Plugins.MyCustom` — in `.csproj` filenames, namespaces, class names, and string literals.
- This makes rename automatic and namespace-correct without any `sed` scripting.

### Directory layout for multi-project template (plugin + test)

```
templates/FrigateRelay.Plugins.Template/
├── .template.config/
│   └── template.json
├── src/
│   └── FrigateRelay.Plugins.Example/
│       ├── FrigateRelay.Plugins.Example.csproj
│       ├── ExampleActionPlugin.cs
│       ├── ExampleOptions.cs
│       └── ExamplePluginRegistrar.cs
└── tests/
    └── FrigateRelay.Plugins.Example.Tests/
        ├── FrigateRelay.Plugins.Example.Tests.csproj
        └── ExampleActionPluginTests.cs
```

The template engine recursively replaces `sourceName` across all subdirectory names and file contents. A multi-project template works natively — no special config needed beyond `sourceName`.

### Install and usage

```bash
# Install from local path (smoke-test pattern per D2)
dotnet new install templates/FrigateRelay.Plugins.Template

# Use the template
dotnet new frigaterelay-plugin -n FrigateRelay.Plugins.SmokeScaffold -o /tmp/smoke

# Build and test the scaffolded output
dotnet build /tmp/smoke/src/FrigateRelay.Plugins.SmokeScaffold -c Release
dotnet run --project /tmp/smoke/tests/FrigateRelay.Plugins.SmokeScaffold.Tests -c Release
```

### .NET 10 constraints

The template engine (`Microsoft.TemplateEngine.*`) ships in the .NET SDK and requires no separate package for local template install/use. No version constraint beyond .NET SDK 10 (already pinned via `global.json`). `dotnet new install <local-path>` is the correct form for local templates (not `--install` which is deprecated in .NET 7+).

**TODO architect:** Decide whether the template's `.csproj` files reference `FrigateRelay.Abstractions` via a relative path (works for in-repo smoke) or a NuGet package reference (works for out-of-repo plugin authors). Since NuGet publish is a Non-Goal for v1, relative path is correct for now; document the future NuGet path in the plugin-author-guide.

---

## 5. CLAUDE.md Staleness Items

Two stale lines flagged in DOCUMENTATION-10, per CONTEXT-11 D3 #2:

### Stale line 1 — Project state section (lines 9-10)

**Current text:**
```
FrigateRelay is a **greenfield .NET 10 rewrite**, currently **pre-implementation**. Nothing but planning docs exists in-tree yet.
```

**Correct text:** Phase 10 is complete; the implementation is complete through Phase 10. Should read something like:
> FrigateRelay is a **production-ready .NET 10 background service**, currently at **Phase 10 complete** (Docker + multi-arch release workflow shipped). Implementation is complete; Phase 11 adds OSS polish.

### Stale line 2 — Jenkinsfile description

**Current text (from CLAUDE.md CI section):**
> `Jenkinsfile` — coverage pipeline. Scripted. Docker agent `mcr.microsoft.com/dotnet/sdk:10.0` (tag-pinned — digest pin + Dependabot `docker` ecosystem deferred to Phase 10).

**Correct text:** Phase 10 fix-ups `1c3eaaa` + `3b87641` resolved both items (digest pin applied, Dependabot docker ecosystem added to `.github/dependabot.yml`). Should read:
> `Jenkinsfile` — coverage pipeline. Scripted. Docker agent `mcr.microsoft.com/dotnet/sdk:10.0` digest-pinned (see Jenkinsfile for current SHA). Dependabot `docker` ecosystem watches the Jenkinsfile SDK image pin (`.github/dependabot.yml`).

**Architect note:** These are small targeted edits. A single plan with 2 tasks (one per stale line) is sufficient.

---

## 6. Phase 9 Integration Test Failure Root-Causes (CRITICAL)

**Status:** Tests identified, source files read. Tests were NOT run — see below for source-analysis root-cause hypothesis.

### Failing test 1: `Validator_ShortCircuits_OnlyAttachedAction`

**File:** `tests/FrigateRelay.IntegrationTests/MqttToValidatorTests.cs`

**Test logic summary:**
- Starts Mosquitto (Testcontainers), BlueIris stub, Pushover stub, Frigate snapshot stub, CodeProject.AI stub (confidence=0.20, below MinConfidence=0.7)
- Registers a custom `CapturingLoggerProvider` via `builder.Services.AddSingleton<ILoggerProvider>(capture)` — **after** `HostBootstrap.ConfigureServices(builder)` to survive Serilog's logging-provider replacement
- Publishes one MQTT event
- Asserts: BlueIris got 1 GET, CodeProject got 1 POST, Pushover got 0 POSTs, and a structured `ValidatorRejected` log entry exists in `captureProvider.Entries` with `EventId.Name == "ValidatorRejected"` and message containing "Pushover" and "strict-person"

**Root-cause hypothesis (from source, not run):**

The test registers `ILoggerProvider` via the service collection AFTER `HostBootstrap.ConfigureServices`. The comment in the test explains this is needed because `AddSerilog` replaces the logging provider pipeline. However, `ILoggerProvider` registered as a singleton in the DI container may not be picked up by the Serilog-replaced logging infrastructure at all — Serilog's `AddSerilog` calls `builder.Logging.ClearProviders()` internally and adds a single Serilog provider; a subsequent `AddSingleton<ILoggerProvider>` in the service collection does not hook into Serilog's sink chain.

The specific assertion that likely fails is:
```csharp
var rejectedEntries = captureProvider.Entries
    .Where(e => e.EventId.Name == "ValidatorRejected")
    .ToList();
rejectedEntries.Should().HaveCount(1, ...);
```

If the `CapturingLoggerProvider` never receives log entries (because Serilog's pipeline doesn't call it), `captureProvider.Entries` is empty, and the assertion fails even though BlueIris + CodeProject assertions pass.

**Depth assessment:** SHALLOW — the WireMock stub assertions likely pass; only the log-capture assertion fails. The fix is either: (a) route the capture through a Serilog sink instead of `ILoggerProvider`, or (b) use a different capture mechanism that integrates with the Serilog pipeline (`builder.Logging.AddProvider(captureProvider)` at the right point in builder setup). This is a 1–2 task fix, not a deep architectural issue.

**Supporting evidence from ISSUES.md ID-22:** "Three sites in `tests/FrigateRelay.Host.Tests/Observability/` use fixed `Task.Delay`... Initial inline attempt used `logger.Records.Any()` but `CapturingLogger<T>` does not expose `Records`." — confirms logger-capture integration was problematic in Phase 9.

### Failing test 2: `TraceSpans_CoverFullPipeline`

**File:** `tests/FrigateRelay.IntegrationTests/Observability/` — file not found at expected path `TraceSpanTests.cs` or `TraceSpansTests.cs`. Directory exists but specific filename was not located in the investigation window.

**What is known from ROADMAP Phase 9 success criteria:**
> Integration test `TraceSpans_CoverFullPipeline` asserts one root span per MQTT event with 4 expected child spans, all under the root activity id.

**Root-cause hypothesis (from context, not source read):**

Phase 9 used an `OpenTelemetry.Exporter.InMemory` package (seen in `FrigateRelay.IntegrationTests.csproj`). The same logging-provider capture issue may affect this test if it also registers a `CapturingLoggerProvider`. Alternatively, the span-parenting across the `Channel<T>` boundary may be timing-sensitive — Activity propagation on `DispatchItem` requires the span to still be active when the consumer reads it. If the producer span closes before the consumer runs (possible under load), the child spans have no parent.

**Depth assessment:** UNCERTAIN — could be shallow (timing / Activity.Current scope) or moderate (Activity propagation across Channel boundary needs an explicit propagation mechanism). CONTEXT-11 D7 says "Phase 9 PLAN-3.1 area" and both tests are known Phase 10 regressions (not new failures), confirmed in ROADMAP Phase 10 status: "192/194 tests pass (2 pre-existing Phase 9 integration regressions)."

**Architect recommendation per D7:** Wave 1 = single triage plan. If `Validator_ShortCircuits` is shallow (log capture fix), fold the fix into triage plan. For `TraceSpans`, read the actual test file before committing to fix depth. If file read confirms span-timing hypothesis → shallow fix; if Activity propagation is broken → split into investigate + fix plans.

**TODO architect:** Read `tests/FrigateRelay.IntegrationTests/Observability/<filename>.cs` to confirm test structure and hypothesis. The file was not found at `TraceSpanTests.cs` or `TraceSpansTests.cs` — check actual filename with `ls tests/FrigateRelay.IntegrationTests/Observability/`.

---

## 7. CI Surface

### ci.yml structure (for docs.yml to mirror)

```yaml
name: CI
on:
  push:
  pull_request:
permissions:
  contents: read
concurrency:
  group: ci-${{ github.ref }}
  cancel-in-progress: true
env:
  DOTNET_CLI_TELEMETRY_OPTOUT: 1
  DOTNET_NOLOGO: 1
  NUGET_XMLDOC_MODE: skip
jobs:
  build-and-test:
    strategy:
      fail-fast: false
      matrix:
        os: [ubuntu-latest, windows-latest]
    runs-on: ${{ matrix.os }}
    timeout-minutes: 15
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json
      - run: dotnet restore FrigateRelay.sln
      - run: dotnet build FrigateRelay.sln -c Release --no-restore
      - shell: bash  # Windows Git Bash consistency
        run: bash .github/scripts/run-tests.sh [--skip-integration on Windows]
```

### Conventions docs.yml must follow

1. `concurrency.group` pattern: `docs-${{ github.ref }}` (mirror `ci-${{ github.ref }}` prefix convention)
2. `cancel-in-progress: true` — cancel obsolete runs on force-push
3. `shell: bash` on any step that runs bash scripts (Git Bash consistency on Windows)
4. `actions/setup-dotnet@v4` with `global-json-file: global.json` — ensures same SDK version
5. Same `env:` block (`DOTNET_CLI_TELEMETRY_OPTOUT`, `DOTNET_NOLOGO`, `NUGET_XMLDOC_MODE`)
6. `permissions: contents: read` (least privilege)

### Path filter recommendation (architect-discretion per D4)

docs.yml should trigger on push/PR touching: `docs/**`, `samples/**`, `templates/**`, `.github/workflows/docs.yml`. This prevents docs.yml from running on every source change, keeping it fast. **Do NOT put scaffold-smoke in ci.yml** — D4 explicitly places it in docs.yml.

### secret-scan.yml tripwire pattern

Two jobs: `scan` (grep tree) + `tripwire-self-test` (grep fixture, fail if any pattern does NOT match). If docs.yml adds doc-rot detection regexes, consider a similar self-test job. No fixture file exists for docs-specific patterns yet.

### Samples project CI note (D8)

`samples/FrigateRelay.Samples.PluginGuide/` is in `FrigateRelay.sln` (IDE builds it, warnings-as-errors applies) but CI build/test runs ONLY in `docs.yml`, not `ci.yml`. The `dotnet build FrigateRelay.sln` step in `ci.yml` WILL build the samples (it's in the solution), but no `dotnet run --project samples/...` step should be added to `ci.yml`.

---

## 8. CHANGELOG Source Material

### HISTORY.md availability

File exists at `.shipyard/HISTORY.md` but exceeds 25,000 tokens — not fully read. Per the phase status lines in ROADMAP.md, confirmed entries exist for:

| Phase | Status line in ROADMAP |
|---|---|
| Phase 1 | COMPLETE (2026-04-24) |
| Phase 2 | COMPLETE (2026-04-24) |
| Phase 3 | COMPLETE (2026-04-24) |
| Phase 4 | complete_with_gaps (2026-04-25) |
| Phase 5 | complete_with_gaps (2026-04-26) |
| Phase 6 | complete (2026-04-26) |
| Phase 7 | complete (2026-04-26) |
| Phase 8 | COMPLETE (2026-04-27) |
| Phase 9 | COMPLETE (2026-04-27) |
| Phase 10 | COMPLETE_WITH_GAPS (2026-04-28) |

All 10 phases have ROADMAP entries with dates — HISTORY.md likely mirrors these. **TODO architect:** Verify HISTORY.md per-phase sections exist with `grep -n "^## Phase" .shipyard/HISTORY.md` before writing CHANGELOG.

### Keep-a-Changelog format

Format: https://keepachangelog.com/en/1.1.0/

Standard headings per release section: `### Added`, `### Changed`, `### Fixed`, `### Removed`, `### Deprecated`, `### Security`. Top entry = `[Unreleased]`. Each phase maps to an `[Unreleased]` sub-section until v1.0.0 is tagged in Phase 12.

Architect recommendation: One `## [Unreleased]` section covering Phase 1–10 (retroactive), with a sub-heading per phase. Full version history deferred until v1.0.0 tag (Phase 12).

---

## 9. SECURITY.md Best-Practice Template

Per D6: GitHub private vulnerability reporting only. No personal email.

### Standard sections

```markdown
# Security Policy

## Supported Versions

| Version | Supported |
|---------|-----------|
| latest  | ✓         |

FrigateRelay is pre-1.0. Only the latest commit on `main` receives security fixes.

## Reporting a Vulnerability

**Do not open a public GitHub issue for security vulnerabilities.**

Use GitHub's private vulnerability reporting:
1. Go to https://github.com/blehnen/frigaterelay/security/advisories/new
2. Fill in the vulnerability details.
3. Submit. The maintainer will respond within 7 days.

> **Maintainer note:** Private vulnerability reporting must be enabled under
> repository Settings → Code security → "Private vulnerability reporting".
> This is a one-time admin step; it cannot be done via code.
```

**Owner/repo slug:** `blehnen/frigaterelay` — architect to confirm against actual GitHub repo URL. D6 notes the repo settings flag as a manual checklist item.

---

## 10. Doc-Sample Copy Mechanism Options

Per ROADMAP: `docs/plugin-author-guide.md` code samples must be "copied verbatim into `samples/FrigateRelay.Samples.PluginGuide/`" so stale docs cannot ship. D8 says architect decides the copy mechanism.

### Option A: Manual sync (discipline-only)

Samples project contains the canonical implementations. Doc code blocks are copy-pasted from samples files. CI builds samples (warnings-as-errors) but does not diff against docs. Drift is caught by code review only.

- Pro: zero tooling, zero CI complexity
- Con: docs can drift silently; violates ROADMAP "stale docs cannot silently ship" requirement

### Option B: CI diff-check (docs.yml job)

A `docs.yml` step extracts fenced code blocks from `docs/plugin-author-guide.md` (e.g., via a small Python/bash script), compares them byte-for-byte against the corresponding files in `samples/`, and fails if any differ.

- Pro: enforces the invariant mechanically; clear CI failure message
- Con: requires a fragile script that maps doc code-blocks to sample files; code-block language/filename annotation must be standardized

### Option C: Include-at-build-time (markdown include)

Samples files are the source of truth. A CI step (or a `docfx`/`markdownlint` plugin) inlines them into the docs at CI time. The committed `docs/plugin-author-guide.md` contains `<!-- include: samples/.../.cs -->` markers.

- Pro: single source of truth; no sync needed
- Con: non-standard markdown; IDE preview breaks; adds a new tool dependency

**Architect recommendation:** Option B is the most straightforward for this project size. The script is ~20 lines of bash. Standardize code block annotation as `` ```csharp filename=ExampleActionPlugin.cs `` and grep for the filename tag. Option A violates the ROADMAP requirement. Option C adds tooling complexity not justified at this scale.

---

## Architect-Relevant Constraints

1. **Wave 1 must be test triage only** (D7). README/CONTRIBUTING/scaffold/etc. MUST NOT start until Wave 1 acceptance criterion is met: 194/194 passing OR 192/192 + 2 explicitly `[Ignore]`-marked with tracking IDs.

2. **TraceSpans test file not confirmed** — actual filename in `tests/FrigateRelay.IntegrationTests/Observability/` unknown. Architect must read directory before writing triage plan.

3. **`Validator_ShortCircuits` root cause is almost certainly the `CapturingLoggerProvider` not receiving Serilog-routed log events** — shallow fix (log capture wiring), not a pipeline bug. High confidence based on source analysis.

4. **Scaffold template uses relative `ProjectReference` to `FrigateRelay.Abstractions`** — this path works for in-repo smoke test but breaks for external plugin authors. Document the future NuGet path in plugin-author-guide; no code change needed now (NuGet publish is Non-Goal v1).

5. **ID-3 (BlueIris csproj missing explicit `<TargetFramework>`)** — scaffold should set explicit `<TargetFramework>net10.0</TargetFramework>` regardless of what existing plugins do.

6. **`InternalsVisibleTo` for NSubstitute requires `DynamicProxyGenAssembly2`** — scaffold test csproj that mocks internal types must include both the test assembly AND `DynamicProxyGenAssembly2` entries. Scaffold should include both preemptively.

7. **Samples project in solution (D8)** — `dotnet build FrigateRelay.sln -c Release` in `ci.yml` will build samples; only explicit `dotnet run --project samples/...` steps should be kept out of `ci.yml`.

8. **GitHub repo owner slug** for SECURITY.md: verify actual repo URL before committing `blehnen/frigaterelay` placeholder.

9. **CLAUDE.md edits are small** — both stale items are single-line edits. One plan, 2 tasks.

10. **`dotnet new install <path>` (not `--install`)** — the `--install` flag is deprecated in .NET 7+; smoke CI must use the non-flag form.

---

## Sources

1. [Custom templates for dotnet new — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/tools/custom-templates)
2. [Create a project template for dotnet new — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/tutorials/cli-templates-create-project-template)
3. [Reference for template.json — dotnet/templating Wiki](https://github.com/dotnet/templating/wiki/Reference-for-template.json)
4. [Keep a Changelog v1.1.0](https://keepachangelog.com/en/1.1.0/)
5. `.shipyard/PROJECT.md` — goals, non-goals, constraints (read in full)
6. `.shipyard/ROADMAP.md` — phases 1–12, success criteria (read in full)
7. `.shipyard/phases/11/CONTEXT-11.md` — 8 user-locked decisions (read in full)
8. `.shipyard/ISSUES.md` — all open/closed issues (read in full)
9. `src/FrigateRelay.Abstractions/*.cs` — all public interfaces and types (read in full)
10. `src/FrigateRelay.Plugins.{BlueIris,Pushover,CodeProjectAi,FrigateSnapshot}/*.csproj` (read in full)
11. `tests/FrigateRelay.Plugins.BlueIris.Tests/FrigateRelay.Plugins.BlueIris.Tests.csproj` (read in full)
12. `tests/FrigateRelay.IntegrationTests/MqttToValidatorTests.cs` (read in full)
13. `tests/FrigateRelay.IntegrationTests/FrigateRelay.IntegrationTests.csproj` (read in full)
14. `.github/workflows/ci.yml` (read in full)
15. `.github/workflows/secret-scan.yml` (read in full)
16. `CLAUDE.md` — architecture invariants and conventions (read in full)

## Uncertainty Flags

- **`TraceSpans_CoverFullPipeline` test file not confirmed** — actual `.cs` filename in `tests/FrigateRelay.IntegrationTests/Observability/` was not read. The hypothesis (Activity propagation timing or log capture) is inferred from context. **Decision Required:** Architect must read the file before committing to fix depth.
- **HISTORY.md content not verified** — file exceeded token limit. Phase entries may differ in structure from ROADMAP summaries. Architect should `grep -n "^## " .shipyard/HISTORY.md` to confirm structure before writing CHANGELOG.
- **Registrar implementation filenames** — `BlueIrisPluginRegistrar.cs` and `PushoverPluginRegistrar.cs` returned "file does not exist" — either the files have different names or are in subdirectories. The registrar pattern was inferred from `IPluginRegistrar.cs` + `PluginRegistrationContext.cs`. Architect should verify actual filename before using as scaffold reference.
- **GitHub repo owner/slug** — used `blehnen/frigaterelay` as placeholder based on git user "Brian Lehnen"; architect must confirm the actual GitHub org/repo name before writing SECURITY.md.
