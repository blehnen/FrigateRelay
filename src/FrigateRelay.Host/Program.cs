// Design note — registrar discovery shape:
// The plan considered two approaches:
//   A) Reflect over ServiceDescriptors after each AddPluginRegistrar<T>() call and instantiate
//      any ImplementationType via Activator.CreateInstance at composition time.
//   B) Keep an explicit, typed list of IPluginRegistrar instances and pass them to
//      PluginRegistrarRunner.RunAll() before builder.Build().
//
// Approach B is used here.  In Phase 1 there are no concrete plugins yet, so the list is empty;
// future phases add their IPluginRegistrar instances to the list and the loop picks them up
// automatically.  Approach B avoids runtime reflection over the ServiceDescriptor graph and is
// fully AOT-compatible.

using FrigateRelay.Abstractions;
using FrigateRelay.Host;

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

// Construct the shared registrar context — all registrars receive the same instance.
// In Phase 1 no concrete plugins exist; the list is empty. Phase 3+ plugins add their
// IPluginRegistrar instances here (or via a future AddPluginRegistrar helper).
var registrationContext = new PluginRegistrationContext(builder.Services, builder.Configuration);
IEnumerable<IPluginRegistrar> registrars = [];

builder.Services.AddHostedService<PlaceholderWorker>();

var app = builder.Build();

// Run registrars AFTER Build() so we can pull ILogger from the built host's DI
// instead of spinning a throwaway LoggerFactory. Registration mutates
// builder.Services (captured by reference in registrationContext); this still
// happens before RunAsync starts the host loop, so hosted services see the full
// service graph.
PluginRegistrarRunner.RunAll(
    registrars,
    registrationContext,
    app.Services.GetRequiredService<ILogger<IPluginRegistrar>>());

await app.RunAsync();
