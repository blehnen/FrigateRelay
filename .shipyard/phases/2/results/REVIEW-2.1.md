# Review: Plan 2.1 (Phase 2)

## Verdict: PASS

Target commit: `13e3ea2 shipyard(phase-2): add GitHub Actions ci.yml`. Reviewer finding: APPROVE, zero critical, one advisory.

## Findings

### Critical
None.

### Minor (advisory / non-blocking)
- **Static test-project list** — `ci.yml` hard-codes the two test-project paths (`tests/FrigateRelay.Abstractions.Tests`, `tests/FrigateRelay.Host.Tests`). Every new test project added in Phase 3+ will require a ci.yml edit. Acceptable for Phase 2 (we only have two). **Carry this to the Phase 3 architect** — they can decide whether to switch to a `find tests/*.Tests -type d -print0 | xargs -0 ...` loop before the count grows.
- **`on: push` fires on all branches / tags.** No branch filter. Matches plan spec; DNWQ does the same. Could be hardened later if scratch-branch noise becomes an issue.
- **Cosmetic: workflow `name: CI` vs plan's lowercase `name: ci`.** No functional impact — concurrency group uses lowercase regardless. Ignore unless style-matching matters.

### Positive
- All three hardening additions (`permissions: contents: read`, `concurrency` with `cancel-in-progress: true`, `timeout-minutes: 15`) present.
- `shell: bash` on test steps — Windows Git Bash consistency per Phase 1 lesson.
- Header comment documents D1/D2 split so a future reader understands the "why no coverage?" and "why not `dotnet test`?" decisions without needing to spelunk CONTEXT-2.md.
- `actions/setup-dotnet@v4` with `global-json-file: global.json` — no `dotnet-version:` override, no implicit roll-forward fallback, fails loud if the pinned SDK is unreachable.
- No `dotnet test`, no coverage flags, no artifact upload. Scope discipline honored.

## Check results

| Command | Result |
|---|---|
| `yq eval . .github/workflows/ci.yml` | parses |
| `yq eval '.on \| keys' ...` | `[push, pull_request]` |
| `yq eval '.jobs.build.strategy.matrix.os' ...` | `[ubuntu-latest, windows-latest]` |
| `! grep -E 'dotnet test\b' ...` | empty (0 matches) |
| `! grep -Ei 'xplat\|collect\|--coverage' ...` | empty (0 matches) |
| `grep -c 'dotnet run --project' ...` | 2 |
| `dotnet build FrigateRelay.sln -c Release` | 0 warnings, 0 errors |
| `dotnet run --project tests/FrigateRelay.Abstractions.Tests -c Release --no-build` | 10 pass, 0 fail |
| `dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build` | 7 pass, 0 fail |

## Forward note for Phase 3

When Phase 3 adds `tests/FrigateRelay.Sources.FrigateMqtt.Tests`, the architect should either (a) append a third `dotnet run --project` step to `ci.yml` as part of Phase 3's plan, or (b) switch the workflow to iterate `tests/*.Tests` dynamically. Decide during Phase 3 planning, not on the fly.
