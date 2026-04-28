using FrigateRelay.Abstractions;
using Microsoft.Extensions.Logging;

namespace FrigateRelay.Samples.PluginGuide;

/// <summary>
/// Demonstrates a minimal <see cref="ISnapshotProvider"/> implementation.
/// Returns a stub four-byte payload tagged with the request's event id rather
/// than performing a real HTTP fetch.
/// </summary>
/// <remarks>
/// <para>
/// Snapshot providers are resolved by the dispatcher using a three-tier lookup:
/// <list type="number">
///   <item>Per-action provider name override (in <c>Subscriptions:N:Actions</c>)</item>
///   <item>Per-subscription default provider name (in <c>Subscriptions:N:SnapshotProvider</c>)</item>
///   <item>Global <c>DefaultSnapshotProvider</c> from host options</item>
/// </list>
/// </para>
/// <para>
/// Providers should be fail-open: return <see langword="null"/> on network errors or
/// timeouts rather than throwing — the dispatcher treats <see langword="null"/> as
/// "no snapshot available" and continues dispatching the action without image data.
/// </para>
/// </remarks>
public sealed partial class SampleSnapshotProvider : ISnapshotProvider
{
    private readonly ILogger<SampleSnapshotProvider> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="SampleSnapshotProvider"/>.
    /// </summary>
    /// <param name="logger">The logger provided by the host DI container.</param>
    public SampleSnapshotProvider(ILogger<SampleSnapshotProvider> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "SampleSnapshot";

    /// <inheritdoc />
    public Task<SnapshotResult?> FetchAsync(SnapshotRequest request, CancellationToken ct)
    {
        // Stub: return four zero bytes to prove the contract without a real HTTP call.
        // In a real provider: use IHttpClientFactory to create a named HttpClient and
        // fetch the image from the camera or NVR endpoint. Return null on failure (fail-open).
        LogFetching(_logger, request.Context.EventId);

        var result = new SnapshotResult
        {
            Bytes = [0x00, 0x00, 0x00, 0x00],
            ContentType = "image/jpeg",
            ProviderName = Name,
        };

        return Task.FromResult<SnapshotResult?>(result);
    }

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "sample_snapshot_provider fetch event_id={EventId}")]
    private static partial void LogFetching(ILogger logger, string eventId);
}
