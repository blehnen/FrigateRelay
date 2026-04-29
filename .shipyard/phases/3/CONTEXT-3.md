# Phase 3 — Context & Decisions

Captured during `/shipyard:plan 3` discussion capture. Phase 3 scope from ROADMAP.md lines 95–119: stand up the Frigate MQTT event source, `EventContext` projection, subscription matcher, and dedupe cache. No downstream actions yet — events terminate at a logged "matched" line.

## Scope reminder (from ROADMAP.md Phase 3)

Deliverables:
- `src/FrigateRelay.Sources.FrigateMqtt/` project with: `FrigateMqttEventSource : IEventSource`, `FrigateMqttOptions` (server, port, client-id, topic, TLS), `FrigatePayload` DTOs (`FrigateEvent`, `FrigateEventBefore`, `FrigateEventAfter`), `SubscriptionMatcher`, `DedupeCache` wrapping `IMemoryCache`.
- `FrigateMqtt.PluginRegistrar : IPluginRegistrar` registering the event source and binding options from the `FrigateMqtt` config section.
- MQTTnet v5 `ManagedMqttClient` with auto-reconnect and per-plugin TLS opt-in (`AllowInvalidCertificates`) scoped to this plugin's client — **no global `ServicePointManager` callback**.
- `tests/FrigateRelay.Sources.FrigateMqtt.Tests/` ≥ 15 passing tests covering: payload deserialization (new/update/end), subscription match on camera+label, zone-filter match in the four zone arrays (`before.current_zones`, `before.entered_zones`, `after.current_zones`, `after.entered_zones`), stationary-guard skip on `update`/`end`, dedupe cache hit/miss with configurable TTL.
- Host wiring so a sample `appsettings.Local.json` with one subscription produces "matched event" log lines on a hand-crafted payload.

Success criteria:
- `dotnet test tests/FrigateRelay.Sources.FrigateMqtt.Tests` ≥ 15 passing.
- Local `docker run eclipse-mosquitto` + `mosquitto_pub -t frigate/events -m '<payload>'` produces exactly one matched-event log line per configured-subscription match (author-verified, not CI-gated per D4 below).
- Graceful shutdown: SIGINT → "MQTT disconnected" log within 5 seconds, exit 0.
- `git grep -n "ServerCertificateValidationCallback" src/` returns 0.

## Decisions

### D1 — **Fire ALL matching subscriptions, not first-match-wins** (deviates from legacy)

Legacy behavior (`.shipyard/codebase/ARCHITECTURE.md` step 4): a `break` after the first matching subscription. Overlapping configs were order-dependent, with only the first winning — a latent footgun the author had to manage by hand.

New behavior: evaluate every subscription in configuration order; any that matches fires its action list. Each matching subscription has its own dedupe bucket (per-subscription cooldown — already aligns with ROADMAP's key shape).

**Why.** Cleaner mental model; matches what most event-driven systems do; eliminates order-dependent configuration footguns; the per-subscription dedupe prevents notification flooding if multiple subs legitimately match.

**Phase 12 parity implication.** This is a **material behavior deviation** from the legacy service. The parity cutover (Phase 12) cannot simply compare alert counts one-for-one — it needs to separately verify:
- For every legacy alert, at least one corresponding FrigateRelay alert fires. (No missed alerts.)
- If FrigateRelay fires MORE alerts than legacy, every additional alert must trace to a subscription that would have matched legacy's second-place "loser" — the user must review these and decide whether they were legitimately deduplicated by the legacy `break` or whether they represent actual new noise.

Phase 12 docs + `docs/migration-from-frigatemqttprocessing.md` must **explicitly state this change** and walk through how to convert legacy INI (which relied on `break` ordering) into JSON (which needs explicit non-overlapping sub filters or the acceptance of extra notifications).

**Rejected:**
- *First-match-wins* — preserves parity but perpetuates the legacy footgun. Phase 12 cutover is easier; user experience is worse.

### D2 — **`EventContext.RawPayload` stays `string`** (no contract change)

Contract defined in PLAN-2.1 is unchanged. Frigate MQTT source stashes the raw UTF-8 JSON string from the `MqttApplicationMessage.PayloadSegment`.

**Why.** YAGNI: no planned Phase 3 / 4 / 5 consumer reads `RawPayload` — every downstream concern (zones, label, camera, event id, stationary) is top-level on `EventContext`. Upgrading to `JsonElement?` couples the Abstractions assembly to `System.Text.Json` (a widening) and introduces `JsonDocument` lifetime traps. The upgrade is speculative.

**Rejected:**
- *`JsonElement?`* — parse-once optimization for a consumer that doesn't exist.
- *`string + IReadOnlyDictionary<string,object?> Extra` side-channel* — extra indirection without a caller.

If a future consumer genuinely needs structured access, the revisit cost is small — the field can be augmented (new optional typed view, retaining `string` for compatibility) without breaking v1.

### D3 — **`EventContext.SnapshotFetcher`: no-op returning `null`** (revised post-research)

**Original D3** said: extract base64 thumbnail from the `frigate/events` payload when present. Research found this premise false: the official Frigate documentation (2026-04-24 snapshot at `https://docs.frigate.video/integrations/mqtt/`) explicitly states `thumbnail: null` — "Always null in published messages." Thumbnails are published on separate per-camera MQTT topics (e.g. `frigate/<camera>/<label>/thumbnail`), not embedded in event payloads. The original D3's decode path was dead code.

**Revised D3.** `FrigateMqttEventSource` builds `SnapshotFetcher` as a delegate returning `ValueTask.FromResult<byte[]?>(null)`. Snapshot fetching moves wholly to Phase 5's `ISnapshotProvider` plugins (`BlueIrisSnapshot` for Blue Iris's `/image/<cam>`, `FrigateSnapshot` for Frigate's `/api/events/<id>/snapshot.jpg`).

**Why.** Honours the reality of the wire format. Keeps Phase 3 HTTP-free. `SnapshotFetcher` on `EventContext` is effectively vestigial in v1 — **flag for simplifier review at Phase 5** whether to remove the field from the Abstractions contract entirely (it was added speculatively in PLAN-2.1). If removed, the Phase 5 `SnapshotResolver` is the only snapshot path; cleaner model.

**Rejected:**
- *Subscribe to thumbnail topics in Phase 3* — expands scope; duplicates in-memory JPEG caching logic that Phase 5 snapshot providers will own via a different mechanism (HTTP).
- *Dead-code base64 decode path against a future Frigate version* — speculative; no specific signal about an upcoming change.

**Implication for Phase 5.** The Phase 5 `SnapshotResolver` does NOT need to fall back to `EventContext.SnapshotFetcher`. Precedence is simply: per-action override → per-subscription default → global default. If none configured, snapshot is omitted (actions that need one fail gracefully or log at Warning).

### D5 — **Add a `false_positive` skip alongside the stationary guard** (small deviation from legacy)

Frigate events carry `after.false_positive` (bool). Legacy code did NOT skip these — it relied on downstream CodeProject.AI validation (disabled in legacy) to filter them. In the new code, CodeProject.AI is Phase 7; we want a cheap upstream guard so bogus events don't waste dispatcher capacity in Phase 4 or pass through when CodeProject.AI isn't configured.

**Rule.** On event `type ∈ {update, end}`, skip if `after.stationary == true` OR `after.false_positive == true`. On `type == new`, always proceed (legacy parity on `new` events is preserved).

**Phase 12 parity implication.** If the parity window surfaces a case where legacy fired but FrigateRelay did not, and the Frigate event carried `after.false_positive == true`, the discrepancy is explained by this guard — not a bug. Phase 12 parity docs must state this.

**Rejected:**
- *Omit the guard (strict legacy parity)* — keeps known-noisy events flowing through the dispatcher. Legacy's absence of this filter is closer to a bug than a feature; the Phase 12 docs already cover multiple intentional improvements (D1, for example).

### D4 — **Testcontainers deferred to Phase 4** per ROADMAP. Phase 3 tests are unit tests only.

Phase 3 tests use:
- `NSubstitute` stubs for the underlying MQTT client's event-publishing shape.
- Hand-crafted UTF-8 byte payloads fed directly to the `FrigateMqttEventSource`'s internal message-handler entry point (the public surface for testing).
- `IMemoryCache` created via `new MemoryCache(new MemoryCacheOptions())` — scoped, not `MemoryCache.Default` (CLAUDE.md invariant).

The ROADMAP's success criterion #2 (`docker run eclipse-mosquitto` + `mosquitto_pub`) is a **manual smoke test by the author**, not a CI-gated check. Document the command recipe in SUMMARY so future contributors can reproduce.

**Rejected:**
- *One Testcontainers smoke test in Phase 3* — tempting but duplicates Phase 4's `FrigateRelay.IntegrationTests` project scaffolding. Phase 4 uses Testcontainers for the full dispatcher slice; doing it once there is cleaner than twice split across phases.
- *Full Testcontainers integration suite in Phase 3* — significant scope bulge; overlaps with Phase 4's stated deliverables.

## Implementation notes (for the architect)

- **Frigate MQTT payload shape** is snake_case at the wire: `type`, `before`, `after`, `current_zones`, `entered_zones`, `stationary`. Use `JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower` (.NET 9+, available on .NET 10) OR `[JsonPropertyName]` attributes per field. Policy approach is less verbose.
- **`FrigatePayload` DTOs are internal to the `FrigateRelay.Sources.FrigateMqtt` project.** They never leak into `EventContext`. Use `InternalsVisibleTo` for the Tests project, same pattern as Phase 1's Host.
- **`EventContext.RawPayload`** — UTF-8 decode the `MqttApplicationMessage.PayloadSegment` (`ReadOnlySpan<byte>`) into a `string` once per message. Not lazy; the source owns one allocation per message.
- **MQTTnet v5 MQTT client** — **correction**: `ManagedMqttClient` does not exist in MQTTnet v5 (ROADMAP Phase 3 text is stale). Use plain `IMqttClient` (via `MqttClientFactory.CreateMqttClient()`) plus a custom reconnect loop: on `DisconnectedAsync` event, wait 5s (matching legacy ROADMAP cadence) and call `ConnectAsync` again, cancelled by the plugin's stopping token. Per-plugin TLS callback lives on `MqttClientOptionsBuilder.WithTlsOptions(...)` — not global. Per-plugin scope: one client instance owned by the plugin's `FrigateMqttEventSource`, never shared across plugins.
- **`FrigateMqttEventSource` + `IAsyncEnumerable<EventContext>`** — the MQTT client is push-based; `IAsyncEnumerable` is pull-based. Bridge via `System.Threading.Channels.Channel<EventContext>` with an unbounded-or-small-bounded config; on MQTT disconnect (during shutdown), complete the channel writer cleanly so the async-enumerator terminates and callers see a normal end-of-stream.
- **`SubscriptionMatcher`** — evaluates each configured `Subscription` against an `EventContext`:
  - Camera (case-insensitive equality).
  - Label (case-insensitive equality).
  - Zone (empty ⇒ match-any; non-empty ⇒ must appear in any of the four zone arrays).
  - Stationary guard: on event `type` ∈ {`update`, `end`}, skip if `data.after.stationary == true`. On `type == new`, always proceed.
  - Returns an `IReadOnlyList<Subscription>` of ALL matches (D1).
- **`DedupeCache`** — per-subscription bucket keyed on `(SubscriptionName, Camera, Label)`; TTL configurable per subscription via `CooldownSeconds`. `IMemoryCache` instance is DI-scoped to the plugin; NOT `MemoryCache.Default`.
- **Matched-event log line**: one Information-level entry per (matching subscription, event) tuple. Format: `"Matched event: subscription={Sub}, camera={Camera}, label={Label}, event_id={Id}"` via `LoggerMessage.Define` (allocation-free; this is a hot path).
- **`git grep ServerCertificateValidationCallback` in src/ must remain empty** — not just "no matches"; the invariant is structural. Use `MQTTnet`'s `TcpOptions.SslOptions.RemoteCertificateValidationCallback` only, and only when `AllowInvalidCertificates: true`.

## Non-goals for Phase 3 (explicit)

- No action dispatcher — events terminate at `"matched event"` log lines. (Phase 4.)
- No snapshot providers. (Phase 5.)
- No validators. (Phase 7.)
- No durable queue / Polly / Channel-based retry pipeline. (Phase 4.)
- No profiles in config. (Phase 8.)
- No observability (OTel, Serilog sinks). (Phase 9.)
- No Docker. (Phase 10.)

## Success-criteria reminders (from ROADMAP.md Phase 3)

- `dotnet test tests/FrigateRelay.Sources.FrigateMqtt.Tests` reports **≥ 15** passing tests.
- Manual local smoke: `docker run -p 1883:1883 eclipse-mosquitto` + host run + `mosquitto_pub -t frigate/events -m '<sample>'` produces exactly one matched-event log line per match (D1: may be multiple if multiple subs match).
- SIGINT during active subscription → "MQTT disconnected" log within 5 seconds; process exits 0.
- `git grep -n "ServerCertificateValidationCallback" src/` returns zero matches.
