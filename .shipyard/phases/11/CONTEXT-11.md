# Phase 11 — Discussion Capture (CONTEXT-11.md)

**Phase:** 11 — Open-Source Polish
**Date:** 2026-04-28
**Method:** `/shipyard:plan 11` Step 2b discussion capture (8 questions across 2 AskUserQuestion rounds)

This file captures user-locked decisions. Researcher and architect MUST treat these as constraints, not suggestions.

---

## Phase scope (from ROADMAP)

- README · CONTRIBUTING · LICENSE · GitHub issue/PR templates
- `templates/FrigateRelay.Plugins.Template/` plugin scaffold
- `docs/plugin-author-guide.md` + `samples/FrigateRelay.Samples.PluginGuide/`
- Scaffold-smoke CI integration

**Adjacent items absorbed into Phase 11 (per D3):** Phase 9 integration test triage, CLAUDE.md staleness fixes, retroactive CHANGELOG.md, SECURITY.md.

---

## Decisions

### D1 — LICENSE

**Decision:** MIT license. Copyright holder = `Brian Lehnen`. Year = `2026`.

**Notes:** Standard SPDX MIT boilerplate. Compatible with the FluentAssertions 6.12.2 pin and all current deps (CLAUDE.md "MIT-compatible deps only" constraint is satisfied). LICENSE file lives at repo root.

---

### D2 — Plugin scaffold mechanism

**Decision:** **`dotnet new` template** via `templates/FrigateRelay.Plugins.Template/.template.config/template.json`. NOT copy-and-rename.

**Notes:**
- ROADMAP Phase 11 wording mentioned `sed`-rename in CI; the user explicitly chose the more polished `dotnet new` path. The `sed` approach is dropped.
- Template short-name = `frigaterelay-plugin` (D5).
- Template engine identifier (full path/id) is architect-discretion — short-name is the user-typed key, the long identifier follows package conventions (e.g. `FrigateRelay.Plugins.Template`).
- Smoke step uses: `dotnet new install <path-to-template-dir>` → `dotnet new frigaterelay-plugin -n FrigateRelay.Plugins.SmokeScaffold` → `dotnet build` → `dotnet run --project <test-project> -c Release`.
- Template MUST produce a buildable plugin with one passing unit test out of the box, mirroring the BlueIris/Pushover csproj shape (architect to confirm).

---

### D3 — Phase 11 scope expansion (adjacent items)

**Decision:** Absorb **ALL FOUR** adjacent items into Phase 11:

1. **Phase 9 integration test triage** — fix `Validator_ShortCircuits_OnlyAttachedAction` and `TraceSpans_CoverFullPipeline` (D7 specifies the wave structure).
2. **CLAUDE.md staleness fixes** — DOCUMENTATION-10 flagged 2 stale lines: (a) Jenkinsfile description still says "tag-pinned — digest pin + Dependabot `docker` ecosystem deferred to Phase 10" (Phase 10 fix-ups `1c3eaaa` + `3b87641` resolved both); (b) "Project state" section still says `currently pre-implementation` / `Nothing but planning docs exists in-tree yet` — wrong post-Phase-10.
3. **CHANGELOG.md (retroactive)** — covering Phase 1 through Phase 10. Source material: `.shipyard/HISTORY.md` per-phase entries + git commit log. Format = Keep-a-Changelog (architect to confirm).
4. **SECURITY.md** — vulnerability disclosure policy. Endpoint per D6.

**Notes:** This expands Phase 11 beyond the literal ROADMAP scope by 4 deliverables. The architect should reflect this in plan-count. Per the ROADMAP risk rating of "Low (documentation and templates)" and the 3–4 hour estimate, the absorbed items add roughly +1–2 hours plus the integration-test triage (unknown depth, see D7).

---

### D4 — CI integration

**Decision:** New workflow file at `.github/workflows/docs.yml`. NOT extending the existing `ci.yml`.

**Notes:**
- `docs.yml` hosts: scaffold-smoke job, samples-build job, plus any other doc-rot detection added in this phase.
- `ci.yml` remains the fast PR gate per Phase 2 D1 (split CI architecture).
- Triggers for `docs.yml` should mirror `ci.yml` (push to main + PRs touching the relevant paths) — architect-discretion on path filters.
- `concurrency:` group should match `ci.yml`'s pattern (cancel obsolete on force-push).

---

### D5 — `dotnet new` template short-name

**Decision:** `frigaterelay-plugin`

**Notes:** Used in docs and CI as `dotnet new frigaterelay-plugin -n MyPlugin`. Long identifier (full template ID) is architect-discretion.

---

### D6 — Vulnerability reporting endpoint

**Decision:** GitHub private vulnerability reporting (`Security` tab → `Report a vulnerability`).

**Notes:**
- SECURITY.md MUST link to the GitHub private-advisory flow.
- SECURITY.md MUST also note that the repo Settings → Code security flag for "Private vulnerability reporting" needs to be enabled by the maintainer (this is a one-time admin step that this phase cannot perform automatically — architect should mention it as a manual checklist item, not as a code task).
- No personal email exposed. No mailto link.

---

### D7 — Phase 9 integration test triage strategy

**Decision:** **Investigate + fix in Wave 1; gate doc work on green tests.** Budget 1 retry. Escape-hatch: if the bug is not fixable inside the wave's tool budget, mark the tests as known-issue (e.g., `[Ignore("Phase-9 regression — see ID-N")]` plus a fresh ISSUES.md entry) and let doc waves proceed.

**Notes:**
- The 2 failing tests touch OTel observability code (span parenting, log capture wiring) — Phase 9's PLAN-3.1 area. Researcher to root-cause.
- The architect's Wave 1 should contain at minimum a single triage plan; if root cause is shallow, plan can include the fix in the same wave; if root cause is deep, the plan can split into investigate-only (Wave 1) + fix (Wave 2).
- Doc waves (README/CONTRIBUTING/scaffold/etc.) MUST start in Wave 2 or later, not parallel to triage in Wave 1.
- Acceptance criterion for the gate: `dotnet build FrigateRelay.sln -c Release` + all test projects via `dotnet run --project tests/<project> -c Release --no-build` produces 194/194 passing (or 192/192 + 2 explicitly-marked-known-issue with tracking IDs).

---

### D8 — Samples project wiring

**Decision:** `samples/FrigateRelay.Samples.PluginGuide/` is **in `FrigateRelay.sln`** (so `dotnet build FrigateRelay.sln -c Release` builds it under warnings-as-errors), but **CI build/test runs only in `docs.yml`**, NOT in `ci.yml`.

**Notes:**
- IDE contributors get full intellisense/refactoring on the samples project (it's part of the solution graph).
- The ci.yml fast-PR-gate stays focused on production code.
- The docs.yml workflow re-builds and tests samples on every push that touches `docs/**`, `samples/**`, `templates/**`, or the workflow file itself.
- Samples MUST follow the same `Directory.Build.props` warnings-as-errors discipline as production code; the file is repo-wide so this is automatic.
- Per ROADMAP success criterion: doc samples are copied verbatim from `docs/plugin-author-guide.md` into the samples project so stale docs cannot ship — architect should specify the copy mechanism (manual sync vs scripted extraction).

---

## Out-of-scope explicitly noted

- **Phase 12 docs sprint items (deferred):** A full `docs/architecture.md`, `docs/configuration-reference.md`, `docs/operations-guide.md`. Phase 11 covers contributor-facing documentation (plugin-author-guide, CONTRIBUTING, scaffold). Operator-facing reference docs remain Phase 12 scope per DOCUMENTATION-10's deferral list.
- **Code of Conduct (`CODE_OF_CONDUCT.md`):** Not requested by user; defer unless architect sees a strong open-source-norms reason to include.
- **GitHub Actions SHA-pinning (ID-24):** Tracked separately; not absorbed into Phase 11.
- **Compose port-binding hardening note (ID-26):** Tracked separately.
- **README badges, screenshots, demo GIF:** Out of scope.
- **Migration of `dotnet new` template to a published `.nupkg`:** Deferred (track as new ID-N if architect deems worth tracking).

---

## Open architect-discretion items (NOT user decisions — architect chooses)

- Long template identifier (full namespace path for the dotnet-new template ID).
- CHANGELOG.md format (Keep-a-Changelog vs custom).
- `docs/plugin-author-guide.md` structure (tutorial-first vs reference-first).
- Whether the plugin scaffold seeds an `IActionPlugin`, `IValidationPlugin`, `ISnapshotProvider`, or `IPluginRegistrar`-only. Researcher should investigate the existing BlueIris/Pushover csproj layouts as the canonical reference; architect picks the minimal scaffold scope.
- Doc-sample copy mechanism (manual sync vs CI script that copies and diffs).
- Wave layout: D7 mandates Wave 1 = test triage; everything else is architect-discretion.

---

## Calibration notes for the architect

- Total deliverables: 7 from ROADMAP + 4 from D3 expansion + 2 from D7 (test triage) = ~13 atomic deliverables. With the ≤3-tasks-per-plan rule, expect ~5–7 plans across 2–3 waves.
- File-disjoint rule for parallel waves: the doc-writing plans (README, CONTRIBUTING, LICENSE, SECURITY, CHANGELOG, plugin-author-guide) touch disjoint files and can run in parallel within one wave. Issue templates and `.github/workflows/docs.yml` similarly disjoint.
- Risk: integration test triage (D7) is the only unknown-depth item. Plan accordingly.
- All Phase 9/10 conventions hold: `shipyard(phase-11):` commit prefix, builders use the established CapturingLogger / `<InternalsVisibleTo>` patterns from CLAUDE.md.
