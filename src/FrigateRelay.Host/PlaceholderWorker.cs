namespace FrigateRelay.Host;

/// <summary>
/// A no-op <see cref="BackgroundService"/> that logs "Host started" exactly once at Information level
/// and then awaits the stopping token.  It will be replaced by real workers in later phases.
/// </summary>
internal sealed class PlaceholderWorker : BackgroundService
{
    private static readonly Action<ILogger, Exception?> LogHostStarted =
        LoggerMessage.Define(LogLevel.Information, new EventId(1, "HostStarted"), "Host started");

    private readonly ILogger<PlaceholderWorker> _logger;

    public PlaceholderWorker(ILogger<PlaceholderWorker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogHostStarted(_logger, null);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown — swallow the cancellation so the host exits with code 0.
        }
    }
}
