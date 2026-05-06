using FluentAssertions;
using FrigateRelay.Abstractions;
using FrigateRelay.Plugins.Roboflow;
using Microsoft.Extensions.Logging;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace FrigateRelay.Plugins.Roboflow.Tests;

[TestClass]
public sealed class RoboflowValidatorTests
{
    // -------------------------------------------------------------------------
    // Test 1: prediction above threshold + matching label → Pass
    // -------------------------------------------------------------------------
    /// <summary>Happy path: one prediction above MinConfidence with a matching AllowedLabel.</summary>
    [TestMethod]
    public async Task ValidateAsync_PredictionAboveThreshold_ReturnsAllow()
    {
        using var stub = WireMockServer.Start();
        StubInferEndpoint(stub, new
        {
            predictions = new[] { new { x = 320, y = 240, width = 50, height = 100, @class = "person", confidence = 0.92, class_id = 0 } },
            image = new { width = 640, height = 480 },
            time = 0.143
        });

        var validator = MakeValidator(stub.Url!, minConf: 0.5, labels: ["person"]);
        var verdict = await validator.ValidateAsync(MakeContext(), MakeSnapshotContext(), CancellationToken.None);

        verdict.Passed.Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // Test 2: prediction below threshold → Fail with reason "validator_no_match"
    // -------------------------------------------------------------------------
    /// <summary>Prediction confidence 0.30 is below MinConfidence 0.50 → reject.</summary>
    [TestMethod]
    public async Task ValidateAsync_PredictionBelowThreshold_ReturnsReject()
    {
        using var stub = WireMockServer.Start();
        StubInferEndpoint(stub, new
        {
            predictions = new[] { new { x = 320, y = 240, width = 50, height = 100, @class = "person", confidence = 0.30, class_id = 0 } },
            image = new { width = 640, height = 480 },
            time = 0.05
        });

        var validator = MakeValidator(stub.Url!, minConf: 0.5, labels: ["person"]);
        var verdict = await validator.ValidateAsync(MakeContext(), MakeSnapshotContext(), CancellationToken.None);

        verdict.Passed.Should().BeFalse();
        verdict.Reason.Should().StartWith("validator_no_match");
    }

    // -------------------------------------------------------------------------
    // Test 3: label not in AllowedLabels → Fail with reason "validator_no_match"
    // -------------------------------------------------------------------------
    /// <summary>Prediction class "dog" not in AllowedLabels ["person"] → reject.</summary>
    [TestMethod]
    public async Task ValidateAsync_LabelNotInAllowList_ReturnsReject()
    {
        using var stub = WireMockServer.Start();
        StubInferEndpoint(stub, new
        {
            predictions = new[] { new { x = 320, y = 240, width = 50, height = 100, @class = "dog", confidence = 0.92, class_id = 1 } },
            image = new { width = 640, height = 480 },
            time = 0.07
        });

        var validator = MakeValidator(stub.Url!, minConf: 0.5, labels: ["person"]);
        var verdict = await validator.ValidateAsync(MakeContext(), MakeSnapshotContext(), CancellationToken.None);

        verdict.Passed.Should().BeFalse();
        verdict.Reason.Should().StartWith("validator_no_match");
    }

    // -------------------------------------------------------------------------
    // Test 4: no snapshot → Fail immediately; zero HTTP requests
    // -------------------------------------------------------------------------
    /// <summary>default(SnapshotContext) returns null from ResolveAsync → fail before HTTP call.</summary>
    [TestMethod]
    public async Task ValidateAsync_NoSnapshot_ReturnsRejectImmediately()
    {
        using var stub = WireMockServer.Start();
        StubInferEndpoint(stub, new
        {
            predictions = new[] { new { x = 0, y = 0, width = 10, height = 10, @class = "person", confidence = 0.9, class_id = 0 } },
            image = new { width = 640, height = 480 },
            time = 0.01
        });

        var validator = MakeValidator(stub.Url!, minConf: 0.5, labels: ["person"]);
        var verdict = await validator.ValidateAsync(MakeContext(), default(SnapshotContext), CancellationToken.None);

        verdict.Passed.Should().BeFalse();
        verdict.Reason.Should().Be("validator_no_snapshot");
        stub.LogEntries.Should().BeEmpty("HTTP must not be called when there is no snapshot");
    }

    // -------------------------------------------------------------------------
    // Test 5: timeout + FailClosed → Fail("validator_timeout"), EventId 7101 logged
    // -------------------------------------------------------------------------
    /// <summary>HTTP hangs beyond client timeout, OnError=FailClosed → reject with EventId 7101.</summary>
    [TestMethod]
    public async Task ValidateAsync_HttpTimeout_FailClosed_ReturnsReject()
    {
        using var stub = WireMockServer.Start();
        stub.Given(Request.Create().WithPath("/infer/object_detection").UsingPost())
            .RespondWith(Response.Create().WithDelay(TimeSpan.FromSeconds(10)).WithStatusCode(200).WithBody("{}"));

        var logger = new CapturingLogger<RoboflowValidator>();
        var validator = MakeValidator(stub.Url!, minConf: 0.5, labels: ["person"],
            onError: RoboflowValidatorErrorMode.FailClosed,
            timeout: TimeSpan.FromMilliseconds(200),
            logger: logger);

        var verdict = await validator.ValidateAsync(MakeContext(), MakeSnapshotContext(), CancellationToken.None);

        verdict.Passed.Should().BeFalse();
        verdict.Reason.Should().Be("validator_timeout");
        logger.Entries.Should().Contain(e => e.Id.Id == 7101 && e.Level == LogLevel.Warning);
    }

    // -------------------------------------------------------------------------
    // Test 6: timeout + FailOpen → Pass(), EventId 7101 logged
    // -------------------------------------------------------------------------
    /// <summary>HTTP hangs beyond client timeout, OnError=FailOpen → allow with EventId 7101.</summary>
    [TestMethod]
    public async Task ValidateAsync_HttpTimeout_FailOpen_ReturnsAllow()
    {
        using var stub = WireMockServer.Start();
        stub.Given(Request.Create().WithPath("/infer/object_detection").UsingPost())
            .RespondWith(Response.Create().WithDelay(TimeSpan.FromSeconds(10)).WithStatusCode(200).WithBody("{}"));

        var logger = new CapturingLogger<RoboflowValidator>();
        var validator = MakeValidator(stub.Url!, minConf: 0.5, labels: ["person"],
            onError: RoboflowValidatorErrorMode.FailOpen,
            timeout: TimeSpan.FromMilliseconds(200),
            logger: logger);

        var verdict = await validator.ValidateAsync(MakeContext(), MakeSnapshotContext(), CancellationToken.None);

        verdict.Passed.Should().BeTrue();
        logger.Entries.Should().Contain(e => e.Id.Id == 7101 && e.Level == LogLevel.Warning);
    }

    // -------------------------------------------------------------------------
    // Test 7: HTTP 500 server error + FailClosed → Fail("validator_unavailable:..."), EventId 7102
    // -------------------------------------------------------------------------
    /// <summary>Server returns 500, OnError=FailClosed → reject with EventId 7102.</summary>
    [TestMethod]
    public async Task ValidateAsync_HttpServerError_FailClosed_ReturnsReject()
    {
        using var stub = WireMockServer.Start();
        stub.Given(Request.Create().WithPath("/infer/object_detection").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500).WithBody("Internal Server Error"));

        var logger = new CapturingLogger<RoboflowValidator>();
        var validator = MakeValidator(stub.Url!, minConf: 0.5, labels: ["person"],
            onError: RoboflowValidatorErrorMode.FailClosed,
            logger: logger);

        var verdict = await validator.ValidateAsync(MakeContext(), MakeSnapshotContext(), CancellationToken.None);

        verdict.Passed.Should().BeFalse();
        verdict.Reason.Should().StartWith("validator_unavailable: ");
        logger.Entries.Should().Contain(e => e.Id.Id == 7102 && e.Level == LogLevel.Warning);
    }

    // -------------------------------------------------------------------------
    // Test 8: pre-cancelled token propagates OperationCanceledException (not swallowed)
    // -------------------------------------------------------------------------
    /// <summary>Host-shutdown cancellation propagates as OperationCanceledException, not a verdict.</summary>
    [TestMethod]
    public async Task ValidateAsync_CancellationRequested_PropagatesOperationCanceled()
    {
        using var stub = WireMockServer.Start();
        StubInferEndpoint(stub, new
        {
            predictions = new[] { new { x = 0, y = 0, width = 10, height = 10, @class = "person", confidence = 0.9, class_id = 0 } },
            image = new { width = 640, height = 480 },
            time = 0.01
        });

        var validator = MakeValidator(stub.Url!, minConf: 0.5, labels: ["person"]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // TaskCanceledException derives from OperationCanceledException; the validator rethrows
        // whichever subtype HttpClient raises. ThrowsAsync matches the base type correctly.
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => validator.ValidateAsync(MakeContext(), MakeSnapshotContext(), cts.Token));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static readonly byte[] FakeJpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46];

    private static EventContext MakeContext(string id = "evt-rf-1") => new()
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

    private static void StubInferEndpoint(WireMockServer ws, object response) =>
        ws.Given(Request.Create().WithPath("/infer/object_detection").UsingPost())
          .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(response));

    private static RoboflowValidator MakeValidator(
        string baseUrl,
        double minConf = 0.5,
        string[]? labels = null,
        RoboflowValidatorErrorMode onError = RoboflowValidatorErrorMode.FailClosed,
        TimeSpan? timeout = null,
        CapturingLogger<RoboflowValidator>? logger = null)
    {
        var opts = new RoboflowOptions
        {
            BaseUrl = baseUrl,
            ModelId = "rfdetr-base/1",
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
        logger ??= new CapturingLogger<RoboflowValidator>();
        return new RoboflowValidator("test-instance", opts, http, logger);
    }
}
