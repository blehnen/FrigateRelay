using FrigateRelay.Host;

var builder = Host.CreateApplicationBuilder(args);

// appsettings.Local.json is the optional developer override (.gitignored); all other
// config sources (appsettings.json, env vars, user-secrets, CLI args) are wired by
// CreateApplicationBuilder.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

HostBootstrap.ConfigureServices(builder);

var app = builder.Build();

// Fail-fast on unknown action plugin names (PROJECT.md S2 + CONTEXT-4 D2).
HostBootstrap.ValidateStartup(app.Services);

await app.RunAsync();
