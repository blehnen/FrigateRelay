namespace FrigateRelay.Host.Dispatch;

/// <summary>
/// Configuration options for <see cref="ChannelActionDispatcher"/>.
/// Bind from the <c>"Dispatcher"</c> configuration section to override defaults.
/// </summary>
public sealed record DispatcherOptions
{
    /// <summary>
    /// Maximum number of pending <see cref="DispatchItem"/> entries per plugin channel.
    /// When the channel is full the oldest item is evicted (drop-oldest) and the
    /// <c>frigaterelay.dispatch.drops</c> counter is incremented (CONTEXT-4 D5, D6).
    /// Default: 256.
    /// </summary>
    public int DefaultQueueCapacity { get; init; } = 256;

    /// <summary>
    /// Per-plugin channel capacity overrides, keyed by plugin name (OrdinalIgnoreCase).
    /// When a plugin's name appears here its channel is created with this capacity instead of
    /// <see cref="DefaultQueueCapacity"/>. Plugin registrars populate this via
    /// <c>IOptions&lt;DispatcherOptions&gt;</c> post-configuration.
    /// </summary>
    public IReadOnlyDictionary<string, int> PerPluginQueueCapacity { get; init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
}
