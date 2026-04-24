# Documentation Report
**Phase:** 3 — FrigateMqtt plugin, EventPump, composition root

## Summary
- CLAUDE.md gaps identified: 5
- No new user-facing docs required
- No architecture doc files exist yet to update

## CLAUDE.md Gaps

### HIGH — MQTTnet v5: no ManagedMqttClient
**Gap:** CLAUDE.md has no MQTTnet guidance. Phase 3 used plain `IMqttClient` + a manual reconnect loop because `ManagedMqttClient` was removed in MQTTnet v5. Any future agent starting from the MQTTnet docs or v4 examples will reach for `ManagedMqttClient` and hit a compile error with no explanation.
**Fix:** Add a one-line note under the `FrigateRelay.Sources.FrigateMqtt` section: `ManagedMqttClient` does not exist in MQTTnet v5 — use `IMqttClient` with an explicit reconnect loop.

### HIGH — `RunAll` must be called pre-`Build()`
**Gap:** CLAUDE.md records the `[SetsRequiredMembers]` and CapturingLogger conventions but not the DI registration ordering constraint. Phase 1's simplification moved `PluginRegistrarRunner.RunAll` post-`Build()`, which broke it; Phase 3 exposed and fixed it. This is a non-obvious pitfall in `Program.cs` with no in-code comment that would stop a future agent from repeating it.
**Fix:** Add to the Architecture invariants or Conventions section: `PluginRegistrarRunner.RunAll` must execute before `builder.Build()` — calling it after produces an already-built container and registrations are silently dropped.

### HIGH — Composition root references concrete plugin assembly
**Gap:** The invariant "Host depends only on Abstractions" is stated, but there is no explanation of where the exception lives. `Program.cs` (the composition root) is the one place that references `FrigateRelay.Sources.FrigateMqtt` directly, by design. Without this note an agent may treat the `Program.cs` reference as a violation and attempt to remove it.
**Fix:** Add a clarifying sentence to the existing "Plugin contracts live in Abstractions" invariant: the composition root (`Program.cs` in `FrigateRelay.Host`) is the intended and sole exception — it references concrete plugin assemblies to wire DI.

### MEDIUM — `run-tests.sh` as canonical test runner
**Gap:** The Commands section still shows bare `dotnet run --project tests/...` invocations and retains the Phase 2 note about considering extraction "when a third project lands." Phase 3 landed that third project and the script was extracted. The note is now stale and the script is not documented.
**Fix:** Replace the two-project `dotnet run` examples with `bash run-tests.sh` (or `.github/scripts/run-tests.sh` — confirm final path), note it discovers test projects via `find`, and remove the "Rule of Three" forward-reference.

### MEDIUM — Config binding shape: top-level `Subscriptions` vs plugin-scoped `FrigateMqtt`
**Gap:** CLAUDE.md states "Config shape is Profiles + Subscriptions (decision S2)" but gives no example of the actual JSON/env-var hierarchy. The `Subscriptions` block is top-level; `FrigateMqtt` (broker, credentials) is plugin-scoped. A developer writing `appsettings.Local.json` has no anchor without reading the source.
**Fix:** Add a minimal skeleton config block (no real values) to the Commands or Architecture section showing the two top-level keys and where plugin config nests.

### LOW — MTP `--coverage-output` env divergence (WSL vs SDK container)
**Gap:** The Jenkinsfile note says "explicit `--coverage-output` IS honored inside the SDK container (verified Phase 2)." Phase 3's `run-tests.sh` includes a fallback copy from `TestResults/` for the WSL case, implying the flag behaves differently outside the container. CLAUDE.md does not record this divergence, which will confuse anyone running coverage locally.
**Fix:** Add a brief note alongside the Jenkinsfile entry: `--coverage-output` is respected in the SDK Docker container but MTP may ignore it under WSL/bare SDK; `run-tests.sh` handles the fallback copy automatically.
