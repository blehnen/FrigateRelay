# Review: Plan 2.2

## Verdict: MINOR_ISSUES

## Findings

### Critical
None.

### Minor

- **`ValidateObservability` has no unit tests** — `StartupValidation.cs` lines 68–77. The existing `ValidateAll` suite supplies a minimal `ServiceCollection` without `IConfiguration`, so the `if (configuration is not null)` guard at line 37 skips Pass 0 entirely; no test exercises `ValidateObservability` directly. A future refactor that removes the guard would silently regress fail-fast behavior. **Tracked as ID-16.** Remediation: PLAN-3.1 (Wave 3 TDD) should add 3 tests against `StartupValidation.ValidateObservability` via `InternalsVisibleTo` — malformed `Otel:OtlpEndpoint`, malformed `Serilog:Seq:ServerUrl`, both valid → zero errors.

- **`ValidateObservability` does not validate the `OTEL_EXPORTER_OTLP_ENDPOINT` env-var fallback** — `HostBootstrap.cs:57` resolves `otlpEndpoint` as `config["Otel:OtlpEndpoint"] ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")`, but `ValidateObservability` (line 70) only checks the config key. If the env var is set to `"not-a-uri"` while the config key is empty, validation passes and `new Uri(otlpEndpoint)` at `HostBootstrap.cs:65` throws `UriFormatException` with a raw stack trace instead of the structured diagnostic. **Tracked as ID-17.** Remediation: apply the same fallback in `ValidateObservability`: `var endpoint = config["Otel:OtlpEndpoint"] ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");`. **Orchestrator applying this fix inline before Wave 3 starts.**

- **XML doc on `ValidateAll` doesn't mention Pass 0** — `StartupValidation.cs:19`. The summary lists the pipeline as "profile resolution → action-plugin existence → snapshot-provider existence → per-action validator existence" without observability. Future readers won't see Pass 0 from the doc alone. Suggestion: prepend "observability endpoint URI validation →" to the pipeline description.

- **`otlpEndpoint` env-var branch creates test-isolation hazard** — `HostBootstrap.cs:56–57`. Any integration test running on a dev machine with `OTEL_EXPORTER_OTLP_ENDPOINT` set will silently wire OTLP. PLAN-3.1's integration tests should set `Otel:OtlpEndpoint` in their in-memory config to keep OTel wiring deterministic.

### Positive

- **Worker SDK Serilog wiring** — `builder.Services.AddSerilog((services, lc) => ...)` from `Serilog.Extensions.Hosting` is the correct API for `HostApplicationBuilder` (NOT `builder.Host.UseSerilog`, which doesn't exist). `ReadFrom.Configuration` + `ReadFrom.Services` + `Enrich.FromLogContext` + `WriteTo.Console` (with output template) + `WriteTo.File` (day rolling, 7 retained) + conditional `WriteTo.Seq` (guarded by `!string.IsNullOrWhiteSpace`).
- **D2 honored:** `AddOpenTelemetry()` registers `ActivitySource`/`Meter` unconditionally; `AddOtlpExporter` calls inside the endpoint-non-empty guard for both tracing and metrics.
- **D7 honored:** Seq sink registered only when `Serilog:Seq:ServerUrl` is non-empty.
- **`ValidateObservability` correctly uses `GetService<IConfiguration>()` + null-guard** — fixes the regression PLAN-2.1 flagged. Existing 5 failing tests now pass; 69/69 green.
- **`AddRuntimeInstrumentation()`** wired on the metrics pipeline — CLR/GC counters available without extra config.
- **No forbidden patterns:** zero `Log.Logger =`, zero `Serilog.AspNetCore`, zero `OpenTelemetry.Exporter.Console`, zero `App.Metrics` / `OpenTracing` / `Jaeger.*`.
- **No secrets** in committed `appsettings.json` (`Serilog:Seq:ServerUrl == ""`, `Otel:OtlpEndpoint == ""`).
- **No new `public` types** introduced. No `[InternalsVisibleTo]` source attributes.
- **Plan deviation documented:** `AddSerilog` vs `UseSerilog` is correctly explained in SUMMARY-2.2 as a Worker SDK adaptation.
- **CA1305 compliance:** `formatProvider: null` passed to all three Serilog sinks under warnings-as-errors.

## Summary
Critical: 0 | Minor: 4 | Positive: 9. APPROVE — ID-17 will be fixed inline by orchestrator before Wave 3; ID-16 (test coverage for `ValidateObservability`) folded into PLAN-3.1 scope; XML-doc and env-var-test-isolation suggestions deferred to Phase 11/12 polish.
