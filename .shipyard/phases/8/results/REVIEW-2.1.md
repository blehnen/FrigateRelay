# Review: Plan 2.1

## Verdict: MINOR_ISSUES

## Findings

### Critical
None.

### Minor

- **`ValidateAll` does not validate the global snapshot default provider** — `src/FrigateRelay.Host/StartupValidation.cs` line 40 passes `globalDefaultProviderName: null` to `ValidateSnapshotProviders`. The pre-PLAN-2.1 `HostBootstrap.ValidateStartup` read `snapshotOpts.DefaultProviderName` from `IOptions<SnapshotResolverOptions>` and passed it through. The new `ValidateAll` silently drops this check. While `SnapshotResolverOptions` uses `.ValidateOnStart()` data annotations, that only checks required-ness, not that the name maps to a registered `ISnapshotProvider`. A typo will no longer be caught at startup.
  - Remediation: in `ValidateAll`, add `var snapshotOpts = services.GetRequiredService<IOptions<SnapshotResolverOptions>>().Value;` and pass `snapshotOpts.DefaultProviderName` to `ValidateSnapshotProviders`.

- **Test 8 (`ValidateAll_ProfileActionUnknownValidator_ReportsValidatorError`) asserts only `"*'Bogus'*"`** — `tests/FrigateRelay.Host.Tests/Configuration/ProfileResolutionTests.cs` lines 246–248. Plan requires assertion that the message contains the validator name AND the registered-names list (Phase 7 message shape). The current `WithMessage("*'Bogus'*")` is too broad.
  - Remediation: tighten to `.WithMessage("*'Bogus'*not registered*")` or split into `.Which.Message.Should().Contain("Bogus").And.Contain("recognized Type")`.

- **Commit-prefix convention deviates from project standard** — three plan commits use `test(host):` / `feat(host):` while all prior Phase 4–8 plan commits use `shipyard(phase-N):`. Cosmetic but breaks `git log --oneline` phase scanning. Future commits should match the established pattern.

- **`ProfileResolutionTests` helper `ValidationPlugin(string key)` is unused** — file lines 44–49 define a helper that no test calls. Remove or wire up.

- **`StartupValidationSnapshotTests.ValidateSnapshotProviders_WithUnknownSubscriptionDefault_Throws`** uses a subscription with neither `Actions` nor `Profile`. After PLAN-2.1, such a subscription would fail D1 mutex via `ProfileResolver.Resolve` before reaching `ValidateSnapshotProviders`. The test calls the method directly so it still passes in isolation, but is not representative of the post-resolver runtime flow. No defect; flag for documentation.

### Positive

- D1 mutex enforced verbatim per CONTEXT-8 (both messages match exactly).
- D5 flat dictionary — single `Profiles.TryGetValue` lookup; no `BasedOn` / nesting / cycle-detection.
- Profile expansion correctly clones `SubscriptionOptions` with `Profile = null` so downstream sees only resolved actions.
- Undefined-profile error includes registered-names list, matching ROADMAP success-criterion shape.
- D9 scope correctly interpreted: `ConfigSizeParityTest` fixture behavior, distinct from collect-all retrofit (which is D7). No mismatch.
- Single aggregated `throw new InvalidOperationException` site in `StartupValidation.cs:47`; zero throws in `ProfileResolver.cs`.
- 10 `ProfileResolutionTests` — meets ROADMAP success criterion exactly.
- Existing tests migrated cleanly to accumulator signature; one bad config still produces one error; assertion semantics preserved.
- Build clean (0 warnings / 0 errors); 68/68 tests pass (58 baseline + 10 new).
- Public-surface guard intact: zero `^public (sealed )?(class|record|interface) ` matches in `src/FrigateRelay.Host/`.
- ChannelActionDispatcher untouched (correct — D9 was not about dispatcher chain).
- TDD discipline: red-phase commit `4e1c683` precedes green-phase `c9a0b4a`.

## Summary
Critical: 0 | Minor: 5 | Positive: 12. APPROVE — implementation correctly delivers all CONTEXT-8 decisions for PLAN-2.1. The orchestrator should fix the global-snapshot-default regression before phase verification (1-line change) and tighten the `'Bogus'` assertion.
