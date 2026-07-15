# neo-zkvm-host

The SP1 **host** runtime that loads the compiled `neo-zkvm-guest` RISC-V
ELF, runs it through SP1's zkVM with a canonical `ProofWitnessArtifactV1`
payload, and produces (a) the public-input commitment that L1 settlement
checks against, (b) the cryptographic ZK validity proof itself.

This is the production proving path for any L2 running in Stage-2
(RISC-V ZK validity) mode.

## Position in the stack

```
[off-chain L2 batch builder]
         ↓ canonical ProofWitnessArtifactV1 bytes
[neo-zkvm-host:  this crate]
   • execute()  → run guest in zkVM, return public-input hash
   • prove()    → real CPU proof + verifying-key bytes
   • verify()   → off-chain pre-submission proof check
         ↓ proof + vk
[L1 NeoHub.VerifierRegistry]  → verifies on-chain
[L1 NeoHub.SettlementManager] → finalizes batch
```

The `bridge/neo-zkvm-guest/` crate is the function being proved (compiled
to a RISC-V ELF by the digest-pinned Docker `cargo prove build`). This crate is the orchestrator
that runs that ELF inside SP1's zkVM and exposes a clean three-function
public API to the rest of the framework (and to operator scripts).

**What's being proved**: the guest decodes complete Neo transactions, verifies
the bounded pre-state/code/manifest witness, executes `tx.Script` through
`neo-vm-rs` with stateful production-priced syscalls, commits HALT overlays,
rolls back FAULT effects, and recomputes every receipt/root/public input.

## Build

Requires Linux or macOS with the SP1 toolchain (`sp1up` → installs
`cargo prove` and the RISC-V succinct target). SP1 does not currently
ship native Windows support; on Windows, run the prover under WSL2 or a
Linux/macOS prover host. Docker is required for production builds. `build.rs`
invokes `cargo prove build --docker --locked` against the sibling guest crate
with the audited immutable SP1 6.2.1 amd64 image digest, so the embedded ELF and
program VK do not inherit host paths or compiler differences. The build script reads the shared
Docker ELF once, checks SHA-256 and VK from that exact snapshot, publishes it as a read-only `0400`
file in Cargo's isolated `OUT_DIR`, and embeds only that copy. This prevents a concurrent guest
build from replacing the shared target ELF between validation and `include_bytes!`. Cached ELFs are disabled by default; set
`NEO_ZKVM_ALLOW_CACHED_ELF=1` only for host-only development that
intentionally does not execute or prove the guest.

If Docker's client configuration injects a loopback HTTP/HTTPS proxy, the shared build support
creates a credential-free temporary Docker configuration that preserves the active context but
removes the unreachable loopback proxy. This prevents containerized Cargo from trying to reach
the host's `127.0.0.1` proxy while keeping non-loopback enterprise proxies unchanged.

```bash
# 1. Install SP1 (one-time, Linux/macOS or WSL2):
curl -L https://sp1up.succinct.xyz | bash
sp1up

# If WSL2 cannot reach GitHub because Windows is using a localhost
# proxy, point WSL at the Windows host gateway first. Replace 7890 with
# the local proxy port shown in Windows Internet Settings.
HOST_IP=$(ip route | awk '/default/ {print $3}')
export http_proxy=http://$HOST_IP:7890
export https_proxy=http://$HOST_IP:7890

# 2. protoc is required by sp1-sdk's transitive deps. Some Linux distros
#    ship an outdated version missing `google/protobuf/empty.proto`. If
#    you hit "empty.proto: file not found" while building sp1-sdk, copy
#    the include dir from the vendored protobuf into your local include:
#
#    mkdir -p ~/.local/include && cp -R \
#      ~/.cargo/registry/src/index.crates.io-*/protoc-bin-vendored-linux-x86_64-*/include/google \
#      ~/.local/include/
#    export CPATH=~/.local/include

# 3. Build:
export SP1_DOCKER_IMAGE=ghcr.io/succinctlabs/sp1@sha256:14d3c46eff7492f87e429bfbf618e3d33499ba7515b15c36eeb1bcaebc9f7b7f
export SP1_GNARK_IMAGE=ghcr.io/succinctlabs/sp1-gnark@sha256:be8555f1ad90870acd8c6ec7fd3ba0b1a2133ea9cddf25e130665aa651129e54

# macOS + Colima: SP1's Groth16 wrapper bind-mounts temporary witness and
# output files into Docker. Keep TMPDIR under the shared repository/user tree;
# the default /var/folders path is not visible inside the Colima VM and Docker
# otherwise creates /witness as a directory.
mkdir -p "$PWD/target/sp1-tmp"
export TMPDIR="$PWD/target/sp1-tmp"

cargo prove build --docker --locked
cargo build --release -p neo-zkvm-host
```

## Library API (`src/lib.rs`)

```rust
pub fn execute(request_bytes: &[u8]) -> Result<ZkExecutionResult, String>
```

Runs the guest inside SP1's zkVM (no proving — just deterministic
execution). Cheap. Used in development and as the "did the script
HALT?" sanity check before producing a real proof.

```rust
pub fn prove(request_bytes: &[u8]) -> Result<ProofResult, String>
```

Generates a real ZK proof for the batch — the on-chain settlement
artifact. Returns the proof bytes (bincode `SP1ProofWithPublicValues`),
the verifying-key bytes (bincode `SP1VerifyingKey`, stable per guest
ELF), and the 32-byte public-input commitment. Substantially slower
than `execute`: minutes per batch on a beefy CPU.

```rust
pub fn verify(
    proof_bytes: &[u8],
    vk_bytes: &[u8],
    expected_public_input_hash: &[u8; 32],
) -> Result<(), String>
```

Mirror of the on-chain dispatch path: deserialize, check the
public-input commitment matches expectation (cheap, catches replay
before burning verifier cycles), then run SP1's cryptographic
verifier. Off-chain prover infrastructure should call this before
submission so it never wastes L1 gas on bad proofs.

## CLI: `prove-batch`

Operator-facing entrypoint:

```text
prove-batch <hex-encoded ProofWitnessArtifactV1>            # execute only (fast)
prove-batch --prove <hex> [--out proof.bin]                 # generate ZK proof (slow)
prove-batch daemon --watch <dir> [--archive <dir>] \
  [--max-queue-bytes N] [--max-queue-tasks N]              # production queue
```

The `--prove` mode writes both `proof.bin` (the proof) and `proof.vk`
(the verifying key) to disk; submit both to L1 via
`NeoHub.SettlementManager.SubmitBatch`.

The daemon treats the filesystem queue as confidential durable state. Unix queue/archive
directories are owner-only `0700`, files are `0600`, and symlinks, foreign ownership, or broader
modes fail closed. The combined watch/archive defaults are capped at 16 GiB and 64
content-addressed tasks, checked before every proof. A completed proof is retained until the .NET
settlement pipeline has durably recorded `SettlementFinalized` and atomically publishes
`<artifact-content-hash>.proof.ack` containing that same 32-byte hash. The daemon validates the
acknowledgement, idempotently removes the request/proof/VK/public-values/result/archive set, then
removes the acknowledgement. Do not apply TTL cleanup to unconfirmed artifacts.

## Tests

```bash
# Daily debug suite (skips SP1 execute/prove cost; release gates below cover it):
cargo test -p neo-zkvm-host

# Default release suite (~1 min — runs the guest in zkVM execute path
# and cross-checks public-input hash byte-for-byte against host run):
cargo test --release -p neo-zkvm-host

# Slow tests (real proof generation + verification + tampered-hash
# negative test — ~4 min wall time, gated behind --ignored so the
# default loop stays fast):
cargo test --release -p neo-zkvm-host -- --ignored --nocapture

# Regenerate the exact Rust/C# on-chain verifier release vector:
cargo run --release -p neo-zkvm-host \
  --example generate_groth16_release_vector --locked
```

The execute/prove tests use committed stateful and native-transition golden artifacts, so
divergence between fresh zkVM execution, host execution, and proven execution
is caught without a script-only fallback.

The 2026-05-17 WSL2 audit run with SP1 `6.2.1` completed the default
release test in about 14 seconds after dependencies were built. The
ignored proof tests generated a real proof in about 42 seconds, verified
it in about 14 seconds, and finished the proof-positive plus tampered-hash
negative suite in about 96 seconds on the audit host.

<!-- N4-CRATE-VISUAL-GUIDE:START -->
## Technical Visual Guide

These diagrams are local to this crate and explain `neo-zkvm-host` at the technical architecture level. They focus on system role, principles, data movement, workflow, state, proof/evidence, trust boundaries, integration, and runtime lifecycle.

Full technical explanation: [docs/learning-guide.md](docs/learning-guide.md).

| View | Diagram | Mermaid |
| --- | --- | --- |
| System Position | ![System Position](docs/figures/position.svg) | [Mermaid](docs/figures/position.mmd) |
| Technical Principles | ![Technical Principles](docs/figures/principles.svg) | [Mermaid](docs/figures/principles.mmd) |
| Conceptual Architecture | ![Conceptual Architecture](docs/figures/architecture.svg) | [Mermaid](docs/figures/architecture.mmd) |
| Workflow | ![Workflow](docs/figures/workflow.svg) | [Mermaid](docs/figures/workflow.mmd) |
| Data Flow | ![Data Flow](docs/figures/dataflow.svg) | [Mermaid](docs/figures/dataflow.mmd) |
| State Model | ![State Model](docs/figures/state-model.svg) | [Mermaid](docs/figures/state-model.mmd) |
| Proof and Evidence Flow | ![Proof and Evidence Flow](docs/figures/proof-flow.svg) | [Mermaid](docs/figures/proof-flow.mmd) |
| Trust Boundaries | ![Trust Boundaries](docs/figures/trust-boundaries.svg) | [Mermaid](docs/figures/trust-boundaries.mmd) |
| Integration Map | ![Integration Map](docs/figures/integration-map.svg) | [Mermaid](docs/figures/integration-map.mmd) |
| Runtime Lifecycle | ![Runtime Lifecycle](docs/figures/lifecycle.svg) | [Mermaid](docs/figures/lifecycle.mmd) |

### Technical Role

- **Layer:** N4 zk host
- **Purpose:** Host-side SP1 prover orchestration for creating and checking L2 batch proofs.
- **Inputs:** L2 batch | guest ELF | prover configuration
- **Responsibilities:** Prepare SP1 stdin | Run prover | Verify proof envelope
- **Outputs:** proof bytes | verification report | state commitment
- **Consumers:** bridge relayer | L1 verifier adapter | devnet scripts

### Reading Order

1. Start with system position and conceptual architecture.
2. Read technical principles, trust boundaries, and state model to understand correctness.
3. Follow workflow and dataflow to see runtime movement.
4. Use proof/evidence flow, integration map, and lifecycle for operational understanding.
<!-- N4-CRATE-VISUAL-GUIDE:END -->
