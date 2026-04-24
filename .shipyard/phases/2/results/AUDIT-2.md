# Security Audit — Phase 2

## Severity summary
Critical: 0 | High: 0 | Medium: 1 | Low: 1 | Info: 3

---

## Findings

### MEDIUM

**[M1] Docker image pinned by tag, not digest (Jenkinsfile:32)**

`mcr.microsoft.com/dotnet/sdk:10.0` resolves to whichever layer digest Microsoft
publishes at pull time. A compromised or silently-patched upstream image would be
pulled without any alert. For a hobby repo this is an accepted risk, but it is worth
documenting explicitly.

- **CWE-829** (Inclusion of Functionality from Untrusted Control Sphere)
- **Remediation:** Pin to a specific digest:
  `mcr.microsoft.com/dotnet/sdk:10.0@sha256:<digest>` and update via Dependabot
  once the `docker` ecosystem is added (planned Phase 10). Until then, accept and
  document the risk in `CONCERNS.md`.

---

### LOW

**[L1] RFC-1918 pattern (Pattern 3) matches documentation strings, not just config values**

`192\.168\.[0-9]{1,3}\.[0-9]{1,3}` is broad. It will fire on any prose line that
mentions a private IP — e.g., a README example like "connect to 192.168.1.50". The
current exclusion list (`.shipyard/`, `CLAUDE.md`, fixture) covers known instances,
but any future documentation file that mentions an example IP will break the `scan`
job unexpectedly.

- **Remediation:** Narrow the pattern to match assignment context, e.g.:
  `(server|host|url|address)\s*[=:]\s*["']?192\.168\.[0-9]{1,3}\.[0-9]{1,3}`
  This mirrors the approach already taken for `apiKey` and `Bearer`.

---

### INFO

**[Info-1] Pattern 4 fix verified clean**

`secret-scan.sh:43` — `api[Kk]ey\s*[=:]\s*["'"'"']?[A-Za-z0-9_\-]{20,}`

The shell quoting for the optional-quote character class resolves correctly to the
ERE `["']?`. The fixture exercises both the double-quote and single-quote branches
(lines 27–28 of fixture). Fix from commit `579126e` is confirmed sound.

---

**[Info-2] No ReDoS potential detected**

All seven patterns are linear. None use nested quantifiers or alternation within
repetition. The longest pattern (`Bearer\s+[A-Za-z0-9._\-]{20,}`) has a single
`+` after `\s` and a `{20,}` on a simple character class — both are O(n). No
catastrophic backtracking path exists.

---

**[Info-3] Dependabot auto-merge: not configured**

`dependabot.yml` defines no `auto-merge` key and no `auto-rebase` directive.
All updates will produce PRs requiring manual approval. Confirmed PR-only behavior.

---

## Fixture file review

All seven fixture values are clearly synthetic:

| Pattern | Value shape | Obviously fake? |
|---------|-------------|-----------------|
| AppToken | `abcdefghijklmnopqrstuvwxyz012345` | Yes — sequential alphabet |
| UserKey | `ABCDEFGHIJKLMNOPQRSTUVWXYZ012345` | Yes — sequential alphabet |
| RFC-1918 IP | `192.168.99.99` | Yes — `.99.99` is a sentinel value |
| apiKey (double-quote) | `xK9mR2nP4qT7vL1wY5sA8bD3cF6hJ0eG` | Yes — random-looking but `# secret-scan:fixture` tag |
| apiKey (single-quote) | `zY8nX5mQ3pS6uH2jK9bF1wT4rL7vC0dE` | Yes — same tag |
| Bearer | `eyJhbGciOiJub25lIiwidHlwIjoiSldUIn0.fake.signature` | Yes — `alg:none` JWT, `fake.signature` literal |
| GitHub PAT | `ghp_ABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890` | Yes — sequential; 40 chars (real PATs are 36 hex after prefix) |
| AWS Key ID | `AKIAIOSFODNN7EXAMPLE00` | Yes — `EXAMPLE` infix matches AWS canonical example |

The `# secret-scan:fixture` tag on every line makes intent unambiguous in git log.
The file header warns against replacing values with "more realistic" examples.

**GitHub secret-scanning service interaction:** GitHub Advanced Security scans for
known secret patterns using its own ruleset independent of this script. The fixture
values above will NOT trigger GitHub's secret-scanning alerts because:
- The GitHub PAT value uses sequential characters, not a real PAT format
- The AWS key uses the canonical `AKIAIOSFODNN7EXAMPLE` form that AWS/GitHub
  explicitly allowlist as documentation examples
- `alg:none` JWTs are not in GitHub's secret scanner ruleset

No fixture value represents a plausible real credential.

---

## Regex safety review

| # | Label | Pattern | ReDoS risk | Notes |
|---|-------|---------|-----------|-------|
| 1 | AppToken | `AppToken\s*=\s*[A-Za-z0-9]{20,}` | None | Linear |
| 2 | UserKey | `UserKey\s*=\s*[A-Za-z0-9]{20,}` | None | Linear |
| 3 | RFC-1918 IP | `192\.168\.[0-9]{1,3}\.[0-9]{1,3}` | None | Overly broad (see L1) |
| 4 | Generic apiKey | `api[Kk]ey\s*[=:]\s*["'"]?[A-Za-z0-9_\-]{20,}` | None | Fix verified (Info-1) |
| 5 | Bearer | `Bearer\s+[A-Za-z0-9._\-]{20,}` | None | Linear |
| 6 | GitHub PAT | `ghp_[A-Za-z0-9]{36}` | None | Fixed-length, precise |
| 7 | AWS Key | `AKIA[A-Z0-9]{16}` | None | Fixed-length, precise |

---

## Workflow permissions check

| Workflow | `permissions` declared | Value | Verdict |
|----------|----------------------|-------|---------|
| `ci.yml` | Yes (top-level) | `contents: read` | PASS — least privilege |
| `secret-scan.yml` | Yes (top-level) | `contents: read` | PASS — least privilege |

Both workflows use only `actions/checkout@v4` and `actions/setup-dotnet@v4`
(both official GitHub Actions, maintained by GitHub). No third-party actions present.

GH Actions cache is explicitly NOT enabled in `setup-dotnet@v4` (no `cache:` key).
No persistent cache → no cache-poisoning vector. Confirmed.

---

## Docker image pinning

**Jenkinsfile** uses `mcr.microsoft.com/dotnet/sdk:10.0` (mutable tag). See M1 above.

Jenkins `cleanWs()` runs post-stage unconditionally (`post { always { ... } }`),
ensuring the workspace-local `.nuget-cache` directory is wiped after every run.
No persistent NuGet cache survives between builds → no cache-poisoning vector
on the Jenkins side either.

---

## Recommendations

| Priority | Finding | Location | Effort | Action |
|----------|---------|----------|--------|--------|
| 1 | Pin Docker image by digest | `Jenkinsfile:32` | Small | Add `@sha256:<digest>`; document in `CONCERNS.md` until Phase 10 |
| 2 | Narrow RFC-1918 regex | `secret-scan.sh:43` | Trivial | Prefix pattern with assignment-context anchor |
