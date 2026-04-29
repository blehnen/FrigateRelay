---
phase: 11-oss-polish
plan: 3.1
wave: 3
dependencies: [1.1, 2.3, 2.4]
must_haves:
  - docs/plugin-author-guide.md (tutorial-first; one code sample per plugin contract)
  - samples/FrigateRelay.Samples.PluginGuide/ project added to FrigateRelay.sln
  - CI doc-rot check — code blocks in plugin-author-guide.md byte-match files in samples/
files_touched:
  - docs/plugin-author-guide.md
  - samples/FrigateRelay.Samples.PluginGuide/FrigateRelay.Samples.PluginGuide.csproj
  - samples/FrigateRelay.Samples.PluginGuide/Program.cs
  - samples/FrigateRelay.Samples.PluginGuide/SampleActionPlugin.cs
  - samples/FrigateRelay.Samples.PluginGuide/SampleValidationPlugin.cs
  - samples/FrigateRelay.Samples.PluginGuide/SampleSnapshotProvider.cs
  - samples/FrigateRelay.Samples.PluginGuide/SamplePluginRegistrar.cs
  - .github/scripts/check-doc-samples.sh
  - .github/workflows/docs.yml  # add doc-rot check job (extends PLAN-2.4 stub)
  - FrigateRelay.sln
tdd: false
risk: medium
---

# Plan 3.1: Plugin author guide + samples project + doc-rot check

## Context

The "stale docs cannot silently ship" ROADMAP success criterion. Three coupled deliverables:

1. **`docs/plugin-author-guide.md`** — tutorial-first walkthrough (architect-discretion locked here: tutorial-first beats reference-first because it's the new-contributor onboarding path; CONTRIBUTING and README already point here). Walks through scaffolding a plugin via `dotnet new frigaterelay-plugin` (PLAN-2.3 deliverable), then extending it for each contract (`IActionPlugin`, `IValidationPlugin`, `ISnapshotProvider`, `IPluginRegistrar`). Closes with the "design for B" note about `AssemblyLoadContext` being the future loader (PROJECT.md Goal #3 — additive, not a rewrite).

2. **`samples/FrigateRelay.Samples.PluginGuide/`** project — a SINGLE compilable .NET project that contains the canonical implementations for every code block in the guide. Doc code-blocks are copied verbatim from these files. Per CONTEXT-11 D8: in `FrigateRelay.sln` (so `dotnet build FrigateRelay.sln -c Release` builds it under warnings-as-errors), but CI test-run is in `docs.yml` only, NOT `ci.yml`.

3. **CI doc-rot check** — Option B from RESEARCH.md sec 10 (architect-discretion locked here). A small bash script extracts fenced code blocks from `docs/plugin-author-guide.md` whose info-string carries a `filename=` annotation (e.g. `` ```csharp filename=SampleActionPlugin.cs ``), then byte-compares each block against `samples/FrigateRelay.Samples.PluginGuide/<filename>`. Mismatch = CI failure with a clear diff. Wired into `docs.yml` as a third job.

**Architect-discretion justifications:**

- **Tutorial-first guide structure.** Reference docs work for known contracts; tutorial docs work for unknown contracts. The plugin contract surface is small (4 interfaces), and the guide audience is "first-time plugin author." Tutorial-first.
- **Samples project ships a single executable.** `Program.cs` exists with `Main` that exercises each plugin shape via in-process DI to prove the samples are runnable, not just buildable. PLAN-2.4's `samples-build` job runs `dotnet run --project samples/FrigateRelay.Samples.PluginGuide` — if `Main` returns 0, samples are healthy. This avoids a separate `samples-tests` project.
- **Doc-rot check is byte-match, not AST-match.** RESEARCH.md sec 10 Option B noted the script is ~20 lines. Architect agrees: byte-match is rigorous AND simple; AST-match would require a Roslyn dependency in CI. Builder decides whether to use bash + sed/awk or python — preference: bash to match `secret-scan.sh` precedent.
- **filename annotation convention.** Code fences use `` ```csharp filename=Foo.cs ``. The script greps for that string, extracts the next fenced block until the closing fence, byte-compares to `samples/FrigateRelay.Samples.PluginGuide/Foo.cs`. Plain `csharp` fences (no filename annotation) are NOT checked — they're free-form snippets in the guide that don't map 1:1 to a sample file.

## Dependencies

- **Wave 1 gate:** PLAN-1.1.
- **Wave 2 gates:** PLAN-2.3 (template exists; the guide walks readers through `dotnet new frigaterelay-plugin`) and PLAN-2.4 (`docs.yml` exists; this plan extends it with a third job).
- **Touch boundary on `.github/workflows/docs.yml`** — PLAN-2.4 created the file with two jobs; this plan APPENDS a third job. Both plans modifying the same file is fine because PLAN-2.4 lands in Wave 2 and PLAN-3.1 lands in Wave 3 (sequential, not parallel).
- **Touch boundary on `FrigateRelay.sln`** — only this plan in Wave 3 touches the sln. No other Wave 3 plan exists.

## Tasks

### Task 1: Samples project + sln wiring

**Files:**
- `samples/FrigateRelay.Samples.PluginGuide/FrigateRelay.Samples.PluginGuide.csproj` (create)
- `samples/FrigateRelay.Samples.PluginGuide/Program.cs` (create)
- `samples/FrigateRelay.Samples.PluginGuide/SampleActionPlugin.cs` (create)
- `samples/FrigateRelay.Samples.PluginGuide/SampleValidationPlugin.cs` (create)
- `samples/FrigateRelay.Samples.PluginGuide/SampleSnapshotProvider.cs` (create)
- `samples/FrigateRelay.Samples.PluginGuide/SamplePluginRegistrar.cs` (create)
- `FrigateRelay.sln` (modify — add the samples project to the solution graph)

**Action:** create + modify

**Description:**

**`FrigateRelay.Samples.PluginGuide.csproj`** — `OutputType=Exe`, .NET 10, references Abstractions:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <IsPackable>false</IsPackable>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Http" Version="10.0.4" />
    <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="10.0.4" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\FrigateRelay.Abstractions\FrigateRelay.Abstractions.csproj" />
  </ItemGroup>
</Project>
```

`Directory.Build.props` (`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`) applies repo-wide → samples build under the same discipline as production code.

**`Program.cs`** — `Main` instantiates each sample plugin via DI, calls `ExecuteAsync` / `ValidateAsync` / `FetchAsync` once with synthetic inputs to prove they run, returns 0 on success. This is the body of the guide's "putting it together" final section. Single-file `Main`, no `Host.CreateApplicationBuilder` complexity (the guide reader already understands that from the Host project).

**`SampleActionPlugin.cs`** — minimal `IActionPlugin` (mirrors the template scaffold in PLAN-2.3, but with snapshot-consuming branch demonstrating the snapshot-resolve pattern). Includes XML doc-comments since `<GenerateDocumentationFile>true</GenerateDocumentationFile>`.

**`SampleValidationPlugin.cs`** — minimal `IValidationPlugin`. Returns `Verdict.Pass()` if `ctx.Label == "person"`, else `Verdict.Fail("not a person")`. Demonstrates the per-action validator pattern (CLAUDE.md V3).

**`SampleSnapshotProvider.cs`** — minimal `ISnapshotProvider`. Returns a stub byte array (e.g. 4 zero bytes) tagged with the request's event id. Demonstrates the contract; not a real HTTP fetch.

**`SamplePluginRegistrar.cs`** — `IPluginRegistrar` registering all three samples + their options.

**Sln wiring:** Use `dotnet sln FrigateRelay.sln add samples/FrigateRelay.Samples.PluginGuide/FrigateRelay.Samples.PluginGuide.csproj` (the canonical safe way to edit `.sln`; manual edits are error-prone). Verify `dotnet build FrigateRelay.sln -c Release` includes the samples project in its output.

**Constraints:**
- All files compile clean (warnings-as-errors).
- All public types have XML doc-comments (since `<GenerateDocumentationFile>true</GenerateDocumentationFile>` and warnings-as-errors → CS1591 fires on missing comments).
- No hard-coded IPs/hostnames.
- No `using static` shortcuts that obscure the contract types — readers should see `FrigateRelay.Abstractions.IActionPlugin`, not just `IActionPlugin` from a star import.

**Acceptance Criteria:**
- All seven files exist (csproj + Program + 4 plugin .cs + Registrar).
- `grep -q 'FrigateRelay.Samples.PluginGuide' FrigateRelay.sln` (sln wiring confirmed).
- `dotnet build FrigateRelay.sln -c Release` exits 0 with zero warnings.
- `dotnet build samples/FrigateRelay.Samples.PluginGuide -c Release` exits 0 (project builds standalone).
- `dotnet run --project samples/FrigateRelay.Samples.PluginGuide -c Release --no-build` exits 0.
- `grep -nE '192\.168\.|10\.[0-9]+\.[0-9]+\.[0-9]+\.[0-9]|AppToken=' samples/` returns zero matches.

### Task 2: docs/plugin-author-guide.md (tutorial-first)

**Files:**
- `docs/plugin-author-guide.md` (create)

**Action:** create

**Description:**

Tutorial-first structure. Each major section ends with a runnable code block tagged `` ```csharp filename=<File>.cs `` so the doc-rot check (Task 3) can byte-compare it.

Sections (in order):

1. **Audience + scope.** Who this guide is for (someone wanting to add a custom action / validator / snapshot provider). What it does NOT cover (changing the host, the dispatcher, or `IEventSource`). Cross-reference CLAUDE.md "Architecture invariants" for the hard constraints.

2. **Scaffold.** Step-by-step:
   ```bash
   dotnet new install templates/FrigateRelay.Plugins.Template/
   dotnet new frigaterelay-plugin -n FrigateRelay.Plugins.MyPlugin -o src/FrigateRelay.Plugins.MyPlugin
   ```
   Walk through what the template generated (csproj shape, the `ExampleActionPlugin` placeholder, the registrar).

3. **`IActionPlugin` walkthrough** — explain the contract (Name, ExecuteAsync), explain when to consume `SnapshotContext` and when to ignore it (BlueIris-pattern vs Pushover-pattern). Code block:
   ```csharp filename=SampleActionPlugin.cs
   <verbatim contents of samples/FrigateRelay.Samples.PluginGuide/SampleActionPlugin.cs>
   ```

4. **`IValidationPlugin` walkthrough** — explain per-action validator chains (CLAUDE.md V3); explain `Verdict.Pass()` / `Verdict.Fail(reason)`; explain that `ValidateAsync` receives the same pre-resolved `SnapshotContext` as the action (so HTTP-fetching the snapshot in the validator does not re-fetch). Code block:
   ```csharp filename=SampleValidationPlugin.cs
   <verbatim contents>
   ```

5. **`ISnapshotProvider` walkthrough** — explain the 3-tier resolution order (per-action override → per-subscription default → global `DefaultSnapshotProvider`). Code block:
   ```csharp filename=SampleSnapshotProvider.cs
   <verbatim contents>
   ```

6. **`IPluginRegistrar` walkthrough** — the entry point the host discovers via DI. Code block:
   ```csharp filename=SamplePluginRegistrar.cs
   <verbatim contents>
   ```

7. **Configuration binding.** How `Profiles` + `Subscriptions` (CLAUDE.md S2) reach the plugin via `IOptions<T>`. Both shapes for `Subscriptions:N:Actions` (object form vs string-array shorthand — ID-12 closure is the operator-facing rationale).

8. **Lifecycle + DI scope rules.** Singleton-only registrations for `IActionPlugin`/`IValidationPlugin`/`ISnapshotProvider` (the dispatcher resolves them once at startup). `IHttpClientFactory` is the canonical way to get scoped `HttpClient` instances. No `_logger.Error(ex.Message, ex)` anti-pattern (CLAUDE.md observability section).

9. **Testing your plugin.** Pattern from RESEARCH.md sec 3 (BlueIris.Tests shape) — MTP runner, `dotnet run` not `dotnet test`, NSubstitute for mocking, WireMock.Net for HTTP stubs, optional `FrigateRelay.TestHelpers.CapturingLogger<T>` if testing log emission.

10. **Forward-compat note.** "Design for B": the same contract will load via `AssemblyLoadContext` in a future phase (PROJECT.md Goal #3). Practical implication: do NOT take static dependencies on host-internal types; use only `FrigateRelay.Abstractions` types in your public surface.

11. **Putting it together.** Show the `Program.cs` final block (verbatim from samples) demonstrating in-process DI of all three plugin shapes:
    ```csharp filename=Program.cs
    <verbatim contents>
    ```

**Constraints:**
- Every `csharp filename=...` fenced block matches BYTE-FOR-BYTE the corresponding file in `samples/FrigateRelay.Samples.PluginGuide/`. Builder workflow: write the samples files in Task 1 first, then in Task 2 cat the file contents into the doc fence with no whitespace edits, no comment trimming, no reformatting.
- Markdown is well-formed; all internal links resolve (CLAUDE.md, README.md, CONTRIBUTING.md, ROADMAP.md).
- No marketing prose. No screenshots/GIFs.

**Acceptance Criteria:**
- `test -f docs/plugin-author-guide.md`
- `grep -q '^# ' docs/plugin-author-guide.md` (single H1).
- `grep -cE '^\`\`\`csharp filename=' docs/plugin-author-guide.md` returns at least 4 (one per Action / Validation / Snapshot / Registrar; Program.cs makes 5).
- `grep -q 'dotnet new frigaterelay-plugin' docs/plugin-author-guide.md`
- `grep -q 'AssemblyLoadContext' docs/plugin-author-guide.md` ("design for B" note).
- `grep -q 'FrigateRelay.TestHelpers' docs/plugin-author-guide.md` (testing section reference).
- `grep -nE '192\.168\.|AppToken=[A-Za-z0-9]{20,}' docs/plugin-author-guide.md` returns zero matches.

### Task 3: Doc-rot check script + docs.yml job (extends PLAN-2.4)

**Files:**
- `.github/scripts/check-doc-samples.sh` (create)
- `.github/workflows/docs.yml` (modify — append a third job `doc-samples-rot`)

**Action:** create + modify

**Description:**

**`.github/scripts/check-doc-samples.sh`** — a small bash script that:

1. Greps `docs/plugin-author-guide.md` for fenced-block opens of the form `` ```csharp filename=<NAME> `` (anchored to the start of a line; trailing flags after the filename name are ignored).
2. For each match, extracts the fenced block contents (everything between the opening fence and the next bare `` ``` `` line on its own).
3. Compares the extracted contents byte-for-byte against `samples/FrigateRelay.Samples.PluginGuide/<NAME>`.
4. On any mismatch, prints a `diff -u` of expected vs actual and exits non-zero.

Suggested implementation skeleton (architect-discretion; builder may rewrite for clarity):

```bash
#!/usr/bin/env bash
set -euo pipefail
DOC=${DOC:-docs/plugin-author-guide.md}
SAMPLES_DIR=${SAMPLES_DIR:-samples/FrigateRelay.Samples.PluginGuide}
exit_code=0

# Extract: filename + body for each fenced csharp block with a filename= annotation
python3 - "$DOC" "$SAMPLES_DIR" <<'PY'
import sys, re, pathlib
doc = pathlib.Path(sys.argv[1]).read_text()
samples = pathlib.Path(sys.argv[2])
pat = re.compile(r"^```csharp\s+filename=(\S+)\s*\n(.*?)^```", re.M | re.S)
fail = 0
for m in pat.finditer(doc):
    name, body = m.group(1), m.group(2)
    sample_path = samples / name
    if not sample_path.exists():
        print(f"::error file={sys.argv[1]}::doc references missing sample file: {sample_path}")
        fail = 1
        continue
    expected = sample_path.read_text()
    if body != expected:
        print(f"::error file={sample_path}::doc/sample drift in {name}")
        import difflib
        sys.stdout.writelines(
            difflib.unified_diff(expected.splitlines(keepends=True),
                                 body.splitlines(keepends=True),
                                 fromfile=str(sample_path), tofile=f"{sys.argv[1]}#{name}"))
        fail = 1
sys.exit(fail)
PY
```

(Builder may use Python via `python3 - <<'PY'` heredoc as shown above, or pure bash with `awk`/`sed` — preference for Python because the regex over multiline fenced blocks is awkward in pure bash. The `secret-scan.sh` precedent uses bash + grep, but that's line-by-line; multi-line block extraction warrants Python.)

**Make the script executable** (`chmod +x .github/scripts/check-doc-samples.sh`).

**`docs.yml` modification** — append a third job `doc-samples-rot`:

```yaml
  doc-samples-rot:
    name: Doc-sample byte-match check
    runs-on: ubuntu-latest
    timeout-minutes: 5
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-python@v5
        with:
          python-version: '3.x'
      - name: Compare doc fences against samples
        run: bash .github/scripts/check-doc-samples.sh
```

**Local verification (builder, before commit):**

After writing the doc and the samples files identically, run:

```bash
bash .github/scripts/check-doc-samples.sh
echo "exit=$?"
```

Expected: exit 0 with no output. If any non-zero, fix the drift before committing.

**Acceptance Criteria:**
- `test -x .github/scripts/check-doc-samples.sh` (executable bit set).
- `bash .github/scripts/check-doc-samples.sh` exits 0 (no drift).
- `grep -q 'doc-samples-rot' .github/workflows/docs.yml` (job added).
- `grep -q 'check-doc-samples.sh' .github/workflows/docs.yml`
- `python3 -c 'import yaml; yaml.safe_load(open(".github/workflows/docs.yml"))'` exits 0 (still valid YAML after append).

## Verification

Run from repo root:

```bash
# 0. All target files exist
test -f docs/plugin-author-guide.md
test -f samples/FrigateRelay.Samples.PluginGuide/FrigateRelay.Samples.PluginGuide.csproj
test -f samples/FrigateRelay.Samples.PluginGuide/Program.cs
test -f samples/FrigateRelay.Samples.PluginGuide/SampleActionPlugin.cs
test -f samples/FrigateRelay.Samples.PluginGuide/SampleValidationPlugin.cs
test -f samples/FrigateRelay.Samples.PluginGuide/SampleSnapshotProvider.cs
test -f samples/FrigateRelay.Samples.PluginGuide/SamplePluginRegistrar.cs
test -f .github/scripts/check-doc-samples.sh
test -x .github/scripts/check-doc-samples.sh

# 1. Solution graph contains samples project
grep -q 'FrigateRelay.Samples.PluginGuide' FrigateRelay.sln

# 2. Build everything (samples + production)
dotnet build FrigateRelay.sln -c Release

# 3. Run samples Program.cs (Main returns 0 on healthy plugin set)
dotnet run --project samples/FrigateRelay.Samples.PluginGuide -c Release --no-build

# 4. Doc-rot byte-match check
bash .github/scripts/check-doc-samples.sh

# 5. Doc structure invariants
grep -q 'dotnet new frigaterelay-plugin' docs/plugin-author-guide.md
test "$(grep -cE '^\`\`\`csharp filename=' docs/plugin-author-guide.md)" -ge 4

# 6. docs.yml has the new job
grep -q 'doc-samples-rot' .github/workflows/docs.yml
python3 -c 'import yaml; yaml.safe_load(open(".github/workflows/docs.yml"))'

# 7. Tests still green (Wave 1 gate did not regress)
bash .github/scripts/run-tests.sh

# 8. Secret + IP scan
grep -nE '192\.168\.|10\.[0-9]+\.[0-9]+\.[0-9]+\.[0-9]|AppToken=[A-Za-z0-9]{20,}' \
  docs/ samples/ .github/scripts/check-doc-samples.sh && exit 1 || true
```
