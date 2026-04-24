# Build Summary: Plan 3.1 — FrigateRelay.Host + Registrar Runner + Host Tests

## Status: complete

## Tasks Completed

- **Task 1 — FrigateRelay.Host worker project** — complete — commit `2655b1d` (builder agent, Sonnet 4.6)
  - `FrigateRelay.Host.csproj` — `Microsoft.NET.Sdk.Worker` SDK, `UserSecretsId` hard-coded to `9a7f6e02-3c8b-4d2e-9b17-afb4c6e03a10` (Q4), explicit `<Content Include="appsettings.Local.json" CopyToOutputDirectory="PreserveNewest" Condition="Exists(...)">` (Q3), `<PackageReference>` on `Microsoft.Extensions.Hosting` 10.0.7 and `Microsoft.Extensions.Configuration.UserSecrets` 10.0.7, `<GenerateDocumentationFile>true</GenerateDocumentationFile>`. Later (task 2) extended with `<InternalsVisibleTo Include="FrigateRelay.Host.Tests" />`.
  - `Program.cs` — uses `Host.CreateApplicationBuilder(args)`, explicit `AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)`, constructs `PluginRegistrationContext` via the `[SetsRequiredMembers]` ctor, invokes `PluginRegistrarRunner.RunAll` with an empty registrar list (no concrete plugins in Phase 1), registers `PlaceholderWorker` as a hosted service, `await app.RunAsync()`.
  - `PlaceholderWorker.cs` — `BackgroundService` logging "Host started" once at Information level via `LoggerMessage.Define` (allocation-free high-frequency-friendly pattern, even though it's called once), `await Task.Delay(Timeout.Infinite, stoppingToken)` with `OperationCanceledException` catch for graceful exit 0.
  - `PluginRegistrarRunner.cs` — `internal static` class, `RunAll(IEnumerable<IPluginRegistrar> registrars, PluginRegistrationContext context, ILogger logger)`. No reflection over service descriptors (Approach B).
  - `appsettings.json` — minimal `{"Logging":{"LogLevel":...}}` skeleton.

- **Task 2 — FrigateRelay.Host.Tests project + 7 tests** — complete across two commits (orchestrator — builder agent truncated after committing task 1; first reviewer pass surfaced a gap)
  - First commit (`1c1a2f6`) — `FrigateRelay.Host.Tests.csproj` + `PluginRegistrarRunnerTests.cs` (5 tests). Missed two files the plan required.
  - Gap-fix commit — added `PlaceholderWorkerTests.cs` (2 tests) and the three explicit `Microsoft.Extensions.*` PackageReferences the plan listed even though they were already transitive. Gap was caught by the Wave 3 reviewer, not by the orchestrator's own verification — a quality lesson documented below.
  - `FrigateRelay.Host.Tests.csproj` — mirrors `FrigateRelay.Abstractions.Tests.csproj` (Approach B PackageReference) + explicit refs on `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.Configuration`, `Microsoft.Extensions.DependencyInjection` (all 10.0.7), ProjectReference to both src projects.
  - `PluginRegistrarRunnerTests.cs` — 5 `[TestMethod]`:
    1. `RunAll_EmptyRegistrars_DoesNothing`
    2. `RunAll_SingleRegistrar_InvokesRegisterOnceWithSharedContext` — `registrar.Received(1).Register(context)`
    3. `RunAll_MultipleRegistrars_InvokedInOrder` — `Received.InOrder(() => { first.Register(...); second.Register(...); third.Register(...); })`
    4. `RunAll_SharedContext_SameInstancePassedToEveryRegistrar` — `Arg.Is<>(c => ReferenceEquals(c, context))`
    5. `RunAll_RegistrarThrows_PropagatesAndShortCircuits` — verifies exception propagates and later registrars are not invoked (documents current behavior; plan did not require exception isolation).
  - `PlaceholderWorkerTests.cs` — 2 `[TestMethod]`:
    6. `ExecuteAsync_LogsHostStarted_ExactlyOnce` — uses a small in-test `CapturingLogger<T>` instead of NSubstitute (see Decision 7). Starts the worker with a CTS, waits 50 ms for `ExecuteAsync` to emit, cancels, stops, asserts exactly one Information-level "Host started" entry.
    7. `ExecuteAsync_OnCancellation_CompletesWithoutThrowing` — CTS pre-cancelled; round-trip of `StartAsync` + `StopAsync` must not surface `OperationCanceledException`.
  - `FrigateRelay.sln` wired via `dotnet sln add`.

## Files Modified

| File | Change | Commit |
|---|---|---|
| `src/FrigateRelay.Host/FrigateRelay.Host.csproj` | created, then `+InternalsVisibleTo` | `2655b1d`, `1c1a2f6` |
| `src/FrigateRelay.Host/Program.cs` | created (49 lines) | `2655b1d` |
| `src/FrigateRelay.Host/PlaceholderWorker.cs` | created (32 lines) | `2655b1d` |
| `src/FrigateRelay.Host/PluginRegistrarRunner.cs` | created (36 lines) | `2655b1d` |
| `src/FrigateRelay.Host/appsettings.json` | created (8 lines) | `2655b1d` |
| `tests/FrigateRelay.Host.Tests/FrigateRelay.Host.Tests.csproj` | created | `1c1a2f6` |
| `tests/FrigateRelay.Host.Tests/PluginRegistrarRunnerTests.cs` | created (86 lines) | `1c1a2f6` |
| `FrigateRelay.sln` | +Host, +Host.Tests | `2655b1d`, `1c1a2f6` |

## Decisions Made

1. **`PluginRegistrarRunner` is `internal static`**, not `public`.
   *Reason:* It is a composition-time helper consumed only by `Program.cs` in the same assembly. Exposing it publicly would create a stable API surface that a future `AssemblyLoadContext` loader might reasonably **not** want to rely on (the loader owns its own registrar invocation shape). Keeping it internal avoids painting a public contract we'd have to maintain.
   *Testability:* Added `<InternalsVisibleTo Include="FrigateRelay.Host.Tests" />` in the Host csproj (MSBuild item; the Microsoft.NET.Sdk-inherited generator emits the `[InternalsVisibleTo]` assembly attribute at build time). Tests reference internals directly; no test hook needed on the production class.

2. **Registrar discovery is Approach B** (explicit typed list passed to `RunAll`), not reflection over `ServiceDescriptors`.
   *Reason:* AOT-friendly, testable with plain stubs, no runtime reflection over a potentially large DI graph. The future `AssemblyLoadContext` loader in the "design for B" phase substitutes the empty list with registrars discovered from plugin DLLs — the `RunAll` shape does not change.

3. **Exception from one registrar propagates and short-circuits** (test 5 documents this).
   *Reason:* Phase 1 intent is to surface registration failures loudly — there is no recovery path during composition. Later phases may add exception isolation if plugin loadouts grow heterogeneous, but that is a deliberate future decision, not a latent bug.

4. **Wave 3 imports the Abstractions assembly via `ProjectReference`, not `PackageReference`.** NuGet packaging for the abstractions is PROJECT.md non-goal for v1.

5. **No changes to `Directory.Build.props` or `.editorconfig` needed for Wave 3.** Wave 2 already established the scoped `[tests/**.cs]` suppressions; Host tests inherited cleanly with zero per-project overrides.

7. **`CapturingLogger<T>` in-test instead of NSubstitute on `ILogger<PlaceholderWorker>`** (Decision added post-reviewer-gap-fix).
   *Reason:* PLAN-3.1 task 2 called for "NSubstitute ILogger<PlaceholderWorker>". NSubstitute on the `ILogger.Log<TState>(...)` generic method is fragile — matching the generic `TState` slot with `Arg.Any<object>()` works sometimes and silently misses sometimes, and producing a meaningful assertion error when a match fails is hard. A tiny in-test `CapturingLogger<T> : ILogger<T>` that stores `(level, eventId, formatter(state, exception))` tuples makes the test read as plainly as the intent: "there must be exactly one Information entry whose message is 'Host started'." Lower ceremony, cleaner failure messages, zero new dependencies. The deviation is minor (the intent is testability, not the mocking library) and documented here.

6. **Host csproj has `<GenerateDocumentationFile>true</GenerateDocumentationFile>`** — pragmatic move by the builder. This sidesteps the "EnableGenerateDocumentationFile" metacheck for IDE0005 without disabling the rule. Src code is API surface; writing XML docs for it is a legitimate cost.

## Issues Encountered

1. **Builder agent truncation mid-task.** Agent `a10301d2c7df9968b` (Sonnet 4.6) successfully committed task 1 at `2655b1d`, then truncated with the self-narration "Task 1 verification passes. Commit it." before starting task 2. Orchestrator completed task 2 inline. Same failure mode as Wave 2. The builder agent run was ~9 minutes and 33 tool uses; likely an internal output-size limit. Future phases should consider smaller task batches or explicit "write SUMMARY now" checkpoints.

2. **`timeout --signal=SIGINT` does not reliably send SIGINT to a grandchild on WSL** (`bash` → `timeout` → `dotnet run` → `FrigateRelay.Host` exe). With `--foreground`, `timeout` sends SIGINT to `dotnet run`, but the signal does not propagate through to the Host exe child within a piped context. The Host exe is then alive with `dotnet run` zombied until the grandparent bash times out or is killed.
   *Working recipe:* Launch `dotnet run --project ... > log 2>&1 &`, `sleep N` to let it start, `HOSTEXE=$(pgrep -f 'FrigateRelay.Host/bin/Release/net10.0/FrigateRelay.Host$' | head -1)`, `kill -INT $HOSTEXE`, `wait`. That reliably produces exit 0 with the "Application is shutting down..." log line.
   *Downstream ripple:* Phase 2 CI must not use `timeout --signal=SIGINT` against `dotnet run`; encode the `pgrep | kill -INT` recipe in the smoke-test step. A simpler alternative is `dotnet publish -c Release -r linux-x64 --self-contained false` + launching the published exe directly (no `dotnet run` wrapper) — worth exploring in Phase 2.

3. **`SetsRequiredMembers` pattern carried from Wave 2** is now precedent. Every class in this codebase with `required init` properties and a ctor will need the attribute; documenting as a convention to capture in lessons-learned at ship time.

4. **NSubstitute.Analyzers noise was absent** — the Wave 2 scoped editorconfig did its job. Host tests inherited cleanly.

5. **Task 2 was initially incomplete — caught by the Wave 3 reviewer, not by the orchestrator's own verification.** The orchestrator completed task 2 after the builder truncated, but wrote only `PluginRegistrarRunnerTests.cs` (5 tests) and stopped. The plan's `files_touched` frontmatter listed both `PluginRegistrarRunnerTests.cs` AND `PlaceholderWorkerTests.cs`, with two specific [TestMethod] (d, e) under task 2. The orchestrator then ran the full-phase verification gate — `dotnet run --project ... -c Release` reported 5 passing, which cleared the ROADMAP gate (≥ 6 across the solution with Abstractions' 10 already), so the orchestrator moved on. The reviewer's spec-compliance check caught the missing file via the plan's frontmatter. Lesson: **verification-against-the-ROADMAP gate is not the same as verification-against-the-plan's frontmatter.** Future phase builds should grep each plan's `files_touched:` list against `git log --name-only` for the phase commits as a cheap, structural completeness check. Logging this as a lesson-learned candidate for `/shipyard:ship`.

## Verification Results

```
$ dotnet build FrigateRelay.sln -c Release
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

```
$ dotnet run --project tests/FrigateRelay.Abstractions.Tests -c Release --no-build
  total: 10
  succeeded: 10
  failed: 0

$ dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build
  total: 7
  succeeded: 7
  failed: 0
```
Phase-wide: **17 tests pass, 0 failures** (ROADMAP gate ≥ 6; plan gates ≥ 13).

```
$ dotnet sln list
src/FrigateRelay.Abstractions/FrigateRelay.Abstractions.csproj
src/FrigateRelay.Host/FrigateRelay.Host.csproj
tests/FrigateRelay.Abstractions.Tests/FrigateRelay.Abstractions.Tests.csproj
tests/FrigateRelay.Host.Tests/FrigateRelay.Host.Tests.csproj
```

**Graceful shutdown smoke** (per Issue #2 working recipe):
```
$ dotnet run --project src/FrigateRelay.Host -c Release --no-build > /tmp/host-run.log 2>&1 &
$ sleep 3
$ HOSTEXE=$(pgrep -f 'FrigateRelay.Host/bin/Release/net10.0/FrigateRelay.Host$' | head -1)
$ kill -INT $HOSTEXE; wait
$ echo "wait-exit=$?"
wait-exit=0

$ cat /tmp/host-run.log
info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to shut down.
info: FrigateRelay.Host.PlaceholderWorker[1]
      Host started
info: Microsoft.Hosting.Lifetime[0]
      Hosting environment: Production
info: Microsoft.Hosting.Lifetime[0]
      Content root path: /mnt/f/git/FrigateRelay/src/FrigateRelay.Host
info: Microsoft.Hosting.Lifetime[0]
      Application is shutting down...
```
Exit 0, "Host started" logged, "Application is shutting down..." confirms graceful path.

**Forbidden patterns:**
```
$ git grep -nE '(ServicePointManager|Newtonsoft|Serilog|OpenTracing|App\.Metrics|DotNetWorkQueue|\.Result\(|\.Wait\()' src/ tests/
(no matches — exit 1 from git grep = clean)
```

**Host transitive deps** (abbreviated — Worker SDK pulls a deep hosting stack; scanning for non-Microsoft entries):
```
$ dotnet list src/FrigateRelay.Host/FrigateRelay.Host.csproj package --include-transitive | grep -vE '^\s*(Microsoft\.|System\.|Project|\[|---|>$|Top-level|Transitive|$)'
(no third-party entries)
```

## Phase 1 end-to-end status

All Phase 1 ROADMAP success criteria met:

| Criterion | Result |
|---|---|
| `dotnet build -c Release` succeeds zero warnings | ✓ |
| `dotnet run --project src/FrigateRelay.Host` logs "Host started" + exits 0 on SIGINT | ✓ (via `pgrep | kill -INT` recipe; WSL `timeout` caveat documented) |
| `dotnet test` ≥ 6 tests pass | ✓ 15 tests (via `dotnet run` — .NET 10 blocks `dotnet test` against MTP) |
| `FrigateRelay.Abstractions` references only `Microsoft.Extensions.*` | ✓ |
| `git grep ServicePointManager` returns zero source matches | ✓ |

## Phase 2+ forward list

- **Phase 2 CI smoke recipe** — encode `dotnet run` (not `dotnet test`) for tests; `pgrep | kill -INT` (not `timeout --signal=SIGINT`) for graceful-shutdown smoke. Consider publishing self-contained for shutdown smoke to avoid `dotnet run` wrapper entirely.
- **Phase 2 editorconfig verification** — CI should grep `.editorconfig` after any PR to confirm the `[tests/**.cs]` suppression block hasn't been deleted.
- **Phase 3 plugin registration** — follow the Approach-B pattern. Each `IPluginRegistrar` implementation constructs no state; the `Register` call binds `Services` and reads `Configuration` section-by-section. Test at the registrar level, not via the host.
- **Lesson-learned draft entries** (for `/shipyard:ship`):
  - SDK version research is unreliable for unreleased feature bands — always cross-check `https://builds.dotnet.microsoft.com/.../releases.json` before pinning.
  - .NET 10 broke two CLI surfaces: `dotnet test` (blocked for MTP) and `dotnet list package --project` flag order. CI and tooling scripts need both recipes.
  - `[SetsRequiredMembers]` is mandatory on any ctor that sets all `required init` properties; omitting it makes the ctor vestigial.
  - `<InternalsVisibleTo>` MSBuild item is the clean way to expose internals to a single test assembly without a `[assembly:]` attribute in source.
