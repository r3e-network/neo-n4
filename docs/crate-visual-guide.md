# Neo N4 Crate Visual Guide

This index links every Rust crate to its local visual architecture guide.

| Crate | Path | Layer | Purpose |
| --- | --- | --- | --- |
| [`neo-execution-core`](../bridge/neo-execution-core/README.md) | `bridge/neo-execution-core` | N4 batch execution core | Backend-neutral L2 batch transition primitives shared by fast execution and proof generation. |
| [`neo-zkvm-guest`](../bridge/neo-zkvm-guest/README.md) | `bridge/neo-zkvm-guest` | N4 zk guest | SP1 guest program that runs deterministic Neo L2 batch execution inside the proof circuit. |
| [`neo-zkvm-host`](../bridge/neo-zkvm-host/README.md) | `bridge/neo-zkvm-host` | N4 zk host | Host-side SP1 prover orchestration for creating and checking L2 batch proofs. |
| [`neo-external-bridge-router`](../external/foreign-contracts/sol/programs/neo-external-bridge-router/README.md) | `external/foreign-contracts/sol/programs/neo-external-bridge-router` | Foreign-chain bridge program | Solana-side bridge router that represents Neo N4 cross-chain lock, mint, burn, and unlock flows. |
| [`neo-riscv-abi`](../external/neo-riscv-vm/crates/neo-riscv-abi/README.md) | `external/neo-riscv-vm/crates/neo-riscv-abi` | NeoVM2 / RISC-V execution profile | Shared ABI, stack values, codec tags, and opcode metadata re-exports for RISC-V execution. |
| [`neo-riscv-contract-harness`](../external/neo-riscv-vm/crates/neo-riscv-contract-harness/README.md) | `external/neo-riscv-vm/crates/neo-riscv-contract-harness` | NeoVM2 / RISC-V execution profile | Test harness for contract-level RISC-V execution and syscall simulation. |
| [`neo-riscv-devpack`](../external/neo-riscv-vm/crates/neo-riscv-devpack/README.md) | `external/neo-riscv-vm/crates/neo-riscv-devpack` | NeoVM2 / RISC-V execution profile | Developer packaging utilities for compiling and preparing RISC-V Neo contracts. |
| [`neo-riscv-guest`](../external/neo-riscv-vm/crates/neo-riscv-guest/README.md) | `external/neo-riscv-vm/crates/neo-riscv-guest` | NeoVM2 / RISC-V execution profile | Guest-side facade and contract runtime glue for NeoVM2/RISC-V contracts. |
| [`neo-riscv-guest-module`](../external/neo-riscv-vm/crates/neo-riscv-guest-module/README.md) | `external/neo-riscv-vm/crates/neo-riscv-guest-module` | NeoVM2 / RISC-V execution profile | PolkaVM guest module entrypoint that packages the guest runtime into executable RISC-V code. |
| [`neo-riscv-host`](../external/neo-riscv-vm/crates/neo-riscv-host/README.md) | `external/neo-riscv-vm/crates/neo-riscv-host` | NeoVM2 / RISC-V execution profile | Host runtime that executes PolkaVM guest modules, accounts gas, and bridges syscalls. |
| [`counter`](../external/neo-riscv-vm/examples/counter/README.md) | `external/neo-riscv-vm/examples/counter` | NeoVM2 / RISC-V execution profile | Minimal state-changing counter contract example. |
| [`devpack-test`](../external/neo-riscv-vm/examples/devpack-test/README.md) | `external/neo-riscv-vm/examples/devpack-test` | NeoVM2 / RISC-V execution profile | Development pack smoke-test contract. |
| [`hello-world`](../external/neo-riscv-vm/examples/hello-world/README.md) | `external/neo-riscv-vm/examples/hello-world` | NeoVM2 / RISC-V execution profile | Small hello-world contract example for the RISC-V toolchain. |
| [`nep17-token`](../external/neo-riscv-vm/examples/nep17-token/README.md) | `external/neo-riscv-vm/examples/nep17-token` | NeoVM2 / RISC-V execution profile | NEP-17 style token contract example for RISC-V execution. |
| [`storage`](../external/neo-riscv-vm/examples/storage/README.md) | `external/neo-riscv-vm/examples/storage` | NeoVM2 / RISC-V execution profile | Storage-focused contract example for host syscall behavior. |
| [`neo-riscv-fuzz`](../external/neo-riscv-vm/fuzz/README.md) | `external/neo-riscv-vm/fuzz` | NeoVM2 / RISC-V execution profile | Fuzzing support for RISC-V VM execution, ABI codecs, and host/guest boundaries. |
| [`neo-contract-template`](../external/neo-riscv-vm/templates/contract/README.md) | `external/neo-riscv-vm/templates/contract` | NeoVM2 / RISC-V execution profile | Starter template for new Neo RISC-V contracts. |
| [`neo-vm-rs`](../external/neo-vm-rs/README.md) | `external/neo-vm-rs` | Shared VM core | Canonical Rust implementation of NeoVM 3.9.x semantics shared by RISC-V and zkVM paths. |
| [`neo-vm-guest`](../external/neo-zkvm/crates/neo-vm-guest/README.md) | `external/neo-zkvm/crates/neo-vm-guest` | zkVM guest facade | Guest-facing adapter that exposes shared NeoVM execution APIs in zkVM-compatible form. |
| [`neo-zkvm-cli`](../external/neo-zkvm/crates/neo-zkvm-cli/README.md) | `external/neo-zkvm/crates/neo-zkvm-cli` | Neo zkVM stack | CLI and developer tooling for assembling, inspecting, proving, and verifying Neo zkVM programs. |
| [`neo-zkvm-examples`](../external/neo-zkvm/crates/neo-zkvm-examples/README.md) | `external/neo-zkvm/crates/neo-zkvm-examples` | Neo zkVM stack | Runnable examples that demonstrate common proof flows and application patterns. |
| [`neo-zkvm-program`](../external/neo-zkvm/crates/neo-zkvm-program/README.md) | `external/neo-zkvm/crates/neo-zkvm-program` | Neo zkVM stack | SP1 guest binary entrypoint that binds proof inputs to deterministic NeoVM execution. |
| [`neo-zkvm-prover`](../external/neo-zkvm/crates/neo-zkvm-prover/README.md) | `external/neo-zkvm/crates/neo-zkvm-prover` | Neo zkVM stack | Proof generation library that turns NeoVM execution inputs into verifiable proof artifacts. |
| [`neo-zkvm-verifier`](../external/neo-zkvm/crates/neo-zkvm-verifier/README.md) | `external/neo-zkvm/crates/neo-zkvm-verifier` | Neo zkVM stack | Verifier library that checks proof envelopes, public outputs, and mode compatibility. |
| [`neo-zkvm-fuzz`](../external/neo-zkvm/fuzz/README.md) | `external/neo-zkvm/fuzz` | Neo zkVM stack | Fuzzing workspace for adversarial proof and VM input exploration. |
| [`neo-n4-sdk`](../sdk/rust/README.md) | `sdk/rust` | Developer SDK | Rust client SDK for building tools and services that talk to Neo N4 APIs. |
| [`neo-bridge-watcher-eth`](../watchers/neo-bridge-watcher-eth/README.md) | `watchers/neo-bridge-watcher-eth` | Cross-chain watcher | Observes ETH bridge events and turns them into normalized Neo N4 relay messages. |
| [`neo-bridge-watcher-sol`](../watchers/neo-bridge-watcher-sol/README.md) | `watchers/neo-bridge-watcher-sol` | Cross-chain watcher | Observes SOL bridge events and turns them into normalized Neo N4 relay messages. |
| [`neo-bridge-watcher-tron`](../watchers/neo-bridge-watcher-tron/README.md) | `watchers/neo-bridge-watcher-tron` | Cross-chain watcher | Observes TRON bridge events and turns them into normalized Neo N4 relay messages. |
