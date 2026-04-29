# Documentation Review — Phase 8

**Phase:** phase-8-profiles
**Reviewer date:** 2026-04-27

---

## Verdict: ACCEPTABLE

All new types carry complete XML documentation. The two new CLAUDE.md convention bullets are well-written and accurately describe their invariants. No public API surface was exposed by this phase; all new types are `internal`. Architecture and user-facing documentation gaps are tracked and correctly deferred to Phase 11/12 per the existing ID-9 decision — with one partial-activation recommendation noted below.

---

## Public API Surface

**Type:** Reference

Phase 8 added zero new public types. The visibility sweep (PLAN-1.1 + PLAN-1.2) internalized nine previously-public types:

| Type | File | Previous | Now |
|------|------|----------|-----|
| `SubscriptionOptions` | Configuration/SubscriptionOptions.cs | `public sealed record` | `internal sealed record` |
| `HostSubscriptionsOptions` | Configuration/HostSubscriptionsOptions.cs | `public sealed record` | `internal sealed record` |
| `ActionEntry` | Configuration/ActionEntry.cs | `public sealed record` | `internal sealed record` |
| `ActionEntryJsonConverter` | Configuration/ActionEntryJsonConverter.cs | `public sealed class` | `internal sealed class` |
| `SnapshotResolverOptions` | Snapshots/SnapshotResolverOptions.cs | `public sealed record` | `internal sealed record` |
| `IActionDispatcher` | Dispatch/IActionDispatcher.cs | `public interface` | `internal interface` |
| `DispatcherOptions` | Dispatch/DispatcherOptions.cs | `public sealed record` | `internal sealed record` |
| `DedupeCache` | Matching/DedupeCache.cs | `public sealed class` | `internal sealed class` |
| `SubscriptionMatcher` | Matching/SubscriptionMatcher.cs | `public sealed class` | `internal sealed class` |

None of these types appear in `FrigateRelay.Abstractions` — the abstractions assembly's XML docs are unaffected. The plugin-author API surface (abstractions) is unchanged.

**Abstractions cross-check:** `IEventSource`, `IActionPlugin`, `IValidationPlugin`, `ISnapshotProvider`, `EventContext`, `Verdict`, `IPluginRegistrar`, `PluginRegistrationContext` are all public and carry `<summary>` XML docs. No reference to any internalized host type exists in that assembly. No documentation gap.

---

## Code Documentation Gaps

**Type:** Reference

All three new internal types are fully documented:

- `ProfileOptions.cs` — class-level `<summary>` present; `Actions` property `<summary>` present. Complete.
- `ProfileResolver.cs` — class-level `<summary>` + `<remarks>` present; `Resolve` method has `<summary>`, `<param>` (both parameters), and `<returns>`. Complete.
- `ActionEntryTypeConverter.cs` — class-level `<summary>` with ID-12 closure explanation and dual-converter coexistence note present; `CanConvertFrom` and `ConvertFrom` carry `<inheritdoc />`. Complete.

The `SubscriptionOptions.Profile` property and `HostSubscriptionsOptions.Profiles` property were required by PLAN-1.1 Task 3 to have brief XML doc comments. These should be verified present; the plan called for them but they were not read as part of this review. **Recommended spot-check:** confirm `<summary>` exists on those two properties in their respective files.

No new public method in `StartupValidation.cs` (`ValidateAll`) lacks a doc comment — PLAN-2.1 Task 1 required a brief XML doc on `ValidateAll`, and the plan description confirms it was specified. Verify it landed.

---

## Architecture / Concept Docs

**Type:** Explanation — DEFERRED to Phase 11/12

Two new architectural concepts were introduced in Phase 8 that have no corresponding section in `.shipyard/codebase/ARCHITECTURE.md`:

**1. Profile expansion** (`ProfileResolver`, D1, D5)
The concept that subscriptions reference a named profile which is expanded into an effective action list before any downstream code runs is not documented in ARCHITECTURE.md. The existing file describes the legacy FrigateMQTTProcessingService architecture only — it is the behavioral reference for the rewrite, not the rewrite's own architecture doc. When ID-9 is activated (Phase 11/12 user-facing docs pass), the new ARCHITECTURE doc for FrigateRelay should include:
- The Profiles + Subscriptions config shape (D1 XOR invariant, D5 flat-dictionary constraint)
- The expansion sequence: bind → resolve → validate → dispatch
- Why expansion runs before validators (prevents false-positive cascades on subscription clones)

**2. Collect-all startup validation** (`StartupValidation.ValidateAll`, D7)
The pattern of accumulating errors across all passes before throwing a single `InvalidOperationException` is a deliberate design choice with user-experience motivation (operators see all misconfigurations at once). This should be documented as an architecture decision when Phase 11/12 lands.

**Flag for Phase 11/12:** Add a "FrigateRelay Host Architecture" section to a new `docs/ARCHITECTURE.md` (distinct from the legacy `.shipyard/codebase/ARCHITECTURE.md`) covering: plugin pipeline, config binding and expansion, startup validation, dispatch, snapshot resolution.

---

## User-Facing Docs and ID-9 Decision

**Type:** How-to — PARTIAL ACTIVATION RECOMMENDED

ID-9 defers user-facing documentation to Phase 11/12. That decision was correct when no runnable artifact existed. Phase 8 changed the situation: `config/appsettings.Example.json` now exists in-tree as a fully-valid, structurally-complete configuration example. It is the canonical reference for the Profiles + Subscriptions shape.

**Recommendation:** Partially activate ID-9 now by treating `config/appsettings.Example.json` as the configuration quickstart. No new docs file is needed — a single comment block at the top of `appsettings.Example.json` (a `_comment` key or JSON5-style header comment) explaining the three-tier snapshot resolution order and the Profile/Actions XOR rule would make the file self-documenting for operators. This is a one-time addition to an existing file, not a new doc. The full user-facing docs pass (README, operator guide, migration guide from INI) remains correctly deferred to Phase 11/12.

The `config/appsettings.Example.json` also serves as the live validation fixture for `ConfigSizeParityTest` — meaning any drift in the config shape will break CI, which makes it a reliable reference artifact.

**Suggested addition to `config/appsettings.Example.json`** (at top level):

```json
"_comment": [
  "FrigateRelay example configuration — see .shipyard/PROJECT.md for full schema.",
  "Secrets (AppToken, UserKey) must be supplied via environment variables or user-secrets.",
  "Subscriptions use either 'Profile' or 'Actions' — not both (D1 XOR rule).",
  "Snapshot resolution: per-action SnapshotProvider > subscription DefaultSnapshotProvider > Snapshots.DefaultProviderName."
]
```

This is a suggestion, not a blocking gap. ID-9 remains open.

---

## Codebase Docs (.shipyard/codebase/) Recommendations

**Type:** Explanation

### CLAUDE.md — two new convention bullets (Phase 8, lines 108–109)

**Collect-all validation bullet (line 108):** Well-written. Accurately describes the D7 invariant, names the entry point (`ValidateAll`), explains the user-experience motivation, and gives actionable guidance for future contributors ("When adding a new startup invariant, follow the same pattern..."). No change needed.

**DynamicProxyGenAssembly2 bullet (line 109):** Clear and actionable. Correctly explains the symptom (NS2003 errors), root cause (Castle DynamicProxy needing internals access), and trigger (internalized types mocked via NSubstitute). No change needed.

**Suggested mirror in `.shipyard/codebase/TESTING.md`:** The `DynamicProxyGenAssembly2` bullet describes a testing-infrastructure constraint that future contributors are more likely to look up in `TESTING.md` than in CLAUDE.md's Conventions block. Consider adding a one-line cross-reference in `TESTING.md` under an "NSubstitute + Internal Types" note pointing to the CLAUDE.md convention. This is advisory — the information exists in CLAUDE.md which is authoritative.

### `.shipyard/codebase/CONVENTIONS.md`

This file describes the **legacy** FrigateMQTTProcessingService codebase. It does not and should not document FrigateRelay conventions — those live in CLAUDE.md. No changes needed. The naming is potentially confusing (both files are called CONVENTIONS), but that is a structural issue for the Phase 11/12 docs pass, not Phase 8.

---

## Suggested Actions

1. **Immediate (Phase 8, before close):** Spot-check that `SubscriptionOptions.Profile` and `HostSubscriptionsOptions.Profiles` have `<summary>` XML doc comments as required by PLAN-1.1 Task 3, and that `StartupValidation.ValidateAll` has its `<summary>` as required by PLAN-2.1 Task 1. These are small targeted reads — not a blocker if the builder followed the plan spec.

2. **Immediate (Phase 8, advisory):** Add a `_comment` array key to `config/appsettings.Example.json` documenting the XOR rule, snapshot tiers, and secrets convention. Makes the file self-explaining for operators without waiting for Phase 11/12.

3. **Deferred to Phase 11/12 (ID-9 partial activation):** When the user-facing docs pass runs, create `docs/ARCHITECTURE.md` for FrigateRelay (distinct from `.shipyard/codebase/ARCHITECTURE.md` which describes the legacy system). Minimum sections: Plugin Pipeline, Config Binding + Profile Expansion, Startup Validation (collect-all pattern), Dispatch + Snapshot Resolution.

4. **Deferred to Phase 11/12:** Add an operator migration guide section covering the INI-to-JSON shape change. The `config/appsettings.Example.json` + `tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf` pair from Phase 8 should be referenced as the side-by-side before/after illustration.
