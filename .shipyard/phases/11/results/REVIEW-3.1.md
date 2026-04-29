# REVIEW-3.1.md — Phase 11 Wave 3 PLAN-3.1

**Reviewer:** shipyard:reviewer
**Date:** 2026-04-28
**Branch:** Initcheckin
**Commits reviewed:** 4e46161, ad46f96, 692620d, d09d304

---

## Pre-Check: Prior Findings

No REVIEW-*.md files from earlier waves in this phase that are outstanding. No
`.shipyard/ISSUES.md` found. No recurring patterns to escalate.

---

## Stage 1: Spec Compliance

**Verdict:** PASS

### Task 1: Samples project + sln wiring

- Status: PASS
- Evidence:
  - All seven files exist as specified:
    - `samples/FrigateRelay.Samples.PluginGuide/FrigateRelay.Samples.PluginGuide.csproj`
    - `samples/FrigateRelay.Samples.PluginGuide/Program.cs`
    - `samples/FrigateRelay.Samples.PluginGuide/SampleActionPlugin.cs`
    - `samples/FrigateRelay.Samples.PluginGuide/SampleValidationPlugin.cs`
    - `samples/FrigateRelay.Samples.PluginGuide/SampleSnapshotProvider.cs`
    - `samples/FrigateRelay.Samples.PluginGuide/SamplePluginRegistrar.cs`
    - (Note: `SamplePluginOptions.cs` is referenced in `SampleActionPlugin.cs` and
      `SamplePluginRegistrar.cs` but is NOT listed as a file in the plan's `files_touched`
      — the builder added it as an unlisted auxiliary file, which is a reasonable
      implementation detail, not a deviation.)
  - Sln wiring confirmed: `FrigateRelay.sln` line 42–45 contains a `"samples"` solution
    folder and `FrigateRelay.Samples.PluginGuide` project entry.
  - `csproj`: `OutputType=Exe`, `net10.0`, `IsPackable=false`,
    `GenerateDocumentationFile=true`, references `FrigateRelay.Abstractions` via
    ProjectReference. Matches spec template exactly.
  - All four contracts represented:
    - `IActionPlugin` — `SampleActionPlugin.cs` implements `IActionPlugin`,
      `ExecuteAsync(EventContext ctx, SnapshotContext snapshot, CancellationToken ct)` —
      3-param ARCH-D2 shape confirmed (lines 39–60).
    - `IValidationPlugin` — `SampleValidationPlugin.cs` implements `IValidationPlugin`,
      `ValidateAsync(EventContext, SnapshotContext, CancellationToken)` confirmed.
    - `ISnapshotProvider` — `SampleSnapshotProvider.cs` implements `ISnapshotProvider`,
      `FetchAsync(SnapshotRequest, CancellationToken)` confirmed.
    - `IPluginRegistrar` — `SamplePluginRegistrar.cs` implements `IPluginRegistrar`,
      `Register(PluginRegistrationContext)` confirmed.
  - `LoggerMessage` source-gen: all three plugin files and `Program.cs` use `[LoggerMessage]`
    delegates — CA1848 compliant.
  - No hard-coded IPs/hostnames in any sample file. No secrets.
  - `Program.cs`: `Main` wires DI, exercises `IActionPlugin`, `IValidationPlugin`
    (both pass + fail cases), and `ISnapshotProvider`; returns `0` on success.
- Notes: The `IDE0130` suppression for namespace mismatch in the csproj is appropriate
  for a flat samples project.

### Task 2: docs/plugin-author-guide.md

- Status: PASS
- Evidence:
  - File exists at `docs/plugin-author-guide.md`.
  - Single H1 (`# FrigateRelay Plugin Author Guide`) at line 1.
  - Tutorial-first structure with 11 ordered sections matching plan spec exactly.
  - All four contract walkthroughs present (sections 3, 4, 5, 6).
  - Section 11 ("Putting it together") includes `Program.cs` fence — total of 5
    annotated `csharp filename=` fences (>= 4 required by spec).
  - `dotnet new frigaterelay-plugin` scaffold command present (section 2, line 46).
  - `AssemblyLoadContext` mentioned in section 10 (forward-compat note).
  - `FrigateRelay.TestHelpers` referenced in section 9.
  - Profiles + Subscriptions config shape covered in section 7, including both
    string-array shorthand and object form (ID-12 closure), and secret-field pattern.
  - No "File Transfer Server" references (confirmed by absence).
  - No hard-coded IPs or `AppToken=` secrets.
  - Code blocks in the guide are verbatim copies of sample files — the doc-rot script
    reported exit 0 for all 5 fences (builder-reported; the script logic is correct).
- Notes: All internal markdown links in the guide point to sections within the same
  document using anchor syntax. CLAUDE.md, README.md, CONTRIBUTING.md, and ROADMAP.md
  are referenced by name/path in prose but not via markdown link syntax — they are
  mentioned as cross-references, not broken links.

### Task 3: check-doc-samples.sh + docs.yml third job

- Status: PASS
- Evidence:
  - `.github/scripts/check-doc-samples.sh` exists with `#!/usr/bin/env bash`,
    `set -euo pipefail`, Python3 heredoc extractor. Guards for missing doc and
    samples dir. Regex `r"^```csharp\s+filename=(\S+)[^\n]*\n(.*?)^```"` with
    `MULTILINE | DOTALL` correctly extracts fenced blocks. Byte-compares extracted
    text against sample file content. Prints unified diff on mismatch. Exits with
    failure count.
  - Script is correct: the plan's suggested implementation used `re.M | re.S`
    inline; the builder correctly expanded these to `re.MULTILINE | re.DOTALL`
    for readability, and separated imports — functionally identical.
  - `docs.yml`: contains three jobs — `scaffold-smoke`, `samples-build`, and
    `doc-samples-rot` (line 128). The third job was appended without modifying the
    first two jobs.
  - `doc-samples-rot` job: `ubuntu-latest`, `timeout-minutes: 5`, `actions/checkout@v4`,
    `actions/setup-python@v5` with `python-version: '3.x'`, runs
    `bash .github/scripts/check-doc-samples.sh`. Matches spec exactly.
  - Addition of `if: hashFiles('docs/plugin-author-guide.md') != ''` guard — this is
    a minor enhancement over the spec (which had no conditional), making the job
    a graceful no-op before the doc lands. Acceptable deviation (additive only).
  - `samples-build` job now correctly activates because
    `samples/FrigateRelay.Samples.PluginGuide` directory exists — the wave-2 forward
    reference conditional skip is now live.
  - YAML validity: file reads cleanly with proper indentation; no structural issues
    observed.
- Notes: Executable bit state cannot be confirmed via file read, but the builder
  reported `test -x .github/scripts/check-doc-samples.sh` passed, and the script
  header (`#!/usr/bin/env bash`) is present.

---

## Stage 2: Code Quality

### Critical

None.

### Important

None.

### Suggestions

- **`check-doc-samples.sh` — script must be run from repo root or paths will break**
  (`samples/FrigateRelay.Samples.PluginGuide/Program.cs` line: N/A; script lines 17–18).
  The `DOC` and `SAMPLES_DIR` default to relative paths. If called from a subdirectory
  the script silently triggers the "file not found" guard and exits 1. The `docs.yml`
  job always runs from the checkout root so this is a CI non-issue, but a developer
  calling it from `docs/` or `.github/scripts/` would get a confusing error. Consider
  adding a note to the script header or computing `SCRIPT_DIR` and defaulting paths
  relative to it. Not a blocker — the CI path is the contract.

- **`SamplePluginRegistrar.cs` — unlisted file `SamplePluginOptions.cs`** not in
  plan's `files_touched` list. This is a documentation gap in the plan, not a code
  defect. The class is required for `IOptions<SamplePluginOptions>` injection in
  `SampleActionPlugin`; without it the project would not compile. The plan spec says
  "all files compile clean" so the addition is implicit. Worth noting if the plan
  files_touched list is used to drive automated coverage checks.

- **`docs.yml` `doc-samples-rot` job — `if:` guard references `plugin-author-guide.md`
  but not `samples/**`** (docs.yml line 133). If someone updates a sample file
  without touching the doc, the rot-check job will still fire (because `docs/**` or
  `samples/**` path filters cover that case at the workflow level), but the job-level
  `if:` guard is solely checking for doc existence. This is fine — doc existence is the
  correct gate. Just noting it's asymmetric (the `scaffold-smoke` guard checks
  template existence; this guard checks doc existence; neither checks both inputs).
  No action required.

---

## Summary

**Verdict:** APPROVE

All three tasks implemented as specified: 7 sample files with correct 3-param contract
signatures and LoggerMessage source-gen, a tutorial-first guide with 5 verbatim-copied
annotated fences covering all 4 contracts, a byte-match doc-rot script that exits 0,
and a properly appended third job in docs.yml.

Critical: 0 | Important: 0 | Suggestions: 3
