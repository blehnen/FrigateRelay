---
phase: 14-v1.2-inference-engines-parallel-validation
plan: 2.3
wave: 2
dependencies: [2.1, 2.2]
must_haves:
  - In-process gRPC test host pattern proven with one full exemplar test before fanning out
  - At least 5 gRPC-path tests covering happy / reject-low-confidence / timeout-FailClosed / unavailable-FailClosed / cancellation
  - gRPC test infrastructure (Grpc.AspNetCore.Server + Microsoft.AspNetCore.TestHost) added to test csproj only
  - CHANGELOG bullet under [Unreleased] / ### Added describing dual-transport DOODS2
  - Cumulative test count rises from 257 (post-PLAN-2.2) to ≥ 262
files_touched:
  - tests/FrigateRelay.Plugins.Doods2.Tests/FrigateRelay.Plugins.Doods2.Tests.csproj
  - tests/FrigateRelay.Plugins.Doods2.Tests/Fixtures/InProcessDoods2GrpcServer.cs
  - tests/FrigateRelay.Plugins.Doods2.Tests/Doods2GrpcValidatorTests.cs
  - CHANGELOG.md
tdd: true
risk: medium
---

# Plan 2.3: DOODS2 gRPC-transport tests + CHANGELOG (PR #14 — DOODS2 validator)

## Context

PLAN-2.2 covers the HTTP transport; this plan covers the gRPC transport. Per RESEARCH §4.3, the in-process gRPC test pattern using `Grpc.AspNetCore.Server` + `Microsoft.AspNetCore.TestHost` is the canonical .NET 10 approach but **has not been verified against this codebase's MSTest v3 + class-fixture pattern** — RESEARCH explicitly flags this as Concern #1. Therefore Task 1 of this plan **sketches one complete in-process gRPC test as a proof-of-shape before fanning out** to the full test set.

Test count target: 257 (post-PLAN-2.2) → **262 (+5 new)** for the gRPC path.

The CHANGELOG bullet covers the entire DOODS2 feature (HTTP + gRPC). Risk: **medium** — gRPC test harness ergonomics in .NET 10 are unverified for this codebase. If the in-process server pattern proves hostile, the builder may fall back to a real `Doods2Server` test fixture using a process-spawned listener; document the choice in the test file's class-level remark.

## Dependencies

- **PLAN-2.1** — `Doods2Validator` gRPC path implementation must compile.
- **PLAN-2.2** — test csproj exists (this plan extends it with `Grpc.AspNetCore.Server` packages).

## Tasks

### Task 1: Add gRPC server packages + write the exemplar `_DetectionAboveThreshold_ReturnsAllow` test end-to-end

**Files:**
- `tests/FrigateRelay.Plugins.Doods2.Tests/FrigateRelay.Plugins.Doods2.Tests.csproj` (modify — add packages)
- `tests/FrigateRelay.Plugins.Doods2.Tests/Fixtures/InProcessDoods2GrpcServer.cs` (new)
- `tests/FrigateRelay.Plugins.Doods2.Tests/Doods2GrpcValidatorTests.cs` (new — exemplar test only)

**Action:** create + modify

**Description:**

**Extend the test csproj** (PLAN-2.2 Task 1) to add the gRPC server packages:
```xml
<PackageReference Include="Grpc.AspNetCore.Server" Version="2.66.0" />
<PackageReference Include="Microsoft.AspNetCore.TestHost" Version="10.0.7" />
<Protobuf Include="..\..\src\FrigateRelay.Plugins.Doods2\Protos\odrpc.proto" GrpcServices="Server" />
```
Verify the actual current versions at PR-2 execution time; the values above are RESEARCH-time guesses.

The test csproj also needs `Grpc.Tools` referenced (build-time only, `PrivateAssets="all"`) so the server-side codegen runs against the same vendored proto. The `<Protobuf>` item's `GrpcServices="Server"` is the key difference from the plugin csproj's `GrpcServices="Client"`. Both client and server stubs are generated; the test project uses both.

**Build the in-process gRPC server fixture** at `tests/FrigateRelay.Plugins.Doods2.Tests/Fixtures/InProcessDoods2GrpcServer.cs`:

```csharp
internal sealed class InProcessDoods2GrpcServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    public string BaseAddress { get; }
    public List<DetectRequest> ReceivedRequests { get; } = new();

    private InProcessDoods2GrpcServer(WebApplication app, string baseAddress)
    { _app = app; BaseAddress = baseAddress; }

    public static async Task<InProcessDoods2GrpcServer> StartAsync(Func<DetectRequest, DetectResponse>? respond = null, TimeSpan? delay = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();   // Microsoft.AspNetCore.TestHost in-process listener
        builder.Services.AddGrpc();
        // Register the test detector implementation that records requests + replies via 'respond'.
        // ...
        var app = builder.Build();
        app.MapGrpcService<TestDetectorImpl>();
        await app.StartAsync();
        var addr = app.Services.GetRequiredService<TestServer>().BaseAddress.ToString();
        return new InProcessDoods2GrpcServer(app, addr);
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}
```

`TestDetectorImpl : Detector.DetectorBase` (the auto-generated server-base abstract class from the `GrpcServices="Server"` codegen) overrides `Detect(DetectRequest req, ServerCallContext ctx)`, records the request into `ReceivedRequests`, optionally awaits the configured `delay` (used for timeout tests), and returns the supplied `respond(req)` result.

The validator-under-test connects to this in-process server via:
```csharp
var channel = GrpcChannel.ForAddress(server.BaseAddress, new GrpcChannelOptions
{
    HttpHandler = server._app.Services.GetRequiredService<TestServer>().CreateHandler(),
});
var grpcClient = new Detector.DetectorClient(channel);
```
The `TestServer.CreateHandler()` produces an `HttpMessageHandler` that routes requests to the in-process server without going through the kernel network stack — this is the key .NET 10 idiom (RESEARCH §4.3).

**Write ONE exemplar test** in `Doods2GrpcValidatorTests.cs`:
```csharp
[TestMethod]
public async Task ValidateAsync_Grpc_DetectionAboveThresholdAfterNormalization_ReturnsAllow()
{
    await using var server = await InProcessDoods2GrpcServer.StartAsync(respond: req =>
    {
        var resp = new DetectResponse();
        resp.Detections.Add(new Detection { Label = "person", Confidence = 80.0f, /* ... bbox ... */ });
        return resp;
    });

    var opts = MakeOptions(transport: Doods2Transport.Grpc, baseUrl: server.BaseAddress, minConf: 0.5);
    var validator = MakeValidator(opts, server);   // helper builds Doods2Validator with grpcClient pointing at the in-process server

    var ctx = MakeContext();
    var snap = MakeSnapshotContext(SnapshotResultFor("fakejpegbytes"));

    var verdict = await validator.ValidateAsync(ctx, snap, CancellationToken.None);

    Assert.IsTrue(verdict.Passed);
    Assert.AreEqual(1, server.ReceivedRequests.Count);
    Assert.AreEqual("default", server.ReceivedRequests[0].DetectorName);
}
```

**Time-box:** if the in-process pattern proves problematic against MSTest v3 + this codebase's Usings shape (RESEARCH Concern #1), the builder MAY fall back to:
- A real `Grpc.AspNetCore.Server` host on `Kestrel` bound to `localhost:<random-port>` (use `IServerAddressesFeature` after `StartAsync` to discover the port). This is real-network in-process — no `TestHost` magic. Slightly slower per test but mechanically simpler.

Document the fallback decision in the file's class-level XML doc-comment if invoked.

**Acceptance Criteria:**
- The exemplar test passes: `dotnet run --project tests/FrigateRelay.Plugins.Doods2.Tests -c Release --no-build -- --filter "Grpc_DetectionAboveThreshold"` exits 0.
- `dotnet build FrigateRelay.sln -c Release` zero warnings.
- The in-process server records the gRPC `DetectRequest` (proves end-to-end wiring: validator → channel → server → response → validator).
- The 0-100 → 0-1 normalization works for the gRPC path (server returns `Confidence = 80.0f`; validator normalizes to 0.8; passes 0.5 threshold).
- gRPC dep containment STILL holds: `dotnet list src/FrigateRelay.Host/FrigateRelay.Host.csproj package --include-transitive | grep -E 'Grpc\.'` returns empty (host stays gRPC-free even though tests use server-side gRPC).

---

### Task 2: Fan out to 4 more gRPC-path tests + CHANGELOG bullet

**Files:**
- `tests/FrigateRelay.Plugins.Doods2.Tests/Doods2GrpcValidatorTests.cs` (extend)
- `CHANGELOG.md` (modify)

**Action:** create + modify

**Description:**

With the exemplar test from Task 1 working, add 4 more gRPC-path tests using the same `InProcessDoods2GrpcServer` fixture:

2. `ValidateAsync_Grpc_DetectionBelowThresholdAfterNormalization_ReturnsReject` — server returns `Confidence = 30.0f`; opts `MinConfidence=0.5`. Expect reject. Counterpart to PLAN-2.2 test #2.

3. `ValidateAsync_Grpc_DeadlineExceeded_FailClosed_ReturnsReject` — server's `respond` callback awaits `delay: TimeSpan.FromSeconds(2)`; client's `opts.Timeout = TimeSpan.FromMilliseconds(200)`. Expect `Verdict.Fail("validator_timeout")`. The validator's gRPC catch-block for `RpcException(StatusCode.DeadlineExceeded)` (PLAN-2.1 Task 2) is exercised here. Verify `CapturingLogger<Doods2Validator>` shows EventId 7201 (same as HTTP timeout — semantically equivalent).

4. `ValidateAsync_Grpc_ServerThrows_FailClosed_ReturnsReject` — server's `respond` throws `new RpcException(new Status(StatusCode.Internal, "boom"))`. Expect `Verdict.Fail` whose reason starts with `"validator_unavailable: "`. Verify EventId 7202.

5. `ValidateAsync_Grpc_CancellationRequested_PropagatesOperationCanceled` — start the call with a `CancellationToken` from a `CancellationTokenSource` whose `Cancel()` is invoked before the call. Expect `OperationCanceledException` (or its subclass) is thrown — NOT swallowed into a verdict. The validator's catch-ordering (`OperationCanceledException when ct.IsCancellationRequested` first) must rethrow per PLAN-2.1 Task 2 + RESEARCH §1.4.

This single test case effectively also covers the HTTP-cancellation behavior (the catch-block is shared per PLAN-2.1's catch-ordering), so PLAN-2.2 deliberately deferred its own cancellation test here.

**CHANGELOG bullet** under `[Unreleased]` / `### Added`:
```markdown
- **DOODS2 validator (#14).** New `FrigateRelay.Plugins.Doods2` validator plugin with
  HTTP and gRPC transports, operator-selectable per validator instance via
  `Validators:<key>:Transport: "Http" | "Grpc"`. HTTP path uses `POST /detect` with
  base64-encoded JPEG; gRPC path uses the `Detector.Detect` RPC from the vendored
  upstream `odrpc.proto`. Per-instance `BaseUrl`, `DetectorName`, `MinConfidence`
  (0-1 scale; the validator normalizes the upstream's 0-100 confidence internally),
  `AllowedLabels`, `OnError` (FailClosed/FailOpen), and `Timeout` config.
  gRPC dependencies (`Grpc.Net.Client`, `Google.Protobuf`, `Grpc.Tools`) are
  contained to this plugin only — `FrigateRelay.Host` and
  `FrigateRelay.Abstractions` remain gRPC-free.
```

Do NOT use real IPs / hostnames. Reference issue `#14`.

**Acceptance Criteria:**
- All 5 gRPC tests pass: `dotnet run --project tests/FrigateRelay.Plugins.Doods2.Tests -c Release --no-build` exits 0 and reports `total: 12` (7 HTTP from PLAN-2.2 + 5 gRPC from this plan).
- Cumulative test count ≥ 262 (257 post-PLAN-2.2 + 5 new): `bash .github/scripts/run-tests.sh --skip-integration` confirms.
- The cancellation test (#5) verifies `OperationCanceledException` is rethrown, not swallowed (proves PLAN-2.1's catch-ordering invariant).
- gRPC dep containment STILL holds (run the dep-list grep).
- CHANGELOG entry references `#14` and explicitly mentions the gRPC-dep-containment invariant.

## Verification

```bash
# Build clean
dotnet build FrigateRelay.sln -c Release

# Tests pass — DOODS2 (full HTTP + gRPC) + everything else
dotnet run --project tests/FrigateRelay.Plugins.Doods2.Tests -c Release --no-build
bash .github/scripts/run-tests.sh --skip-integration

# Test count gate — at least 262 total (250 post-Wave 1 + 7 HTTP + 5 gRPC)
TOTAL=$(bash .github/scripts/run-tests.sh --skip-integration 2>&1 | grep -E '^total:' | awk '{ sum += $2 } END { print sum }')
[ "$TOTAL" -ge 262 ] || { echo "test count regression: $TOTAL < 262"; exit 1; }

# gRPC dep containment — load-bearing
dotnet list src/FrigateRelay.Abstractions/FrigateRelay.Abstractions.csproj package --include-transitive | grep -E 'Grpc\.' && exit 1 || true
dotnet list src/FrigateRelay.Host/FrigateRelay.Host.csproj package --include-transitive | grep -E 'Grpc\.' && exit 1 || true

# Source-level gRPC containment unchanged
git grep -nE 'Grpc\.' src/FrigateRelay.Host src/FrigateRelay.Abstractions   # must be empty

# Architectural invariants
git grep -nE 'App\.Metrics|OpenTracing|Jaeger\.' src/                # must be empty
git grep -nE '\.(Result|Wait)\(' src/                                 # must be empty
git grep -n 'ServicePointManager' src/                                # must be empty

# Tests use the shared CapturingLogger
git grep -n 'Substitute.For<ILogger' tests/FrigateRelay.Plugins.Doods2.Tests/    # must be empty

# Secret scan still clean
bash .github/scripts/secret-scan.sh

# CHANGELOG references the new feature
grep -n '#14' CHANGELOG.md
grep -n 'DOODS2' CHANGELOG.md
```
