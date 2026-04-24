# CONCERNS.md

## Overview

This is a .NET Framework 4.8 Windows service with no tests, no version control, and several security and operational risks concentrated in a small codebase (~5 projects, ~500 LOC of meaningful logic). The most acute concerns are the global TLS certificate bypass, plaintext credentials in the INI config file, and total absence of automated testing or change safety nets. Dependency ages are moderate — no ancient versions found — but the OpenTracing/Jaeger stack is archived upstream.

## Metrics

| Metric | Value |
|--------|-------|
| Projects using packages.config NuGet style | 3 of 5 |
| Total packages (FrigateMQTTMainLogic) | 96 entries |
| Test projects | 0 |
| Git commits | 0 (not a repo) |
| TODO / FIXME / HACK markers in source | 0 (risks are in commented-out code, not markers) |
| Hard-coded IPs in shipped source | 1 (`http://192.168.0.58:5001` in `Pushover.cs`) |
| Hard-coded local paths in config template | 2 (`Configs/logging.settings` lines 3, 7) |
| DotNetWorkQueue retry exhaustion behavior | Silent drop after 3/6/9 s |

---

## Findings

### Security

- **Global TLS certificate bypass**: `ServicePointManager.ServerCertificateValidationCallback` is set to return `true` unconditionally at application startup. This affects the entire AppDomain — every outbound HTTPS call, including the Pushover API at `https://api.pushover.net`, will accept any certificate, including forged ones. The intent (accept self-signed certs on local Frigate/BlueIris hosts) is documented in CLAUDE.md, but the scope is wider than necessary.
  - Evidence: `Source/FrigateMQTTProcessingService/Program.cs` lines 21–23
  - Severity: **High**

- **Plaintext credentials in INI config**: `PushoverSettings.AppToken` and `UserKey` are stored as plaintext strings in `FrigateMQTTProcessingService.conf` (INI format, parsed by SharpConfig). There is no encryption, secret store integration, or environment-variable indirection. Anyone with read access to the deploy directory can extract the Pushover application token and user key.
  - Evidence: `Source/FrigateMQTTConfiguration/ConfigurationLoader.cs` (loads `.conf`); credential field names confirmed in `CLAUDE.md` `[PushoverSettings]` section.
  - Severity: **High**

- **Hard-coded private IP in dead but shipped code**: `Pushover.CodeProjectConfirms()` is commented out but fully compiled into the binary. It hard-codes `http://192.168.0.58:5001` as a CodeProject.AI endpoint. If uncommented without config extraction, it will silently target that host. The code is reachable only by uncommenting the call site, but it ships in every release binary.
  - Evidence: `Source/FrigateMQTTMainLogic/Pushover.cs` line 111
  - Severity: **Medium**

- **Author's local Seq URL committed in config template**: `Configs/logging.settings` (the template operators are told to copy) contains a hard-coded private Seq URL (`http://192.168.0.2:5341`) and a full Windows filesystem path (`C:\Git\FrigateMQTTProcessing\...`). These are not secrets in the cryptographic sense, but they leak internal network topology and should not be in the repository template.
  - Evidence: `Configs/logging.settings` lines 3 and 7
  - Severity: **Low**

---

### Operational / Reliability

- **No automated tests of any kind**: There are zero test projects in the solution. Every change to event routing, dedupe logic, or queue wiring is entirely unverified. Regressions will surface only in production.
  - Evidence: `Source/FrigateMQTTProcessingService.sln` — no `*.Tests` or `*.Spec` projects present; confirmed in `CLAUDE.md`.
  - Severity: **High**

- **No version control**: The project directory is not a git repository. There is no commit history, no ability to diff or revert changes, and no audit trail of what changed when.
  - Evidence: Working directory confirmed as non-repo in session context; no `.git/` directory present.
  - Severity: **High**

- **`.Result` blocking call on async Task risks deadlock**: `FrigateMain.Start()` calls `_main.Run(...).Result` on line 66. In a synchronization-context-bearing host (such as some Topshelf configurations or future migration targets), blocking on an async task this way can cause a deadlock. Currently benign because Topshelf's `WhenStarted` callback runs on a thread-pool thread without a sync context, but the pattern is fragile.
  - Evidence: `Source/FrigateMQTTProcessingMain/FrigateMain.cs` line 66
  - Severity: **Medium**

- **`Task.WaitAll` inside an async event handler**: `ClientOnApplicationMessageReceivedAsync` calls `Task.WaitAll(tasks.ToArray())` in three separate branches (lines 163, 198, 233). This blocks the async continuation inside what is nominally an `async Task` method, consuming a thread-pool thread for the duration of queue send operations. Under high message rates this degrades throughput and could exhaust thread-pool capacity.
  - Evidence: `Source/FrigateMQTTMainLogic/Main.cs` lines 163, 198, 233
  - Severity: **Medium**

- **DotNetWorkQueue silently drops messages after retry exhaustion**: The retry policy retries any `Exception` at 3 s, 6 s, and 9 s. No dead-letter queue, no error log entry for drop, and no alerting path exists after the third retry. A persistent failure (e.g., BlueIris unreachable, Pushover API down) will silently discard the triggering event.
  - Evidence: `Source/FrigateMQTTMainLogic/Queue.cs` lines 83–88; no post-retry handler registered.
  - Severity: **Medium**

- **`MemoryCache.Default` (global shared cache) used for dedupe**: `_cameraLastEvent` is assigned `MemoryCache.Default` — the process-wide singleton. Any other code in the AppDomain (e.g., future DotNetWorkQueue internals or a referenced library) using `MemoryCache.Default` with colliding key names would corrupt the per-camera throttle state.
  - Evidence: `Source/FrigateMQTTMainLogic/Main.cs` line 39
  - Severity: **Low**

- **`_cameraEventTracker` is populated but its reads are vestigial**: `_cameraEventTracker` (`ConcurrentDictionary<string, string>`) is populated with camera names at startup (line 71) and updated with `data.type` on each matched event (line 236), but it is never read to gate any behavior. It appears to be scaffolding for a planned feature that was never completed. It adds noise and a misleading impression of stateful tracking.
  - Evidence: `Source/FrigateMQTTMainLogic/Main.cs` lines 24, 37, 71, 236; no `.TryGetValue` / `.ContainsKey` reads found in the file.
  - Severity: **Low**

- **`SetupMetrics` directory-existence logic is inverted**: In `FrigateMain.SetupMetrics()`, the guard is `if (Directory.Exists(folder)) Directory.CreateDirectory(folder)` — it only creates the directory if it already exists, which is a no-op. The intent was clearly the inverse (`if (!Directory.Exists(...))`). Metrics file output is currently commented out (the `Report.ToTextFile` block), so this bug has no runtime impact today, but it will surface if reporting is re-enabled.
  - Evidence: `Source/FrigateMQTTProcessingMain/FrigateMain.cs` lines 118–119
  - Severity: **Low**

- **No deployment scripting or health check**: Install is manual (`Topshelf install` + copy two config files). There is no health-check endpoint, no watchdog beyond Windows Service recovery (restarts on crash only, configured via Topshelf), and no structured startup validation that config files are present and parseable before the service reports started.
  - Evidence: `Source/FrigateMQTTProcessingService/Program.cs` lines 55–80; `CLAUDE.md` "What is not in this repo" section.
  - Severity: **Medium**

---

### Dependency / Supply Chain

- **`packages.config` NuGet style in 3 of 5 projects**: `FrigateMQTTConfiguration`, `FrigateMQTTLogging`, and `FrigateMQTTMainLogic` use the legacy `packages.config` format. This requires manual `<Reference HintPath>` maintenance in `.csproj` files, makes transitive dependency auditing harder, and is unsupported in `dotnet` CLI workflows without migration.
  - Evidence: `Source/FrigateMQTTConfiguration/packages.config`, `Source/FrigateMQTTLogging/packages.config`, `Source/FrigateMQTTMainLogic/packages.config`
  - Severity: **Medium**

- **Key package versions (as found — no CVE speculation)**:
  - `DotNetWorkQueue` 0.6.8 — major version 0; pre-1.0 stability guarantees apply.
  - `MQTTnet` 4.1.4.563 — current at time of writing; not ancient.
  - `Newtonsoft.Json` 13.0.2 — current stable; not a concern.
  - `RestSharp` 108.0.3 — current stable; not a concern.
  - `SharpConfig` 3.2.9.1 — see below.
  - `OpenTracing` 0.12.1 — see below.
  - `Polly` 7.2.3 — Polly v8 is a breaking-change rewrite; not urgent but a future migration cost.
  - Evidence: `Source/FrigateMQTTMainLogic/packages.config`
  - Severity: **Low** (informational)

- **`SharpConfig` maintenance status unclear**: Only `FrigateMQTTConfiguration` references SharpConfig 3.2.9.1. The library has limited community adoption; its maintenance cadence and security response posture are unknown. [Inferred] If SharpConfig becomes unmaintained, the INI parsing layer would need replacement.
  - Evidence: `Source/FrigateMQTTConfiguration/packages.config` line 2
  - Severity: **Low**

- **OpenTracing / Jaeger stack is archived upstream**: `OpenTracing` 0.12.1 and `Jaeger` (referenced in `FrigateMain.cs`) implement the OpenTracing spec, which the CNCF has archived in favor of OpenTelemetry. The packages still function, but will receive no new features or security updates. `OpenTelemetry` 1.3.2 is also present in `packages.config` but appears unused in application code — suggesting a partial migration was started and abandoned.
  - Evidence: `Source/FrigateMQTTMainLogic/packages.config` lines 34–36 (`OpenTelemetry`, `OpenTelemetry.Api`, `OpenTracing`); `Source/FrigateMQTTProcessingMain/FrigateMain.cs` lines 9, 12 (`using Jaeger`, `using OpenTracing`).
  - Severity: **Medium** (modernization concern, not a bug)

- **`net48` target framework is Windows-only and out of mainstream support**: .NET Framework 4.8 receives security fixes but no new features. It is Windows-exclusive. Migrating to .NET 8+ would be required to run on Linux or in containers. Not an immediate risk for a Windows service deployment, but a long-term portability lock-in.
  - Evidence: `Source/FrigateMQTTProcessingMain/FrigateMQTTProcessingMain.csproj` (SDK-style, `net48`); `CLAUDE.md` build section.
  - Severity: **Low**

---

### Maintainability / Design Smells

- **`FrigateMQTTProcessingMain` vs `FrigateMQTTMainLogic/Main.cs` naming collision**: The service-wrapper project is named `FrigateMQTTProcessingMain` and contains class `FrigateMain`; the MQTT work class is named `Main` in project `FrigateMQTTMainLogic`. Both are colloquially "Main." New contributors will consistently confuse them. CLAUDE.md acknowledges this explicitly.
  - Evidence: `Source/FrigateMQTTProcessingMain/FrigateMain.cs`; `Source/FrigateMQTTMainLogic/Main.cs`
  - Severity: **Low**

- **`Main.cs` is a god class (~310 LOC)**: `FrigateMQTTMainLogic.Main` handles MQTT client construction and lifecycle, topic subscription, JSON deserialization, per-subscription event matching, zone filtering, dedupe/throttle logic, queue dispatch, and camera-state tracking. Single-responsibility is violated across at least five distinct concerns.
  - Evidence: `Source/FrigateMQTTMainLogic/Main.cs` lines 1–310
  - Severity: **Medium**

- **`ClientOnApplicationMessageReceivedAsync` has deeply duplicated branching**: The `new`, `update`, and `end` event-type branches (lines 134–234) each contain nearly identical blocks: send camera message, check cache, conditionally send pushover, add cache entry, log zones, `Task.WaitAll`. The triplication means any change to the dispatch pattern must be made in three places.
  - Evidence: `Source/FrigateMQTTMainLogic/Main.cs` lines 134–234
  - Severity: **Medium**

- **Commented-out code paths represent unrecorded design decisions**: `Pushover.cs` retains three commented-out image-source alternatives (Frigate snapshot URL, Frigate thumbnail URL, CodeProject.AI second-pass confirmation). The rationale for the current design (BlueIris images preferred; CodeProject.AI disabled) exists only in CLAUDE.md, not adjacent to the code. If CLAUDE.md drifts, the intent of these stubs is lost.
  - Evidence: `Source/FrigateMQTTMainLogic/Pushover.cs` lines 45–58
  - Severity: **Low**

- **Hard-coded Windows-style relative paths for metrics output**: `SetupMetrics` passes `@"Metrics\metricsAPI.txt"` and `@"Metrics\metricsQueue.txt"` (backslash-separated, relative). These are Windows path strings embedded in code. The metrics file reporting is currently commented out, so there is no runtime failure, but re-enabling it would break under any non-Windows hosting.
  - Evidence: `Source/FrigateMQTTProcessingMain/FrigateMain.cs` lines 58–59
  - Severity: **Low**

- **`README.md` is boilerplate from a different project**: The repository README describes a "File Transfer Server," not this service. New readers who open the repo root will get actively wrong information.
  - Evidence: `README.md` (confirmed in CLAUDE.md: "README.md is a stub (old 'File Transfer Server' boilerplate); do not trust it")
  - Severity: **Low**

---

### Unknowns / Open Questions

- **`app.config` binding redirects**: The SDK-style projects (`FrigateMQTTProcessingMain`, `FrigateMQTTProcessingService`) may auto-generate binding redirects; the legacy-format projects do not. Whether assembly version conflicts exist between the `System.*` 4.3.x packages (in `packages.config`) and the `Microsoft.Extensions.*` 7.0.x packages has not been verified. A failed redirect would produce a `FileLoadException` at runtime only.
  - Evidence: `Source/FrigateMQTTMainLogic/packages.config` — mix of `System.*` at 4.3.x and `Microsoft.Extensions.*` at 7.0.x.

- **`tracesettings.json` template**: No committed template for `tracesettings.json` was found in `Configs/` or elsewhere. If a new operator wants to enable Jaeger tracing, there is no reference file showing the required JSON schema.
  - Evidence: `Source/FrigateMQTTProcessingMain/FrigateMain.cs` lines 104–113 (runtime loading); no `tracesettings.json` found in `Configs/`.

---

## Summary Table

| Item | Detail | Severity | Confidence |
|------|---------|----------|------------|
| Global TLS bypass | `Program.cs:21-23` — all certs accepted AppDomain-wide | High | Observed |
| Plaintext Pushover credentials | `FrigateMQTTProcessingService.conf` INI file | High | Observed |
| No tests | Zero test projects in solution | High | Observed |
| No git repository | No version control safety net | High | Observed |
| Hard-coded private IP in dead code | `Pushover.cs:111` — `192.168.0.58:5001` | Medium | Observed |
| `.Result` deadlock risk | `FrigateMain.cs:66` | Medium | Observed |
| `Task.WaitAll` in async handler | `Main.cs:163,198,233` | Medium | Observed |
| Silent message drop after retry | `Queue.cs:83-88` — no dead-letter handler | Medium | Observed |
| No health check / deployment scripting | Manual install only | Medium | Observed |
| OpenTracing/Jaeger archived upstream | `packages.config` + `FrigateMain.cs` | Medium | Observed |
| `packages.config` in 3 projects | Legacy NuGet style | Medium | Observed |
| God-class `Main.cs` | ~310 LOC, 5+ responsibilities | Medium | Observed |
| Triplication in event dispatch | `Main.cs:134-234` | Medium | Observed |
| Inverted directory-exists guard | `FrigateMain.cs:118-119` (metrics path) | Low | Observed |
| `MemoryCache.Default` global cache | `Main.cs:39` | Low | Observed |
| `_cameraEventTracker` written, never read | `Main.cs:24,71,236` | Low | Observed |
| Author paths/IP in config template | `Configs/logging.settings:3,7` | Low | Observed |
| SharpConfig maintenance unknown | `FrigateMQTTConfiguration/packages.config:2` | Low | Inferred |
| `net48` Windows-only lock-in | All projects | Low | Observed |
| Metrics paths Windows-only strings | `FrigateMain.cs:58-59` | Low | Observed |
| Commented-out code / missing decision log | `Pushover.cs:45-58` | Low | Observed |
| Misleading README | `README.md` — wrong project description | Low | Observed |
| Binding redirect correctness | Mixed `System.*` 4.3.x + `Microsoft.*` 7.0.x | Unknown | Inferred |
| No `tracesettings.json` template | No reference file for Jaeger config | Unknown | Observed |
