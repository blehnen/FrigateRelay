# Simplification Review — Phase 11

**Phase:** 11 — Open-Source Polish
**Date:** 2026-04-28
**Diff base:** 0861818..HEAD
**Scope:** ~25 files, ~1 800 lines added, ~20 lines removed (docs, samples, templates, root, .github/)

## Summary

Phase 11 is primarily documentation, scaffolding, and one test-mechanism fix. No cross-task duplication was found. The single notable pattern is the private `CapturingSerilogSink` nested inside `MqttToValidatorTests` — it works correctly now and the SUMMARY-1.1 already flags it as a latent extraction candidate if a second test class needs Serilog capture. All other code is well-scoped and proportionate to its purpose.

## Findings

### High (apply now or before ship)

None.

### Medium (apply opportunistically)

None.

### Low (track or dismiss)

1. **`CapturingSerilogSink` is a private nested class with no second consumer** — `tests/FrigateRelay.IntegrationTests/MqttToValidatorTests.cs:307`. The class translates `Serilog.Events.LogEvent` into `CapturedEntry` and is used by both `MqttToValidatorTests` test methods via the shared `CapturingLoggerProvider`. If a future test class (e.g. an end-to-end observability test) needs the same Serilog capture mechanism, extract `CapturingSerilogSink` + `CapturingLoggerProvider` + `CapturedEntry` to `tests/FrigateRelay.TestHelpers/`. Rule of Three: not actionable at 1 consumer; note for when a second test file needs it. **Effort:** Trivial when triggered.

2. **`docs.yml` repeats `checkout` + `setup-dotnet` steps across all three jobs** — `.github/workflows/docs.yml:49-55, 96-101, 133-136`. Each job independently checks out and sets up .NET (or Python). This is the standard GitHub Actions pattern for parallel jobs and is not a defect, but if the job count grows, a reusable workflow (`uses: ./.github/workflows/dotnet-setup.yml`) would consolidate the boilerplate. Rule of Three: currently 2 dotnet-setup repetitions (scaffold-smoke, samples-build); not worth extracting yet. **Effort:** Trivial when triggered.

3. **`SamplePluginRegistrar` XML doc block is verbose relative to its content** — `samples/FrigateRelay.Samples.PluginGuide/SamplePluginRegistrar.cs:6-26`. Three `<para>` blocks explain AddSingleton lifetime, IPluginRegistrar contract, and IHttpClientFactory guidance. This is a sample file intentionally read by plugin authors, so the verbosity is appropriate by design — flag only as a reminder that if the matching section in `docs/plugin-author-guide.md` is updated, the XML doc should stay in sync (it is NOT covered by `check-doc-samples.sh`). **Effort:** Trivial maintenance note, not a code change.

## Patterns Avoided (positive notes)

- **Python heredoc in `check-doc-samples.sh`** looks over-engineered at first glance (a bash script embedding a full Python program). It is justified: the regex-based fence extractor and unified-diff output require Python's `re`, `difflib`, and `pathlib` — a pure bash/sed/awk equivalent would be longer and harder to maintain. The heredoc keeps everything in one deployable file with no extra dependencies.

- **Verbose XML docs on all sample plugin types** (`SampleActionPlugin`, `SampleValidationPlugin`, `SampleSnapshotProvider`, `SamplePluginRegistrar`) look like AI bloat on internal types. They are justified: these files are the authoritative tutorial examples that `docs/plugin-author-guide.md` fences verbatim. Plugin authors read the source directly; the XML docs explain the WHY behind each design decision. Removing them would degrade the guide's teaching value.

- **`CapturingLoggerProvider` wrapper around `ConcurrentBag<CapturedEntry>`** (MqttToValidatorTests.cs:295-298) is a one-property class that could be replaced by a bare `ConcurrentBag`. The wrapper is justified because it matches the naming convention of `CapturingLogger<T>` in `TestHelpers` and signals intent clearly to future readers. If extraction to TestHelpers occurs, the wrapper becomes the natural type boundary.

## Coverage

- Files reviewed: ~25 (samples/, templates/, docs/, root README/CONTRIBUTING/SECURITY/CHANGELOG, .github/workflows/docs.yml, .github/scripts/check-doc-samples.sh, tests/FrigateRelay.IntegrationTests/MqttToValidatorTests.cs)
- Plans reviewed: 6/6 (1.1, 2.1, 2.2, 2.3, 2.4, 3.1)
