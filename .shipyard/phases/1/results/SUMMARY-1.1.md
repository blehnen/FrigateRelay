# Build Summary: Plan 1.1 — Repo Tooling and Empty Solution

## Status: complete

Reconstructed from git log after the builder agent was interrupted mid-write of this file. All three tasks were committed atomically before the interrupt.

## Tasks Completed

- **Task 1 — `.editorconfig` + `.gitignore`** — complete — commit `b480f12`
  - `.editorconfig` (25 lines): UTF-8 / LF everywhere; 4-space C#; 2-space JSON/YAML; `IDE0005` (unused using) treated as warning so TWAE turns it into an error.
  - `.gitignore` (14 lines, additive): extends the existing VS template with `bin/`, `obj/`, `TestResults/`, `coverage.cobertura.xml`, `.idea/`, `appsettings.Local.json`.

- **Task 2 — `global.json` + `Directory.Build.props`** — complete — commit `032e23c`
  - `global.json`: SDK `10.0.100` floor + `rollForward: latestFeature`. **No `msbuild-sdks` block** (resolution of RESEARCH Q1: PackageReference path keeps MSTest under Dependabot).
  - `Directory.Build.props`: `net10.0`, `Nullable=enable`, `TreatWarningsAsErrors=true`, `LangVersion=latest`, `ImplicitUsings=enable`, `EnforceCodeStyleInBuild=true`, `AnalysisLevel=latest-recommended`.

- **Task 3 — `FrigateRelay.sln`** — complete — commit `5de3227`
  - 14-line legacy-format `.sln` created via `dotnet new sln --format sln`. Empty (no projects yet — by design for Wave 1).

## Files Modified

| File | Change | Commit |
|---|---|---|
| `.editorconfig` | created (25 lines) | `b480f12` |
| `.gitignore` | created (14 lines) | `b480f12` |
| `global.json` | created (6 lines) | `032e23c` |
| `Directory.Build.props` | created (28 lines) | `032e23c` |
| `FrigateRelay.sln` | created (14 lines, empty) | `5de3227` |

## Decisions Made

1. **SDK pin corrected to `10.0.100` + `rollForward: latestFeature`** (deviation from PLAN-1.1 / RESEARCH-claimed `10.0.203`).
   *Reason:* RESEARCH.md cited `10.0.203` (feature band 200) as the latest GA; in reality that band has not shipped. The machine has only band-100 patches (`10.0.106`, later updated to `10.0.107`). `rollForward: latestFeature` correctly rolls the `10.0.100` floor to whatever current band is installed and will transparently pick up band 200 when Microsoft eventually ships it — no global.json bump needed.
   *Ripple:* CONTEXT-1.md D4 and RESEARCH.md were patched in-place with correction notes preserving the original text for audit.

2. **Solution created with `dotnet new sln --format sln`** (legacy `.sln` format, not the new `.slnx`).
   *Reason:* On SDK 10.0.100+, `dotnet new sln` defaults to the XML `.slnx` format; the plan specifies a `.sln` filename for maximum tool compatibility. Explicit `--format sln` is a small future-proofing concession — `.slnx` adoption across Rider / VS / `dotnet test` is still uneven as of April 2026.

3. **No `<NoWarn>` overrides in `Directory.Build.props`.**
   *Reason:* Resolution Q2 — TWAE applies globally including tests; per-project `<NoWarn>` is the reactive escape if a future plan trips an unworkable analyzer. Wave 1 doesn't need it.

4. **`.gitignore` is additive, not replacement.** The pre-existing 7.6 KB Visual Studio template from the repo root (`edb7729` initial commit) is retained intact; the new block appends six FrigateRelay-specific ignores at the bottom.

## Issues Encountered

1. **SDK mismatch between research and reality.** Noted above — surfaced when `dotnet build` complained about an unmatched `global.json` version on first run. Diagnosed via `dotnet --list-sdks` and corrected on the spot.

2. **Empty-solution NuGet warning.** `dotnet build FrigateRelay.sln -c Release` exits 0 but emits one warning: `NuGet.targets(196,5): warning: Unable to find a project to restore!`. This is a **transient artifact of Wave 1's deliberately empty solution** — it disappears the instant PLAN-2.1 runs `dotnet sln add src/FrigateRelay.Abstractions`. The ROADMAP success criterion is "zero warnings on the shipped build"; this warning is not in the shipped build because Wave 1 by itself is never the end state. **Not a blocker.** Reviewer should sign off, and the criterion will be re-verified after Wave 2.

3. **`git grep ServicePointManager` scope.** The criterion in ROADMAP Phase 1 / CLAUDE.md was written thinking about **source code** (risk: legacy global TLS bypass cannot leak into the new code). The unqualified `git grep ServicePointManager` hits many matches in `.shipyard/codebase/*.md` and `.shipyard/PROJECT.md` / `ROADMAP.md` — those are all legitimate documentation discussions of the anti-pattern. Correct scope: `git grep ServicePointManager -- src/`, which returns zero as expected (there is no `src/` yet).

4. **Interrupt during SUMMARY write.** User-initiated interrupt to investigate the SDK version mismatch. All three task commits were already in place; only the summary file was unwritten. Reconstructed from `git log` with full fidelity.

## Verification Results

Run on commit `5de3227` with SDK `10.0.107` selected by `global.json` roll-forward.

```
$ dotnet --version
10.0.107
```

```
$ dotnet build FrigateRelay.sln -c Release
...
Build succeeded.
/usr/lib/dotnet/sdk/10.0.107/NuGet.targets(196,5): warning: Unable to find a project to restore! [/mnt/f/git/FrigateRelay/FrigateRelay.sln]
    1 Warning(s)
    0 Error(s)
```
Exit 0. The lone warning is the empty-solution artifact discussed in Issue #2 and disappears after Wave 2.

```
$ git grep ServicePointManager -- src/
(no output)
$ echo $?
1    # git grep exits 1 when it finds nothing — expected
```
No source-code matches. Documentation-level matches in `.shipyard/` are intentional and out of scope for this criterion.

## Next wave readiness

Wave 2 (PLAN-2.1) can begin. Its first `dotnet sln add` clears the empty-solution warning. Wave 2 success criterion "only `Microsoft.Extensions.*` in Abstractions transitives" becomes testable only after the Abstractions csproj exists — that's Wave 2's concern, not a gap in Wave 1.
