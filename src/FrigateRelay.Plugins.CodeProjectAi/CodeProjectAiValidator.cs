using System.Net.Http.Headers;
using System.Net.Http.Json;
using FrigateRelay.Abstractions;
using Microsoft.Extensions.Logging;

namespace FrigateRelay.Plugins.CodeProjectAi;

/// <summary>
/// One named instance of the CodeProject.AI validator. Submits the dispatched
/// snapshot to <c>POST {BaseUrl}/v1/vision/detection</c>, evaluates the returned
/// predictions against <see cref="CodeProjectAiOptions.MinConfidence"/> +
/// <see cref="CodeProjectAiOptions.AllowedLabels"/>, and returns a <see cref="Verdict"/>.
/// </summary>
/// <remarks>
/// <para><strong>No HTTP retry pipeline (CONTEXT-7 D4).</strong> Validators are pre-action
/// gates; per-attempt retry latency would systematically delay every notification. Single
/// timeout, fail-closed/open per <see cref="CodeProjectAiOptions.OnError"/>. INTENTIONALLY
/// asymmetric with BlueIris/Pushover action plugins, which DO retry 3/6/9s.</para>
/// <para><strong>Catch-block ordering matters (RESEARCH §6).</strong>
/// <c>OperationCanceledException when ct.IsCancellationRequested</c> is caught FIRST so host
/// shutdown propagates; <see cref="TaskCanceledException"/> (which derives from it) is caught
/// SECOND and maps to validator timeout per <see cref="CodeProjectAiOptions.OnError"/>.</para>
/// </remarks>
public sealed partial class CodeProjectAiValidator : IValidationPlugin
{
    private readonly string _name;
    private readonly CodeProjectAiOptions _opts;
    private readonly HttpClient _http;
    private readonly ILogger<CodeProjectAiValidator> _logger;

    /// <summary>Initialises a CodeProject.AI validator instance.</summary>
    /// <param name="name">The instance key from the top-level <c>Validators</c> config dictionary (e.g. <c>"strict-person"</c>).</param>
    /// <param name="opts">Bound options for this instance.</param>
    /// <param name="http">Per-instance <see cref="HttpClient"/> with <c>BaseAddress</c> + <c>Timeout</c> set by the registrar.</param>
    /// <param name="logger">Logger for validator-side warnings (timeout, unavailable). The dispatcher emits the structured <c>validator_rejected</c> entry separately.</param>
    public CodeProjectAiValidator(
        string name,
        CodeProjectAiOptions opts,
        HttpClient http,
        ILogger<CodeProjectAiValidator> logger)
    {
        _name = name;
        _opts = opts;
        _http = http;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => _name;

    /// <inheritdoc />
    public async Task<Verdict> ValidateAsync(EventContext ctx, SnapshotContext snapshot, CancellationToken ct)
    {
        var snap = await snapshot.ResolveAsync(ctx, ct).ConfigureAwait(false);
        if (snap is null)
            return Verdict.Fail("validator_no_snapshot");

        try
        {
            using var content = BuildMultipart(snap.Bytes);
            using var response = await _http.PostAsync("/v1/vision/detection", content, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<CodeProjectAiResponse>(ct).ConfigureAwait(false);
            return EvaluatePredictions(body);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Host shutdown — propagate. NOT a validator failure.
            throw;
        }
        catch (TaskCanceledException ex)
        {
            Log.ValidatorTimeout(_logger, _name, ctx.EventId, ex);
            return _opts.OnError == ValidatorErrorMode.FailOpen
                ? Verdict.Pass()
                : Verdict.Fail("validator_timeout");
        }
        catch (HttpRequestException ex)
        {
            Log.ValidatorUnavailable(_logger, _name, ctx.EventId, ex);
            return _opts.OnError == ValidatorErrorMode.FailOpen
                ? Verdict.Pass()
                : Verdict.Fail($"validator_unavailable: {ex.Message}");
        }
    }

    private static MultipartFormDataContent BuildMultipart(ReadOnlyMemory<byte> bytes)
    {
        // Phase 6 D12: .NET 10 emits unquoted name= in multipart by default. DO NOT
        // manually quote — keeps wire format consistent with other plugins.
        var content = new MultipartFormDataContent();
        var image = new ByteArrayContent(bytes.ToArray());
        image.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(image, "image", "snapshot.jpg");
        return content;
    }

    private Verdict EvaluatePredictions(CodeProjectAiResponse? body)
    {
        if (body is null || !body.Success || body.Predictions is null || body.Predictions.Count == 0)
            return Verdict.Fail("validator_no_predictions");

        foreach (var p in body.Predictions)
        {
            if (p.Confidence < _opts.MinConfidence) continue;
            if (_opts.AllowedLabels.Length > 0 &&
                !_opts.AllowedLabels.Any(l => string.Equals(l, p.Label, StringComparison.OrdinalIgnoreCase)))
                continue;
            return Verdict.Pass(p.Confidence);
        }

        return Verdict.Fail($"validator_no_match: minConfidence={_opts.MinConfidence}, allowedLabels=[{string.Join(",", _opts.AllowedLabels)}]");
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 7001, Level = LogLevel.Warning,
            Message = "CodeProject.AI validator '{Validator}' timed out for event {EventId}")]
        public static partial void ValidatorTimeout(ILogger logger, string validator, string eventId, Exception ex);

        [LoggerMessage(EventId = 7002, Level = LogLevel.Warning,
            Message = "CodeProject.AI validator '{Validator}' unavailable for event {EventId}")]
        public static partial void ValidatorUnavailable(ILogger logger, string validator, string eventId, Exception ex);
    }
}
