using FrigateRelay.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FrigateRelay.Plugins.BlueIris;

/// <summary>
/// Fetches snapshots from Blue Iris by issuing an HTTP GET against a configured URL template.
/// Reuses the named HttpClient "BlueIris" (with TLS opt-in and Polly retry) already registered
/// by <see cref="PluginRegistrar"/>. Fail-open: non-2xx, network errors, and timeouts return
/// null and log a Warning rather than throwing (CONTEXT-5 D2).
/// </summary>
internal sealed class BlueIrisSnapshotProvider : ISnapshotProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly BlueIrisSnapshotUrlTemplate _snapshotTemplate;
    private readonly ILogger<BlueIrisSnapshotProvider> _logger;

    public BlueIrisSnapshotProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<BlueIrisOptions> options,
        BlueIrisSnapshotUrlTemplate snapshotTemplate,
        ILogger<BlueIrisSnapshotProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _ = options; // bound via DI; kept for potential future use
        _snapshotTemplate = snapshotTemplate;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "BlueIris";

    /// <inheritdoc />
    public async Task<SnapshotResult?> FetchAsync(SnapshotRequest request, CancellationToken ct)
    {
        var url = _snapshotTemplate.Template.Resolve(request.Context);
        var client = _httpClientFactory.CreateClient("BlueIris");

        try
        {
            using var resp = await client.GetAsync(url, ct);

            if (!resp.IsSuccessStatusCode)
            {
                Log.NonSuccess(_logger, (int)resp.StatusCode, url);
                return null;
            }

            var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
            var contentType = resp.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
            var etag = resp.Headers.ETag?.Tag;

            return new SnapshotResult
            {
                Bytes = bytes,
                ContentType = contentType,
                ProviderName = Name,
                ETag = etag,
            };
        }
        catch (HttpRequestException ex)
        {
            Log.NetworkError(_logger, ex, url);
            return null;
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // Timeout (not a graceful-shutdown cancellation) — fail-open per D2
            Log.Timeout(_logger, ex, url);
            return null;
        }
        // Cancellation (ct.IsCancellationRequested) propagates as OperationCanceledException — not caught here
    }

    private static class Log
    {
        private static readonly Action<ILogger, int, string, Exception?> _nonSuccess =
            LoggerMessage.Define<int, string>(
                LogLevel.Warning,
                new EventId(1, "blueiris_snapshot_non_success"),
                "bluiris_snapshot_non_success status={Status} url={Url}");

        private static readonly Action<ILogger, string, Exception?> _networkError =
            LoggerMessage.Define<string>(
                LogLevel.Warning,
                new EventId(2, "blueiris_snapshot_network_error"),
                "blueiris_snapshot_network_error url={Url}");

        private static readonly Action<ILogger, string, Exception?> _timeout =
            LoggerMessage.Define<string>(
                LogLevel.Warning,
                new EventId(3, "blueiris_snapshot_timeout"),
                "blueiris_snapshot_timeout url={Url}");

        public static void NonSuccess(ILogger logger, int status, string url) =>
            _nonSuccess(logger, status, url, null);

        public static void NetworkError(ILogger logger, Exception ex, string url) =>
            _networkError(logger, url, ex);

        public static void Timeout(ILogger logger, Exception ex, string url) =>
            _timeout(logger, url, ex);
    }
}
