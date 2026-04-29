# Research: Phase 8 — Profiles in Configuration

## Context

Phase 8 adds named `Profiles` to the configuration schema (PROJECT.md decision S2), eliminating the
9× action-list repetition in the author's production INI. It also closes ID-12 by adding
`ActionEntryTypeConverter` so `IConfiguration.Bind` correctly handles the legacy string-array
`Actions: ["BlueIris"]` form. All binding decisions are captured in CONTEXT-8.md (D1–D6), which
this document treats as binding constraints.

---

## Section 1 — Existing Host Configuration Types

### `ActionEntry` — `src/FrigateRelay.Host/Configuration/ActionEntry.cs`

```csharp
[JsonConverter(typeof(ActionEntryJsonConverter))]
public sealed record ActionEntry(
    string Plugin,
    string? SnapshotProvider = null,
    IReadOnlyList<string>? Validators = null);
```

- Visibility: **`public`** (raised from `internal` during Phase 5 due to CS0053 cascade — tracked as ID-10).
- The primary constructor doubles as the `ActionEntry(string)` ctor that ID-12 references: calling
  `new ActionEntry("BlueIris")` works today via the primary ctor with defaults for the optional params.
- No secondary `string`-only constructor exists. The TypeConverter can call `new ActionEntry(pluginName)`.
- `[JsonConverter]` fires only on `JsonSerializer.Deserialize` paths — **not** on `IConfiguration.Bind`.

### `ActionEntryJsonConverter` — `src/FrigateRelay.Host/Configuration/ActionEntryJsonConverter.cs`

```csharp
public sealed class ActionEntryJsonConverter : JsonConverter<ActionEntry>
```

- Visibility: **`public`** (ID-10).
- Handles string-form (`"BlueIris"`) and object-form (`{"Plugin":"BlueIris",...}`).
- Contains a private `ActionEntryDto` inner record to avoid recursive converter invocation.
- The XML doc comment on this class explicitly documents the ID-12 limitation (lines 21–26).
- **Root cause of ID-12**: `IConfiguration.Bind` uses `TypeDescriptor.GetConverter(typeof(ActionEntry))`
  for scalar child paths. No `TypeConverter` is registered → binder silently skips the scalar string
  and produces an empty `Actions` list.

### `SubscriptionOptions` — `src/FrigateRelay.Host/Configuration/SubscriptionOptions.cs`

```csharp
public sealed record SubscriptionOptions
{
    public required string Name { get; init; }
    public required string Camera { get; init; }
    public required string Label { get; init; }
    public string? Zone { get; init; }
    public int CooldownSeconds { get; init; } = 60;
    public IReadOnlyList<ActionEntry> Actions { get; init; } = Array.Empty<ActionEntry>();
    public string? DefaultSnapshotProvider { get; init; }
}
```

- Visibility: **`public`** (ID-10 cascade).
- **No `Profile` property exists yet** — Phase 8 adds `public string? Profile { get; init; }`.
- `Actions` defaults to `Array.Empty<ActionEntry>()`, not `null`. Per D1, Phase 8 changes semantics:
  an empty `Actions` with no `Profile` becomes a startup error (currently it is a silent no-op).

### `HostSubscriptionsOptions` — `src/FrigateRelay.Host/Configuration/HostSubscriptionsOptions.cs`

```csharp
public sealed record HostSubscriptionsOptions
{
    public IReadOnlyList<SubscriptionOptions> Subscriptions { get; init; } = Array.Empty<SubscriptionOptions>();
    public SnapshotResolverOptions Snapshots { get; init; } = new();
}
```

- Visibility: **`public`** (ID-10 cascade).
- **No `Profiles` property exists yet** — Phase 8 adds
  `public IReadOnlyDictionary<string, ProfileOptions> Profiles { get; init; } = ...`.
- Bound via `services.AddOptions<HostSubscriptionsOptions>().Bind(builder.Configuration)` in
  `HostBootstrap.ConfigureServices` (line 26–27), which means `Profiles` binds from the top-level
  config key `"Profiles"` automatically once the property is added.

---

## Section 2 — Phase 7 Startup-Validation Pattern

**Location**: `src/FrigateRelay.Host/StartupValidation.cs` (static class, `internal`).
**Wiring**: called from `HostBootstrap.ValidateStartup(IServiceProvider)` (line 87–102), which is
invoked in `Program.cs` after `builder.Build()` and before `app.RunAsync()`.

**Structure** (verbatim from current code):

- Three `public static void Validate*` methods: `ValidateActions`, `ValidateSnapshotProviders`,
  `ValidateValidators`.
- Each iterates subscriptions and throws `InvalidOperationException` on the **first** error found
  (fail-fast, not collect-all). This differs from CONTEXT-8 cross-cutting note 1, which requires
  collecting **all** errors before throwing. **This is a constraint the architect must address for
  the Phase 8 profile validator** — either retrofit the existing validators to collect-all, or implement
  the profile validator with collect-all and note the divergence.
- Error message format: `"Subscription '<name>' references unknown action plugin '<plugin>'. Registered plugins: [...]."` — sentence-ending period, named entities, registered-names list.
- Exception type: `InvalidOperationException` (no custom exception type).

**Phase 8 profile validator** must:
1. Run **before** `ValidateActions` and `ValidateValidators` (profile expansion must happen first so
   those validators see the resolved action list).
2. Be wired into `HostBootstrap.ValidateStartup` as a new call before line 91.
3. Mutate `subsOpts` in-memory (expand profile references into `Actions` lists) OR store the
   resolved subscriptions in a separate structure the downstream validators consume.

> **Key finding**: `ValidateValidators` iterates `sub.Actions` directly. If profile expansion is
> done by mutating the subscription list before validators run, no changes to `ValidateValidators`
> or `ValidateActions` are needed. The cleanest approach is for the profile resolver to produce a
> fully-resolved `IReadOnlyList<SubscriptionOptions>` (with `Actions` populated) and pass that list
> to all subsequent validators.

---

## Section 3 — Phase 7 Keyed-Validator-Instance Pattern

**Registration** (`CodeProjectAi.PluginRegistrar`): registers each named validator instance as a
keyed singleton `IValidationPlugin` using the instance key from the `Validators` config section.

**Resolution in `EventPump`** (`src/FrigateRelay.Host/EventPump.cs`, lines 104–106):

```csharp
IReadOnlyList<IValidationPlugin> validators = entry.Validators is { Count: > 0 } keys
    ? keys.Select(k => _services.GetRequiredKeyedService<IValidationPlugin>(k)).ToArray()
    : Array.Empty<IValidationPlugin>();
```

- Resolution is keyed by the string in `ActionEntry.Validators[i]`.
- `EventPump` does not distinguish whether the `ActionEntry` came from an inline config or a profile
  expansion — it only sees the resolved `sub.Actions` list at dispatch time.
- **Confirmed: no EventPump changes are needed for Phase 8.** Profile expansion produces
  `ActionEntry` records with `Validators: [...]` identical in shape to inline entries. The dispatcher
  and EventPump are unaffected.
- `StartupValidation.ValidateValidators` also iterates `sub.Actions` directly (line 109–128) and
  will work correctly post-expansion with no changes.

---

## Section 4 — SnapshotResolver — No Changes Needed

**Location**: `src/FrigateRelay.Host/Snapshots/SnapshotResolver.cs` (`internal sealed class`).

```csharp
public async ValueTask<SnapshotResult?> ResolveAsync(
    EventContext context,
    string? perActionProviderName,
    string? subscriptionDefaultProviderName,
    CancellationToken cancellationToken)
```

Three-tier lookup (lines 40–61): per-action → subscription → global `_options.DefaultProviderName`.

- `ProfileOptions` carries only `Actions` (per D4/D5) — no `DefaultSnapshotProvider` field.
- Profile expansion writes `ActionEntry` records into `SubscriptionOptions.Actions`; the
  subscription's own `DefaultSnapshotProvider` is unchanged.
- `IActionDispatcher.EnqueueAsync` (called from EventPump line 108–110) passes
  `entry.SnapshotProvider` and `sub.DefaultSnapshotProvider` — both already present for
  profile-expanded entries.
- **Confirmed: `SnapshotResolver` needs zero changes for Phase 8.**

---

## Section 5 — IConfiguration.Bind + TypeConverter Mechanics (ID-12 Fix)

**Root cause**: `ConfigurationBinder` reads each config path child as a scalar string. When the
target type (`ActionEntry`) has no registered `TypeConverter`, the binder cannot convert the scalar
and produces a default/empty object. `[JsonConverter]` is never consulted by `ConfigurationBinder`.

**Fix approach** (per D2 and ID-12 recommendation — Option 1):

```csharp
[TypeConverter(typeof(ActionEntryTypeConverter))]
public sealed record ActionEntry(...)
```

```csharp
public sealed class ActionEntryTypeConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? ctx, Type sourceType)
        => sourceType == typeof(string) || base.CanConvertFrom(ctx, sourceType);

    public override object ConvertFrom(ITypeDescriptorContext? ctx, CultureInfo? culture, object value)
        => value is string s ? new ActionEntry(s) : base.ConvertFrom(ctx, culture, value)!;
}
```

**Where `[TypeConverter]` is applied**: directly on the `ActionEntry` record (the same file,
alongside the existing `[JsonConverter]` attribute). Both attributes co-exist without conflict —
they serve disjoint call paths.

**`TypeDescriptor.AddAttributes` at startup**: not needed when `[TypeConverter]` is on the type
itself. The attribute approach is the canonical pattern and works with `IConfiguration.Bind`
because `ConfigurationBinder` calls `TypeDescriptor.GetConverter(type)` which reads type-level
attributes.

**Files the architect creates/modifies for the fix**:

| File | Action |
|------|--------|
| `src/FrigateRelay.Host/Configuration/ActionEntry.cs` | Add `[TypeConverter(typeof(ActionEntryTypeConverter))]` attribute |
| `src/FrigateRelay.Host/Configuration/ActionEntryTypeConverter.cs` | New file |
| `tests/FrigateRelay.Host.Tests/Configuration/ActionEntryTypeConverterTests.cs` | New file (~3 unit tests) |
| `tests/FrigateRelay.IntegrationTests/` | Add or extend a test fixture using string-array form |
| `CLAUDE.md` | Update ID-12 invariant block with "Phase 8 closed ID-12" note |

No host pipeline or DI changes are needed — the TypeConverter is invoked automatically by the
binder infrastructure once the attribute is present.

**Source**: Microsoft.Extensions.Configuration.Binder reads converters via
`TypeDescriptor.GetConverter(targetType)` for non-collection scalar properties. The canonical
pattern is documented at
https://learn.microsoft.com/en-us/dotnet/api/system.componentmodel.typeconverter and
https://learn.microsoft.com/en-us/dotnet/core/extensions/configuration-api#type-conversions (the
latter confirms the binder delegates to `TypeConverter` for complex-from-string binding).

---

## Section 6 — Test Project Layout

**Existing `tests/FrigateRelay.Host.Tests/` subdirectories** (from csproj and Phase history):

```
tests/FrigateRelay.Host.Tests/
├── Configuration/           # ActionEntryJsonConverterTests.cs exists here (Phase 5)
├── Dispatch/                # ChannelActionDispatcherTests.cs
├── Matching/                # SubscriptionMatcherTests.cs, DedupeCacheTests.cs
├── Snapshots/               # SnapshotResolverTests.cs
├── Startup/                 # StartupValidationTests.cs (Phase 7)
├── Usings.cs
└── FrigateRelay.Host.Tests.csproj
```

**New Phase 8 test files** (matching the existing `Configuration/` subdirectory pattern):

| Path | Purpose |
|------|---------|
| `tests/FrigateRelay.Host.Tests/Configuration/ProfileResolutionTests.cs` | Profile binding, expansion, fail-fast validation (D1 mutual-exclusion, undefined profile, no profile+no actions) |
| `tests/FrigateRelay.Host.Tests/Configuration/ActionEntryTypeConverterTests.cs` | String-array form binds, object form still binds, mixed array binds |
| `tests/FrigateRelay.Host.Tests/Configuration/ConfigSizeParityTest.cs` | Loads `legacy.conf` + `appsettings.Example.json`, asserts JSON char count ≤ 60% of INI char count |

**Fixture files** (note: ROADMAP uses `Config/` while a subdirectory under the test project is
the existing pattern for integration fixtures):

| Path | Purpose | MSBuild item needed |
|------|---------|---------------------|
| `tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf` | Sanitized real INI (user-provided per D6) | `<None Update="Fixtures/legacy.conf"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></None>` |
| `config/appsettings.Example.json` | JSON profile-shape equivalent (per CONTEXT-8 note 3) | Referenced by path in test; does NOT need CopyToOutput if test reads from project root |

**`FrigateRelay.TestHelpers` availability**: confirmed referenced in `FrigateRelay.Host.Tests.csproj`
(line 34: `<ProjectReference Include="..\FrigateRelay.TestHelpers\FrigateRelay.TestHelpers.csproj" />`).
`CapturingLogger<T>` is available via `global using FrigateRelay.TestHelpers;` in `Usings.cs`.

**`ConfigSizeParityTest` file path resolution**: the test should locate `legacy.conf` relative to
`AppContext.BaseDirectory` (the test exe output directory) using the `CopyToOutputDirectory`
MSBuild item. The `config/appsettings.Example.json` should be read relative to the repo root —
the test can use `Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "config",
"appsettings.Example.json")` or embed the path as a test data file. Simplest: add both files as
`<None CopyToOutputDirectory=PreserveNewest>` items in the test csproj and use `AppContext.BaseDirectory`.

---

## Section 7 — Legacy INI Schema Reference

**Configuration system**: SharpConfig 3.2.9.1 reading `FrigateMQTTProcessingService.conf` into
typed POCOs.

**Observed section structure** (from INTEGRATIONS.md, STACK.md, CONVENTIONS.md):

```ini
[ServerSettings]
Server = <mqtt-broker-host>
BlueIrisImages = http://<bi-host>:<port>/image/
FrigateApi = http://<frigate-host>:<port>

[PushoverSettings]
AppToken = <token>
UserKey = <key>
NotifySleepTime = 30

[SubscriptionSettings]
; One [SubscriptionSettings] section per subscription — repeated N times.
Name = <subscription-name>
Camera = http://<bi-host>:<port>/json?... (full trigger URL)
CameraShortName = <bi-short-name>
Zone = <zone-name-or-empty>
Label = <object-label>
; No cooldown field observed — NotifySleepTime in PushoverSettings is global
```

**Repetition pattern**: Each subscription repeats the full `[SubscriptionSettings]` block. With 9
subscriptions where most share the same BlueIris trigger URL template and Pushover settings, the
repetition is in the `Camera` (full URL per subscription) and `CameraShortName` fields. The INI has
no profile concept — every subscription is self-contained.

**What Phase 8 eliminates**: With profiles, subscriptions sharing the same action-list shape (e.g.,
all `[BlueIris, Pushover]` actions with the same snapshot provider) reference one profile rather
than repeating the action-list inline. The `appsettings.Example.json` character count vs. INI
character count is the Phase 8 success criterion.

**Sanitization rules** are defined in CONTEXT-8.md D3 and D6. The SANITIZATION-CHECKLIST.md the
architect produces must cover: IP → `example.local` or RFC 5737 prefix; AppToken/UserKey →
`<redacted>`; username:password segments → `<user>:<pass>@`; preserve all camera/zone/label names
and structural whitespace.

---

## Section 8 — Existing Fixture and Config Locations

**Existing `appsettings*.json` files**:

| Path | Role |
|------|------|
| `src/FrigateRelay.Host/appsettings.json` | Production base config — contains only `Logging` section; no plugin config, no secrets |
| `tests/FrigateRelay.IntegrationTests/Fixtures/` | Integration test fixture configs (WireMock stubs, Mosquitto settings) |

**`config/` directory**: does NOT exist yet. Phase 8 creates `config/appsettings.Example.json`
(per CONTEXT-8 note 3 and ROADMAP Phase 8 deliverables).

**Integration test fixture pattern**: tests under `tests/FrigateRelay.IntegrationTests/Fixtures/`
use JSON config fragments loaded via `IConfigurationBuilder.AddJsonFile(path)`. Phase 8's
`ConfigSizeParityTest` follows the same pattern for loading both files.

---

## Section 9 — CI Files Phase 8 Touches

**`.github/scripts/run-tests.sh`** auto-discovers test projects (glob `tests/*.Tests/*.Tests.csproj`).
The `FrigateRelay.Host.Tests` project already exists and is discovered. **Phase 8 needs zero
`ci.yml` or `Jenkinsfile` edits** — no new test project is added.

**`secret-scan.yml` and `secret-scan.sh`**: the `legacy.conf` fixture presents a risk because it
contains redacted-but-shaped strings (e.g. `AppToken = <redacted>`). The existing patterns are:

```
'AppToken\s*=\s*[A-Za-z0-9]{20,}'   # requires 20+ alphanumeric chars after =
'UserKey\s*=\s*[A-Za-z0-9]{20,}'    # same
'192\.168\.[0-9]{1,3}\.[0-9]{1,3}'  # RFC-1918 IP pattern
```

**`<redacted>` strings do NOT match these patterns** (angle brackets are not `[A-Za-z0-9]`; the
literal string `<redacted>` is 9 chars and contains `<>`). RFC 5737 IPs (`192.0.2.x`,
`198.51.100.x`, `203.0.113.x`) do NOT match the `192\.168\.` pattern. **The sanitized `legacy.conf`
will not trigger the secret-scan if sanitization rules are followed correctly.**

**Recommendation**: No changes to `secret-scan.sh` are needed IF the fixture uses `<redacted>` for
tokens and RFC 5737 IPs. Add a comment in `secret-scan.sh` noting that `tests/` fixtures are
scanned (they are NOT in the exclusion list), so the sanitization rules are the only safeguard.
The architect may optionally add `tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf` to the
scan exclusions list as belt-and-suspenders, but it is not required if sanitization is correct.

---

## Section 10 — Visibility Sweep Scope Check (ID-2 + ID-10)

| Type | File | Current Visibility | Phase 8 Modifies? | External Consumers? |
|------|----|----|----|-----|
| `ActionEntry` | `Configuration/ActionEntry.cs` | `public` (ID-10) | Yes — add `[TypeConverter]` | None outside Host + tests |
| `ActionEntryJsonConverter` | `Configuration/ActionEntryJsonConverter.cs` | `public` (ID-10) | No | None |
| `SnapshotResolverOptions` | `Snapshots/SnapshotResolverOptions.cs` | `public` (ID-10) | No | None |
| `SubscriptionOptions` | `Configuration/SubscriptionOptions.cs` | `public` (ID-10) | Yes — add `Profile` property | None |
| `HostSubscriptionsOptions` | `Configuration/HostSubscriptionsOptions.cs` | `public` (ID-10) | Yes — add `Profiles` property | None |
| `IActionDispatcher` | `Dispatch/IActionDispatcher.cs` | `public` (ID-2) | No | None |
| `DispatcherOptions` | `Dispatch/DispatcherOptions.cs` | `public` (ID-2) | No | None |
| `DedupeCache` | `Matching/DedupeCache.cs` | `public` (ID-10 cascade) | No | None |
| `SubscriptionMatcher` | `Matching/SubscriptionMatcher.cs` | `public` (ID-10 cascade) | No | None |

**Conclusion**: Phase 8 already touches `ActionEntry`, `SubscriptionOptions`, and
`HostSubscriptionsOptions`. Adding the full ID-2 + ID-10 visibility sweep (internalizing all of the
above) in the same phase is mechanically safe — no external consumers exist. CONTEXT-8 note 6
recommends folding this in. The architect should confirm scope with the user since it adds ~8 files
touched for visibility changes alone.

**CS0053 cascade risk**: If `SubscriptionOptions.Actions` (type `IReadOnlyList<ActionEntry>`) is
`public` and `ActionEntry` is internalized, the compiler will reject it. The only safe sweep order
is: internalize everything simultaneously (all in one pass), or internalize none. The entire group
must move to `internal` together.

---

## New Types Phase 8 Creates

| Type | File | Proposed Visibility | Notes |
|------|------|---------------------|-------|
| `ProfileOptions` | `Configuration/ProfileOptions.cs` | `internal` (new, can start internal) | `Actions: IReadOnlyList<ActionEntry>` only — no other properties per D5 |
| `ActionEntryTypeConverter` | `Configuration/ActionEntryTypeConverter.cs` | `internal` | Closes ID-12 |

`ProfileOptions` can be `internal` from creation since it is referenced only by
`HostSubscriptionsOptions.Profiles` — if that property is also `internal` (post-sweep) there is
no CS0053 issue.

---

## Open Questions for the Architect

1. **Collect-all vs. fail-on-first for profile validation**: CONTEXT-8 note 1 requires collecting
   all errors before throwing. The existing `ValidateActions`, `ValidateSnapshotProviders`, and
   `ValidateValidators` all throw on the **first** error. Should Phase 8 retrofit all three to
   collect-all (scope increase), or implement only the profile validator as collect-all and leave
   the Phase 7 validators as-is (inconsistent behavior)? **Decision required.**

2. **Profile expansion timing**: Should profile resolution mutate the `HostSubscriptionsOptions`
   in-place before the existing validators run, or should a resolved copy be passed explicitly?
   In-place mutation of an options object is unusual; a resolved-copy pattern is cleaner but
   requires threading the resolved list through `ValidateStartup`. **Recommend resolved-copy** —
   document for the architect.

3. **ID-2 + ID-10 visibility sweep scope**: CONTEXT-8 note 6 says "recommend: fold into Phase 8 —
   but flag for user confirmation if scope feels stretched." The sweep touches 9 files with
   visibility-only changes. Given the 2–3 hour Phase 8 estimate, this may stretch the phase.
   **Decision required from user.**

4. **`legacy.conf` fixture dependency**: Per D6, the builder must detect the missing
   `tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf` and fail with a clear message pointing to
   `SANITIZATION-CHECKLIST.md`. The `ConfigSizeParityTest` should `Assert.Inconclusive` (not
   `Assert.Fail`) when the fixture is absent — failing CI before the user has provided their real
   conf is a false failure. **Confirm the desired behavior.**

5. **`appsettings.Example.json` path for the parity test**: The `config/` directory is at the repo
   root. The test exe runs from `tests/FrigateRelay.Host.Tests/bin/Release/net10.0/`. Relative path
   resolution across 5 `..` hops is fragile. Best option: add `config/appsettings.Example.json` as
   a linked file in the test csproj with `CopyToOutputDirectory=PreserveNewest`, avoiding path
   climbing. **Architect should decide the linking approach.**

---

## Sources

1. `src/FrigateRelay.Host/Configuration/ActionEntry.cs` — current type surface (read 2026-04-26)
2. `src/FrigateRelay.Host/Configuration/ActionEntryJsonConverter.cs` — ID-12 root-cause code (read 2026-04-26)
3. `src/FrigateRelay.Host/Configuration/SubscriptionOptions.cs` — current shape, no Profile property (read 2026-04-26)
4. `src/FrigateRelay.Host/Configuration/HostSubscriptionsOptions.cs` — no Profiles property (read 2026-04-26)
5. `src/FrigateRelay.Host/StartupValidation.cs` — Phase 7 validator pattern (read 2026-04-26)
6. `src/FrigateRelay.Host/HostBootstrap.cs` — ValidateStartup wiring (read 2026-04-26)
7. `src/FrigateRelay.Host/EventPump.cs` — keyed-validator resolution, dispatcher call (read 2026-04-26)
8. `src/FrigateRelay.Host/Dispatch/IActionDispatcher.cs` — dispatch interface (read 2026-04-26)
9. `src/FrigateRelay.Host/Dispatch/DispatcherOptions.cs` — visibility (read 2026-04-26)
10. `src/FrigateRelay.Host/Snapshots/SnapshotResolver.cs` — 3-tier resolution (read 2026-04-26)
11. `src/FrigateRelay.Host/Snapshots/SnapshotResolverOptions.cs` — visibility (read 2026-04-26)
12. `src/FrigateRelay.Host/Matching/DedupeCache.cs` — visibility (read 2026-04-26)
13. `src/FrigateRelay.Host/Matching/SubscriptionMatcher.cs` — visibility (read 2026-04-26)
14. `tests/FrigateRelay.Host.Tests/FrigateRelay.Host.Tests.csproj` — test deps, TestHelpers ref (read 2026-04-26)
15. `.github/scripts/secret-scan.sh` — patterns list, exclusion rules (read 2026-04-26)
16. `.github/workflows/secret-scan.yml` — workflow structure (read 2026-04-26)
17. `.shipyard/phases/8/CONTEXT-8.md` — binding decisions D1–D6 (read 2026-04-26)
18. `.shipyard/ISSUES.md` — ID-2, ID-10, ID-12 descriptions (read 2026-04-26)
19. `.shipyard/codebase/STACK.md` — legacy SharpConfig INI structure (read 2026-04-26)
20. `.shipyard/codebase/CONVENTIONS.md` — SharpConfig POCO mapping, INI section shapes (read 2026-04-26)
21. `.shipyard/codebase/INTEGRATIONS.md` — SubscriptionSettings field names, repetition evidence (read 2026-04-26)
22. `.shipyard/PROJECT.md` — S2 config decision, Success Criterion #2 (read 2026-04-26)
23. `.shipyard/ROADMAP.md` — Phase 8 deliverables and success criteria (read 2026-04-26)
24. Microsoft Learn — TypeConverter + IConfiguration.Bind integration:
    https://learn.microsoft.com/en-us/dotnet/api/system.componentmodel.typeconverter
    https://learn.microsoft.com/en-us/dotnet/core/extensions/configuration-api#type-conversions
