using FrigateRelay.Host;
using FrigateRelay.Host.Health;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// appsettings.Local.json is the optional developer override (.gitignored); all other
// config sources (appsettings.json, env vars, user-secrets, CLI args) are wired by
// WebApplication.CreateBuilder. Kestrel listen URL is controlled via ASPNETCORE_URLS
// (default http://+:8080 in Docker — do NOT hard-code the port here).
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

HostBootstrap.ConfigureServices(builder);

var app = builder.Build();

// Fail-fast on unknown action plugin names (PROJECT.md S2 + CONTEXT-4 D2).
HostBootstrap.ValidateStartup(app.Services);

// /healthz: 200 when MQTT connected + ApplicationStarted; 503 otherwise (CONTEXT-10 D4).
// ResponseWriter serialises HealthReport to compact JSON (no HealthChecks.UI dependency).
app.MapHealthChecks("/healthz", new HealthCheckOptions
{
    ResponseWriter = HealthzResponseWriter.WriteAsync,
});

await app.RunAsync();
