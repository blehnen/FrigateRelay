# Phase 2 — Context & Decisions

Captured during `/shipyard:plan 2` discussion capture. Phase 2 scope from ROADMAP.md is CI skeleton + committed-secrets tripwire. Phase 1 lessons (Jul-truncated test invocation, .NET 10 CLI surface changes) shape these decisions.

## Scope reminder (from ROADMAP.md Phase 2)

- `.github/workflows/build.yml` — triggers on `push` / `pull_request`; matrix on `windows-latest` + `ubuntu-latest`; runs restore/build/test/coverage/artifact. Patterned on `F:\Git\DotNetWorkQueue\.github\workflows`.
- `.github/workflows/secret-scan.yml` — fails build on committed secret patterns (`AppToken=…`, `UserKey=…`, api-key-shaped strings, `192.168.x.x`).
- `.github/dependabot.yml` — nuget + github-actions ecosystems, weekly.

Success criteria (ROADMAP lines 87–89):
- First PR into the repo shows both workflows green.
- Deliberately-committed fake-token test string causes `secret-scan.yml` to fail (verified once, then reverted).
- Coverage artifact (`coverage.cobertura.xml`) downloadable from the Actions run.

## Decisions

### D1 — **Mirror DotNetWorkQueue CI topology: GH Actions for PR gate, Jenkinsfile for coverage**

DNWQ's actual split, observed on disk at `F:\Git\DotNetWorkQueue\`:

- `ci.yml` (68 lines) — Debug build + `dotnet test` per test project. **No coverage**, no TRX logger. Fast PR feedback.
- `publish.yml` (243 lines) — release publishing (deferred to our Phase 10).
- `Jenkinsfile` (385 lines) — coverage: `dotnet test --settings coverage.runsettings --collect:"XPlat Code Coverage" --results-directory coverage/unit-X` per project. Runs on the author's self-hosted Jenkins; not per-PR.

FrigateRelay adopts the same split:

- **GH `ci.yml`** (Phase 2) — build + test (no coverage). Fast PR gate.
- **GH `secret-scan.yml`** (Phase 2) — secret-pattern tripwire, separate workflow so one job failing the scan doesn't mask the build signal.
- **GH `dependabot.yml`** (Phase 2) — nuget + github-actions, weekly.
- **Jenkinsfile** (Phase 2 — added deliverable) — coverage runs, cobertura XML artifacts per project.

**Rejected:**
- *GitHub-only with coverage* — forces GH to carry the slower coverage pass; fine for a small repo today but diverges from the author's established muscle memory and splits attention between two CI strategies.
- *GitHub-only without coverage in v1* — defers a real deliverable; coverage is cheap to set up now while CI is being built from scratch.

### D2 — **CI test invocation: `dotnet run --project tests/<project> -c Release` per test project**

Phase 1 lesson: .NET 10 SDK explicitly blocks `dotnet test` against the Microsoft Testing Platform (`https://aka.ms/dotnet-test-mtp-error`). Our test projects already have `OutputType=Exe` and run under MTP, so `dotnet run` is the canonical invocation.

- **GH `ci.yml`**: loop both test projects with `dotnet run --project ... -c Release` (no `--coverage` flags). Build in Release (one compile, avoids Debug rebuild on the test step).
- **Jenkinsfile**: same invocation plus the MTP code-coverage extension — `dotnet run --project tests/<project> -c Release -- --coverage --coverage-output-format cobertura --coverage-output <dir>/<project>.cobertura.xml`. Exact flags pinned by researcher against MSTest 4.2.1's bundled `Microsoft.Testing.Extensions.CodeCoverage`.

**Rejected:**
- *Legacy `dotnet test --collect:"XPlat Code Coverage"` via the VSTest adapter* — still bundled in MSTest 4.2.1 but deprecated for MTP on .NET 10 SDK. Adds tech debt we'd immediately have to pay down.
- *The "new dotnet test experience" env-var opt-in* — too immature on .NET 10 SDK today; would force the Phase 2 workflow to track an SDK preview feature.

### D3 — **Defer graceful-shutdown smoke to Phase 4 integration tests**

Phase 4 stands up Testcontainers Mosquitto and exercises the host end-to-end. That's the proper place for host-lifecycle smoke testing — a broker is running, the host really receives events, SIGINT during a real workload proves the shutdown path.

Phase 2 CI covers: build, unit tests, secret-scan. **Not** a manual shutdown smoke.

**Rejected:**
- *Linux-only `pgrep | kill -INT` step in `ci.yml`* — ports the Phase 1 recipe into CI, but PowerShell parity on Windows is painful (Windows has no real SIGINT), and the signal-under-`dotnet-run` wrapper issue documented in Phase 1 SUMMARY-3.1 means the recipe is WSL-specific. Too fragile for a gating workflow.

### D4 — **Secret-scan tripwire: self-test job against a committed fixture**

`secret-scan.yml` has two jobs:

1. **`scan`** — greps the tree (excluding `.shipyard/` docs and the tripwire fixture itself) for secret-shaped patterns; fails the build on any hit.
2. **`tripwire-self-test`** — greps **only** the fixture file `.github/secret-scan-fixture.txt` (committed intentionally; has fake-shaped strings like `AppToken=abcdefghijklmnopqrstuvwxyz01234` and `192.168.99.99`). If the regex does NOT match the fixture, the scanner is broken and this job fails. Runs on every push, no manual dance.

The fixture is excluded from the main `scan` job via a path filter.

**Rejected:**
- *Manual one-time verification on a scratch branch* — ROADMAP's original plan; one-time only, no ongoing proof. Regex drift over time would go unnoticed.
- *Both (self-test + manual)* — adds a one-shot manual step without buying more safety than the self-test already gives.

### D5 — **Dependabot: `nuget` + `github-actions` ecosystems, weekly**

Per ROADMAP. **Not** `docker` — Phase 10 adds the Dockerfile; Dependabot for docker images can be added in the same phase.

## Implementation notes (for the architect)

- **Matrix for `ci.yml`**: `[ubuntu-latest, windows-latest]`. Both use `actions/setup-dotnet@v4` with `global-json-file: global.json` to pick up the `10.0.100 + rollForward: latestFeature` pin. Build once per matrix leg (`dotnet build -c Release`), then loop test projects with `dotnet run --project ... --no-build -c Release`.
- **Jenkinsfile language**: declarative or scripted — mirror the DNWQ style (script). Keep stage count small: `Checkout` → `Restore` → `Build -c Release` → `Coverage run per project` → `Archive cobertura artifacts`. No publish, no deploy (Phase 10).
- **coverage.runsettings**: only if the MTP extension needs it. Default MTP code-coverage extension is configured via command-line flags, not runsettings. Researcher confirms.
- **Secret-scan regex**: same patterns listed in ROADMAP Phase 2. The regex excludes `.shipyard/` directories (legitimate discussion of patterns in planning docs) and the fixture file.
- **`ci.yml` upload-artifact step**: phase 2 produces **no** coverage artifact (coverage is Jenkins-side). Upload test logs (`trx` or MTP output) only if troubleshooting becomes a frequent need; otherwise skip.
- **Fast PR feedback target**: `ci.yml` should complete in under 3 minutes on a cold cache — build + two `dotnet run` test invocations for a project this small is well under that.

## Non-goals for Phase 2 (explicit)

- No release workflow / publish.yml. (Phase 10.)
- No Docker build. (Phase 10.)
- No SBOM, license-check, or supply-chain workflows. (Not planned for v1.)
- No integration tests / Testcontainers. (Phase 4.)
- No per-PR coverage thresholds, no Codecov / Coveralls integration. (Deferred; Jenkins produces cobertura — uploading elsewhere is a later decision.)
- No Dockerfile / GHCR release (Phase 10).
