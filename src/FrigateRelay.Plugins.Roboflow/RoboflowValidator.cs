using System.Net.Http.Json;
using FrigateRelay.Abstractions;
using Microsoft.Extensions.Logging;

namespace FrigateRelay.Plugins.Roboflow;

/// <summary>
/// One named instance of the Roboflow Inference validator. Submits the dispatched snapshot to
/// <c>POST {BaseUrl}/infer/object_detection</c>, evaluates the returned predictions against
/// <see cref="RoboflowOptions.MinConfidence"/> + <see cref="RoboflowOptions.AllowedLabels"/>,
/// and returns a <see cref="Verdict"/>.
/// </summary>
/// <remarks>
/// <para><strong>Self-hosted only (CONTEXT-14 D2).</strong> Targets the self-hosted Roboflow
/// Inference server. No Roboflow Hosted Cloud API in v1.2.</para>
/// <para><strong>Per-instance model (CONTEXT-14 D3).</strong> <see cref="RoboflowOptions.ModelId"/>
/// is per-instance; operators declare multiple validator instances for per-camera model selection.
/// </para>
/// <para><strong>No HTTP retry pipeline (CONTEXT-14 / CONTEXT-7 D4).</strong> Validators are
/// pre-action gates; per-attempt retry latency would systematically delay every notification.
/// Single timeout, fail-closed/open per <see cref="RoboflowOptions.OnError"/>. INTENTIONALLY
/// asymmetric with BlueIris/Pushover action plugins, which DO retry 3/6/9s.</para>
/// <para><strong>Catch-block ordering matters (RESEARCH §1.4).</strong>
/// <c>OperationCanceledException when ct.IsCancellationRequested</c> is caught FIRST so host
/// shutdown propagates; <see cref="TaskCanceledException"/> (which derives from it) is caught
/// SECOND and maps to validator timeout per <see cref="RoboflowOptions.OnError"/>.</para>
/// <para><strong>Confidence scale.</strong> Roboflow returns confidence 0.0–1.0. No normalization
/// is applied — compare directly to <see cref="RoboflowOptions.MinConfidence"/>.</para>
/// </remarks>
public sealed partial class RoboflowValidator : IValidationPlugin
{
    private readonly string _name;
    private readonly RoboflowOptions _opts;
    private readonly HttpClient _http;
    private readonly ILogger<RoboflowValidator> _logger;

    /// <summary>Initialises a Roboflow Inference validator instance.</summary>
    /// <param name="name">The instance key from the top-level <c>Validators</c> config dictionary (e.g. <c>"roboflow_persons"</c>).</param>
    /// <param name="opts">Bound options for this instance.</param>
    /// <param name="http">Per-instance <see cref="HttpClient"/> with <c>BaseAddress</c> + <c>Timeout</c> set by the registrar.</param>
    /// <param name="logger">Logger for validator-side warnings (timeout, unavailable). The dispatcher emits the structured <c>validator_rejected</c> entry separately.</param>
    public RoboflowValidator(
        string name,
        RoboflowOptions opts,
        HttpClient http,
        ILogger<RoboflowValidator> logger)
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
            var base64 = Convert.ToBase64String(snap.Bytes);
            var request = new RoboflowRequest(
                ModelId: _opts.ModelId,
                Image: new RoboflowRequestImage(Type: "base64", Value: base64),
                Confidence: _opts.MinConfidence);

            using var response = await _http.PostAsJsonAsync("/infer/object_detection", request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<RoboflowResponse>(ct).ConfigureAwait(false);
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
            return _opts.OnError == RoboflowValidatorErrorMode.FailOpen
                ? Verdict.Pass()
                : Verdict.Fail("validator_timeout");
        }
        catch (HttpRequestException ex)
        {
            Log.ValidatorUnavailable(_logger, _name, ctx.EventId, ex);
            return _opts.OnError == RoboflowValidatorErrorMode.FailOpen
                ? Verdict.Pass()
                : Verdict.Fail($"validator_unavailable: {ex.Message}");
        }
    }

    private Verdict EvaluatePredictions(RoboflowResponse? body)
    {
        if (body?.Predictions is null || body.Predictions.Count == 0)
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
        [LoggerMessage(EventId = 7101, Level = LogLevel.Warning,
            Message = "Roboflow validator '{Validator}' timed out for event {FrigateEventId}")]
        public static partial void ValidatorTimeout(ILogger logger, string validator, string frigateEventId, Exception ex);

        [LoggerMessage(EventId = 7102, Level = LogLevel.Warning,
            Message = "Roboflow validator '{Validator}' unavailable for event {FrigateEventId}")]
        public static partial void ValidatorUnavailable(ILogger logger, string validator, string frigateEventId, Exception ex);
    }
}
