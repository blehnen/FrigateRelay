using FrigateRelay.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FrigateRelay.Host.Health;

/// <summary>
/// Reports the combined readiness of the host: returns <see cref="HealthCheckResult.Healthy"/>
/// only when both the MQTT client is connected AND the host has fully started
/// (all <see cref="IHostedService.StartAsync"/> calls have completed).
/// </summary>
/// <remarks>
/// Injected into the ASP.NET Core health-check pipeline via
/// <c>AddHealthChecks().AddCheck&lt;MqttHealthCheck&gt;("mqtt-and-startup")</c>
/// in <see cref="HostBootstrap"/>. The <c>/healthz</c> endpoint returns 503 until
/// both conditions hold; 200 thereafter. Used by Docker HEALTHCHECK and CI smoke tests.
/// </remarks>
internal sealed class MqttHealthCheck : IHealthCheck
{
    private readonly IMqttConnectionStatus _mqttStatus;
    private readonly IHostApplicationLifetime _lifetime;

    public MqttHealthCheck(IMqttConnectionStatus mqttStatus, IHostApplicationLifetime lifetime)
    {
        _mqttStatus = mqttStatus;
        _lifetime = lifetime;
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // IHostApplicationLifetime.ApplicationStarted fires once every IHostedService.StartAsync
        // has completed — the authoritative "host is running" signal.
        var started = _lifetime.ApplicationStarted.IsCancellationRequested;
        var mqttConnected = _mqttStatus.IsConnected;

        if (started && mqttConnected)
            return Task.FromResult(HealthCheckResult.Healthy("MQTT connected and host started."));

        var data = new Dictionary<string, object>
        {
            ["started"] = started,
            ["mqttConnected"] = mqttConnected,
        };

        return Task.FromResult(HealthCheckResult.Unhealthy(
            $"Not ready: started={started} mqttConnected={mqttConnected}",
            data: data));
    }
}
