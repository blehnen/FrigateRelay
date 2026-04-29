# Documentation Review — Phase 1

## Coverage assessment

**XML doc comments** — `GenerateDocumentationFile=true` is present in both `FrigateRelay.Abstractions.csproj` and `FrigateRelay.Host.csproj`. All 10 Abstractions source files carry `///` summaries. `dotnet build -c Release` produces zero warnings (CS1591 is a WAE in this repo), confirming no public member is undocumented. Coverage is complete.

**CLAUDE.md** — Captures architecture invariants, commands, and testing conventions. Does not yet reflect three Phase 1 operational discoveries.

**SUMMARY files** — All three are detailed and accurate. They are the authoritative record of builder decisions for future agents.

## Gaps

### HIGH — must address this phase

None. XML doc coverage is already complete and verified.

### MEDIUM — should address before Phase 11

**CLAUDE.md is missing three operational facts that Phase 2+ agents will need to rediscover otherwise:**

1. **`dotnet run` vs `dotnet test`** — SUMMARY-2.1 Decision 3 documents that `dotnet test` is blocked by .NET 10 for MTP projects. The Commands section of CLAUDE.md currently shows `dotnet test` for single-project invocation without this caveat. A Phase 2 CI agent will fail if it follows the Commands section literally.

2. **`[SetsRequiredMembers]` convention** — SUMMARY-2.1 Decision 1 and SUMMARY-3.1 Issue 3 both note this is now an established codebase convention. It is not mentioned in CLAUDE.md's architecture invariants or the testing section.

3. **`CapturingLogger<T>` test helper** — SUMMARY-3.1 Decision 7 explains why it exists and when to prefer it over NSubstitute on `ILogger<T>`. Future test authors will either reinvent it or use NSubstitute incorrectly without this guidance.

### LOW — note only

- The WSL `pgrep | kill -INT` smoke-test recipe (SUMMARY-3.1 Issue 2) is a CI concern, not a contributor concern. Deferring to Phase 2 CI setup is appropriate.
- Plugin-author docs remain correctly deferred to Phase 11.

## Recommendations

Update `CLAUDE.md` with the three MEDIUM items above:
- Amend the `dotnet test` command in the Commands section to note the `dotnet run --project` workaround required on .NET 10 + MTP.
- Add a short note under Architecture invariants or Testing: "`[SetsRequiredMembers]` is required on any ctor that sets all `required init` properties."
- Add a sentence to the Testing section: "Prefer `CapturingLogger<T>` (defined inline in test files) over NSubstitute when asserting on `ILogger<T>` — NSubstitute's generic `Log` match is unreliable."
