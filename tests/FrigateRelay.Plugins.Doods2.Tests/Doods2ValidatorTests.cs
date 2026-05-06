using FrigateRelay.Abstractions;
using FrigateRelay.Plugins.Doods2;
using Microsoft.Extensions.Logging;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace FrigateRelay.Plugins.Doods2.Tests;

[TestClass]
public sealed class Doods2ValidatorTests
{
    // -------------------------------------------------------------------------
    // Test 1: detection above threshold after 0-100 → 0-1 normalization → Pass
    // -------------------------------------------------------------------------
    /// <summary>
    /// Load-bearing assertion: DOODS2 returns confidence 80.0 (0-100 scale).
    /// MinConfidence=0.5 (0-1 scale). Validator normalizes 80.0/100=0.80 ≥ 0.5 → Allow.
    /// Proves the RESEARCH §7.2 normalization is in place (PLAN-2.2 §Task 2 test #1).
    /// </summary>
    [TestMethod]
    public async Task ValidateAsync_DetectionAboveThresholdAfterNormalization_ReturnsAllow()
    {
        using var stub = WireMockServer.Start();
        StubDetectEndpoint(stub, ("person", 80.0));

        var validator = MakeValidator(stub.Url!, minConf: 0.5, labels: ["person"]);
        var verdict = await validator.ValidateAsync(MakeContext(), MakeSnapshotContext(), CancellationToken.None);

        verdict.Passed.Should().BeTrue();
        verdict.Score.Should().BeApproximately(0.80, 0.001, "80.0 raw / 100.0 = 0.80 normalized");

        // Lock in the request-body scale invariant: MinConfidence=0.5 (0-1 operator scale) must
        // be sent to DOODS2 as 50.0 in the `detect` dict (0-100 server scale). A regression that
        // sends 0.5 unscaled would cause DOODS2 to filter at 0.5% confidence — masked by the
        // post-filter today, but operator intent would be silently mis-encoded.
        stub.LogEntries.Should().HaveCount(1);
        var body = stub.LogEntries[0].RequestMessage?.Body;
        body.Should().NotBeNull();
        body!.Should().Contain("\"*\":50");
    }

    // -------------------------------------------------------------------------
    // Test 2: detection below threshold after normalization → Reject
    // -------------------------------------------------------------------------
    /// <summary>
    /// DOODS2 returns confidence 30.0 (0-100 scale).
    /// MinConfidence=0.5 (0-1 scale). Validator normalizes 30.0/100=0.30 &lt; 0.5 → Reject.
    /// Counterpart to Test 1: proves the asymmetry on a 0.5 threshold.
    /// </summary>
    [TestMethod]
    public async Task ValidateAsync_DetectionBelowThresholdAfterNormalization_ReturnsReject()
    {
        using var stub = WireMockServer.Start();
        StubDetectEndpoint(stub, ("person", 30.0));

        var validator = MakeValidator(stub.Url!, minConf: 0.5, labels: ["person"]);
        var verdict = await validator.ValidateAsync(MakeContext(), MakeSnapshotContext(), CancellationToken.None);

        verdict.Passed.Should().BeFalse();
        verdict.Reason.Should().StartWith("validator_no_match");
    }

    // -------------------------------------------------------------------------
    // Test 3: label not in AllowedLabels → Reject
    // -------------------------------------------------------------------------
    /// <summary>
    /// DOODS2 returns label "dog" with high confidence (90.0 → 0.90 normalized).
    /// AllowedLabels=["person"] excludes "dog" → Reject.
    /// </summary>
    [TestMethod]
    public async Task ValidateAsync_LabelNotInAllowList_ReturnsReject()
    {
        using var stub = WireMockServer.Start();
        StubDetectEndpoint(stub, ("dog", 90.0));

        var validator = MakeValidator(stub.Url!, minConf: 0.5, labels: ["person"]);
        var verdict = await validator.ValidateAsync(MakeContext(), MakeSnapshotContext(), CancellationToken.None);

        verdict.Passed.Should().BeFalse();
        verdict.Reason.Should().StartWith("validator_no_match");
    }

    // -------------------------------------------------------------------------
    // Test 4: no snapshot → Reject immediately; zero HTTP requests
    // -------------------------------------------------------------------------
    /// <summary>
    /// default(SnapshotContext).ResolveAsync returns null → Fail("validator_no_snapshot")
    /// before reaching the HTTP layer. WireMock must receive zero requests.
    /// </summary>
    [TestMethod]
    public async Task ValidateAsync_NoSnapshot_ReturnsRejectImmediately()
    {
        using var stub = WireMockServer.Start();
        StubDetectEndpoint(stub, ("person", 90.0));

        var validator = MakeValidator(stub.Url!, minConf: 0.5, labels: ["person"]);
        var verdict = await validator.ValidateAsync(MakeContext(), default(SnapshotContext), CancellationToken.None);

        verdict.Passed.Should().BeFalse();
        verdict.Reason.Should().Be("validator_no_snapshot");
        stub.LogEntries.Should().BeEmpty("HTTP must not be called when there is no snapshot");
    }

    // -------------------------------------------------------------------------
    // Test 5: HTTP timeout + FailClosed → Fail("validator_timeout"), EventId 7201
    // -------------------------------------------------------------------------
    /// <summary>
    /// WireMock delays 10 s; client timeout 200 ms. OnError=FailClosed → Reject with
    /// EventId 7201 (Doods2ValidatorTimeout LoggerMessage).
    /// </summary>
    [TestMethod]
    public async Task ValidateAsync_HttpTimeout_FailClosed_ReturnsReject()
    {
        using var stub = WireMockServer.Start();
        // No body matchers here — error-path tests focus on validator behavior, not the
        // request-body contract (covered by Test 1's body assertion). Same pattern in tests 6-8.
        stub.Given(Request.Create().WithPath("/detect").UsingPost())
            .RespondWith(Response.Create().WithDelay(TimeSpan.FromSeconds(10)).WithStatusCode(200).WithBody("{}"));

        var logger = new CapturingLogger<Doods2Validator>();
        var validator = MakeValidator(stub.Url!, minConf: 0.5, labels: ["person"],
            onError: Doods2ValidatorErrorMode.FailClosed,
            timeout: TimeSpan.FromMilliseconds(200),
            logger: logger);

        var verdict = await validator.ValidateAsync(MakeContext(), MakeSnapshotContext(), CancellationToken.None);

        verdict.Passed.Should().BeFalse();
        verdict.Reason.Should().Be("validator_timeout");
        logger.Entries.Should().Contain(e => e.Id.Id == 7201 && e.Level == LogLevel.Warning,
            "EventId 7201 (Doods2ValidatorTimeout) must be logged on timeout");
    }

    // -------------------------------------------------------------------------
    // Test 6: HTTP timeout + FailOpen → Pass
    // -------------------------------------------------------------------------
    /// <summary>
    /// WireMock delays 10 s; client timeout 200 ms. OnError=FailOpen → Allow (EventId 7201 logged).
    /// </summary>
    [TestMethod]
    public async Task ValidateAsync_HttpTimeout_FailOpen_ReturnsAllow()
    {
        using var stub = WireMockServer.Start();
        stub.Given(Request.Create().WithPath("/detect").UsingPost())
            .RespondWith(Response.Create().WithDelay(TimeSpan.FromSeconds(10)).WithStatusCode(200).WithBody("{}"));

        var logger = new CapturingLogger<Doods2Validator>();
        var validator = MakeValidator(stub.Url!, minConf: 0.5, labels: ["person"],
            onError: Doods2ValidatorErrorMode.FailOpen,
            timeout: TimeSpan.FromMilliseconds(200),
            logger: logger);

        var verdict = await validator.ValidateAsync(MakeContext(), MakeSnapshotContext(), CancellationToken.None);

        verdict.Passed.Should().BeTrue();
        logger.Entries.Should().Contain(e => e.Id.Id == 7201 && e.Level == LogLevel.Warning,
            "EventId 7201 (Doods2ValidatorTimeout) must be logged on timeout even when FailOpen");
    }

    // -------------------------------------------------------------------------
    // Test 7: HTTP 500 server error + FailClosed → Fail("validator_unavailable:..."), EventId 7202
    // -------------------------------------------------------------------------
    /// <summary>
    /// Server returns HTTP 500. OnError=FailClosed → Reject with reason starting "validator_unavailable: "
    /// and EventId 7202 (Doods2ValidatorUnavailable LoggerMessage).
    /// </summary>
    [TestMethod]
    public async Task ValidateAsync_HttpServerError_FailClosed_ReturnsReject()
    {
        using var stub = WireMockServer.Start();
        stub.Given(Request.Create().WithPath("/detect").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500).WithBody("Internal Server Error"));

        var logger = new CapturingLogger<Doods2Validator>();
        var validator = MakeValidator(stub.Url!, minConf: 0.5, labels: ["person"],
            onError: Doods2ValidatorErrorMode.FailClosed,
            logger: logger);

        var verdict = await validator.ValidateAsync(MakeContext(), MakeSnapshotContext(), CancellationToken.None);

        verdict.Passed.Should().BeFalse();
        verdict.Reason.Should().StartWith("validator_unavailable: ");
        logger.Entries.Should().Contain(e => e.Id.Id == 7202 && e.Level == LogLevel.Warning,
            "EventId 7202 (Doods2ValidatorUnavailable) must be logged on HTTP error");
    }

    // -------------------------------------------------------------------------
    // Test 8: HTTP 500 server error + FailOpen → Pass (closes FailOpen-on-HTTP-error gap)
    // -------------------------------------------------------------------------
    /// <summary>
    /// Server returns HTTP 500. OnError=FailOpen → Allow (EventId 7202 logged).
    /// Mirrors PR #42 REVIEW-1.2 finding that added this coverage for Roboflow.
    /// </summary>
    [TestMethod]
    public async Task ValidateAsync_HttpServerError_FailOpen_ReturnsAllow()
    {
        using var stub = WireMockServer.Start();
        stub.Given(Request.Create().WithPath("/detect").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500));

        var logger = new CapturingLogger<Doods2Validator>();
        var validator = MakeValidator(stub.Url!, minConf: 0.5, labels: ["person"],
            onError: Doods2ValidatorErrorMode.FailOpen,
            logger: logger);

        var verdict = await validator.ValidateAsync(MakeContext(), MakeSnapshotContext(), CancellationToken.None);

        verdict.Passed.Should().BeTrue();
        logger.Entries.Should().Contain(e => e.Id.Id == 7202 && e.Level == LogLevel.Warning,
            "EventId 7202 (Doods2ValidatorUnavailable) must be logged on HTTP error even when FailOpen");
    }

    // -------------------------------------------------------------------------
    // Test 9: pre-cancelled token propagates OperationCanceledException (absorbed from PLAN-2.3)
    // -------------------------------------------------------------------------
    /// <summary>
    /// Host-shutdown cancellation must propagate as OperationCanceledException, not be swallowed
    /// into a verdict. The pre-cancelled token also asserts that no HTTP request is ever sent
    /// (WireMock must be empty). If a future refactor moves the cancellation check after the
    /// network call, the stub.LogEntries assertion fails. Mirrors PR #42 REVIEW-1.2 fix.
    /// </summary>
    [TestMethod]
    public async Task ValidateAsync_CancellationRequested_PropagatesOperationCanceled()
    {
        using var stub = WireMockServer.Start();
        StubDetectEndpoint(stub, ("person", 90.0));

        var validator = MakeValidator(stub.Url!, minConf: 0.5, labels: ["person"]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // TaskCanceledException derives from OperationCanceledException; ThrowsAsync matches base type.
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => validator.ValidateAsync(MakeContext(), MakeSnapshotContext(), cts.Token));

        // Pre-cancelled token must short-circuit before the HTTP layer.
        stub.LogEntries.Should().BeEmpty("a cancelled token must not reach the HTTP layer");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static readonly byte[] FakeJpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46];

    private static EventContext MakeContext(string id = "evt-doods2-1") => new()
    {
        EventId = id,
        Camera = "driveway",
        Label = "person",
        Zones = Array.Empty<string>(),
        StartedAt = DateTimeOffset.UtcNow,
        RawPayload = "{}",
        SnapshotFetcher = _ => ValueTask.FromResult<byte[]?>(null),
    };

    private static SnapshotContext MakeSnapshotContext()
    {
        var result = new SnapshotResult
        {
            Bytes = FakeJpeg,
            ContentType = "image/jpeg",
            ProviderName = "Frigate",
        };
        return new SnapshotContext(result);
    }

    /// <summary>
    /// Stubs POST /detect with body matchers protecting the DOODS2 request contract.
    /// If a future serializer change drops or renames detector_name / data / detect,
    /// every test using this stub fails fast.
    /// </summary>
    private static void StubDetectEndpoint(WireMockServer ws, params (string label, double rawConfidence0to100)[] detections)
    {
        var detectionList = detections.Select(d => new
        {
            top = 0.0,
            left = 0.0,
            bottom = 100.0,
            right = 100.0,
            label = d.label,
            confidence = d.rawConfidence0to100,
        }).ToArray();

        ws.Given(Request.Create()
                .WithPath("/detect")
                .UsingPost()
                // Body matchers harden the DOODS2 request contract (PR #42 REVIEW-1.2 lesson).
                .WithBody(new JsonPathMatcher("$.detector_name"))
                .WithBody(new JsonPathMatcher("$.data"))
                .WithBody(new JsonPathMatcher("$.detect")))
          .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBodyAsJson(new
                {
                    id = "evt-doods2-1",
                    detections = detectionList,
                }));
    }

    private static Doods2Validator MakeValidator(
        string baseUrl,
        double minConf = 0.5,
        string[]? labels = null,
        Doods2ValidatorErrorMode onError = Doods2ValidatorErrorMode.FailClosed,
        TimeSpan? timeout = null,
        CapturingLogger<Doods2Validator>? logger = null,
        string detectorName = "default")
    {
        var opts = new Doods2Options
        {
            BaseUrl = baseUrl,
            DetectorName = detectorName,
            MinConfidence = minConf,
            AllowedLabels = labels ?? [],
            OnError = onError,
            Timeout = timeout ?? TimeSpan.FromSeconds(5),
        };
        var http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = opts.Timeout,
        };
        logger ??= new CapturingLogger<Doods2Validator>();
        return new Doods2Validator("test-instance", opts, http, logger);
    }
}
