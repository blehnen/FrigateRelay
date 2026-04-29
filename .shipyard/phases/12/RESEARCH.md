# Phase 12 Research

## 1. Legacy INI Structure (for migration tool)

Source: `.shipyard/codebase/CONVENTIONS.md`, `ARCHITECTURE.md`, `STACK.md`.

### Section inventory

The legacy `FrigateMQTTProcessingService.conf` is parsed by `SharpConfig` into three POCO types in `FrigateMQTTConfiguration`:

| INI Section | POCO Class | Notes |
|---|---|---|
| `[ServerSettings]` | `ServerSettings` | MQTT broker, Blue Iris image base URL |
| `[PushoverSettings]` | `PushoverSettings` | Pushover credentials + cooldown |
| `[SubscriptionSettings]` | `SubscriptionSettings` | Per-camera subscription; `[Serializable]` required by SharpConfig for section list |

SharpConfig supports section lists via `configuration.GetAll<SubscriptionSettings>()` — multiple `[SubscriptionSettings]` sections enumerate as an array. That is the multi-subscription pattern.

### Known per-section keys (from CONVENTIONS.md + ARCHITECTURE.md evidence)

**`[ServerSettings]`**
| INI Key | Evidence | Target appsettings.json path |
|---|---|---|
| `Server` | `Main.cs` line 52 — `ServerSettings.Server` used as TCP hostname | `FrigateMqtt:Server` |
| `BlueIrisImages` | `Pushover.cs` — `ServerSettings.BlueIrisImages + cameraShortName` | `BlueIris:SnapshotUrlTemplate` (base URL prefix) |

**`[PushoverSettings]`**
| INI Key | Evidence | Target appsettings.json path |
|---|---|---|
| `AppToken` | `PushoverSettings.cs` line 15 `AppToken { get; set; }` | `Pushover:AppToken` (secret — env var only) |
| `UserKey` | `PushoverSettings.cs` line 16 `UserKey { get; set; }` | `Pushover:UserKey` (secret — env var only) |
| `NotifySleepTime` | `PushoverSettings.cs` constructor sets `NotifySleepTime = 30` (seconds default) | Per-subscription `CooldownSeconds` in `Subscriptions:N` |

**`[SubscriptionSettings]`** (repeated once per subscription)
| INI Key | Evidence | Target appsettings.json path |
|---|---|---|
| `CameraName` | `Main.cs` line 79 — subscription match on `sub.CameraName` == `data.before.camera` | `Subscriptions:N:Camera` |
| `ObjectName` | `Main.cs` line 80 — subscription match on `sub.ObjectName` == `data.before.label` | `Subscriptions:N:Label` |
| `Zone` | `Main.cs` line 81 — zone membership check | `Subscriptions:N:Zone` |
| `Camera` | `Main.cs` lines 141-158 — `sub.Camera` is the Blue Iris HTTP trigger URL; also used as dedupe cache key | `BlueIris:TriggerUrlTemplate` (per-subscription override) OR a subscription-level `BlueIrisCamera` field |
| `Name` | `Main.cs` line 70 — `sub.Name` used in log | `Subscriptions:N:Name` |

**Unclear / not directly evidenced from codebase docs:**
- Whether `[ServerSettings]` carries MQTT port (likely `Port`) or client-id — not evidenced in the docs. TODO architect: check `PushoverSettings.cs` and `ServerSettings.cs` in `.shipyard/codebase/` if more detail needed, or accept that the tool must handle unknown keys gracefully.
- `BlueIrisImages` → `BlueIris:SnapshotUrlTemplate` mapping requires converting a base-URL prefix to the template-token DSL `{camera}`. The tool needs logic here, not a direct copy.

### Multi-subscription pattern

Multiple `[SubscriptionSettings]` sections in sequence. SharpConfig reads them as a list. Each corresponds to one entry in `Subscriptions[]` in the JSON output. The `appsettings.Example.json` (9 subscriptions, all referencing profile `Standard`) is the target shape.

### Committed legacy `.conf` fixture

`tests/FrigateRelay.Host.Tests/Configuration/Fixtures/legacy.conf` — **does NOT exist** (confirmed: `File.Exists` check in `ConfigSizeParityTest.cs` line 34 hard-fails with a sanitization instruction when absent). The test instructs the author to sanitize their real `.conf` and place it there. This file must be created by the operator before `ConfigSizeParityTest` can pass.

The `FrigateRelay.Host.Tests.csproj` has `<None Update="Fixtures\legacy.conf"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></None>` — the slot is wired; the file just doesn't exist yet.

The MigrateConf test project needs its own fixture copy at `tests/FrigateRelay.MigrateConf.Tests/Fixtures/sanitized-legacy.conf`. Architect must plan for operator to provide this sanitized file before Wave 1 tests can pass.

---

## 2. ConfigSizeParityTest

**File:** `tests/FrigateRelay.Host.Tests/Configuration/ConfigSizeParityTest.cs`

**What it asserts:**
1. `legacy.conf` fixture exists at `tests/FrigateRelay.Host.Tests/Configuration/Fixtures/legacy.conf` — **hard fails** (no `Assert.Inconclusive`) if absent.
2. Raw character count ratio: `jsonLength / iniLength <= 0.60`. No whitespace stripping or normalization.
3. Sub-assertion: `config/appsettings.Example.json` binds and validates successfully via `StartupValidation.ValidateAll` with stub plugins (`BlueIris`, `Pushover` as action; `Frigate` as snapshot; `CodeProjectAi` as validator).

**Fixture dependencies:**
- `Fixtures/legacy.conf` — operator-created sanitized INI (not committed, not auto-generated).
- `config/appsettings.Example.json` — committed at repo root, linked into test output via csproj `<None Include>`.

**appsettings.Example.json shape (confirmed, read directly):** 9 subscriptions, 1 profile (`Standard`) with 2 actions (`BlueIris`, `Pushover` with `SnapshotProvider: "Frigate"`). Total: 21 lines / ~630 chars.

**MigrateConf success criterion:** Tool output must be ≤ 60% of `legacy.conf` char count AND must pass `StartupValidation.ValidateAll`. The smoke test in MigrateConf.Tests should:
1. Run the tool against `tests/FrigateRelay.MigrateConf.Tests/Fixtures/sanitized-legacy.conf`.
2. Assert the output passes `ConfigSizeParityTest`'s same ratio check.
3. Optionally call `Json_Binds_And_Validates_Successfully` logic directly (can share helper or duplicate inline).

**Architect note:** The parity test's binding sub-assertion uses NSubstitute on `IActionPlugin`/`ISnapshotProvider`/`IValidationPlugin` — the MigrateConf test project must also reference NSubstitute if it exercises this path, or extract the validation logic into a helper.

---

## 3. Action Plugin csproj Patterns (DryRun Rollout)

### Action plugins requiring DryRun (D5 scope)

| Plugin | Options record | Plugin class | Logging style | DryRun insert point |
|---|---|---|---|---|
| `FrigateRelay.Plugins.BlueIris` | `BlueIrisOptions.cs` — `public sealed record`, `required string TriggerUrlTemplate`, `bool AllowInvalidCertificates`, `TimeSpan RequestTimeout`, `int? QueueCapacity`, `string? SnapshotUrlTemplate` | `BlueIrisActionPlugin.cs` — `internal sealed class`, `ExecuteAsync` calls `client.GetAsync(url, ct)` | Static `Action<ILogger,...>` fields at class level (NOT nested static class) — `LogTriggerSuccess`, `LogTriggerFailed` via `LoggerMessage.Define` | After `var url = _template.Resolve(ctx)` but before `using var client = _httpFactory.CreateClient(...)` — return early with a `LogDryRun` call |
| `FrigateRelay.Plugins.Pushover` | `PushoverOptions.cs` — `internal sealed class` (NOT record), `[Required]` on `AppToken`/`UserKey`, multiple fields | `PushoverActionPlugin.cs` — `internal sealed class`, `ExecuteAsync` calls `snapshot.ResolveAsync`, builds `MultipartFormDataContent`, calls `client.SendAsync` | Nested `private static class Log` containing static `Action<ILogger,...>` fields — `_sendSucceeded`, `_sendFailed`, `_snapshotUnavailable` | At top of `ExecuteAsync` before `var opts = _options.Value` — return early with a `Log.WouldExecute(...)` call |

**Logging style discrepancy to flag:** BlueIris uses top-level class static fields; Pushover uses a nested `Log` static class. DryRun log emission should follow the owning plugin's existing style (BlueIris = top-level field; Pushover = nested `Log` class method). Use `LoggerMessage.Define` per CLAUDE.md CA1848.

**Options record vs class discrepancy:** `BlueIrisOptions` is a `record`; `PushoverOptions` is a `class`. Adding `DryRun` to BlueIris: `public bool DryRun { get; init; }` in the record. Adding to Pushover: `public bool DryRun { get; init; }` in the class. Both are straightforward; architect should note the type discrepancy.

**`PushoverOptions` visibility:** `internal sealed class` — the `DryRun` property stays `internal` consistent with the enclosing type.

**`BlueIrisOptions` visibility:** `public sealed record` — `DryRun` will be `public`. Default `false`.

### Out-of-scope plugins (confirmed)

- `FrigateRelay.Plugins.CodeProjectAi` — implements `IValidationPlugin`, not `IActionPlugin`. D5 explicitly excludes validators.
- `FrigateRelay.Plugins.FrigateSnapshot` — implements `ISnapshotProvider`, not `IActionPlugin`. D5 explicitly excludes snapshot providers.

### EventId recommendations for DryRun log

- BlueIris: next available after 202. Suggest `EventId(203, "BlueIrisDryRun")`.
- Pushover: next available after 3. Suggest `EventId(4, "PushoverDryRun")` inside `Log` class.

---

## 4. Dispatcher Flow Constraints

From `ChannelActionDispatcher.cs` (read directly):

- **DryRun must return normally** (no throw). The consumer's success path (`actionActivity.SetStatus(ActivityStatusCode.Ok)` + `DispatcherDiagnostics.ActionsSucceeded.Add(1, ...)`) executes only when `ExecuteAsync` returns without throwing. A DryRun early-return satisfies this — counters tick as "succeeded".
- **`ActionsSucceeded` counter** — incremented at line 271 after `await plugin.ExecuteAsync(...)` returns. DryRun hits this path. Correct per D5: metrics should reflect "would-have-executed."
- **`ActionsFailed` counter** — incremented only in the outer `catch (Exception ex)` block. DryRun does not throw, so this counter does NOT tick. Correct.
- **`ActionsDispatched` counter** — incremented in `EnqueueAsync` before the item enters the channel, regardless of DryRun. Correct: dispatch happens even in dry-run mode.
- **OTel span** — `action.<name>.execute` span is still started and completed with `ActivityStatusCode.Ok`. The `outcome` tag will be `"success"` (same `goto NextItem` is not triggered; validator path is separate). Architect may want to consider setting `outcome = "dry_run"` as a separate tag value, but that's optional and not mandated by D5.
- **No dispatcher code changes needed** — DryRun is entirely implemented inside each plugin's `ExecuteAsync`. The dispatcher is unaware.

---

## 5. Serilog Audit-Log Mechanism Options

### Option A — Parse existing rolling file sink

- `HostBootstrap.cs` already writes `logs/frigaterelay-.log` (rolling, daily, 7-file retention) in non-Docker deployments.
- Current formatter: default Serilog text template `[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}{NewLine}{Message:lj}{NewLine}{Exception}`. This is **NOT** structured JSON — it is human-readable text.
- To make Option A viable, the file sink must be switched to `new CompactJsonFormatter()` (Serilog.Formatting.Compact) so log entries are NDJSON with named properties. OR: the file path can output NDJSON while the console keeps the human-readable template.
- The DryRun `LoggerMessage` entries will emit named properties (`event_id`, `camera`, `label`, `action`, `url`/`token summary`) that are accessible as structured fields in NDJSON.
- Parsing: the reconciler reads NDJSON, filters by `EventId.Name == "BlueIrisDryRun"` or `"PushoverDryRun"`, extracts `(timestamp, camera, label, action, outcome)`.

### Option B — Dedicated audit-log Serilog sink

- Add a second `WriteTo.File` with `CompactJsonFormatter` to a separate `logs/audit-.log` path, filtered to only DryRun EventIds using a `Serilog.Filters.Expressions` or `When(evt => ...)` filter.
- Cleaner separation but adds a new file path and a new config key.
- Requires `Serilog.Filters.Expressions` package (MIT, maintained).

### Recommendation (architect's call)

**Option A with formatter upgrade** is lower surface area: change the file sink in `HostBootstrap.cs` to use `CompactJsonFormatter`, and the reconciler reads the existing log. No new package, no new sink. The architect must decide whether to upgrade the formatter only for the parity window or permanently (structured file logs have production value).

**Option B** is cleaner operationally but adds one package and one config key. Bias toward A per D5 "least-effort path."

---

## 6. Release Prerequisites

### Remote slug

TODO architect: run `git remote -v` to confirm remote URL. Based on README.md line 11 (`git clone https://github.com/blehnen/FrigateRelay.git`), the remote is `https://github.com/blehnen/FrigateRelay.git`. Owner slug: `blehnen`.

### release.yml behavior on `v1.0.0` tag (confirmed from file read)

- Trigger: `push: tags: ["v*"]` — matches `v1.0.0`.
- Smoke gate: builds `linux/amd64`, starts Mosquitto sidecar, starts FrigateRelay, polls `/healthz` for 30s. **Multi-arch push does NOT happen if smoke fails.**
- Tags generated by `docker/metadata-action`: `ghcr.io/blehnen/frigaterelay:1.0.0`, `ghcr.io/blehnen/frigaterelay:1`, `ghcr.io/blehnen/frigaterelay:latest`.
- Multi-arch: `linux/amd64` + `linux/arm64` via QEMU.
- Concurrency: `group: release-${{ github.ref }}`, `cancel-in-progress: false` — serializes duplicate pushes of the same tag.
- Permissions: `packages: write` via `GITHUB_TOKEN` — no PAT required.

### Branch protection

TODO architect: check GitHub repo settings for `main` branch protection rules. No evidence in committed files. Manual `git tag v1.0.0 && git push --tags` is the planned path per D7 — branch protection on `main` does not block tag pushes unless tag protection rules are configured separately.

---

## 7. tools/ and scripts/ Surface

### tools/ directory

Does NOT exist yet. Confirmed by absence — no `tools/` directory is referenced in any existing csproj, sln, or CI file. Phase 12 creates it from scratch per D6.

### Existing scripts/

TODO architect: `find scripts/ -type f` not run. Based on CLAUDE.md and Phase 11 docs:
- `.github/scripts/run-tests.sh` — confirmed exists (read directly). Auto-discovers `tests/*.Tests/*.csproj` via `find tests -maxdepth 2 -name '*Tests.csproj'`. A new `tests/FrigateRelay.MigrateConf.Tests/` directory matching `*Tests.csproj` will be picked up automatically — no changes to `run-tests.sh` needed.
- `.github/scripts/secret-scan.sh` — exists (Phase 2). No changes needed for Phase 12.
- `scripts/check-doc-samples.sh` — Phase 11 artifact. Bash + Python pattern established.

### sln precedent for tool projects

No existing `tools/` csproj in `FrigateRelay.sln`. Phase 12 adds the first. Pattern to follow: mirror `src/FrigateRelay.Plugins.BlueIris/` csproj shape (`OutputType=Exe` for the tool; `OutputType=Exe` + MSTest for the companion test). Both get added to `FrigateRelay.sln`.

### ci.yml impact

`run-tests.sh` glob picks up `tests/FrigateRelay.MigrateConf.Tests/FrigateRelay.MigrateConf.Tests.csproj` automatically (matches `*Tests.csproj` at maxdepth 2). No ci.yml edits needed for test discovery. The tool itself (`tools/FrigateRelay.MigrateConf/`) is NOT a test project — it won't be run by `run-tests.sh`.

---

## 8. Sanitized Legacy .conf Fixture Status

**ORCHESTRATOR CORRECTION (2026-04-28):** the researcher reported `legacy.conf` does NOT exist, citing path `tests/FrigateRelay.Host.Tests/Configuration/Fixtures/legacy.conf`. That path is wrong — the file is actually at `tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf` (NO `Configuration/` subdir). The file **DOES exist**: 88 lines, 11 INI sections (1 `[ServerSettings]`, 1 `[PushoverSettings]`, 9 repeated `[SubscriptionSettings]` blocks). The csproj `<None Update="Fixtures\legacy.conf">` confirms the actual `Fixtures/` path, not a `Configuration/Fixtures/` path. The researcher's `Assert.Fail` quote is real (it lives in `ConfigSizeParityTest.cs:34`) but it never fires today because the file is present.

**Actual structure** (verified by orchestrator `cat` + `grep`):
- 1× `[ServerSettings]` block (server, frigateapi, blueirisimages keys; example uses RFC 5737 documentation IPs `192.0.2.x`)
- 1× `[PushoverSettings]` block (AppToken, UserKey — both empty in fixture)
- 9× `[SubscriptionSettings]` blocks (DriveWay Person/Car/etc.; each has Name, LocationName, ObjectName, Camera, CameraShortName, CameraName, Zone)

**Implications for architect (UPDATED):**
1. **The legacy.conf fixture exists.** `ConfigSizeParityTest` runs today. MigrateConf can use this same fixture (or a copy under `tests/FrigateRelay.MigrateConf.Tests/Fixtures/`) for round-trip testing.
2. The fixture uses **RFC 5737 documentation IPs (`192.0.2.x`)** — these are CLAUDE.md secret-scan-safe (the secret-scan rejects RFC 1918 ranges, not 5737 documentation ranges). MigrateConf test fixtures can mirror this convention.
3. `.shipyard/phases/8/SANITIZATION-CHECKLIST.md` may or may not exist; the existing `legacy.conf` proves whatever sanitization process happened during Phase 8 produced an acceptable fixture. Architect can re-use the same approach.
4. Wave 1 plans the MigrateConf tool against this REAL fixture. No "operator must create fixture" instruction needed — Phase 8 already handled it.

**Actual INI structure (verified, sanitized):**

```ini
[ServerSettings]
Server = <mqtt-broker-host>
BlueIrisImages = <blueiris-base-url>/image/

[PushoverSettings]
AppToken = <redact>
UserKey = <redact>
NotifySleepTime = 30

[SubscriptionSettings]
Name = DriveWay Person
CameraName = DriveWayHD
ObjectName = Person
Zone = driveway_person_zone
Camera = <blueiris-trigger-url>

[SubscriptionSettings]
; ... repeated 8 more times for 9 subscriptions total
```

---

## 9. README Migration-Section Status

**Current README.md** (Phase 11 output, read to line 60): No migration section exists. The README contains:
- Quickstart (Docker)
- Configuration (IConfiguration layering, env vars, full example excerpt)
- Adding a new plugin tutorial reference

Phase 12 Wave 3 adds a `## Migrating from FrigateMQTTProcessingService` section referencing:
- `docs/migration-from-frigatemqttprocessing.md`
- `tools/FrigateRelay.MigrateConf/` usage instructions

This is a net-new section — no placeholder to update.

---

## 10. Open Issues Touching Parity Scope

| ID | Title | Parity relevance |
|---|---|---|
| ID-7 | `{score}` placeholder in CONTEXT-4 D3 — parser does not accept it | Low. MigrateConf tool only generates `{camera}`, `{label}`, `{event_id}`, `{zone}` in URL templates. No `{score}` in migration output. Non-issue. |
| ID-8 | `--coverage` branch of `run-tests.sh` drops `PASS_THROUGH_ARGS` | Not Phase 12 scope. Only affects Jenkins coverage runs with extra filter args. |
| ID-22 | `Task.Delay` magic delays in Phase 9 observability tests | Not Phase 12 scope per CONTEXT-12.md explicit exclusion. |
| ID-24 | `release.yml` actions tag-pinned not SHA-pinned | Pre-v1.0 hardening advisory. Architect should note as a RELEASING.md callout — fix before or after tag push is operator choice. Low risk for a personal-use repo. |
| ID-3 | `TargetFramework` missing from BlueIris csproj | Non-blocking. Build inherits from `Directory.Build.props`. |

No open issues are blocking Phase 12 delivery. ID-24 is worth a sentence in the release checklist.

---

## Architect-Relevant Constraints

1. **`legacy.conf` is operator-created, not agent-created.** Wave 1 MUST document sanitization steps and block Wave 1 test success on operator placing the file. `ConfigSizeParityTest` hard-fails without it.

2. **MigrateConf tool parses INI without SharpConfig.** CLAUDE.md excludes SharpConfig. Architect must choose: `Microsoft.Extensions.Configuration.Ini` (MIT, in-box) or hand-rolled INI parser. The SharpConfig multi-section syntax (`[SubscriptionSettings]` repeated) may not be standard INI — `Microsoft.Extensions.Configuration.Ini` may require numbered sections (`[SubscriptionSettings:0]`, `[SubscriptionSettings:1]`). TODO: architect should confirm SharpConfig's exact wire format for repeated sections and whether M.E.C.Ini handles it.

3. **`BlueIrisOptions` is a `record`, `PushoverOptions` is a `class`.** DryRun property addition is straightforward in both but the architect should document the discrepancy in the plan so the builder doesn't inadvertently convert the class to a record.

4. **Serilog file sink is text-format, not JSON.** If Option A (parse existing sink) is chosen for parity-CSV export, `HostBootstrap.cs` needs a formatter change (`CompactJsonFormatter`). This is a production change that affects all non-Docker deployments.

5. **`run-tests.sh` auto-discovers test projects** — no changes needed when `tests/FrigateRelay.MigrateConf.Tests/` is added.

6. **release.yml smoke gate is a real gate** — `v1.0.0` tag push will fail if the amd64 image smoke does not pass `/healthz` within 30s. Ensure the operator has a working Docker environment when pushing the tag.

7. **`appsettings.Example.json` is the shape target** — 9 subscriptions, 1 profile, ~630 chars. The sanitized `legacy.conf` must be at least ~1050 chars for the 60% ratio to pass. The author's real 9-subscription INI (with 9 repeated `[SubscriptionSettings]` blocks, each with 5+ keys) will easily exceed this.

---

## Uncertainty Flags

- **SharpConfig repeated-section wire format:** ARCHITECTURE.md confirms multiple `[SubscriptionSettings]` sections exist and are read via `configuration.GetAll<SubscriptionSettings>()`. Whether the raw INI file uses truly identical `[SubscriptionSettings]` headers (SharpConfig-specific) or numbered variants is NOT confirmed from docs. The MigrateConf tool must handle this correctly. **Decision Required:** architect should specify whether the tool hand-parses INI or relies on M.E.C.Ini, and validate against the real `.conf` format.

- **`ServerSettings` full key list:** Only `Server` and `BlueIrisImages` are evidenced from the codebase docs. Additional keys (e.g. MQTT `Port`, `ClientId`, TLS settings) may exist. The tool should emit warnings for unmapped keys rather than silently dropping them.

- **`SubscriptionSettings.Camera` field semantics:** Used as the Blue Iris HTTP trigger URL AND as the dedupe cache key in the legacy service. In the new config, the trigger URL is `BlueIris:TriggerUrlTemplate`. Per-subscription camera mapping is implicit in the template tokens (`{camera}` → Frigate `CameraName`). The MigrateConf tool needs a decision: does it generate a global `TriggerUrlTemplate` from the first subscription's `Camera` URL and extract the camera placeholder, or does it produce per-subscription inline action overrides? **Architect-discretion item.**

- **Serilog formatter choice for Option A:** Not confirmed whether `Serilog.Formatting.Compact` is already a dependency in `FrigateRelay.Host`. If not, adding it for parity-window logging is a new (small) package dep. TODO architect: `dotnet list src/FrigateRelay.Host package --include-transitive | grep Compact`.

- **Phase 8 `SANITIZATION-CHECKLIST.md` existence:** Referenced in `ConfigSizeParityTest.cs` error message but not read. TODO architect: confirm `.shipyard/phases/8/SANITIZATION-CHECKLIST.md` exists before referencing it in Wave 1 migration doc.
