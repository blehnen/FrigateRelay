using System.Globalization;
using FluentAssertions;
using FrigateRelay.Host;
using FrigateRelay.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrigateRelay.IntegrationTests;

/// <summary>
/// Integration test: /healthz 503 → 200 → 503 transition.
///
/// Asserts CONTEXT-10 D4 semantics end-to-end:
/// - Before MQTT connects: 503 with body containing mqttConnected=false
/// - After MQTT connects and ApplicationStarted fires: 200 with body status=Healthy
/// - After broker stops: 503 again within the reconnect-poll window
/// </summary>
[TestClass]
public sealed class HealthzReadinessTests
{
    [TestMethod]
    [Timeout(90_000)]
    public async Task Healthz_Transitions_503_To_200_To_503_On_BrokerStop()
    {
        // ── 1. Start Mosquitto ───────────────────────────────────────────────
        await using var mosquitto = new MosquittoFixture();
        await mosquitto.InitializeAsync().ConfigureAwait(false);

        // ── 2. Build WebApplication on ephemeral port ────────────────────────
        // ASPNETCORE_URLS=http://127.0.0.1:0 asks Kestrel to bind to a random
        // OS-assigned port. We capture it from IServerAddressesFeature after StartAsync.
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());

        // "urls" is the Kestrel configuration key read before binding — equivalent to
        // ASPNETCORE_URLS env var but injectable via in-memory config in tests.
        // Port 0 asks the OS for an ephemeral free port; captured via IServerAddressesFeature
        // after StartAsync.
        //
        // ValidateActions requires each subscription to declare Profile OR at least one Action
        // referencing a registered plugin. We provide a minimal BlueIris stub URL so the
        // BlueIris config section exists and the plugin registrar activates — no real server
        // needed since no events are published in this test.
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["urls"] = "http://127.0.0.1:0",
            ["FrigateMqtt:Server"] = mosquitto.Hostname,
            ["FrigateMqtt:Port"] = mosquitto.Port.ToString(CultureInfo.InvariantCulture),
            ["FrigateMqtt:Topic"] = "frigate/events",
            ["BlueIris:TriggerUrlTemplate"] = "http://127.0.0.1:1/admin?camera={camera}&trigger=1",
            ["BlueIris:RequestTimeout"] = "00:00:02",
            ["Subscriptions:0:Name"] = "HealthzTest",
            ["Subscriptions:0:Camera"] = "test",
            ["Subscriptions:0:Label"] = "person",
            ["Subscriptions:0:Actions:0:Plugin"] = "BlueIris",
        });

        // Suppress noisy logs during test; errors still surface.
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        // Wire all host services + /healthz endpoint (same path as Program.cs).
        HostBootstrap.ConfigureServices(builder);

        var app = builder.Build();
        HostBootstrap.ValidateStartup(app.Services);
        app.MapHealthChecks("/healthz", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            ResponseWriter = FrigateRelay.Host.Health.HealthzResponseWriter.WriteAsync,
        });

        await app.StartAsync().ConfigureAwait(false);

        // ── 3. Capture the bound ephemeral port ──────────────────────────────
        var server = app.Services.GetRequiredService<IServer>();
        var addressFeature = server.Features.Get<IServerAddressesFeature>();
        addressFeature.Should().NotBeNull("Kestrel must expose IServerAddressesFeature");

        var boundAddress = addressFeature!.Addresses.First();
        // boundAddress is e.g. "http://127.0.0.1:54321"
        var healthzUri = new Uri(new Uri(boundAddress), "/healthz");

        using var httpClient = new HttpClient();

        // ── 4. Assert 503 BEFORE MQTT connects ──────────────────────────────
        // Issue the first request immediately after StartAsync. The MQTT reconnect
        // loop runs as a background task and hasn't completed ConnectAsync yet.
        // Retry a few times (up to 500 ms) in case Kestrel is still binding.
        HttpResponseMessage? initialResponse = null;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                initialResponse = await httpClient.GetAsync(healthzUri).ConfigureAwait(false);
                break;
            }
            catch (HttpRequestException)
            {
                await Task.Delay(50).ConfigureAwait(false);
            }
        }

        initialResponse.Should().NotBeNull("Kestrel must be accepting connections after StartAsync");

        // The first call must be 503 — either MQTT hasn't connected yet OR
        // ApplicationStarted hasn't fired yet (in practice MQTT is the slow path).
        // If MQTT has already connected by the time this request lands (very fast
        // local loopback), we tolerate 200 here and proceed straight to the 200 assertion.
        // The critical assertion is the 503-AFTER-broker-stop at step 7.
        var initialStatus = (int)initialResponse!.StatusCode;
        (initialStatus == 503 || initialStatus == 200).Should().BeTrue(
            $"First /healthz must be 503 or 200, got {initialStatus}");

        if (initialStatus == 503)
        {
            var initialBody = await initialResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
            initialBody.Should().ContainAny("mqttConnected", "started",
                "503 body must name the failing check");
        }

        // ── 5. Poll until 200 (up to 10 seconds) ────────────────────────────
        const int maxPollIterations = 100;
        const int pollIntervalMs = 100;
        HttpResponseMessage? healthyResponse = null;

        for (var i = 0; i < maxPollIterations; i++)
        {
            var resp = await httpClient.GetAsync(healthzUri).ConfigureAwait(false);
            if ((int)resp.StatusCode == 200)
            {
                healthyResponse = resp;
                break;
            }
            await Task.Delay(pollIntervalMs).ConfigureAwait(false);
        }

        Assert.IsNotNull(healthyResponse,
            $"/healthz did not return 200 within {maxPollIterations * pollIntervalMs / 1000}s — " +
            "MQTT client never connected or ApplicationStarted never fired.");

        var healthyBody = await healthyResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
        healthyBody.Should().Contain("Healthy", "200 body must report status=Healthy");

        // ── 6. Stop the Mosquitto broker ─────────────────────────────────────
        // DisposeAsync stops the container. The MQTT reconnect loop will detect the
        // disconnect on the next TryPingAsync (every 5 seconds) and call SetConnected(false).
        await mosquitto.DisposeAsync().ConfigureAwait(false);

        // ── 7. Poll until 503 returns (up to 15 seconds) ────────────────────
        // The reconnect loop checks every 5s; allow 3 intervals plus buffer.
        const int maxStopIterations = 150;
        const int stopPollIntervalMs = 100;
        HttpResponseMessage? unhealthyResponse = null;

        for (var i = 0; i < maxStopIterations; i++)
        {
            HttpResponseMessage resp;
            try
            {
                resp = await httpClient.GetAsync(healthzUri).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                // Host itself hasn't stopped — transient connection issue, retry.
                await Task.Delay(stopPollIntervalMs).ConfigureAwait(false);
                continue;
            }

            if ((int)resp.StatusCode == 503)
            {
                unhealthyResponse = resp;
                break;
            }
            await Task.Delay(stopPollIntervalMs).ConfigureAwait(false);
        }

        Assert.IsNotNull(unhealthyResponse,
            $"/healthz did not return 503 within {maxStopIterations * stopPollIntervalMs / 1000}s " +
            "after broker stop — IMqttConnectionStatus.SetConnected(false) may not be wired.");

        var unhealthyBody = await unhealthyResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
        unhealthyBody.Should().ContainAny("Unhealthy", "mqttConnected",
            "503 body after broker stop must name the failed check");

        // ── 8. Stop the host ─────────────────────────────────────────────────
        await app.StopAsync().ConfigureAwait(false);
        await app.DisposeAsync().ConfigureAwait(false);
    }
}
