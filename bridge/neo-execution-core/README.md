# neo-execution-core

Backend-agnostic Neo L2 batch execution primitives shared by prover and fast
execution adapters.

This crate is deliberately not a VM:

- no SP1 dependency;
- no PolkaVM dependency;
- no NeoVM opcode interpreter;
- `#![no_std]` with `alloc`, so it can be used by constrained guests later.

It owns only the deterministic batch contract that both execution paths must
agree on:

1. parse canonical `BatchExecutionRequest` bytes;
2. fold L1 messages into the running state root;
3. call a backend-supplied per-transaction executor;
4. commit the backend's `VmExecutionReceipt` into receipt/state roots;
5. compute `txRoot`, `receiptRoot`, `postStateRoot`, and `publicInputHash`.

## Backend model

The shared boundary is `VmExecutionReceipt`:

```rust
pub struct VmExecutionReceipt {
    pub state: u8,
    pub gas_consumed: u64,
    pub output_hash: [u8; 32],
}
```

The zkVM guest maps `neo_vm_guest::ProofOutput` into this shape. A
PolkaVM-backed RISC-V adapter should map `neo_riscv_abi::ExecutionResult` the
same way, after hashing its canonical ABI result bytes. The core never needs to
know which VM produced the receipt.

## Why PolkaVM stays outside

`external/neo-riscv-vm` is the PolkaVM-backed Neo RISC-V execution engine. It
is the fast execution path, benchmarking target, and parity oracle. This crate
does not link to PolkaVM because the same batch commitment logic must also run
inside SP1's zkVM guest. Keeping the boundary at `VmExecutionReceipt` prevents
the proving path from depending on a native PolkaVM host while still allowing
both backends to share the exact same batch folding rules.

## Tests

```bash
cargo test -p neo-execution-core
```
