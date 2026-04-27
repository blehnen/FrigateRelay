# Issue Tracker

## Open Issues

### ID-1: Simplify IEventMatchSink justification in PLAN-3.1

**Source:** verifier (Phase 3 spec-compliance review)
**Severity:** Non-blocking (clarity improvement)
**Status:** Open

**Description:**
PLAN-3.1 Task 1 Context section includes a detailed multi-paragraph explanation of why `IEventMatchSink` is added to Abstractions. The justification is correct, but could be condensed to a single sentence for readability:

> "IEventMatchSink keeps Host plugin-agnostic by delegating event routing and dedupe logic to the plugin implementation."

**Current text (lines in PLAN-3.1 Context):**
"EventPump becomes a trivial fan-out... Plugin implements it doing matcher + dedupe + log. Simpler."

**Suggested revision:**
Replace the above with the one-liner above, or similar.

**Impact:** Documentation clarity only. No code changes needed.

**Owner:** (None assigned; deferred for Phase 3 builder to address if desired)

---

### ID-3: `TargetFramework` missing from BlueIris csproj(s)

**Source:** verifier (Phase 4 post-build review, REVIEW-1.2)
**Severity:** Minor
**Status:** Open

**Description:**
`src/FrigateRelay.Plugins.BlueIris/FrigateRelay.Plugins.BlueIris.csproj` (and possibly the test csproj) may be missing an explicit `<TargetFramework>net10.0</TargetFramework>`, relying solely on `Directory.Build.props` inheritance. Build passes but explicit declaration is preferred for clarity and tooling support.

**Impact:** Non-functional. Build succeeds via inheritance.

---

### ID-4: `--filter-query` flag in CLAUDE.md may be stale for MSTest v4.2.1

**Source:** verifier (Phase 4 post-build review, REVIEW-1.2)
**Severity:** Minor
**Status:** Open

**Description:**
CLAUDE.md documents `--filter-query` as the MTP single-test filter flag. The installed runner is MSTest v4.2.1 (confirmed in test output). The correct flag for this version should be verified and CLAUDE.md updated if it differs.

**Impact:** Developer friction when running a single test by name. No test-correctness impact.

---

### ID-5: `CapturingLogger<T>` duplicated as inner class in dispatcher tests

**Source:** verifier (Phase 4 post-build review, REVIEW-2.1)
**Severity:** Minor
**Status:** Resolved (commit `c68dfaf`, 2026-04-25)

**Description:**
`CapturingLogger` was defined as a private inner class in `tests/FrigateRelay.Host.Tests/Dispatch/ChannelActionDispatcherTests.cs`. CLAUDE.md documents the capturing-logger pattern as a shared convention.

**Resolution:**
Extracted to `tests/FrigateRelay.Host.Tests/CapturingLogger.cs` as `internal sealed class CapturingLogger<T> : ILogger<T>` (commit `c68dfaf`). Inner class removed; `ChannelActionDispatcherTests` updated to use `CapturingLogger<ChannelActionDispatcher>`. All 29 Host tests pass.

---

### ID-6: `OperationCanceledException` sets `ActivityStatusCode.Error` in dispatcher consumer

**Source:** verifier (Phase 4 post-build review, REVIEW-2.1)
**Severity:** Minor
**Status:** Open

**Description:**
In `ChannelActionDispatcher`'s consumer loop, an `OperationCanceledException` (which occurs during graceful shutdown) sets the OTel `Activity` status to `ActivityStatusCode.Error`. This is semantically incorrect — graceful cancellation is not an error. The status should be `Unset` or `Ok` when cancelled via the shutdown token.

**Impact:** Misleading OTel traces during normal host shutdown. Low-risk one-line fix.

---

### ID-7: CONTEXT-4 D3 lists `{score}` placeholder but parser does not accept it

**Source:** verifier (Phase 4 post-build review, REVIEW-2.2)
**Severity:** Note (doc stale)
**Status:** Open

**Description:**
CONTEXT-4 D3 defines 5 URL template placeholders including `{score}`. The architect dropped `{score}` during plan design (recorded in PLAN-1.2 Task 2 and the pre-execution VERIFICATION.md Section G) because `EventContext` has no `Score` property. The code is correct — the parser's allowlist contains only `{camera}`, `{label}`, `{event_id}`, `{zone}`. However, CONTEXT-4 D3 has not been updated to remove `{score}` from the table.

**Impact:** The stale CONTEXT-4 doc will mislead the Phase 5 builder who reuses the templater for `BlueIrisSnapshot`. Remove the `{score}` row from CONTEXT-4 D3, or add a strikethrough/note that it was deferred.

---

### ID-8: `PASS_THROUGH_ARGS` not forwarded in `--coverage` branch of `run-tests.sh`

**Source:** verifier (Phase 4 post-build review, REVIEW-3.2)
**Severity:** Minor
**Status:** Open

**Description:**
In `.github/scripts/run-tests.sh`, the `--coverage` branch (lines 67-70) calls `dotnet run` without appending `"${PASS_THROUGH_ARGS[@]}"`. Only the non-coverage branch (line 86) passes them through. Any extra arguments (e.g. a future `--filter-query`) are silently dropped in Jenkins coverage runs.

**Fix:** Append `"${PASS_THROUGH_ARGS[@]}"` to the `dotnet run` invocation at line 70, after the `--coverage-output` argument.

**Impact:** Silent arg drop in Jenkins coverage runs. Does not affect CI (non-coverage) or local fast runs.

---

### ID-9: User-facing documentation deferred to Phase 12

**Source:** documenter (Phase 4 post-build review, DOCUMENTATION-4)
**Severity:** Minor
**Status:** Deferred (user decision, 2026-04-25)

**Description:**
Phase 4 ships a working MQTT → BlueIris vertical slice but the repo has no `README.md`, no `docs/` tree, and no operator-facing configuration documentation. XML doc-comments on the public abstractions surface are in good shape; the gap is operator/user docs.

**Decision:** Defer all documentation generation to ship time (Phase 12) per user choice. Rationale: Phases 5–7 add Snapshot providers, Pushover, and CodeProject.AI validator — operator docs written now would need substantial rewrites once those plugins land. Architecture and plugin-author docs similarly benefit from waiting until the plugin patterns are stable across 3+ implementations.

**Reactivation triggers:**
- A new contributor asks how to configure FrigateRelay — write a minimal `README.md` quickstart.
- Phase 11 (Operations & Docs) begins per ROADMAP — generate the full docs tree.
- An external user opens an issue asking for setup instructions.

---

### ID-11: `CapturingLogger<T>` duplicated across test assemblies — Rule of Three  *[RESOLVED 2026-04-26]*

**Source:** reviewer (Phase 5 REVIEW-2.2, 2026-04-26)
**Severity:** Minor
**Status:** **Resolved** (Phase 6 prep cleanup, commit pending)

**Resolution:**
Extracted to `tests/FrigateRelay.TestHelpers/FrigateRelay.TestHelpers.csproj` (`OutputType=Library`, no test runner deps). `CapturingLogger<T>` raised to `public sealed`, namespace `FrigateRelay.TestHelpers`. The 4 duplicate copies (Host.Tests, BlueIris.Tests, FrigateSnapshot.Tests, Pushover.Tests) deleted. Each test csproj gains a `<ProjectReference>` to TestHelpers; type is exposed via `global using FrigateRelay.TestHelpers;` in a `Usings.cs` per test project. `run-tests.sh` glob (`tests/*.Tests/*.Tests.csproj`) does not pick up the helper project (no `.Tests` suffix). 124/124 tests still pass after extraction.

---

## Closed Issues

### ID-2: `IActionDispatcher`/`DispatcherOptions` should be `internal` *[CLOSED 2026-04-27]*

**Source:** verifier (Phase 4 post-build review, REVIEW-1.1)
**Status:** **Closed** (commit `b5b87eb`, Phase 8 PLAN-1.1)

**Resolution:**
Phase 8 PLAN-1.1 visibility sweep flipped `IActionDispatcher` and `DispatcherOptions` to `internal`, alongside `SubscriptionOptions`, `HostSubscriptionsOptions`, `SnapshotResolverOptions`, `DedupeCache`, and `SubscriptionMatcher`. `<InternalsVisibleTo Include="DynamicProxyGenAssembly2" />` added in MSBuild item form to permit NSubstitute proxying of `IActionDispatcher`. Build green; 55 Host tests pass.

---

### ID-10: `ActionEntry`, `ActionEntryJsonConverter`, `SnapshotResolverOptions` raised to `public` — should be `internal` *[CLOSED 2026-04-27]*

**Source:** reviewer (Phase 5 REVIEW-1.2, 2026-04-26)
**Status:** **Closed** (commits `b5b87eb`, `e622a39`, `6264154`, Phase 8 PLAN-1.1 + PLAN-1.2)

**Resolution:**
Phase 8 PLAN-1.1 internalized `ActionEntryJsonConverter` and `SnapshotResolverOptions` (commit `e622a39`). Phase 8 PLAN-1.2 internalized `ActionEntry` itself (commit `6264154`) — feasible because the visibility sweep also internalized the surrounding `SubscriptionOptions` / `HostSubscriptionsOptions` carriers, eliminating the CS0053 cascade that originally forced the types public. All host-internal configuration types now correctly express the boundary.

---

### ID-12: `Actions: ["BlueIris"]` string-form back-compat broken under `IConfiguration.Bind` *[CLOSED 2026-04-27]*

**Source:** Phase 5 build (integration test regression, 2026-04-26)
**Severity:** Medium
**Status:** **Closed** (commit `6264154`, Phase 8 PLAN-1.2)

**Resolution:**
Implemented Option 1: `ActionEntryTypeConverter : TypeConverter` decorating `ActionEntry` via `[TypeConverter(typeof(ActionEntryTypeConverter))]`. `CanConvertFrom(string) => true`, `ConvertFrom(string s) => new ActionEntry(s)`. Coexists with the existing `[JsonConverter]` on disjoint code paths (binder uses TypeConverter; `JsonSerializer.Deserialize` uses JsonConverter). 3 TDD tests in `ActionEntryTypeConverterTests` exercise string-only / object-only / mixed array binding via `IConfiguration.Bind` — all green. Operators with Phase 4 `appsettings.json` files using the string-array shape `["BlueIris"]` will now bind correctly; the silent-drop regression is fixed.
