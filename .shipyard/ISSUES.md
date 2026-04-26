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

### ID-2: `IActionDispatcher`/`DispatcherOptions` should be `internal`

**Source:** verifier (Phase 4 post-build review, REVIEW-1.1)
**Severity:** Minor
**Status:** Open

**Description:**
`IActionDispatcher` and `DispatcherOptions` in `src/FrigateRelay.Host/Dispatch/` are declared `public`. These are host-internal types with no external consumers. They should be `internal` to correctly express the boundary.

**Impact:** API surface correctness. No functional impact. No downstream consumers exist yet — cleanest to fix before Phase 5 adds more host-internal types.

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

### ID-10: `ActionEntry`, `ActionEntryJsonConverter`, `SnapshotResolverOptions` raised to `public` — should be `internal`

**Source:** reviewer (Phase 5 REVIEW-1.2, 2026-04-26)
**Severity:** Minor
**Status:** Open

**Description:**
`ActionEntry` (`src/FrigateRelay.Host/Configuration/ActionEntry.cs:18`), `ActionEntryJsonConverter` (`src/FrigateRelay.Host/Configuration/ActionEntryJsonConverter.cs:15`), and `SnapshotResolverOptions` (`src/FrigateRelay.Host/Snapshots/SnapshotResolverOptions.cs:7`) are declared `public`. Per PLAN-1.2 Task 1 acceptance criteria, these should be `internal`. They were raised to `public` during the build to resolve CS0053 (public `SubscriptionOptions.Actions: IReadOnlyList<ActionEntry>` cannot have an internal element type).

The deliberate trade-off was to raise the new types rather than cascade `SubscriptionOptions`, `HostSubscriptionsOptions`, `DedupeCache`, and `SubscriptionMatcher` to `internal`. This is documented in SUMMARY-1.2 Decisions.

**Fix:** Consolidate into the existing ID-2 sweep. When `IActionDispatcher` and `DispatcherOptions` are internalized (ID-2), also internalize `ActionEntry`, `ActionEntryJsonConverter`, `SnapshotResolverOptions`, `SubscriptionOptions`, and `HostSubscriptionsOptions` in the same pass. All of these are host-internal configuration types with no external consumers.

**Impact:** API surface correctness. No functional impact. No external consumers exist.

### ID-11: `CapturingLogger<T>` duplicated across test assemblies — Rule of Three  *[RESOLVED 2026-04-26]*

**Source:** reviewer (Phase 5 REVIEW-2.2, 2026-04-26)
**Severity:** Minor
**Status:** **Resolved** (Phase 6 prep cleanup, commit pending)

**Resolution:**
Extracted to `tests/FrigateRelay.TestHelpers/FrigateRelay.TestHelpers.csproj` (`OutputType=Library`, no test runner deps). `CapturingLogger<T>` raised to `public sealed`, namespace `FrigateRelay.TestHelpers`. The 4 duplicate copies (Host.Tests, BlueIris.Tests, FrigateSnapshot.Tests, Pushover.Tests) deleted. Each test csproj gains a `<ProjectReference>` to TestHelpers; type is exposed via `global using FrigateRelay.TestHelpers;` in a `Usings.cs` per test project. `run-tests.sh` glob (`tests/*.Tests/*.Tests.csproj`) does not pick up the helper project (no `.Tests` suffix). 124/124 tests still pass after extraction.

---

### ID-12: `Actions: ["BlueIris"]` string-form back-compat broken under `IConfiguration.Bind`

**Source:** Phase 5 build (integration test regression, 2026-04-26)
**Severity:** Medium
**Status:** Open

**Description:**
PLAN-1.2 promised back-compat via `ActionEntryJsonConverter` so existing `appsettings.json` fixtures using `"Actions": ["BlueIris"]` (string-array form) would still bind. The converter works for direct `JsonSerializer.Deserialize` calls (5 unit tests in `ActionEntryJsonConverterTests` confirm this), **but does not fire when `IConfiguration.Bind` reads the configuration tree**.

The Configuration binder builds `IReadOnlyList<ActionEntry>` by iterating `Actions:0`, `Actions:1`, ... and constructing each `ActionEntry` from child paths. When the path resolves to a scalar string (e.g. `Subscriptions:0:Actions:0 = "BlueIris"`), the binder has no `TypeConverter` for `ActionEntry` and silently produces an empty `Actions` list. The `MqttToBlueIris_HappyPath` integration test caught this — it failed with "found 0" trigger fires until the fixture was updated to the object form `Subscriptions:0:Actions:0:Plugin = "BlueIris"`.

This means existing operators with Phase 4 `appsettings.json` files using the string-array shape will silently lose action firing on upgrade to Phase 5.

**Fix options:**
1. **`TypeConverter` on `ActionEntry`** — implement `ActionEntryTypeConverter` that converts a string to `new ActionEntry(stringValue)`. Register via `[TypeConverter(typeof(...))]` on the record. The Configuration binder uses `TypeConverter` for primitive-like types, so this would handle the scalar case.
2. **Custom `IConfigureOptions<HostSubscriptionsOptions>`** — post-process the `Actions` list after binding by re-walking `IConfiguration` paths and patching scalar children into the object form.
3. **Document the breaking change** — accept that the migration requires fixture updates, surface it in the upgrade notes.

Recommended: Option 1. The TypeConverter pattern is small (~15 LOC + 3 unit tests) and idiomatic for `Microsoft.Extensions.Configuration`.

**Impact:** Operator upgrade path. No data loss, but actions silently stop firing if the legacy string-array shape is used. Fail-fast would be preferable but the binder doesn't surface bind failures by default.

## Closed Issues

(None)
