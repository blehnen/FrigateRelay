# FrigateRelay — multi-stage Dockerfile
#
# Base: Alpine (musl libc) for smallest footprint (~80–90 MB target).
# Fallback: if a runtime issue surfaces with MQTTnet sockets or OTel HTTP/2,
# swap to mcr.microsoft.com/dotnet/runtime-deps:10.0-bookworm-slim and change the
# RID flags from linux-musl-x64/arm64 to linux-x64/arm64. No other changes needed.
#
# Supply-chain: both base images are tag + sha256 digest pinned (B3).
# Dependabot docker ecosystem will rewrite both atomically on image updates.

# ── Build stage ──────────────────────────────────────────────────────────────
# Debian-based SDK: cross-compiles to musl via -r linux-musl-x64 / linux-musl-arm64.
# Using sdk:10.0-alpine here would require Alpine-in-Alpine toolchain; Debian SDK
# is the recommended cross-compile path (RESEARCH.md C3).
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine@sha256:0191ff386e93923edf795d363ea0ae0669ce467ada4010b370644b670fa495c1 AS build

# BuildKit TARGETARCH is injected by `docker buildx build --platform linux/amd64|arm64`.
# Map Docker arch names to .NET musl RIDs.
ARG TARGETARCH
RUN case "$TARGETARCH" in \
      amd64) echo "linux-musl-x64"  > /tmp/rid ;; \
      arm64) echo "linux-musl-arm64" > /tmp/rid ;; \
      *)     echo "Unsupported TARGETARCH: $TARGETARCH" >&2; exit 1 ;; \
    esac

WORKDIR /src

# Copy solution + project files first so NuGet restore is cached as a layer.
COPY FrigateRelay.sln global.json Directory.Build.props ./
COPY src/FrigateRelay.Abstractions/FrigateRelay.Abstractions.csproj               src/FrigateRelay.Abstractions/
COPY src/FrigateRelay.Host/FrigateRelay.Host.csproj                               src/FrigateRelay.Host/
COPY src/FrigateRelay.Sources.FrigateMqtt/FrigateRelay.Sources.FrigateMqtt.csproj src/FrigateRelay.Sources.FrigateMqtt/
COPY src/FrigateRelay.Plugins.BlueIris/FrigateRelay.Plugins.BlueIris.csproj       src/FrigateRelay.Plugins.BlueIris/
COPY src/FrigateRelay.Plugins.FrigateSnapshot/FrigateRelay.Plugins.FrigateSnapshot.csproj src/FrigateRelay.Plugins.FrigateSnapshot/
COPY src/FrigateRelay.Plugins.Pushover/FrigateRelay.Plugins.Pushover.csproj       src/FrigateRelay.Plugins.Pushover/
COPY src/FrigateRelay.Plugins.CodeProjectAi/FrigateRelay.Plugins.CodeProjectAi.csproj    src/FrigateRelay.Plugins.CodeProjectAi/

RUN RID=$(cat /tmp/rid) && \
    dotnet restore src/FrigateRelay.Host/FrigateRelay.Host.csproj -r "$RID" --locked-mode

# Copy full source (test projects excluded via .dockerignore).
COPY src/ src/
COPY config/ config/

# Self-contained publish for the target RID (D3: untrimmed, no AOT, no single-file).
RUN RID=$(cat /tmp/rid) && \
    dotnet publish src/FrigateRelay.Host/FrigateRelay.Host.csproj \
      -c Release \
      -r "$RID" \
      --self-contained true \
      -p:PublishSingleFile=false \
      -p:PublishTrimmed=false \
      -p:PublishAot=false \
      --no-restore \
      -o /app/publish

# ── Runtime stage ─────────────────────────────────────────────────────────────
# Alpine runtime-deps: only the native musl libc + ICU; no .NET runtime (bundled in publish).
# Digest-pinned for supply-chain integrity (B3). Tag retained for human readability.
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-alpine@sha256:4f08c162590324de60f31937f5b5fa9f2b5eddaa4f0aaec3c872f855bf16c36c AS runtime

# Dedicated non-root user + group (UID/GID 10001) — ROADMAP Phase 10 + B1.
# RUN before USER so addgroup/adduser execute as root.
RUN addgroup -g 10001 -S frigaterelay && \
    adduser  -u 10001 -S frigaterelay -G frigaterelay

WORKDIR /app

COPY --from=build /app/publish ./

# appsettings.Docker.json is bundled in the publish output (CopyToOutputDirectory=PreserveNewest).
# The ASPNETCORE_ENVIRONMENT=Docker env var below activates it at runtime, which:
#   - Loads Serilog Console-only config layer (no file sink in container — closes ID-23).
#   - Suppresses the programmatic file sink guard in HostBootstrap.ConfigureServices.

# Container-specific environment defaults.
ENV ASPNETCORE_ENVIRONMENT=Docker
ENV ASPNETCORE_URLS=http://+:8080
# ENV OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf  # uncomment if HTTP/2 issues arise on Alpine (RESEARCH.md C2)

# Own the app directory so the non-root user can write logs if an operator mounts a volume.
RUN chown -R frigaterelay:frigaterelay /app

# Switch to non-root for all subsequent runtime operations.
USER 10001:10001

EXPOSE 8080

# Alpine ships wget (not curl). Spider mode: exits 1 on non-2xx; no file written.
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD wget -q --spider http://localhost:8080/healthz || exit 1

ENTRYPOINT ["./FrigateRelay.Host"]
