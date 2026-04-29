---
phase: 12-parity-cutover
plan: 1.1
wave: 1
dependencies: []
must_haves:
  - BlueIrisOptions.DryRun bool init property (default false)
  - BlueIrisActionPlugin early-return on DryRun=true with structured `would-execute` log emission via LoggerMessage source-gen
  - Unit test asserts DryRun=true does NOT call the HttpClient and emits exactly one `BlueIrisDryRun` log entry
files_touched:
  - src/FrigateRelay.Plugins.BlueIris/BlueIrisOptions.cs
  - src/FrigateRelay.Plugins.BlueIris/BlueIrisActionPlugin.cs
  - tests/FrigateRelay.Plugins.BlueIris.Tests/BlueIrisActionPluginTests.cs
tdd: true
risk: low
---

# Plan 1.1: BlueIris DryRun flag (per-action, logging-only)

## Context

CONTEXT-12 D5 locks the parity-window mechanism: each `IActionPlugin` gains a `DryRun: true` config flag. When set, `ExecuteAsync` logs a structured `would-execute` entry and returns success normally — no external HTTP call. Per RESEARCH §3 + §4 the dispatcher needs no changes; the change is fully internal to each plugin.

This plan covers BlueIris only. Pushover is PLAN-1.2 (file-disjoint sibling in Wave 1).

**Architect-discretion locked:**

- BlueIrisOptions is a `public sealed record` (per RESEARCH §3) — `DryRun` is a `public bool DryRun { get; init; }` with default `false`.
- BlueIris uses top-level static `Action<ILogger,...>` fields (NOT a nested `Log` class) for logging — RESEARCH §3. The DryRun emitter follows the same pattern: a new top-level static `LogDryRun` field defined via `LoggerMessage.Define`.
- EventId for the DryRun emission: `EventId(203, "BlueIrisDryRun")` (RESEARCH §3 confirmed next-available after 202).
- Early-return placement: at the TOP of `ExecuteAsync` BEFORE template resolution, so a misconfigured template does not throw during the parity window. The DryRun log captures `(camera, label, event_id)` from `EventContext` only, NOT the resolved URL — avoids any chance of leaking the trigger URL into the audit log.
- Counters: `ActionsSucceeded` ticks (per RESEARCH §4). DryRun is intentionally indistinguishable from a real success at the metrics layer; the audit-log NDJSON (PLAN-1.5) is the differentiator.

## Dependencies

- None (Wave 1 plan, no upstream).
- File-disjoint with PLAN-1.2, 1.3, 1.4, 1.5 (this plan touches only `src/FrigateRelay.Plugins.BlueIris/**` + its `tests/` sibling).

## Tasks

### Task 1: Add `DryRun` to `BlueIrisOptions`

**Files:**
- `src/FrigateRelay.Plugins.BlueIris/BlueIrisOptions.cs` (modify)

**Action:** modify

**Description:**
Append a new `init`-only property to the existing `public sealed record BlueIrisOptions`:

```csharp
/// <summary>
/// When true, the BlueIris plugin emits a structured `would-execute` log entry
/// at Info level (EventId 203, "BlueIrisDryRun") and returns success without
/// calling the BlueIris HTTP trigger endpoint. Used during the parity-window
/// (Phase 12) to log would-be-actions without firing real triggers. Default false.
/// </summary>
public bool DryRun { get; init; }
```

Place it after `SnapshotUrlTemplate` to preserve property order continuity. No other property edits.

**Acceptance Criteria:**
- `grep -q 'public bool DryRun' src/FrigateRelay.Plugins.BlueIris/BlueIrisOptions.cs`
- `grep -q 'BlueIrisDryRun' src/FrigateRelay.Plugins.BlueIris/BlueIrisOptions.cs` (XML doc references the EventId name).
- `dotnet build src/FrigateRelay.Plugins.BlueIris/FrigateRelay.Plugins.BlueIris.csproj -c Release` succeeds with zero warnings.

### Task 2: Wire DryRun early-return into `BlueIrisActionPlugin.ExecuteAsync`

**Files:**
- `src/FrigateRelay.Plugins.BlueIris/BlueIrisActionPlugin.cs` (modify)

**Action:** modify

**Description:**

1. Add a new top-level static field next to the existing `LogTriggerSuccess` / `LogTriggerFailed` fields:

```csharp
private static readonly Action<ILogger, string, string, string, Exception?> LogDryRun =
    LoggerMessage.Define<string, string, string>(
        LogLevel.Information,
        new EventId(203, "BlueIrisDryRun"),
        "BlueIris DryRun would-execute for camera={Camera} label={Label} event_id={EventId}");
```

2. At the very top of `public async Task ExecuteAsync(EventContext ctx, SnapshotContext snapshot, CancellationToken ct)` — BEFORE any template resolution / `HttpClient` creation — add:

```csharp
if (_options.DryRun)
{
    LogDryRun(_logger, ctx.Camera, ctx.Label, ctx.EventId, null);
    return;
}
```

Where `_options` is the existing `BlueIrisOptions` field (already injected via `IOptions<BlueIrisOptions>` in the ctor — confirm via `grep _options src/FrigateRelay.Plugins.BlueIris/BlueIrisActionPlugin.cs`; if the field is named differently, adapt).

3. No changes to the rest of `ExecuteAsync`. The `using var client = ...` block and the actual HTTP call remain untouched on the non-DryRun path.

4. Make `ExecuteAsync` keep its current `async`/`await` shape — the early `return` is a `void`-equivalent in an async Task method (returns a completed task to the dispatcher, which increments `ActionsSucceeded` per RESEARCH §4).

**Acceptance Criteria:**
- `grep -q 'BlueIrisDryRun' src/FrigateRelay.Plugins.BlueIris/BlueIrisActionPlugin.cs`
- `grep -q 'LoggerMessage.Define' src/FrigateRelay.Plugins.BlueIris/BlueIrisActionPlugin.cs`
- `grep -q 'if (_options.DryRun)' src/FrigateRelay.Plugins.BlueIris/BlueIrisActionPlugin.cs`
- `dotnet build src/FrigateRelay.Plugins.BlueIris/FrigateRelay.Plugins.BlueIris.csproj -c Release` clean (warnings-as-errors, including CA1848).
- `git grep -n 'EventId(203' src/FrigateRelay.Plugins.BlueIris/` shows exactly one match (no accidental duplicate).

### Task 3: Unit test — DryRun=true skips HTTP call and emits one log entry

**Files:**
- `tests/FrigateRelay.Plugins.BlueIris.Tests/BlueIrisActionPluginTests.cs` (modify — append two `[TestMethod]`s; do NOT replace the file)

**Action:** modify (TDD: write test first, then run Task 2 changes)

**Description:**

Append to the existing `BlueIrisActionPluginTests` class. Use the project's existing `CapturingLogger<BlueIrisActionPlugin>` from `FrigateRelay.TestHelpers` (CLAUDE.md convention; do NOT redefine).

```csharp
[TestMethod]
public async Task ExecuteAsync_DryRunTrue_DoesNotCallHttpClientAndLogsWouldExecute()
{
    // Arrange: a delegating handler that fails the test if invoked.
    var handler = new InvocationCountingHandler();
    var httpClient = new HttpClient(handler);
    var httpFactory = Substitute.For<IHttpClientFactory>();
    httpFactory.CreateClient(Arg.Any<string>()).Returns(httpClient);

    var logger = new CapturingLogger<BlueIrisActionPlugin>();
    var options = Options.Create(new BlueIrisOptions
    {
        TriggerUrlTemplate = "http://example.invalid/trigger?camera={camera}",
        DryRun = true,
    });
    var plugin = new BlueIrisActionPlugin(logger, httpFactory, options);

    var ctx = new EventContext
    {
        EventId = "ev-1",
        Camera = "DriveWayHD",
        Label = "person",
        RawPayload = "{}",
        StartedAt = DateTimeOffset.UtcNow,
        SnapshotFetcher = _ => ValueTask.FromResult<byte[]?>(null),
    };

    // Act
    await plugin.ExecuteAsync(ctx, default, CancellationToken.None);

    // Assert
    handler.SendInvocations.Should().Be(0);
    logger.Entries.Should()
        .ContainSingle(e => e.EventId.Name == "BlueIrisDryRun")
        .Which.Message.Should().Contain("DriveWayHD").And.Contain("person").And.Contain("ev-1");
}

[TestMethod]
public async Task ExecuteAsync_DryRunFalse_CallsHttpClientAsBefore()
{
    // Arrange: a 200-returning handler. We just need to confirm that with DryRun=false
    // (the default for this options instance) the HTTP path is taken.
    var handler = new StubHandler(HttpStatusCode.OK);
    var httpClient = new HttpClient(handler);
    var httpFactory = Substitute.For<IHttpClientFactory>();
    httpFactory.CreateClient(Arg.Any<string>()).Returns(httpClient);

    var logger = new CapturingLogger<BlueIrisActionPlugin>();
    var options = Options.Create(new BlueIrisOptions
    {
        TriggerUrlTemplate = "http://example.invalid/trigger?camera={camera}",
        // DryRun omitted — default false.
    });
    var plugin = new BlueIrisActionPlugin(logger, httpFactory, options);

    var ctx = new EventContext
    {
        EventId = "ev-1",
        Camera = "DriveWayHD",
        Label = "person",
        RawPayload = "{}",
        StartedAt = DateTimeOffset.UtcNow,
        SnapshotFetcher = _ => ValueTask.FromResult<byte[]?>(null),
    };

    await plugin.ExecuteAsync(ctx, default, CancellationToken.None);

    handler.SendInvocations.Should().Be(1);
    logger.Entries.Should().NotContain(e => e.EventId.Name == "BlueIrisDryRun");
}
```

Helper handlers `InvocationCountingHandler` / `StubHandler` may already exist in this test project (RESEARCH §3 referenced existing tests for BlueIris with stub handlers); if not, add them at the bottom of the file as small private nested classes that override `SendAsync`. **Builder check:** before authoring new helpers, `grep -n 'class.*HttpMessageHandler' tests/FrigateRelay.Plugins.BlueIris.Tests/` to find any existing ones and reuse.

**Acceptance Criteria:**
- `grep -q 'ExecuteAsync_DryRunTrue_DoesNotCallHttpClient' tests/FrigateRelay.Plugins.BlueIris.Tests/BlueIrisActionPluginTests.cs`
- `grep -q 'ExecuteAsync_DryRunFalse_CallsHttpClientAsBefore' tests/FrigateRelay.Plugins.BlueIris.Tests/BlueIrisActionPluginTests.cs`
- `dotnet build tests/FrigateRelay.Plugins.BlueIris.Tests/FrigateRelay.Plugins.BlueIris.Tests.csproj -c Release` clean.
- `dotnet run --project tests/FrigateRelay.Plugins.BlueIris.Tests -c Release --no-build` exits 0 with all tests green.
- Test count for this csproj has increased by ≥2 vs. pre-plan baseline.

## Verification

```bash
# 1. Build the BlueIris plugin and its tests clean (warnings-as-errors)
dotnet build src/FrigateRelay.Plugins.BlueIris/FrigateRelay.Plugins.BlueIris.csproj -c Release
dotnet build tests/FrigateRelay.Plugins.BlueIris.Tests/FrigateRelay.Plugins.BlueIris.Tests.csproj -c Release

# 2. Tests green
dotnet run --project tests/FrigateRelay.Plugins.BlueIris.Tests -c Release --no-build

# 3. Whole-solution sanity (no regressions in other tests)
dotnet build FrigateRelay.sln -c Release

# 4. Confirm the DryRun knob is wired
grep -q 'public bool DryRun' src/FrigateRelay.Plugins.BlueIris/BlueIrisOptions.cs
grep -q 'BlueIrisDryRun' src/FrigateRelay.Plugins.BlueIris/BlueIrisActionPlugin.cs
grep -q 'if (_options.DryRun)' src/FrigateRelay.Plugins.BlueIris/BlueIrisActionPlugin.cs

# 5. Secret-scan stays clean
.github/scripts/secret-scan.sh
```
