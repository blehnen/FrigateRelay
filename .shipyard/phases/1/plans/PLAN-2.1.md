---
phase: foundation-abstractions
plan: 2.1
wave: 2
dependencies: [1.1]
must_haves:
  - FrigateRelay.Abstractions class library with only Microsoft.Extensions.* deps
  - EventContext is immutable, source-agnostic (no Frigate types)
  - Verdict uses static factories with private constructor enforcing invariants (D3)
  - IEventSource, IActionPlugin, IValidationPlugin, ISnapshotProvider, IPluginRegistrar defined
  - PluginRegistrationContext exposes IServiceCollection AND IConfiguration (D1)
  - Abstractions.Tests uses MSTest v3 + MTP + FluentAssertions 6.12.2 + NSubstitute
  - Contract-shape tests assert immutability + Verdict invariants
files_touched:
  - src/FrigateRelay.Abstractions/FrigateRelay.Abstractions.csproj
  - src/FrigateRelay.Abstractions/EventContext.cs
  - src/FrigateRelay.Abstractions/Verdict.cs
  - src/FrigateRelay.Abstractions/SnapshotRequest.cs
  - src/FrigateRelay.Abstractions/SnapshotResult.cs
  - src/FrigateRelay.Abstractions/IEventSource.cs
  - src/FrigateRelay.Abstractions/IActionPlugin.cs
  - src/FrigateRelay.Abstractions/IValidationPlugin.cs
  - src/FrigateRelay.Abstractions/ISnapshotProvider.cs
  - src/FrigateRelay.Abstractions/IPluginRegistrar.cs
  - src/FrigateRelay.Abstractions/PluginRegistrationContext.cs
  - tests/FrigateRelay.Abstractions.Tests/FrigateRelay.Abstractions.Tests.csproj
  - tests/FrigateRelay.Abstractions.Tests/VerdictTests.cs
  - tests/FrigateRelay.Abstractions.Tests/EventContextTests.cs
  - tests/FrigateRelay.Abstractions.Tests/PluginRegistrationContextTests.cs
tdd: true
risk: high
---

# PLAN-2.1 — FrigateRelay.Abstractions and Contract-Shape Tests

## Context

This plan defines the public plugin surface that every later phase will consume. Getting the shape wrong now ripples across every plugin project; therefore risk is **high** and all abstractions ship with contract-shape tests from the start (`tdd: true`).

Decisions enforced:

- **D1** — `PluginRegistrationContext` carries both `IServiceCollection` and `IConfiguration` (lightweight POCO/record; no `HostApplicationBuilder` leak).
- **D3** — `Verdict` is a `readonly record struct` with a **private constructor** and three static factories: `Pass()`, `Pass(double score)`, `Fail(string reason)`. `Passed == true` implies `Reason is null`; `Passed == false` implies `Reason is not null and not empty`. These invariants are unit-tested. `readonly record struct` chosen over `record` to avoid heap allocation per validator call — a hot path in the dispatcher in later phases.

Per CLAUDE.md invariant: the abstractions assembly depends **only** on `Microsoft.Extensions.*`. Verification asserts this explicitly.

`EventContext` shape (source-agnostic): `string EventId`, `string Camera`, `string Label`, `IReadOnlyList<string> Zones`, `DateTimeOffset StartedAt`, `string RawPayload` (opaque string — provider-specific JSON lives here), `Func<CancellationToken, ValueTask<byte[]?>> SnapshotFetcher` (delegate so sources can lazily fetch). No `FrigateEvent`, `FrigateEventBefore`, etc. types appear in this assembly — they live in `FrigateRelay.Sources.FrigateMqtt` (Phase 3).

Open-question resolutions baked into this plan:

- **Q1 (applied)** — Test project uses **Approach B**: `<Project Sdk="Microsoft.NET.Sdk">` with `<PackageReference Include="MSTest" Version="4.2.1" />`, `EnableMSTestRunner=true`, `TestingPlatformDotnetTestSupport=true`, `OutputType=Exe`. Rationale mirrors PLAN-1.1's Q1 resolution (Dependabot coverage).

## Dependencies

- **PLAN-1.1** — global.json, Directory.Build.props, and empty FrigateRelay.sln must exist.

## Tasks

<task id="1" files="src/FrigateRelay.Abstractions/FrigateRelay.Abstractions.csproj, src/FrigateRelay.Abstractions/Verdict.cs, src/FrigateRelay.Abstractions/EventContext.cs, src/FrigateRelay.Abstractions/SnapshotRequest.cs, src/FrigateRelay.Abstractions/SnapshotResult.cs" tdd="false">
  <action>Create src/FrigateRelay.Abstractions/FrigateRelay.Abstractions.csproj as a Microsoft.NET.Sdk class library (no explicit TargetFramework — inherited from Directory.Build.props), with PropertyGroup adding IsPackable=false and a single PackageReference on Microsoft.Extensions.DependencyInjection.Abstractions Version=10.0.0 and Microsoft.Extensions.Configuration.Abstractions Version=10.0.0 (these transitively bring IConfiguration + IServiceCollection with zero third-party tail). Implement Verdict.cs as a readonly record struct with private ctor and three static factories: Pass(), Pass(double score), Fail(string reason); public properties Passed (bool), Reason (string?), Score (double?). Implement EventContext.cs as a sealed record with required init-only members: string EventId, string Camera, string Label, IReadOnlyList&lt;string&gt; Zones (default empty), DateTimeOffset StartedAt, string RawPayload, Func&lt;CancellationToken, ValueTask&lt;byte[]?&gt;&gt; SnapshotFetcher. Implement SnapshotRequest.cs as a sealed record with EventContext Context, string? ProviderName, bool IncludeBoundingBox. Implement SnapshotResult.cs as a sealed record with byte[] Bytes, string ContentType, string ProviderName. Fail(reason) must throw ArgumentException if reason is null/empty/whitespace.</action>
  <verify>cd /mnt/f/git/FrigateRelay && dotnet sln add src/FrigateRelay.Abstractions/FrigateRelay.Abstractions.csproj && dotnet build src/FrigateRelay.Abstractions/FrigateRelay.Abstractions.csproj -c Release 2>&1 | tee /tmp/abs-build.log && ! grep -Ei "warning|error" /tmp/abs-build.log</verify>
  <done>Project builds clean with zero warnings. dotnet list package shows only Microsoft.Extensions.* dependencies. Verdict has private ctor (Reflection-checkable) and exactly three public static factories.</done>
</task>

<task id="2" files="src/FrigateRelay.Abstractions/IEventSource.cs, src/FrigateRelay.Abstractions/IActionPlugin.cs, src/FrigateRelay.Abstractions/IValidationPlugin.cs, src/FrigateRelay.Abstractions/ISnapshotProvider.cs, src/FrigateRelay.Abstractions/IPluginRegistrar.cs, src/FrigateRelay.Abstractions/PluginRegistrationContext.cs" tdd="false">
  <action>Define the five plugin interfaces and the registration context in a single commit. IEventSource: string Name { get; }; IAsyncEnumerable&lt;EventContext&gt; ReadEventsAsync(CancellationToken ct). IActionPlugin: string Name { get; }; Task ExecuteAsync(EventContext ctx, CancellationToken ct). IValidationPlugin: string Name { get; }; Task&lt;Verdict&gt; ValidateAsync(EventContext ctx, CancellationToken ct). ISnapshotProvider: string Name { get; }; Task&lt;SnapshotResult?&gt; FetchAsync(SnapshotRequest request, CancellationToken ct). IPluginRegistrar: void Register(PluginRegistrationContext context). PluginRegistrationContext is a sealed class with two init-only properties required IServiceCollection Services and required IConfiguration Configuration, plus a constructor taking both. All interfaces live in namespace FrigateRelay.Abstractions. Each file has one XML doc comment stating its purpose in one sentence.</action>
  <verify>cd /mnt/f/git/FrigateRelay && dotnet build src/FrigateRelay.Abstractions/FrigateRelay.Abstractions.csproj -c Release 2>&1 | tee /tmp/abs-build2.log && ! grep -Ei "warning|error" /tmp/abs-build2.log && grep -l "interface IEventSource" src/FrigateRelay.Abstractions/IEventSource.cs && grep -l "interface IActionPlugin" src/FrigateRelay.Abstractions/IActionPlugin.cs && grep -l "interface IValidationPlugin" src/FrigateRelay.Abstractions/IValidationPlugin.cs && grep -l "interface ISnapshotProvider" src/FrigateRelay.Abstractions/ISnapshotProvider.cs && grep -l "interface IPluginRegistrar" src/FrigateRelay.Abstractions/IPluginRegistrar.cs</verify>
  <done>All five interfaces compile. PluginRegistrationContext exposes both Services (IServiceCollection) and Configuration (IConfiguration). Project still builds with zero warnings.</done>
</task>

<task id="3" files="tests/FrigateRelay.Abstractions.Tests/FrigateRelay.Abstractions.Tests.csproj, tests/FrigateRelay.Abstractions.Tests/VerdictTests.cs, tests/FrigateRelay.Abstractions.Tests/EventContextTests.cs, tests/FrigateRelay.Abstractions.Tests/PluginRegistrationContextTests.cs" tdd="true">
  <action>Write tests first. Create tests/FrigateRelay.Abstractions.Tests/FrigateRelay.Abstractions.Tests.csproj as Microsoft.NET.Sdk with OutputType=Exe, EnableMSTestRunner=true, TestingPlatformDotnetTestSupport=true, IsPackable=false, and PackageReferences: MSTest 4.2.1, FluentAssertions 6.12.2, NSubstitute 5.3.0, NSubstitute.Analyzers.CSharp 1.0.17 (PrivateAssets=all), plus ProjectReference to FrigateRelay.Abstractions. Write VerdictTests.cs with [TestMethod] coverage for: (a) Pass_NoScore_HasNoReasonAndNoScore; (b) Pass_WithScore_CarriesScoreAndNoReason; (c) Fail_WithReason_IsFailedAndCarriesReason; (d) Fail_WithNullOrWhitespaceReason_Throws (DataRow null, "", "   "); (e) Verdict_Ctor_IsNotPublic (Reflection asserting all public ctors count == 0). Write EventContextTests.cs with [TestMethod]: (f) EventContext_AllMembers_AreInitOnly (Reflection walks properties, asserts each setter is init via IsInitOnly metadata); (g) Zones_DefaultsToEmpty. Write PluginRegistrationContextTests.cs with [TestMethod]: (h) Context_ExposesServicesAndConfiguration (instantiate with NSubstitute stubs, FluentAssertions .Should().BeSameAs()). Add the test project to the solution. Total test methods defined: 8 minimum (the 4 DataRow cases of d count as 4 separate test executions, putting executed-test-count well over 8).</action>
  <verify>cd /mnt/f/git/FrigateRelay && dotnet sln add tests/FrigateRelay.Abstractions.Tests/FrigateRelay.Abstractions.Tests.csproj && dotnet test tests/FrigateRelay.Abstractions.Tests -c Release 2>&1 | tee /tmp/abs-test.log && grep -E "Passed!|Passed:" /tmp/abs-test.log</verify>
  <done>dotnet test reports Passed result with at least 8 passing tests, 0 failures. Verdict invariant tests confirm private ctor and reason-rules. EventContext immutability test passes via reflection. PluginRegistrationContext exposes both Services and Configuration.</done>
</task>

## Verification

Run from repo root `/mnt/f/git/FrigateRelay/`:

```bash
# Solution still builds clean
dotnet build FrigateRelay.sln -c Release

# Abstractions assembly has no third-party runtime deps
dotnet list package --include-transitive --project src/FrigateRelay.Abstractions/FrigateRelay.Abstractions.csproj \
  | tee /tmp/abs-deps.log
# Must show ONLY Microsoft.Extensions.* and System.* (BCL) entries:
! grep -Ev "Microsoft\.Extensions\.|^\s*$|^\s*>|^\s*Project|^\s*Top-level|^\s*Transitive|Package|Requested|Resolved|System\.|^\s*---" /tmp/abs-deps.log | grep -E "^\s*>"

# Tests pass
dotnet test tests/FrigateRelay.Abstractions.Tests -c Release
# Expect: Passed, 8+ tests, 0 failures

# ServicePointManager still structurally impossible
git grep ServicePointManager ; [ $? -ne 0 ] && echo OK
```

Expected: solution builds clean, `dotnet list package --include-transitive` shows only `Microsoft.Extensions.*` / `System.*` BCL entries, Abstractions.Tests reports 8+ passed / 0 failed, `git grep ServicePointManager` empty.
