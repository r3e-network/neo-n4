# Multi-stage Dockerfile for neo-bridge-watcher-eth.
#
# Build:
#   docker build -f watchers/neo-bridge-watcher-eth/Dockerfile \
#                -t neo-bridge-watcher-eth:latest .
#
# (Run from the workspace root, NOT from this directory — cargo
# workspace resolution needs the root Cargo.toml + sibling crates'
# Cargo.toml files. The .dockerignore at the workspace root keeps
# the build context lean.)
#
# Run (one-off, default config path):
#   docker run --rm \
#     -v $(pwd)/watcher.toml:/etc/watcher/watcher.toml:ro \
#     -v $(pwd)/watcher.priv:/var/lib/watcher/keys/watcher.priv:ro \
#     -v watcher-journal:/var/lib/watcher/journal \
#     -p 9090:9090 \
#     neo-bridge-watcher-eth:latest
#
# Run (custom config path):
#   docker run ... neo-bridge-watcher-eth:latest --config /alt/path.toml

# ───────────────────────────────────────────────────────────────────
# Stage 1 — builder
# ───────────────────────────────────────────────────────────────────
# Rust 1.85+ required: transitive dep `base64ct` (pulled by k256 →
# elliptic-curve) uses edition 2024, stabilized in 1.85. Pin 1.86
# (current stable as of 2026-05) for headroom.
FROM rust:1.86-slim-bookworm AS builder

# System deps for rustls + ring (used by reqwest's blocking client).
# pkg-config + libssl-dev are belt-and-suspenders — rustls technically
# doesn't need them, but some transitive crates (ring, etc.) probe
# for them. ca-certificates lets cargo fetch crates from crates.io
# over HTTPS.
RUN apt-get update && apt-get install -y --no-install-recommends \
        pkg-config libssl-dev ca-certificates && \
    rm -rf /var/lib/apt/lists/*

WORKDIR /build

# Copy the entire workspace. `.dockerignore` at the workspace root
# strips heavy directories (target/, .git/, external/neo, etc.) so
# the context is reasonable.
COPY . .

# Build the watcher daemon with the live-rpc feature. --locked pins
# the dep versions from Cargo.lock so the container build is
# reproducible across runs. --release for production; debug builds
# are dramatically slower at the polling rates production sees.
RUN cargo build --release --locked \
        -p neo-bridge-watcher-eth \
        --features live-rpc && \
    strip target/release/neo-bridge-watcher-eth

# ───────────────────────────────────────────────────────────────────
# Stage 2 — runtime
# ───────────────────────────────────────────────────────────────────
# distroless cc-debian12 ships glibc + libgcc + libssl + ca-certs
# and nothing else. No shell, no package manager, no /bin/sh — much
# smaller attack surface than even alpine. The :nonroot variant pre-
# creates an unprivileged user (uid:gid 65532:65532).
FROM gcr.io/distroless/cc-debian12:nonroot AS runtime

LABEL org.opencontainers.image.title="neo-bridge-watcher-eth"
LABEL org.opencontainers.image.description="External-bridge watcher daemon for the Neo Elastic Network (Eth/EVM family ↔ Neo)"
LABEL org.opencontainers.image.source="https://github.com/r3e-network/neo-n4"
LABEL org.opencontainers.image.documentation="https://github.com/r3e-network/neo-n4/tree/master/watchers/neo-bridge-watcher-eth"
LABEL org.opencontainers.image.licenses="MIT"

COPY --from=builder /build/target/release/neo-bridge-watcher-eth /usr/local/bin/neo-bridge-watcher-eth

# Run as the distroless `nonroot` user (uid:gid 65532:65532).
USER 65532:65532
WORKDIR /var/lib/watcher

# Health endpoint port. Operator's TOML must bind here for the k8s
# probes / Docker HEALTHCHECK to work.
EXPOSE 9090

# HEALTHCHECK for `docker run` users. K8s readinessProbe / livenessProbe
# in deploy/k8s.yaml supersede this when running under k8s.
# (distroless has no curl/wget — we'd add `--health-cmd` from the
# host instead. Skip HEALTHCHECK in-image; let k8s + operator-supplied
# tooling drive it.)

ENTRYPOINT ["/usr/local/bin/neo-bridge-watcher-eth"]
CMD ["--config", "/etc/watcher/watcher.toml"]
