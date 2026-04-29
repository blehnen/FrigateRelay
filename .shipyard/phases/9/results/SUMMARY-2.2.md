# Build Summary: Plan 2.2 — Host Wiring: Serilog + OpenTelemetry + StartupValidation

## Status: complete

Build clean (0 warnings / 0 errors). 69/69 tests pass.

## Tasks Completed

| Task | Commit | Description |
|------|--------|-------------|
| Task 1 — appsettings.json | `c82dd83` | `Serilog` + `Otel` sections with empty-string defaults |
| Task 2a — Serilog | `d6da64d` | `AddSerilog` in HostBootstrap with Console + File + conditional Seq |
| Task 2b — OpenTelemetry | `9fe5274` | `AddOpenTelemetry` with conditional `AddOtlpExporter` + `AddRuntimeInstrumentation` |
| Task 3 — ValidateObservability | `c7ee4d1` | `ValidateObservability` in StartupValidation, wired in `ValidateAll` pass 0 |

## Files Modified

- `src/FrigateRelay.Host/appsettings.json` — `Serilog` and `Otel` sections.
- `src/FrigateRelay.Host/HostBootstrap.cs` — `AddSerilog` + `AddOpenTelemetry` registration.
- `src/FrigateRelay.Host/StartupValidation.cs` — new `ValidateObservability` method + wiring in `ValidateAll` pass 0.

## Decisions Made

- **`AddSerilog`, not `UseSerilog`** — `HostApplicationBuilder` in .NET 10 does not expose `.Host` as `IHostBuilder`. `builder.Services.AddSerilog(...)` from `Serilog.Extensions.Hosting` v10 is the correct API. Functionally identical: same provider replacement, `ReadFrom.Configuration`, `ReadFrom.Services`, `Enrich.FromLogContext`.
- **OTel endpoint precedence: config key over env var** — `builder.Configuration["Otel:OtlpEndpoint"]` read first; `OTEL_EXPORTER_OTLP_ENDPOINT` env var as fallback. Config key is testable without touching process environment.
- **`GetService<IConfiguration>()` not `GetRequiredService`** — existing `ValidateAll` unit tests (`ProfileResolutionTests`, `ConfigSizeParityTest`) build minimal `ServiceCollection` without `IConfiguration`. Using `GetRequiredService` caused 5 regressions (flagged by PLAN-2.1's SUMMARY). Fixed with nullable `GetService` + null-guard; pass 0 skipped in minimal test providers, runs in production.
- **CA1305 `formatProvider: null`** — all three Serilog sink calls required explicit `formatProvider: null` to suppress CA1305 under warnings-as-errors.

## Issues Encountered (Lesson Seeds)

- `HostApplicationBuilder.Host` does not exist in .NET 10 — plan templates for Worker SDK should use `builder.Services.AddSerilog()`, not `builder.Host.UseSerilog()`. The Phase 9 plan said "Worker SDK uses `Serilog.Extensions.Hosting`" but didn't specify the call site; this is the surprise.
- Serilog sink overloads trigger CA1305 under warnings-as-errors — always pass `formatProvider: null` explicitly. Affected `Console`, `File`, and `Seq` sinks.
- `ValidateAll` tests use minimal `ServiceCollection` — any new pass requiring `IConfiguration` must use `GetService` with null-guard, not `GetRequiredService`. PLAN-2.1's SUMMARY caught this regression.
- Parallel-wave build conflict with PLAN-2.1 — initial build attempt failed because PLAN-2.1 was modifying overlapping diagnostic surface. Resolved by committing in a deterministic order; PLAN-2.1 then unblocked their build by removing my stale `using Microsoft.Extensions.Configuration` (IDE0005). Lesson: when two parallel plans both touch warning-sensitive code, the later-committing plan must verify warnings are clean, not just errors.

## Verification Results

- Build: clean at every commit (0 warnings, 0 errors).
- Tests: 69/69 pass after all four commits.
- `appsettings.json` shape: `Serilog:Seq:ServerUrl == ""` and `Otel:OtlpEndpoint == ""` confirmed (no secrets, empty default values).
- Excluded patterns: zero matches for `Serilog.AspNetCore`, `OpenTelemetry.Exporter.Console`, `App.Metrics`, `OpenTracing`, `Jaeger.*`, `Log.Logger =` (the static Serilog anti-pattern from CONTEXT-9 is not introduced).
- ValidateObservability fail-fast verified by `ValidateAll` pass 0; error message lists the offending key + value.
- No new `public` types. No `.Result`/`.Wait()`. No `ServicePointManager`. No hard-coded IPs in committed config.
