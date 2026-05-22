# NeoVM Layer Boundary Cleanup Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` or `superpowers:subagent-driven-development` to execute this plan task by task. Keep each task verifiable before moving to the next one.

## Goal

Clean the VM stack layering so shared NeoVM semantics live in `neo-vm-rs`, while `neo-riscv-vm` and `neo-zkvm` retain only their runtime, proving, host, and tooling responsibilities.

## Current Boundary

- `neo-vm-rs` owns canonical stack values, opcode metadata, interpreter behavior, syscall metadata, and runtime semantic helpers.
- `neo-riscv-vm` owns PolkaVM/RISC-V guest integration, contract runtime glue, native contract wrappers, tooling, and generated module execution.
- `neo-zkvm` owns SP1 proving, witness orchestration, proof fixtures, and zkVM-specific CLI workflows.

## Tasks

1. Downshift reusable StackValue extraction helpers into `neo-vm-rs`.
   - Add shared helpers for bool, signed integer, bounded unsigned integers, byte vectors, fixed-size byte arrays, UTF-8 strings, and array/struct item extraction.
   - Add tests in `neo-vm-rs` proving NeoVM integer and byte-sequence edge cases.
2. Update `neo-riscv-abi` to re-export the shared helper API.
   - Keep ABI crate as the stable facade for RISC-V downstream crates.
   - Avoid introducing direct devpack coupling to `neo-vm-rs`.
3. Slim `neo-riscv-devpack` native wrappers.
   - Remove local StackValue extraction implementations.
   - Import shared helpers from `neo-riscv-abi`.
   - Keep contract-call stack construction and host invocation local to devpack.
4. Remove unused placeholder modules.
   - Delete empty or future-only modules that are not part of the current API.
   - Verify no public imports or docs still reference them.
5. Validate the slice.
   - Run targeted `neo-vm-rs` tests.
   - Run targeted `neo-riscv-vm` tests for ABI and devpack crates.
   - Run formatting checks on modified Rust workspaces.

## Completion Criteria

- StackValue extraction behavior has one implementation in `neo-vm-rs`.
- RISC-V devpack native contract wrappers use shared extraction helpers through `neo-riscv-abi`.
- Placeholder-only source files are removed or replaced with real code.
- Modified crates pass targeted tests and formatting checks.

## Execution Status

Completed on 2026-05-22:

- `neo-vm-rs@408a4279b7484051ee1dea5a0d752e2dcc0d5304` now owns shared StackValue extraction helpers for native-contract result decoding.
- `neo-riscv-vm@efa66ff1f71fc8d0499a402f95b716c5474c71c2` re-exports those helpers through `neo-riscv-abi`, removes the duplicated devpack extraction implementations, deletes the empty `macros.rs` placeholder, and pins the new shared VM core revision.
- `neo-zkvm@0ef5f14139ffa980e0f4b66bfd84da5b4c41bd9f` is pinned to the same shared VM core revision for cross-runtime consistency.

Validation completed:

- `cargo fmt --all -- --check` in `external/neo-vm-rs`
- `cargo test --locked` in `external/neo-vm-rs`
- `cargo clippy --locked --all-targets -- -D warnings` in `external/neo-vm-rs`
- `cargo fmt --all -- --check` in `external/neo-riscv-vm`
- `cargo test --locked` in `external/neo-riscv-vm`
- `cargo test --workspace --locked --exclude neo-riscv-guest-module` in `external/neo-riscv-vm`
- `cargo clippy --workspace --locked --exclude neo-riscv-guest-module --all-targets -- -D warnings` in `external/neo-riscv-vm`
- `cargo fmt --all -- --check` in `external/neo-zkvm`
- `cargo test --workspace --locked` in `external/neo-zkvm`
- `cargo clippy --workspace --locked --all-targets -- -D warnings` in `external/neo-zkvm`
- `cargo fmt --all -- --check` in the parent `neo-n4` workspace
- `cargo test --workspace --locked` in the parent `neo-n4` workspace
- `cargo clippy --workspace --locked --all-targets -- -D warnings` in the parent `neo-n4` workspace
