using FrigateRelay.Abstractions;
using Microsoft.Extensions.Logging;

namespace FrigateRelay.Plugins.BlueIris;

internal sealed class BlueIrisActionPlugin : IActionPlugin
{
    private static readonly Action<ILogger, string, string, Exception?> LogTriggerSuccess =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(201, "BlueIrisTriggerSuccess"),
            "BlueIris trigger fired event_id={EventId} url={Url}");

    private static readonly Action<ILogger, string, string, Exception?> LogTriggerFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(202, "BlueIrisTriggerFailed"),
            "BlueIris trigger failed event_id={EventId} url={Url}");

    private readonly IHttpClientFactory _httpFactory;
    private readonly BlueIrisUrlTemplate _template;
    private readonly ILogger<BlueIrisActionPlugin> _logger;

    public BlueIrisActionPlugin(
        IHttpClientFactory httpFactory,
        BlueIrisUrlTemplate template,
        ILogger<BlueIrisActionPlugin> logger)
    {
        _httpFactory = httpFactory;
        _template = template;
        _logger = logger;
    }

    public string Name => "BlueIris";

    public async Task ExecuteAsync(EventContext ctx, CancellationToken ct)
    {
        var url = _template.Resolve(ctx);
        using var client = _httpFactory.CreateClient("BlueIris");

        try
        {
            using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            LogTriggerSuccess(_logger, ctx.EventId, url, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            LogTriggerFailed(_logger, ctx.EventId, url, ex);
            throw;
        }
    }
}
