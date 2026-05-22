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

<!-- N4-CRATE-VISUAL-GUIDE:START -->
## Technical Visual Guide

These diagrams are local to this crate and explain `neo-execution-core` at the technical architecture level. They focus on system role, principles, data movement, workflow, state, proof/evidence, trust boundaries, integration, and runtime lifecycle.

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

- **Layer:** N4 batch execution core
- **Purpose:** Backend-neutral L2 batch transition primitives shared by fast execution and proof generation.
- **Inputs:** L2 batch | previous state root | execution parameters
- **Responsibilities:** Validate batch shape | Apply deterministic transition | Commit new state root
- **Outputs:** execution trace | new state root | public proof inputs
- **Consumers:** neo-zkvm-guest | neo-zkvm-host | gateway services

### Reading Order

1. Start with system position and conceptual architecture.
2. Read technical principles, trust boundaries, and state model to understand correctness.
3. Follow workflow and dataflow to see runtime movement.
4. Use proof/evidence flow, integration map, and lifecycle for operational understanding.
<!-- N4-CRATE-VISUAL-GUIDE:END -->
