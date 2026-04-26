using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FrigateRelay.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FrigateRelay.Plugins.Pushover;

internal sealed class PushoverActionPlugin : IActionPlugin
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IOptions<PushoverOptions> _options;
    private readonly EventTokenTemplate _template;
    private readonly ILogger<PushoverActionPlugin> _logger;

    public PushoverActionPlugin(
        IHttpClientFactory httpFactory,
        IOptions<PushoverOptions> options,
        EventTokenTemplate template,
        ILogger<PushoverActionPlugin> logger)
    {
        _httpFactory = httpFactory;
        _options = options;
        _template = template;
        _logger = logger;
    }

    public string Name => "Pushover";

    public async Task ExecuteAsync(EventContext ctx, SnapshotContext snapshot, CancellationToken cancellationToken)
    {
        var opts = _options.Value;
        var client = _httpFactory.CreateClient("Pushover");

        var snapshotResult = await snapshot.ResolveAsync(ctx, cancellationToken).ConfigureAwait(false);
        if (snapshotResult is null)
        {
            Log.SnapshotUnavailable(_logger, ctx.EventId);
        }

        var message = _template.Resolve(ctx, urlEncode: false);

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(opts.AppToken), "token");
        content.Add(new StringContent(opts.UserKey), "user");
        content.Add(new StringContent(message), "message");
        content.Add(new StringContent(opts.Priority.ToString(CultureInfo.InvariantCulture)), "priority");

        if (!string.IsNullOrEmpty(opts.Title))
        {
            content.Add(new StringContent(opts.Title), "title");
        }

        if (snapshotResult is not null)
        {
            var attachment = new ByteArrayContent(snapshotResult.Bytes);
            attachment.Headers.ContentType = new MediaTypeHeaderValue(snapshotResult.ContentType);
            content.Add(attachment, "attachment", "snapshot.jpg");
            content.Add(new StringContent(snapshotResult.ContentType), "attachment_type");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/1/messages.json")
        {
            Content = content,
        };

        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            // Polly's retry policy already exhausted (for 5xx) or skipped (for 4xx).
            var errorBody = await TryReadErrorBodyAsync(response, cancellationToken).ConfigureAwait(false);
            Log.SendFailed(_logger, ctx.EventId, (int)response.StatusCode, errorBody);
            response.EnsureSuccessStatusCode(); // throws HttpRequestException
        }

        // Pushover returns 200 even for some application-level failures — body has status=0.
        var body = await response.Content.ReadFromJsonAsync<PushoverResponse>(cancellationToken).ConfigureAwait(false);
        if (body is null || body.Status != 1)
        {
            var errors = body?.Errors is null ? "(no detail)" : string.Join(", ", body.Errors);
            Log.SendFailed(_logger, ctx.EventId, 200, errors);
            throw new HttpRequestException($"Pushover returned status=0: {errors}");
        }

        Log.SendSucceeded(_logger, ctx.EventId);
    }

    private static async Task<string> TryReadErrorBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<PushoverResponse>(ct).ConfigureAwait(false);
            return body?.Errors is null ? "(no detail)" : string.Join(", ", body.Errors);
        }
        catch
        {
            return "(unparseable)";
        }
    }

    private static class Log
    {
        private static readonly Action<ILogger, string, Exception?> _sendSucceeded =
            LoggerMessage.Define<string>(
                LogLevel.Information,
                new EventId(1, "PushoverSendSucceeded"),
                "Pushover notification sent for event_id={EventId}");

        private static readonly Action<ILogger, string, int, string, Exception?> _sendFailed =
            LoggerMessage.Define<string, int, string>(
                LogLevel.Warning,
                new EventId(2, "PushoverSendFailed"),
                "Pushover send failed for event_id={EventId} http_status={Status}: {Errors}");

        private static readonly Action<ILogger, string, Exception?> _snapshotUnavailable =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(3, "pushover_snapshot_unavailable"),
                "pushover_snapshot_unavailable event_id={EventId}");

        public static void SendSucceeded(ILogger logger, string eventId) =>
            _sendSucceeded(logger, eventId, null);

        public static void SendFailed(ILogger logger, string eventId, int status, string errors) =>
            _sendFailed(logger, eventId, status, errors, null);

        public static void SnapshotUnavailable(ILogger logger, string eventId) =>
            _snapshotUnavailable(logger, eventId, null);
    }
}
