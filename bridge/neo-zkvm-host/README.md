# neo-zkvm-host

The SP1 **host** runtime that loads the compiled `neo-zkvm-guest` RISC-V
ELF, runs it through SP1's zkVM with a canonical `BatchExecutionRequest`
payload, and produces (a) the public-input commitment that L1 settlement
checks against, (b) the cryptographic ZK validity proof itself.

This is the production proving path for any L2 running in Stage-2
(RISC-V ZK validity) mode.

## Position in the stack

```
[off-chain L2 batch builder]
         ↓ canonical BatchExecutionRequest bytes
[neo-zkvm-host:  this crate]
   • execute()  → run guest in zkVM, return public-input hash
   • prove()    → real CPU proof + verifying-key bytes
   • verify()   → off-chain pre-submission proof check
         ↓ proof + vk
[L1 NeoHub.VerifierRegistry]  → verifies on-chain
[L1 NeoHub.SettlementManager] → finalizes batch
```

The `bridge/neo-zkvm-guest/` crate is the function being proved (compiled
to a RISC-V ELF by `cargo prove build`). This crate is the orchestrator
that runs that ELF inside SP1's zkVM and exposes a clean three-function
public API to the rest of the framework (and to operator scripts).

**What's being proved**: each tx in the batch is loaded as a Neo N3 VM
script and executed by the real `neo_vm_guest::execute` (vendored from
`external/neo-zkvm/crates/neo-vm-guest`, which contains the full Neo N3
VM in pure Rust — opcodes, stack, gas accounting, native contracts,
storage). The proof attests to actual VM execution outcomes — halt or
fault, gas consumed, top-of-stack result — not just a hash of the
input bytes.

## Build

Requires the SP1 toolchain (`sp1up` → installs `cargo prove` and the
RISC-V succinct target). `build.rs` invokes `cargo prove build` against
the sibling guest crate on every build, so the embedded ELF stays
synced with the guest source.

```bash
# 1. Install SP1 (one-time):
curl -L https://sp1.succinct.xyz | bash
sp1up

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
cargo build --release -p neo-zkvm-host
```

## Library API (`src/lib.rs`)

```rust
pub fn execute(request_bytes: &[u8]) -> Result<ExecutionResult, String>
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
prove-batch <hex-encoded BatchExecutionRequest>             # execute only (fast)
prove-batch --prove <hex> [--out proof.bin]                 # generate ZK proof (slow)
```

The `--prove` mode writes both `proof.bin` (the proof) and `proof.vk`
(the verifying key) to disk; submit both to L1 via
`NeoHub.SettlementManager.SubmitBatch`.

## Tests

```bash
# Default suite (1 test, ~42s — runs the guest in zkVM execute path
# and cross-checks public-input hash byte-for-byte against host run):
cargo test --release -p neo-zkvm-host

# Slow tests (real proof generation + verification + tampered-hash
# negative test — ~4 min wall time, gated behind --ignored so the
# default loop stays fast):
cargo test --release -p neo-zkvm-host -- --ignored
```

Both tests use the same minimal `BatchExecutionRequest` builder so any
divergence between zkVM execution, host execution, and proven
execution is caught.
