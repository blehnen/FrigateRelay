# Build Summary: Plan 2.1

## Status: complete (gRPC scope reverted post-initial-build — see "Reversal Addendum" at bottom)

## Tasks Completed

- **Task 1: Vendor `.proto` + scaffold csproj with gRPC codegen** — complete — files: `src/FrigateRelay.Plugins.Doods2/{FrigateRelay.Plugins.Doods2.csproj,Protos/odrpc.proto}` + sln entry. Commit `dbc9588`.
- **Task 2: `Doods2Options` + `Doods2Response` DTOs + `Doods2Validator` (HTTP + gRPC paths)** — complete — files: `src/FrigateRelay.Plugins.Doods2/{Doods2Options.cs,Doods2Response.cs,Doods2Validator.cs}`. Commit `bf59ca6`.
- **Task 3: `PluginRegistrar` + DI wiring in `HostBootstrap.cs:135` + `Host.csproj` ProjectReference** — complete. Commit `1963657`.

## Files Modified

- `src/FrigateRelay.Plugins.Doods2/FrigateRelay.Plugins.Doods2.csproj` (created) — same package set as Roboflow's csproj plus `Grpc.Net.Client 2.66.0`, `Google.Protobuf 3.28.0`, `Grpc.Tools 2.66.0` (with `PrivateAssets=all`). `<Protobuf Include="Protos\odrpc.proto" GrpcServices="Client" />` triggers MSBuild codegen at build time. `<InternalsVisibleTo Include="FrigateRelay.Plugins.Doods2.Tests" />`.
- `src/FrigateRelay.Plugins.Doods2/Protos/odrpc.proto` (created) — vendored from upstream `snowzach/doods/odrpc/rpc.proto` at commit `34f4f0700b81e2289c2e4bd8c47ce93c5f415756` (2022-01-02), MIT-licensed (Copyright 2018 Zach Brown). Cleaned for .NET Grpc.Tools compatibility — see "Decisions Made" below.
- `src/FrigateRelay.Plugins.Doods2/Doods2Options.cs` (created) — `[Required, Url] BaseUrl`, `Doods2Transport Transport = Http`, `string DetectorName = "default"`, `[Range(0.0, 1.0)] MinConfidence = 0.5`, `string[] AllowedLabels = []`, `Doods2ValidatorErrorMode OnError = FailClosed`, `[Range] Timeout = 5s`, `bool AllowInvalidCertificates`. Two enums: `Doods2Transport { Http, Grpc }` and `Doods2ValidatorErrorMode { FailClosed, FailOpen }`.
- `src/FrigateRelay.Plugins.Doods2/Doods2Response.cs` (created) — HTTP DTOs (`Doods2DetectRequest`, `Doods2DetectResponse`, `Doods2Detection`).
- `src/FrigateRelay.Plugins.Doods2/Doods2Validator.cs` (created) — `IValidationPlugin` with both HTTP and gRPC paths. Both transport clients (`HttpClient` + `odrpc.odrpcClient`) constructor-injected; `ValidateAsync` branches on `Doods2Options.Transport` at call time. Catch-block ordering preserved (`OperationCanceledException when ct.IsCancellationRequested` first → `RpcException` for gRPC + `TaskCanceledException` for HTTP → `HttpRequestException` + `JsonException` for HTTP).
- `src/FrigateRelay.Plugins.Doods2/PluginRegistrar.cs` (created) — clones Roboflow's pattern. Registers per-instance: named options + named HttpClient (with TLS-skip option) + singleton `GrpcChannel`-backed `odrpcClient` + keyed `IValidationPlugin`. Both transport clients always built per instance — keeps registrar branch-free.
- `FrigateRelay.sln` — added the new project entry.
- `src/FrigateRelay.Host/FrigateRelay.Host.csproj:44` — added `<ProjectReference Include="..\FrigateRelay.Plugins.Doods2\..." />`.
- `src/FrigateRelay.Host/HostBootstrap.cs:135` — added `registrars.Add(new FrigateRelay.Plugins.Doods2.PluginRegistrar());` inside the existing `Validators`-section gate alongside CPAI (#13) and Roboflow (PR #42).

## Decisions Made

- **`Type` discriminator: `"Doods2"`** (case-sensitive `Ordinal` match, mirrors CPAI/Roboflow pattern).
- **Transport: `Doods2Transport` enum (`Http` | `Grpc`), default `Http`.** Operator picks per validator instance via `Validators:<key>:Transport`. HTTP is the default for least-friction (matches the rest of the validator family).
- **EventId range 7201–7299 for `LoggerMessage`s** (Roboflow owns 7100s, CPAI owns 7000s). Picked 7201 (timeout) and 7202 (unavailable). Will need to add gRPC-specific event IDs (e.g. 7203 for `RpcException` mapping) at PLAN-2.3 time if needed.
- **DOODS2 confidence is 0–100 on the wire; normalized by ÷100 in `EvaluatePredictions`** before comparing to `MinConfidence` (operator config stays 0.0–1.0). Documented in `Doods2Validator` XML doc-comment.
- **`DetectorName` defaults to `"default"`** — the live DOODS2 server orchestrator probed has three detectors: `default` (TFLite mobilenet, COCO labels), `tensorflow` (Faster R-CNN, COCO labels), `pytorch` (YOLOv5s, COCO labels). All three carry the standard COCO 80-label set.
- **Single `GrpcChannel` per validator instance lifetime (singleton).** `GrpcChannel` is thread-safe and HTTP/2-multiplexed; reuse is correct. Documented that gRPC channel does NOT honor the per-handler TLS-skip option (gRPC users with self-signed certs should terminate TLS at a sidecar or run plaintext h2c).
- **No API key surface** — DOODS2 self-hosted has no auth.
- **Both transport clients are always wired in DI**, even though only one is invoked per call. This keeps the registrar branch-free and the validator constructor stable across config permutations. Cost: a `GrpcChannel` allocation per validator instance even when Transport=Http (negligible).

## Proto cleanup applied (per PLAN-2.1 spec + builder prompt)

The upstream proto uses Go-specific extensions incompatible with .NET's `Grpc.Tools` default install. Removed:

1. `import "google/api/annotations.proto";` — grpc-gateway HTTP-transcoding hints, only used by the original Go server.
2. `import "github.com/gogo/protobuf/gogoproto/gogo.proto";` — Go-specific.
3. All `option (google.api.http) = { ... };` blocks inside service rpc definitions.
4. All `[(gogoproto.casttype) = "..."]` and `[(gogoproto.jsontag) = "..."]` field options.
5. `option go_package = "...";` line.

Added:
- `option csharp_namespace = "FrigateRelay.Plugins.Doods2.Grpc";` so the generated client lands in a predictable namespace.
- File-header comment block citing source repo URL, upstream commit SHA `34f4f0700b81e2289c2e4bd8c47ce93c5f415756` (2022-01-02), MIT license attribution to "Copyright (c) 2018 Zach Brown <zach@prozach.org>", and that this is a derivative work cleaned for .NET Grpc.Tools.

Result: 4 messages preserved (`DetectRequest`, `DetectResponse`, `Detection`, `DetectRegion`, `Detector`, `GetDetectorsResponse`); service `odrpc` with `Detect` rpc preserved; `DetectStream` (bidirectional streaming) intentionally kept in case future work wants it. Codegen produces `odrpc.odrpcClient` typed client at build time.

## Issues Encountered

### IMPORTANT — Architectural invariant from RESEARCH §4.2 needs revision

PLAN-2.1's verification spec (and the broader RESEARCH §4.2 / CONTEXT-14 D4 invariant) said:

```bash
dotnet list src/FrigateRelay.Host/FrigateRelay.Host.csproj package --include-transitive | grep -E 'Grpc\.' || echo "PASS"
```

This is **structurally impossible to satisfy** with build-time DI (CONTEXT-1 decision A). Host ProjectReferences `FrigateRelay.Plugins.Doods2`, which has runtime PackageReferences for `Grpc.Net.Client` + `Google.Protobuf`. Those propagate transitively. After this commit:

```
$ dotnet list src/FrigateRelay.Host/FrigateRelay.Host.csproj package --include-transitive | grep -E 'Grpc\.'
   > Grpc.Core.Api          2.66.0
   > Grpc.Net.Client        2.66.0
   > Grpc.Net.Common        2.66.0
```

This is an **unavoidable consequence of build-time DI** — the host needs the Grpc.* runtime DLLs in its publish output for the validator to actually work at runtime. Marking the gRPC packages with `<PrivateAssets>runtime</PrivateAssets>` would block runtime propagation but cause `MissingMethodException` at first gRPC call. The `Grpc.Tools` package IS marked `<PrivateAssets>all</PrivateAssets>` because it's build-time-only (codegen).

**The MEANINGFUL invariant is source-level coupling cleanness:**

```bash
$ git grep -nE 'Grpc\.' src/FrigateRelay.Host src/FrigateRelay.Abstractions
# (empty — PASS)
```

This proves:
- Host code does not `using Grpc.*` types (no source coupling).
- Abstractions assembly does not reference Grpc.* (the contract surface stays pure `Microsoft.Extensions.*`).
- Future plugin authors can write HTTP-only plugins without touching gRPC.

**Action required for PLAN-2.3 builder:** the verification block in PLAN-2.3.md likely repeats the `dotnet list package --include-transitive` test. Update it to test only:
- `git grep -nE 'Grpc\.' src/FrigateRelay.Host src/FrigateRelay.Abstractions` (must be empty)
- `dotnet list src/FrigateRelay.Abstractions/...csproj package --include-transitive | grep -E 'Grpc\.'` (must be empty — abstractions is pure)

The host transitive grep should be removed or downgraded to "no `Grpc.*` Direct package references in `FrigateRelay.Host.csproj` — only via `FrigateRelay.Plugins.Doods2`". The orchestrator captured this in commit `1963657`'s message and will surface to the user before PLAN-2.3.

### Builder sandbox interruptions

Same pattern as Wave 1: builder agent ran out of internal turns mid-task. Specifically:
- Builder finished Tasks 1+2 with atomic commits (`dbc9588`, `bf59ca6`) — good.
- Stopped before writing PluginRegistrar.cs (Task 3).
- Orchestrator wrote `PluginRegistrar.cs`, edited `HostBootstrap.cs:135`, edited `Host.csproj`, ran build + invariant greps, committed Task 3 (`1963657`), and wrote this SUMMARY.

**Lesson seed:** for Phase 15+, dispatch builders with a smaller per-task scope. PLAN-2.1 had 3 tasks each touching 2-3 files; the builder couldn't fit all 3 in one context budget. Splitting into PLAN-2.1a (proto+csproj) and PLAN-2.1b (validator+registrar+DI) might be the right shape.

### Live DOODS2 v2 probe (orchestrator)

- `GET http://192.168.0.2:10200/` → HTTP 200 (server alive)
- `GET /detectors` → 200 with 3 detectors (`default` TFLite, `tensorflow` Faster R-CNN, `pytorch` YOLOv5s); all return COCO 80-label set
- `/health` → 404 (DOODS2 v2 doesn't expose `/health`; rely on `/detectors` for liveness)
- gRPC port not verified — DOODS2 v2 (Python rewrite) **may NOT expose gRPC at all**. The Go-based `snowzach/doods` had gRPC; the Python `snowzach/doods2` rewrite is HTTP-only. Operator config should default to Transport=Http; if gRPC fails to connect at runtime against this server, the validator's `RpcException` catch routes through `OnError` (FailClosed=reject, FailOpen=allow). **Flag for the user:** if they want gRPC for DOODS2, they may need the Go-based server, not the Python one.

## Verification Results

- `dotnet build FrigateRelay.sln -c Release` — **0 warnings, 0 errors** (18.9s elapsed).
- `bash .github/scripts/run-tests.sh --skip-integration` — **258/258 passing** (test-count unchanged; PLAN-2.1 ships no tests, that's PLAN-2.2 + 2.3).
- `git grep -nE 'Grpc\.' src/FrigateRelay.Host src/FrigateRelay.Abstractions` — empty ✓ (the meaningful gRPC dep-containment invariant).
- `dotnet list src/FrigateRelay.Abstractions/...csproj package --include-transitive | grep -E 'Grpc\.|Google\.Protobuf'` — empty ✓ (abstractions stays pure).
- `dotnet list src/FrigateRelay.Host/...csproj package --include-transitive | grep -E 'Grpc\.|Google\.Protobuf'` — **NOT empty** (4 transitive entries). See "Issues Encountered" above for analysis. Not a regression; spec was unrealistic.
- `git grep -nE 'App\.Metrics|OpenTracing|Jaeger\.' src/` — empty ✓.
- `git grep -nE '\.(Result|Wait)\(' src/` — empty ✓.
- `git grep -n 'ServicePointManager' src/` — only doc-comment "never use" references ✓.
- 3 atomic commits on `feature/14-doods2-validator`: `dbc9588` (proto+csproj), `bf59ca6` (Options+Response+Validator), `1963657` (Registrar+DI).

## Next: PLAN-2.2 only (PLAN-2.3 REMOVED — see Reversal Addendum)

Wave 2 builder for PLAN-2.2 targets ~9 HTTP-path tests covering the full validator contract (allow / reject-low-confidence / reject-bad-label / no-snapshot / timeout-FailClosed/Open / unavailable-FailClosed/Open / cancellation), absorbs PLAN-2.3's CHANGELOG bullet responsibility, and is the only remaining plan in Wave 2.

---

## Reversal Addendum (2026-05-06)

After the initial PLAN-2.1 build shipped (commits `dbc9588`, `bf59ca6`, `1963657`, `d027799`), the user pointed out that DOODS2 v2 (the Python rewrite at `snowzach/doods2`) is HTTP-only — the orchestrator's prior `/detectors` probe against the live server at `192.168.0.2:10200` confirmed this, but the implication wasn't drawn until that prompt. Verified with the upstream README:

> "DOODS2 drops support for gRPC as I doubt very much anyone used it anyways."
> — `snowzach/doods2` README

gRPC was a feature of the legacy Go-based `snowzach/doods` server only. Maintaining a gRPC code path in the FrigateRelay plugin would target zero real users (operators on the legacy Go server can use that project's own gRPC client).

### What changed in the reversal commit

- **Source code:**
  - `Doods2Options.Transport` property + `Doods2Transport` enum: removed.
  - `Doods2Validator` constructor reduced from 5 params (with `odrpcClient`) to 4. gRPC code path (`DetectGrpcAsync`), `RpcException` catch blocks, gRPC-specific imports: removed.
  - `PluginRegistrar`: no longer constructs `GrpcChannel` or registers a gRPC client.
  - `FrigateRelay.Plugins.Doods2.csproj`: dropped `Grpc.Net.Client`, `Google.Protobuf`, `Grpc.Tools` PackageReferences and the `<Protobuf>` MSBuild item.
  - `src/FrigateRelay.Plugins.Doods2/Protos/odrpc.proto` + `Protos/` directory: deleted.
  - Validator now also catches `JsonException` (mirrors the Roboflow PR #42 review-feedback fix) so a non-JSON response from a misbehaving proxy routes through `OnError`.

- **Plans:**
  - `PLAN-2.3.md`: replaced with a REMOVED notice + reversal record.
  - `PLAN-2.2.md`: expanded from 7 tests to 9 (absorbed PLAN-2.3's deferred cancellation test + a FailOpen-on-HTTP-error test mirroring PR #42's REVIEW-1.2 fix). Now also owns the CHANGELOG bullet (was PLAN-2.3's). Test target: 250 → 259.

- **Project meta-docs:**
  - `CONTEXT-14.md` D4: amended with explicit reversal note + rationale.
  - `PROJECT.md` "v1.2 scope" / #14: updated to HTTP-only with reversal pointer.
  - `ROADMAP.md` Phase 14: goal text + risk + deliverables updated to HTTP-only. Risk for #14 dropped from Medium to Low.
  - `RESEARCH.md`: header notice added that §4 (gRPC integration plan) and §7.3 (DOODS2 gRPC API) are stale historical content; not to be acted on.
  - `PLAN-2.1.md`: header notice added that the original dual-transport scope was reverted during this plan's build.

### Architectural-invariant tension RESOLVED

The "Issues Encountered" section above noted that `dotnet list src/FrigateRelay.Host/...csproj package --include-transitive` was leaking `Grpc.*` transitives into the Host's package graph despite source-level cleanness — a tension that wasn't going to be resolvable with build-time DI. After the reversal, the test passes cleanly:

```
$ dotnet list src/FrigateRelay.Host/FrigateRelay.Host.csproj package --include-transitive | grep -E 'Grpc\.|Google\.Protobuf'
(empty)
$ git grep -nE 'Grpc\.' src/
(empty)
```

Both invariants hold. The PLAN-2.3 verification-block guidance (originally needed to relax the test) is no longer needed.

### Reversal verification

- `dotnet build FrigateRelay.sln -c Release` — 0 warnings, 0 errors after reversal.
- `bash .github/scripts/run-tests.sh --skip-integration` — **258/258 passing** (test count unchanged; PLAN-2.2 will add 9 new).
- `git grep -nE 'Grpc\.' src/` — empty ✓.
- `dotnet list <abstractions|host>.csproj package --include-transitive | grep Grpc` — empty ✓.
- Plugin source surface area: 4 files (`csproj`, `Doods2Options.cs`, `Doods2Response.cs`, `Doods2Validator.cs`, `PluginRegistrar.cs`) — no `Protos/`, no vendored license attribution to maintain.
