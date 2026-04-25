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
}
