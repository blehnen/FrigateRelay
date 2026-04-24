# Documentation Review — Phase 2

## CLAUDE.md coverage assessment

**CI command pattern (dotnet run, not dotnet test):** Covered in the Commands section — explicit note with a link to the MTP error page. No gap.

**Coverage is Jenkins-side, not GH Actions:** The CI section mentions both files but says nothing about this split. The `ci.yml` SUMMARY decision "No coverage, no artifacts, no TRX: D1 — Jenkins-side" is not surfaced in CLAUDE.md. Future agents touching the workflow could add coverage steps, not knowing the split is deliberate.

**Secret-scan tripwire and fixture:** The CI section lists `secret-scan.yml` but says nothing about the tripwire fixture or the `selftest` job. The CLAUDE.md architecture invariant ("CI greps for…") describes the *intent* but not the *mechanism* (script + fixture + selftest job). An agent editing the fixture without knowing its self-test role could silently break detection.

**`--coverage-output <path>` honored in SDK Docker image:** Not in CLAUDE.md. This is a verified, non-obvious finding (SUMMARY-3.1) relevant to any Phase 3+ work on test infrastructure. Currently lives only in the SUMMARY file.

**`[tests/**.cs]` editorconfig suppressions:** Covered in Conventions. No gap.

**SUMMARY files sufficiency:** All four SUMMARYs (1.1, 1.2, 2.1, 3.1) record decisions, verification steps, and issues in enough detail to reconstruct rationale. No material gaps.

## Gaps

### HIGH — must address this phase

None. Existing CLAUDE.md CI section is functional for current Phase 2 scope.

### MEDIUM — should address before Phase 11

**Gap 1 — Coverage split not documented.**
CLAUDE.md CI section should note that coverage collection and reporting are Jenkins-only (Jenkinsfile). The GH Actions `ci.yml` is a fast gate only — no coverage flags, no TRX artifacts. Without this, a future agent will add coverage to `ci.yml` as a reasonable improvement.
Suggested addition: one sentence in the CI bullet for `build.yml` / `ci.yml`.

**Gap 2 — Secret-scan tripwire mechanism not documented.**
CLAUDE.md should note that `.github/secret-scan-fixture.txt` is a deliberate tripwire and that `secret-scan.yml` runs a `tripwire-self-test` job against it. Modifying or deleting that file silently disables the self-test. A single sentence in the CI section or Security area is sufficient.

### LOW — note only

**Gap 3 — `--coverage-output <path>` SDK behavior.**
SUMMARY-3.1 verified that `mcr.microsoft.com/dotnet/sdk:10.0` honors the explicit `--coverage-output` path flag. This is non-obvious and may matter in Phase 3+ test infrastructure work. Worth a one-line note in the Testing section of CLAUDE.md once test infrastructure is built out; not urgent now.

## Recommendations

1. Add to the `ci.yml` CI bullet in CLAUDE.md: "fast build/test gate only — no coverage; coverage is Jenkinsfile-only."
2. Add to the CI section: "`.github/secret-scan-fixture.txt` is a tripwire; the `tripwire-self-test` job validates every pattern fires against it — do not edit or delete without updating the self-test."
3. Defer Gap 3 to Phase 3 or later when test infrastructure documentation is written.
