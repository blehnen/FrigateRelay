# CONTEXT-8 — Phase 8 Discussion Decisions

**Phase.** 8 — Profiles in Configuration
**Status.** Decisions captured (2026-04-26). Authoritative input for the researcher and architect.

This document records user decisions on the gray areas surfaced before plan generation. Downstream agents (researcher, architect, builder, reviewer) MUST treat these as binding. Any deviation is a regression and must be surfaced as a question, not silently chosen otherwise.

---

## D1 — Profile + inline composition is **mutually exclusive**

**Decision.** A `Subscription` declares **either** `Profile: "<name>"` **or** an inline `Actions:` array — never both. Setting both is a startup configuration error.

**Behavior.**
- `Subscription` with `Profile: "X"` and no `Actions:` → effective action list comes entirely from `Profiles.X.Actions`.
- `Subscription` with `Actions: [...]` and no `Profile:` → effective action list is the inline list.
- `Subscription` with **both** `Profile:` and `Actions:` → host fails fast at startup with a clear diagnostic naming the offending subscription, e.g. `"Subscription '<name>' may declare either 'Profile' or 'Actions', not both."`
- `Subscription` with **neither** `Profile:` nor `Actions:` → host fails fast at startup with: `"Subscription '<name>' must declare either 'Profile' or 'Actions'."` (a subscription with no actions is a config error, not a no-op.)

**Architectural impact.**
- `SubscriptionOptions` keeps both `Profile: string?` and `Actions: IReadOnlyList<ActionEntry>?` properties (both nullable).
- A new validator (call it `ProfileResolutionValidator` or similar) runs in startup validation alongside the existing `StartupValidation` pipeline (Phase 7 pattern), expanding `Profile:` references into the effective `Actions` list before the dispatcher sees the subscription.
- After resolution, downstream code (matcher, dispatcher) sees only the **resolved** action list — it must not need to know whether the actions came from a profile or inline.

**Why this option.** Simplest schema and validator. No "override precedence" question to answer. No "did the operator intend the profile or the inline list?" ambiguity. Profile dedup is the stated goal of Phase 8 (Success Criterion #2 measures the JSON-vs-INI character ratio); supporting inline-extends-profile composition would dilute that signal.

---

## D2 — ID-12 (`Actions: ["BlueIris"]` string-form silently dropped under `IConfiguration.Bind`) is **fixed in Phase 8**

**Decision.** Add an `ActionEntryTypeConverter` (and registration) as a Phase 8 task. Close ID-12 in `ISSUES.md` when the fix lands.

**Scope.**
- `[TypeConverter(typeof(ActionEntryTypeConverter))]` attribute on the `ActionEntry` record.
- `ActionEntryTypeConverter : TypeConverter` with `CanConvertFrom(string)` returning `true`, `ConvertFrom(string)` returning `new ActionEntry(stringValue)`. (The `ActionEntry(string)` ctor already exists per ID-12 description.)
- ~3 unit tests in `tests/FrigateRelay.Host.Tests/Configuration/ActionEntryTypeConverterTests.cs` proving:
  - String-array form `Actions: ["BlueIris"]` binds correctly via `IConfigurationBuilder.Build().Bind(...)`.
  - Object form `Actions: [{ "Plugin": "BlueIris" }]` continues to bind correctly.
  - Mixed array `Actions: ["BlueIris", { "Plugin": "Pushover", "SnapshotProvider": "Frigate" }]` binds with both elements correctly populated.
- One integration-test fixture using the legacy string-form to prove the bind regression is closed end-to-end (re-using the Phase 4 `MqttToBlueIrisSliceTests` shape with a string-array `Actions:` config).
- Update CLAUDE.md's ID-12 invariant block: replace "the string-array shape silently produces an empty `Actions` list" with a "since Phase 8" note that both forms work; preserve the historical context as a "Phase 8 closed ID-12 by adding `ActionEntryTypeConverter`" line.

**Why this option.** Phase 8 is the only phase that explicitly touches the action-list config shape. Bundling the fix here means all action-binding logic lives in one phase, the new `appsettings.Example.json` fixture cannot accidentally regress, and CLAUDE.md gets one coherent update instead of two. ID-12 is **Medium** severity (silent action drop on operator upgrade) and should not bake into Phase 9+.

**Out-of-scope.** No changes to `ActionEntryJsonConverter` (the existing `[JsonConverter]` for direct `JsonSerializer.Deserialize` paths). The two converters operate on disjoint code paths and both are needed.

---

## D3 — `ConfigSizeParityTest` measures **real sanitized production INI**

**Decision.** The fixture INI committed at `tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf` is a sanitized copy of the author's actual `FrigateMQTTProcessingService.conf`. Synthetic INIs are not used.

**Implication for Success Criterion #2.** The 60% character-count gate measures the **real** repetition the rewrite was supposed to eliminate. If the author's actual deployment has 9 subscriptions averaging 14 lines each in INI form, the JSON profile shape must reduce that to ≤ 60% of the INI character count to pass. The gate is therefore a TRUE end-to-end proof of the design value, not a synthetic benchmark.

**Sanitization rules** (encoded in a SANITIZATION-CHECKLIST.md the architect must produce as part of plan output):
- Replace any IP address matching `[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+` with `example.local` or a `192.0.2.x` (RFC 5737 documentation prefix) value.
- Replace any port-bearing host like `host:5001` with `example.local:5001` (preserve port).
- Replace `AppToken=<value>` and `UserKey=<value>` with `<redacted>` literal strings.
- Replace any `username:password@` segments inside URLs with `<user>:<pass>@`.
- Preserve all camera names, object labels, zone names, profile/subscription **structure**, and whitespace — these drive the character-count comparison and must reflect the real deployment's verbosity.
- Run `git grep -E '[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+|AppToken=[A-Za-z0-9]{20,}|UserKey=[A-Za-z0-9]{20,}|api[a-z0-9]{28,}'` against the fixture before commit; expect zero matches outside RFC 5737 prefixes.

**Why this option.** Success Criterion #2 from PROJECT.md is "configuration is meaningfully shorter than the legacy INI." A synthetic INI lets the test pass against a benchmark we control; only the real conf measures whether the design actually solves the operator's problem. Leakage risk is mitigated by an auditable sanitization checklist + a `secret-scan.yml` tripwire that already exists.

---

## D4 — Snapshot resolution stays at **3 tiers**: per-action → per-subscription → global

**Decision.** The `SnapshotResolver` precedence chain established in Phase 5 is unchanged. Profiles do **not** introduce a new resolution tier.

**Behavior with profiles.**
- Profile actions can carry per-action `SnapshotProvider:` overrides (Tier 1 — same as inline actions).
- The subscription's own `DefaultSnapshotProvider:` (Tier 2) applies regardless of whether the action came from a profile or inline.
- Global `DefaultSnapshotProvider:` (Tier 3) is the floor.
- Profiles do **not** have their own `DefaultSnapshotProvider:` field. Profiles are pure action-list shapes.

**Architectural impact.**
- `ProfileOptions` has `Actions: IReadOnlyList<ActionEntry>` and **nothing else**. No `DefaultSnapshotProvider:` field.
- `SnapshotResolver` does not change — it already takes `(perActionProvider, perSubscriptionProvider, globalProvider)` and returns the first non-null one.
- The dispatcher's `DispatchItem` already carries the per-action and per-subscription provider names (Phase 6 wiring); profile expansion produces the same shape.

**Why this option.** Phase 5 was deliberate about the 3-tier order ("per-action override → per-subscription default → global `DefaultSnapshotProvider`" — quoting CLAUDE.md). Adding a 4th tier or changing precedence is a behavioral change that operators upgrading from Phase 5 would not expect. Profiles solve the **action-list repetition** problem; they should not also solve a snapshot-resolution problem that wasn't broken.

---

## D5 — Profiles are a **flat dictionary**; no `BasedOn` / nesting

**Decision.** `Profiles:` binds to `Dictionary<string, ProfileOptions>`. No profile-to-profile composition, no inheritance, no cycle-detection logic needed.

**Schema.**
```jsonc
{
  "Profiles": {
    "Standard":   { "Actions": [{"Plugin": "BlueIris"}, {"Plugin": "Pushover", "SnapshotProvider": "Frigate"}] },
    "Validated":  { "Actions": [{"Plugin": "BlueIris"}, {"Plugin": "Pushover", "Validators": ["CodeProjectAi"]}] }
  },
  "Subscriptions": [
    { "Camera": "front", "Object": "person", "Profile": "Standard" },
    { "Camera": "doorbell", "Object": "person", "Profile": "Validated" }
  ]
}
```

**What is explicitly NOT supported.**
- `Profiles.X.BasedOn: "Y"` — not a field. `ProfileOptions` has only `Actions:`.
- A profile referencing another profile by name inside its `Actions` list — not supported. `ActionEntry.Plugin` must name a registered `IActionPlugin`, never another profile.
- Action-level "extends profile" — not supported, follows from D1.

**Why this option.** Phase 8's deliverable is profile-level dedup across subscriptions (9× → 1× per profile). Action-list dedup across profiles is a second-order optimization that does not appear in PROJECT.md or ROADMAP.md and is not measured by Success Criterion #2. The flat shape keeps the validator a single pass over the dictionary with O(profiles + subscriptions) complexity and zero cycle-detection code.

---

## D6 — Sanitized INI fixture: **user-provided before/during build**, with auditable checklist

**Decision.** The plan does NOT include a builder task that reads the author's real `FrigateMQTTProcessingService.conf` from any path. The user manually sanitizes their real conf and places it at `tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf` before the build step that needs it (or the build pauses with a clear prompt if the file is missing).

**Architect responsibilities.**
- Produce `.shipyard/phases/8/SANITIZATION-CHECKLIST.md` (or include the checklist as a section in PLAN-{W}.{P}.md for the relevant task) so the user has a clear, auditable list of what to redact and how.
- Add a builder task whose acceptance criterion includes: "If `tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf` does not exist, fail the build with a message pointing at SANITIZATION-CHECKLIST.md and stop. Do NOT generate a synthetic substitute."
- Add a CI tripwire (greps the fixture for `[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+` excluding `192.0.2.` and `203.0.113.` and `198.51.100.` RFC 5737 prefixes; greps for `AppToken=[A-Za-z0-9]{20,}`, `UserKey=[A-Za-z0-9]{20,}`, `api[a-z0-9]{28,}`) so a re-introduced secret blocks the build.

**Why this option.** Auto-sanitization via regex pass produces a fixture whose redaction quality is whatever the regex catches — a missed pattern means a leaked secret in the public repo. Manual sanitization with a checklist places redaction in human hands and lets the tripwire catch what humans miss. The cost is one manual step at build time, paid once.

---

## Cross-cutting notes for the architect

1. **Fail-fast diagnostics format.** All Phase 8 startup-validation messages should follow the existing Phase 7 `StartupValidation` shape (one log line per error, then an aggregated `InvalidOperationException` thrown at the end). Do not bail on the first error — collect all errors so an operator with three misconfigurations sees all three at once. (This matches the legacy lesson the rewrite is meant to demonstrate: actionable, not piecemeal, diagnostics.)

2. **Validator interaction with Phase 7.** Profile actions with `Validators: [...]` reuse the per-action validator chain wired in Phase 7. The `ValidateValidators` pass (Phase 7 closed) must continue to fire after profile expansion — i.e. profile expansion runs **before** validator-existence checks, so undefined validator names inside a profile produce the same `validator_unregistered` error an inline action would.

3. **`appsettings.Example.json` location.** ROADMAP says `config/appsettings.Example.json`. There is currently no `config/` directory in tree (verified: `ls config/` returned empty in the planning preflight). The architect creates this directory in Phase 8.

4. **`appsettings.Example.json` content target.** Reproduce the author's 9-subscription production deployment using the new Profiles shape — i.e. the `appsettings.Example.json` is the JSON counterpart to the `tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf` fixture. The character-count comparison test reads BOTH files. Names, labels, zones must align so the comparison is meaningful.

5. **Plan size.** Phase 8 is **Low risk / 2–3 hour estimate** per ROADMAP. Plan should be small — likely 2 waves, 3-4 plans total, well under the ≤3-tasks-per-plan ceiling. Do not over-decompose.

6. **Issues to consider beyond ID-12.** ID-2 and ID-10 are about internal-vs-public visibility of `ActionEntry`, `ActionEntryJsonConverter`, `SnapshotResolverOptions`, `IActionDispatcher`, `DispatcherOptions`, `SubscriptionOptions`, `HostSubscriptionsOptions`. Phase 8 will modify `ActionEntry` (TypeConverter) and `SubscriptionOptions` (add `Profile:` property), and create `ProfileOptions`. **Resolved by D8 below — bundled into Phase 8.**

---

## D7 — Existing startup validators are **retrofit to collect-all** in Phase 8

**Decision.** The Phase 7 `StartupValidation` pipeline (and any earlier validators that throw on first error — validator-existence, action-existence, snapshot-provider-existence) is retrofit so each validator accumulates errors into a list. After all validators run, if any errors exist, a single `InvalidOperationException` is thrown whose `Message` is a multi-line aggregate of all collected errors.

**Shape.**
```csharp
// before
if (!registered.Contains(name))
    throw new InvalidOperationException($"Validator '{name}' is not registered.");

// after
if (!registered.Contains(name))
    errors.Add($"Validator '{name}' is not registered.");
// ...end of pass:
if (errors.Count > 0)
    throw new InvalidOperationException(
        "Startup configuration invalid:\n  - " + string.Join("\n  - ", errors));
```

**Scope (architect must enumerate every validator touched).**
- The Phase 7 `StartupValidation.ValidateValidators` pass.
- Any `ValidateActionPlugins` / action-name existence pass (Phase 4-era).
- Any `ValidateSnapshotProviders` pass (Phase 5).
- The new Phase 8 profile-existence + profile-vs-inline-mutex + plugin-name-existence-inside-profile-actions pass (D1).
- All passes share one accumulator; the host throws once at the end.

**Why.** Operators with three misconfigurations should see all three at once, not chase them one fix at a time. This was already the spirit of CONTEXT-8 cross-cutting note 1; D7 makes it explicit and binding for the entire startup validation surface, not just the new Phase 8 validator. Consistency across all validators is preferred over cherry-picked retrofit.

**Architect responsibilities.**
- Locate every existing throw-on-first-error validator in `src/FrigateRelay.Host/`. Document the list in the relevant PLAN.
- Provide a shared `StartupValidationContext` (or equivalent — could be as simple as a `List<string> errors`) that all validators write into.
- Update existing tests that asserted on the single-error message to assert on substring presence within the aggregated message (or update to match the new multi-line shape).

---

## D8 — ID-2 + ID-10 visibility sweep is **bundled into Phase 8**

**Decision.** A single Phase 8 task internalizes the following types in one pass:

- `ActionEntry` (currently `public`)
- `ActionEntryJsonConverter` (currently `public`)
- `SnapshotResolverOptions` (currently `public`)
- `SubscriptionOptions` (currently `public`)
- `HostSubscriptionsOptions` (currently `public`)
- `IActionDispatcher` (currently `public`)
- `DispatcherOptions` (currently `public`)
- `DedupeCache` (currently `public`)
- `SubscriptionMatcher` (currently `public`)

Plus the new `ProfileOptions` type — created `internal` from the start.

**Implementation.**
- Flip every type above to `internal sealed` (where applicable — interfaces stay `internal` but not sealed).
- The Phase 5 trade-off (raise `ActionEntry` to `public` to satisfy CS0053 because `SubscriptionOptions.Actions` was `public IReadOnlyList<ActionEntry>`) cascades the other direction: with `SubscriptionOptions` now `internal`, every nested element type can also be `internal`.
- Add `<InternalsVisibleTo Include="FrigateRelay.Host.Tests" />` to `src/FrigateRelay.Host/FrigateRelay.Host.csproj` if any tests cross the boundary (per CLAUDE.md convention — MSBuild item, not source-level attribute). It already exists per the codebase notes; verify and re-use.
- Run `dotnet build FrigateRelay.sln -c Release` after the sweep — must remain warning-free.

**Closes.** ID-2 (Phase 4 reviewer note), ID-10 (Phase 5 SUMMARY-1.2 deferred decision).

**Why.** Phase 8 modifies `ActionEntry`, `ActionEntryJsonConverter`, `SubscriptionOptions`, `HostSubscriptionsOptions` directly (for profile binding + TypeConverter). CS0053 constraints make the visibility flip all-or-nothing — flipping one without flipping the chain produces compile errors. Doing the full sweep in the same phase that touches these files avoids re-opening the files for a cleanup pass later. Closes 2 open ISSUES at no additional file-touch cost.

---

## D9 — `ConfigSizeParityTest` behavior on missing fixture: **hard fail with checklist link**

**Decision.** When `tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf` does not exist, `ConfigSizeParityTest` fails the test run with a clear, actionable message:

```
Assert.Fail(
    "legacy.conf fixture missing at " + fixturePath + ". " +
    "Sanitize your real FrigateMQTTProcessingService.conf per " +
    ".shipyard/phases/8/SANITIZATION-CHECKLIST.md and place the " +
    "redacted result at the path above. This test cannot run without it.");
```

**Behavior.** No environment-detection branch (no `GITHUB_ACTIONS` / `JENKINS_HOME` check). The test fails identically in CI and on local dev runs.

**Why.**
- Matches D6 ("build pauses with a clear prompt if missing").
- A test that silently skips on local dev and only fails in CI is a flaky-feeling test — contributors run a green local suite, push, and watch CI fail; they then have to discover why. A hard fail with a clear remediation message converts that into a one-time setup step every contributor performs once.
- Phase 8 Success Criterion #2 ("ConfigSizeParityTest passes — Success Criterion #2 is now a CI gate, not a claim" — quoting ROADMAP) is only meaningful if the gate is unconditional. `Assert.Inconclusive` paths weaken the gate to "passes when the fixture exists, undefined when it doesn't" — that's not a gate.

**Architect responsibilities.**
- The PLAN task that creates `ConfigSizeParityTest` must explicitly call out the hard-fail-on-missing behavior in its acceptance criteria.
- The `SANITIZATION-CHECKLIST.md` must be committed BEFORE or in the same commit as the test, so the failure message points at a real file from day one.
- The plan should sequence things so the user is prompted for the fixture early in the build, not at the end (a builder task that pauses for the user to provide the file is fine — but the prompt should appear before the integration tests run, not after).

---

## Decision summary table

| ID | Topic | Decision |
|----|-------|----------|
| D1 | Profile + inline composition | Mutually exclusive; fail-fast if both, fail-fast if neither |
| D2 | ID-12 (string-form `Actions:` regression) | Fixed in Phase 8 via `ActionEntryTypeConverter` |
| D3 | `ConfigSizeParityTest` reference | Real sanitized production INI |
| D4 | Snapshot precedence with profiles | Unchanged 3-tier (per-action → per-subscription → global) |
| D5 | Profile composition | Flat dictionary; no `BasedOn` / nesting |
| D6 | Sanitized INI fixture sourcing | User-provided; auditable `SANITIZATION-CHECKLIST.md`; build pauses if missing |
| D7 | Startup validator error reporting | Collect-all retrofit across all validators |
| D8 | ID-2 + ID-10 visibility sweep | Bundled into Phase 8; 9 types flipped to `internal` |
| D9 | `ConfigSizeParityTest` missing fixture | Hard fail in all environments with checklist link |
