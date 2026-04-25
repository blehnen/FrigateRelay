using FluentAssertions;
using FrigateRelay.Abstractions;
using FrigateRelay.Plugins.BlueIris;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Polly;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace FrigateRelay.Plugins.BlueIris.Tests;

[TestClass]
public class BlueIrisActionPluginTests
{
    private static EventContext NewCtx(string camera = "front", string label = "person",
        string eventId = "ev-1") => new()
    {
        EventId = eventId,
        Camera = camera,
        Label = label,
        Zones = Array.Empty<string>(),
        StartedAt = DateTimeOffset.UnixEpoch,
        RawPayload = "{}",
        SnapshotFetcher = static _ => ValueTask.FromResult<byte[]?>(null),
    };

    private static ServiceProvider BuildProviderViaRegistrar(string templateUrl, bool allowInvalidCerts = false)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BlueIris:TriggerUrlTemplate"] = templateUrl,
                ["BlueIris:AllowInvalidCertificates"] = allowInvalidCerts.ToString(),
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        var ctx = new PluginRegistrationContext(services, config);
        new PluginRegistrar().Register(ctx);
        return services.BuildServiceProvider();
    }

    // Builds a provider with a zero-delay retry handler for speed in tests 2 & 3.
    private static ServiceProvider BuildFastRetryProvider(string templateUrl)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BlueIris:TriggerUrlTemplate"] = templateUrl,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // Register options + template singleton same as registrar.
        services
            .AddOptions<BlueIrisOptions>()
            .Bind(config.GetSection("BlueIris"))
            .ValidateOnStart();

        services.AddSingleton(sp =>
            BlueIrisUrlTemplate.Parse(sp.GetRequiredService<IOptions<BlueIrisOptions>>().Value.TriggerUrlTemplate));

        // Zero-delay retry for test speed — ConfigurePrimaryHttpMessageHandler must chain off the
        // IHttpClientBuilder returned by AddHttpClient, not off the resilience builder.
        var httpClientBuilder = services.AddHttpClient("BlueIris");
        httpClientBuilder.AddResilienceHandler("BlueIris-retry", builder =>
        {
            builder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                DelayGenerator = static _ => ValueTask.FromResult<TimeSpan?>(TimeSpan.Zero),
            });
        });
        httpClientBuilder.ConfigurePrimaryHttpMessageHandler(_ => new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        });

        services.AddSingleton<BlueIrisActionPlugin>();
        services.AddSingleton<IActionPlugin>(sp => sp.GetRequiredService<BlueIrisActionPlugin>());

        return services.BuildServiceProvider();
    }

    [TestMethod]
    public async Task ExecuteAsync_HappyPath_FiresSingleGetWithResolvedUrl()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/admin").WithParam("camera", "front").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200));

        var templateUrl = $"{server.Urls[0]}/admin?camera={{camera}}&trigger=1";
        var sp = BuildProviderViaRegistrar(templateUrl);
        var plugin = sp.GetRequiredService<IActionPlugin>();

        await plugin.ExecuteAsync(NewCtx(camera: "front"), CancellationToken.None);

        server.LogEntries.Should().HaveCount(1);
        var requestMessage = server.LogEntries.Single().RequestMessage!;
        requestMessage.Path.Should().Be("/admin");
        requestMessage.Query.Should().NotBeNull().And.ContainKey("camera");
        requestMessage.Query!["camera"].Should().ContainSingle("front");
    }

    [TestMethod]
    public async Task ExecuteAsync_TransientFailure_RetriesAndSucceeds()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/admin").UsingGet())
            .InScenario("retry")
            .WillSetStateTo("attempt1")
            .RespondWith(Response.Create().WithStatusCode(503));

        server
            .Given(Request.Create().WithPath("/admin").UsingGet())
            .InScenario("retry")
            .WhenStateIs("attempt1")
            .WillSetStateTo("attempt2")
            .RespondWith(Response.Create().WithStatusCode(503));

        server
            .Given(Request.Create().WithPath("/admin").UsingGet())
            .InScenario("retry")
            .WhenStateIs("attempt2")
            .RespondWith(Response.Create().WithStatusCode(200));

        var templateUrl = $"{server.Urls[0]}/admin";
        var sp = BuildFastRetryProvider(templateUrl);
        var plugin = sp.GetRequiredService<IActionPlugin>();

        await plugin.ExecuteAsync(NewCtx(), CancellationToken.None);

        server.LogEntries.Should().HaveCount(3);
    }

    [TestMethod]
    public async Task ExecuteAsync_PersistentFailure_AfterAllRetries_ThrowsHttpRequestException()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/admin").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(503));

        var templateUrl = $"{server.Urls[0]}/admin";
        var sp = BuildFastRetryProvider(templateUrl);
        var plugin = sp.GetRequiredService<IActionPlugin>();

        var act = async () => await plugin.ExecuteAsync(NewCtx(), CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
        server.LogEntries.Should().HaveCount(4); // 1 initial + 3 retries
    }

    [TestMethod]
    public async Task Register_WithUnknownPlaceholderInTemplate_FailsAtStartup()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BlueIris:TriggerUrlTemplate"] = "https://x/{score}",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        var ctx = new PluginRegistrationContext(services, config);
        new PluginRegistrar().Register(ctx);

        // ValidateOnStart fires during IHost.StartAsync, not Build().
        var host = new HostBuilder()
            .ConfigureServices(s =>
            {
                foreach (var sd in services)
                    s.Add(sd);
            })
            .Build();

        var act = async () => await host.StartAsync();
        await act.Should().ThrowAsync<OptionsValidationException>();
    }

    [TestMethod]
    public async Task Register_WithMissingTemplate_FailsAtStartup()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        var ctx = new PluginRegistrationContext(services, config);
        new PluginRegistrar().Register(ctx);

        var host = new HostBuilder()
            .ConfigureServices(s =>
            {
                foreach (var sd in services)
                    s.Add(sd);
            })
            .Build();

        var act = async () => await host.StartAsync();
        await act.Should().ThrowAsync<OptionsValidationException>()
            .WithMessage("*BlueIris.TriggerUrlTemplate is required*");
    }

    [TestMethod]
    public void Register_AllowInvalidCertificatesTrue_PrimaryHandlerSkipsValidation()
    {
        // Use IHttpMessageHandlerFactory to get the raw handler chain (bypasses resilience wrapper).
        SocketsHttpHandler? BuildHandler(bool allowInvalidCerts)
        {
            var cfg = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["BlueIris:TriggerUrlTemplate"] = "http://localhost/admin",
                    ["BlueIris:AllowInvalidCertificates"] = allowInvalidCerts.ToString(),
                })
                .Build();
            var svc = new ServiceCollection();
            svc.AddLogging();
            new PluginRegistrar().Register(new PluginRegistrationContext(svc, cfg));
            var sp = svc.BuildServiceProvider();

            var handlerFactory = sp.GetRequiredService<IHttpMessageHandlerFactory>();
            var handler = handlerFactory.CreateHandler("BlueIris");

            HttpMessageHandler? current = handler;
            while (current is DelegatingHandler dh)
                current = dh.InnerHandler;
            return current as SocketsHttpHandler;
        }

        var handlerWithSkip = BuildHandler(true);
        var handlerWithoutSkip = BuildHandler(false);

        handlerWithSkip.Should().NotBeNull("SocketsHttpHandler must be the primary handler");
        handlerWithSkip!.SslOptions.RemoteCertificateValidationCallback.Should().NotBeNull(
            "AllowInvalidCertificates=true must set a custom validation callback");

        handlerWithoutSkip.Should().NotBeNull("SocketsHttpHandler must be the primary handler");
        handlerWithoutSkip!.SslOptions.RemoteCertificateValidationCallback.Should().BeNull(
            "AllowInvalidCertificates=false must leave callback null");
    }
}
