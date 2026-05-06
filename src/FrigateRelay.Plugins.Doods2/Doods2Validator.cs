using System.Net.Http.Json;
using System.Text.Json;
using FrigateRelay.Abstractions;
using Microsoft.Extensions.Logging;

namespace FrigateRelay.Plugins.Doods2;

/// <summary>
/// One named instance of the DOODS2 validator. Submits the dispatched snapshot to
/// <c>POST {BaseUrl}/detect</c>, evaluates the returned detections against
/// <see cref="Doods2Options.MinConfidence"/> + <see cref="Doods2Options.AllowedLabels"/>,
/// and returns a <see cref="Verdict"/>.
/// </summary>
/// <remarks>
/// <para><strong>Self-hosted only.</strong> Targets a self-hosted DOODS2 server. No auth surface.</para>
/// <para><strong>HTTP only.</strong> DOODS2 v2 (the Python rewrite at <c>snowzach/doods2</c>) is
/// HTTP-only — gRPC was a feature of the original Go-based <c>snowzach/doods</c> and was
/// intentionally dropped in v2 ("DOODS2 drops support for gRPC as I doubt very much anyone used
/// it anyways" — upstream README). Operators on the legacy Go server can use the original
/// gRPC client; this plugin does not maintain that path.</para>
/// <para><strong>DOODS2 confidence scale (RESEARCH §7.2 chief gotcha).</strong> DOODS2 returns
/// confidence in the 0–100 range, not 0–1. This validator normalizes by dividing by 100 before
/// comparing to <see cref="Doods2Options.MinConfidence"/>. All operator-facing config uses 0–1.</para>
/// <para><strong>No HTTP retry pipeline (CONTEXT-14 / CONTEXT-7 D4).</strong> Validators are
/// pre-action gates; per-attempt retry latency would systematically delay every notification.
/// Single <see cref="Doods2Options.Timeout"/>; fail-{closed,open} per
/// <see cref="Doods2Options.OnError"/>. Intentionally asymmetric with action plugins.</para>
/// <para><strong>Catch-block ordering.</strong>
/// <c>OperationCanceledException when ct.IsCancellationRequested</c> is caught FIRST so host
/// shutdown propagates; <see cref="TaskCanceledException"/> (timeout) SECOND;
/// <see cref="System.Net.Http.HttpRequestException"/> (network/non-2xx) THIRD;
/// <see cref="JsonException"/> (non-JSON or shape-shifted body) FOURTH — all three error
/// categories route through <see cref="Doods2Options.OnError"/> identically.</para>
/// </remarks>
public sealed partial class Doods2Validator : IValidationPlugin
{
    private readonly string _name;
    private readonly Doods2Options _opts;
    private readonly System.Net.Http.HttpClient _http;
    private readonly ILogger<Doods2Validator> _logger;

    /// <summary>Initialises a DOODS2 validator instance.</summary>
    /// <param name="name">The instance key from the top-level <c>Validators</c> config dictionary (e.g. <c>"doods2_persons"</c>).</param>
    /// <param name="opts">Bound options for this instance.</param>
    /// <param name="http">Per-instance <see cref="System.Net.Http.HttpClient"/> with <c>BaseAddress</c> + <c>Timeout</c> set by the registrar.</param>
    /// <param name="logger">Logger for validator-side warnings (timeout, unavailable). The dispatcher emits the structured <c>validator_rejected</c> entry separately.</param>
    public Doods2Validator(
        string name,
        Doods2Options opts,
        System.Net.Http.HttpClient http,
        ILogger<Doods2Validator> logger)
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
            // Guard before any work: a pre-cancelled token must short-circuit before reaching
            // the HTTP layer (also prevents downstream `HttpClient` from sending bytes if
            // SendAsync's pre-cancel check is bypassed by a future framework regression).
            ct.ThrowIfCancellationRequested();

            var base64 = Convert.ToBase64String(snap.Bytes);
            var request = new Doods2HttpRequest(
                DetectorName: _opts.DetectorName,
                Data: base64,
                // DOODS2's `detect` map expects 0-100 thresholds (matches its on-wire confidence
                // scale). MinConfidence is operator-facing 0-1; multiply by 100.0 to send the
                // server-side filter the right value. The plugin's post-filter
                // (`EvaluateDetections`) is the authoritative gate; this just keeps DOODS2 from
                // returning every detection above 0.5% confidence when the operator wanted 50%.
                Detect: new Dictionary<string, double> { ["*"] = _opts.MinConfidence * 100.0 });

            using var response = await _http.PostAsJsonAsync("/detect", request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<Doods2HttpResponse>(ct).ConfigureAwait(false);
            return EvaluateDetections(body?.Detections ?? Array.Empty<Doods2Detection>());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Host shutdown — propagate. NOT a validator failure.
            throw;
        }
        catch (TaskCanceledException ex)
        {
            Log.Doods2ValidatorTimeout(_logger, _name, ctx.EventId, ex);
            return _opts.OnError == Doods2ValidatorErrorMode.FailOpen
                ? Verdict.Pass()
                : Verdict.Fail("validator_timeout");
        }
        catch (System.Net.Http.HttpRequestException ex)
        {
            Log.Doods2ValidatorUnavailable(_logger, _name, ctx.EventId, ex);
            return _opts.OnError == Doods2ValidatorErrorMode.FailOpen
                ? Verdict.Pass()
                : Verdict.Fail($"validator_unavailable: {ex.Message}");
        }
        catch (JsonException ex)
        {
            // DOODS2 returned non-JSON (HTML error page from a misbehaving proxy, truncated body)
            // or a shape we don't recognize. Route through OnError so a misbehaving server can't
            // escape the FailOpen/FailClosed contract via an unhandled deserialization exception.
            Log.Doods2ValidatorUnavailable(_logger, _name, ctx.EventId, ex);
            return _opts.OnError == Doods2ValidatorErrorMode.FailOpen
                ? Verdict.Pass()
                : Verdict.Fail($"validator_unavailable: {ex.Message}");
        }
    }

    private Verdict EvaluateDetections(IReadOnlyList<Doods2Detection> detections)
    {
        if (detections.Count == 0)
            return Verdict.Fail("validator_no_predictions");

        foreach (var d in detections)
        {
            // DOODS2 returns confidence in 0-100 scale; normalize to 0-1 before comparing.
            var normalized = d.Confidence / 100.0;
            if (normalized < _opts.MinConfidence) continue;
            // STJ does not enforce non-nullable record params on deserialization; a missing/null
            // label deserializes as null. Skip such detections rather than risk NRE in the
            // AllowedLabels comparison.
            if (string.IsNullOrEmpty(d.Label)) continue;
            if (_opts.AllowedLabels.Length > 0 &&
                !_opts.AllowedLabels.Any(l => string.Equals(l, d.Label, StringComparison.OrdinalIgnoreCase)))
                continue;
            return Verdict.Pass(normalized);
        }

        return Verdict.Fail($"validator_no_match: minConfidence={_opts.MinConfidence}, allowedLabels=[{string.Join(",", _opts.AllowedLabels)}]");
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 7201, Level = LogLevel.Warning,
            Message = "DOODS2 validator '{Validator}' timed out for event {FrigateEventId}")]
        public static partial void Doods2ValidatorTimeout(ILogger logger, string validator, string frigateEventId, Exception ex);

        [LoggerMessage(EventId = 7202, Level = LogLevel.Warning,
            Message = "DOODS2 validator '{Validator}' unavailable for event {FrigateEventId}")]
        public static partial void Doods2ValidatorUnavailable(ILogger logger, string validator, string frigateEventId, Exception ex);
    }
}
