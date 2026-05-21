# NeoVM Rust Full Interpreter Extraction Plan

## Goal

Turn `neo-vm-rs` from a shared ABI/metadata crate into the canonical shared NeoVM2 interpreter core used by the N4 RISC-V execution profile and the zkVM verification path.

The current crate is useful but incomplete: it owns shared stack/result/opcode metadata, while the real interpreter still lives in `neo-riscv-vm/crates/neo-riscv-guest`. That leaves duplicated VM semantics and makes zkVM/RISC-V consistency hard to prove.

## Target Architecture

- `neo-vm-rs`
  - Owns stack/result ABI types.
  - Owns opcode metadata.
  - Owns the no-std + alloc interpreter.
  - Exposes `interpret`, `interpret_with_syscalls`, retained-state helpers, and the syscall provider trait.
- `neo-riscv-vm`
  - Keeps PolkaVM/RISC-V host, guest-module, tooling, and contract harness responsibilities.
  - Depends on `neo-vm-rs` for VM semantics instead of carrying a private interpreter copy.
- `neo-zkvm`
  - Uses the same shared interpreter semantics for witness generation and compatibility checks.
  - Keeps proving-system and circuit/proof orchestration separate from the interpreter core.

NeoVM2/RISC-V remains the canonical default Layer-2 VM profile. Other VM profiles such as EVM, WASM, Move, or custom profiles should plug into N4 as separate execution profiles, not replace the shared NeoVM2 core.

## Implementation Tasks

1. Add failing `neo-vm-rs` interpreter behavior tests:
   - `PUSH2 PUSH3 ADD RET` returns `HALT` and `Integer(5)`.
   - `INITSLOT`, `STLOC0`, `LDLOC0`, and `RET` preserve local variables.
   - `SYSCALL` invokes a host provider with the correct API id and mutable stack.
   - `TRY` catches syscall failures and continues through the catch path.
2. Move the interpreter modules from `neo-riscv-guest` into `neo-vm-rs`:
   - `interpreter/mod.rs`
   - `interpreter/helpers.rs`
   - `interpreter/runtime_types.rs`
   - `interpreter/opcodes.rs`
3. Adapt imports so the interpreter uses `neo-vm-rs` native ABI types directly:
   - `ExecutionResult`
   - `StackValue`
   - `VmState`
   - `syscall_arg_count`
4. Preserve no-std + alloc compatibility and the PolkaVM/riscv32 retention path.
5. Export the interpreter API from `neo-vm-rs`.
6. Update `neo-riscv-guest` to depend on and re-export `neo-vm-rs` interpreter APIs, then remove its private duplicate interpreter modules.
7. Update `neo-zkvm` references so proof/witness execution and direct interpreter execution use the same semantics where applicable.
8. Run the full validation chain before pushing:
   - `cargo fmt --all -- --check`
   - `cargo clippy --all-targets --all-features -- -D warnings`
   - `cargo test --locked --all-targets`
   - `cargo test --locked --no-default-features --all-targets`
   - downstream `neo-riscv-vm` workspace tests, excluding only known native Windows guest-module symbol-link targets when necessary
   - downstream `neo-zkvm` workspace tests and CLI smoke run

## Completion Criteria

- `neo-vm-rs` contains real interpreter implementation files, not empty folders or metadata-only definitions.
- A standalone consumer can call `neo_vm_rs::interpret(&script)` and execute NeoVM2 bytecode.
- RISC-V and zkVM paths no longer have divergent NeoVM stack/result/opcode definitions.
- All supported local checks pass with documented exceptions for platform-specific host-link targets.

## Implementation Status

Completed on 2026-05-21:

- `r3e-network/neo-vm-rs@f61f764a3e80e9f9925615e23f6c7fc6ee31ae37` owns the shared interpreter core.
- `r3e-network/neo-riscv-vm@d7d201c92cb907fe0cdf9af1bfad3625b36f331a` uses `neo-riscv-guest` as a facade over `neo-vm-rs`.
- `r3e-network/neo-zkvm@c0a39cfaa21e7eb675fbacf23ccd442617e1296e` is pinned to the same shared VM core revision.
- Parent `r3e-network/neo-n4` submodules are advanced to those commits.
