---
plan_id: 7.1.1
title: IValidationPlugin signature + SnapshotContext.PreResolved + dispatcher validator chain
wave: 1
plan: 1
dependencies: []
files_touched:
  - src/FrigateRelay.Abstractions/IValidationPlugin.cs
  - src/FrigateRelay.Abstractions/SnapshotContext.cs
  - src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs
  - tests/FrigateRelay.Abstractions.Tests/SnapshotContextTests.cs
  - tests/FrigateRelay.Host.Tests/Dispatch/ChannelActionDispatcherTests.cs
tdd: true
estimated_tasks: 3
---

# Plan 1.1: IValidationPlugin signature + SnapshotContext.PreResolved + dispatcher validator chain

## Context
Phase 7 wires `IValidationPlugin` into the dispatcher with per-action chain semantics (PROJECT.md V3, CONTEXT-7 D1, D6, D11). Validators must share **one** resolved snapshot with the action they gate (RESEARCH §5: `SnapshotContext.ResolveAsync` does NOT cache today — direct delegation to the resolver). This plan:

1. Changes `IValidationPlugin.ValidateAsync` to take a `SnapshotContext` (CONTEXT-7 D1, mirrors Phase 6 ARCH-D2 for `IActionPlugin`).
2. Adds a `SnapshotContext.PreResolved` constructor (RESEARCH §5 Option A — chosen by architect lock-in).
3. Wires the validator chain inside `ChannelActionDispatcher.ConsumeAsync` ABOVE the existing Polly `ResiliencePipeline.ExecuteAsync` block (CONTEXT-7 D4 + RESEARCH §7-4: validators bypass action retry policy).

Per RESEARCH §4: zero existing test stubs of `IValidationPlugin` exist — only the interface file changes. All existing dispatcher tests use `Array.Empty<IValidationPlugin>()` and remain green.

## Dependencies
None — Wave 1 parallel with PLAN-1.2.

## Tasks

### Task 1: Add `SnapshotContext.PreResolved` constructor (TDD)
**Files:** `src/FrigateRelay.Abstractions/SnapshotContext.cs`, `tests/FrigateRelay.Abstractions.Tests/SnapshotContextTests.cs`
**Action:** modify (struct) + test
**Description:**

**Test first.** Add 2 tests to `SnapshotContextTests.cs` (create file if absent — verify under `tests/FrigateRelay.Abstractions.Tests/`):

```csharp
[TestMethod]
public async Task ResolveAsync_PreResolved_ReturnsCachedWithoutResolver()
{
    var pre = new SnapshotResult(/* …minimal valid — see existing SnapshotResult ctor */);
    var ctx = new SnapshotContext(pre);
    var got = await ctx.ResolveAsync(EventContextFixture.Sample, CancellationToken.None);
    got.Should().BeSameAs(pre);
}

[TestMethod]
public async Task ResolveAsync_PreResolvedNull_ReturnsNullWithoutResolver()
{
    var ctx = new SnapshotContext(preResolved: null);
    var got = await ctx.ResolveAsync(EventContextFixture.Sample, CancellationToken.None);
    got.Should().BeNull();
}
```

Then modify `SnapshotContext` per RESEARCH §5 verbatim. Add field `_preResolved` (SnapshotResult?), `_hasPreResolved` (bool), and the new constructor `SnapshotContext(SnapshotResult? preResolved)` with XML doc explaining "used by the dispatcher to share one resolved snapshot across the validator chain and the action; bypasses the resolver." Update `ResolveAsync`:

```csharp
public ValueTask<SnapshotResult?> ResolveAsync(EventContext context, CancellationToken ct)
{
    if (_hasPreResolved) return ValueTask.FromResult(_preResolved);
    if (_resolver is null) return ValueTask.FromResult<SnapshotResult?>(null);
    return _resolver.ResolveAsync(context, PerActionProviderName, SubscriptionDefaultProviderName, ct);
}
```

Mark the new ctor with `[SetsRequiredMembers]` if `SnapshotContext` has any required properties (per CLAUDE.md Conventions — verify; currently a readonly struct, likely no `required init`).

**Acceptance criteria:**
- 2 new tests pass via `dotnet run --project tests/FrigateRelay.Abstractions.Tests -c Release`.
- `default(SnapshotContext)` still returns null from `ResolveAsync` (existing tests unchanged).
- Existing `SnapshotContext` construction call sites (1 in dispatcher, plus tests) still compile — additive change.

### Task 2: Change `IValidationPlugin.ValidateAsync` signature
**Files:** `src/FrigateRelay.Abstractions/IValidationPlugin.cs`
**Action:** modify
**Description:**

Update the interface per CONTEXT-7 D1:

```csharp
public interface IValidationPlugin
{
    string Name { get; }
    Task<Verdict> ValidateAsync(
        EventContext ctx,
        SnapshotContext snapshot,
        CancellationToken ct);
}
```

Update XML docstrings on `ValidateAsync` to call out:
- `snapshot.ResolveAsync` returns the dispatcher's pre-resolved snapshot when validators are present (no double-fetch).
- A failing `Verdict.Passed == false` short-circuits THIS action only; other actions in the same event proceed (V3).
- `OperationCanceledException` (cooperative cancel via `ct`) MUST propagate; do not catch and convert to a `Verdict.Fail`.

**Acceptance criteria:**
- `dotnet build FrigateRelay.sln -c Release` clean.
- `git grep -n "ValidateAsync" src/FrigateRelay.Abstractions/IValidationPlugin.cs` shows the new 3-parameter signature.
- Per RESEARCH §4: zero existing implementations to migrate. Confirm via `git grep -nE 'class .*IValidationPlugin|: IValidationPlugin' src/ tests/` — should be empty after this task.

### Task 3: Wire validator chain into `ChannelActionDispatcher.ConsumeAsync` (TDD additive)
**Files:** `src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs`, `tests/FrigateRelay.Host.Tests/Dispatch/ChannelActionDispatcherTests.cs`
**Action:** modify + test
**Description:**

**Tests first** (additive — existing 8 dispatcher tests using `Array.Empty<IValidationPlugin>()` stay green per RESEARCH §4):

```csharp
[TestMethod]
public async Task ConsumeAsync_ValidatorChain_ShortCircuitsOnFirstFailingVerdict()
{
    // Arrange: 2 validators — first passes, second fails. Action plugin is a spy.
    var v1 = Substitute.For<IValidationPlugin>();
    v1.Name.Returns("v1");
    v1.ValidateAsync(Arg.Any<EventContext>(), Arg.Any<SnapshotContext>(), Arg.Any<CancellationToken>())
      .Returns(Task.FromResult(Verdict.Pass()));

    var v2 = Substitute.For<IValidationPlugin>();
    v2.Name.Returns("v2");
    v2.ValidateAsync(Arg.Any<EventContext>(), Arg.Any<SnapshotContext>(), Arg.Any<CancellationToken>())
      .Returns(Task.FromResult(Verdict.Fail("low-confidence")));

    var v3 = Substitute.For<IValidationPlugin>(); // must NOT be called
    v3.Name.Returns("v3");

    var actionSpy = new SpyActionPlugin();
    // …enqueue with [v1, v2, v3] validators and run consumer.

    actionSpy.ExecuteCalls.Should().Be(0);
    await v3.DidNotReceive().ValidateAsync(default!, default, default);
    capturingLogger.Entries.Should().Contain(e => e.Message.Contains("validator_rejected") && e.Message.Contains("v2"));
}

[TestMethod]
public async Task ConsumeAsync_SnapshotIsSharedBetweenValidatorAndAction()
{
    // Arrange: ISnapshotResolver mock returns ONE SnapshotResult; counts ResolveAsync calls.
    // Validator and action both call snapshot.ResolveAsync(...).
    // Assert: resolver called exactly once; both invocations got the same SnapshotResult instance.
    snapshotResolverMock.Received(1).ResolveAsync(Arg.Any<EventContext>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
}
```

Use the shared `CapturingLogger<ChannelActionDispatcher>` from `tests/FrigateRelay.TestHelpers/` (CLAUDE.md Conventions — do NOT redefine).

Then modify `ChannelActionDispatcher.ConsumeAsync` per RESEARCH §5 dispatcher snippet:

```csharp
// Build resolver-backed SnapshotContext from per-action / per-subscription tiers.
// NOTE: ChannelActionDispatcher's field is named `_snapshotResolver` (verified
// against src/FrigateRelay.Host/Dispatch/ChannelActionDispatcher.cs:43).
// SnapshotContext's own backing field is `_resolver` — different file, do not confuse.
var initial = _snapshotResolver is null
    ? default
    : new SnapshotContext(_snapshotResolver, item.PerActionSnapshotProvider, item.SubscriptionSnapshotProvider);

// Pre-resolve ONCE if any validator runs, so validator + action share the snapshot.
SnapshotContext shared;
if (item.Validators.Count > 0)
{
    var preResolved = await initial.ResolveAsync(item.Context, ct).ConfigureAwait(false);
    shared = new SnapshotContext(preResolved);
}
else
{
    shared = initial; // action-only path: lazy resolve, no double-fetch risk
}

// Validator chain — runs ABOVE Polly retry. CONTEXT-7 D4: validators bypass action's retry.
foreach (var v in item.Validators)
{
    var verdict = await v.ValidateAsync(item.Context, shared, ct).ConfigureAwait(false);
    if (!verdict.Passed)
    {
        Log.ValidatorRejected(_logger, item.Context.EventId, item.Plugin.Name, v.Name, verdict.Reason);
        return; // short-circuit THIS action only (V3); other actions proceed in their own consumer iterations.
    }
}

// Action — wrapped in existing Polly ResiliencePipeline. Uses `shared` (cached snapshot if validators ran).
await _resiliencePipeline.ExecuteAsync(async token =>
{
    await item.Plugin.ExecuteAsync(item.Context, shared, token).ConfigureAwait(false);
}, ct).ConfigureAwait(false);
```

Add a `LoggerMessage.Define` source-gen entry `Log.ValidatorRejected` with structured fields per CONTEXT-7 D7: `event_id`, `action`, `validator`, `reason`. (Camera/label can be omitted if not on `DispatchItem` — verify via Read; if absent, log `event_id`/`action`/`validator`/`reason` only and document the gap.)

The exact fields-on-`DispatchItem` (`PerActionSnapshotProvider`, `SubscriptionSnapshotProvider`, `Validators`) already exist per RESEARCH §4 (line 28 — `DispatchItem.cs` has `IReadOnlyList<IValidationPlugin>`). Verify by reading `src/FrigateRelay.Host/Dispatch/DispatchItem.cs` before editing.

**Acceptance criteria:**
- 2 new dispatcher tests pass.
- All existing dispatcher tests pass unchanged.
- `git grep -n "ValidatorRejected\|validator_rejected" src/FrigateRelay.Host/Dispatch/` shows the new structured logger.
- `git grep -nE '\.(Result|Wait)\(' src/` empty.
- Build clean.

## Verification

```bash
dotnet build FrigateRelay.sln -c Release
dotnet run --project tests/FrigateRelay.Abstractions.Tests -c Release
dotnet run --project tests/FrigateRelay.Host.Tests -c Release
git grep -n "PreResolved" src/FrigateRelay.Abstractions/SnapshotContext.cs
git grep -n "SnapshotContext snapshot" src/FrigateRelay.Abstractions/IValidationPlugin.cs
git grep -nE '\.(Result|Wait)\(' src/                     # must be empty
```

## Notes for builder
- **Phase 6 ARCH-D2 precedent:** `IActionPlugin.ExecuteAsync` already takes `SnapshotContext` — the validator interface is the symmetric mirror.
- **RESEARCH §5 (caching guarantee):** read this section before editing `SnapshotContext.cs`. The `_hasPreResolved` flag is required so `default(SnapshotContext)` (which has `_resolver == null`) does NOT accidentally return a cached null.
- **CapturingLogger:** use the shared `tests/FrigateRelay.TestHelpers/CapturingLogger<T>` (CLAUDE.md Conventions — extracted in Phase 6, ID-11). Do NOT add a per-assembly copy.
- **Catch order in any error handler MUST be:** `OperationCanceledException when ct.IsCancellationRequested` → `TaskCanceledException` → `HttpRequestException`. (RESEARCH §6 — applies to PLAN-2.1 mostly, but if you add error handling here keep the order consistent.)
- **No `_logger.Error(ex.Message, ex)` anti-pattern.** Use `LoggerMessage.Define` (CONTEXT-7 D7).
