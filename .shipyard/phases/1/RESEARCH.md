# Research: Phase 1 Foundation — Package Versions and API Shape

**Date:** 2026-04-24  
**Scope:** .NET 10 SDK pin, hosting API, MSTest v3 + MTP csproj shape, FluentAssertions 6.12.2 compatibility, NSubstitute, user secrets wiring, SDK choice, global.json and Directory.Build.props templates.

---

## Versions Table

| Package | Version | Source | Notes |
|---------|---------|--------|-------|
| .NET 10 SDK | **10.0.100** (pin floor; actual roll-forward target is latest 10.0.x installed — 10.0.107 observed on dev box 2026-04-24) | `dotnet --list-sdks` on 2026-04-24 after `apt-get install dotnet-sdk-10.0` | **Correction (post-build-W1):** original research cited `10.0.203` as latest GA, but that feature band has not shipped. Microsoft's current latest is in the **100 band**. `global.json` pins `10.0.100` floor with `rollForward: latestFeature`, which picks up the installed 100-band patch now and will pick up future 200-band releases with no config change. Cross-check via `https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json` before citing any specific version. |
| Microsoft.Extensions.Hosting | **10.0.7** (NuGet latest: 10.0.6 at time of search, 10.0.7 ships with runtime patch) | [nuget.org/packages/microsoft.extensions.hosting](https://www.nuget.org/packages/microsoft.extensions.hosting/) | Pulled transitively by `Microsoft.NET.Sdk.Worker`. No explicit `<PackageReference>` needed for the host project. Targets net8.0+. |
| Microsoft.Extensions.Configuration.UserSecrets | **10.0.7** | [nuget.org/packages/Microsoft.Extensions.Configuration.UserSecrets](https://www.nuget.org/packages/Microsoft.Extensions.Configuration.UserSecrets) | Targets net8.0, net9.0, net10.0, netstandard2.0, net462. Must be referenced **explicitly** in Worker SDK projects — transitive reference from M.E.Hosting does not generate the `UserSecretsId` MSBuild attribute automatically (see SDK issue [dotnet/sdk#4007](https://github.com/dotnet/sdk/issues/4007)). |
| MSTest.Sdk (project SDK) | **4.2.1** | [nuget.org/packages/MSTest.Sdk](https://www.nuget.org/packages/MSTest.Sdk) | Released 2026-04-07. Preferred path: `Sdk="MSTest.Sdk/4.2.1"` in test `.csproj`. Sets `EnableMSTestRunner` and `TestingPlatformDotnetTestSupport` to `true` by default. |
| MSTest (meta-package, alternative path) | **4.2.1** | [nuget.org/packages/MSTest](https://www.nuget.org/packages/MSTest) | Released 2026-04-07. Bundles: `MSTest.TestFramework`, `MSTest.TestAdapter`, `MSTest.Analyzers`, `Microsoft.NET.Test.Sdk ≥ 18.3.0`, `Microsoft.Testing.Extensions.CodeCoverage ≥ 18.5.2`, `Microsoft.Testing.Extensions.TrxReport ≥ 2.2.1`. Use this with `Microsoft.NET.Sdk` + `EnableMSTestRunner` if you do not use the MSTest.Sdk project SDK. |
| Microsoft.Testing.Platform | Bundled in MSTest 3.2.0+; no separate `<PackageReference>` needed when using MSTest.Sdk or the MSTest meta-package | [learn.microsoft.com/dotnet/core/testing/unit-testing-mstest-running-tests](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-mstest-running-tests) | The runner is embedded in the test binary. No `vstest.console` or `dotnet test` adapter host required. |
| FluentAssertions | **6.12.2** (pinned — do not upgrade) | [nuget.org/packages/FluentAssertions/6.12.2](https://www.nuget.org/packages/FluentAssertions/6.12.2) | TFMs: net6.0, netcoreapp2.1, netcoreapp3.0, netstandard2.0, netstandard2.1, net47. No explicit net10.0 TFM, but the `netstandard2.0` asset is compatible with any .NET 10.0 consumer without issues. License: Apache-2.0. Version 7.x+ is commercial; **stay at 6.12.2**. Transitive dep: `System.Threading.Tasks.Extensions ≥ 4.5.0` (on netstandard2.0) — that package is an inbox BCL shim on net10.0; no conflict. |
| NSubstitute | **5.3.0** | [nuget.org/packages/nsubstitute](https://www.nuget.org/packages/nsubstitute/) | Released 2024-10-28. Targets net6.0+, netstandard2.0+, net462+. Fully compatible with net10.0 via net6.0 asset. A `6.0.0-rc.1` pre-release exists; do not use pre-release. License: BSD-3-Clause. |
| NSubstitute.Analyzers.CSharp | **1.0.17** | [nuget.org/packages/NSubstitute.Analyzers.CSharp](https://www.nuget.org/packages/NSubstitute.Analyzers.CSharp) | Companion analyzer; add as `<PackageReference ... PrivateAssets="all">`. |

---

## MSTest v3 + MTP csproj Snippet

Two equivalent approaches. **Approach A (preferred — MSTest.Sdk project SDK)** is simpler. Approach B preserves `Microsoft.NET.Sdk` for teams that need it.

### Approach A — MSTest.Sdk project SDK (recommended)

```xml
<Project Sdk="MSTest.Sdk/4.2.1">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <!-- OutputType=Exe and EnableMSTestRunner=true are set by MSTest.Sdk automatically -->
    <!-- TestingExtensionsProfile defaults to "Default" (CodeCoverage + TrxReport enabled) -->
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="FluentAssertions" Version="6.12.2" />
    <PackageReference Include="NSubstitute" Version="5.3.0" />
    <PackageReference Include="NSubstitute.Analyzers.CSharp" Version="1.0.17">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
    <!-- Project reference to the assembly under test -->
  </ItemGroup>

</Project>
```

The MSTest.Sdk version must also appear in `global.json` under `msbuild-sdks`:

```json
{
  "sdk": { "version": "10.0.203", "rollForward": "latestFeature" },
  "msbuild-sdks": {
    "MSTest.Sdk": "4.2.1"
  }
}
```

### Approach B — Microsoft.NET.Sdk + PackageReference (fallback)

Use this if the NuGet SDK tooling friction noted in the MSTest.Sdk docs is a concern (Dependabot limited support, VS NuGet UI cannot update SDK version).

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <EnableMSTestRunner>true</EnableMSTestRunner>
    <TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>
    <TestingPlatformShowTestsFailure>true</TestingPlatformShowTestsFailure>
    <OutputType>Exe</OutputType>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="MSTest" Version="4.2.1" />
    <PackageReference Include="FluentAssertions" Version="6.12.2" />
    <PackageReference Include="NSubstitute" Version="5.3.0" />
    <PackageReference Include="NSubstitute.Analyzers.CSharp" Version="1.0.17">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
  </ItemGroup>

</Project>
```

### `dotnet test --filter` behavior with MTP

With MTP (both approaches), `dotnet test --filter` is **translated** to MTP's `--filter` argument via the `TestingPlatformDotnetTestSupport=true` bridge. The filter syntax is identical to VSTest for `FullyQualifiedName~` substring matching:

```bash
dotnet test tests/FrigateRelay.Host.Tests --filter "FullyQualifiedName~MqttToBlueIris_HappyPath"
```

The native MTP CLI form (when running the test binary directly) uses `-- --filter` as a trailing argument:

```bash
dotnet run --project tests/FrigateRelay.Host.Tests -- --filter "FullyQualifiedName~MqttToBlueIris_HappyPath"
```

Both forms work. The `dotnet test` form is preferred for CI consistency with coverage collection (`--collect:"XPlat Code Coverage"`).

**Important:** `--collect:"XPlat Code Coverage"` is a VSTest argument. With MTP, code coverage is handled by the bundled `Microsoft.Testing.Extensions.CodeCoverage` extension, activated automatically under the `Default` profile. The CI command will need to pass `-- --coverage` (MTP form) rather than `--collect:"XPlat Code Coverage"` when using pure MTP mode. When `TestingPlatformDotnetTestSupport=true` is set, `dotnet test` bridges most VSTest arguments, but coverage collection behavior should be verified during Phase 2 CI setup.

---

## Host Project SDK Choice

**Recommendation: `Microsoft.NET.Sdk.Worker`**

Use `<Project Sdk="Microsoft.NET.Sdk.Worker">` for `FrigateRelay.Host`. This SDK:
- Automatically copies `*.json` files (including `appsettings.json`) to output and publish directories — without this, `appsettings.json` is not present beside the binary and `IConfiguration` falls back to empty.
- Adds `Microsoft.Extensions.Hosting` implicitly (no explicit `<PackageReference>` required).
- Enables `implicit usings` for `Microsoft.Extensions.*` namespaces.
- Sets `OutputType=Exe` by default.

`Microsoft.NET.Sdk` (base SDK) requires manually adding `<Content Include="appsettings.json"><CopyToOutputDirectory>Always</CopyToOutputDirectory></Content>` for every config file. `Microsoft.NET.Sdk.Web` pulls in ASP.NET Core, which is unnecessary and increases the binary footprint for a pure background service.

For `FrigateRelay.Abstractions`: use `Microsoft.NET.Sdk` (class library, no hosting dependency, no file copying needed).

---

## `global.json` Template

Per decision D4 (`rollForward: latestFeature`), pinned to the latest 10.x feature band without requiring byte-for-byte SDK match:

```json
{
  "sdk": {
    "version": "10.0.203",
    "rollForward": "latestFeature"
  },
  "msbuild-sdks": {
    "MSTest.Sdk": "4.2.1"
  }
}
```

The `msbuild-sdks` section is required only if using Approach A (MSTest.Sdk project SDK). If using Approach B (PackageReference), omit `msbuild-sdks`.

---

## `Directory.Build.props` Template

Minimal shape. All constraints from PROJECT.md non-functional requirements:

```xml
<Project>
  <PropertyGroup>
    <!-- Target framework for all projects -->
    <TargetFramework>net10.0</TargetFramework>

    <!-- Enforce nullability analysis -->
    <Nullable>enable</Nullable>

    <!-- Treat all compiler warnings as errors in every project -->
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>

    <!-- Always use the latest C# language version available on the SDK -->
    <LangVersion>latest</LangVersion>

    <!-- Implicit usings reduce boilerplate -->
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
```

Note: `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` applies to **all** projects including test projects. If MSTest.Sdk or NSubstitute analyzers emit suppressible warnings, they may need per-project `<NoWarn>` overrides — verify during Phase 1 build.

---

## API-Shape Notes: .NET 10 Hosting vs .NET 8/9

### `Host.CreateApplicationBuilder` — no breaking changes from .NET 8/9

The method signature and default configuration order are unchanged from .NET 6 (when it was introduced). Default app configuration load order (last-wins):

1. `appsettings.json`
2. `appsettings.{Environment}.json` (e.g. `appsettings.Development.json`)
3. User Secrets (only when `DOTNET_ENVIRONMENT=Development`)
4. Environment variables
5. Command-line arguments

**`appsettings.Local.json` is not loaded automatically.** Phase 1 must add it explicitly after the builder is created:

```csharp
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);
```

Place this call after `Host.CreateApplicationBuilder(args)` and before `builder.Build()`. Using `optional: true` means the file's absence is not an error, which is correct — it is a dev-only override. `reloadOnChange: false` aligns with the "restart on config change" non-goal in PROJECT.md.

### User Secrets in Worker SDK projects

`Host.CreateApplicationBuilder` automatically calls `AddUserSecrets` when the environment is `Development`. However, for Worker SDK projects (`Microsoft.NET.Sdk.Worker`), the SDK does **not** automatically generate the `[assembly: UserSecretsId("...")]` attribute unless `Microsoft.Extensions.Configuration.UserSecrets` is referenced **directly** (not just transitively). Add an explicit reference in `FrigateRelay.Host.csproj`:

```xml
<PackageReference Include="Microsoft.Extensions.Configuration.UserSecrets" Version="10.0.7" />
```

This also enables `dotnet user-secrets init` and `dotnet user-secrets set` to work from the project directory.

### `IHostedLifecycleService` (added in .NET 8, unchanged in .NET 10)

In addition to `IHostedService.StartAsync`/`StopAsync`, .NET 8+ provides `IHostedLifecycleService` with `StartingAsync`, `StartedAsync`, `StoppingAsync`, `StoppedAsync`. The no-op `BackgroundService` placeholder in Phase 1 does not need to implement this interface, but it is available for future phases.

### `IHostApplicationBuilder` vs `IHostBuilder` — choose `IHostApplicationBuilder`

`Host.CreateApplicationBuilder` (returns `HostApplicationBuilder : IHostApplicationBuilder`) is the current recommended idiom. `Host.CreateDefaultBuilder` (returns `IHostBuilder`) is the legacy callback-based pattern. Per Microsoft docs (updated 2026-03-30), new projects should use `IHostApplicationBuilder`. The Worker SDK template generates `Host.CreateApplicationBuilder` by default.

---

## `.gitignore` Note

The canonical content is generated by `dotnet new gitignore` (installs from the `dotnet/sdk` template pack) or can be sourced from [github/gitignore — VisualStudio.gitignore](https://github.com/github/gitignore/blob/main/VisualStudio.gitignore). Key sections: `bin/`, `obj/`, `.vs/`, `*.user`, `TestResults/`, `*.trx`, coverage outputs, `publish/`, NuGet package caches. Do not hand-roll; run `dotnet new gitignore` at repo root during Phase 1.

---

## Open Questions (Decision Required by Architect)

1. **MSTest.Sdk vs PackageReference approach.** MSTest.Sdk (Approach A) is simpler and idiomatic for .NET 10, but has a known limitation: Dependabot cannot auto-update the SDK version in `global.json` ([dependabot-core#12824](https://github.com/dependabot/dependabot-core/issues/12824)). Phase 2's `dependabot.yml` will catch NuGet `PackageReference` versions but not `msbuild-sdks` entries. Approach B (PackageReference) is less elegant but fully Dependabot-compatible. **Architect should decide which to use before writing `.csproj` files.** This research presents both; Approach A is used in the snippet above as the default since it matches the Microsoft documentation examples for net10.0.

2. **`TreatWarningsAsErrors` in test projects.** Setting this globally via `Directory.Build.props` applies to test projects. MSTest.Sdk 4.2.1 with the `Default` profile enables `Microsoft.Testing.Extensions.CodeCoverage`; that extension may emit CS analyzer warnings. If the build breaks at zero warnings, a per-project `<TreatWarningsAsErrors>false</TreatWarningsAsErrors>` override or a `<NoWarn>` list may be needed for test projects only. Cannot verify without a build attempt.

3. **`appsettings.Local.json` file-copy behavior in Worker SDK.** The Worker SDK auto-copies `*.json` files matching the `appsettings.*.json` glob. Verify that `appsettings.Local.json` is included in the copy glob or add an explicit `<Content Include="appsettings.Local.json" CopyToOutputDirectory="PreserveNewest" Condition="Exists('appsettings.Local.json')" />` entry — the `Condition` guard prevents build failure when the file does not exist in CI.

4. **`UserSecretsId` value.** The explicit `<PackageReference Include="Microsoft.Extensions.Configuration.UserSecrets">` triggers MSBuild to auto-generate a `UserSecretsId` GUID in the `.csproj` on first `dotnet user-secrets init`. The architect should decide whether to pre-populate this with a stable GUID in the generated `.csproj` or let `dotnet user-secrets init` generate it.

---

## Sources

1. [.NET 10.0.7 release notes — dotnet/core GitHub](https://github.com/dotnet/core/blob/main/release-notes/10.0/10.0.7/10.0.7.md) — SDK 10.0.203 version confirmed.
2. [.NET 10.0 Update — April 21, 2026 — Microsoft Support](https://support.microsoft.com/en-us/topic/-net-10-0-update-april-21-2026-f163c5c4-30f2-42e8-9775-cf07c04c819b) — April 2026 servicing release.
3. [MSTest SDK configuration — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-mstest-sdk) — MSTest.Sdk 4.1.0 example, profiles, property names.
4. [Run tests with MSTest — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-mstest-running-tests) — MTP vs VSTest, filter syntax, csproj Approach B snippet.
5. [NuGet Gallery — MSTest.Sdk 4.2.1](https://www.nuget.org/packages/MSTest.Sdk) — latest version confirmed 2026-04-07.
6. [NuGet Gallery — MSTest 4.2.1](https://www.nuget.org/packages/MSTest) — meta-package dependencies listed.
7. [NuGet Gallery — FluentAssertions 6.12.2](https://www.nuget.org/packages/FluentAssertions/6.12.2) — TFMs and transitive deps confirmed.
8. [NuGet Gallery — NSubstitute 5.3.0](https://www.nuget.org/packages/nsubstitute/) — latest stable version.
9. [NuGet Gallery — Microsoft.Extensions.Configuration.UserSecrets 10.0.7](https://www.nuget.org/packages/Microsoft.Extensions.Configuration.UserSecrets) — TFMs confirmed.
10. [NuGet Gallery — Microsoft.Extensions.Hosting 10.0.6](https://www.nuget.org/packages/microsoft.extensions.hosting/) — latest NuGet listing at research time.
11. [.NET Generic Host — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host) — `CreateApplicationBuilder` default config sources, updated 2026-03-30.
12. [Worker Services in .NET — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/extensions/workers) — Worker SDK behavior.
13. [dotnet/sdk issue #4007 — UserSecretsId not generated transitively](https://github.com/dotnet/sdk/issues/4007) — Worker SDK user-secrets caveat.
14. [github/gitignore — VisualStudio.gitignore](https://github.com/github/gitignore/blob/main/VisualStudio.gitignore) — canonical .gitignore source.
