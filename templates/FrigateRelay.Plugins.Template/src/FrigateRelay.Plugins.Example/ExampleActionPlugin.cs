using FrigateRelay.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FrigateRelay.Plugins.Example;

/// <summary>
/// Sample action plugin scaffolded by the FrigateRelay plugin template.
/// Replace the body of <see cref="ExecuteAsync"/> with your plugin's behavior.
/// </summary>
public sealed partial class ExampleActionPlugin : IActionPlugin
{
    private readonly ILogger<ExampleActionPlugin> _logger;
    private readonly ExampleOptions _options;

    public ExampleActionPlugin(ILogger<ExampleActionPlugin> logger, IOptions<ExampleOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public string Name => "Example";

    public Task ExecuteAsync(EventContext ctx, SnapshotContext snapshot, CancellationToken ct)
    {
        LogEventReceived(_logger, ctx.EventId, ctx.Camera);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Example plugin received event {EventId} for camera {Camera}")]
    private static partial void LogEventReceived(ILogger logger, string eventId, string camera);
}
