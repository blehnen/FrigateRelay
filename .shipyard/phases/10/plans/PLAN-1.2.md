---
phase: 10-docker-release
plan: 1.2
wave: 1
dependencies: []
must_haves:
  - New ValidateSerilogPath pass added to StartupValidation.ValidateAll Pass 0 cluster
  - Rejects .. path traversal, UNC \\ paths, and absolute paths outside allowlist (/var/log/frigaterelay/, /app/logs/)
  - Closes ID-21 in .shipyard/ISSUES.md (status -> Closed with Phase 10 reference)
  - Tests cover all 6 cases enumerated in RESEARCH.md B4
files_touched:
  - src/FrigateRelay.Host/StartupValidation.cs
  - tests/FrigateRelay.Host.Tests/Configuration/SerilogPathValidationTests.cs
  - .shipyard/ISSUES.md
tdd: true
risk: low
---

# Plan 1.2: Serilog file-sink path validation (closes ID-21)

## Context

Implements CONTEXT-10 B1 — operator-controlled `Serilog:WriteTo:*:Args:path` values must be rejected at startup if they contain `..` (path traversal), begin with `\\` (UNC), or are absolute paths outside the allowlist (`/var/log/frigaterelay/`, `/app/logs/`). Pairs with the non-root container user (Phase 10 Dockerfile) to close ID-21's residual risk that an operator could redirect log output to overwrite system files.

`StartupValidation.cs` already lives at `src/FrigateRelay.Host/StartupValidation.cs` (namespace `FrigateRelay.Host`), and `ValidateAll` already retrieves `IConfiguration` via `services.GetService<IConfiguration>()` for the existing `ValidateObservability` pass — so the new `ValidateSerilogPath(IConfiguration, ICollection<string>)` pass slots in alongside `ValidateObservability` at Pass 0 with NO signature change to `ValidateAll`. Follows the Phase 8 D7 collect-all pattern: appends to `errors`, never throws.

## Dependencies

None (Wave 1). File-disjoint from PLAN-1.1 and PLAN-1.3.

## Tasks

### Task 1: Add ValidateSerilogPath pass + wire into ValidateAll

**Files:**
- `src/FrigateRelay.Host/StartupValidation.cs` (modify — add `internal static void ValidateSerilogPath(IConfiguration, ICollection<string>)`; insert one call in `ValidateAll` immediately after the existing `ValidateObservability(configuration, errors)` call inside the `if (configuration is not null)` block)

**Action:** modify

**Description:**
Add `ValidateSerilogPath`:

```csharp
internal static void ValidateSerilogPath(IConfiguration config, ICollection<string> errors)
{
    var allowlist = new[] { "/var/log/frigaterelay/", "/app/logs/" };
    foreach (var sink in config.GetSection("Serilog:WriteTo").GetChildren())
    {
        var path = sink["Args:path"];
        if (string.IsNullOrWhiteSpace(path)) continue;

        if (path.Contains(".."))
            errors.Add($"Serilog:WriteTo path '{path}' contains '..' path traversal segments and is rejected.");
        else if (path.StartsWith(@"\\", StringComparison.Ordinal))
            errors.Add($"Serilog:WriteTo path '{path}' is a UNC path and is not permitted.");
        else if (path.StartsWith('/') &&
                 !allowlist.Any(prefix => path.StartsWith(prefix, StringComparison.Ordinal)))
            errors.Add($"Serilog:WriteTo path '{path}' is an absolute path outside the allowed prefixes ({string.Join(", ", allowlist)}).");
    }
}
```

Wire in `ValidateAll` (Pass 0 cluster):
```csharp
if (configuration is not null)
{
    ValidateObservability(configuration, errors);
    ValidateSerilogPath(configuration, errors);   // <-- NEW
}
```

Note: do NOT log the raw path through any `ILogger` — the value is operator-controlled and could contain log-spoofing payloads (ID-13 collision avoidance). It only enters the aggregated `InvalidOperationException` message via `errors.Add(...)`, which is the existing pattern.

**Acceptance Criteria:**
- `grep -n 'ValidateSerilogPath' src/FrigateRelay.Host/StartupValidation.cs` returns at least 2 matches (definition + ValidateAll call site).
- `dotnet build FrigateRelay.sln -c Release` exits 0 with zero warnings.

### Task 2: Unit tests covering the 6 scenarios

**Files:**
- `tests/FrigateRelay.Host.Tests/Configuration/SerilogPathValidationTests.cs` (create)

**Action:** test

**Description:**
Mirror the existing `ValidateObservabilityTests.cs` shape: build `IConfiguration` via `new ConfigurationBuilder().AddInMemoryCollection(...).Build()`, invoke `StartupValidation.ValidateSerilogPath(config, errors)` directly with a fresh `List<string> errors`, and assert on `errors`.

Test cases (one `[TestMethod]` per case, MSTest v3, `Method_Condition_Expected` naming):
1. `ValidateSerilogPath_DotDotPath_AddsTraversalError` — path `"../../etc/passwd"` -> errors contains substring `"path traversal"`.
2. `ValidateSerilogPath_UncPath_AddsUncError` — path `@"\\server\share\log.txt"` -> errors contains `"UNC"`.
3. `ValidateSerilogPath_AbsolutePathOutsideAllowlist_AddsError` — path `"/etc/passwd"` -> errors contains `"absolute path outside the allowed prefixes"`.
4. `ValidateSerilogPath_AllowlistedAbsolutePath_NoError` — path `"/var/log/frigaterelay/app.log"` -> errors is empty.
5. `ValidateSerilogPath_RelativeSafePath_NoError` — path `"logs/frigaterelay-.log"` -> errors is empty.
6. `ValidateSerilogPath_NoWriteToSection_NoError` — empty config -> errors is empty.

Plus one integration assertion: `ValidateAll_WithBadSerilogPath_ThrowsAggregatedError` — full `ValidateAll` call against a minimal `ServiceProvider` + bad path; assert single `InvalidOperationException` whose message contains both `"Startup configuration invalid"` AND the path-traversal error string. Reuse the existing minimal-SP fixture pattern from `ValidateObservabilityTests.cs`.

**Acceptance Criteria:**
- `test -f tests/FrigateRelay.Host.Tests/Configuration/SerilogPathValidationTests.cs`
- `grep -c '\[TestMethod\]' tests/FrigateRelay.Host.Tests/Configuration/SerilogPathValidationTests.cs` returns at least `7`.
- `dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build -- --filter-query "/*/*/SerilogPathValidationTests/*"` exits 0 with all tests passing.

### Task 3: Close ID-21 in ISSUES.md

**Files:**
- `.shipyard/ISSUES.md` (modify)

**Action:** modify

**Description:**
Flip ID-21's status from Open/Deferred to `Closed`. Add a closing entry referencing Phase 10 + this plan (PLAN-1.2) + the resolution path (validator pass + non-root container user from PLAN-2.1). Format MUST match the existing close-record style used for ID-11, ID-16, ID-17 (consult those entries for tone/structure). Do NOT touch any other issue's status.

**Acceptance Criteria:**
- `awk '/^### ID-21/,/^### ID-22/' .shipyard/ISSUES.md | grep -i 'closed'` returns at least one line.
- `awk '/^### ID-21/,/^### ID-22/' .shipyard/ISSUES.md | grep -i 'PLAN-1.2\|Phase 10'` returns at least one line.
- `git grep -n 'ID-21' .shipyard/ISSUES.md | grep -i 'open\|deferred' | grep -v '^.*Closed'` returns zero matches.

## Verification

Run from repo root:

```bash
dotnet build FrigateRelay.sln -c Release
dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build -- --filter-query "/*/*/SerilogPathValidationTests/*"

# Pass is wired into ValidateAll
grep -q 'ValidateSerilogPath' src/FrigateRelay.Host/StartupValidation.cs

# ID-21 closed
awk '/^### ID-21/,/^### ID-22/' .shipyard/ISSUES.md | grep -qi 'closed'
```
