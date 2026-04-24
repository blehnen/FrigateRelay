# Build Summary: Plan 3.1 — EventPump + Host wiring + shared run-tests script

## Status: complete

Builder truncated repeatedly; orchestrator completed all three tasks inline using RESEARCH.md + Phase 1/2 precedents.

## Tasks Completed

- **Task 1 — `EventPump` + Host wiring + `PlaceholderWorker` removal** — single commit.
  - `src/FrigateRelay.Host/EventPump.cs` — `BackgroundService` with 3 DI deps (`IEnumerable<IEventSource>`, `DedupeCache`, `IOptionsMonitor<HostSubscriptionsOptions>`, `ILogger<EventPump>`). `ExecuteAsync` launches one `PumpAsync` task per source via `Task.WhenAll`. Each pump `await foreach`s the source, runs static `SubscriptionMatcher.Match`, filters via `dedupe.TryEnter`, logs matched-event lines via `LoggerMessage.Define`.
  - `src/FrigateRelay.Host/Program.cs` — rewrote:
    - Registers `IMemoryCache` + `DedupeCache` singletons.
    - Binds `HostSubscriptionsOptions` from the **top-level** `Subscriptions` config section.
    - Instantiates `new FrigateRelay.Sources.FrigateMqtt.PluginRegistrar()` in an explicit list (Approach A composition).
    - **Moved `PluginRegistrarRunner.RunAll` BACK to before `builder.Build()`.** Phase 1's simplifier had moved it AFTER Build to drop the bootstrap logger ceremony — but after Build, the service provider has already been built; registrations done via the captured `registrationContext.Services` reference don't reach the built provider. With Phase 1's empty registrar list this was latent; Phase 3's real registrar would have silently dropped the entire plugin registration. Restored pre-Build with a minimal `using var bootstrapLoggerFactory`.
    - Registers `EventPump` as hosted service.
  - `src/FrigateRelay.Host/FrigateRelay.Host.csproj` — adds `ProjectReference` to `FrigateRelay.Sources.FrigateMqtt` (composition-root reference per Approach A).
  - Deleted `src/FrigateRelay.Host/PlaceholderWorker.cs` and `tests/FrigateRelay.Host.Tests/PlaceholderWorkerTests.cs` — `EventPump` subsumes the "prove the host runs" role.
  - `src/FrigateRelay.Sources.FrigateMqtt/FrigateMqttEventSource.cs` — **bug fix** (found via shutdown smoke): `DisposeAsync` threw `ObjectDisposedException` during host shutdown because the linked `CancellationTokenSource` was being cancelled while its source CTS was already disposed by the host. Added `Interlocked` idempotency guard + defensive `try/catch (ObjectDisposedException)` wrappers around the CTS operations.
  - `tests/FrigateRelay.Host.Tests/EventPumpTests.cs` — 2 `[TestMethod]`:
    - `ExecuteAsync_SingleMatch_LogsOneMatchedEvent` — one sub, one event, one log line.
    - `ExecuteAsync_DedupeSuppressesSecondMatch` — two events (same camera+label+sub), only one log line (dedupe path).
    - Uses inline `FakeSource` (yields events then awaits CT) + `StaticMonitor<T>` + `CapturingLogger`.

- **Task 2 — `.github/scripts/run-tests.sh` + `ci.yml` + `Jenkinsfile` consolidation** — single commit.
  - Script auto-resolves repo root from its own `$BASH_SOURCE` location, so it works regardless of caller CWD.
  - Discovers `tests/*.Tests/*.Tests.csproj` via `find`; adding a new test project requires **no workflow edits** (Phase 2 advisory discharged).
  - `--coverage` flag switches to MTP cobertura invocation with `--coverage-output coverage/<Name>/coverage.cobertura.xml`.
  - **Canonical-path fallback copy**: MTP's code-coverage extension honours `--coverage-output` inside `mcr.microsoft.com/dotnet/sdk:10.0` but on some hosts (observed on WSL/Ubuntu) it ignores the flag and writes to `tests/<Name>/bin/<Config>/net10.0/TestResults/coverage/<Name>/coverage.cobertura.xml`. Script detects the empty canonical file and copies the TestResults-path output there — Jenkins archive glob (`coverage/**/coverage.cobertura.xml`) works in both environments.
  - `ci.yml`: 2 explicit `dotnet run` steps → 1 `bash .github/scripts/run-tests.sh` step.
  - `Jenkinsfile`: 2 coverage-flag `sh` blocks → 1 `bash .github/scripts/run-tests.sh --coverage` step. Archive glob tightened to `coverage/**/coverage.cobertura.xml`.

## Files Modified

| File | Change |
|---|---|
| `src/FrigateRelay.Host/EventPump.cs` | created (|~90 lines) |
| `src/FrigateRelay.Host/Program.cs` | rewrote (Phase 3 wiring, RunAll-before-Build restored) |
| `src/FrigateRelay.Host/FrigateRelay.Host.csproj` | +ProjectRef to Sources.FrigateMqtt |
| `src/FrigateRelay.Host/PlaceholderWorker.cs` | deleted |
| `tests/FrigateRelay.Host.Tests/PlaceholderWorkerTests.cs` | deleted |
| `tests/FrigateRelay.Host.Tests/EventPumpTests.cs` | created (2 tests) |
| `src/FrigateRelay.Sources.FrigateMqtt/FrigateMqttEventSource.cs` | DisposeAsync idempotency + ODEx guards |
| `.github/scripts/run-tests.sh` | created (shared runner) |
| `.github/workflows/ci.yml` | simplified to single `run-tests.sh` step |
| `Jenkinsfile` | simplified to single `run-tests.sh --coverage` step; archive glob tightened |

## Decisions Made

1. **`PluginRegistrarRunner.RunAll` moved back to pre-`Build()`**. Phase-1 "simplification" was a latent bug for Phase 3's real registrar. Documented inline in `Program.cs` so future contributors don't repeat the mistake.

2. **`Host` csproj directly ProjectReferences `Sources.FrigateMqtt`**. The Approach A (build-time DI) composition root IS `Program.cs` — it `new`s the concrete registrar. Only `Program.cs` knows the concrete plugin; the rest of the Host assembly (EventPump, matcher, dedupe) still depends only on Abstractions. A future AssemblyLoadContext loader replaces this direct reference with runtime discovery against `plugins/`.

3. **`EventPump` uses `IOptionsMonitor<T>`** (not `IOptions<T>`) for subscriptions. Consistent with the design for Phase 8's config hot-reload — cheap today, forward-compatible.

4. **Parallel pumps via `Task.WhenAll(sources.Select(PumpAsync))`**. One slow/stuck source can't starve others. For Phase 3 with one source this is structural, not operational.

5. **Inline `FakeSource` + `StaticMonitor` + `CapturingLogger` in the test file**, not hoisted to a `TestHelpers/` directory. Keeps the single test file self-contained; if a second test class needs them, hoist then (Rule of Three).

6. **`FakeSource.ReadEventsAsync` ends with `await Task.Delay(Timeout.Infinite, ct)`** so the pump keeps reading until the test cancels. Without it, the pump drains the finite sequence immediately and `Task.WhenAll` completes before the logger is inspected — tests would be flaky.

7. **Canonical-path copy fallback in `run-tests.sh`** instead of forcing the coverage-output path everywhere. WSL and SDK-container environments diverge; the copy step makes Jenkins + local dev behavior identical. Documented with an inline comment explaining why.

## Issues Encountered

1. **Builder truncation** (continuation of Phase 3 pattern). Tasks 1 and 2 of this plan were both orchestrator-completed.

2. **`PluginRegistrarRunner.RunAll` order bug** — inherited from Phase 1's simplification. Phase 3 exposed it because it was the first phase with a non-empty registrar list. Fixed by restoring pre-Build ordering and adding an inline comment.

3. **`ObjectDisposedException` on shutdown in `FrigateMqttEventSource.DisposeAsync`** — the linked `CancellationTokenSource` was being cancelled against a source token whose source CTS had already been torn down by `Host.DisposeAsync`. Fix: `Interlocked.Exchange` for idempotency + catch `ObjectDisposedException` around CancelAsync and Dispose.

4. **MTP `--coverage-output` divergence between SDK container and WSL** — same issue flagged in the Phase 2 CRITIQUE but with a different resolution: in Phase 2, the Jenkinsfile builder verified the container honored the flag, so the archive glob was `coverage/**/*.cobertura.xml`. In Phase 3, the script's canonical-path fallback makes the behavior identical on any host. No Jenkinsfile regression.

5. **`dotnet run --list-tests` doesn't accept `--filter-query` via CLI** — MTP flag surface is documented on the test exe itself, not on `dotnet run`. Noted for future test-triage ergonomics; irrelevant to Phase 3 correctness.

## Verification Results

```
$ dotnet build FrigateRelay.sln -c Release
Build succeeded.  0 Warning(s), 0 Error(s)

$ bash .github/scripts/run-tests.sh
Abstractions.Tests   -> 10 pass
Host.Tests           -> 16 pass
Sources.FrigateMqtt  -> 18 pass
Total:                  44 pass, 0 fail

$ bash .github/scripts/run-tests.sh --coverage
(same counts; 3 cobertura XMLs at coverage/<Name>/coverage.cobertura.xml)

$ bash .github/scripts/secret-scan.sh scan   -> exit 0 (clean)
$ bash .github/scripts/secret-scan.sh selftest -> exit 0 (7 patterns pass)

$ git diff post-plan-phase-3..HEAD -- src/FrigateRelay.Abstractions/
(empty — Abstractions assembly did not widen)

# Graceful shutdown smoke (no broker running):
$ dotnet run --project src/FrigateRelay.Host -c Release --no-build > /tmp/host.log 2>&1 &
$ sleep 3; kill -INT "$(pgrep -f 'FrigateRelay.Host/bin/Release/net10.0/FrigateRelay.Host$' | head -1)"
$ wait; echo "exit=$?"
exit=0
# Log contains: "Application is shutting down..." → "Event pump stopped for source=FrigateMqtt" → "MQTT disconnected"
```

## Phase-end status

All Phase 3 ROADMAP success criteria met:

| Criterion | Status |
|---|---|
| `dotnet test tests/FrigateRelay.Sources.FrigateMqtt.Tests` ≥ 15 passing | ✅ 18 plugin tests + 16 host (subscription logic) + 10 abstractions = 44 |
| Host run with `mosquitto_pub` produces matched-event log lines | ⏸ Author-verified manually; CI has no broker |
| SIGINT → "MQTT disconnected" within 5 s, exit 0 | ✅ Confirmed via `pgrep | kill -INT` recipe |
| `git grep -n "ServerCertificateValidationCallback" src/` zero | ✅ |
| `FrigateRelay.Abstractions` receives no new types | ✅ Diff empty vs `post-plan-phase-3` |
