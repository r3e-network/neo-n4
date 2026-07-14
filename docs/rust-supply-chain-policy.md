# Rust Supply-Chain Policy

This project treats Rust advisories as release inputs, not as cosmetic
warnings. A production release must keep `cargo audit` vulnerability count at
zero and must either remove, upgrade, or explicitly accept every informational
warning that remains in `Cargo.lock`.

Curated audit evidence is stored in:

- `docs/audit/production-readiness-audit.md`
- `docs/audit/repository-coverage-ledger.md`

Raw command logs and `cargo audit --json` output are local scratch artifacts,
not tracked source files. Attach fresh raw logs to a release approval package
rather than committing machine-local evidence to the repository.

## Current Status

The SP1 crates are pinned to `6.2.1` to match the installed `cargo-prove`
toolchain:

- `bridge/neo-zkvm-host`: `sp1-sdk = 6.2.1`
- `bridge/neo-zkvm-guest`: `sp1-zkvm = 6.2.1`

`cargo audit --json` with the advisory database refreshed reports:

| Lockfile | Vulnerabilities | Unmaintained | Unsound | Yanked |
| --- | ---: | ---: | ---: | ---: |
| Root `Cargo.lock` | 0 | 6 | 1 | 1 |
| `external/neo-riscv-vm/Cargo.lock` | 0 | 1 | 0 | 1 |
| `external/neo-vm-rs/Cargo.lock` | 0 | 0 | 0 | 0 |
| `external/neo-zkvm/Cargo.lock` | 0 | 7 | 1 | 1 |
| `external/foreign-contracts/sol/Cargo.lock` | 0 | 1 | 0 | 0 |

The root and independently locked `external/neo-zkvm` graphs previously
resolved `serial_test 3.4.0 -> scc 2.4.0`. A semver-compatible update to
`serial_test 3.5.0` removes `scc` and therefore removes RUSTSEC-2026-0205 from
both graphs.

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
| RUSTSEC-2026-0173 | `proc-macro-error2 2.0.1` | `dynasm 3.2.1` via SP1 JIT | Accepted transitive dependency |
| RUSTSEC-2025-0134 | `rustls-pemfile 2.2.0` | `tonic 0.12.3` via SP1 prover types | Accepted transitive dependency |
| RUSTSEC-2023-0089 | `atomic-polyfill 1.0.3` | `neo-riscv-host -> postcard -> heapless` in the independently locked RISC-V workspace | Accepted submodule compatibility dependency |
| yanked, no advisory | `spin 0.9.8` | SP1's `lazy_static` graph and `neo-riscv-host -> postcard -> heapless` | Exact lock remains reproducible; replace when upstream moves to a non-yanked compatible release |

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
cargo prove build --docker --locked
cd bridge/neo-zkvm-host
cargo test --release --locked
cargo test --release --locked -- --ignored --nocapture
```

For Windows operators, run the SP1 commands under WSL2 or a Linux/macOS prover
host. Native Windows is not an SP1 prover-host target. If Windows uses a local
HTTP proxy, WSL2 download commands may need the Windows host gateway address
instead of `127.0.0.1`.
