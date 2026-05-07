# Research: Phase 15 — v1.2.1 Hardening Patch Surface Inventory

**Date:** 2026-05-07
**Scope:** Issues #8, #13, #14, #15, #19, #20, #24, #25, #26, #27
**Branch:** `main` (post-v1.2.0 tag, Phase 14 complete)

---

## 1. Surface Inventory

---

### Issue #13 — Newline sanitization in `StartupValidation.cs`

**Target file:** `src/FrigateRelay.Host/StartupValidation.cs`

**Current `errors.Add(...)` call sites and classification:**

All sites are in `StartupValidation.cs` (passes 1–4) and `ProfileResolver.cs` (pass 0 for profile resolution):

| Line | File | Interpolated value(s) | Operator-controlled? |
|------|------|-----------------------|----------------------|
| 77 | StartupValidation.cs | `endpoint` — from `config["Otel:OtlpEndpoint"]` or env var | YES |
| 81 | StartupValidation.cs | `seq` — from `config["Serilog:Seq:ServerUrl"]` | YES |
| 108 | StartupValidation.cs | `path` — from `config["Serilog:WriteTo:N:Args:path"]` | YES |
| 110 | StartupValidation.cs | `path` (same) | YES |
| 113 | StartupValidation.cs | `path` (same) + `allowlist` (literal array, NOT operator-controlled) | path=YES, allowlist=NO |
| 137–142 | StartupValidation.cs | `sub.Name`, `entry.Plugin` + `registeredNames` join (plugin names are DI-registered — from config) | sub.Name YES, entry.Plugin YES, registeredNames=conditionally YES |
| 163–167 | StartupValidation.cs | `globalDefaultProviderName`, `registeredNames` | YES / conditionally YES |
| 173–177 | StartupValidation.cs | `sub.Name`, `sub.DefaultSnapshotProvider`, `registeredNames` | all YES |
| 183–187 | StartupValidation.cs | `sub.Name`, `entry.Plugin`, `entry.SnapshotProvider`, `registeredNames` | all YES |
| 216–219 | StartupValidation.cs | `key` (validator instance key from config) | YES |
| 41–42 | ProfileResolver.cs | `sub.Name` | YES |
| 49–50 | ProfileResolver.cs | `sub.Name` | YES |
| 61–64 | ProfileResolver.cs | `sub.Name`, `profileName`, `defined` (profile key list from config) | all YES |

**Internal (literal/constant) values not needing sanitization:**

- `allowlist` array at line 113: literals `"/var/log/frigaterelay/"`, `"/app/logs/"` — hardcoded in source, not operator-controlled.
- Error message prose (static string fragments around interpolated values) — not operator-controlled.
- `registeredNames` at lines 142, 167, 177, 187: these are names of DI-registered plugins/providers — they originate from `IActionPlugin.Name` / `ISnapshotProvider.Name` properties, which are hardcoded in plugin source (e.g. `"BlueIris"`, `"Frigate"`). However, the registered list is included in error messages for operator guidance. Low risk but defensively should also go through `Sanitize` since future plugins are operator-authored.

**Design constraint (D4):** Helper is `private static string Sanitize(string? value)` inside `StartupValidation.cs`. No new class, no extension method.

**Gotcha — `ProfileResolver.cs` is a separate file.** Its `errors.Add` calls at lines 41, 49, 61–64 also interpolate operator-controlled `sub.Name` and `profileName`. Two paths forward:
1. Move `Sanitize` to `internal static` so `ProfileResolver` can call it (or keep it `private static` in `StartupValidation` and have `ProfileResolver` take a `Func<string,string> sanitize` parameter — unnecessary complexity).
2. Make `Sanitize` `internal static` on `StartupValidation` and have `ProfileResolver` call it directly (both are in `FrigateRelay.Host`, same namespace root — this is the simpler choice).

The architect must decide: `private static` (D4 literal) means `ProfileResolver.cs` errors are not sanitized unless the helper is promoted to `internal static`. Since both files are in the same assembly and the test project already has `InternalsVisibleTo`, promoting to `internal static` is the recommended path.

**No existing `Regex` or sanitization helper** exists anywhere in `StartupValidation.cs` — confirmed by grep returning zero matches.

---

### Issue #14 — Empty/whitespace plugin-name guard in `ActionEntryTypeConverter`

**Target file:** `src/FrigateRelay.Host/Configuration/ActionEntryTypeConverter.cs`

**Current shape (lines 32–40):**

```csharp
public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    => value is string s
        ? new ActionEntry(s)
        : base.ConvertFrom(context, culture, value)!;
```

No guard exists. An empty string or whitespace-only string `s` proceeds directly to `new ActionEntry(s)`, which produces a valid `ActionEntry` with `Plugin = ""` or `Plugin = "   "`. Downstream, `StartupValidation.ValidateActions` (line 135) checks `registeredNames.Contains(entry.Plugin)` and produces "unknown plugin" error — one indirection removed from the actual problem.

**JSON-side counterpart (`ActionEntryJsonConverter.cs` lines 44–46):**

```csharp
if (string.IsNullOrEmpty(dto.Plugin))
    throw new JsonException("ActionEntry object form requires a non-empty 'Plugin' field.");
```

The JSON path rejects empty at the converter boundary (throws `JsonException`). The `[TypeConverter]`/`IConfiguration.Bind` path does not. #14 brings parity.

**Required fix:** Insert `string.IsNullOrWhiteSpace(s)` guard before `new ActionEntry(s)` that throws `FormatException` with a message identifying the offending value. `FormatException` is the idiomatic exception for `TypeConverter.ConvertFrom` failures (matches `TypeConverter` contract; `ConfigurationBinder` surfaces it as a configuration binding error).

**Existing test file:** `tests/FrigateRelay.Host.Tests/Configuration/ActionEntryTypeConverterTests.cs`

**Current test count: 6 tests** (3 original ID-12 tests + 3 ParallelValidators round-trip tests added in Phase 14). Test naming convention: `Method_Condition_Expected` underscore style confirmed (`Bind_StringArrayActions_PopulatesEntries`, etc.).

**New tests to add (per ROADMAP):** 2 — one for empty string, one for whitespace-only string.

**Gotcha:** The JSON path uses `string.IsNullOrEmpty` (not `IsNullOrWhiteSpace`). The `[TypeConverter]` path should use `IsNullOrWhiteSpace` to be stricter (whitespace-only string `"   "` is just as invalid as empty and `IConfiguration.Bind` can produce it). This is intentional — no alignment needed with the JSON converter's narrower check.

---

### Issue #19 — Name-allowlist enforcement (`ValidateNames` pass)

**Target file:** `src/FrigateRelay.Host/StartupValidation.cs` (new `ValidateNames` pass + new `internal static` method)

**Regex (D1):** `^[A-Za-z0-9_. -]+$` — permits alphanumeric, underscore, dot, space, hyphen. Rejects CRLF, null, control chars, Unicode punctuation, slashes, colons, at-signs.

**No existing compiled `Regex` member** in `StartupValidation.cs` — grep returned zero matches. New `private static readonly Regex` (or `[GeneratedRegex]`) goes in `StartupValidation.cs`.

**Four name kinds and where they are enumerable from `HostSubscriptionsOptions`:**

| Name kind | Source path | Access pattern |
|-----------|-------------|----------------|
| Subscription names | `options.Subscriptions[i].Name` | Iterate `HostSubscriptionsOptions.Subscriptions` |
| Profile names | `options.Profiles.Keys` | Iterate `HostSubscriptionsOptions.Profiles.Keys` |
| Plugin names (per action) | `options.Subscriptions[i].Actions[j].Plugin` | Nested iterate; also from profile actions post-resolution |
| Validator instance keys | `options.Subscriptions[i].Actions[j].Validators[k]` | Nested iterate; these are the `instance.Key` values from the top-level `Validators:*` config section |

**Important:** `ValidateNames` operates on the **raw** `options` (pre-ProfileResolver) because profile names must also be validated, and because the new pass runs before pass 1 (ProfileResolver). The architect must decide ordering — validating names before ProfileResolver means subscriptions referencing a bad-name profile fail name-check first (that is fine; errors accumulate). Alternatively the new pass could operate on `resolved` but then profile names aren't checked.

**Recommended ordering:** Call `ValidateNames(options, errors)` **before** `ProfileResolver.Resolve` (i.e. as a new Pass 0a before the current Pass 0 observability check, or between Pass 0 and Pass 1). This ensures names are checked on the raw config graph and profile keys are included.

**appsettings.Example.json analysis — will names pass D1 regex?**

File at `config/appsettings.Example.json`. Subscription names present:
- `"DriveWay Person"` — has space: PASSES `^[A-Za-z0-9_. -]+$`
- `"DriveWay Car"` — PASSES
- `"Backyard Person"` — PASSES
- `"Garage Person"` — PASSES
- `"Front Door Porch"` — PASSES
- `"Back Yard Porch"` — PASSES
- `"Front Door"` — PASSES
- `"Front Yard Potch"` — PASSES (note: typo "Potch" in existing file — not a regex issue)
- `"Front Yard"` — PASSES

Profile name: `"Standard"` — PASSES.

Plugin names in actions: `"BlueIris"`, `"Pushover"` — both PASS.

**No updates to `appsettings.Example.json` are needed.** All existing names pass D1 regex.

`docker/appsettings.Smoke.json` has no subscriptions and no plugin/profile names — nothing to validate.

**Gotcha — `ValidateNames` must enumerate validator keys from config, not from options.** The `HostSubscriptionsOptions` type does not carry the top-level `Validators` dictionary; that section is consumed directly by plugin registrars. The validator instance keys appear in `ActionEntry.Validators` (type `IReadOnlyList<string>?`). So `ValidateNames` can reach validator keys via `options.Subscriptions[i].Actions[j].Validators[k]` — these are the keys typed by the operator (e.g. `"strict-person"`, `"driveway-cpai"`). Profile actions are not in `options.Subscriptions` pre-resolution, but profile action validator keys can be reached via `options.Profiles.Values.SelectMany(p => p.Actions).SelectMany(a => a.Validators ?? [])`.

**Gotcha — `ValidateAll` signature change.** `ValidateNames` needs `HostSubscriptionsOptions options` (already passed to `ValidateAll`) — no new parameter needed. The pass is appended to the accumulator pattern without changing `ValidateAll`'s public signature.

---

### Issue #20 — OTLP endpoint scheme restriction

**Target file:** `src/FrigateRelay.Host/StartupValidation.cs`, method `ValidateObservability` (lines 71–82).

**Current code (lines 75–77):**

```csharp
var endpoint = config["Otel:OtlpEndpoint"] ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
if (!string.IsNullOrWhiteSpace(endpoint) && !Uri.TryCreate(endpoint, UriKind.Absolute, out _))
    errors.Add($"Otel:OtlpEndpoint '{endpoint}' is not a valid absolute URI.");
```

The existing check only validates that the value is a parseable absolute URI. After the fix, a second check is needed: when `Uri.TryCreate` succeeds, assert `uri.Scheme` is in `{"http", "https", "grpc"}`. The `out _` discard must change to `out var uri` to access the scheme.

**Exact change shape:**

```csharp
if (!string.IsNullOrWhiteSpace(endpoint))
{
    if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        errors.Add($"Otel:OtlpEndpoint '{endpoint}' is not a valid absolute URI.");
    else if (uri.Scheme is not ("http" or "https" or "grpc"))
        errors.Add($"Otel:OtlpEndpoint '{endpoint}' has unsupported scheme '{uri.Scheme}'; allowed: http, https, grpc.");
}
```

Note: The `Serilog:Seq:ServerUrl` check (lines 79–81) does NOT need a scheme restriction — Seq only speaks HTTP/HTTPS and `Uri.TryCreate` already validates format. Scope is OTLP only.

**Note on `endpoint` value in error message:** This is operator-controlled text flowing into `errors.Add`. After #13's `Sanitize` helper is added, this line must also wrap `endpoint` in `Sanitize(...)` — coordinate with #13 work.

**Existing test file:** `tests/FrigateRelay.Host.Tests/Observability/ValidateObservabilityTests.cs`

**Current test count: 3 tests.**
- `MalformedOtlpEndpoint_ProducesOneError_ContainingKeyName` — malformed URI
- `MalformedSeqServerUrl_ProducesOneError_ContainingKeyName` — malformed URI
- `ValidAbsoluteUris_ProduceZeroErrors` — valid URIs (tests `http://otel-collector:4317` — this will still pass post-fix since `http` is allowed)

**New tests to add (per ROADMAP):** 3 — rejected scheme (e.g. `file:///tmp/x`), allowed scheme `grpc://`, allowed scheme `https://`.

**Gotcha:** The `ValidAbsoluteUris_ProduceZeroErrors` test at line 79 uses `http://otel-collector:4317` — scheme `http` — this continues to pass after the fix. No test regression risk.

**Gotcha:** The `grpc` scheme is an informal scheme (not an IANA-registered scheme for gRPC). `Uri.TryCreate("grpc://host:4317", ...)` does parse successfully and returns `uri.Scheme = "grpc"` — confirmed by .NET URI handling of unknown schemes. No issue.

---

### Issue #8 — `--coverage` branch arg parity in `run-tests.sh`

**Target file:** `.github/scripts/run-tests.sh`

**Current `--coverage` branch (lines 67–70):**

```bash
dotnet run --project "$proj" -c "$CONFIG" --no-build -- \
  --coverage \
  --coverage-output-format cobertura \
  --coverage-output "coverage/${name}/coverage.cobertura.xml"
```

`"${PASS_THROUGH_ARGS[@]}"` is **absent** from this invocation.

**Non-coverage branch (line 86):**

```bash
dotnet run --project "$proj" -c "$CONFIG" --no-build "${PASS_THROUGH_ARGS[@]}"
```

`"${PASS_THROUGH_ARGS[@]}"` IS present here.

**Fix:** Append `"${PASS_THROUGH_ARGS[@]}"` to the `dotnet run` invocation in the `--coverage` branch. Placement: after the `--coverage-output` arg, still inside the `-- ...` MTP passthrough section. The `--` separator is already present at line 67; `PASS_THROUGH_ARGS` entries like `--filter "ClassName"` are MTP args and belong after `--`.

**Corrected block:**

```bash
dotnet run --project "$proj" -c "$CONFIG" --no-build -- \
  --coverage \
  --coverage-output-format cobertura \
  --coverage-output "coverage/${name}/coverage.cobertura.xml" \
  "${PASS_THROUGH_ARGS[@]}"
```

**No test changes.** No CI yaml changes — the script is already called correctly by both `ci.yml` and `Jenkinsfile`.

---

### Issue #15 — RFC 1918 fixture coverage

**Target files:**
- `.github/scripts/secret-scan.sh` — PATTERNS / LABELS arrays (lines 29–47)
- `.github/secret-scan-fixture.txt` — fixture file (lines 22–23 area)

**Current state of PATTERNS/LABELS arrays:**

```bash
LABELS=(
  "AppToken"
  "UserKey"
  "RFC-1918 IP"          # covers only 192.168.x.x
  "Generic apiKey"
  "Bearer token"
  "GitHub PAT"
  "AWS Access Key"
)

PATTERNS=(
  'AppToken\s*=\s*[A-Za-z0-9]{20,}'
  'UserKey\s*=\s*[A-Za-z0-9]{20,}'
  '192\.168\.[0-9]{1,3}\.[0-9]{1,3}'   # index 2 — only 192.168
  'api[Kk]ey\s*[=:]\s*["'"'"']?[A-Za-z0-9_\-]{20,}'
  'Bearer\s+[A-Za-z0-9._\-]{20,}'
  'ghp_[A-Za-z0-9]{36}'
  'AKIA[A-Z0-9]{16}'
)
```

**Required additions:** Two new parallel-array entries (appended after index 6):

| New index | Label | Pattern (ERE) |
|-----------|-------|---------------|
| 7 | `RFC-1918 10.x.x.x` | `10\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}` |
| 8 | `RFC-1918 172.16-31.x.x` | `172\.(1[6-9]\|2[0-9]\|3[0-1])\.[0-9]{1,3}\.[0-9]{1,3}` |

**Required fixture additions** (2 new lines at end of `.github/secret-scan-fixture.txt`):

```
# Pattern 8 — RFC-1918 10.x.x.x (10\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3})
server_url=http://10.0.1.100:8080/api # secret-scan:fixture

# Pattern 9 — RFC-1918 172.16-31.x.x (172\.(1[6-9]|2[0-9]|3[0-1])\.[0-9]{1,3}\.[0-9]{1,3})
server_url=http://172.16.0.50:8080/api # secret-scan:fixture
```

**Tripwire contract:** `secret-scan.yml` job `tripwire-self-test` (lines 23–33) calls `bash .github/scripts/secret-scan.sh selftest`. The selftest mode iterates `PATTERNS[i]` and does `git grep -qE "$pattern" -- "$FIXTURE"`. Both new fixture lines must match their respective patterns or the tripwire fails.

**Scan exclusions already in place:** `.shipyard/`, `CLAUDE.md`, `.github/secret-scan-fixture.txt` are excluded from the `scan` mode. No exclusion changes needed — the new patterns detect the same "committed IP" shape.

**Gotcha:** The `scan` mode exclusion at line 74 is path-based. Any occurrence of `10.x.x.x` or `172.16-31.x.x` in non-excluded source files will now fail the scan. Confirm no such IPs exist in `src/`, `tests/`, `docker/`, `.github/` outside the fixture. The existing `192.168.x.x` scan already covers this shape — the repo is clean of hard-coded IPs by architectural invariant.

**Gotcha — ERE syntax in secret-scan.sh.** The alternation `|` in bash single-quoted strings doesn't need escaping in ERE, but the script passes patterns via `"$pattern"` double-quoted to `git grep -nE`. The pattern `172\.(1[6-9]|2[0-9]|3[0-1])...` uses `|` inside a character group bracket alternative — valid ERE. In the bash array the single-quoted string stores the literal pipe character correctly.

---

### Issue #24 — SHA-pin 3rd-party GitHub Actions

**Target files:** `.github/workflows/release.yml`, `.github/workflows/ci.yml`, `.github/workflows/secret-scan.yml`

**All `uses: action@vN` references found:**

From `release.yml`:

| Step | Current reference | Line |
|------|-------------------|------|
| Checkout | `actions/checkout@v6` | 52 |
| Set up QEMU | `docker/setup-qemu-action@v4` | 56 |
| Set up Docker Buildx | `docker/setup-buildx-action@v4` | 60 |
| Log in to GHCR | `docker/login-action@v4` | 63 |
| Extract Docker metadata | `docker/metadata-action@v6` | 72 |
| Build amd64 image for smoke | `docker/build-push-action@v7` | 86 |
| Build and push multi-arch image | `docker/build-push-action@v7` | 147 |

From `ci.yml`:

| Step | Current reference | Line |
|------|-------------------|------|
| Checkout | `actions/checkout@v6` | 39 |
| Setup .NET | `actions/setup-dotnet@v5` | 42 |

From `secret-scan.yml`:

| Step | Current reference | Line |
|------|-------------------|------|
| Checkout (scan job) | `actions/checkout@v6` | 16 |
| Checkout (tripwire job) | `actions/checkout@v6` | 28 |

**Total distinct action references to pin:** 7 unique action versions across 3 files:
1. `actions/checkout@v6` — appears in all 3 workflow files (4 total `uses:` lines)
2. `actions/setup-dotnet@v5` — ci.yml only
3. `docker/setup-qemu-action@v4` — release.yml only
4. `docker/setup-buildx-action@v4` — release.yml only
5. `docker/login-action@v4` — release.yml only
6. `docker/metadata-action@v6` — release.yml only
7. `docker/build-push-action@v7` — release.yml only (2 `uses:` lines)

**ROADMAP says 6 actions in 2 files** (release.yml + ci.yml) — `secret-scan.yml` was not explicitly mentioned but also has `actions/checkout@v6`. The architect should decide whether to pin `secret-scan.yml` as well; the ROADMAP greppable invariant `git grep -nE 'uses:\s*[^@\s]+@v[0-9]' .github/workflows/` covers ALL workflow files, so `secret-scan.yml` must also be pinned for the invariant to pass.

**Dependabot config:** `.github/dependabot.yml` already configures `github-actions` ecosystem weekly Monday — it will maintain SHA bumps as PRs after the initial bootstrap pin.

**Gotcha:** The architect needs the actual SHAs at time of plan writing. These change with new releases. The researcher cannot resolve them without executing `gh api` calls — this is flagged as a **Decision Required** item below.

---

### Issue #25 — Mosquitto-smoke.conf WARNING header

**Target file:** `docker/mosquitto-smoke.conf`

**Current state (4 lines total):**

```
# Smoke-test Mosquitto config — used ONLY by .github/workflows/release.yml
# during the post-build /healthz smoke step. Do not deploy this in production.
listener 1883 0.0.0.0
allow_anonymous true
```

**Existing warning language:** A single inline comment on line 1 says "used ONLY by ... release.yml" and line 2 says "Do not deploy this in production." The language is understated and not prominently formatted.

**Required change:** Replace or augment lines 1–2 with a prominent multi-line `# WARNING` block at the top. Functional lines 3–4 (`listener` and `allow_anonymous`) are unchanged.

**No behavioral impact** — Mosquitto ignores comment lines.

---

### Issue #26 — Docker-compose example port-binding comment

**Target file:** `docker/docker-compose.example.yml`

**Current state of ports section (lines 21–22):**

```yaml
    ports:
      - "8080:8080"
    # healthcheck inherited from the image's HEALTHCHECK directive.
```

**No existing comment** about binding behavior or network security near the `ports:` key. The comment at line 22 is about healthcheck, not port binding.

**Required change:** Add a comment recommending `127.0.0.1:8080:8080` for untrusted networks, placed directly above or inline with the `"8080:8080"` line. The default value stays `"8080:8080"` — comment only.

**No existing security comment to avoid duplicating.** The file header (lines 1–5) discusses the broker, not port binding. Line 15 sets `ASPNETCORE_URLS: http://+:8080` (no comment about public binding). The architect has a clean slate for the new comment.

---

### Issue #27 — Windows-path rejection in `ValidateSerilogPath`

**Target file:** `src/FrigateRelay.Host/StartupValidation.cs`, method `ValidateSerilogPath` (lines 99–115).

**Current `ValidateSerilogPath` logic:**

- Line 107: rejects `..` traversal
- Line 109: rejects UNC `\\` prefix
- Lines 111–113: rejects absolute Linux paths (`/`) outside allowlist `["/var/log/frigaterelay/", "/app/logs/"]`

**Gap:** A Windows-style absolute path like `C:\Windows\System32\evil.log` does NOT start with `/` or `\\`, so it falls through all three checks and is silently accepted. The existing XML doc comment at lines 92–96 explicitly calls this out:

> "Windows-style absolute paths (e.g. `C:\Windows\...`) are not explicitly blocked here because the container target is Linux-only... A future hardening pass may add a `Path.IsPathRooted` check with OS guard for broader coverage."

**Required fix (D5 platform seam):** Add an optional `Func<bool>? isWindows = null` parameter to `ValidateSerilogPath`. Default resolves to `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)`. New guard:

```csharp
var onWindows = (isWindows ?? (() => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)))();
if (onWindows && Path.IsPathRooted(path) && !path.StartsWith('/') && !path.StartsWith(@"\\", StringComparison.Ordinal))
    errors.Add($"Serilog:WriteTo path '{path}' is a Windows-style absolute path and is not permitted.");
```

The `!path.StartsWith('/')` excludes Linux absolute paths (already handled above). The `!path.StartsWith(@"\\")` excludes UNC (already handled above). Only `C:\...`-style paths hit this branch.

**Signature change:** `ValidateSerilogPath` is `internal static` — only called from `ValidateAll` (line 40) and from tests. Adding an optional parameter is non-breaking at call sites. `ValidateAll` at line 40 calls `ValidateSerilogPath(configuration, errors)` with no third arg — this continues to compile since the new parameter defaults to `null`.

**Existing test file:** `tests/FrigateRelay.Host.Tests/Configuration/SerilogPathValidationTests.cs`

**Current test count: 9 tests** (8 `[TestMethod]` tests in the `SerilogPathValidationTests` class + 1 `ValidateAll_WithBadSerilogPath_ThrowsAggregatedError` integration test in the same file).

**New tests to add (per ROADMAP):** 2 Windows-path rejection tests using `() => true` as `isWindows` seam — both run cross-platform on Linux CI.

**Test structure pattern to follow:** Existing tests use `ConfigWithSinkPath(path)` helper (private static, lines 24–29) and call `StartupValidation.ValidateSerilogPath(config, errors)` directly. New tests will call `StartupValidation.ValidateSerilogPath(config, errors, isWindows: () => true)` or `() => false`.

**Gotcha — update existing XML doc comment.** Lines 92–96 in `ValidateSerilogPath`'s XML doc currently say the Windows check is absent and deferred to a future pass. After #27 ships, this comment must be updated to remove the "future hardening" language and reflect the new `Func<bool>` parameter.

**Gotcha — `path` in error message is operator-controlled.** After #13 ships, this `errors.Add` call must also wrap `path` in `Sanitize(...)`. Coordinate with #13.

---

## 2. Convention Confirmations

The following CLAUDE.md conventions apply to all Phase 15 work without exception:

- **MTP test invocation:** `dotnet run --project tests/<project> -c Release` (NOT `dotnet test`). No change from previous phases.
- **Test naming:** `Method_Condition_Expected` underscores. `CA1707` silenced for `tests/**/*.cs` via `.editorconfig` — applies to all 13 new tests.
- **`CapturingLogger<T>`:** Available from `tests/FrigateRelay.TestHelpers/` via `global using FrigateRelay.TestHelpers;` in each test project's `Usings.cs`. Phase 15 tests do not require logger capture — all new tests assert on the `errors` accumulator directly. `CapturingLogger<T>` is not needed for the 13 new tests.
- **`<InternalsVisibleTo Include="DynamicProxyGenAssembly2" />`:** Already present in `FrigateRelay.Host.csproj` (required for NSubstitute on internal types from previous phases). No change needed — `StartupValidation` (internal static) is accessible to tests via the existing `InternalsVisibleTo FrigateRelay.Host.Tests` entry. No new NSubstitute mocking of internal types is introduced in Phase 15.
- **Warnings-as-errors:** `Directory.Build.props` enforces this on Linux + Windows. New `Regex` member (if `[GeneratedRegex]` is used) must be on a `partial class` — but `StartupValidation` is currently `internal static class`, not `partial`. Use `private static readonly Regex` with `RegexOptions.Compiled` instead of `[GeneratedRegex]` to avoid making the class `partial`.
- **D7 collect-all pattern:** All new passes (`ValidateNames`) append to the shared `List<string> errors` parameter and never throw inline. `ValidateAll` remains the single entry point that allocates the accumulator and throws once.
- **FluentAssertions pinned to 6.12.2:** All new test assertions must use the existing pinned version. No `.Should().Be()` APIs removed in 6.12.2. No upgrade.

---

## 3. Test-Count Baseline

**Static count of `[TestMethod]` attributes across all test files:** 293

This is the **actual static count** from reading the codebase now. The CONTEXT-15.md states 291 (post-Phase 14 baseline). The discrepancy of 2 is likely from additional tests added between the CONTEXT-15 note and this research. The architect should confirm the true running count by executing:

```
dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build 2>&1 | tail -5
dotnet run --project tests/FrigateRelay.Abstractions.Tests -c Release --no-build 2>&1 | tail -5
```

For planning purposes: **Phase 15 success criterion = static baseline + 13 new tests.** If baseline is 293, the post-Phase-15 gate is 306.

**Per-file breakdown (selected Phase 15-relevant files):**

| File | Current count |
|------|---------------|
| `ActionEntryTypeConverterTests.cs` | 6 |
| `SerilogPathValidationTests.cs` | 9 |
| `ValidateObservabilityTests.cs` | 3 |
| `StartupValidationValidatorsTests.cs` | 3 |

---

## 4. Risk Callouts

**R1 — `ProfileResolver.cs` is out of `ValidateAll`'s direct reach for `Sanitize`.**
`errors.Add(...)` in `ProfileResolver.cs` interpolates `sub.Name` and `profileName` at lines 41, 49, 61–64. `ProfileResolver` is a separate `internal static class` in the same assembly. If the architect uses `private static` (D4 literal), the `ProfileResolver` error messages are not sanitized. The fix is to make `Sanitize` `internal static` on `StartupValidation` — this is consistent with D4's intent (one definition, one file) while enabling the one other consumer in the same assembly. **This is not captured in CONTEXT-15.md** and will surprise the architect if not addressed in the plan.

**R2 — `ValidateNames` must enumerate validator keys without a dedicated options type.**
The `HostSubscriptionsOptions` record does not carry a `Validators` dictionary — that section is consumed by plugin registrars at registration time. Validator instance keys only appear as string values inside `ActionEntry.Validators`. The `ValidateNames` pass can only reach them by iterating `options.Subscriptions[*].Actions[*].Validators[*]` and `options.Profiles.Values[*].Actions[*].Validators[*]`. This is correct but means the "validator name" being validated is the config key string, not any `IValidationPlugin.Name` property. The pass validates what operators type, which is what matters for CWE-117.

**R3 — `ValidateSerilogPath` XML doc comment references the future hardening pass (#27) as not-yet-done.**
Lines 92–96 currently say the Windows check is deferred. After #27 ships, the comment is wrong. The plan must include updating this comment. Risk if missed: misleads future contributors.

**R4 — `ValidateObservability` uses `out _` discard.**
Line 76 currently: `!Uri.TryCreate(endpoint, UriKind.Absolute, out _)`. The #20 fix requires capturing the `Uri` to check `uri.Scheme`. The `out _` must change to `out var uri`. This is a mechanical change but the single-line conditional must be refactored to a block — the architect should note the style change (warnings-as-errors may flag unused `uri` if the refactor isn't done carefully, but the scheme check uses `uri` so it won't be unused).

**R5 — 293 vs 291 test-count discrepancy.**
Static grep shows 293 `[TestMethod]` attributes; CONTEXT-15.md states 291. Possible causes: 2 tests added to integration test suite that weren't counted in the Phase 14 baseline capture. The architect must run the suite once before writing test-count gates in PLAN-15 to confirm the true runner-reported count (some `[TestMethod]` methods may be in abstract base classes that don't run independently).

**R6 — `secret-scan.yml` has `actions/checkout@v6` but ROADMAP only mentions `release.yml` + `ci.yml`.**
The greppable invariant `git grep -nE 'uses:\s*[^@\s]+@v[0-9]' .github/workflows/` covers all workflow files. `secret-scan.yml` has 2 `actions/checkout@v6` references (lines 16 and 28) that will cause the invariant check to fail unless they are also pinned. The architect's plan must include `secret-scan.yml` in the SHA-pinning scope even though the ROADMAP text says "2 workflow files."

---

## 5. Uncertainty Flags

**Decision Required — #24 SHA values:** The architect's plan requires the full commit SHAs for the 7 action versions listed in the surface inventory. These must be resolved at plan-write time using `gh api /repos/<action>/git/ref/tags/<version>` or equivalent. The researcher cannot resolve them without executing shell commands. Recommended approach: architect resolves SHAs during PLAN-15.2 dispatch using:

```bash
gh api /repos/actions/checkout/git/ref/tags/v6
gh api /repos/docker/setup-qemu-action/git/ref/tags/v4
gh api /repos/docker/setup-buildx-action/git/ref/tags/v4
gh api /repos/docker/login-action/git/ref/tags/v4
gh api /repos/docker/metadata-action/git/ref/tags/v6
gh api /repos/docker/build-push-action/git/ref/tags/v7
gh api /repos/actions/setup-dotnet/git/ref/tags/v5
```

**Decision Required — #19 `ValidateNames` pass ordering:** The researcher recommends running `ValidateNames` before `ProfileResolver.Resolve` to catch both subscription names and profile keys. However, profile actions (in `options.Profiles`) are pre-resolution, so their validator keys are reachable directly. The architect should confirm this ordering in the plan and ensure the pass description in the code comment matches the D7 sequence numbering.

**Inconclusive — `grpc` scheme support in `Uri.TryCreate`:** The OTLP collector gRPC endpoint is typically expressed as `grpc://host:4317`. .NET's `Uri` class parses unknown schemes without error, returning them as `uri.Scheme`. This should work correctly. The architect may wish to add a quick inline test confirming `new Uri("grpc://host:4317").Scheme == "grpc"` before writing the test case, to avoid a false assumption.

---

## Sources

1. `/mnt/f/git/frigaterelay/.shipyard/ROADMAP.md` lines 468–522 — Phase 15 deliverables and success criteria
2. `/mnt/f/git/frigaterelay/.shipyard/phases/15/CONTEXT-15.md` — design decisions D1–D7
3. `/mnt/f/git/frigaterelay/src/FrigateRelay.Host/StartupValidation.cs` — full read
4. `/mnt/f/git/frigaterelay/src/FrigateRelay.Host/Configuration/ActionEntryTypeConverter.cs` — full read
5. `/mnt/f/git/frigaterelay/src/FrigateRelay.Host/Configuration/ActionEntryJsonConverter.cs` — full read
6. `/mnt/f/git/frigaterelay/src/FrigateRelay.Host/Configuration/ActionEntry.cs` — full read
7. `/mnt/f/git/frigaterelay/src/FrigateRelay.Host/Configuration/HostSubscriptionsOptions.cs` — full read
8. `/mnt/f/git/frigaterelay/src/FrigateRelay.Host/Configuration/SubscriptionOptions.cs` — full read
9. `/mnt/f/git/frigaterelay/src/FrigateRelay.Host/Configuration/ProfileOptions.cs` — full read
10. `/mnt/f/git/frigaterelay/src/FrigateRelay.Host/Configuration/ProfileResolver.cs` — full read
11. `/mnt/f/git/frigaterelay/src/FrigateRelay.Host/HostBootstrap.cs` lines 120–155
12. `/mnt/f/git/frigaterelay/src/FrigateRelay.Plugins.CodeProjectAi/PluginRegistrar.cs` — full read (confirms validator key shape)
13. `/mnt/f/git/frigaterelay/.github/scripts/run-tests.sh` — full read
14. `/mnt/f/git/frigaterelay/.github/scripts/secret-scan.sh` — full read
15. `/mnt/f/git/frigaterelay/.github/secret-scan-fixture.txt` — full read
16. `/mnt/f/git/frigaterelay/.github/workflows/release.yml` — full read
17. `/mnt/f/git/frigaterelay/.github/workflows/ci.yml` — full read
18. `/mnt/f/git/frigaterelay/.github/workflows/secret-scan.yml` — full read
19. `/mnt/f/git/frigaterelay/docker/mosquitto-smoke.conf` — full read
20. `/mnt/f/git/frigaterelay/docker/docker-compose.example.yml` — full read
21. `/mnt/f/git/frigaterelay/config/appsettings.Example.json` — full read
22. `/mnt/f/git/frigaterelay/docker/appsettings.Smoke.json` — full read
23. `/mnt/f/git/frigaterelay/tests/FrigateRelay.Host.Tests/Configuration/ActionEntryTypeConverterTests.cs` — full read (6 tests)
24. `/mnt/f/git/frigaterelay/tests/FrigateRelay.Host.Tests/Configuration/SerilogPathValidationTests.cs` — full read (9 tests)
25. `/mnt/f/git/frigaterelay/tests/FrigateRelay.Host.Tests/Observability/ValidateObservabilityTests.cs` — full read (3 tests)
26. `/mnt/f/git/frigaterelay/tests/FrigateRelay.Host.Tests/Configuration/StartupValidationValidatorsTests.cs` — full read (3 tests)
27. Grep of `[TestMethod]` across all test files — 293 total across 55 files
