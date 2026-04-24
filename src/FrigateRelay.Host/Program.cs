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

// --- Plugin registrar discovery (composition time, before Build()) ---
// Construct the shared context once; all registrars receive the same instance.
var registrationContext = new PluginRegistrationContext(builder.Services, builder.Configuration);

// In Phase 1 no concrete plugins exist; the list is empty.
// Phase 3+ plugins add their IPluginRegistrar here (or via a dedicated AddPluginRegistrar helper).
IEnumerable<IPluginRegistrar> registrars = [];

// Create a bootstrap logger for the pre-Build() registration phase.
// Use a category name string since PluginRegistrarRunner is a static class
// and cannot be used as a type argument.
using var bootstrapLoggerFactory = LoggerFactory.Create(lb => lb.AddConsole());
var bootstrapLogger = bootstrapLoggerFactory.CreateLogger("FrigateRelay.Host.PluginRegistrarRunner");

PluginRegistrarRunner.RunAll(registrars, registrationContext, bootstrapLogger);

// --- Host services ---
builder.Services.AddHostedService<PlaceholderWorker>();

var app = builder.Build();
await app.RunAsync();
