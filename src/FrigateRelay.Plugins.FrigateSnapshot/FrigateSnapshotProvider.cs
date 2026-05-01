using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FrigateRelay.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FrigateRelay.Plugins.FrigateSnapshot;

/// <summary>
/// Fetches event snapshots directly from a Frigate NVR instance via its REST API.
/// </summary>
/// <remarks>
/// Fail-open per D2: transport errors and non-2xx responses are logged at Warning and return
/// <see langword="null"/> — the action pipeline continues without a snapshot rather than failing.
/// </remarks>
internal sealed class FrigateSnapshotProvider : ISnapshotProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly FrigateSnapshotOptions _options;
    private readonly ILogger<FrigateSnapshotProvider> _logger;

    public FrigateSnapshotProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<FrigateSnapshotOptions> options,
        ILogger<FrigateSnapshotProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Frigate";

    /// <inheritdoc />
    public async Task<SnapshotResult?> FetchAsync(SnapshotRequest request, CancellationToken ct)
    {
        var eventId = request.Context.EventId;
        var encodedId = Uri.EscapeDataString(eventId);
        var path = _options.UseThumbnail
            ? $"/api/events/{encodedId}/thumbnail.jpg"
            : $"/api/events/{encodedId}/snapshot.jpg";

        var url = BuildUrl(path, request);

        var client = _httpClientFactory.CreateClient("FrigateSnapshot");
        int retriesRemaining = _options.Retry404Count;

        try
        {
            while (true)
            {
                using var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);
                if (_options.ApiToken.Length > 0)
                {
                    httpRequest.Headers.Authorization =
                        new AuthenticationHeaderValue("Bearer", _options.ApiToken);
                }

                var response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);

                if (response.StatusCode == HttpStatusCode.NotFound && retriesRemaining > 0)
                {
                    retriesRemaining--;
                    Log.Snapshot404Retry(_logger, eventId, _options.Retry404Count - retriesRemaining);
                    response.Dispose();
                    await Task.Delay(_options.Retry404Delay, ct);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    Log.SnapshotFailed(_logger, eventId, (int)response.StatusCode);
                    return null;
                }

                var bytes = await response.Content.ReadAsByteArrayAsync(ct);
                var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
                var etag = response.Headers.ETag?.Tag;

                Log.SnapshotSuccess(_logger, eventId, bytes.Length);

                return new SnapshotResult
                {
                    Bytes = bytes,
                    ContentType = contentType,
                    ProviderName = Name,
                    ETag = etag,
                };
            }
        }
        catch (HttpRequestException ex)
        {
            Log.SnapshotFailed(_logger, eventId, ex.Message);
            return null;
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            Log.SnapshotFailed(_logger, eventId, ex.Message);
            return null;
        }
    }

    private string BuildUrl(string path, SnapshotRequest request)
    {
        // Determine effective bounding-box setting: request override OR options default.
        var includeBbox = request.IncludeBoundingBox || _options.IncludeBoundingBox;

        var hasQuery = includeBbox
            || _options.IncludeTimestamp
            || _options.Crop
            || _options.Quality.HasValue
            || _options.Height.HasValue;

        if (!hasQuery)
            return path;

        var sb = new StringBuilder(path);
        sb.Append('?');
        var first = true;

        void Append(string key, string value)
        {
            if (!first) sb.Append('&');
            sb.Append(Uri.EscapeDataString(key));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(value));
            first = false;
        }

        if (includeBbox) Append("bbox", "1");
        if (_options.IncludeTimestamp) Append("timestamp", "1");
        if (_options.Crop) Append("crop", "1");
        if (_options.Quality.HasValue) Append("quality", _options.Quality.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (_options.Height.HasValue) Append("h", _options.Height.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return sb.ToString();
    }

    private static class Log
    {
        private static readonly Action<ILogger, string, int, Exception?> _snapshotSuccess =
            LoggerMessage.Define<string, int>(
                LogLevel.Debug,
                new EventId(1, "frigate_snapshot_success"),
                "Frigate snapshot fetched for event {FrigateEventId}: {Bytes} bytes");

        private static readonly Action<ILogger, string, int, Exception?> _snapshot404Retry =
            LoggerMessage.Define<string, int>(
                LogLevel.Debug,
                new EventId(2, "frigate_snapshot_404_retry"),
                "Frigate snapshot 404 for event {FrigateEventId}; retry attempt {Attempt}");

        private static readonly Action<ILogger, string, string, Exception?> _snapshotFailedMessage =
            LoggerMessage.Define<string, string>(
                LogLevel.Warning,
                new EventId(4, "frigate_snapshot_failed"),
                "Frigate snapshot fetch failed for event {FrigateEventId}: {Reason}");

        private static readonly Action<ILogger, string, int, Exception?> _snapshotFailedStatus =
            LoggerMessage.Define<string, int>(
                LogLevel.Warning,
                new EventId(3, "frigate_snapshot_failed"),
                "Frigate snapshot fetch failed for event {FrigateEventId}: HTTP {StatusCode}");

        public static void SnapshotSuccess(ILogger logger, string eventId, int byteCount)
            => _snapshotSuccess(logger, eventId, byteCount, null);

        public static void Snapshot404Retry(ILogger logger, string eventId, int attempt)
            => _snapshot404Retry(logger, eventId, attempt, null);

        public static void SnapshotFailed(ILogger logger, string eventId, int statusCode)
            => _snapshotFailedStatus(logger, eventId, statusCode, null);

        public static void SnapshotFailed(ILogger logger, string eventId, string reason)
            => _snapshotFailedMessage(logger, eventId, reason, null);
    }
}
