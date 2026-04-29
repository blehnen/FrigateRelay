using System.Text;
using FluentAssertions;
using FrigateRelay.Abstractions;
using FrigateRelay.Plugins.CodeProjectAi;
using Microsoft.Extensions.Logging;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace FrigateRelay.Plugins.CodeProjectAi.Tests;

[TestClass]
public sealed class CodeProjectAiValidatorTests
{
    // -------------------------------------------------------------------------
    // Test 1: confidence above threshold + matching label → Pass
    // -------------------------------------------------------------------------
    [TestMethod]
    public async Task ValidateAsync_PredictionAboveThreshold_ReturnsPass()
    {
        using var stub = WireMockServer.Start();
        stub.Given(Request.Create().WithPath("/v1/vision/detection").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(
                new { success = true, code = 200, predictions = new[] {
                    new { label = "person", confidence = 0.87, x_min = 1, y_min = 2, x_max = 3, y_max = 4 } } }));

        var validator = NewValidator(stub.Url!, minConfidence: 0.5, allowedLabels: ["person"]);
        var verdict = await validator.ValidateAsync(MakeEvent(), MakeSnapshot(), CancellationToken.None);

        verdict.Passed.Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // Test 2: confidence below threshold → Fail with reason mentioning confidence
    // -------------------------------------------------------------------------
    [TestMethod]
    public async Task ValidateAsync_PredictionBelowThreshold_ReturnsFail()
    {
        using var stub = WireMockServer.Start();
        stub.Given(Request.Create().WithPath("/v1/vision/detection").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(
                new { success = true, code = 200, predictions = new[] {
                    new { label = "person", confidence = 0.30, x_min = 1, y_min = 2, x_max = 3, y_max = 4 } } }));

        var validator = NewValidator(stub.Url!, minConfidence: 0.5, allowedLabels: ["person"]);
        var verdict = await validator.ValidateAsync(MakeEvent(), MakeSnapshot(), CancellationToken.None);

        verdict.Passed.Should().BeFalse();
        verdict.Reason.Should().Contain("minConfidence");
    }

    // -------------------------------------------------------------------------
    // Test 3: label not in AllowedLabels → Fail
    // -------------------------------------------------------------------------
    [TestMethod]
    public async Task ValidateAsync_LabelNotInAllowedList_ReturnsFail()
    {
        using var stub = WireMockServer.Start();
        stub.Given(Request.Create().WithPath("/v1/vision/detection").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(
                new { success = true, code = 200, predictions = new[] {
                    new { label = "dog", confidence = 0.95, x_min = 1, y_min = 2, x_max = 3, y_max = 4 } } }));

        var validator = NewValidator(stub.Url!, minConfidence: 0.5, allowedLabels: ["person"]);
        var verdict = await validator.ValidateAsync(MakeEvent(), MakeSnapshot(), CancellationToken.None);

        verdict.Passed.Should().BeFalse();
        verdict.Reason.Should().Contain("allowedLabels");
    }

    // -------------------------------------------------------------------------
    // Test 4: AllowedLabels empty = no filter → Pass on any label above threshold
    // -------------------------------------------------------------------------
    [TestMethod]
    public async Task ValidateAsync_AllowedLabelsEmpty_AcceptsAnyLabel()
    {
        using var stub = WireMockServer.Start();
        stub.Given(Request.Create().WithPath("/v1/vision/detection").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(
                new { success = true, code = 200, predictions = new[] {
                    new { label = "dog", confidence = 0.90, x_min = 1, y_min = 2, x_max = 3, y_max = 4 } } }));

        var validator = NewValidator(stub.Url!, minConfidence: 0.5, allowedLabels: []);
        var verdict = await validator.ValidateAsync(MakeEvent(), MakeSnapshot(), CancellationToken.None);

        verdict.Passed.Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // Test 5: timeout + FailClosed → Verdict.Fail("validator_timeout")
    // -------------------------------------------------------------------------
    [TestMethod]
    public async Task ValidateAsync_FailClosed_OnTimeout_ReturnsFail()
    {
        using var stub = WireMockServer.Start();
        stub.Given(Request.Create().WithPath("/v1/vision/detection").UsingPost())
            .RespondWith(Response.Create().WithDelay(TimeSpan.FromSeconds(10))
                .WithStatusCode(200).WithBody("{}"));

        var logger = new CapturingLogger<CodeProjectAiValidator>();
        var validator = NewValidator(stub.Url!, timeout: TimeSpan.FromMilliseconds(500),
            onError: ValidatorErrorMode.FailClosed, logger: logger);

        var verdict = await validator.ValidateAsync(MakeEvent(), MakeSnapshot(), CancellationToken.None);

        verdict.Passed.Should().BeFalse();
        verdict.Reason.Should().Be("validator_timeout");
        logger.Entries.Should().Contain(e => e.Id.Id == 7001 && e.Level == LogLevel.Warning);
    }

    // -------------------------------------------------------------------------
    // Test 6: timeout + FailOpen → Verdict.Pass()
    // -------------------------------------------------------------------------
    [TestMethod]
    public async Task ValidateAsync_FailOpen_OnTimeout_ReturnsPass()
    {
        using var stub = WireMockServer.Start();
        stub.Given(Request.Create().WithPath("/v1/vision/detection").UsingPost())
            .RespondWith(Response.Create().WithDelay(TimeSpan.FromSeconds(10))
                .WithStatusCode(200).WithBody("{}"));

        var logger = new CapturingLogger<CodeProjectAiValidator>();
        var validator = NewValidator(stub.Url!, timeout: TimeSpan.FromMilliseconds(500),
            onError: ValidatorErrorMode.FailOpen, logger: logger);

        var verdict = await validator.ValidateAsync(MakeEvent(), MakeSnapshot(), CancellationToken.None);

        verdict.Passed.Should().BeTrue();
        logger.Entries.Should().Contain(e => e.Id.Id == 7001 && e.Level == LogLevel.Warning);
    }

    // -------------------------------------------------------------------------
    // Test 7: multipart wire format uses unquoted name=image (Phase 6 D12)
    // -------------------------------------------------------------------------
    [TestMethod]
    public async Task ValidateAsync_MultipartWireFormat_UsesUnquotedNameImage()
    {
        using var stub = WireMockServer.Start();
        stub.Given(Request.Create().WithPath("/v1/vision/detection").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(
                new { success = true, code = 200, predictions = new[] {
                    new { label = "person", confidence = 0.9, x_min = 1, y_min = 2, x_max = 3, y_max = 4 } } }));

        var validator = NewValidator(stub.Url!, minConfidence: 0.5, allowedLabels: ["person"]);
        await validator.ValidateAsync(MakeEvent(), MakeSnapshot(), CancellationToken.None);

        var req = stub.LogEntries.Single();
        // WireMock returns null Body (string) for binary multipart bodies — read raw bytes.
        var rawBytes = req.RequestMessage?.BodyAsBytes
            ?? throw new InvalidOperationException("WireMock recorded no body bytes");
        var asText = Encoding.UTF8.GetString(rawBytes);

        // .NET 10 emits unquoted name= and filename= (Phase 6 D12).
        asText.Should().Contain("name=image", "multipart name parameter must be unquoted on .NET 10");
        asText.Should().NotContain("name=\"image\"", "manual quoting would diverge from default and break wire-format invariants");
    }

    // -------------------------------------------------------------------------
    // Test 8: success=true with predictions parses correctly through DTO
    // -------------------------------------------------------------------------
    [TestMethod]
    public async Task ValidateAsync_HappyPath_ParsesPredictionsArray()
    {
        using var stub = WireMockServer.Start();
        stub.Given(Request.Create().WithPath("/v1/vision/detection").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(
                new
                {
                    success = true,
                    code = 200,
                    processMs = 31,
                    inferenceMs = 24,
                    predictions = new[]
                    {
                        new { label = "person", confidence = 0.87, x_min = 142, y_min = 88, x_max = 396, y_max = 612 },
                        new { label = "car",    confidence = 0.52, x_min = 412, y_min = 318, x_max = 781, y_max = 540 },
                    }
                }));

        // MinConfidence 0.85 + AllowedLabels ["person"] → only the person prediction qualifies.
        var validator = NewValidator(stub.Url!, minConfidence: 0.85, allowedLabels: ["person"]);
        var verdict = await validator.ValidateAsync(MakeEvent(), MakeSnapshot(), CancellationToken.None);

        verdict.Passed.Should().BeTrue();
        verdict.Score.Should().BeApproximately(0.87, 0.001, "Verdict.Pass(score) carries the matched prediction's confidence");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static readonly byte[] FakeJpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46];

    private static EventContext MakeEvent(string id = "evt-1") => new()
    {
        EventId = id,
        Camera = "front_door",
        Label = "person",
        Zones = Array.Empty<string>(),
        StartedAt = DateTimeOffset.UtcNow,
        RawPayload = "{}",
        SnapshotFetcher = _ => ValueTask.FromResult<byte[]?>(null),
    };

    private static SnapshotContext MakeSnapshot()
    {
        var result = new SnapshotResult
        {
            Bytes = FakeJpeg,
            ContentType = "image/jpeg",
            ProviderName = "Frigate",
        };
        return new SnapshotContext(result);
    }

    private static CodeProjectAiValidator NewValidator(
        string baseUrl,
        double minConfidence = 0.5,
        string[]? allowedLabels = null,
        ValidatorErrorMode onError = ValidatorErrorMode.FailClosed,
        TimeSpan? timeout = null,
        CapturingLogger<CodeProjectAiValidator>? logger = null)
    {
        var opts = new CodeProjectAiOptions
        {
            BaseUrl = baseUrl,
            MinConfidence = minConfidence,
            AllowedLabels = allowedLabels ?? [],
            OnError = onError,
            Timeout = timeout ?? TimeSpan.FromSeconds(5),
        };
        var http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = opts.Timeout,
        };
        logger ??= new CapturingLogger<CodeProjectAiValidator>();
        return new CodeProjectAiValidator("test-instance", opts, http, logger);
    }
}
