using FrigateRelay.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FrigateRelay.Samples.PluginGuide;

/// <summary>
/// Demonstrates a minimal <see cref="IActionPlugin"/> implementation.
/// This plugin logs the event and, when configured to do so, resolves
/// a snapshot via the pre-wired <see cref="SnapshotContext"/> parameter.
/// </summary>
/// <remarks>
/// Snapshot-consuming pattern: call <see cref="SnapshotContext.ResolveAsync"/> to obtain image
/// bytes. Plugins that do not need snapshot images (e.g. camera-trigger plugins like BlueIris)
/// accept the snapshot parameter and ignore it — no compile-time opt-out exists.
/// </remarks>
public sealed partial class SampleActionPlugin : IActionPlugin
{
    private readonly ILogger<SampleActionPlugin> _logger;
    private readonly SamplePluginOptions _options;

    /// <summary>
    /// Initializes a new instance of <see cref="SampleActionPlugin"/>.
    /// </summary>
    /// <param name="logger">The logger provided by the host DI container.</param>
    /// <param name="options">Bound configuration options for this plugin.</param>
    public SampleActionPlugin(
        ILogger<SampleActionPlugin> logger,
        IOptions<SamplePluginOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    /// <inheritdoc />
    public string Name => "Sample";

    /// <inheritdoc />
    public async Task ExecuteAsync(
        EventContext ctx,
        SnapshotContext snapshot,
        CancellationToken ct)
    {
        LogEventReceived(_logger, ctx.EventId, ctx.Camera, ctx.Label);

        if (_options.FetchSnapshot)
        {
            // Resolve the snapshot via the three-tier resolver pre-wired by the dispatcher.
            // Calling ResolveAsync on default(SnapshotContext) is safe — it returns null.
            var result = await snapshot.ResolveAsync(ctx, ct).ConfigureAwait(false);
            if (result is not null)
            {
                LogSnapshotReceived(_logger, ctx.EventId, result.Bytes.Length, result.ContentType);
            }
            else
            {
                LogSnapshotUnavailable(_logger, ctx.EventId);
            }
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "sample_action_plugin event_id={EventId} camera={Camera} label={Label}")]
    private static partial void LogEventReceived(
        ILogger logger, string eventId, string camera, string label);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "sample_action_plugin snapshot_received event_id={EventId} bytes={Bytes} content_type={ContentType}")]
    private static partial void LogSnapshotReceived(
        ILogger logger, string eventId, int bytes, string contentType);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "sample_action_plugin snapshot_unavailable event_id={EventId}")]
    private static partial void LogSnapshotUnavailable(ILogger logger, string eventId);
}
