using FrigateRelay.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FrigateRelay.Plugins.Pushover;

// Stub — full implementation added in Task 2.
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

    public Task ExecuteAsync(EventContext ctx, SnapshotContext snapshot, CancellationToken ct)
        => Task.CompletedTask;
}
