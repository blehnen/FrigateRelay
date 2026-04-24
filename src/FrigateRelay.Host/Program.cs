using FrigateRelay.Abstractions;
using FrigateRelay.Host;
using FrigateRelay.Host.Configuration;
using FrigateRelay.Host.Matching;
using Microsoft.Extensions.Caching.Memory;

var builder = Host.CreateApplicationBuilder(args);

// Layered config — order matters (last writer wins):
//   1. appsettings.json          (committed defaults — wired by CreateApplicationBuilder)
//   2. appsettings.{Env}.json    (wired by CreateApplicationBuilder)
//   3. appsettings.Local.json    (developer override — optional, .gitignored)
//   4. Environment variables     (wired by CreateApplicationBuilder)
//   5. User-secrets              (Development only — wired by CreateApplicationBuilder)
//   6. Command-line args         (wired by CreateApplicationBuilder)
//
// Only #3 needs an explicit AddJsonFile call; the rest are provided by CreateApplicationBuilder.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

// ── Host-scope services (matcher uses no DI; register the cache, dedupe, options binding) ──
// Top-level "Subscriptions" section binds to HostSubscriptionsOptions.Subscriptions.
// This matches Phase 8's Profiles+Subscriptions shape (subscriptions are host-level, not plugin-level).
builder.Services.AddOptions<HostSubscriptionsOptions>()
    .Bind(builder.Configuration);

builder.Services.AddSingleton<IMemoryCache>(new MemoryCache(new MemoryCacheOptions()));
builder.Services.AddSingleton<DedupeCache>();
builder.Services.AddHostedService<EventPump>();

// ── Plugin registrars (Approach B: explicit list; no reflection) ──
// Registrars MUST run BEFORE builder.Build() so their service additions reach the built provider.
// For the bootstrap logger we use a minimal LoggerFactory — the built host's logging
// isn't available yet. One allocation, disposed right after; acceptable ceremony.
var registrationContext = new PluginRegistrationContext(builder.Services, builder.Configuration);
IEnumerable<IPluginRegistrar> registrars =
[
    new FrigateRelay.Sources.FrigateMqtt.PluginRegistrar(),
];

using (var bootstrapLoggerFactory = LoggerFactory.Create(lb => lb.AddConsole()))
{
    var bootstrapLogger = bootstrapLoggerFactory.CreateLogger<IPluginRegistrar>();
    PluginRegistrarRunner.RunAll(registrars, registrationContext, bootstrapLogger);
}

var app = builder.Build();
await app.RunAsync();
