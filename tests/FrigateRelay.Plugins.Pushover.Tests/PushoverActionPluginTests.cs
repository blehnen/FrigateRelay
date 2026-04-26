using FluentAssertions;
using FrigateRelay.Abstractions;
using FrigateRelay.Plugins.Pushover;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;
using Polly;
using System.Text;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace FrigateRelay.Plugins.Pushover.Tests;

[TestClass]
public class PushoverActionPluginTests
{
    private const string SuccessJson = """{"status":1,"request":"req-123"}""";

    private static EventContext NewCtx(
        string camera = "front",
        string label = "person",
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

    /// <summary>
    /// Builds a ServiceProvider for the Pushover plugin pointed at the WireMock server URL,
    /// with zero-delay retry for test speed.
    /// </summary>
    private static ServiceProvider BuildFastRetryProvider(string baseAddress)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Pushover:AppToken"] = "test-app-token",
                ["Pushover:UserKey"] = "test-user-key",
                ["Pushover:MessageTemplate"] = "{label} detected on {camera}",
                ["Pushover:BaseAddress"] = baseAddress,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        var ctx = new PluginRegistrationContext(services, config);
        new PluginRegistrar().Register(ctx);

        // Override with zero-delay retry for test speed.
        // We must re-register the http client builder with zero delay AFTER the registrar.
        // The cleanest approach: build with standard registrar then override the named client.
        // Since AddResilienceHandler is additive, we use a separate fast-retry provider pattern.
        var sp = services.BuildServiceProvider();
        return sp;
    }

    /// <summary>
    /// Builds a ServiceProvider with zero-delay retries, using the registrar then patching
    /// the resilience pipeline via a fresh DI setup that mirrors the registrar but with zero delay.
    /// </summary>
    private static (ServiceProvider sp, CapturingLogger<PushoverActionPlugin> logger) BuildFastProvider(string baseAddress)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Pushover:AppToken"] = "test-app-token",
                ["Pushover:UserKey"] = "test-user-key",
                ["Pushover:MessageTemplate"] = "{label} detected on {camera}",
                ["Pushover:BaseAddress"] = baseAddress,
            })
            .Build();

        var services = new ServiceCollection();
        var capturer = new CapturingLogger<PushoverActionPlugin>();
        services.AddSingleton<ILogger<PushoverActionPlugin>>(capturer);

        services
            .AddOptions<PushoverOptions>()
            .Bind(config.GetSection("Pushover"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<PushoverOptions>, PushoverOptionsValidator>();

        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<PushoverOptions>>().Value;
            return EventTokenTemplate.Parse(opts.MessageTemplate, "Pushover.MessageTemplate");
        });

        var httpClientBuilder = services.AddHttpClient("Pushover", (sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<PushoverOptions>>().Value;
            client.BaseAddress = new Uri(opts.BaseAddress);
            client.Timeout = opts.RequestTimeout;
        });

        httpClientBuilder.AddResilienceHandler("pushover-retry", builder =>
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

        services.AddSingleton<PushoverActionPlugin>();
        services.AddSingleton<IActionPlugin>(sp => sp.GetRequiredService<PushoverActionPlugin>());

        return (services.BuildServiceProvider(), capturer);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 1: Happy path — posts token/user/message parts, logs success
    // ──────────────────────────────────────────────────────────────────────────
    [TestMethod]
    public async Task ExecuteAsync_HappyPath_PostsTokenUserMessageParts()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/1/messages.json").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(SuccessJson));

        var (sp, logger) = BuildFastProvider(server.Urls[0]);
        var plugin = sp.GetRequiredService<IActionPlugin>();
        var ctx = NewCtx(camera: "front", label: "person");

        await plugin.ExecuteAsync(ctx, default, CancellationToken.None);

        server.LogEntries.Should().HaveCount(1);
        var body = server.LogEntries.Single().RequestMessage!.Body!;
        body.Should().Contain("name=token");
        body.Should().Contain("test-app-token");
        body.Should().Contain("name=user");
        body.Should().Contain("test-user-key");
        body.Should().Contain("name=message");
        body.Should().Contain("person detected on front");

        logger.Entries.Should().Contain(e =>
            e.Id.Name == "PushoverSendSucceeded" && e.Level == Microsoft.Extensions.Logging.LogLevel.Information);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 2: Null snapshot — posts WITHOUT attachment part (fail-open)
    // ──────────────────────────────────────────────────────────────────────────
    [TestMethod]
    public async Task ExecuteAsync_NullSnapshot_PostsWithoutAttachment()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/1/messages.json").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(SuccessJson));

        var (sp, _) = BuildFastProvider(server.Urls[0]);
        var plugin = sp.GetRequiredService<IActionPlugin>();

        // default(SnapshotContext) returns null from ResolveAsync
        await plugin.ExecuteAsync(NewCtx(), default, CancellationToken.None);

        server.LogEntries.Should().HaveCount(1);
        var body = server.LogEntries.Single().RequestMessage!.Body!;
        body.Should().NotContain("name=\"attachment\"");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 3: With snapshot — attaches bytes + content-type part
    // ──────────────────────────────────────────────────────────────────────────
    [TestMethod]
    public async Task ExecuteAsync_WithSnapshot_AttachesBytesAndContentType()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/1/messages.json").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(SuccessJson));

        var (sp, _) = BuildFastProvider(server.Urls[0]);
        var plugin = sp.GetRequiredService<IActionPlugin>();

        var stubResolver = new StubResolver(new SnapshotResult
        {
            Bytes = [0xFF, 0xD8, 0xFF],
            ContentType = "image/jpeg",
            ProviderName = "Test",
        });

        var snapshotCtx = new SnapshotContext(stubResolver, null, null);

        await plugin.ExecuteAsync(NewCtx(), snapshotCtx, CancellationToken.None);

        server.LogEntries.Should().HaveCount(1);
        // Binary multipart body — WireMock captures it as bytes, not a string. Decode to string for
        // header-line assertions; binary blocks remain in the same buffer but won't fail UTF-8 decode.
        var bytes = server.LogEntries.Single().RequestMessage!.BodyAsBytes!;
        var body = System.Text.Encoding.UTF8.GetString(bytes);
        body.Should().Contain("name=attachment");
        body.Should().Contain("filename=snapshot.jpg");
        body.Should().Contain("Content-Type: image/jpeg");
        body.Should().Contain("name=attachment_type");
        var attachTypeIdx = body.IndexOf("name=attachment_type", StringComparison.Ordinal);
        body[attachTypeIdx..].Should().Contain("image/jpeg");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 4: status=0 body — throws HttpRequestException, logs pushover_send_failed
    // ──────────────────────────────────────────────────────────────────────────
    [TestMethod]
    public async Task ExecuteAsync_Status0Body_ThrowsAndLogs()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/1/messages.json").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody("""{"status":0,"errors":["bad token"]}"""));

        var (sp, logger) = BuildFastProvider(server.Urls[0]);
        var plugin = sp.GetRequiredService<IActionPlugin>();

        var act = async () => await plugin.ExecuteAsync(NewCtx(), default, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();

        logger.Entries.Should().Contain(e =>
            e.Id.Name == "PushoverSendFailed" &&
            e.Level == Microsoft.Extensions.Logging.LogLevel.Warning &&
            e.Message.Contains("bad token"));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 5: HTTP 4xx — NOT retried (Polly default skips 4xx)
    // ──────────────────────────────────────────────────────────────────────────
    [TestMethod]
    public async Task ExecuteAsync_Http4xx_NotRetried()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/1/messages.json").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(400)
                .WithBody("""{"status":0,"errors":["invalid token"]}"""));

        var (sp, _) = BuildFastProvider(server.Urls[0]);
        var plugin = sp.GetRequiredService<IActionPlugin>();

        var act = async () => await plugin.ExecuteAsync(NewCtx(), default, CancellationToken.None);
        await act.Should().ThrowAsync<HttpRequestException>();

        // 4xx is not retried — exactly 1 request
        server.LogEntries.Should().HaveCount(1);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 6: HTTP 5xx x3 then 200 — retried, succeeds, no exception
    // ──────────────────────────────────────────────────────────────────────────
    [TestMethod]
    public async Task ExecuteAsync_Http5xx_RetriedThenSucceeds()
    {
        using var server = WireMockServer.Start();

        server
            .Given(Request.Create().WithPath("/1/messages.json").UsingPost())
            .InScenario("retry5xx")
            .WillSetStateTo("attempt1")
            .RespondWith(Response.Create().WithStatusCode(500));

        server
            .Given(Request.Create().WithPath("/1/messages.json").UsingPost())
            .InScenario("retry5xx")
            .WhenStateIs("attempt1")
            .WillSetStateTo("attempt2")
            .RespondWith(Response.Create().WithStatusCode(500));

        server
            .Given(Request.Create().WithPath("/1/messages.json").UsingPost())
            .InScenario("retry5xx")
            .WhenStateIs("attempt2")
            .WillSetStateTo("attempt3")
            .RespondWith(Response.Create().WithStatusCode(500));

        server
            .Given(Request.Create().WithPath("/1/messages.json").UsingPost())
            .InScenario("retry5xx")
            .WhenStateIs("attempt3")
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(SuccessJson));

        var (sp, _) = BuildFastProvider(server.Urls[0]);
        var plugin = sp.GetRequiredService<IActionPlugin>();

        // Should not throw — succeeds on 4th attempt
        await plugin.ExecuteAsync(NewCtx(), default, CancellationToken.None);

        server.LogEntries.Should().HaveCount(4); // 1 initial + 3 retries
    }

    private sealed class StubResolver(SnapshotResult? toReturn) : ISnapshotResolver
    {
        public ValueTask<SnapshotResult?> ResolveAsync(EventContext context, string? perActionProviderName, string? subscriptionDefaultProviderName, CancellationToken cancellationToken) =>
            ValueTask.FromResult(toReturn);
    }
}
