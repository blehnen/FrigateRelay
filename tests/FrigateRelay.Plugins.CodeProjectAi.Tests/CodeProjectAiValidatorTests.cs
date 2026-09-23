using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
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
    // Test 9: configured MinConfidence is sent as the min_confidence form field (#133)
    // -------------------------------------------------------------------------
    [TestMethod]
    public async Task ValidateAsync_Multipart_SendsMinConfidenceFormField()
    {
        using var stub = WireMockServer.Start();
        stub.Given(Request.Create().WithPath("/v1/vision/detection").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(
                new { success = true, code = 200, predictions = Array.Empty<object>() }));

        var validator = NewValidator(stub.Url!, minConfidence: 0.25);
        await validator.ValidateAsync(MakeEvent(), MakeSnapshot(), CancellationToken.None);

        var body = stub.LogEntries.Single().RequestMessage?.BodyAsBytes
            ?? throw new InvalidOperationException("WireMock recorded no body bytes");
        ReadMinConfidenceField(body).Should().Be("0.25");
    }

    // -------------------------------------------------------------------------
    // Test 10: min_confidence is culture-invariant — a de-DE host must not send "0,25"
    // -------------------------------------------------------------------------
    [TestMethod]
    public async Task ValidateAsync_CommaDecimalCulture_SendsInvariantMinConfidence()
    {
        using var stub = WireMockServer.Start();
        stub.Given(Request.Create().WithPath("/v1/vision/detection").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(
                new { success = true, code = 200, predictions = Array.Empty<object>() }));

        var validator = NewValidator(stub.Url!, minConfidence: 0.25);
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            await validator.ValidateAsync(MakeEvent(), MakeSnapshot(), CancellationToken.None);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        var body = stub.LogEntries.Single().RequestMessage?.BodyAsBytes
            ?? throw new InvalidOperationException("WireMock recorded no body bytes");
        ReadMinConfidenceField(body).Should().Be("0.25");
    }

    // -------------------------------------------------------------------------
    // Test 11: MinConfidence below the server's default is reachable (#133 regression).
    // The stub mimics a CPAI-shape backend with a 0.4 server-side floor that is only
    // lowered when the request carries min_confidence — as blueiris-ai-gateway does.
    // -------------------------------------------------------------------------
    [TestMethod]
    public async Task ValidateAsync_MinConfidenceBelowServerDefault_ReturnsPass()
    {
        using var stub = WireMockServer.Start();
        stub.Given(Request.Create().WithPath("/v1/vision/detection").UsingPost()
                .WithBody((byte[]? b) => b is not null
                    && double.TryParse(ReadMinConfidenceField(b), NumberStyles.Float, CultureInfo.InvariantCulture, out var min)
                    && min <= 0.3))
            .AtPriority(1)
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(
                new { success = true, code = 200, predictions = new[] {
                    new { label = "person", confidence = 0.30, x_min = 1, y_min = 2, x_max = 3, y_max = 4 } } }));
        stub.Given(Request.Create().WithPath("/v1/vision/detection").UsingPost())
            .AtPriority(2)
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(
                new { success = true, code = 200, predictions = Array.Empty<object>() }));

        var validator = NewValidator(stub.Url!, minConfidence: 0.25, allowedLabels: ["person"]);
        var verdict = await validator.ValidateAsync(MakeEvent(), MakeSnapshot(), CancellationToken.None);

        verdict.Passed.Should().BeTrue("the server must be told the configured threshold, not apply its own 0.4 default");
        verdict.Score.Should().BeApproximately(0.30, 0.001);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>Extracts the <c>min_confidence</c> form-field value from a raw multipart body, or null if absent.</summary>
    private static string? ReadMinConfidenceField(byte[] body)
    {
        var match = Regex.Match(
            Encoding.UTF8.GetString(body),
            @"name=""?min_confidence""?\r\n(?:[^\r\n]+\r\n)*\r\n(?<value>[^\r\n]*)\r\n");
        return match.Success ? match.Groups["value"].Value : null;
    }

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
