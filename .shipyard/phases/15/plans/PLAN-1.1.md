---
phase: 15-v1.2.1-hardening
plan: 1.1
wave: 1
dependencies: []
must_haves:
  - newline sanitization helper (internal static) reachable from both StartupValidation.cs and ProfileResolver.cs
  - permissive-printable name allowlist enforced for subscription / profile / plugin / validator names
  - OTLP endpoint scheme restricted to http / https / grpc
  - Windows-style absolute Serilog path rejection with injectable Func<bool> isWindows seam
files_touched:
  - src/FrigateRelay.Host/StartupValidation.cs
  - src/FrigateRelay.Host/Configuration/ProfileResolver.cs
  - tests/FrigateRelay.Host.Tests/Configuration/StartupValidationNameSanitizationTests.cs
  - tests/FrigateRelay.Host.Tests/Configuration/StartupValidationNameAllowlistTests.cs
  - tests/FrigateRelay.Host.Tests/Observability/ValidateObservabilityTests.cs
  - tests/FrigateRelay.Host.Tests/Configuration/SerilogPathValidationTests.cs
  - CHANGELOG.md
tdd: true
risk: low
---

# Plan 1.1: StartupValidation hardening (#13, #19, #20, #27)

## Context

Bundle the four StartupValidation-touching issues into a single owner of `StartupValidation.cs` so Wave 1 plans avoid file overlap. Issue #13 closes CWE-117 by sanitizing operator-controlled values flowing into `errors.Add` (RESEARCH.md §1 lists 12 call sites across `StartupValidation.cs` lines 77–219 and `ProfileResolver.cs` lines 41–64). Issue #19 adds a `ValidateNames` pass enforcing the D1 permissive-printable regex `^[A-Za-z0-9_. -]+$` across all four name kinds (D2). Issue #20 closes CWE-183 by extending `ValidateObservability` with a scheme allowlist `{http, https, grpc}`. Issue #27 closes the residual CWE-22 gap by rejecting Windows-style absolute Serilog paths via the D5 `Func<bool>? isWindows = null` seam.

## Dependencies

None — Wave 1 root.

## Tasks

### Task 1: Sanitize helper + apply at every operator-controlled `errors.Add` site (#13)
**Files:** `src/FrigateRelay.Host/StartupValidation.cs`, `src/FrigateRelay.Host/Configuration/ProfileResolver.cs`, `tests/FrigateRelay.Host.Tests/Configuration/StartupValidationNameSanitizationTests.cs`
**Action:** modify + create
**Description:**
Add `internal static string Sanitize(string? value)` to `StartupValidation.cs` (D4 updated — `internal` not `private`, so `ProfileResolver.cs` in the same assembly can call it; this resolves R1 in RESEARCH.md). Behavior: returns `string.Empty` for `null`; for non-null, replace `\r` and `\n` with the literal escape sequences `\\r` and `\\n` so the resulting string is a single safe log line. Do not strip — escape — so the operator can still see what they typed.

Wrap every operator-controlled interpolation in both files with `Sanitize(...)`. Per RESEARCH.md §1 the sites are:
- `StartupValidation.cs` lines 77, 81, 108, 110, 113 (path), 137–142 (`sub.Name`, `entry.Plugin`, registeredNames join), 163–167 (`globalDefaultProviderName`, registeredNames), 173–177 (`sub.Name`, `sub.DefaultSnapshotProvider`, registeredNames), 183–187 (`sub.Name`, `entry.Plugin`, `entry.SnapshotProvider`, registeredNames), 216–219 (validator `key`).
- `ProfileResolver.cs` lines 41, 49, 61–64 (`sub.Name`, `profileName`, joined `defined` profile-key list).

The `registeredNames` join (lines 142, 167, 177, 187) — apply Sanitize per-element via `string.Join(", ", registeredNames.Select(Sanitize))` because plugin authors are eventually operators (RESEARCH.md §1 note). Hardcoded prose / allowlist literals are left untouched.

Write 2 failing MSTest tests first in a new file `StartupValidationNameSanitizationTests.cs`:
1. A subscription whose `Name` contains `\n` produces an aggregated error message that does NOT contain a literal `\n` byte (assert `result.Message.Contains('\n') == false` outside the standard MSTest formatting; use `\\n` literal-escape match).
2. A `Validators` instance key containing `\r` is escaped in the diagnostic.

Tests construct `HostSubscriptionsOptions` directly and call `StartupValidation.ValidateAll(serviceProvider, options)`, asserting on the thrown `InvalidOperationException.Message`.

**TDD:** true
**Acceptance Criteria:**
- `internal static string Sanitize(string?)` defined exactly once in `StartupValidation.cs`.
- Every interpolation of `sub.Name`, `profileName`, `entry.Plugin`, `entry.SnapshotProvider`, validator `key`, OTLP `endpoint`, Seq `seq`, Serilog `path`, `registeredNames`, and the profile-keys join is wrapped in `Sanitize(...)` in both files.
- 2 new tests pass.
- `git grep -nE 'errors\.Add\(\$"[^"]*\{(sub\.Name|profileName|entry\.Plugin|entry\.SnapshotProvider|key|endpoint|seq|path|globalDefaultProviderName)' src/FrigateRelay.Host/` returns ZERO unsanitized matches (every match must show `Sanitize(...)` wrapping).

### Task 2: ValidateNames pass + permissive-printable regex (#19) and ValidateObservability scheme allowlist (#20)
**Files:** `src/FrigateRelay.Host/StartupValidation.cs`, `tests/FrigateRelay.Host.Tests/Configuration/StartupValidationNameAllowlistTests.cs`, `tests/FrigateRelay.Host.Tests/Observability/ValidateObservabilityTests.cs`
**Action:** modify + create
**Description:**
**Part A — ValidateNames (#19):** Add a `private static readonly Regex NameAllowlist = new("^[A-Za-z0-9_. -]+$", RegexOptions.Compiled)` member to `StartupValidation.cs` (NOT `[GeneratedRegex]` — that requires `partial class`, which `internal static class StartupValidation` is not; see RESEARCH.md §2). Add a new pass `internal static void ValidateNames(HostSubscriptionsOptions options, List<string> errors)` that iterates and tests four kinds:
- Subscription names: `options.Subscriptions[i].Name`
- Profile keys: `options.Profiles.Keys`
- Plugin names: `options.Subscriptions[i].Actions[j].Plugin` AND `options.Profiles.Values[k].Actions[j].Plugin`
- Validator instance keys: `options.Subscriptions[i].Actions[j].Validators[k]` AND `options.Profiles.Values[m].Actions[j].Validators[k]`

For each value where `NameAllowlist.IsMatch(value) == false`, append a structured error of the shape `errors.Add($"<kind> name '{Sanitize(value)}' is invalid; only [A-Za-z0-9_. -] are permitted (CRLF, control chars, slashes, colons, and at-signs are rejected).")`. Wire the call into `ValidateAll` BEFORE `ProfileResolver.Resolve` (per RESEARCH.md §1 ordering recommendation; pre-resolution because profile keys must be checked).

Write 4 failing tests first in a new file `StartupValidationNameAllowlistTests.cs`:
1. Subscription name with `\n` — rejected with `subscription` in message.
2. Profile key with `/` — rejected with `profile` in message.
3. Plugin name with `:` — rejected with `plugin` in message.
4. Spaced subscription name `"DriveWay Person"` — accepted (zero errors emitted by `ValidateNames`); confirms D1 preserves the existing `appsettings.Example.json` shape.

**Part B — Scheme allowlist (#20):** In `ValidateObservability` (lines 75–77), refactor the single-line `Uri.TryCreate(endpoint, UriKind.Absolute, out _)` into a block: change `out _` to `out var uri`; on success, add `else if (uri!.Scheme is not ("http" or "https" or "grpc"))` that appends `errors.Add($"Otel:OtlpEndpoint '{Sanitize(endpoint)}' has unsupported scheme '{Sanitize(uri.Scheme)}'; allowed: http, https, grpc.")`. The existing `Serilog:Seq:ServerUrl` check at lines 79–81 is NOT touched (RESEARCH.md §1 — Seq scope is HTTP-only, no scheme allowlist needed).

Append 3 failing tests first to `ValidateObservabilityTests.cs`:
1. `Otel:OtlpEndpoint = "file:///tmp/x"` — rejected; error contains `unsupported scheme` and `'file'`.
2. `Otel:OtlpEndpoint = "grpc://otel-collector:4317"` — accepted (zero errors).
3. `Otel:OtlpEndpoint = "https://otel-collector:4318"` — accepted (zero errors).

**TDD:** true
**Acceptance Criteria:**
- `ValidateNames` is called from `ValidateAll` before `ProfileResolver.Resolve`.
- All 9 spaced subscription names from `config/appsettings.Example.json` pass `ValidateNames` (verified by Task 4 build, no separate test required — RESEARCH.md §1 already confirmed by static analysis).
- 4 new name-allowlist tests + 3 new scheme-allowlist tests pass.
- `Otel:OtlpEndpoint = "file:///tmp/x"` produces a `ValidateAll` aggregate exception whose message names the offending value.

### Task 3: Windows-path rejection in ValidateSerilogPath (#27) + CHANGELOG entries
**Files:** `src/FrigateRelay.Host/StartupValidation.cs`, `tests/FrigateRelay.Host.Tests/Configuration/SerilogPathValidationTests.cs`, `CHANGELOG.md`
**Action:** modify
**Description:**
Add an optional parameter `Func<bool>? isWindows = null` to `ValidateSerilogPath` (D5). Default resolves via `(isWindows ?? (() => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)))()`. After the existing UNC + Linux-allowlist checks, add a final guard: if `onWindows && Path.IsPathRooted(path) && !path.StartsWith('/') && !path.StartsWith(@"\\", StringComparison.Ordinal)`, append `errors.Add($"Serilog:WriteTo path '{Sanitize(path)}' is a Windows-style absolute path and is not permitted.")`.

Update the XML doc comment at lines 92–96 of `ValidateSerilogPath`: remove the "future hardening pass may add..." sentence and replace with one sentence documenting the new `isWindows` parameter and that Windows-rooted paths are rejected when the predicate returns true (RESEARCH.md R3).

`ValidateAll` at line 40 currently calls `ValidateSerilogPath(configuration, errors)` — keep as-is (the new parameter defaults to `null`, which resolves to runtime detection in production).

Write 2 failing tests first appended to `SerilogPathValidationTests.cs`:
1. `path = @"C:\Windows\System32\evil.log"` with `isWindows: () => true` — rejected; error contains `Windows-style absolute path`.
2. Same path with `isWindows: () => false` — accepted (zero errors from the Windows guard; the path may still trigger the existing Linux-allowlist guard, so use a path that wouldn't trigger that — e.g. `@"D:\logs\app.log"` with `isWindows: () => false` produces zero errors).

**CHANGELOG.md:** Append four `[Unreleased]` entries (D6) — one line per closed issue:
- `### Security` section: lines for #13 (CWE-117), #19 (name allowlist), #20 (CWE-183), #27 (CWE-22 residual).

**TDD:** true
**Acceptance Criteria:**
- 2 new Windows-path tests pass on Linux CI (no Windows agent required).
- XML doc comment on `ValidateSerilogPath` no longer mentions "future hardening".
- `CHANGELOG.md` `[Unreleased]` section gains 4 lines naming issues #13, #19, #20, #27.
- All existing `SerilogPathValidationTests` (9 tests) continue to pass.

## Verification

```bash
# Build clean
dotnet build FrigateRelay.sln -c Release

# Run all Host tests (covers all 4 issues + existing baseline)
dotnet run --project tests/FrigateRelay.Host.Tests -c Release

# Targeted runs for the new test classes
dotnet run --project tests/FrigateRelay.Host.Tests -c Release -- --filter "StartupValidationNameSanitizationTests"
dotnet run --project tests/FrigateRelay.Host.Tests -c Release -- --filter "StartupValidationNameAllowlistTests"
dotnet run --project tests/FrigateRelay.Host.Tests -c Release -- --filter "ValidateObservabilityTests"
dotnet run --project tests/FrigateRelay.Host.Tests -c Release -- --filter "SerilogPathValidationTests"

# Architectural invariants unchanged
git grep ServicePointManager src/                                    # empty
git grep -nE '\.(Result|Wait)\(' src/                                # empty
git grep -nE 'App\.Metrics|OpenTracing|Jaeger\.' src/                # empty

# #13 invariant — every operator-controlled interpolation is sanitized
git grep -nE 'errors\.Add\(\$"[^"]*\{(sub\.Name|profileName|entry\.Plugin|entry\.SnapshotProvider|endpoint|seq|path|globalDefaultProviderName)' src/FrigateRelay.Host/StartupValidation.cs src/FrigateRelay.Host/Configuration/ProfileResolver.cs | grep -v 'Sanitize('
# expected: empty (every match must already wrap in Sanitize)
```
