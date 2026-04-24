# Build Summary: Plan 2.1 — FrigateRelay.Abstractions + Contract-Shape Tests

## Status: complete

## Tasks Completed

- **Task 1 — Abstractions csproj + value types (`Verdict`, `EventContext`, `SnapshotRequest`, `SnapshotResult`)** — complete — commit `4437142`
  - `FrigateRelay.Abstractions.csproj` — `Microsoft.NET.Sdk`, `IsPackable=false`, references only `Microsoft.Extensions.Configuration.Abstractions` 10.0.0 + `Microsoft.Extensions.DependencyInjection.Abstractions` 10.0.0. Transitive deps are `Microsoft.Extensions.Primitives` 10.0.0 only — invariant satisfied.
  - `Verdict.cs` (46 lines) — `readonly record struct`, private ctor, static factories `Pass()`, `Pass(double score)`, `Fail(string reason)`. Fail throws `ArgumentException` on null/empty/whitespace.
  - `EventContext.cs` (34 lines) — sealed record, required init-only members: `EventId`, `Camera`, `Label`, `Zones` (defaults to `Array.Empty<string>()`), `StartedAt`, `RawPayload`, `SnapshotFetcher` (`Func<CancellationToken, ValueTask<byte[]?>>`). No Frigate-specific types.
  - `SnapshotRequest.cs` (16 lines) — sealed record: `Context`, `ProviderName?`, `IncludeBoundingBox`.
  - `SnapshotResult.cs` (16 lines) — sealed record: `Bytes`, `ContentType`, `ProviderName`.

- **Task 2 — Plugin interfaces + `PluginRegistrationContext`** — complete — commit `76037b9`
  - `IEventSource.cs` — `Name`, `IAsyncEnumerable<EventContext> ReadEventsAsync(ct)`.
  - `IActionPlugin.cs` — `Name`, `Task ExecuteAsync(EventContext ctx, ct)`.
  - `IValidationPlugin.cs` — `Name`, `Task<Verdict> ValidateAsync(EventContext ctx, ct)`.
  - `ISnapshotProvider.cs` — `Name`, `Task<SnapshotResult?> FetchAsync(SnapshotRequest request, ct)`.
  - `IPluginRegistrar.cs` — `void Register(PluginRegistrationContext context)`.
  - `PluginRegistrationContext.cs` — sealed class with **required init-only** `IServiceCollection Services` and `IConfiguration Configuration`, plus a ctor taking both. `[SetsRequiredMembers]` added **retroactively during task 3** (see Decisions).

- **Task 3 — Test project + contract-shape tests + sln wiring** — complete — commit `f18c56a` (`shipyard(phase-1): add Abstractions tests + wire to sln`)
  - Test csproj: `Microsoft.NET.Sdk` (Approach B / PackageReference). `OutputType=Exe`, `EnableMSTestRunner=true`, `TestingPlatformDotnetTestSupport=true`, `TestingPlatformShowTestsFailure=true`. PackageReferences: `MSTest` 4.2.1, `FluentAssertions` 6.12.2, `NSubstitute` 5.3.0, `NSubstitute.Analyzers.CSharp` 1.0.17 (`PrivateAssets=all`). ProjectReference to `FrigateRelay.Abstractions`.
  - `VerdictTests.cs` — 5 `[TestMethod]` (one data-driven with 3 `[DataRow]` expansions). Asserts factory output invariants and — via `Reflection` — the class has zero public ctors.
  - `EventContextTests.cs` — 2 `[TestMethod]`. Walks properties via `Reflection.PropertyInfo.SetMethod.IsInitOnly` / `RequiredMemberAttribute` to assert init-only; asserts `Zones` default.
  - `PluginRegistrationContextTests.cs` — 1 `[TestMethod]`. NSubstitute stubs for both deps; FluentAssertions `.Should().BeSameAs()` on each property.
  - `dotnet sln add` both projects.

## Files Modified

| File | Change | Commit |
|---|---|---|
| `src/FrigateRelay.Abstractions/FrigateRelay.Abstractions.csproj` | created | `4437142` |
| `src/FrigateRelay.Abstractions/Verdict.cs` | created (46 lines) | `4437142` |
| `src/FrigateRelay.Abstractions/EventContext.cs` | created (34 lines) | `4437142` |
| `src/FrigateRelay.Abstractions/SnapshotRequest.cs` | created | `4437142` |
| `src/FrigateRelay.Abstractions/SnapshotResult.cs` | created | `4437142` |
| `src/FrigateRelay.Abstractions/IEventSource.cs` | created | `76037b9` |
| `src/FrigateRelay.Abstractions/IActionPlugin.cs` | created | `76037b9` |
| `src/FrigateRelay.Abstractions/IValidationPlugin.cs` | created | `76037b9` |
| `src/FrigateRelay.Abstractions/ISnapshotProvider.cs` | created | `76037b9` |
| `src/FrigateRelay.Abstractions/IPluginRegistrar.cs` | created | `76037b9` |
| `src/FrigateRelay.Abstractions/PluginRegistrationContext.cs` | created `76037b9`, patched `f18c56a` ([SetsRequiredMembers]) | `76037b9`+`f18c56a` |
| `tests/FrigateRelay.Abstractions.Tests/*` (4 files) | created | `f18c56a` |
| `FrigateRelay.sln` | +2 projects via `dotnet sln add` | `f18c56a` |
| `.editorconfig` | +test-scoped suppressions for CA1707 + IDE0005 | `f18c56a` |

## Decisions Made

1. **`[SetsRequiredMembers]` on `PluginRegistrationContext`'s ctor** (retroactive fix during task 3).
   *Problem:* Class had both a ctor AND `required init` properties. Without `[SetsRequiredMembers]`, callers could not use the ctor alone — the compiler still demanded object-initializer syntax to satisfy the required members (`CS9035`). The ctor was effectively vestigial.
   *Resolution:* Marked the ctor with `[SetsRequiredMembers]`, making it a valid standalone constructor and preserving the option to use object-initializer syntax. This honors D1 semantics (both `Services` and `Configuration` are mandatory) and enables clean `new PluginRegistrationContext(services, configuration)` call sites in tests and later the host.

2. **Test-scoped `[tests/**.cs]` suppressions in `.editorconfig`** for `CA1707` (naming) and `IDE0005` (unused usings).
   *Problem:* TWAE is globally on (Q2 resolution). `.editorconfig` promotes `IDE0005` to a warning across `*.cs`. Combined with `latest-recommended` analyzers, this treats DAMP-style underscore test names as **build errors** and raises `EnableGenerateDocumentationFile` because `IDE0005` needs the doc file to detect unused usings reliably.
   *Resolution:* Add `[tests/**.cs]` section setting both diagnostics to `none`. This is the architect's anticipated "per-project escape" (Q2), applied via editorconfig globbing rather than per-csproj `<NoWarn>` — scales better if more test projects are added. Src/ code is unaffected.
   *Rationale:* Test names use underscores as a standard DAMP convention (readable failure output). Test assemblies are not a public API surface, so XML doc files add noise without value.

3. **Invoke tests via `dotnet run --project` not `dotnet test`.**
   *Problem:* .NET 10 SDK explicitly blocks `dotnet test` against MTP via the VSTest target. Running `dotnet test tests/FrigateRelay.Abstractions.Tests -c Release` produces: `error: Testing with VSTest target is no longer supported by Microsoft.Testing.Platform on .NET 10 SDK and later`.
   *Resolution:* Since the csproj has `OutputType=Exe` (MTP requirement), invoke as an exe: `dotnet run --project tests/FrigateRelay.Abstractions.Tests -c Release`. The MTP runner prints its native test summary (`total: 10  failed: 0  succeeded: 10`). Future phases should follow this pattern; ROADMAP Phase 2 (CI) will need the workflow to use the same invocation instead of `dotnet test`.
   *Downstream ripple:* PLAN-3.1's verification commands will need adjustment (they currently say `dotnet test`). Flag for the Wave 3 builder.

4. **Test project uses Approach B (PackageReference)** per Q1 resolution. `<Project Sdk="Microsoft.NET.Sdk">` with `<PackageReference Include="MSTest" Version="4.2.1" />` pulls in the full MSTest + MTP + TestAdapter stack transitively, and all deps are Dependabot-visible.

5. **`Verdict` is a `readonly record struct`** (not a `record class`) per PLAN-2.1's stated intent: avoid heap allocation on the per-validation hot path. The private primary ctor + public static factories pattern works identically for both; struct is measurably cheaper under load.

## Issues Encountered

1. **Builder agent truncation mid-Task 3.** Agent `a8847f7d3a38b48ac` committed tasks 1 and 2 cleanly, wrote `VerdictTests.cs` + `EventContextTests.cs` (uncommitted), and truncated at "Now write the three test files (TDD — tests first):" before producing `PluginRegistrationContextTests.cs` or wiring to sln. Resolved by orchestrator finishing the task inline.

2. **`[SetsRequiredMembers]` omission.** Described in Decision 1. Found when the first attempt at `PluginRegistrationContextTests` triggered `CS9035: Required member 'Services' must be set in the object initializer or attribute constructor`.

3. **CA1707 + IDE0005 TWAE escalation.** Described in Decision 2. The .NET 10 analyzer surface plus `AnalysisLevel=latest-recommended` in `Directory.Build.props` combined with TWAE made test-naming a build error. Worth noting: `Directory.Build.props` is global; if src/ code ever wants DAMP-style naming (it won't, but just in case), the escape is the same `[tests/**.cs]` pattern generalized.

4. **`dotnet test` blocked.** Described in Decision 3. This is a documented .NET 10 breaking change (https://aka.ms/dotnet-test-mtp-error). Phase 2 CI workflow needs to plan around it from day one.

5. **`dotnet list package` CLI surface change.** The old `dotnet list package --project <csproj> --include-transitive` now prints help instead of running — syntax was changed to `dotnet list <csproj> package --include-transitive` on .NET 10 SDK. Not a blocker; CI scripts and any verification recipes need to be updated.

## Verification Results

Run on commit `f18c56a`:

```
$ dotnet build FrigateRelay.sln -c Release
Build succeeded.
    0 Warning(s)
    0 Error(s)
```
Wave 1's empty-solution warning is gone (expected — Wave 2 added the first project).

```
$ dotnet run --project tests/FrigateRelay.Abstractions.Tests -c Release
MSTest v4.2.1 (UTC 04/02/2026) [ubuntu.24.04-x64 - .NET 10.0.7]
Test run summary: Passed! - tests/FrigateRelay.Abstractions.Tests/bin/Release/net10.0/FrigateRelay.Abstractions.Tests.dll (net10.0|x64)
  total: 10
  failed: 0
  succeeded: 10
  skipped: 0
  duration: 517ms
```
10 passing, 0 failing (plan gate: ≥ 8).

```
$ dotnet list src/FrigateRelay.Abstractions/FrigateRelay.Abstractions.csproj package --include-transitive
Project 'FrigateRelay.Abstractions' has the following package references
   [net10.0]:
   Top-level Package                                            Requested   Resolved
   > Microsoft.Extensions.Configuration.Abstractions            10.0.0      10.0.0
   > Microsoft.Extensions.DependencyInjection.Abstractions      10.0.0      10.0.0

   Transitive Package                     Resolved
   > Microsoft.Extensions.Primitives      10.0.0
```
Only `Microsoft.Extensions.*` entries — invariant satisfied.

```
$ git grep ServicePointManager -- src/ tests/
(no output)
```
No source-code matches.

## Next wave readiness

Wave 3 (PLAN-3.1) can begin. Key caveats for the Wave-3 builder:

- **Tests must be invoked via `dotnet run --project ...`**, not `dotnet test`. Update PLAN-3.1's verification commands and Host tests csproj (also `OutputType=Exe`).
- **Test csproj template** — reuse `FrigateRelay.Abstractions.Tests.csproj` as the starting template; the PackageReferences and properties are already stable.
- **Editorconfig inherits** — the `[tests/**.cs]` suppressions already apply to `FrigateRelay.Host.Tests/**`.
- **`[SetsRequiredMembers]`** is now idiomatic in this codebase. If Wave 3 introduces other classes with `required init` + ctor, follow the same pattern.
- **CI (Phase 2) pre-warning** — any future CI workflow that invokes `dotnet test` will fail on .NET 10. Plan the workflow to use `dotnet run` against each test project (or the forthcoming opt-in "new dotnet test" experience when it stabilizes — investigate during Phase 2).
