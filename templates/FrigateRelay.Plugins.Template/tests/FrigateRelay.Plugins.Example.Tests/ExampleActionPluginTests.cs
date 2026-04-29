using FluentAssertions;
using FrigateRelay.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FrigateRelay.Plugins.Example.Tests;

[TestClass]
public sealed class ExampleActionPluginTests
{
    [TestMethod]
    public async Task ExecuteAsync_LogsAndReturnsCompletedTask()
    {
        var plugin = new ExampleActionPlugin(
            NullLogger<ExampleActionPlugin>.Instance,
            Options.Create(new ExampleOptions()));

        plugin.Name.Should().Be("Example");

        var ctx = new EventContext
        {
            EventId = "ev-1",
            Camera = "cam-1",
            Label = "person",
            RawPayload = "{}",
            StartedAt = DateTimeOffset.UtcNow,
            SnapshotFetcher = _ => ValueTask.FromResult<byte[]?>(null),
        };

        await plugin.ExecuteAsync(ctx, default, CancellationToken.None);
        // Reaching here without throwing verifies the plugin contract is satisfied and
        // the implementation handles a default SnapshotContext safely.
    }
}
