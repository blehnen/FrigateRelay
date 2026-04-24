#!/usr/bin/env bash
# .github/scripts/run-tests.sh
#
# Canonical test-runner script shared between GitHub Actions (ci.yml) and
# Jenkins (Jenkinsfile). Discovers all test projects under tests/*.Tests/ and
# invokes each via `dotnet run` (the MTP runner path — .NET 10 SDK blocks
# `dotnet test` against Microsoft.Testing.Platform).
#
# USAGE:
#   bash .github/scripts/run-tests.sh             # fast mode — no coverage
#   bash .github/scripts/run-tests.sh --coverage  # Jenkins — MTP cobertura
#
# ENV:
#   CONFIG    Build configuration (default: Release)
#
# Adding a new test project requires NO changes here — the `find` glob picks
# it up automatically. Extracted in Phase 3 per Phase-2 reviewer advisory
# (Rule of Three: 3 test projects is when the duplication cost starts to
# outweigh the extraction cost).

set -euo pipefail

# Resolve the repo root so `find tests` works regardless of the caller's PWD.
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" &> /dev/null && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$REPO_ROOT"

CONFIG="${CONFIG:-Release}"
COVERAGE=0
if [[ "${1:-}" == "--coverage" ]]; then
  COVERAGE=1
fi

# Discover *.Tests csproj files directly under tests/<Name>/. Depth 2 so
# we don't recurse into bin/ or obj/.
mapfile -t PROJECTS < <(find tests -maxdepth 2 -name '*.Tests.csproj' -type f | sort)

if [[ ${#PROJECTS[@]} -eq 0 ]]; then
  echo "run-tests.sh: no test projects found under tests/*.Tests/" >&2
  exit 2
fi

echo "run-tests.sh: discovered ${#PROJECTS[@]} test project(s); coverage=$COVERAGE; config=$CONFIG"

for proj in "${PROJECTS[@]}"; do
  name=$(basename "$(dirname "$proj")")
  echo ""
  echo "── ${name} ──"

  if [[ "$COVERAGE" -eq 1 ]]; then
    mkdir -p "coverage/${name}"
    dotnet run --project "$proj" -c "$CONFIG" --no-build -- \
      --coverage \
      --coverage-output-format cobertura \
      --coverage-output "coverage/${name}/coverage.cobertura.xml"

    # MTP's Microsoft.Testing.Extensions.CodeCoverage honors --coverage-output
    # inside the mcr.microsoft.com/dotnet/sdk:10.0 container but ignores it on
    # some host setups (observed on WSL/Ubuntu), writing instead to
    #   tests/<Name>.Tests/bin/<Config>/net10.0/TestResults/coverage/<Name>.Tests/coverage.cobertura.xml
    # Normalize by copying the actual output to the canonical path so the
    # Jenkinsfile archive glob (`coverage/**/coverage.cobertura.xml`) works in
    # both environments.
    if [[ ! -s "coverage/${name}/coverage.cobertura.xml" ]]; then
      fallback=$(find "tests/${name}/bin/${CONFIG}" -name 'coverage.cobertura.xml' -type f 2>/dev/null | head -1)
      if [[ -n "${fallback}" ]]; then
        cp "${fallback}" "coverage/${name}/coverage.cobertura.xml"
      fi
    fi
  else
    dotnet run --project "$proj" -c "$CONFIG" --no-build
  fi
done
