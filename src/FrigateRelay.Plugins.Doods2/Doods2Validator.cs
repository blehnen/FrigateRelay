using System.Net.Http.Json;
using FrigateRelay.Abstractions;
using FrigateRelay.Plugins.Doods2.Grpc;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace FrigateRelay.Plugins.Doods2;

/// <summary>
/// One named instance of the DOODS2 validator. Submits the dispatched snapshot to either
/// <c>POST {BaseUrl}/detect</c> (HTTP) or the gRPC <c>odrpc.Detect</c> RPC, evaluates the
/// returned detections against <see cref="Doods2Options.MinConfidence"/> +
/// <see cref="Doods2Options.AllowedLabels"/>, and returns a <see cref="Verdict"/>.
/// </summary>
/// <remarks>
/// <para><strong>Self-hosted only.</strong> Targets a self-hosted DOODS2 server. No auth surface.</para>
/// <para><strong>Dual transport (CONTEXT-14 D4).</strong> <see cref="Doods2Options.Transport"/>
/// selects between HTTP and gRPC per validator instance. Both clients are constructor-injected;
/// only the transport-relevant client is invoked at runtime — this keeps the registrar branch-free
/// and the type testable without conditional DI logic.</para>
/// <para><strong>DOODS2 confidence scale (RESEARCH §7.2 chief gotcha).</strong> DOODS2 returns
/// confidence in the 0–100 range, not 0–1. This validator normalizes by dividing by 100 before
/// comparing to <see cref="Doods2Options.MinConfidence"/>. All operator-facing config uses 0–1.</para>
/// <para><strong>No HTTP retry pipeline (CONTEXT-14 / CONTEXT-7 D4).</strong> Validators are
/// pre-action gates; per-attempt retry latency would systematically delay every notification.
/// Single <see cref="Doods2Options.Timeout"/>; fail-{closed,open} per
/// <see cref="Doods2Options.OnError"/>. Intentionally asymmetric with action plugins.</para>
/// <para><strong>Catch-block ordering (RESEARCH §1.4).</strong>
/// <c>OperationCanceledException when ct.IsCancellationRequested</c> is caught FIRST so host
/// shutdown propagates; <see cref="TaskCanceledException"/> (HTTP timeout) is SECOND;
/// <see cref="RpcException"/> with <see cref="StatusCode.DeadlineExceeded"/> (gRPC deadline) is
/// THIRD — same OnError mapping as HTTP timeout; generic <see cref="RpcException"/> (gRPC
/// unavailable) is FOURTH; <see cref="System.Net.Http.HttpRequestException"/> (HTTP unavailable)
/// is FIFTH.</para>
/// </remarks>
public sealed partial class Doods2Validator : IValidationPlugin
{
    private readonly string _name;
    private readonly Doods2Options _opts;
    private readonly System.Net.Http.HttpClient _http;
    private readonly odrpc.odrpcClient _grpcClient;
    private readonly ILogger<Doods2Validator> _logger;

    /// <summary>Initialises a DOODS2 validator instance.</summary>
    /// <param name="name">The instance key from the top-level <c>Validators</c> config dictionary (e.g. <c>"doods2_persons"</c>).</param>
    /// <param name="opts">Bound options for this instance.</param>
    /// <param name="http">Per-instance <see cref="System.Net.Http.HttpClient"/> with <c>BaseAddress</c> + <c>Timeout</c> set by the registrar (used by HTTP transport).</param>
    /// <param name="grpcClient">Per-instance gRPC client sharing a singleton <c>GrpcChannel</c> (used by gRPC transport).</param>
    /// <param name="logger">Logger for validator-side warnings (timeout, unavailable). The dispatcher emits the structured <c>validator_rejected</c> entry separately.</param>
    public Doods2Validator(
        string name,
        Doods2Options opts,
        System.Net.Http.HttpClient http,
        odrpc.odrpcClient grpcClient,
        ILogger<Doods2Validator> logger)
    {
        _name = name;
        _opts = opts;
        _http = http;
        _grpcClient = grpcClient;
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
            var detections = _opts.Transport == Doods2Transport.Grpc
                ? await DetectGrpcAsync(snap.Bytes, ct).ConfigureAwait(false)
                : await DetectHttpAsync(snap.Bytes, ct).ConfigureAwait(false);

            return EvaluateDetections(detections);
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
        catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
        {
            // gRPC deadline-exceeded is the gRPC equivalent of HTTP timeout.
            Log.Doods2ValidatorTimeout(_logger, _name, ctx.EventId, ex);
            return _opts.OnError == Doods2ValidatorErrorMode.FailOpen
                ? Verdict.Pass()
                : Verdict.Fail("validator_timeout");
        }
        catch (RpcException ex)
        {
            Log.Doods2ValidatorUnavailable(_logger, _name, ctx.EventId, ex);
            return _opts.OnError == Doods2ValidatorErrorMode.FailOpen
                ? Verdict.Pass()
                : Verdict.Fail($"validator_unavailable: {ex.Message}");
        }
        catch (System.Net.Http.HttpRequestException ex)
        {
            Log.Doods2ValidatorUnavailable(_logger, _name, ctx.EventId, ex);
            return _opts.OnError == Doods2ValidatorErrorMode.FailOpen
                ? Verdict.Pass()
                : Verdict.Fail($"validator_unavailable: {ex.Message}");
        }
    }

    private async Task<IReadOnlyList<Doods2Detection>> DetectHttpAsync(ReadOnlyMemory<byte> bytes, CancellationToken ct)
    {
        var base64 = Convert.ToBase64String(bytes.Span);
        var request = new Doods2HttpRequest(
            DetectorName: _opts.DetectorName,
            Data: base64,
            Detect: new Dictionary<string, double> { ["*"] = _opts.MinConfidence });

        using var response = await _http.PostAsJsonAsync("/detect", request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<Doods2HttpResponse>(ct).ConfigureAwait(false);
        return body?.Detections ?? Array.Empty<Doods2Detection>();
    }

    private async Task<IReadOnlyList<Doods2Detection>> DetectGrpcAsync(ReadOnlyMemory<byte> bytes, CancellationToken ct)
    {
        var grpcRequest = new DetectRequest
        {
            DetectorName = _opts.DetectorName,
            Data = ByteString.CopyFrom(bytes.Span),
        };
        grpcRequest.Detect.Add("*", (float)_opts.MinConfidence);

        var grpcResponse = await _grpcClient.DetectAsync(
            grpcRequest,
            deadline: DateTime.UtcNow.Add(_opts.Timeout),
            cancellationToken: ct).ConfigureAwait(false);

        // Map generated proto Detection types onto the shared Doods2Detection DTO.
        return grpcResponse.Detections
            .Select(d => new Doods2Detection(d.Top, d.Left, d.Bottom, d.Right, d.Label, d.Confidence))
            .ToList();
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
