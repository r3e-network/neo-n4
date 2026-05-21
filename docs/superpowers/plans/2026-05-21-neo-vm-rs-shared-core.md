# Neo VM Rs Shared Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create a shared `neo-vm-rs` Rust core so `neo-zkvm` and `neo-riscv-vm` stop carrying divergent NeoVM opcode, stack value, execution result, and syscall semantics.

**Architecture:** The first migration stage makes `neo-vm-rs` a stable, `no_std + alloc` semantics crate with canonical NeoVM data types. `neo-zkvm` and `neo-riscv-vm` then import those canonical types through compatibility aliases, leaving SP1 proving and PolkaVM host execution in their own repositories.

**Tech Stack:** Rust 2021, `serde` with `alloc`, `sha2` without default features, `r3e-network/neo-vm-rs` pinned by git revision in downstream workspaces.

---

### Task 1: Stabilize `neo-vm-rs` Shared API

**Files:**
- Modify: `D:/Git/neo-n4/external/neo-vm-rs/Cargo.toml`
- Modify: `D:/Git/neo-n4/external/neo-vm-rs/src/lib.rs`
- Create: `D:/Git/neo-n4/external/neo-vm-rs/src/opcode.rs`
- Create: `D:/Git/neo-n4/external/neo-vm-rs/src/stack_value.rs`
- Create: `D:/Git/neo-n4/external/neo-vm-rs/src/execution.rs`
- Create: `D:/Git/neo-n4/external/neo-vm-rs/src/syscall.rs`
- Create: `D:/Git/neo-n4/external/neo-vm-rs/src/limits.rs`
- Create: `D:/Git/neo-n4/external/neo-vm-rs/tests/shared_semantics.rs`

- [x] Replace the old unstable crate root with a small `no_std + alloc` public API.
- [x] Add canonical NeoVM opcode metadata copied from the RISC-V path because it has the complete slot opcode range.
- [x] Add shared `StackValue`, `VmState`, `BackendKind`, and `ExecutionResult`.
- [x] Add shared `interop_hash` and `syscall_arg_count`.
- [x] Run `cargo test` in `external/neo-vm-rs`.

### Task 2: Make `neo-riscv-vm` Consume Shared Types

**Files:**
- Modify: `D:/Git/neo-n4/external/neo-riscv-vm/Cargo.toml`
- Modify: `D:/Git/neo-n4/external/neo-riscv-vm/crates/neo-riscv-abi/Cargo.toml`
- Modify: `D:/Git/neo-n4/external/neo-riscv-vm/crates/neo-riscv-abi/src/lib.rs`

- [x] Add `neo-vm-rs` pinned to `r3e-network/neo-vm-rs@05fa120` at workspace level.
- [x] Re-export `BackendKind`, `ExecutionResult`, `StackValue`, `VmState`, `interop_hash`, and `syscall_arg_count` from `neo-vm-rs`.
- [x] Keep `neo-riscv-abi` codec modules unchanged so existing wire formats remain compatible.
- [x] Run `cargo test -p neo-riscv-abi -p neo-riscv-guest`.

### Task 3: Make `neo-zkvm` Consume Shared Semantic Types

**Files:**
- Modify: `D:/Git/neo-n4/external/neo-zkvm/Cargo.toml`
- Modify: `D:/Git/neo-n4/external/neo-zkvm/crates/neo-vm-core/Cargo.toml`
- Modify: `D:/Git/neo-n4/external/neo-zkvm/crates/neo-vm-core/src/opcode.rs`

- [x] Add git dependency on `neo-vm-rs`.
- [x] Replace the local `OpCode` definition with a re-export of `neo_vm_rs::OpCode`.
- [x] Leave `NeoVM`, storage, and native-contract code in `neo-vm-core` for this stage.
- [x] Run `cargo test -p neo-vm-core --locked`.

### Task 4: Parent Repository Integration

**Files:**
- Modify: `D:/Git/neo-n4/.gitmodules`
- Add submodule: `D:/Git/neo-n4/external/neo-vm-rs`

- [x] Keep the submodule URL under `https://github.com/r3e-network/neo-vm-rs.git`.
- [x] Verify `git submodule status --recursive` includes `external/neo-vm-rs`.

### Task 5: Cross-Repo Validation

**Commands:**
- `cargo test` in `D:/Git/neo-n4/external/neo-vm-rs`
- `cargo test -p neo-riscv-abi -p neo-riscv-guest` in `D:/Git/neo-n4/external/neo-riscv-vm`
- `cargo test -p neo-vm-core --locked` in `D:/Git/neo-n4/external/neo-zkvm`
- `cargo run --locked --bin neo-zkvm -- run 12139E40` in `D:/Git/neo-n4/external/neo-zkvm`

- [x] Confirm all commands exit 0.
- [x] Record remaining gap: full interpreter migration is stage two, after shared semantics are live in both consumers.

**Validation completed:** `cargo test --locked` in `neo-vm-rs`, `cargo test -p neo-riscv-abi -p neo-riscv-guest --locked` in `neo-riscv-vm`, `cargo test --workspace --locked --exclude neo-riscv-devpack --exclude neo-riscv-guest-module` in `neo-riscv-vm`, `cargo test -p neo-vm-core --locked` in `neo-zkvm`, `cargo test --workspace --locked` in `neo-zkvm`, and `cargo run --locked --bin neo-zkvm -- run 12139E40` all exited 0 after downstream workspaces were switched to `r3e-network/neo-vm-rs@05fa120`.

**Windows validation boundary:** full `cargo test --workspace --locked` in `neo-riscv-vm` still fails on Windows for existing PolkaVM guest/devpack test targets because `host_call` / `host_on_instruction` are unresolved host symbols when linked as native test binaries. The shared ABI migration path was validated by the targeted ABI/guest tests and by the workspace run excluding those two known host-bound targets.

**Top-level validation boundary:** full `cargo test --workspace --locked` in `neo-n4` still fails on Windows because `bridge/neo-zkvm-host` pulls `sp1-jit`, which imports POSIX `std::os::fd` and shared-memory APIs unavailable on the Windows native target. `cargo test --workspace --locked --exclude neo-zkvm-host` exited 0.

**Stage-two gap:** `neo-zkvm` now exposes canonical opcode metadata through `neo-vm-rs`, but its execution engine still contains legacy crypto pseudo-opcodes (`0xF0..0xF3`). Those need a dedicated migration to canonical NeoVM syscall/native-contract behavior because `0xF1` conflicts with canonical `THROWIFNOT`.
