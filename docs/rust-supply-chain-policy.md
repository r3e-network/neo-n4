# Rust Supply-Chain Policy

This project treats Rust advisories as release inputs, not as cosmetic
warnings. A production release must keep `cargo audit` vulnerability count at
zero and must either remove, upgrade, or explicitly accept every informational
warning that remains in `Cargo.lock`.

Current audit evidence is stored in:

- `CODEX_DEEP_AUDIT/cargo-audit-after-sp1-6.2.1-online-proxy-final.json`
- `CODEX_DEEP_AUDIT/wsl-rust-workspace-clippy-sp1-6.2.1-final.log`
- `CODEX_DEEP_AUDIT/wsl-rust-workspace-test-release-sp1-6.2.1-final.log`
- `CODEX_DEEP_AUDIT/wsl-cargo-update-sp1-6.2.1.log`

## Current Status

The SP1 crates are pinned to `6.2.1` to match the installed `cargo-prove`
toolchain:

- `bridge/neo-zkvm-host`: `sp1-sdk = 6.2.1`
- `bridge/neo-zkvm-guest`: `sp1-zkvm = 6.2.1`

`cargo audit --json` with the advisory database refreshed reports:

- Vulnerabilities: `0`
- Informational warnings: `5` unmaintained crates and `1` unsound crate

## Accepted Informational Warnings

These are accepted for the current release train because they are transitive
through SP1 or the vendored NeoVM stack and cannot be remediated by a normal
semver-compatible `cargo update` without forking upstream proof dependencies.

| Advisory | Package | Current source | Disposition |
| --- | --- | --- | --- |
| RUSTSEC-2026-0002 | `lru 0.12.5` | `sp1-prover 6.2.1 -> sp1-sdk 6.2.1` | Accepted with reachability note below |
| RUSTSEC-2025-0141 | `bincode 1.3.3` | `neo-zkvm-host`, SP1, and vendored NeoVM crates | Accepted compatibility dependency |
| RUSTSEC-2021-0139 | `ansi_term 0.12.1` | SP1 tracing stack | Accepted transitive dependency |
| RUSTSEC-2025-0119 | `number_prefix 0.4.0` | SP1 progress/indicator stack | Accepted transitive dependency |
| RUSTSEC-2024-0436 | `paste 1.0.15` | SP1/zkVM macro stack | Accepted transitive dependency |
| RUSTSEC-2025-0134 | `rustls-pemfile 2.2.0` | `tonic 0.12.3` via SP1 prover types | Accepted transitive dependency |

## `lru` Reachability Note

The active `lru` advisory is for `lru::IterMut`. The local dependency trace is:

```text
lru v0.12.5
`-- sp1-prover v6.2.1
    `-- sp1-sdk v6.2.1
        `-- neo-zkvm-host
```

The SP1 6.2.1 source locations using `LruCache` are:

- `sp1-prover-6.2.1/src/worker/controller/mod.rs`
- `sp1-prover-6.2.1/src/shapes.rs`

Those locations use cache construction plus `get`, `put`, and `push`; no
`lru::IterMut` call is used in the SP1 prover paths exercised by the
`neo-zkvm-host` proof tests. This is still an upstream dependency exception,
not a clean bill of health for the crate. Remove the exception when SP1 moves
to `lru >= 0.16.3` or removes `lru`.

## Verification Commands

Run these before release:

```bash
cargo audit --json
cargo tree -i lru --locked --workspace
cargo fmt --all -- --check
cargo clippy --workspace --all-targets --locked -- -D warnings
cargo test --workspace --release --locked
cargo prove build
cd bridge/neo-zkvm-host
cargo test --release --locked
cargo test --release --locked -- --ignored --nocapture
```

For Windows operators, run the SP1 commands under WSL2 or a Linux/macOS prover
host. Native Windows is not an SP1 prover-host target. If Windows uses a local
HTTP proxy, WSL2 download commands may need the Windows host gateway address
instead of `127.0.0.1`.
