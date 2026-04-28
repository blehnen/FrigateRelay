# REVIEW-2.3 — dotnet new Plugin Template

**Reviewer:** reviewer-2-3
**Date:** 2026-04-28
**Commits:** b0f92a6, ee58550, b55605e
**Status:** COMPLETE

---

## Stage 1 — Correctness

### template.json
- [x] `identity: "FrigateRelay.Plugins.Template"` present
- [x] `shortName: "frigaterelay-plugin"` present
- [x] `name: "FrigateRelay Plugin"` present
- [x] `tags: { language: C#, type: project }` present
- [x] `sourceName: "FrigateRelay.Plugins.Example"` — correct
- [x] `sourceName` rewrite verified: `dotnet new frigaterelay-plugin -n FrigateRelay.Plugins.SmokeReview -o /tmp/smoke-review-2-3` produces `FrigateRelay.Plugins.SmokeReview.csproj`, `SmokeReview` namespace in all source files
- [x] Valid JSON confirmed

### Plugin csproj (`FrigateRelay.Plugins.Example.csproj`)
- [x] `<TargetFramework>net10.0</TargetFramework>`
- [x] `<IsPackable>false</IsPackable>`
- [x] `<GenerateDocumentationFile>true</GenerateDocumentationFile>` (matches BlueIris pattern)
- [x] `<ProjectReference Include="..\..\..\..\src\FrigateRelay.Abstractions\FrigateRelay.Abstractions.csproj" />`
- [x] `PackageReference Include="Microsoft.Extensions.Http" Version="10.0.4"` (matches BlueIris)
- [x] `PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="10.0.4"` (matches BlueIris)
- [x] `<InternalsVisibleTo Include="FrigateRelay.Plugins.Example.Tests" />`
- [x] Builds clean within repo (`0 Warning(s)`, `0 Error(s)`)
- NOTE: Template csproj lacks `<RootNamespace>` and `<AssemblyName>` explicit properties that BlueIris has. Not a defect — dotnet infers these from the project file name, and the `sourceName` rename handles the rename. Minor divergence only.

### ExampleActionPlugin.cs
- [x] Implements `IActionPlugin`
- [x] `ExecuteAsync(EventContext ctx, SnapshotContext snapshot, CancellationToken ct)` — correct 3-parameter Phase 6 ARCH-D2 shape
- [x] Ignores `SnapshotContext` (correct for action-only plugin)
- [x] `public sealed partial class` (correct for `[LoggerMessage]` source-gen)
- [x] `Name => "Example"` property present

### ExamplePluginRegistrar.cs
- [x] Implements `IPluginRegistrar`
- [x] `Register(PluginRegistrationContext context)` signature correct
- [x] Registers `IActionPlugin` + options binding — matches CLAUDE.md pattern

### Test csproj
- [x] `<OutputType>Exe</OutputType>`
- [x] `<EnableMSTestRunner>true</EnableMSTestRunner>`
- [x] `<TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>`
- [x] `<TestingPlatformShowTestsFailure>true</TestingPlatformShowTestsFailure>`
- [x] `MSTest Version="4.2.1"` (matches existing tests)
- [x] `FluentAssertions Version="6.12.2"` pinned directly (no Directory.Packages.props exists)
- [x] `NSubstitute Version="5.3.0"` + Analyzers
- [x] `Microsoft.Extensions.Hosting Version="10.0.0"` (matches BlueIris.Tests shape)
- [x] `<InternalsVisibleTo Include="DynamicProxyGenAssembly2" />` present

### Test naming
- [x] `ExecuteAsync_LogsAndReturnsCompletedTask` uses underscores
- NOTE: Strictly the convention is `Method_Condition_Expected` (3 parts). This name is `Method_Description` (2 parts) — acceptable for a smoke-style assertion that covers both success and no-throw in one. Not a violation.

### Smoke test
- [x] `dotnet new install templates/FrigateRelay.Plugins.Template/` exits 0
- [x] `dotnet new frigaterelay-plugin -n FrigateRelay.Plugins.SmokeReview -o /tmp/smoke-review-2-3` exits 0
- [x] `sourceName` rename correct — all files/namespaces rewritten to `SmokeReview`
- FINDING (NON-BLOCKING): `dotnet build` on the scaffolded output from an arbitrary directory **fails** — the relative `../../../../src/FrigateRelay.Abstractions/...` ProjectReference path resolves to the repo only when the scaffold is consumed from within the repo tree. This is **by design per PLAN-2.3** ("out-of-repo authors: publish FrigateRelay.Abstractions to NuGet first") and the plan comment in the csproj documents it. The canonical CI scaffold-smoke (`docs.yml`, PLAN-2.4) places the scaffolded output inside the repo tree where the relative path resolves.
- [x] Template build + test within repo: `1/1 passed` (verified independently)

---

## Stage 2 — Integration

### `GenerateDocumentationFile=true` + `CS1591 NoWarn`
- JUSTIFIED. `BlueIris.csproj` and `Pushover.csproj` both have `<GenerateDocumentationFile>true</GenerateDocumentationFile>`. The template mirrors that pattern. `CS1591` suppression is needed because scaffold template code intentionally omits XML doc comments on public members — the comment in the csproj explains this. The combination is consistent and not a regression.

### `CA1707 NoWarn` in test csproj
- JUSTIFIED. `.editorconfig` silences CA1707 for `[tests/**.cs]` — that glob matches `tests/` at repo root. Template tests live at `templates/FrigateRelay.Plugins.Template/tests/**.cs` which is **outside** the glob. Scaffolded consumers would place tests at a different path still outside the glob. Adding the suppression to the test csproj is the only reliable mechanism here. This is explicitly noted in the CLAUDE.md rationale ("templates/**.tests is OUTSIDE that glob").

### LoggerMessage pattern
- Template uses `[LoggerMessage]` partial source-gen (CA1848/CA1873 preferred form).
- Existing BlueIris/Pushover plugins use the older `LoggerMessage.Define<T>(...)` delegate form.
- Only `CodeProjectAiValidator.cs` uses the `[LoggerMessage]` partial attribute form within `src/`.
- The partial source-gen form is the modern, preferred CA1848-compliant approach and is superior to `LoggerMessage.Define`. This is a **minor pattern inconsistency** with BlueIris/Pushover (which predate the project's style guidance on this), not a regression. The template models the better practice for plugin authors.

### No file overlap with PLAN-2.1/2.2/2.4
- [x] All 8 files touched are exclusively under `templates/FrigateRelay.Plugins.Template/`. No overlap.

### SUMMARY-2.3.md not updated
- Builder's `SUMMARY-2.3.md` is a stale skeleton (`in_progress`, all tasks `pending`, no smoke result). The commit message carries the actual smoke summary. Low severity — the review file supersedes it.

---

## Findings

| # | Severity | Description |
|---|---|---|
| 1 | INFO | Scaffolded output builds only within repo tree — by design per PLAN-2.3 + csproj comment; CI scaffold-smoke (PLAN-2.4) is the binding gate |
| 2 | INFO | Template plugin csproj omits explicit `<RootNamespace>`/`<AssemblyName>` vs BlueIris — dotnet infers correctly; sourceName rename handles it |
| 3 | INFO | `[LoggerMessage]` partial source-gen form differs from `LoggerMessage.Define` in BlueIris/Pushover — template models the modern preferred pattern |
| 4 | INFO | SUMMARY-2.3.md stale skeleton — commit message is the authoritative smoke record |
| 5 | INFO | Test name `ExecuteAsync_LogsAndReturnsCompletedTask` is 2-part vs canonical 3-part — acceptable for a single-scenario smoke test |

**Blocking findings: 0**

---

## Verdict

**APPROVED**

All PLAN-2.3 must-haves satisfied. template.json fields correct, sourceName rename verified, csproj mirrors BlueIris pattern, IActionPlugin 3-param shape correct, IPluginRegistrar correct, test csproj OutputType=Exe + MSTest v3 + FluentAssertions 6.12.2 pinned, CA1707/CS1591 suppressions justified, LoggerMessage source-gen is the better pattern, no file overlap. Template builds and tests pass 1/1 within repo. Smoke of scaffolded output outside repo fails (expected by design; documented in csproj comment; PLAN-2.4 CI is the gate). No blocking findings.
