# Phase 8 — Legacy INI Sanitization Checklist

**Audience.** The author (project owner) — the only person with the real `FrigateMQTTProcessingService.conf`. Builders and contributors must not perform this step on someone else's behalf; the file content carries operator secrets that no other party should see in unredacted form.

**Why this exists.** Phase 8's `ConfigSizeParityTest` measures the JSON Profiles config against the **real** legacy INI (CONTEXT-8 D3). Synthetic fixtures would pass against a benchmark we control; only your real conf measures whether the rewrite actually solves the operator's problem. This checklist plus the existing `secret-scan.yml` tripwire keep redaction auditable.

**Outcome.** A redacted copy of your `FrigateMQTTProcessingService.conf` placed at the path below, containing zero secrets and zero internal IPs/hostnames, with **structure and verbosity preserved** so the parity comparison is honest.

---

## Step 1 — Where the redacted file goes

```
tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf
```

Exact filename, exact case. The build copies this file to the test output directory; `ConfigSizeParityTest` reads it from `AppContext.BaseDirectory/Fixtures/legacy.conf`. Do not commit the original `FrigateMQTTProcessingService.conf` to the repo under any name.

---

## Step 2 — What to redact

Apply every rule below. The patterns are deliberately broad — when in doubt, redact.

| # | What | Replace with | Rationale |
|---|------|--------------|-----------|
| 1 | Any IPv4 address matching `[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+` | `example.local` (preferred) **or** an RFC 5737 documentation prefix: `192.0.2.x`, `198.51.100.x`, `203.0.113.x` | Hides your LAN topology. CLAUDE.md forbids hard-coded IPs in source — including in fixtures. |
| 2 | Port-bearing hosts like `192.168.0.58:5001` | `example.local:5001` (**preserve the port number**) | Port numbers are part of the legacy URL structure and contribute to char count; preserve them. |
| 3 | `AppToken=<value>` | `AppToken=<redacted>` | Pushover application token — high-value secret. |
| 4 | `UserKey=<value>` | `UserKey=<redacted>` | Pushover user key — identifies you on Pushover's platform. |
| 5 | Any other key matching `*api*key*=…`, `*token*=…`, `*secret*=…` (case-insensitive) | `<redacted>` | Belt-and-suspenders for any field added since the legacy schema was last documented. |
| 6 | URL credentials — `username:password@host` segments | `<user>:<pass>@host` | Basic-auth creds embedded in URLs. |
| 7 | Hostnames that are not `example.local` (e.g. internal DNS names like `homelab.lan`, `pi-frigate.local`, your.dyndns.host) | `example.local` (or a sub-prefix like `frigate.example.local`, `bi.example.local` if multiple distinct hosts existed) | Internal DNS names leak topology and may be guessable. |
| 8 | MAC addresses, serial numbers, device UUIDs (if any appear) | `<redacted>` | Unlikely in this schema, but safe. |

---

## Step 3 — What NOT to change

The 60% character-count gate measures the **real** verbosity of the INI. Modifying anything below makes the comparison dishonest:

- **Camera names.** Keep `front`, `doorbell`, `garage`, etc. exactly as in your real conf.
- **Object labels.** Keep `person`, `car`, `dog`, etc.
- **Zone names.** Keep `Driveway`, `FrontPorch`, `Backyard`, etc.
- **Subscription structure.** All 9 (or however many) `[SubscriptionSettings]` sections must remain. Do not collapse or deduplicate them — that's literally what Phase 8 is being measured for.
- **Whitespace.** Indentation, blank lines between sections, trailing newlines — preserve byte-for-byte.
- **Comments.** Preserve INI comments (`; ...` lines). They count toward the char total because they are part of the operator's real config burden.
- **Field ordering.** Don't reorder fields within a section.
- **Trailing newline.** Preserve the file's final `\n` (or absence thereof).

If your real conf has `CameraShortName` repeated 9 times with the same prefix, the redacted copy must too. The parity test is not gamed by leaving repetition in.

---

## Step 4 — Pre-commit verification

Run **every** command below from the repo root **before** `git add tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf`. Each must produce empty output (exit code 0 and no matches printed).

```bash
# 1. No real-world IPv4 addresses (RFC 5737 prefixes are allowed).
git grep -nE '([0-9]{1,3}\.){3}[0-9]{1,3}' tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf \
  | grep -vE '(^|[^0-9])(192\.0\.2\.|198\.51\.100\.|203\.0\.113\.)'
# expect: empty

# 2. No RFC 1918 private IPs (192.168.x.x, 10.x.x.x, 172.16-31.x.x).
git grep -nE '192\.168\.[0-9]{1,3}\.[0-9]{1,3}|10\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}|172\.(1[6-9]|2[0-9]|3[01])\.[0-9]{1,3}\.[0-9]{1,3}' \
  tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf
# expect: empty

# 3. No long alphanumeric secrets (Pushover-shaped tokens, generic API keys).
git grep -nE 'AppToken[[:space:]]*=[[:space:]]*[A-Za-z0-9]{20,}' tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf
git grep -nE 'UserKey[[:space:]]*=[[:space:]]*[A-Za-z0-9]{20,}' tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf
git grep -nE 'api[a-z0-9]{28,}' tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf
# expect: empty (each)

# 4. No URL-embedded basic-auth (username:password@host).
git grep -nE '://[^:/[:space:]]+:[^@[:space:]]+@' tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf
# expect: empty

# 5. The repo-wide secret scanner agrees the file is clean.
bash .github/scripts/secret-scan.sh
# expect: exit 0
```

If any command produces output, redact further and re-run. Do not commit a file with non-empty output from any of the above.

---

## Step 5 — What the test does when the fixture is missing

If you forget to place `legacy.conf` (or it gets cleaned out of a fresh checkout), the test fails with **this exact message** (per CONTEXT-8 D9):

> `legacy.conf fixture missing at <path>. Sanitize your real FrigateMQTTProcessingService.conf per .shipyard/phases/8/SANITIZATION-CHECKLIST.md and place the redacted result at the path above. This test cannot run without it.`

There is **no** `Assert.Inconclusive` skip path. The test fails identically in CI and on local dev runs — by design (D9). When you see this message, return to Step 1.

---

## Step 6 — Commit hygiene

- Commit `legacy.conf` and `config/appsettings.Example.json` in the **same commit** as `ConfigSizeParityTest.cs` so the gate is unconditional from the moment it lands.
- Reference Phase 8 in the commit message (`shipyard(phase-8): legacy.conf fixture + parity test`).
- Do **not** include the original `FrigateMQTTProcessingService.conf` in the working tree at any point during the commit. Sanitize in a scratch directory outside the repo, then copy the redacted result to the fixture path.
- Do **not** rebase a commit that reverts a sanitization step — `git log -p tests/FrigateRelay.Host.Tests/Fixtures/legacy.conf` should show one initial add and only sanitized diffs thereafter.

---

## Quick reference — minimal redacted INI shape

A redacted `[ServerSettings]` block should look like:

```ini
[ServerSettings]
Server = example.local
BlueIrisImages = http://example.local:81/image/
FrigateApi = http://example.local:5000

[PushoverSettings]
AppToken = <redacted>
UserKey = <redacted>
NotifySleepTime = 30
```

Subscription blocks keep their structure intact:

```ini
[SubscriptionSettings]
Name = Frontyard
Camera = http://example.local:81/json?...
CameraShortName = front
Zone = Driveway
Label = person
```

— Camera names, zones, labels: real. Hostnames and tokens: redacted. Whitespace and field order: untouched.
