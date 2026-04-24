# Research: Phase 2 CI Skeleton

_Date: 2026-04-24. Researcher: Claude Sonnet 4.6._

---

## MTP Code Coverage CLI

### How `Microsoft.Testing.Extensions.CodeCoverage` is enabled in this repo

The two test projects use `Sdk="Microsoft.NET.Sdk"` with `<PackageReference Include="MSTest" Version="4.2.1" />`, `<EnableMSTestRunner>true</EnableMSTestRunner>`, and `<OutputType>Exe</OutputType>`. The `MSTest` 4.2.1 meta-package includes `Microsoft.Testing.Platform.MSBuild` transitively. Per official docs (updated 2026-02-25):

> "When using Microsoft.Testing.Platform.MSBuild (included transitively by MSTest, NUnit, and xUnit runners), code coverage extensions are auto-registered when you install their NuGet packages — no code changes needed."

The `Default` extensions profile (applied automatically when neither `TestingExtensionsProfile` nor `EnableMicrosoftTestingExtensionsCodeCoverage=false` is set) includes Code Coverage. **No additional NuGet package reference, no `.runsettings` file, and no MSBuild property is required** to enable coverage in these projects.

**Verification:** `EnableMicrosoftTestingExtensionsCodeCoverage` is not set to `false` in either csproj. Coverage is on by default.

### Exact CLI invocation

The `--` separator is required to pass arguments to the test executable rather than to `dotnet run` itself.

```bash
dotnet run --project tests/FrigateRelay.Host.Tests -c Release --no-build -- \
  --coverage \
  --coverage-output-format cobertura \
  --coverage-output coverage/host-tests/FrigateRelay.Host.Tests.cobertura.xml
```

```bash
dotnet run --project tests/FrigateRelay.Abstractions.Tests -c Release --no-build -- \
  --coverage \
  --coverage-output-format cobertura \
  --coverage-output coverage/abstractions-tests/FrigateRelay.Abstractions.Tests.cobertura.xml
```

### Flag reference (from official docs, learn.microsoft.com, updated 2026-02-25)

| Flag | Description |
|------|-------------|
| `--coverage` | Enable collection via dotnet-coverage tool. Required. |
| `--coverage-output-format` | `coverage` (default), `xml`, or `cobertura`. Use `cobertura` for Jenkins Cobertura plugin consumption. Branch coverage is only present in cobertura format. |
| `--coverage-output` | Path/filename. Default is `TestResults/<guid>.coverage`. Set explicitly for predictable Jenkins archiving. |
| `--coverage-settings` | Path to an XML settings file. **Not required** — CLI flags are sufficient for our use case. |

### Output file layout for Jenkins workspace

```
${WORKSPACE}/
  coverage/
    abstractions-tests/
      FrigateRelay.Abstractions.Tests.cobertura.xml
    host-tests/
      FrigateRelay.Host.Tests.cobertura.xml
```

The Jenkins `recordCoverage` step pattern `coverage/**/*.cobertura.xml` will match both files.

### Important note on `IncludeTestAssembly`

The default value of `IncludeTestAssembly` in `Microsoft.Testing.Extensions.CodeCoverage` is `false` (changed from VSTest's `true`). This means the test projects themselves are excluded from coverage numbers by default — only the referenced `src/` assemblies are measured. This is the correct behavior for our purposes.

### MSBuild opt-in check

Both test csproj files do not set `EnableMicrosoftTestingExtensionsCodeCoverage=false`, so coverage is active. No changes needed to either csproj for the Jenkinsfile coverage run.

---

## `actions/setup-dotnet@v4` + `global-json-file` Cookbook

### How `rollForward: latestFeature` is handled

PR #224 (merged 2021-09-13) implemented rollForward support. With `rollForward: latestFeature`, the action extracts only the major and minor version from the pinned value and resolves to the **latest available patch** for that feature band. Our `global.json` pins `10.0.100` with `rollForward: latestFeature`, so the action installs the latest available `10.0.x` SDK on the runner — which is exactly what we want (tracks `10.0.107` as of 2026-04-24 without requiring a `global.json` update on each SDK release).

**Caveat:** If the `10.0` feature band is not available at all on the runner, the action will fail. This is unlikely for `ubuntu-latest` and `windows-latest` which ship current .NET SDKs, but it is an open question for SDK releases that lag runner image updates (see Open Questions).

### YAML snippet (ready to use)

```yaml
- name: Setup .NET
  uses: actions/setup-dotnet@v4
  with:
    global-json-file: global.json
```

The `global-json-file` path is relative to the repository root (i.e., the working directory after `actions/checkout`). If the file does not exist, the action fails with an error — which is what we want (catches accidental deletion of `global.json`).

### Per-OS notes

**ubuntu-latest:** No known pitfalls. The action installs to `~/.dotnet`. `PATH` is updated automatically.

**windows-latest:** Default install is `C:\Program Files\dotnet`. One documented pitfall: if generating a temporary `global.json` inside the workflow on Windows, use `pwsh` or `bash` shell to avoid BOM/CRLF formatting issues that can cause SDK resolution to fail. We do not generate `global.json` dynamically, so this does not apply.

**Both runners:** Do not specify `dotnet-version` alongside `global-json-file`. Specifying both causes the action to install two SDK entries; the `global-json-file` value takes precedence for resolution but the extra SDK wastes install time. Use one or the other — for this repo, `global-json-file` only.

### DNWQ comparison

DNWQ's `ci.yml` uses `actions/setup-dotnet@v5` with explicit `dotnet-version: |` block listing two versions. FrigateRelay targets only `.NET 10` so `global-json-file` is cleaner and eliminates version duplication.

---

## Dependabot v2 Config Template

```yaml
# .github/dependabot.yml
#
# Dependabot version updates for FrigateRelay.
#
# Scope (D5 decision): nuget + github-actions ecosystems only.
# docker ecosystem is intentionally excluded until Phase 10 adds the Dockerfile.
#
# MSTest.Sdk version is managed via global.json msbuild-sdks key; Dependabot
# cannot currently update MSBuild SDK versions in global.json
# (dependabot-core#12824 / dependabot-core#8615). Update MSTest.Sdk manually
# when needed — see ROADMAP Phase 2 notes.
version: 2
updates:
  # ── NuGet ────────────────────────────────────────────────────────────────────
  # Updates PackageReference entries in all *.csproj files under the repo root.
  # Does NOT use centralized Directory.Packages.props (not in use for this repo).
  - package-ecosystem: "nuget"
    directory: "/"
    schedule:
      interval: "weekly"
      day: "monday"
    # Group all patch updates into one PR to reduce noise.
    groups:
      microsoft-extensions:
        patterns:
          - "Microsoft.Extensions.*"
      mstest:
        patterns:
          - "MSTest*"
          - "Microsoft.Testing.*"

  # ── GitHub Actions ───────────────────────────────────────────────────────────
  # Updates uses: lines in .github/workflows/*.yml files.
  # Dependabot searches /.github/workflows automatically when directory is "/".
  - package-ecosystem: "github-actions"
    directory: "/"
    schedule:
      interval: "weekly"
      day: "monday"
```

### Notes

- `directory: "/"` is correct for both ecosystems: NuGet searches for `*.csproj` / `packages.config` recursively from the repo root; GitHub Actions searches `/.github/workflows/` automatically.
- We do not use `Directory.Packages.props` or solution filters, so no additional `directories:` list is needed.
- The `MSTest.Sdk` limitation (cannot update `global.json` `msbuild-sdks` block) is a known Dependabot bug. Since our test projects use `Microsoft.NET.Sdk` (not `MSTest.Sdk`), this limitation does not apply to us.
- `groups:` is optional but reduces PR count for the `Microsoft.Extensions.*` family which releases in lockstep.

---

## Jenkinsfile Skeleton (Scripted)

Adapted from DNWQ's Jenkinsfile. Key differences: Docker image is `mcr.microsoft.com/dotnet/sdk:10.0`, test invocation is `dotnet run` (not `dotnet test`), coverage flags are MTP-style, and there is no parallel integration matrix (Phase 2 scope is unit tests only).

```groovy
// Jenkinsfile
// FrigateRelay — coverage pipeline
// Runs on: push to main, weekly cron.
// Produces: cobertura XML artifacts consumed by the Jenkins Coverage plugin.

pipeline {
    agent none

    environment {
        DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        DOTNET_NOLOGO               = '1'
        NUGET_XMLDOC_MODE           = 'skip'
    }

    triggers {
        // Weekly on Sunday at 02:00 in addition to push-triggered runs.
        cron('0 2 * * 0')
    }

    stages {
        stage('Build & Coverage') {
            agent {
                docker {
                    image 'mcr.microsoft.com/dotnet/sdk:10.0'
                    // Persist NuGet cache across runs to speed up restore.
                    args  '-v nuget-cache:/root/.nuget/packages'
                }
            }

            steps {
                sh 'dotnet restore FrigateRelay.sln'

                sh 'dotnet build FrigateRelay.sln -c Release --no-restore'

                // Run each test project with MTP coverage flags.
                // -- separates dotnet-run args from test-exe args (required).
                sh '''
                    dotnet run --project tests/FrigateRelay.Abstractions.Tests \
                        -c Release --no-build -- \
                        --coverage \
                        --coverage-output-format cobertura \
                        --coverage-output coverage/abstractions-tests/FrigateRelay.Abstractions.Tests.cobertura.xml

                    dotnet run --project tests/FrigateRelay.Host.Tests \
                        -c Release --no-build -- \
                        --coverage \
                        --coverage-output-format cobertura \
                        --coverage-output coverage/host-tests/FrigateRelay.Host.Tests.cobertura.xml
                '''
            }

            post {
                always {
                    // Archive the raw XML so the Coverage plugin can parse it.
                    archiveArtifacts artifacts: 'coverage/**/*.cobertura.xml',
                                     allowEmptyArchive: false

                    // Display coverage trends in Jenkins UI.
                    // Requires "Coverage" plugin (jenkinsci/coverage-plugin).
                    // The older "Cobertura" plugin uses coberturaPublisher() instead;
                    // prefer recordCoverage() as coberturaPublisher is end-of-life.
                    recordCoverage(
                        tools: [[parser: 'COBERTURA', pattern: 'coverage/**/*.cobertura.xml']],
                        id: 'cobertura',
                        name: 'FrigateRelay Coverage'
                    )
                }
            }
        }
    }

    post {
        failure {
            echo 'Pipeline failed. Check stage logs for details.'
        }
        success {
            echo 'Coverage pipeline completed.'
        }
    }
}
```

### Notes

- The Docker agent uses the official Microsoft SDK image tag `10.0` (tracks `10.0.x` patch releases). Pin to `10.0.107` in the image tag if you need reproducibility: `mcr.microsoft.com/dotnet/sdk:10.0.107`.
- The NuGet cache volume `nuget-cache` must be a named Docker volume pre-created on the Jenkins agent host, or replaced with a bind-mount path. Without it, every build re-downloads packages.
- `recordCoverage` requires the [Coverage plugin](https://plugins.jenkins.io/coverage/) (successor to the deprecated Cobertura plugin). If the Jenkins instance uses the older Cobertura plugin, substitute `coberturaPublisher(coberturaReportFile: 'coverage/**/*.cobertura.xml')`.
- `--no-build` on `dotnet run` is correct — the solution was already built in the `dotnet build` step.

---

## Secret-Scan Regex Set

Patterns for `.github/workflows/secret-scan.yml`. Ordered from highest to lowest confidence. All patterns use ERE syntax (`grep -E` / `git grep -E`).

| # | Pattern | Justification | FP Risk |
|---|---------|---------------|---------|
| 1 | `AppToken\s*=\s*[A-Za-z0-9]{20,}` | Exact Pushover `AppToken` field name from legacy config and PROJECT.md. No realistic false positive — this field name is application-specific. | None |
| 2 | `UserKey\s*=\s*[A-Za-z0-9]{20,}` | Exact Pushover `UserKey` field name. Same reasoning. | None |
| 3 | `192\.168\.[0-9]{1,3}\.[0-9]{1,3}` | Hard-coded RFC-1918 /16 subnet addresses. Legacy code had `192.168.0.58` hard-coded. Any occurrence in `src/` is forbidden by architecture invariants. | Very low — `192.168.x.x` patterns are specific enough; `10.x.x.x` / `172.16-31.x.x` omitted intentionally (too common in URLs and test stubs). |
| 4 | `api[Kk]ey\s*[=:]\s*["']?[A-Za-z0-9_\-]{20,}` | Generic API key assignment covering `apiKey =`, `api_key:`, `ApiKey=` forms. Anchored to an assignment operator to avoid matching variable names. Tightened from ROADMAP's `api[a-z0-9]{28,}` which had no assignment anchor and would match identifiers like `apiclientlogginghandler`. | Low |
| 5 | `Bearer\s+[A-Za-z0-9._\-]{20,}` | Bearer tokens in source (e.g. hard-coded HTTP header value). Requires `Bearer ` prefix — low FP rate. Would catch a Blue Iris or CodeProject.AI bearer token accidentally committed. | Low |
| 6 | `ghp_[A-Za-z0-9]{36}` | GitHub PAT format (classic). Exact 36-char alphanumeric body after `ghp_` prefix matches the GitHub format exactly. No realistic false positive — the prefix is GitHub-specific. | None |
| 7 | `AKIA[A-Z0-9]{16}` | AWS Access Key ID. Exact 20-char format with `AKIA` prefix. Not an AWS project today, but this pattern is so precise (no FPs) and the impact so high that it costs nothing to include. | None |

**Patterns deliberately excluded:**

- `api[a-z0-9]{28,}` (ROADMAP baseline) — too loose; matches identifiers, URLs, and package names. Replaced by pattern 4 above which requires an assignment anchor.
- Pushover 30-char alphanumeric token without field name anchor — would match any 30-char alphanumeric string including GUIDs, base64 chunks, and hex hashes. Keep field-anchored patterns (1 and 2) instead.
- `[0-9a-f]{32}` (MD5 hashes) — too broad; matches cache keys, test GUIDs, etc.
- GitHub fine-grained PAT (`github_pat_`) — valid but less common; can be added in a later phase if the repo starts using fine-grained tokens.

**Exclusion paths for the `scan` job:**
- `.shipyard/` — planning docs discuss secret patterns legitimately.
- `.github/secret-scan-fixture.txt` — intentional fixture; excluded by path.

---

## Secret-Scan Fixture Format

### Draft `.github/secret-scan-fixture.txt`

```
# FrigateRelay secret-scan fixture
# This file contains FAKE credential-shaped strings for tripwire self-testing.
# None of these are real credentials. The file is intentionally committed.
# The 'scan' job excludes this file; the 'tripwire-self-test' job targets it.
#
# Format: one match per pattern, clearly labelled.

# Pattern 1 — Pushover AppToken
AppToken=abcdefghijklmnopqrstuvwxyz012345

# Pattern 2 — Pushover UserKey
UserKey=ABCDEFGHIJKLMNOPQRSTUVWXYZ012345

# Pattern 3 — RFC-1918 hard-coded IP
server_url=http://192.168.99.99:8080/trigger

# Pattern 4 — Generic API key
apiKey = "xK9mR2nP4qT7vL1wY5sA8bD3cF6hJ0eG"

# Pattern 5 — Bearer token
Authorization: Bearer eyJhbGciOiJub25lIiwidHlwIjoiSldUIn0.fake.signature

# Pattern 6 — GitHub PAT
token=ghp_ABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890

# Pattern 7 — AWS Access Key ID
aws_access_key_id=AKIAIOSFODNN7EXAMPLE00
```

### Self-test shell snippet

Place this in the `tripwire-self-test` job of `secret-scan.yml`. The script iterates every pattern and asserts each matches at least one line in the fixture. A non-match means the pattern is broken (e.g., after a regex edit that introduced a syntax error or over-tightened the pattern).

```bash
#!/usr/bin/env bash
set -euo pipefail

FIXTURE=".github/secret-scan-fixture.txt"
FAILED=0

check() {
  local label="$1"
  local pattern="$2"
  if git grep -qE "$pattern" -- "$FIXTURE"; then
    echo "PASS: $label"
  else
    echo "FAIL: $label — pattern did not match fixture"
    FAILED=1
  fi
}

check "AppToken"          'AppToken\s*=\s*[A-Za-z0-9]{20,}'
check "UserKey"           'UserKey\s*=\s*[A-Za-z0-9]{20,}'
check "RFC-1918 IP"       '192\.168\.[0-9]{1,3}\.[0-9]{1,3}'
check "Generic apiKey"    'api[Kk]ey\s*[=:]\s*["'"'"']?[A-Za-z0-9_\-]{20,}'
check "Bearer token"      'Bearer\s+[A-Za-z0-9._\-]{20,}'
check "GitHub PAT"        'ghp_[A-Za-z0-9]{36}'
check "AWS Access Key"    'AKIA[A-Z0-9]{16}'

if [ "$FAILED" -ne 0 ]; then
  echo "One or more secret-scan patterns did not match the fixture. Regex may be broken."
  exit 1
fi
echo "All patterns matched fixture — scanner is healthy."
```

**Note on fixture discoverability:** The strings in the fixture are fake (wrong length, wrong character distribution, or known test values like `AKIAIOSFODNN7EXAMPLE00` from AWS documentation). No gitattributes or `.gitignore` trick will prevent a search engine from indexing them if the repo is public. Mitigation: ensure every fixture string is visibly fake (e.g., `EXAMPLE`, sequential chars, or documented test values). Do not use real credential shapes that coincidentally pass validation — use clearly synthetic ones.

---

## DNWQ Patterns Worth Copying

From `ci.yml` and `Jenkinsfile`:

1. **`DOTNET_CLI_TELEMETRY_OPTOUT=1` + `DOTNET_NOLOGO=1` + `NUGET_XMLDOC_MODE=skip`** — three environment variables set at pipeline level. Reduce noise in build logs and skip telemetry without needing per-step flags. Copy verbatim into both `ci.yml` env block and Jenkinsfile environment block.

2. **`stash`/`unstash` pattern for coverage artifacts** — DNWQ stashes coverage XML per-stage and unstashes all of them in a final `Coverage Report` stage. For FrigateRelay's two-project scope, this is overkill in Phase 2 (both projects run in one stage), but the pattern is right if the repo grows. Not adopted in Phase 2.

3. **Build once, test with `--no-build`** — DNWQ builds first then passes `--no-build` (or `--no-restore`) to test steps. FrigateRelay should do the same: `dotnet build -c Release --no-restore`, then `dotnet run ... --no-build`. Avoids silent Debug/Release mode mismatch.

4. **`agent { label 'docker' }` for Jenkins** — DNWQ uses a Docker-labeled agent rather than specifying the image in `agent { docker { image ... } }`. This works if the Jenkins agent has .NET installed natively. For FrigateRelay we use `agent { docker { image 'mcr.microsoft.com/dotnet/sdk:10.0' } }` instead so Jenkins does not need a per-host .NET install. Both approaches are valid; the inline Docker image approach is more portable.

5. **`post { failure / success }` blocks** — DNWQ wraps both pipeline-level and stage-level post conditions. Copy the pipeline-level `post` block for visibility.

---

## DNWQ Patterns That DO NOT Apply

1. **Per-database-transport test matrix** — DNWQ runs 12+ parallel integration stages (SqlServer, PostgreSQL, Redis, SQLite, LiteDB, Memory — each with a Linq variant). FrigateRelay has two test projects and no database transports. Do not copy the parallel stage structure.

2. **`--settings Source/coverage.runsettings --collect:"XPlat Code Coverage"`** — This is the VSTest/Coverlet invocation. FrigateRelay uses MTP (`dotnet run -- --coverage --coverage-output-format cobertura`). These are incompatible; mixing them would fail.

3. **`-f net10.0` framework flag on `dotnet test`** — DNWQ specifies `-f net10.0` because its projects multi-target (`net6.0;net8.0;net10.0`). FrigateRelay is single-target `net10.0` only; the flag is unnecessary.

4. **`dotnet test` invocation** — DNWQ uses `dotnet test` because its test projects use the VSTest runner (or the MTP VSTest compat mode). FrigateRelay's test projects have `EnableMSTestRunner=true` and `OutputType=Exe` which blocks `dotnet test` on .NET 10 SDK (see `https://aka.ms/dotnet-test-mtp-error`). Use `dotnet run` exclusively.

5. **`reportgenerator` + Codecov upload** — DNWQ's Coverage Report stage installs `dotnet-reportgenerator-globaltool` and uploads to Codecov via a credential. FrigateRelay is zero-budget and does not have a Codecov account. Use `recordCoverage` (Jenkins Coverage plugin) directly on the raw cobertura XML.

6. **`actions/setup-dotnet@v5` with multi-version `dotnet-version:` block** — DNWQ installs both .NET 8 and .NET 10 because of multi-targeting. FrigateRelay uses `global-json-file: global.json` only.

7. **`actions/checkout@v5`** — DNWQ uses v5. Current stable release of `actions/checkout` is v4. Use `actions/checkout@v4` unless there is a specific reason to pin v5 (there is none for this repo).

8. **`withCredentials([string(credentialsId: ...)])` blocks** — DNWQ injects database connection strings via Jenkins credentials. FrigateRelay Phase 2 has no credentials to inject; skip this pattern entirely.

9. **`sleep(time: N, unit: 'SECONDS')` stagger** — DNWQ staggers parallel stages with sleeps to avoid Docker-in-Docker port collisions. FrigateRelay has no parallel stages in Phase 2.

---

## Open Questions

1. **`actions/setup-dotnet@v4` vs `@v5`** — DNWQ uses `v5`. The research found that `@v4` supports `global-json-file` with `rollForward`. Whether `@v5` exists as a stable release or is a pre-release/alias needs a one-line check (`curl https://api.github.com/repos/actions/setup-dotnet/tags`). If `v5` is stable, prefer it for forward-compat with the runner ecosystem; if it is a floating alias or pre-release, stay on `v4`. **Decision required by architect.**

2. **`setup-dotnet` behavior when `10.0.x` feature band is not yet on the runner image** — The research confirms that `rollForward: latestFeature` causes the action to install the latest available `10.0.x` SDK, but the behavior when *no* `10.0.x` is available is undocumented. This is unlikely to bite in practice (GitHub-hosted `ubuntu-latest` and `windows-latest` ship current .NET SDKs), but if the repo is created before a new SDK feature band is available on runners, the workflow will fail at setup time. Mitigation: the action will install the SDK from the distribution channel if it is not pre-installed. No action needed, but worth noting.

3. **Jenkins Coverage plugin vs legacy Cobertura plugin** — The Jenkinsfile uses `recordCoverage()` (Coverage plugin). If the Jenkins instance uses only the older `coberturaPublisher()` (Cobertura plugin, which reached end-of-life), the `recordCoverage` step will fail with "No such DSL method." The architect should confirm which plugin is installed on the target Jenkins instance. Both are included as comments in the Jenkinsfile skeleton above.

4. **NuGet cache volume for Jenkins** — The Jenkinsfile mounts `nuget-cache` as a named Docker volume. This volume must exist on the Jenkins agent host before the pipeline runs (`docker volume create nuget-cache`). If the agent is ephemeral or managed, a host bind-mount path (e.g., `-v /var/jenkins/nuget-cache:/root/.nuget/packages`) may be more reliable. **Architect should confirm the Jenkins agent topology.**

5. **`MSTest.Sdk` version in Dependabot** — The test projects use `Sdk="Microsoft.NET.Sdk"` not `Sdk="MSTest.Sdk"`, so the known Dependabot limitation for MSBuild SDK version updates in `global.json` does not affect the `MSTest` package version (which is a regular `PackageReference`). Confirmed: Dependabot will update `MSTest` 4.2.1 via `PackageReference` normally.

6. **`actions/checkout` fetch-depth for `git grep` in secret-scan** — `git grep` on the working tree does not need history, but the default `fetch-depth: 0` (full history) is unnecessary. Consider `fetch-depth: 1` on the secret-scan job to speed up the checkout step. Small optimization; architect may decide to standardize on one `fetch-depth` across all workflow jobs.

---

## Sources

1. [Microsoft.Testing.Platform code coverage — .NET | Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-code-coverage) (updated 2026-02-25)
2. [MSTest SDK configuration — .NET | Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-mstest-sdk) (updated 2026-04-09)
3. [Microsoft.Testing.Platform Code Coverage extensions — .NET | Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-extensions-code-coverage)
4. [actions/setup-dotnet — Support rollForward option from global.json (PR #224)](https://github.com/actions/setup-dotnet/pull/224) (merged 2021-09-13)
5. [actions/setup-dotnet — rollForward issue #223](https://github.com/actions/setup-dotnet/issues/223)
6. [Configuring Dependabot version updates — GitHub Docs](https://docs.github.com/en/code-security/how-tos/secure-your-supply-chain/secure-your-dependencies/configuring-dependabot-version-updates)
7. [Dependabot options reference — GitHub Docs](https://docs.github.com/en/code-security/reference/supply-chain-security/dependabot-options-reference)
8. [Coverage Plugin — Jenkins](https://plugins.jenkins.io/coverage/)
9. [recordCoverage step — Jenkins Pipeline Steps](https://www.jenkins.io/doc/pipeline/steps/coverage/)
10. [Microsoft.Testing.Extensions.CodeCoverage NuGet package](https://www.nuget.org/packages/Microsoft.Testing.Extensions.CodeCoverage/) (v18.6.2 as of research date)
11. [Using Docker with Jenkins Pipeline](https://www.jenkins.io/doc/book/pipeline/docker/)
12. [global.json overview — .NET CLI | Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json)
