---
phase: foundation-abstractions
plan: 3.1
wave: 3
dependencies: [2.1]
must_haves:
  - FrigateRelay.Host Worker SDK project with layered config (appsettings.json + env + user-secrets + appsettings.Local.json)
  - Program.cs uses Host.CreateApplicationBuilder
  - No-op BackgroundService logs "Host started" once and awaits stopping token
  - Plugin-registrar discovery loop resolves IEnumerable<IPluginRegistrar> and invokes Register(PluginRegistrationContext)
  - Host exits 0 on SIGINT within 5 seconds
  - FrigateRelay.Host.Tests with at least one test exercising the registrar loop via an NSubstitute stub
  - All logging goes through M.E.L. console provider only (D2 — no Serilog, no OTel)
files_touched:
  - src/FrigateRelay.Host/FrigateRelay.Host.csproj
  - src/FrigateRelay.Host/Program.cs
  - src/FrigateRelay.Host/PlaceholderWorker.cs
  - src/FrigateRelay.Host/PluginRegistrarRunner.cs
  - src/FrigateRelay.Host/appsettings.json
  - tests/FrigateRelay.Host.Tests/FrigateRelay.Host.Tests.csproj
  - tests/FrigateRelay.Host.Tests/PluginRegistrarRunnerTests.cs
  - tests/FrigateRelay.Host.Tests/PlaceholderWorkerTests.cs
tdd: true
risk: medium
---

# PLAN-3.1 — FrigateRelay.Host, Registrar Loop, and Host Tests

## Context

This plan stands up the runnable-but-work-free host that proves the composition root works end-to-end. The registrar loop is the load-bearing piece: every future plugin (Phase 3+) plugs in by shipping an `IPluginRegistrar` that this loop invokes. Getting this right now means Phase 3 can focus on MQTT without relitigating the DI shape.

Decisions enforced:

- **D1** — `PluginRegistrationContext` is constructed in `Program.cs` with the builder's `Services` and `Configuration` and passed to every discovered `IPluginRegistrar` **before** `builder.Build()`.
- **D2** — Logging is the default M.E.L. console provider only. No Serilog package reference. No OpenTelemetry. The "Host started" log line is a plain `_logger.LogInformation("Host started")` call inside the `BackgroundService`.
- **D4** — SDK pin is already established by PLAN-1.1's `global.json`; this plan's csproj just inherits it.

Open-question resolutions baked into this plan:

- **Q1 (applied)** — Host csproj uses `<Project Sdk="Microsoft.NET.Sdk.Worker">` (not MSTest.Sdk — this is the host, not a test project). Host.Tests csproj uses **Approach B** (Microsoft.NET.Sdk + MSTest 4.2.1 PackageReference) for Dependabot coverage, matching PLAN-2.1.
- **Q3 — `appsettings.Local.json` copy-to-output.** Decision: **explicit `<Content>` item with `Condition="Exists('appsettings.Local.json')"` and `CopyToOutputDirectory=PreserveNewest`**. Rationale: the Worker SDK's default `*.json` glob behavior around optional files is environment-sensitive — CI boxes without the file would fail the glob on some SDK builds. An explicit conditional Content item is unambiguous, survives SDK updates, and matches the PROJECT.md requirement that the file is an optional dev-only override. The file itself is NOT created in this plan (it's in `.gitignore` per PLAN-1.1).
- **Q4 — `UserSecretsId` value.** Decision: **hard-code a fresh GUID in the csproj at repo-init time**. Rationale: a stable id avoids ad-hoc `dotnet user-secrets init` drift between contributors (each would generate their own, diverging `.csproj` in PRs); the id is not a secret, and it makes `dotnet user-secrets set` work identically for every clone on first command. The generated GUID is `frigaterelay-host-2026a-dev` in braced-GUID form — the plan's task text specifies the exact constant string for reproducibility: `9a7f6e02-3c8b-4d2e-9b17-afb4c6e03a10`.

Host started log line requirement: the `PlaceholderWorker : BackgroundService` logs `"Host started"` at Information exactly once in `ExecuteAsync`, then awaits `stoppingToken` via `await Task.Delay(Timeout.Infinite, stoppingToken)` inside a try/catch that swallows `OperationCanceledException` so graceful shutdown returns exit code 0.

## Dependencies

- **PLAN-2.1** — FrigateRelay.Abstractions must expose `IPluginRegistrar` and `PluginRegistrationContext`.

## Tasks

<task id="1" files="src/FrigateRelay.Host/FrigateRelay.Host.csproj, src/FrigateRelay.Host/appsettings.json, src/FrigateRelay.Host/PlaceholderWorker.cs, src/FrigateRelay.Host/PluginRegistrarRunner.cs, src/FrigateRelay.Host/Program.cs" tdd="false">
  <action>Create src/FrigateRelay.Host/FrigateRelay.Host.csproj as Microsoft.NET.Sdk.Worker with UserSecretsId=9a7f6e02-3c8b-4d2e-9b17-afb4c6e03a10, RootNamespace=FrigateRelay.Host, AssemblyName=FrigateRelay.Host, IsPackable=false, and an explicit ItemGroup with &lt;Content Include="appsettings.Local.json" CopyToOutputDirectory="PreserveNewest" Condition="Exists('appsettings.Local.json')" /&gt;. PackageReferences: Microsoft.Extensions.Configuration.UserSecrets 10.0.7 (required explicitly in Worker SDK per RESEARCH §Host Project SDK Choice). ProjectReference to FrigateRelay.Abstractions. Create appsettings.json with minimal {"Logging":{"LogLevel":{"Default":"Information","Microsoft.Hosting.Lifetime":"Information"}}}. Create PlaceholderWorker.cs implementing BackgroundService; ExecuteAsync logs "Host started" at Information exactly once via injected ILogger&lt;PlaceholderWorker&gt;, then `try { await Task.Delay(Timeout.Infinite, stoppingToken); } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }`. Create PluginRegistrarRunner.cs as internal static class with method `public static void RunAll(IEnumerable&lt;IPluginRegistrar&gt; registrars, PluginRegistrationContext context, ILogger logger)` that iterates registrars, logs "Registering plugin {Name}" per registrar using its type name, and calls Register(context). Create Program.cs using Host.CreateApplicationBuilder(args): (1) call builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false) after build; (2) construct PluginRegistrationContext(builder.Services, builder.Configuration); (3) discover registrars from the CURRENT builder.Services collection (iterate descriptors where ServiceType==typeof(IPluginRegistrar) and ImplementationInstance or ImplementationType is available, instantiate any ImplementationType via Activator.CreateInstance for parameterless ctors only) OR, simpler and preferred, expose an extension method `IServiceCollection.AddPluginRegistrar&lt;T&gt;()` that adds T as a keyed singleton AND immediately invokes its Register method against the context — choose the second shape (static discovery at composition time, no reflection over ServiceDescriptors) and document the choice in a top-of-file comment. For Phase 1 there are no registrars yet; the loop is wired but iterates an empty sequence. Then builder.Services.AddHostedService&lt;PlaceholderWorker&gt;(); build; run.</action>
  <verify>cd /mnt/f/git/FrigateRelay && dotnet sln add src/FrigateRelay.Host/FrigateRelay.Host.csproj && dotnet build src/FrigateRelay.Host/FrigateRelay.Host.csproj -c Release 2>&1 | tee /tmp/host-build.log && ! grep -Ei "warning|error" /tmp/host-build.log && grep -q "UserSecretsId>9a7f6e02-3c8b-4d2e-9b17-afb4c6e03a10" src/FrigateRelay.Host/FrigateRelay.Host.csproj</verify>
  <done>Host project builds clean with zero warnings. UserSecretsId is the hard-coded GUID. appsettings.Local.json has a conditional Content item. Program.cs uses Host.CreateApplicationBuilder and invokes the registrar loop.</done>
</task>

<task id="2" files="tests/FrigateRelay.Host.Tests/FrigateRelay.Host.Tests.csproj, tests/FrigateRelay.Host.Tests/PluginRegistrarRunnerTests.cs, tests/FrigateRelay.Host.Tests/PlaceholderWorkerTests.cs" tdd="true">
  <action>Write tests first. Create tests/FrigateRelay.Host.Tests/FrigateRelay.Host.Tests.csproj as Microsoft.NET.Sdk with OutputType=Exe, EnableMSTestRunner=true, TestingPlatformDotnetTestSupport=true, IsPackable=false, PackageReferences: MSTest 4.2.1, FluentAssertions 6.12.2, NSubstitute 5.3.0, NSubstitute.Analyzers.CSharp 1.0.17 (PrivateAssets=all), Microsoft.Extensions.Logging.Abstractions 10.0.0, Microsoft.Extensions.Configuration 10.0.0, Microsoft.Extensions.DependencyInjection 10.0.0. ProjectReference to FrigateRelay.Host and FrigateRelay.Abstractions. Write PluginRegistrarRunnerTests.cs with [TestMethod]: (a) RunAll_WithOneStubRegistrar_CallsRegisterOnce — NSubstitute stubs an IPluginRegistrar; constructs a PluginRegistrationContext with a fresh ServiceCollection and ConfigurationBuilder().Build(); invokes PluginRegistrarRunner.RunAll([stub], context, NullLogger.Instance); asserts stub.Received(1).Register(context). (b) RunAll_WithEmptySequence_DoesNothing — passes empty array, asserts no throw. (c) RunAll_PassesSameContextToAllRegistrars — two stubs; asserts both received the SAME context reference (FluentAssertions .Should().BeSameAs()). Write PlaceholderWorkerTests.cs with [TestMethod]: (d) ExecuteAsync_LogsHostStarted_ExactlyOnce — NSubstitute ILogger&lt;PlaceholderWorker&gt;, run worker with a CTS cancelled after 50ms, await StopAsync, assert logger received an Information-level call whose message contains "Host started" exactly once. (e) ExecuteAsync_OnCancellation_CompletesWithoutThrowing — CTS cancelled immediately, assert StartAsync+StopAsync round-trip completes without throwing and exits cleanly. Total tests: 5 in this file alone; combined with 8 in PLAN-2.1 this puts the solution-wide total at 13, well above the ≥6 bar.</action>
  <verify>cd /mnt/f/git/FrigateRelay && dotnet sln add tests/FrigateRelay.Host.Tests/FrigateRelay.Host.Tests.csproj && dotnet test tests/FrigateRelay.Host.Tests -c Release 2>&1 | tee /tmp/host-test.log && grep -E "Passed!|Passed:" /tmp/host-test.log</verify>
  <done>Host.Tests reports 5+ passing tests, 0 failures. Registrar loop test confirms Register is invoked with the shared context. Worker test confirms "Host started" logged exactly once and cancellation is clean.</done>
</task>

<task id="3" files="src/FrigateRelay.Host/Program.cs, src/FrigateRelay.Host/PlaceholderWorker.cs" tdd="false">
  <action>Manual runtime verification of the success criteria. This task does not create or modify files beyond minor log-output polishing if the timeout-based smoke test reveals an issue. Run the host under a 10-second timeout on Linux/WSL and capture its output; send SIGINT mid-run via the timeout utility and confirm exit code 0 and "Host started" appearing in the captured log. Document the Windows-equivalent invocation in this task so reviewers can reproduce on either OS. Linux/WSL: `timeout --signal=SIGINT --preserve-status 5 dotnet run --project src/FrigateRelay.Host -c Release 2>&1 | tee /tmp/host-run.log ; echo exit=$?`. Windows PowerShell equivalent (documented for the record, not executed here): `$p = Start-Process -FilePath dotnet -ArgumentList 'run','--project','src/FrigateRelay.Host','-c','Release' -PassThru -RedirectStandardOutput host-run.log ; Start-Sleep -Seconds 3 ; Stop-Process -Id $p.Id ; Get-Content host-run.log`. The Linux form uses `timeout --signal=SIGINT` which is the faithful SIGINT-and-let-it-shutdown-gracefully test; exit code 0 (preserved via --preserve-status when the process exits normally after SIGINT) confirms the cancellation path.</action>
  <verify>cd /mnt/f/git/FrigateRelay && timeout --signal=SIGINT --preserve-status 5 dotnet run --project src/FrigateRelay.Host -c Release 2>&1 | tee /tmp/host-run.log ; RC=$? ; grep -q "Host started" /tmp/host-run.log && echo "LOG_OK" ; [ $RC -eq 0 ] && echo "EXIT_OK"</verify>
  <done>/tmp/host-run.log contains "Host started". Process exits with code 0 within 5 seconds of SIGINT (both LOG_OK and EXIT_OK printed).</done>
</task>

## Verification

Run from repo root `/mnt/f/git/FrigateRelay/`:

```bash
# Whole-solution build clean
dotnet build FrigateRelay.sln -c Release

# Whole-solution tests: must report ≥6 passing, 0 failures
dotnet test FrigateRelay.sln -c Release --no-build 2>&1 | tee /tmp/all-test.log
grep -E "Passed:\s+[0-9]+" /tmp/all-test.log
grep -E "Failed:\s+0" /tmp/all-test.log

# Runtime: host logs "Host started" and exits 0 on SIGINT within 5s (Linux/WSL)
timeout --signal=SIGINT --preserve-status 5 dotnet run --project src/FrigateRelay.Host -c Release 2>&1 | tee /tmp/host-run.log
echo "exit=$?"
grep -q "Host started" /tmp/host-run.log && echo LOG_OK

# Windows PowerShell equivalent (for CI parity documentation):
#   $p = Start-Process dotnet -ArgumentList 'run','--project','src/FrigateRelay.Host','-c','Release' `
#        -PassThru -RedirectStandardOutput host-run.log
#   Start-Sleep -Seconds 3
#   Stop-Process -Id $p.Id
#   Select-String -Path host-run.log -Pattern 'Host started'

# Abstractions deps still clean
dotnet list package --include-transitive --project src/FrigateRelay.Abstractions/FrigateRelay.Abstractions.csproj \
  | grep -v -E "Microsoft\.Extensions\.|System\." | grep -E "^\s*>" && echo FAIL || echo OK

# Structural invariants
git grep ServicePointManager ; [ $? -ne 0 ] && echo OK_NO_SPM
git grep -nE '\.(Result|Wait)\(' src/ ; [ $? -ne 0 ] && echo OK_NO_SYNC
```

Expected: `dotnet build` clean, `dotnet test` reports **≥6 passing tests, 0 failures** across the solution (target: 13), `host-run.log` contains "Host started" and process exited 0, no `ServicePointManager`, no `.Result`/`.Wait()` in src.
