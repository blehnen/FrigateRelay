using FrigateRelay.Abstractions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace FrigateRelay.Host.Snapshots;

/// <summary>
/// Host-internal implementation of <see cref="ISnapshotResolver"/>.
/// Applies a three-tier provider lookup (per-action → per-subscription → global default)
/// and caches successful results using a sliding-TTL <see cref="IMemoryCache"/>.
/// </summary>
internal sealed class SnapshotResolver : ISnapshotResolver
{
    private readonly Dictionary<string, ISnapshotProvider> _providers;
    private readonly IMemoryCache _cache;
    private readonly SnapshotResolverOptions _options;
    private readonly ILogger<SnapshotResolver> _logger;

    public SnapshotResolver(
        IEnumerable<ISnapshotProvider> providers,
        IMemoryCache cache,
        IOptions<SnapshotResolverOptions> options,
        ILogger<SnapshotResolver> logger)
    {
        _providers = providers.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<SnapshotResult?> ResolveAsync(
        EventContext context,
        string? perActionProviderName,
        string? subscriptionDefaultProviderName,
        CancellationToken cancellationToken)
    {
        // --- Tier resolution ---
        string? name;
        string tier;

        if (!string.IsNullOrEmpty(perActionProviderName))
        {
            name = perActionProviderName;
            tier = "per-action";
        }
        else if (!string.IsNullOrEmpty(subscriptionDefaultProviderName))
        {
            name = subscriptionDefaultProviderName;
            tier = "subscription";
        }
        else if (!string.IsNullOrEmpty(_options.DefaultProviderName))
        {
            name = _options.DefaultProviderName;
            tier = "global";
        }
        else
        {
            Log.SnapshotProviderUnresolved(_logger, context.EventId);
            return null;
        }

        // --- Provider lookup ---
        if (!_providers.TryGetValue(name, out var provider))
        {
            Log.SnapshotProviderUnknown(_logger, name);
            return null;
        }

        // --- Cache check ---
        var cacheKey = $"{name}:{context.EventId}";
        if (_cache.TryGetValue<SnapshotResult>(cacheKey, out var cached))
        {
            Log.SnapshotResolved(_logger, name, tier, context.EventId, "hit");
            return cached;
        }

        // --- Cache miss: fetch from provider ---
        var request = new SnapshotRequest
        {
            Context = context,
            ProviderName = name,
            IncludeBoundingBox = false,
        };

        var result = await provider.FetchAsync(request, cancellationToken).ConfigureAwait(false);

        if (result is not null)
        {
            var entryOptions = new MemoryCacheEntryOptions
            {
                SlidingExpiration = _options.CacheSlidingTtl,
            };
            _cache.Set(cacheKey, result, entryOptions);
            Log.SnapshotResolved(_logger, name, tier, context.EventId, "miss");
        }

        return result;
    }

    /// <summary>High-performance log helpers using <c>LoggerMessage.Define</c>.</summary>
    private static class Log
    {
        private static readonly Action<ILogger, string, Exception?> _snapshotProviderUnresolved =
            LoggerMessage.Define<string>(
                LogLevel.Warning,
                new EventId(1, "snapshot_provider_unresolved"),
                "snapshot_provider_unresolved event_id={EventId}");

        private static readonly Action<ILogger, string, Exception?> _snapshotProviderUnknown =
            LoggerMessage.Define<string>(
                LogLevel.Warning,
                new EventId(2, "snapshot_provider_unknown"),
                "snapshot_provider_unknown provider={ProviderName}");

        private static readonly Action<ILogger, string, string, string, string, Exception?> _snapshotResolved =
            LoggerMessage.Define<string, string, string, string>(
                LogLevel.Debug,
                new EventId(3, "snapshot_resolved"),
                "snapshot_resolved provider={ProviderName} tier={Tier} event_id={EventId} cache={CacheStatus}");

        public static void SnapshotProviderUnresolved(ILogger logger, string eventId) =>
            _snapshotProviderUnresolved(logger, eventId, null);

        public static void SnapshotProviderUnknown(ILogger logger, string providerName) =>
            _snapshotProviderUnknown(logger, providerName, null);

        public static void SnapshotResolved(ILogger logger, string providerName, string tier, string eventId, string cacheStatus) =>
            _snapshotResolved(logger, providerName, tier, eventId, cacheStatus, null);
    }
}
