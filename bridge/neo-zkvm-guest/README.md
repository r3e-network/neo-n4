# neo-zkvm-guest

The Rust crate that compiles to a RISC-V ELF and runs inside the SP1 zkVM.
**This is the function the SP1 prover proves correct** — the deterministic
batch executor that any L2 chain running in Stage-2 (ZK validity) mode
points its prover at.

## Position in the stack

```
[off-chain L2 batch builder]
         ↓ canonical BatchExecutionRequest bytes
[SP1 prover host: bridge/neo-zkvm-bridge]
         ↓ runs neo-zkvm-guest (this crate) inside zkVM
         ↓ produces ZK proof + public-input commitment
[L1 NeoHub.VerifierRegistry]
         ↓ verifies proof
[L1 NeoHub.SettlementManager]
         ✓ batch finalized
```

Mirrors the C# reference path
(`Neo.L2.Executor.ApplicationEngineTransactionExecutor` +
`Neo.L2.Executor.ReferenceBatchExecutor`) in compute, but encoded as a
RISC-V program SP1 can prove.

## Build

Requires the SP1 toolchain (`cargo prove`):

```bash
# 1. Install SP1 (one-time):
curl -L https://sp1.succinct.xyz | bash
sp1up

# 2. Build the guest ELF:
cd bridge/neo-zkvm-guest
cargo prove build
# → produces target/elf-compilation/riscv32im-succinct-zkvm-elf/release/neo-zkvm-guest
```

Without the SP1 toolchain, `cargo build` (default features) compiles the
crate as a host binary — useful for unit-testing the pure execution
functions on the host:

```bash
cargo test
# → 7 tests passed (parse + execute + Merkle determinism + version/truncation rejection)
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
3. Applies each transaction in order — pure deterministic state-root advancement.
4. Computes Merkle roots over per-tx hashes + per-receipt hashes.
5. Commits a public-input bundle hash to the SP1 output stream.

## What's intentionally simplified vs the C# path

- **No real Neo VM execution**: the guest's `apply_transaction` is a
  deterministic state-root advancement (Hash256(prev_root || tx_hash)),
  not a Neo VM `ApplicationEngine.Run`. Real Stage-2 deployments wire
  in a RISC-V port of the operator's executor logic — same shape, but
  with the actual contract-execution semantics they care about.
- **No native-contract bootstrap**: the C# path needs `NeoVMGenesisBootstrap`
  to populate PolicyContract / ContractManagement state. The proving
  target only needs the deterministic state-root function; the executor
  semantics are the operator's business.

## Constraint: this sandbox cannot compile the SP1-feature path

The `cargo prove` step requires SP1's RISC-V toolchain installed offline
(several GB of cross-compile bits). This sandbox doesn't have it, so
end-to-end proving is verified by:

1. Host-side unit tests pass (`cargo test`) — the pure execution functions
   are deterministic + correct.
2. The `bridge/neo-zkvm-bridge` Rust crate exercises the SP1 link path
   with `SP1_FORCE_DUMMY=true` in CI (skips guest compile, exercises host
   prover linkage).

Operators who deploy Stage-2 chains run `cargo prove build` once on their
prover infrastructure to produce the matching guest ELF. The C# host then
loads it via `Sp1RiscVProver` (in `src/Neo.L2.Proving.Sp1/`).
