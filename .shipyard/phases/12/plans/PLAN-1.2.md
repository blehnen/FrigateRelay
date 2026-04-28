---
phase: 12-parity-cutover
plan: 1.2
wave: 1
dependencies: []
must_haves:
  - PushoverOptions.DryRun bool init property (default false)
  - PushoverActionPlugin early-return on DryRun=true with structured `would-execute` log emission via the existing nested static `Log` class
  - Unit test asserts DryRun=true does NOT call the HttpClient and emits exactly one `PushoverDryRun` log entry
files_touched:
  - src/FrigateRelay.Plugins.Pushover/PushoverOptions.cs
  - src/FrigateRelay.Plugins.Pushover/PushoverActionPlugin.cs
  - tests/FrigateRelay.Plugins.Pushover.Tests/PushoverActionPluginTests.cs
tdd: true
risk: low
---

# Plan 1.2: Pushover DryRun flag (per-action, logging-only)

## Context

CONTEXT-12 D5 sibling to PLAN-1.1. Pushover gains the same DryRun semantics; implementation differs in two structural ways called out by RESEARCH §3:

- `PushoverOptions` is an `internal sealed class` (NOT a record) with `[Required]` on `AppToken`/`UserKey`. Adding `DryRun` follows the class shape: `public bool DryRun { get; init; }`. The class stays `internal`.
- Pushover uses a nested `private static class Log` for `LoggerMessage.Define` emitters (`_sendSucceeded`, `_sendFailed`, `_snapshotUnavailable`). The DryRun emitter is added INSIDE that nested `Log` class as a peer.

**Architect-discretion locked:**

- EventId for the Pushover DryRun emission: `EventId(4, "PushoverDryRun")` (RESEARCH §3 next-available after 1, 2, 3 used by the existing emitters).
- Early-return placement: at the TOP of `ExecuteAsync` BEFORE `var opts = _options.Value` resolution. This means a missing `AppToken` will NOT throw during DryRun even though it's `[Required]` — but **`Options` validation runs at startup (`StartupValidation.ValidateAll`)** so the operator cannot start the host with `DryRun: true` and an empty token; the startup gate catches it. This is the correct posture: DryRun is for runtime simulation, not validation bypass.
- The DryRun log captures `(camera, label, event_id)` only; the actual Pushover message body is NOT logged (avoids any tokens + reduces audit-log noise).
- Counters: same as PLAN-1.1 — `ActionsSucceeded` ticks. NDJSON (PLAN-1.5) is the audit substrate.

## Dependencies

- None (Wave 1 plan, no upstream).
- File-disjoint with PLAN-1.1, 1.3, 1.4, 1.5 (this plan touches only `src/FrigateRelay.Plugins.Pushover/**` + its `tests/` sibling).

## Tasks

### Task 1: Add `DryRun` to `PushoverOptions`

**Files:**
- `src/FrigateRelay.Plugins.Pushover/PushoverOptions.cs` (modify)

**Action:** modify

**Description:**
Append a new `init`-only property to `PushoverOptions`. The class stays `internal sealed` and stays a class (NOT converted to record).

```csharp
/// <summary>
/// When true, the Pushover plugin emits a structured `would-execute` log entry
/// at Info level (EventId 4, "PushoverDryRun") and returns success without
/// calling the Pushover API. Used during the parity-window (Phase 12) to log
/// would-be-actions without firing real notifications. Default false.
/// </summary>
public bool DryRun { get; init; }
```

Place it after `QueueCapacity` to preserve property-order continuity. Do NOT add `[Required]` (default false is valid).

**Acceptance Criteria:**
- `grep -q 'public bool DryRun' src/FrigateRelay.Plugins.Pushover/PushoverOptions.cs`
- `grep -q 'PushoverDryRun' src/FrigateRelay.Plugins.Pushover/PushoverOptions.cs` (XML doc references the EventId name).
- `grep -c 'internal sealed class PushoverOptions' src/FrigateRelay.Plugins.Pushover/PushoverOptions.cs` is 1 (class shape preserved — no accidental record conversion).
- `dotnet build src/FrigateRelay.Plugins.Pushover/FrigateRelay.Plugins.Pushover.csproj -c Release` clean.

### Task 2: Wire DryRun early-return into `PushoverActionPlugin.ExecuteAsync`

**Files:**
- `src/FrigateRelay.Plugins.Pushover/PushoverActionPlugin.cs` (modify)

**Action:** modify

**Description:**

1. Inside the existing nested `private static class Log` (locate via `grep -n 'private static class Log' src/FrigateRelay.Plugins.Pushover/PushoverActionPlugin.cs`), add a new emitter as a peer to `_sendSucceeded`:

```csharp
private static readonly Action<ILogger, string, string, string, Exception?> _wouldExecute =
    LoggerMessage.Define<string, string, string>(
        LogLevel.Information,
        new EventId(4, "PushoverDryRun"),
        "Pushover DryRun would-execute for camera={Camera} label={Label} event_id={EventId}");

public static void WouldExecute(ILogger logger, string camera, string label, string eventId)
    => _wouldExecute(logger, camera, label, eventId, null);
```

2. At the very top of `public async Task ExecuteAsync(EventContext ctx, SnapshotContext snapshot, CancellationToken ct)` — BEFORE `var opts = _options.Value` and BEFORE any `snapshot.ResolveAsync` call — add:

```csharp
if (_options.Value.DryRun)
{
    Log.WouldExecute(_logger, ctx.Camera, ctx.Label, ctx.EventId);
    return;
}
```

The plugin already holds `_options` as `IOptions<PushoverOptions>` (RESEARCH §3 reference) — verify with `grep -n 'IOptions<PushoverOptions>' src/FrigateRelay.Plugins.Pushover/PushoverActionPlugin.cs`. If the field is named differently, adapt.

3. No other changes. The non-DryRun path is unchanged.

**Acceptance Criteria:**
- `grep -q 'PushoverDryRun' src/FrigateRelay.Plugins.Pushover/PushoverActionPlugin.cs`
- `grep -q 'WouldExecute' src/FrigateRelay.Plugins.Pushover/PushoverActionPlugin.cs`
- `grep -q 'if (_options.Value.DryRun)' src/FrigateRelay.Plugins.Pushover/PushoverActionPlugin.cs`
- `dotnet build src/FrigateRelay.Plugins.Pushover/FrigateRelay.Plugins.Pushover.csproj -c Release` clean (CA1848 enforced).
- `git grep -n 'EventId(4' src/FrigateRelay.Plugins.Pushover/` shows exactly one match.

### Task 3: Unit test — DryRun=true skips HTTP call and emits one log entry

**Files:**
- `tests/FrigateRelay.Plugins.Pushover.Tests/PushoverActionPluginTests.cs` (modify — append two `[TestMethod]`s; do NOT replace)

**Action:** modify (TDD: write test first, then run Task 2 changes)

**Description:**

Append two tests mirroring PLAN-1.1's BlueIris pair, adapted for Pushover. Use `CapturingLogger<PushoverActionPlugin>` from `FrigateRelay.TestHelpers`. Reuse any existing `InvocationCountingHandler` / `StubHandler` in this test project; if absent, add as private nested classes.

```csharp
[TestMethod]
public async Task ExecuteAsync_DryRunTrue_DoesNotCallHttpClientAndLogsWouldExecute()
{
    var handler = new InvocationCountingHandler();
    var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.pushover.invalid") };
    var httpFactory = Substitute.For<IHttpClientFactory>();
    httpFactory.CreateClient(Arg.Any<string>()).Returns(httpClient);

    var logger = new CapturingLogger<PushoverActionPlugin>();
    var options = Options.Create(new PushoverOptions
    {
        AppToken = "stub-token-not-real",
        UserKey  = "stub-user-not-real",
        DryRun   = true,
    });
    var plugin = new PushoverActionPlugin(logger, httpFactory, options);

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

    handler.SendInvocations.Should().Be(0);
    logger.Entries.Should()
        .ContainSingle(e => e.EventId.Name == "PushoverDryRun")
        .Which.Message.Should().Contain("DriveWayHD").And.Contain("person").And.Contain("ev-1");
}

[TestMethod]
public async Task ExecuteAsync_DryRunFalse_CallsHttpClientAsBefore()
{
    var handler = new StubHandler(HttpStatusCode.OK, """{"status":1}""");
    var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.pushover.invalid") };
    var httpFactory = Substitute.For<IHttpClientFactory>();
    httpFactory.CreateClient(Arg.Any<string>()).Returns(httpClient);

    var logger = new CapturingLogger<PushoverActionPlugin>();
    var options = Options.Create(new PushoverOptions
    {
        AppToken = "stub-token-not-real",
        UserKey  = "stub-user-not-real",
        // DryRun omitted — default false.
    });
    var plugin = new PushoverActionPlugin(logger, httpFactory, options);

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
    logger.Entries.Should().NotContain(e => e.EventId.Name == "PushoverDryRun");
}
```

**Stub-token convention:** the strings `"stub-token-not-real"` / `"stub-user-not-real"` are deliberately NOT secret-scan-shaped (length ≠ 30 chars; no `AppToken=` or `UserKey=` literal in test body — `secret-scan.sh` matches `AppToken=[A-Za-z0-9]{20,}` shape, this is a property setter not a key=value literal). Builder MUST run `.github/scripts/secret-scan.sh` after edits to confirm.

**Acceptance Criteria:**
- `grep -q 'ExecuteAsync_DryRunTrue_DoesNotCallHttpClient' tests/FrigateRelay.Plugins.Pushover.Tests/PushoverActionPluginTests.cs`
- `grep -q 'ExecuteAsync_DryRunFalse_CallsHttpClientAsBefore' tests/FrigateRelay.Plugins.Pushover.Tests/PushoverActionPluginTests.cs`
- `dotnet build tests/FrigateRelay.Plugins.Pushover.Tests/FrigateRelay.Plugins.Pushover.Tests.csproj -c Release` clean.
- `dotnet run --project tests/FrigateRelay.Plugins.Pushover.Tests -c Release --no-build` exits 0.
- `.github/scripts/secret-scan.sh` exits 0.

## Verification

```bash
dotnet build src/FrigateRelay.Plugins.Pushover/FrigateRelay.Plugins.Pushover.csproj -c Release
dotnet build tests/FrigateRelay.Plugins.Pushover.Tests/FrigateRelay.Plugins.Pushover.Tests.csproj -c Release
dotnet run --project tests/FrigateRelay.Plugins.Pushover.Tests -c Release --no-build
dotnet build FrigateRelay.sln -c Release
grep -q 'public bool DryRun' src/FrigateRelay.Plugins.Pushover/PushoverOptions.cs
grep -q 'PushoverDryRun' src/FrigateRelay.Plugins.Pushover/PushoverActionPlugin.cs
.github/scripts/secret-scan.sh
```
