# neo-zkvm-guest

The Rust crate that compiles to a RISC-V ELF and runs inside the SP1 zkVM.
**This is the function the SP1 prover proves correct** — the deterministic
batch executor that any L2 chain running in Stage-2 (ZK validity) mode
points its prover at.

## Position in the stack

```
[off-chain L2 sequencer]
         ↓ canonical BatchExecutionRequest bytes (*.batch.bin in queue dir)
[bridge/neo-zkvm-host  (prove-batch daemon)]
         ↓ loads neo-zkvm-guest (this crate) into SP1 zkVM
         ↓ runs neo_vm_guest::execute on every tx → real Neo N3 VM
         ↓ produces ZK proof + verifying-key bytes + public-input commitment
[L1 NeoHub.VerifierRegistry]
         ↓ verifies proof
[L1 NeoHub.SettlementManager]
         ✓ batch finalized
```

What gets proven: each tx in the batch is loaded as a Neo N3 VM script and
executed by `neo_vm_guest::execute` (vendored from
`external/neo-zkvm/crates/neo-vm-guest`, which contains the full Neo N3
VM in pure Rust — opcodes, eval stack, gas accounting, native contracts,
storage). The proof attests to actual VM execution outcomes — halt or
fault, gas consumed, top-of-stack result — not a hash of the input bytes.

## Build

Requires the SP1 toolchain (`cargo prove`):

```bash
# 1. Install SP1 (one-time):
curl -L https://sp1.succinct.xyz | bash
sp1up

# 2. Build the guest ELF:
cd bridge/neo-zkvm-guest
cargo prove build
# → produces target/elf-compilation/riscv64im-succinct-zkvm-elf/release/neo-zkvm-guest
```

Without the SP1 toolchain, `cargo build` (default features) compiles the
crate as a host binary — useful for unit-testing the pure execution
functions on the host:

```bash
cargo test
# → 8 tests passed (parse + execute + Merkle determinism + version/truncation rejection)
```

## Wire format

`BatchExecutionRequest` (canonical bytes, all little-endian):

```
[1B  version=1]
[4B  chainId]
[8B  batchNumber]
[32B preStateRoot]
[32B daCommitment]
[4B  l1MessageCount]
  [(4B msgLen)(msgBytes)]*l1MessageCount
[4B  txCount]
  [(4B txLen)(txBytes)]*txCount
```

Output: 32-byte `publicInputHash` committed via `sp1_zkvm::io::commit`.
This binds into the proof's public outputs and is what the on-chain
verifier compares against `L2BatchCommitment.PublicInputHash`.

## What this crate does

1. Parses the canonical wire-format batch request.
2. Applies any L1→L2 messages to the state (hashing each into the state root).
3. **Executes each tx through real Neo N3 VM** via `neo_vm_guest::execute`,
   folding the resulting `ProofOutput` (state, gas, top-of-stack, error)
   into the receipt digest and state root.
4. Computes Merkle roots over per-tx hashes + per-receipt hashes.
5. Commits a public-input bundle hash to the SP1 output stream.

## End-to-end proving

`cargo prove build` step requires SP1's RISC-V toolchain installed
(several GB of cross-compile bits, install via `sp1up`). Once built,
`bridge/neo-zkvm-host/tests/end_to_end.rs` runs the guest in real SP1's
zkVM and asserts the public-input hash matches host-mode execution
byte-for-byte. Two `#[ignore]`-gated tests exercise real CPU proof
generation + verification + a tampered-hash negative test (~3.5 min
combined) — see `bridge/neo-zkvm-host/README.md`.

Operators who deploy Stage-2 chains run `cargo prove build` once on
their prover infrastructure to produce the matching guest ELF, then run
`prove-batch daemon --watch <queue-dir>` to consume sealed batches as
they arrive. See `docs/launching-an-l2.md` § "Prover deployment" for the
operator runbook.
