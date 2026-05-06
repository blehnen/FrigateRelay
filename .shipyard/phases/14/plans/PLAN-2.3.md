---
phase: 14-v1.2-inference-engines-parallel-validation
plan: 2.3
wave: 2
dependencies: []
must_haves: []
files_touched: []
tdd: false
risk: low
status: REMOVED
---

# Plan 2.3: REMOVED — DOODS2 gRPC scope reverted

## Status: REMOVED 2026-05-06

This plan originally covered DOODS2 gRPC-transport unit tests. **The gRPC scope was reverted during PLAN-2.1 build** after the orchestrator probed the live DOODS2 v2 server at `192.168.0.2:10200` and verified upstream's README:

> "DOODS2 drops support for gRPC as I doubt very much anyone used it anyways."
> — `snowzach/doods2` README

The Python `snowzach/doods2` rewrite is HTTP-only by design. gRPC was a feature of the legacy Go-based `snowzach/doods` server and was deliberately removed in v2. Operators on the legacy Go server can use the original gRPC client; this plugin targets v2 and ships HTTP-only.

## What got reverted

- `Grpc.Net.Client`, `Google.Protobuf`, `Grpc.Tools` package references removed from `FrigateRelay.Plugins.Doods2.csproj`.
- `<Protobuf>` MSBuild item removed.
- Vendored `src/FrigateRelay.Plugins.Doods2/Protos/odrpc.proto` deleted.
- `Doods2Options.Transport` property + `Doods2Transport` enum removed (always-HTTP simplifies the surface).
- `Doods2Validator` constructor reduced from 5 params to 4 (no `odrpcClient grpcClient`).
- gRPC code path (`DetectGrpcAsync`), `RpcException` catch blocks, gRPC-specific imports removed.
- `PluginRegistrar` no longer constructs `GrpcChannel` or registers a gRPC client.

Net effect: `git grep -nE 'Grpc\.' src/` returns empty across the entire repo; `dotnet list src/FrigateRelay.Host/FrigateRelay.Host.csproj package --include-transitive` shows zero `Grpc.*` entries (the architectural-invariant tension surfaced in SUMMARY-2.1's "Issues Encountered" is resolved).

## What this plan would have shipped (recorded for posterity)

- 5 gRPC-path unit tests using `Grpc.AspNetCore.Server` + a real Kestrel-on-random-port test fixture.
- `Grpc.AspNetCore.Server` package added to `tests/FrigateRelay.Plugins.Doods2.Tests/`.
- Server-side proto codegen via `<Protobuf GrpcServices="Server">`.
- Test count target was 262; now stays at PLAN-2.2's target.

## What absorbs PLAN-2.3's residual responsibilities

- **CHANGELOG bullet** for DOODS2 — moved into PLAN-2.2 Task 3.
- **Cancellation test** that was deferred from PLAN-2.2 to here — added to PLAN-2.2's test set.
- **Final dep-containment verification** — covered by PLAN-2.2's Verification block.

## Reversal rationale

User confirmed during Wave 2 build (commit `1963657` / SUMMARY-2.1.md): the live DOODS2 server is the v2 Python rewrite which dropped gRPC. Maintaining a gRPC code path that targets a hypothetical legacy Go server adds ~300 LOC, a new dependency family, and ~5 tests for a feature with zero real-world users. CONTEXT-14 D4 has been amended.
