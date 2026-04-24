# Phase 1 — Context & Decisions

Captured during `/shipyard:plan 1` discussion capture. These decisions narrow ambiguity in the Phase 1 scope from `.shipyard/ROADMAP.md` and shape the contracts every later phase consumes.

## Scope reminder (from ROADMAP.md Phase 1)

- `FrigateRelay.sln` + `Directory.Build.props` (.NET 10, nullable, warnings-as-errors, latest C#).
- `src/FrigateRelay.Abstractions/` — `IEventSource`, `IActionPlugin`, `IValidationPlugin`, `ISnapshotProvider`, `EventContext`, `Verdict`, `SnapshotRequest` / `SnapshotResult`, `IPluginRegistrar`, `PluginRegistrationContext`.
- `src/FrigateRelay.Host/` — `Host.CreateApplicationBuilder`, layered config (`appsettings.json` + env + user-secrets + `appsettings.Local.json`), no-op `BackgroundService`, DI composition root, `IPluginRegistrar` discovery loop.
- `tests/FrigateRelay.Abstractions.Tests/` + `tests/FrigateRelay.Host.Tests/` — MSTest v3 + Microsoft.Testing.Platform + FluentAssertions 6.12.2 + NSubstitute; ≥ 6 passing tests total.
- `.editorconfig`, `.gitignore`, `global.json`.

## Decisions

### D1 — `PluginRegistrationContext` carries both `IServiceCollection` and `IConfiguration`

Registrars receive a `PluginRegistrationContext` exposing:

- `IServiceCollection Services` — for DI registration.
- `IConfiguration Configuration` — so each plugin can bind its own options section (e.g. `FrigateMqtt`, `BlueIris`, `Pushover`) without the host having to know plugin-specific config shapes.

**Rejected:**
- *`IServiceCollection` only* — would force the host to know every plugin's config section names, defeating the separation goal.
- *Full `HostApplicationBuilder`* — too much surface area; risks plugins touching logging/hosting directly and blocks the future `AssemblyLoadContext` loader from running registrars in isolation.

**Implication for plan:** `PluginRegistrationContext` is a lightweight record/POCO (no services on it beyond `Services` + `Configuration`). Registrars should be pure (no side effects beyond `Services.AddXxx` calls). The host's discovery loop is: resolve all `IPluginRegistrar` → call `Register(context)` on each → build the service provider.

### D2 — Phase 1 uses Microsoft.Extensions.Logging console provider only

Serilog + sinks (Console, File, Seq) and OpenTelemetry are deferred to **Phase 9**, as the roadmap already specifies. Phase 1 wires nothing beyond the default M.E.L. logger that `Host.CreateApplicationBuilder` registers — the "Host started" log line required by Phase 1's success criteria flows through `ILogger<T>` to the default console provider.

**Rejected:**
- *Serilog wired now, sinks deferred* — mixes Phase 9 concerns into Phase 1 and creates a refactor when Phase 9 adds Serilog.Settings.Configuration layering.

**Implication for plan:** No Serilog package references in Phase 1. No `Log.Logger` static. All logging goes through injected `ILogger<T>`.

### D3 — `Verdict` uses static factory methods

```csharp
return Verdict.Pass();
return Verdict.Pass(score: 0.92);
return Verdict.Fail("confidence below 0.75");
```

`Verdict` is a `readonly record struct` (or `record`, to be decided by architect — tradeoff: struct avoids heap alloc per validation call but can't be the target of `with`-chains across assemblies without care) with private constructor plus static factories. The factories make invalid states unrepresentable — a passed verdict never carries a reason, a failed verdict always does.

**Rejected:**
- *Public record constructor* — allows `new Verdict(true, "rejected", null)` which is nonsense; pushes validation burden to every call site.

**Implication for plan:** `Verdict` gets its own file with factory methods. Tests assert invariants (e.g. `Verdict.Pass()` has no `Reason`; `Verdict.Fail("x")` carries `"x"`).

### D4 — `global.json` pins latest .NET 10 GA with `rollForward: latestFeature`

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature"
  }
}
```

(Exact GA version to be filled in by the architect at generation time — latest `10.0.x` band available on 2026-04-24.)

**Rejected:**
- *Pin exact + `rollForward: disable`* — contributors must match the SDK build byte-for-byte; too much friction for a hobby-scale OSS project.
- *`rollForward: major`* — no floor below .NET 10; doesn't buy anything over CSPROJ targeting `net10.0`.

**Implication for plan:** `global.json` at repo root. CI workflow (Phase 2) references it via `actions/setup-dotnet@v4`'s `global-json-file` input.

## Non-goals for Phase 1 (explicit)

- No MQTT client code. (Phase 3.)
- No `IActionDispatcher` / `Channel<T>` / Polly. (Phase 4.)
- No Serilog, OpenTelemetry, or metrics. (Phase 9.)
- No Docker, healthcheck endpoint, or CI workflows. (Phase 2 for CI, Phase 10 for Docker.)
- No concrete plugin projects. (Phase 3+ per roadmap dependency chain.)

## Success-criteria reminders (from ROADMAP.md Phase 1)

- `dotnet build FrigateRelay.sln -c Release` succeeds on Windows and WSL Linux, **zero warnings**.
- `dotnet run --project src/FrigateRelay.Host` logs `"Host started"` at Information and exits cleanly on SIGINT within 5 seconds.
- `dotnet test` reports **≥ 6** passing tests across both test projects.
- `FrigateRelay.Abstractions` references only `Microsoft.Extensions.*` — no third-party runtime deps (`dotnet list package --include-transitive`).
- `git grep ServicePointManager` returns zero results.
