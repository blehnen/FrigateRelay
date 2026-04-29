---
phase: phase-4-action-dispatcher-blueiris
plan: 3.2
wave: 3
dependencies: [2.1, 2.2]
must_haves:
  - tests/FrigateRelay.IntegrationTests/ project with MTP wiring
  - MosquittoFixture (Testcontainers) with anonymous-auth via WithResourceMapping
  - MqttToBlueIris_HappyPath integration test using Testcontainers Mosquitto + WireMock.Net BlueIris stub
  - run-tests.sh --skip-integration flag (Windows leg)
  - ci.yml Windows runner skips integration tests
  - Jenkinsfile doc-comment about Docker socket precondition
files_touched:
  - tests/FrigateRelay.IntegrationTests/FrigateRelay.IntegrationTests.csproj
  - tests/FrigateRelay.IntegrationTests/Fixtures/MosquittoFixture.cs
  - tests/FrigateRelay.IntegrationTests/MqttToBlueIrisSliceTests.cs
  - .github/scripts/run-tests.sh
  - .github/workflows/ci.yml
  - Jenkinsfile
  - FrigateRelay.sln
tdd: false
risk: high
---

# Plan 3.2: Integration test project + CI Windows-skip flag

## Context

Stands up the new `tests/FrigateRelay.IntegrationTests/` project with the end-to-end `MqttToBlueIris_HappyPath` test (Testcontainers Mosquitto + WireMock.Net BlueIris stub — RESEARCH §4 + §5), and adds the `--skip-integration` flag to `.github/scripts/run-tests.sh` so the Windows CI matrix leg can opt out (RESEARCH §8 caveat — Testcontainers can't run Linux containers on `windows-latest`). Owns ROADMAP deliverables 10, 11, 12, 13.

Wave 3 (parallel with PLAN-3.1) because this plan touches CI scripts, the new IntegrationTests project, and the test fixture — all disjoint from PLAN-3.1's `EventPump.cs`, `Program.cs`, and `SubscriptionOptions.cs`. Both Wave-3 plans depend on Wave-2 having delivered a working BlueIris plugin + dispatcher, but they don't conflict with each other on files.

**Risk: high** — Testcontainers + WireMock + a real host startup is the most cross-cutting test in the suite to date. RESEARCH §9 Risk 2 + §8 caveats apply. If the Linux CI run fails due to Docker-in-CI issues, the builder must surface that fast (don't muscle through with `Thread.Sleep` waits or by widening cancellation tokens).

## Dependencies

- PLAN-2.1 (`ChannelActionDispatcher` runs end-to-end).
- PLAN-2.2 (`BlueIrisActionPlugin` + registrar are wired and HTTP-callable).
- PLAN-3.1 is **NOT** a hard dependency — but for the integration test to actually fire BlueIris on a matched event, `EventPump` must dispatch (which is PLAN-3.1's work). **Practical wave-3 ordering:** both 3.1 and 3.2 can be drafted in parallel, but the integration test cannot pass until 3.1 is merged. Builder may run them concurrently and merge 3.1 first — or sequence 3.2 after 3.1 if simpler.

## Files touched

- `tests/FrigateRelay.IntegrationTests/FrigateRelay.IntegrationTests.csproj` (create)
- `tests/FrigateRelay.IntegrationTests/Fixtures/MosquittoFixture.cs` (create)
- `tests/FrigateRelay.IntegrationTests/MqttToBlueIrisSliceTests.cs` (create)
- `.github/scripts/run-tests.sh` (modify — add `--skip-integration` filter)
- `.github/workflows/ci.yml` (modify — pass `--skip-integration` on Windows leg)
- `Jenkinsfile` (modify — add doc-comment about Docker socket precondition)
- `FrigateRelay.sln` (modify — `dotnet sln add` the new test project)

## Tasks

### Task 1: Create FrigateRelay.IntegrationTests project + MosquittoFixture
**Files:** `tests/FrigateRelay.IntegrationTests/FrigateRelay.IntegrationTests.csproj`, `tests/FrigateRelay.IntegrationTests/Fixtures/MosquittoFixture.cs`, `FrigateRelay.sln`
**Action:** create
**Description:**

Create the test csproj mirroring the Phase-3 test projects' shape but with `Testcontainers` and `WireMock.Net` package refs:

- `<Sdk>Microsoft.NET.Sdk</Sdk>`, `<TargetFramework>net10.0</TargetFramework>`, `<OutputType>Exe</OutputType>`.
- MSTest v3 + Microsoft.Testing.Platform packages (mirror PLAN-1.2's test csproj exactly).
- `<PackageReference Include="FluentAssertions" Version="6.12.2" />` — pinned.
- `<PackageReference Include="Testcontainers" Version="*" />` (latest 4.x stable per RESEARCH §4).
- `<PackageReference Include="WireMock.Net" Version="*" />` (matches PLAN-2.2's pin; aligned).
- `<PackageReference Include="MQTTnet" Version="5.*" />` — for publishing the test event payload.
- `<ProjectReference Include="..\..\src\FrigateRelay.Host\FrigateRelay.Host.csproj" />` (the integration test owns the host startup).
- `<ProjectReference Include="..\..\src\FrigateRelay.Plugins.BlueIris\FrigateRelay.Plugins.BlueIris.csproj" />`.
- `<ProjectReference Include="..\..\src\FrigateRelay.Sources.FrigateMqtt\FrigateRelay.Sources.FrigateMqtt.csproj" />`.

Add to solution: `dotnet sln FrigateRelay.sln add tests/FrigateRelay.IntegrationTests/FrigateRelay.IntegrationTests.csproj`.

Create `Fixtures/MosquittoFixture.cs` (RESEARCH §4 working sample, with anonymous-auth via `WithResourceMapping`):

```csharp
using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace FrigateRelay.IntegrationTests.Fixtures;

internal sealed class MosquittoFixture : IAsyncDisposable
{
    private readonly IContainer _container;

    public MosquittoFixture()
    {
        var conf = "listener 1883\nallow_anonymous true\n";
        _container = new ContainerBuilder()
            .WithImage("eclipse-mosquitto:2")
            .WithPortBinding(1883, true)
            .WithResourceMapping(Encoding.UTF8.GetBytes(conf), "/mosquitto/config/mosquitto.conf")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(1883))
            .Build();
    }

    public string Hostname => _container.Hostname;
    public int Port => _container.GetMappedPublicPort(1883);

    public ValueTask InitializeAsync() => new(_container.StartAsync());
    public async ValueTask DisposeAsync() => await _container.DisposeAsync().ConfigureAwait(false);
}
```

Note `internal sealed` — fixtures don't need to be public.

**Acceptance Criteria:**
- `tests/FrigateRelay.IntegrationTests/FrigateRelay.IntegrationTests.csproj` builds: `dotnet build tests/FrigateRelay.IntegrationTests/FrigateRelay.IntegrationTests.csproj -c Release` returns exit 0 zero warnings.
- The csproj has all four `ProjectReference` entries listed above.
- `MosquittoFixture` uses `WithResourceMapping` to inject the conf (RESEARCH §4 — env-var conf is NOT supported).
- `git grep -n "FrigateRelay.IntegrationTests" FrigateRelay.sln` returns at least one match.

### Task 2: Implement MqttToBlueIris_HappyPath integration test
**Files:** `tests/FrigateRelay.IntegrationTests/MqttToBlueIrisSliceTests.cs`
**Action:** create + test
**Description:**

End-to-end test: spin up Mosquitto + WireMock, build a `Host.CreateApplicationBuilder` with in-memory configuration pointing at both, run the host briefly, publish a Frigate event payload to `frigate/events`, await the dispatch, assert WireMock received exactly one GET.

Test:

```csharp
[TestClass]
public sealed class MqttToBlueIrisSliceTests
{
    [TestMethod]
    [Timeout(30_000)]   // ROADMAP <30s SLO
    public async Task MqttToBlueIris_HappyPath()
    {
        // 1. Mosquitto.
        await using var mosquitto = new MosquittoFixture();
        await mosquitto.InitializeAsync().ConfigureAwait(false);

        // 2. WireMock BlueIris stub.
        using var wireMock = WireMockServer.Start();
        wireMock.Given(Request.Create()
            .UsingGet()
            .WithPath("/admin")
            .WithParam("camera", "front")
            .WithParam("trigger", "1"))
            .RespondWith(Response.Create().WithStatusCode(200));

        // 3. Host with in-memory config.
        var builder = Host.CreateApplicationBuilder([]);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BlueIris:TriggerUrlTemplate"] = $"{wireMock.Urls[0]}/admin?camera={{camera}}&trigger=1",
            ["BlueIris:RequestTimeout"] = "00:00:05",
            ["FrigateMqtt:Host"] = mosquitto.Hostname,
            ["FrigateMqtt:Port"] = mosquitto.Port.ToString(CultureInfo.InvariantCulture),
            ["FrigateMqtt:EventTopic"] = "frigate/events",   // confirm exact key from MqttOptions.cs at impl time
            ["Subscriptions:0:Name"] = "FrontCam",
            ["Subscriptions:0:Camera"] = "front",
            ["Subscriptions:0:Label"] = "person",
            ["Subscriptions:0:Actions:0"] = "BlueIris",
        });

        // Wire host services + plugin registrars exactly as Program.cs does.
        // Builder note: extract a small `HostBootstrap.ConfigureServices(builder)` helper from Program.cs
        // so this test calls the same wiring path. Without that, this test duplicates Program.cs and rots.
        HostBootstrap.ConfigureServices(builder);

        using var app = builder.Build();
        await app.StartAsync().ConfigureAwait(false);

        try
        {
            // 4. Publish a Frigate "new" event payload (RESEARCH §6).
            var payload = """
            {
              "type": "new",
              "before": null,
              "after": {
                "id": "ev-test-001",
                "camera": "front",
                "label": "person",
                "stationary": false,
                "false_positive": false,
                "score": 0.91,
                "start_time": 1745558400.0,
                "current_zones": ["driveway"],
                "entered_zones": ["driveway"]
              }
            }
            """;

            var mqttFactory = new MqttClientFactory();
            using var client = mqttFactory.CreateMqttClient();
            await client.ConnectAsync(new MqttClientOptionsBuilder()
                .WithTcpServer(mosquitto.Hostname, mosquitto.Port)
                .Build()).ConfigureAwait(false);
            await client.PublishStringAsync("frigate/events", payload).ConfigureAwait(false);

            // 5. Await dispatch — poll WireMock until 1 hit or 10s elapse.
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                if (wireMock.LogEntries.Count() >= 1) break;
                await Task.Delay(100).ConfigureAwait(false);
            }

            // 6. Assert.
            var matchingRequests = wireMock.FindLogEntries(
                Request.Create().UsingGet().WithPath("/admin").WithParam("camera", "front")).ToList();
            matchingRequests.Should().HaveCount(1, "exactly one BlueIris trigger should fire for one matched Frigate event");
        }
        finally
        {
            await app.StopAsync().ConfigureAwait(false);
            await client.DisconnectAsync().ConfigureAwait(false);   // if still connected
        }
    }
}
```

**Crucial cross-plan ripple:** the test calls `HostBootstrap.ConfigureServices(builder)`. PLAN-3.1's Program.cs currently has its wiring inline. **Action for the builder of this plan:** extract a `internal static class HostBootstrap` in `src/FrigateRelay.Host/` exposing `ConfigureServices(HostApplicationBuilder)` that does everything Program.cs's top-level statements currently do, so both Program.cs and this test share the same wiring path. Without this, the test duplicates wiring and rots the moment Program.cs is touched.

This is a small (~30-line) refactor; document the change in this plan's commit. PLAN-3.1's Task 2 must be updated to delegate to `HostBootstrap.ConfigureServices(builder)` rather than holding the wiring inline. **Architect note:** if Plan 3.1 has already shipped before Plan 3.2, this becomes a follow-up refactor commit owned by Plan 3.2.

`<InternalsVisibleTo Include="FrigateRelay.IntegrationTests" />` must be added to `src/FrigateRelay.Host/FrigateRelay.Host.csproj` so the test can call `HostBootstrap.ConfigureServices` (it's `internal`).

**Acceptance Criteria:**
- `MqttToBlueIris_HappyPath` test passes when run on Linux: `dotnet run --project tests/FrigateRelay.IntegrationTests -c Release -- --filter-query "/*/*/MqttToBlueIrisSliceTests/MqttToBlueIris_HappyPath"`.
- Test completes in <30s (wall-clock — encoded by `[Timeout(30_000)]`).
- `wireMock.FindLogEntries(...)` count is exactly 1 (not zero, not two — proves the dedupe + dispatch path fires once-and-only-once per published event).
- `git grep -nE '\.(Result|Wait)\(' tests/FrigateRelay.IntegrationTests/` returns zero matches (RESEARCH §5 anti-pattern).
- `git grep -nE '192\.168\.|10\.0\.0\.' tests/FrigateRelay.IntegrationTests/` returns zero matches — no hard-coded IPs (Mosquitto is at `mosquitto.Hostname`, WireMock at `wireMock.Urls[0]`).
- `HostBootstrap` exists in `src/FrigateRelay.Host/` as `internal static class`; Program.cs delegates to it.

### Task 3: CI integration — run-tests.sh flag + ci.yml Windows skip + Jenkinsfile doc-comment
**Files:** `.github/scripts/run-tests.sh`, `.github/workflows/ci.yml`, `Jenkinsfile`
**Action:** modify
**Description:**

**`.github/scripts/run-tests.sh`** — add a `--skip-integration` flag that filters out the IntegrationTests project from the discovery loop. Read the script first to understand the current structure (RESEARCH §8: project discovery is `find tests -maxdepth 2 -name '*.Tests.csproj' -type f` at line 41).

```bash
#!/usr/bin/env bash
set -euo pipefail

SKIP_INTEGRATION=false
PASS_THROUGH_ARGS=()
for arg in "$@"; do
    case "$arg" in
        --skip-integration) SKIP_INTEGRATION=true ;;
        *) PASS_THROUGH_ARGS+=("$arg") ;;
    esac
done

# ... existing discovery + run loop ...
# When SKIP_INTEGRATION is true, filter out FrigateRelay.IntegrationTests:
while IFS= read -r proj; do
    if [[ "$SKIP_INTEGRATION" == "true" && "$proj" == *FrigateRelay.IntegrationTests* ]]; then
        echo "Skipping integration test project: $proj"
        continue
    fi
    dotnet run --project "$proj" -c Release --no-build "${PASS_THROUGH_ARGS[@]}"
done < <(find tests -maxdepth 2 -name '*.Tests.csproj' -type f | sort)
```

The exact existing structure is what matters; the builder reads the current script and adapts. The flag MUST:
- Be the only project the flag skips (RESEARCH §8 + Phase 4 instructions).
- Pass through any non-flag args (e.g., `--coverage` from Jenkinsfile) untouched.
- Print to stdout when it skips, so CI logs make the decision visible.

**`.github/workflows/ci.yml`** — add `--skip-integration` to the test-run step on the Windows leg. The cleanest approach: a single test step that uses bash's `if`:

```yaml
- name: Run tests
  shell: bash
  run: |
    if [ "${{ runner.os }}" = "Windows" ]; then
      bash .github/scripts/run-tests.sh --skip-integration
    else
      bash .github/scripts/run-tests.sh
    fi
```

OR two steps with `if: runner.os == 'Linux'` / `if: runner.os == 'Windows'`. Either works; pick the one that produces the cleanest diff against the current `ci.yml`.

**`Jenkinsfile`** — add a doc-comment near the test-run stage explaining the Docker-socket precondition (RESEARCH §8 + Phase 4 instructions). Do NOT change the test execution — Jenkins runs ALL tests including integration, because coverage is the whole point.

```groovy
// PRECONDITION (Phase 4 — Testcontainers): the Jenkins agent host must mount /var/run/docker.sock
// into this Docker SDK container so Testcontainers can launch sibling containers (Mosquitto for
// FrigateRelay.IntegrationTests). Configure with the agent's `args` directive:
//   args '-v /var/run/docker.sock:/var/run/docker.sock'
// Without this, the integration suite fails at container start with "Cannot connect to the
// Docker daemon at unix:///var/run/docker.sock".
```

**Acceptance Criteria:**
- `.github/scripts/run-tests.sh --skip-integration` runs Host.Tests + Abstractions.Tests + Plugins.BlueIris.Tests + Sources.FrigateMqtt.Tests but NOT IntegrationTests. Verify locally on Linux: count of test projects run is `total - 1`.
- `.github/scripts/run-tests.sh` (no flag) runs ALL test projects including IntegrationTests. Verify by tail-grepping the script output for `FrigateRelay.IntegrationTests`.
- `.github/scripts/run-tests.sh --coverage --skip-integration` (composite) passes `--coverage` through and still skips IntegrationTests — flag ordering must not matter.
- `.github/workflows/ci.yml` invokes `--skip-integration` only on the Windows leg (`runner.os == 'Windows'`). The Linux leg runs unflagged.
- `Jenkinsfile` contains the doc-comment block above (or equivalent prose) as a `//` comment block near the test stage. `git grep -n "Testcontainers" Jenkinsfile` returns at least one match.

## Verification

```bash
# Build everything
dotnet build FrigateRelay.sln -c Release

# Run integration test on Linux/WSL
dotnet run --project tests/FrigateRelay.IntegrationTests -c Release -- --filter-query "/*/*/MqttToBlueIrisSliceTests/MqttToBlueIris_HappyPath"

# Verify the flag works
bash .github/scripts/run-tests.sh --skip-integration 2>&1 | grep -c "FrigateRelay.IntegrationTests"
# Expected: 1 (the "Skipping integration test project: ..." line)

bash .github/scripts/run-tests.sh 2>&1 | grep -c "FrigateRelay.IntegrationTests"
# Expected: ≥ 1 (the project is run, not skipped)

# CLAUDE.md invariants
git grep -nE '\.(Result|Wait)\(' src/ tests/
git grep -n "ServicePointManager" src/
git grep -nE '192\.168\.|http://[a-zA-Z0-9.-]+:[0-9]{2,5}' src/ tests/
```

Expected: build clean, integration test passes in <30s, flag works in both directions, all CLAUDE.md invariant greps zero.

## Notes for the builder

- The `HostBootstrap.ConfigureServices` extraction is the trickiest cross-plan ripple in Phase 4. If PLAN-3.1 has already shipped, this plan picks up that refactor as a sibling commit. If 3.1 and 3.2 ship together, coordinate the extraction so Program.cs delegates to `HostBootstrap` from the start.
- `MqttClientFactory.CreateMqttClient()` is the .NET MQTT API surface used in Phase 3; verify the exact namespace and call form by reading `src/FrigateRelay.Sources.FrigateMqtt/PluginRegistrar.cs` (already imports `MQTTnet`).
- The integration test's `wireMock.LogEntries.Count() >= 1` polling loop has a 10-second deadline (well below the 30s test timeout). If the dispatch + retry pipeline takes longer in practice, the dispatcher Polly schedule (3+6+9 = 18s worst case) will exceed the polling budget — but on a happy-path test the first request should succeed immediately and no retries fire. **If the test flakes, do NOT widen the polling budget without investigation** — flakiness usually indicates a real wiring bug.
- The Jenkins doc-comment is the only Jenkins change. Do NOT add `--skip-integration` to Jenkinsfile — coverage-on-integration is the gate's whole point.
- RESEARCH §8 confirms zero edits required to `run-tests.sh` for project discovery; only the new `--skip-integration` flag is a script change, and it's local to the new behavior.
- RESEARCH §9 Risk 2 is the dominant risk for this plan: the Windows CI matrix leg must succeed AFTER the integration project lands. Builder MUST verify the GH Actions run goes green on BOTH ubuntu-latest and windows-latest before declaring this plan done.
