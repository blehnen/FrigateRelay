# Review: Plan 3.1 (Phase 3) — EventPump + Host wiring + CI consolidation

## Verdict: PASS

Orchestrator review — builder agents truncated repeatedly on Phase 3 tasks; orchestrator implemented and self-reviews against the plan + verification gates.

## Findings

### Critical
None.

### Minor (non-blocking)
- **`EventPump` uses `IOptionsMonitor<HostSubscriptionsOptions>`** instead of `IOptions<T>`. Slight overhead today (no config reload in v1 per PROJECT.md non-goals); forward-compatible with Phase 8's subscription hot-reload if we ever build it. Low-cost defensible choice.
- **Canonical-path copy fallback in `run-tests.sh`** is a workaround for environment divergence between WSL host and SDK container. Works in both places but adds script complexity. Acceptable tradeoff; alternative would be to always write to `TestResults/` and archive from there — would change the Jenkinsfile artifact shape for no user-visible benefit.
- **`FakeSource.ReadEventsAsync` blocks on `Task.Delay(Timeout.Infinite, ct)` after yielding the finite event list.** Without this the pump drains immediately before logs are asserted. Test is correct but subtle; a comment would help a future reader.
- **Program.cs now references `FrigateRelay.Sources.FrigateMqtt` directly** (via project reference in the csproj + `new` in Program.cs). The rest of the Host assembly (EventPump, matcher, dedupe) stays plugin-agnostic — only the composition root knows the concrete plugin. Clean by inspection; matches Approach A.

### Positive
- **Found and fixed the `RunAll`-after-`Build()` latent bug** inherited from Phase 1's simplification. Without Phase 3's real registrar, the bug would have silently dropped plugin registration.
- **`DisposeAsync` idempotency fix** exposed by a real graceful-shutdown smoke test (not a hypothetical). Debugged via stack trace, fixed with `Interlocked.Exchange` guard + targeted `catch (ObjectDisposedException)` wrappers.
- **CI consolidation via `run-tests.sh`** discharges the Phase-2 advisory cleanly: adding a new test project requires NO workflow edit in either `ci.yml` or `Jenkinsfile` — just drop the test project under `tests/*.Tests/`.
- **Parallel pumps** via `Task.WhenAll(sources.Select(PumpAsync))` — one stuck source can't starve others. Defensible even with one source today.
- **Matched-event log line format** uses `LoggerMessage.Define<...>` — allocation-free; matches Phase 1 convention.
- 44 tests total (10 + 16 + 18), well above the ≥15 phase floor.

## Plan frontmatter cross-check

| `files_touched` entry | Present? |
|---|---|
| `src/FrigateRelay.Host/EventPump.cs` | ✅ |
| `src/FrigateRelay.Host/Program.cs` (modified) | ✅ |
| `src/FrigateRelay.Host/PlaceholderWorker.cs` (deleted) | ✅ |
| `tests/FrigateRelay.Host.Tests/EventPumpTests.cs` | ✅ |
| `.github/scripts/run-tests.sh` | ✅ |
| `.github/workflows/ci.yml` (modified) | ✅ |
| `Jenkinsfile` (modified) | ✅ |

All expected files landed.

## Check results

| Command | Result |
|---|---|
| `dotnet build FrigateRelay.sln -c Release` | 0 warnings, 0 errors |
| `bash .github/scripts/run-tests.sh` | 44 pass (10+16+18), 0 fail |
| `bash .github/scripts/run-tests.sh --coverage` | 44 pass; 3 cobertura XMLs at canonical paths |
| `bash .github/scripts/secret-scan.sh scan` | exit 0 |
| `bash .github/scripts/secret-scan.sh selftest` | exit 0, 7 patterns PASS |
| Graceful shutdown smoke (`pgrep \| kill -INT`) | exit 0; logs "Application is shutting down..." → "Event pump stopped" → "MQTT disconnected" |
| `git diff post-plan-phase-3..HEAD -- src/FrigateRelay.Abstractions/` | empty — no new types |
| `git grep -nE '(ManagedMqttClient\|ServicePointManager\|MemoryCache\.Default\|\.Result\(\|\.Wait\(\|Newtonsoft\|Serilog)' src/ tests/ \| grep -vE '///\|//'` | empty |
| `dotnet sln list` | 4 src/test projects (Abstractions, Host, Sources.FrigateMqtt, 3 Tests) |

## Phase 3 closeout

All ROADMAP Phase 3 success criteria met (see SUMMARY-3.1 closeout table).
