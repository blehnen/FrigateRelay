using FrigateRelay.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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

    private static readonly Action<ILogger, string, string, string, Exception?> LogDryRun =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Information,
            new EventId(203, "BlueIrisDryRun"),
            "BlueIris DryRun would-execute for camera={Camera} label={Label} event_id={EventId}");

    private readonly IHttpClientFactory _httpFactory;
    private readonly BlueIrisUrlTemplate _template;
    private readonly ILogger<BlueIrisActionPlugin> _logger;
    private readonly BlueIrisOptions _options;

    public BlueIrisActionPlugin(
        IHttpClientFactory httpFactory,
        BlueIrisUrlTemplate template,
        ILogger<BlueIrisActionPlugin> logger,
        IOptions<BlueIrisOptions> options)
    {
        _httpFactory = httpFactory;
        _template = template;
        _logger = logger;
        _options = options.Value;
    }

    public string Name => "BlueIris";

    public async Task ExecuteAsync(EventContext ctx, SnapshotContext snapshot, CancellationToken ct)
    {
        if (_options.DryRun)
        {
            LogDryRun(_logger, ctx.Camera, ctx.Label, ctx.EventId, null);
            return;
        }

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
