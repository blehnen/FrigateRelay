# Contributing to FrigateRelay

## Building and testing

All commands assume `FrigateRelay.sln` at repo root.

**Build** (warnings-as-errors; must be clean on Linux and Windows):

```bash
dotnet build FrigateRelay.sln -c Release
```

**Run tests** — test projects use MSTest v3 with Microsoft.Testing.Platform runner (`OutputType=Exe`). Use `dotnet run`, not `dotnet test`:

```bash
dotnet run --project tests/FrigateRelay.Abstractions.Tests -c Release
dotnet run --project tests/FrigateRelay.Host.Tests -c Release
```

The CI wrapper script runs all test projects:

```bash
bash .github/scripts/run-tests.sh
```

**Filter to a single test:**

```bash
dotnet run --project tests/FrigateRelay.Host.Tests -c Release -- --filter "PluginRegistrarRunnerTests"
```

**Integration tests** require Docker (Testcontainers spins up Mosquitto and WireMock stubs automatically).

## Coding standards

The full set of architecture invariants is in `CLAUDE.md` — treat it as authoritative. Contributor-relevant highlights:

- **Warnings-as-errors** enforced repo-wide. Build must be clean on both Linux and Windows before opening a PR.
- **No `.Result` / `.Wait()` in source.** Always await.
- **Test names use underscores** — `Method_Condition_Expected` (DAMP convention). `CA1707` is silenced for `tests/**.cs` via `.editorconfig`.
- **Use the shared `CapturingLogger<T>`** from `tests/FrigateRelay.TestHelpers/` for log assertions — do not create per-assembly copies and do not mock `ILogger<T>` via NSubstitute (fragile around generic `TState` matching).
- **`<InternalsVisibleTo>` via the MSBuild item form** in `.csproj`, not a source-level `[assembly:]` attribute.
- **No hard-coded IPs or hostnames** — including in comments. The secret-scan CI job will fail the build.
- **No secrets in committed config files.** Secret fields must default to `""` and be supplied via environment variables or user-secrets at runtime.

## Test framework details

- **MSTest v3 (4.2.1)** with Microsoft.Testing.Platform runner.
- **FluentAssertions pinned at 6.12.2** — do not upgrade. This version is Apache-2.0 licensed; 7.x moved to a commercial license and is incompatible with the MIT-compatible deps constraint.
- **NSubstitute** for mocks. When mocking an `internal` type, add both `<InternalsVisibleTo Include="FrigateRelay.X.Tests" />` and `<InternalsVisibleTo Include="DynamicProxyGenAssembly2" />` to the production `.csproj`.
- **Testcontainers.NET** for integration tests (requires Docker).
- **WireMock.Net** for HTTP stubs (Blue Iris, Pushover, CodeProject.AI).
- New and changed features need unit or integration test coverage.

## PR checklist

Copy this into your PR description:

```
- [ ] Build is green on Linux: `dotnet build FrigateRelay.sln -c Release`
- [ ] Tests pass: `bash .github/scripts/run-tests.sh`
- [ ] No new `.Result` / `.Wait()` calls in source
- [ ] No hard-coded IPs/hostnames or secrets
- [ ] CHANGELOG.md updated under `## [Unreleased]` if the change is user-visible
- [ ] New plugin? Followed `docs/plugin-author-guide.md`
- [ ] Phase-managed commit? Message follows `shipyard(phase-N): ...` convention
```

## Adding a new plugin

Use the scaffold template to generate a correctly-shaped plugin project:

```bash
dotnet new install templates/FrigateRelay.Plugins.Template
dotnet new frigaterelay-plugin -n FrigateRelay.Plugins.MyPlugin -o src/FrigateRelay.Plugins.MyPlugin
```

See `docs/plugin-author-guide.md` for the full walkthrough — contract interfaces, options binding, registrar pattern, test setup, and wiring into the host.

## Reporting issues

For security vulnerabilities, see `SECURITY.md` — do not open a public issue. For all other bugs and feature requests, open a [GitHub Issue](https://github.com/blehnen/FrigateRelay/issues).
