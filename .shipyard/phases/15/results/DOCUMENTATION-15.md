# Phase 15 Documentation Review

**Type:** Explanation (phase report)
**Phase:** 15 — security hardening patch (v1.2.1)

## Verdict: GAPS_NON_BLOCKING

---

## Gaps Identified

### Operator-facing

**GAP-1 (Priority 1) — Name allowlist not documented (#19)**
`docs/observability.md` and `README.md` both describe configuration keys (subscription names,
profile names, plugin names, validator keys) but neither mentions that these names are now
validated against the allowlist `^[A-Za-z0-9_. -]+$` at startup. Operators who migrate a
config with names containing slashes, colons, at-signs, or other punctuation will hit a
startup rejection with no prior warning in the documentation. The CHANGELOG entry covers the
fact of the restriction, but no doc tells operators what characters are accepted vs. rejected.

The README's Configuration section (around the `Profiles` / `Subscriptions` example) is the
natural place for a single-sentence callout: "Names (subscription, profile, plugin, validator)
must match `^[A-Za-z0-9_. -]+$`; spaces, dots, hyphens, and underscores are allowed."

**GAP-2 (Priority 2) — OTLP endpoint scheme restriction not in observability doc (#20)**
`docs/observability.md` §"How to enable OTLP export" shows valid `http://` and `grpc://`
examples but does not state that any other scheme (e.g., `file://`, `ftp://`) is rejected at
startup. Before Phase 15, a bad scheme produced a cryptic `ArgumentException` from inside the
OTLP exporter at first flush — an operator reading the observability doc had no way to know
this was validated early. The doc should note, adjacent to the `OtlpEndpoint` examples, that
accepted schemes are `http`, `https`, and `grpc`; other schemes produce a structured startup
diagnostic.

**GAP-3 (Priority 3) — Windows Serilog path restriction undocumented (#27)**
`docs/observability.md` covers the Seq log sink but does not document the Serilog file-sink
configuration or its startup validation. Operators on Windows who configure
`Serilog:WriteTo:*:Args:path` to a Windows-rooted path (e.g., `C:\logs\frigate.log`) will
receive a startup rejection with no prior documentation. Because this is a Windows-only
restriction enforced at startup, it is likely to be surprising. A note in the observability
doc or README config section — "On Windows hosts, `Serilog:WriteTo:*:Args:path` must be a
relative path; Windows-rooted absolute paths (e.g., `C:\logs\...`) are rejected at startup" —
would prevent confusion.

**GAP-4 (Priority 4) — Empty plugin name behavior not mentioned in config docs (#14)**
The README's Configuration section describes the `["BlueIris"]` shorthand and the object form
for `Subscriptions:N:Actions`. It does not state that an empty or whitespace-only plugin name
in either form is rejected at the converter boundary with a `FormatException`. This is a
minor gap — operators who supply empty names are likely already making a mistake they would
notice — but a one-liner in the startup-validation section would be complete.

### Contributor-facing

**GAP-5 (Non-blocking, deferred) — Secret-scan tripwire mechanism not in CONTRIBUTING.md**
`CONTRIBUTING.md` mentions the secret-scan CI job in the PR checklist ("No hard-coded IPs/hostnames
or secrets") but does not explain the tripwire self-test mechanism. Contributors who add a new
secret-shaped pattern to `secret-scan.sh` must also add a matching fixture line to
`secret-scan-fixture.txt`, or the tripwire job silently rots. This is Phase 16 / future scope
material — Phase 15 extended the fixture; the process is captured in the SUMMARY but not in
contributor docs. Not blocking.

---

## Suggested Updates (priority order)

1. **README.md — Configuration section**: add one sentence after the `Profiles`/`Subscriptions`
   example stating the name allowlist regex and the character classes allowed/rejected. This is
   the highest-traffic operator touchpoint.

2. **docs/observability.md — "How to enable OTLP export" section**: add one sentence after the
   `OtlpEndpoint` examples noting that the scheme is validated at startup and only `http`,
   `https`, and `grpc` are accepted; other schemes produce a structured diagnostic before the
   host starts.

3. **docs/observability.md — new "Serilog file sink" subsection** (or note in existing Seq
   section): document the `Serilog:WriteTo:*:Args:path` key, note that relative paths are
   recommended, and that Windows-rooted absolute paths are rejected on Windows hosts at startup.

4. **README.md or docs/observability.md — one-liner for #14**: in whichever section discusses
   `Subscriptions:N:Actions`, note that empty or whitespace-only plugin names are rejected at
   the converter boundary.

5. **CONTRIBUTING.md (future scope)**: add a "Secret-scan tripwire" subsection explaining that
   new patterns in `secret-scan.sh` require a matching fixture line in
   `secret-scan-fixture.txt`, and that `tripwire-self-test` enforces this automatically.

---

## Already Documented

- **CHANGELOG.md** — all 10 Phase 15 IDs present under `[Unreleased]` with adequate context:
  CWE numbers cited for security items (#13 CWE-117, #19 CWE-117, #20 CWE-183, #27 CWE-22);
  fixed items (#8, #14) self-explanatory; documentation items (#25, #26) brief but clear;
  CI/supply-chain items (#15, #24) accurate. CHANGELOG is complete and derivable release notes
  are available.

- **docker/mosquitto-smoke.conf** — WARNING header added in-file (#25). Operators copying the
  file see the CI-only caveat immediately. No additional doc needed.

- **docker/docker-compose.example.yml** — inline comment recommending `127.0.0.1` binding for
  untrusted networks (#26). Sufficient; the README Docker section's `docker compose -f ...`
  invocation naturally points operators to the compose file where the comment lives.

- **Internal changes (#8, #13, #15, #24)** — no operator doc required. Contributor-facing
  behavior (run-tests.sh fix, SHA-pinning, secret-scan fixture) is either self-evident or
  captured in CONTRIBUTING.md's PR checklist adequately for current scope.

---

## Notes

- The `docs/migration-from-frigatemqttprocessing.md` §"Validation gate" already explains
  startup validation collects all errors; no update needed there for Phase 15 changes.
- The observability doc's OTLP examples use `http://` and `grpc://` — both valid under the new
  allowlist. No examples need changing; only a note needs adding.
- GAP-3 (Windows path) may be low operator-impact given the project's Docker-first deployment
  model, but the Windows host path is explicitly supported and the restriction is surprising
  enough to warrant documentation.
