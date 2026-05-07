# Review: Plan 1.1

**Reviewer:** Senior code reviewer (Claude Sonnet 4.6)
**Date:** 2026-05-07
**Commits reviewed:** `ac8e9a4` (Task 1 #13), `f343860` (Task 2 #19+#20), `b0c261b` (Task 3 #27 + CHANGELOG)

## Verdict: PASS

---

## Stage 1 — Correctness

### Task 1: Sanitize helper + apply at every operator-controlled `errors.Add` site (#13)

**Status: PASS**

**Evidence:**
- `internal static string Sanitize(string? value)` defined exactly once at `StartupValidation.cs:24–28`.
- Escaping is correct and per-spec: `value.Replace("\r", @"\r").Replace("\n", @"\n")` — the `@"\r"` and `@"\n"` verbatim literals produce the two-character sequences `\r` and `\n` (backslash + letter), not the control characters. D4's "escape not strip" requirement is satisfied.
- All 12 operator-controlled `errors.Add` sites confirmed wrapped in `Sanitize(...)`: 9 sites in `StartupValidation.cs` and 3 sites in `Configuration/ProfileResolver.cs` (lines 42, 50, 62–63). The `registeredNames` join uses `.Select(Sanitize)` per-element.
- Greppable invariant passes: `git grep -nE 'errors\.Add\(\$"[^"]*\{(sub\.Name|profileName|entry\.Plugin|...)' src/FrigateRelay.Host/ | grep -v 'Sanitize('` returns empty.
- 2 new tests in `StartupValidationNameSanitizationTests.cs`: (1) subscription name with `\n` — asserts raw newline absent from stripped message and `\n` literal-escape present; (2) validator key with `\r` — same pattern for `\r`. Both test methods are sound and non-trivially correct.

**Notes:**
- D4 upgraded helper from `private` to `internal` to reach `ProfileResolver.cs` in the same assembly — consistent with CONTEXT-15 D4 update note and RESEARCH.md R1.
- Test 1 correctly strips the fixed header separator (`\n  - `) before asserting no raw `\n`, preventing a false positive from the collect-all join.

---

### Task 2 Part A: ValidateNames pass + permissive-printable regex (#19)

**Status: PASS**

**Evidence:**
- `private static readonly Regex NameAllowlist = new("^[A-Za-z0-9_. -]+$", RegexOptions.Compiled)` at `StartupValidation.cs:35` — correct regex, `RegexOptions.Compiled` (not `[GeneratedRegex]` which would require `partial class`; documented in XML comment).
- `internal static void ValidateNames(HostSubscriptionsOptions options, List<string> errors)` covers all four kinds as specified:
  - Subscription names: `options.Subscriptions` → `sub.Name` (line 49–53)
  - Profile keys: `options.Profiles.Keys` (line 56–60)
  - Plugin names in subscriptions AND in profiles: two separate foreach loops (lines 62–69, 72–79)
  - Validator instance keys in subscriptions AND in profiles: two separate foreach loops (lines 82–93, 96–108)
- Wired into `ValidateAll` at line 141 as "Pass 0.5" — after Pass 0 observability, before Pass 1 `ProfileResolver.Resolve`. Comment documents the ordering and rationale.
- All 9 spaced subscription names from `config/appsettings.Example.json` (e.g. `"DriveWay Person"`, `"Front Door Porch"`) contain only `[A-Za-z0-9 ]` and pass the regex; compat confirmed by static analysis.
- 4 new tests in `StartupValidationNameAllowlistTests.cs`: (1) `\n` in subscription name → rejected with "subscription" in message; (2) `/` in profile key → rejected with "profile" in message; (3) `:` in plugin name → rejected with "plugin" in message; (4) `"DriveWay Person"` → accepted with zero errors.
- Tests use `.ContainEquivalentOf(...)` (case-insensitive) to match the sentence-cased messages — this is a pragmatic deviation from the plan's original lowercase assertion, documented in SUMMARY-1.1 "Decisions Made". The production messages ("Subscription name '...'", "Profile name '...'") are readable operator diagnostics; `ContainEquivalentOf` correctly widens the assertion to be case-insensitive. No concern here.

**Notes:**
- Empty/null names are skipped via `!string.IsNullOrEmpty(...)` guard — deliberate, as empty names are caught by `ProfileResolver`'s mutex check (neither Profile nor Actions). Not a gap for the CWE-117 surface.
- Error message shape (`"{Kind} name '{Sanitize(value)}' is invalid; ..."`) includes the sanitized value for operator diagnosis.

---

### Task 2 Part B: ValidateObservability scheme allowlist (#20)

**Status: PASS**

**Evidence:**
- `ValidateObservability` at `StartupValidation.cs:180–184` refactored from `out _` discard to `out var uri`; scheme check added: `uri.Scheme is not ("http" or "https" or "grpc")` with structured diagnostic naming the offending scheme and the allowlist.
- Both interpolated values (`endpoint`, `uri.Scheme`) wrapped in `Sanitize(...)`.
- `Serilog:Seq:ServerUrl` path unchanged, consistent with RESEARCH.md §4 scope decision.
- 3 new tests appended to `ValidateObservabilityTests.cs`: `file:///tmp/x` rejected (contains "unsupported scheme" and `'file'`); `grpc://otel-collector:4317` accepted; `https://otel-collector:4318` accepted.
- All 3 prior `ValidateObservabilityTests` continue to pass (6/6 total confirmed in SUMMARY verification results).

---

### Task 3: Windows-path rejection in ValidateSerilogPath (#27) + CHANGELOG entries

**Status: PASS**

**Evidence:**
- `ValidateSerilogPath` signature at line 212: `Func<bool>? isWindows = null` parameter added. Default resolves to `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)` via lambda at line 215.
- `IsWindowsRootedPath` private helper at lines 236–237: `path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/')`. Correctly handles `C:\`, `c:/`, and lowercase drive letters (via `char.IsLetter`) without relying on `Path.IsPathRooted` which is OS-dependent.
- Windows guard applied at line 228: `else if (onWindows && IsWindowsRootedPath(path))` — correctly sequenced after UNC and Linux-allowlist checks (only Windows-style paths that haven't already been caught reach this branch).
- Path value wrapped in `Sanitize(path)` at line 229.
- XML doc comment at lines 197–211 updated: "future hardening pass may add..." language removed; new `isWindows` parameter documented including cross-platform seam rationale.
- `ValidateAll` at line 137 still calls `ValidateSerilogPath(configuration, errors)` with two args — third parameter defaults to `null` (runtime detection) as required.
- 2 new tests in `SerilogPathValidationTests.cs` (lines 207–234): `C:\Windows\System32\evil.log` with `isWindows: () => true` → rejected with "Windows-style absolute path"; `D:\logs\app.log` with `isWindows: () => false` → accepted (zero errors). Both tests are cross-platform by design.
- All 9 prior `SerilogPathValidationTests` continue to pass (11/11 confirmed in SUMMARY).
- CHANGELOG.md `[Unreleased]` `### Security` section contains 4 new lines for #13, #19, #20, #27, each naming the issue, the CWE (where applicable), and the mechanism.

---

## Stage 2 — Integration

### Critical

None.

---

### Minor

**1. `ValidateNames` silent pass on whitespace-only names (e.g. `"   "`)**
- File: `src/FrigateRelay.Host/StartupValidation.cs:51,58,67,77,90,104`
- All six `ValidateNames` guards use `!string.IsNullOrEmpty(...)` — this lets a whitespace-only string like `"   "` bypass the name check entirely. The regex `^[A-Za-z0-9_. -]+$` would actually match a space-only string (space is in the character class). The effect: a subscription named `"   "` passes `ValidateNames`, but `ProfileResolver` will not reject it either (it just becomes a sub with an empty-looking name). Downstream `ValidateActions` will catch that actions have no matching plugin if the name is also blank, but the name itself is accepted.
- This is consistent with how the existing codebase handles empty names (they fall through to other validation), but it means CWE-117 defense for whitespace-only names is incomplete at the `ValidateNames` layer.
- **Remediation:** Change guards to `!string.IsNullOrWhiteSpace(...)` to reject both empty and whitespace-only names consistently with the `ActionEntryTypeConverter` fix planned in ID-14.

**2. `ValidateAll` doc comment (`/// <summary>`) still describes only 4 passes, not 5**
- File: `src/FrigateRelay.Host/StartupValidation.cs:118–119`
- The XML doc on `ValidateAll` says "profile resolution → action-plugin existence → snapshot-provider existence → per-action validator existence" — 4 passes. Pass 0 (observability) is unnamed in the summary, and the new Pass 0.5 (`ValidateNames`) is absent from the summary sentence entirely. This is a documentation gap for contributors reading the XML doc.
- **Remediation:** Update the summary to: "Pass 0 — observability endpoint validation → Pass 0.5 — name allowlist → Pass 1 — profile resolution → Pass 2 — action-plugin existence → Pass 3 — snapshot-provider existence → Pass 4 — per-action validator existence."

**3. `IsWindowsRootedPath` does not handle the `C:foo` (relative-with-drive-letter) form**
- File: `src/FrigateRelay.Host/StartupValidation.cs:236–237`
- Windows also supports a "rooted-to-drive" relative form: `C:foo` (no backslash, relative to current directory on drive C). `path[2]` would be `f`, not `\` or `/`, so `IsWindowsRootedPath` returns `false` and the path silently passes. On Windows, this path resolves relative to the drive's current directory, not system root — so it is lower risk than `C:\System32\...`, but it is still a Windows-drive-prefixed form that the guard intends to block.
- **Remediation:** Change the helper to check `path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':'` (dropping the third-char constraint), which blocks all drive-letter-prefixed forms. Alternatively, add an `|| (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':')` branch to the existing helper. The plan's original `Path.IsPathRooted` would also have flagged this form on Windows; the custom helper, while solving the cross-platform problem, is slightly narrower.

**4. No test for `ValidateNames` with validator keys in profile actions**
- File: `tests/FrigateRelay.Host.Tests/Configuration/StartupValidationNameAllowlistTests.cs`
- Tests cover: subscription name (test 1), profile key (test 2), plugin name in subscription (test 3), and space-acceptance (test 4). There is no test covering the validator instance key path in *profile* actions (`options.Profiles.Values[*].Actions[*].Validators[*]`). The implementation loops this path (lines 96–108), but it is untested. The existing test 1 hits validator keys only indirectly through the `ValidateAll` path (Test 2 in `StartupValidationNameSanitizationTests`), not via `ValidateNames` directly.
- **Remediation:** Add a test constructing a `HostSubscriptionsOptions` with a profile whose action has a validator key containing an invalid character (e.g. `":"`) and asserting `ValidateNames` rejects it.

---

### Positive

- **Sanitize escaping is semantically correct.** The `@"\r"` / `@"\n"` verbatim literals produce exactly the two-character escape sequences operators expect in diagnostic output. Many implementations accidentally use `"\\r"` thinking they need double-escape; the C# verbatim string literal makes the intent unambiguous.
- **`IsWindowsRootedPath` solves the cross-platform BCL gap cleanly.** The decision to replace `Path.IsPathRooted` (which is OS-dependent) with a direct character-pattern check is the correct portable approach. The 3-char minimum length guard (`path.Length >= 3`) prevents index-out-of-bounds on short strings. The comment documents the BCL limitation explicitly for future contributors.
- **`ValidateAll` ordering is correctly documented inline.** Pass 0.5 comment ("before resolve so profile keys are checked") makes the ordering rationale visible at the call site.
- **`ContainEquivalentOf` over re-casing messages.** Preserving readable sentence-cased operator diagnostics while adapting the test assertion is the right tradeoff. Re-casing the messages to lowercase to satisfy test assertions would reduce diagnostic quality for operators.
- **CHANGELOG entries are precise and linked to CWE identifiers.** Each of the 4 new entries names the issue number, the CWE, and the mitigation mechanism — matches the convention established in prior releases.
- **`Func<bool>? isWindows = null` seam keeps tests deterministic.** Both the `() => true` and `() => false` paths are exercised without any OS-conditional test attribute, maintaining the CLAUDE.md invariant that all tests run on both Linux and Windows CI agents.
- **11 net-new tests, all named `Method_Condition_Expected`**, consistent with CLAUDE.md underscore convention. No new `dotnet test` invocations introduced; MTP `dotnet run` pattern maintained throughout.
- **No FluentAssertions upgrade** — package remains at 6.12.2 (Apache-2.0 pin). No Newtonsoft.Json introduced. No `ServicePointManager` usage. All architectural invariants from CLAUDE.md hold.
