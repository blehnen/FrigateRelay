# Build Summary: Plan 1.1 — StartupValidation hardening (#13, #19, #20, #27)

## Status: complete

## Tasks Completed

- **Task 1 — `Sanitize` helper + apply at every operator-controlled `errors.Add` site (#13)** — complete. Commit `ac8e9a4`. Added `internal static string Sanitize(string?)` to `StartupValidation.cs` and applied at all 12 sites listed in RESEARCH.md §1 across `StartupValidation.cs` and `Configuration/ProfileResolver.cs`. 2 net-new tests (`StartupValidationNameSanitizationTests.cs`) cover newline-bearing subscription name and CR-bearing validator key. Greppable invariant `git grep -nE 'errors\.Add\(\$"[^"]*\{(operator-controlled-name)' src/FrigateRelay.Host/{StartupValidation.cs,Configuration/ProfileResolver.cs} | grep -v 'Sanitize('` returns empty.
- **Task 2 — `ValidateNames` pass + permissive-printable regex (#19) and `ValidateObservability` scheme allowlist (#20)** — complete. Commit `f343860`. Added `private static readonly Regex NameAllowlist = new("^[A-Za-z0-9_. -]+$", RegexOptions.Compiled);` and `internal static void ValidateNames(HostSubscriptionsOptions, List<string>)` covering all four name kinds (subscription, profile, plugin, validator) per CONTEXT-15 D2. Wired into `ValidateAll` between Pass 0 (observability) and Pass 1 (`ProfileResolver.Resolve`) so profile keys are checked pre-resolution. Refactored `ValidateObservability` from `out _` to `out var uri` and added scheme allowlist `{http, https, grpc}` after the existing `Uri.TryCreate`. 4 + 3 = 7 net-new tests (`StartupValidationNameAllowlistTests.cs`, appended `ValidateObservabilityTests.cs`).
- **Task 3 — Windows-path rejection in `ValidateSerilogPath` (#27) + CHANGELOG entries** — complete. Commit `b0c261b`. Added `Func<bool>? isWindows = null` parameter to `ValidateSerilogPath` per CONTEXT-15 D5. Default resolves to `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)`. Cross-platform Windows-rooted-path detection via private helper `IsWindowsRootedPath` (drive-letter pattern `[A-Za-z]:[\\\/]`) — replaces `Path.IsPathRooted` which the plan specified but which fails silently on Linux for `C:\...` patterns. XML doc comment updated to remove "future hardening pass may add..." line and document the new parameter. 2 net-new tests in `SerilogPathValidationTests.cs`. CHANGELOG.md `[Unreleased]` `### Security` section extended with 4 lines (#13, #19, #20, #27) — combined with Tasks landed by PLAN-1.2/1.3/1.4, all 10 Phase 15 IDs are now in the v1.2.1 release notes.

## Files Modified

- `src/FrigateRelay.Host/StartupValidation.cs` — Sanitize helper (Task 1), 12 application sites wrapped with Sanitize (Task 1), NameAllowlist regex member + ValidateNames pass + wired into ValidateAll (Task 2 Part A), ValidateObservability scheme allowlist (Task 2 Part B), ValidateSerilogPath isWindows parameter + IsWindowsRootedPath helper + XML doc update (Task 3).
- `src/FrigateRelay.Host/Configuration/ProfileResolver.cs` — 5 operator-controlled `errors.Add` sites wrapped with Sanitize (Task 1).
- `tests/FrigateRelay.Host.Tests/Configuration/StartupValidationNameSanitizationTests.cs` — new file, 2 tests for #13.
- `tests/FrigateRelay.Host.Tests/Configuration/StartupValidationNameAllowlistTests.cs` — new file, 4 tests for #19.
- `tests/FrigateRelay.Host.Tests/Observability/ValidateObservabilityTests.cs` — appended 3 tests for #20 (file:// rejected; grpc:// accepted; https:// accepted).
- `tests/FrigateRelay.Host.Tests/Configuration/SerilogPathValidationTests.cs` — appended 2 tests for #27 (cross-platform Windows-path rejection via injected predicate).
- `CHANGELOG.md` — 4 new entries under `[Unreleased]` `### Security` for #13, #19, #20, #27.

## Decisions Made

- **`Path.IsPathRooted` replaced with `IsWindowsRootedPath` helper.** PLAN-1.1 Task 3 originally specified `Path.IsPathRooted(path)` for the Windows guard. On Linux runners, `Path.IsPathRooted("C:\Windows\...")` returns false (drive-letter notation isn't recognized) — the test assertion failed when run cross-platform. The portable fix is a tiny private helper checking the drive-letter pattern (`[A-Za-z]:[\\\/]`). Documented in the helper's comment with rationale.
- **Test assertions use `.ContainEquivalentOf` (case-insensitive) for kind-prefix substring matching.** The implementation produces sentence-cased operator messages like `"Subscription name 'X' is invalid; ..."`. Tests originally asserted lowercase substrings (`Should().Contain("subscription")`), which failed under FluentAssertions' case-sensitive `Contain`. Switched to `ContainEquivalentOf` rather than re-casing the operator-facing messages, preserving readable diagnostics.
- **`Sanitize` helper escapes (not strips) CRLF.** Per CONTEXT-15 D4 the helper replaces `\r` with `\\r` and `\n` with `\\n` so the operator can still see what they typed in the diagnostic; stripping would hide the operator error.
- **`ValidateNames` runs as Pass 0.5** (between Pass 0 observability and Pass 1 `ProfileResolver.Resolve`). Profile keys must be checked pre-resolution because resolution dereferences profile names; rejecting bad keys before lookup gives a clearer diagnostic.
- **`Serilog:Seq:ServerUrl` scheme not restricted** — Seq is HTTP-only by design (RESEARCH.md §1), so the existing absolute-URI check is sufficient. Only `Otel:OtlpEndpoint` carries a scheme allowlist.

## Issues Encountered

- **Test/implementation case mismatch (#19, Task 2 Part A).** First test run produced 3 failures because tests asserted lowercase substring (`"subscription"`) against sentence-cased messages (`"Subscription name 'X' is invalid"`). Resolved via `.ContainEquivalentOf(...)` switch. **Lesson:** when the architect specifies a `<kind>` placeholder in an error-message format string, the test assertion's case-sensitivity matters and should be explicit in the plan.
- **`Path.IsPathRooted` not cross-platform for Windows-style paths (#27, Task 3).** The plan's verbatim recipe included `Path.IsPathRooted(path)` which is OS-dependent — Linux returns false for `C:\...`. Replaced with a portable drive-letter pattern check. **Lesson:** cross-platform path detection in unit tests requires explicit pattern logic, not BCL helpers that key on the host OS.
- **Builder agent terminated mid-Task-2.** First `dotnet run` for the new name-allowlist tests showed 3/4 fails (the case-mismatch above), and the agent stopped without diagnosing. Orchestrator inspected the test output, applied the `ContainEquivalentOf` fix, ran tests green, and continued the plan manually. Tasks 2 (Part B) and 3 were completed without the agent. **Lesson:** for plans with a high TDD-cycle count, the builder needs explicit "if test fails X, do Y" diagnostic guidance — failure modes the architect can foresee should be in the plan.

## Verification Results

- `dotnet build FrigateRelay.sln -c Release` — 0 warnings, 0 errors. ✓ (warnings-as-errors invariant unchanged).
- `dotnet run --project tests/FrigateRelay.Host.Tests -c Release` — 146/146 pass (135 prior + 11 net-new). ✓
- Targeted runs:
  - `StartupValidationNameSanitizationTests` — 2/2 ✓
  - `StartupValidationNameAllowlistTests` — 4/4 ✓
  - `ValidateObservabilityTests` — 6/6 (3 prior + 3 new) ✓
  - `SerilogPathValidationTests` — 11/11 (9 prior + 2 new) ✓
- Architectural invariants:
  - `git grep ServicePointManager src/` — only doc-comment matches (3 plugin Options + 1 source comment); no actual `ServicePointManager` API usage. ✓
  - `git grep -nE '\.(Result|Wait)\(' src/` — empty. ✓
  - `git grep -nE 'App\.Metrics|OpenTracing|Jaeger\.' src/` — empty. ✓
  - `git grep -nE 'errors\.Add\(\$"[^"]*\{(sub\.Name|profileName|entry\.Plugin|entry\.SnapshotProvider|endpoint|seq|path|globalDefaultProviderName)' src/FrigateRelay.Host/{StartupValidation.cs,Configuration/ProfileResolver.cs} | grep -v 'Sanitize('` — empty. ✓ (every operator-controlled interpolation wraps in `Sanitize`).
- Commits: `ac8e9a4` (Task 1 #13), `f343860` (Task 2 #19+#20), `b0c261b` (Task 3 #27 + CHANGELOG).
