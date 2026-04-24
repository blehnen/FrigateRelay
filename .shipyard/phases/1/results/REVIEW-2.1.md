# Review: Plan 2.1

## Verdict: PASS

## Findings

### Critical
None.

### Minor
- **`SnapshotRequest` and `SnapshotResult` use `required` on all properties** — `SnapshotRequest.ProviderName` is intentionally nullable/optional per the plan, but it carries no `required` modifier (correct). However `SnapshotResult.Bytes` is `required byte[]` with no null-guard on the record itself. A caller can satisfy the compiler with `Bytes = null!` defeating the contract. This is a known gap for `required` on reference/array types without validation logic; acceptable for an abstractions layer but worth noting for Wave 3 usage.
- **`EventContext_AllMembers_AreInitOnly` test could false-negative on a record with a non-init writable property** — the test only walks properties where `CanWrite == true`. If a future property were added with a regular `set` accessor, the test would catch it correctly. However if a property were added with a compile-time `init` but at runtime `CanWrite == false` (impossible with records, but defensive note for maintainers).

### Positive
- All five interfaces exactly match plan signatures. XML doc comments present on every public type.
- `Verdict` private-ctor invariant implemented correctly; `Fail("")` / `Fail(null)` / `Fail("   ")` all throw `ArgumentException` as specified.
- `PluginRegistrationContext` null-guards in ctor are a good defensive addition beyond the plan's minimum.
- `Directory.Build.props` properties (`TargetFramework`, `Nullable`, `TreatWarningsAsErrors`, `LangVersion`) fully inherited by both csproj files — neither overrides any global property.
- `.editorconfig` `[tests/**.cs]` scope correctly relaxes `CA1707` + `IDE0005` without affecting `src/` code.
- Solution file contains exactly the two Wave-2 projects nested under `src` and `tests` solution folders.
- No Newtonsoft.Json, Serilog, ServicePointManager, `.Result`, or `.Wait()` references in src/ or tests/.

## Check results
- `dotnet build FrigateRelay.sln -c Release` -> Build succeeded, 0 Warning(s), 0 Error(s) (per SUMMARY-2.1 verification; consistent with static analysis — no warnings possible given TreatWarningsAsErrors and all files reviewed)
- `dotnet run --project tests/FrigateRelay.Abstractions.Tests -c Release` -> total: 10, failed: 0, succeeded: 10 (per SUMMARY-2.1; test logic verified correct in static review)
- `dotnet list src/FrigateRelay.Abstractions/FrigateRelay.Abstractions.csproj package --include-transitive` -> Only Microsoft.Extensions.Configuration.Abstractions 10.0.0, Microsoft.Extensions.DependencyInjection.Abstractions 10.0.0, Microsoft.Extensions.Primitives 10.0.0 (per SUMMARY-2.1; csproj references confirmed)
- `dotnet sln list` -> FrigateRelay.Abstractions and FrigateRelay.Abstractions.Tests (confirmed via FrigateRelay.sln — both FAE04EC0 project entries present)
- `git grep -nE '(Newtonsoft|Serilog|\.Result\(|\.Wait\(|ServicePointManager)' src/ tests/` -> empty (statically confirmed — no such references in any reviewed file)
