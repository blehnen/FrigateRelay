using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FrigateRelay.Host.Health;

/// <summary>
/// Writes a <see cref="HealthReport"/> as compact JSON to the HTTP response.
/// Used as the <c>ResponseWriter</c> for the <c>/healthz</c>
/// <see cref="Microsoft.AspNetCore.Builder.HealthCheckEndpointRouteBuilderExtensions.MapHealthChecks(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder, string)"/>
/// endpoint.
/// </summary>
/// <remarks>
/// Output format (machine-consumed only — no UI dependency):
/// <code>
/// {
///   "status": "Healthy" | "Unhealthy" | "Degraded",
///   "checks": [
///     { "name": "mqtt-and-startup", "status": "Unhealthy", "data": { "started": false, "mqttConnected": false } }
///   ]
/// }
/// </code>
/// </remarks>
internal static class HealthzResponseWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static async Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                data = e.Value.Data.Count > 0
                    ? (object)e.Value.Data
                    : (object)new { },
            }),
        };

        await JsonSerializer.SerializeAsync(context.Response.Body, payload, JsonOptions)
            .ConfigureAwait(false);
    }
}
