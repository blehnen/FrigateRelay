# Review: Plan 3.1

## Verdict: CRITICAL_ISSUES

## Findings

### Critical

- **Task 3 (CLAUDE.md updates) was omitted entirely.** SUMMARY-3.1 lists only 4 modified files and does not mention CLAUDE.md. Three plan acceptance criteria fail:
  - **Stale ID-12 paragraph still present** (CLAUDE.md line 93): `"so the string-array shape silently produces an empty Actions list at runtime, dropping action firing without any error. Tracked as ID-12 in .shipyard/ISSUES.md..."`. The plan required this paragraph be replaced with a Phase-8-closure note now that `ActionEntryTypeConverter` makes both forms bind. Acceptance grep `git grep -nE 'silently produces an empty .Actions. list' CLAUDE.md` should return zero — currently returns line 93.
  - **D7 collect-all convention bullet absent** from the Conventions section. Plan required adding: "Startup validators accumulate errors into a shared List<string> and throw a single aggregated InvalidOperationException at the end. Pattern lives in StartupValidation.ValidateAll." Acceptance grep `git grep -nE 'collect-all|ValidateAll' CLAUDE.md` returns zero.
  - **"Phase 8 closed ID-12" note absent** from CLAUDE.md. Acceptance grep `git grep -nE 'Phase 8 closed ID-12' CLAUDE.md` returns zero.

### Minor

None — Tasks 1 and 2 are spec-compliant.

### Positive

- `config/appsettings.Example.json` is well-shaped: single `Standard` profile, 9 subscriptions all using `Profile: "Standard"` (no inline `Actions`), object-form action entries, no secrets, no IPs in JSON. Camera names, labels, zones mirror legacy.conf.
- `legacy.conf` IPs are RFC 5737 (`192.0.2.x` TEST-NET-1), not RFC 1918 — CI secret-scan returns clean.
- `ConfigSizeParityTest`:
  - D9 hard-fail with `Assert.Fail` and verbatim sanitization-checklist pointer when fixture missing.
  - D3 raw `File.ReadAllText().Length`, no normalization.
  - `ratio.Should().BeLessOrEqualTo(0.60, ...)` matches ROADMAP success criterion #2.
  - Sub-assertion `Json_Binds_And_Validates_Successfully` stubs every plugin/snapshot/validator named in the example and runs `StartupValidation.ValidateAll` — proves the example is structurally correct, not just short.
- Single physical `appsettings.Example.json` at `config/`; csproj `<Link>` keeps it next to the test binary at runtime without duplication.
- 56.7% parity ratio (well under the 60% gate); 1322 / 2329 chars; the 9-subscription INI body collapses to 9 one-liner subscription rows + 1 profile.
- Build clean (0 warn / 0 err); 69/69 tests pass (68 baseline + 1 ConfigSizeParityTest); --filter run isolates the new test passing 1/1.
- Public-surface guard intact: zero matches for `^public (sealed )?(class|record|interface) ` in `src/FrigateRelay.Host/`.
- ISSUES.md correctly shows ID-2, ID-10, ID-12 in the Closed Issues section (orchestrator's prior bookkeeping commit `544516e`).
- Commit prefix `shipyard(phase-8):` correctly used (recovers from PLAN-2.1's `feat(host)`/`test(host)` drift).

## Required fix
Apply Task 3 of PLAN-3.1: update CLAUDE.md to remove the stale ID-12 invariant paragraph and add the D7 collect-all conventions bullet. Orchestrator will perform this fix inline since the code is otherwise spec-compliant; verifier will confirm in Step 5.

Critical: 1 | Minor: 0 | Positive: 12.
