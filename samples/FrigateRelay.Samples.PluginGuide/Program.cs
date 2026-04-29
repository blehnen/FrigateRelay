using FrigateRelay.Abstractions;
using FrigateRelay.Samples.PluginGuide;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// -----------------------------------------------------------------------
// FrigateRelay.Samples.PluginGuide — "putting it together" entry point
//
// This program wires up the three sample plugin implementations via
// in-process DI (no full Generic Host) and exercises each contract once
// with a synthetic EventContext.  It is the live proof that the sample
// code compiles, resolves from DI, and runs without throwing.
//
// The docs.yml CI workflow runs `dotnet run --project samples/... --no-build`
// and treats a zero exit code as "samples healthy."
// -----------------------------------------------------------------------

var configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Sample:FetchSnapshot"] = "true",
    })
    .Build();

var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));

// Simulate how the host invokes plugin registrars at startup.
var registrar = new SamplePluginRegistrar();
registrar.Register(new PluginRegistrationContext(services, configuration));

var provider = services.BuildServiceProvider();

// Synthetic event — no live Frigate instance needed.
var ctx = new EventContext
{
    EventId = "guide-demo-001",
    Camera = "front-door",
    Label = "person",
    StartedAt = DateTimeOffset.UtcNow,
    RawPayload = "{}",
    SnapshotFetcher = static _ => ValueTask.FromResult<byte[]?>(null),
};

var logger = provider.GetRequiredService<ILogger<Program>>();

// --- IActionPlugin ---
var actionPlugin = provider.GetRequiredService<IActionPlugin>();
Log.RunningPlugin(logger, "IActionPlugin", actionPlugin.Name);
await actionPlugin.ExecuteAsync(ctx, default, CancellationToken.None).ConfigureAwait(false);

// --- IValidationPlugin (person → Pass) ---
var validationPlugin = provider.GetRequiredService<IValidationPlugin>();
Log.RunningValidator(logger, validationPlugin.Name, "person");
var pass = await validationPlugin.ValidateAsync(ctx, default, CancellationToken.None).ConfigureAwait(false);
if (!pass.Passed)
{
    Console.Error.WriteLine($"ERROR: validator expected Pass for label=person but got Fail({pass.Reason})");
    return 1;
}

// --- IValidationPlugin (car → Fail) ---
var carCtx = ctx with { Label = "car" };
Log.RunningValidator(logger, validationPlugin.Name, "car");
var fail = await validationPlugin.ValidateAsync(carCtx, default, CancellationToken.None).ConfigureAwait(false);
if (fail.Passed)
{
    Console.Error.WriteLine("ERROR: validator expected Fail for label=car but got Pass");
    return 1;
}

// --- ISnapshotProvider ---
var snapshotProvider = provider.GetRequiredService<ISnapshotProvider>();
Log.RunningProvider(logger, snapshotProvider.Name);
var request = new SnapshotRequest { Context = ctx };
var snapshot = await snapshotProvider.FetchAsync(request, CancellationToken.None).ConfigureAwait(false);
if (snapshot is null)
{
    Console.Error.WriteLine("ERROR: snapshot provider returned null but was expected to return a stub result");
    return 1;
}

Log.AllSucceeded(logger, snapshot.Bytes.Length);
return 0;

/// <summary>LoggerMessage delegates for the entry-point program.</summary>
internal static partial class Log
{
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Running {Contract} '{Name}'")]
    internal static partial void RunningPlugin(ILogger logger, string contract, string name);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Running IValidationPlugin '{Name}' with label={Label}")]
    internal static partial void RunningValidator(ILogger logger, string name, string label);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Running ISnapshotProvider '{Name}'")]
    internal static partial void RunningProvider(ILogger logger, string name);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "All sample plugins exercised successfully. snapshot bytes={Bytes}")]
    internal static partial void AllSucceeded(ILogger logger, int bytes);
}
