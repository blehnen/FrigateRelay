using System.Net;
using FrigateRelay.Abstractions;
using FrigateRelay.Plugins.BlueIris;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace FrigateRelay.Plugins.BlueIris.Tests;

[TestClass]
public sealed class BlueIrisSnapshotProviderTests
{
    // JPEG magic bytes — used to assert real bytes round-trip
    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF, 0xE0];

    private static EventContext MakeContext(string camera = "test_cam") =>
        new()
        {
            EventId = "evt-001",
            Camera = camera,
            Label = "person",
            StartedAt = DateTimeOffset.UtcNow,
            RawPayload = "{}",
            SnapshotFetcher = static _ => ValueTask.FromResult<byte[]?>(null),
        };

    private static (BlueIrisSnapshotProvider Sut, WireMockServer Server, CapturingLogger<BlueIrisSnapshotProvider> Logger)
        BuildSut(string pathTemplate = "/snapshot/{camera}")
    {
        var server = WireMockServer.Start();
        var baseUrl = server.Urls[0];
        var fullTemplate = $"{baseUrl}{pathTemplate}";

        var logger = new CapturingLogger<BlueIrisSnapshotProvider>();

        var services = new ServiceCollection();
        services.AddHttpClient("BlueIris")
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            });

        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IHttpClientFactory>();

        var snapshotTemplate = new BlueIrisSnapshotUrlTemplate(BlueIrisUrlTemplate.Parse(fullTemplate));

        var sut = new BlueIrisSnapshotProvider(factory, snapshotTemplate, logger);
        return (sut, server, logger);
    }

    [TestMethod]
    public async Task FetchAsync_200_ReturnsSnapshotResult()
    {
        var (sut, server, _) = BuildSut("/snapshot/{camera}");
        using (server)
        {
            server
                .Given(Request.Create().WithPath("/snapshot/test_cam").UsingGet())
                .RespondWith(Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "image/jpeg")
                    .WithBody(JpegMagic));

            var request = new SnapshotRequest { Context = MakeContext("test_cam") };
            var result = await sut.FetchAsync(request, CancellationToken.None);

            Assert.IsNotNull(result);
            CollectionAssert.AreEqual(JpegMagic, result.Bytes);
            Assert.AreEqual("image/jpeg", result.ContentType);
            Assert.AreEqual("BlueIris", result.ProviderName);
        }
    }

    [TestMethod]
    public async Task FetchAsync_404_ReturnsNull_AndLogsWarning()
    {
        var (sut, server, logger) = BuildSut("/snapshot/{camera}");
        using (server)
        {
            server
                .Given(Request.Create().WithPath("/snapshot/test_cam").UsingGet())
                .RespondWith(Response.Create().WithStatusCode(404));

            var request = new SnapshotRequest { Context = MakeContext("test_cam") };
            var result = await sut.FetchAsync(request, CancellationToken.None);

            Assert.IsNull(result);
            var warnings = logger.Entries.Where(e => e.Level == LogLevel.Warning).ToList();
            Assert.AreEqual(1, warnings.Count, "Expected exactly one Warning log entry.");
            StringAssert.Contains(warnings[0].Message, "404");
        }
    }

    [TestMethod]
    public async Task FetchAsync_NetworkError_ReturnsNull_AndLogsWarning()
    {
        // Use a port that was just freed (bind ephemeral, capture port, release) to simulate a
        // connection-refused network error. Avoids hard-coded ports that can collide with other
        // services on CI runners.
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var unusedPort = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var badTemplate = $"http://127.0.0.1:{unusedPort}/snapshot/{{camera}}";

        var logger = new CapturingLogger<BlueIrisSnapshotProvider>();

        var services = new ServiceCollection();
        services.AddHttpClient("BlueIris")
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                ConnectTimeout = TimeSpan.FromSeconds(2),
            });

        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IHttpClientFactory>();

        var snapshotTemplate = new BlueIrisSnapshotUrlTemplate(BlueIrisUrlTemplate.Parse(badTemplate));
        var sut = new BlueIrisSnapshotProvider(factory, snapshotTemplate, logger);

        var request = new SnapshotRequest { Context = MakeContext("test_cam") };
        var result = await sut.FetchAsync(request, CancellationToken.None);

        Assert.IsNull(result, "Expected null on network error.");
        var warnings = logger.Entries.Where(e => e.Level == LogLevel.Warning).ToList();
        Assert.IsTrue(warnings.Count >= 1, "Expected at least one Warning log entry on network error.");
    }

    [TestMethod]
    public async Task FetchAsync_ResolvesCameraToken_FromContext()
    {
        var (sut, server, _) = BuildSut("/image/{camera}");
        using (server)
        {
            server
                .Given(Request.Create().WithPath("/image/front_door").UsingGet())
                .RespondWith(Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "image/jpeg")
                    .WithBody(JpegMagic));

            var request = new SnapshotRequest { Context = MakeContext("front_door") };
            await sut.FetchAsync(request, CancellationToken.None);

            var matchedRequests = server.FindLogEntries(
                Request.Create().WithPath("/image/front_door").UsingGet());
            Assert.AreEqual(1, matchedRequests.Count, "WireMock should have received exactly 1 request to /image/front_door.");
        }
    }
}
