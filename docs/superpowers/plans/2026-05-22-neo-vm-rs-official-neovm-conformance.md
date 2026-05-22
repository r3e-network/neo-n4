# NeoVM Rs Official NeoVM Conformance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Validate `neo-vm-rs` against the official Neo N3 runtime VM baseline, currently `neo-node v3.9.2` -> `Neo v3.9.1` -> `Neo.VM v3.9.0`.

**Architecture:** Keep `neo-vm-rs` as the canonical Rust implementation, but add an automated conformance suite sourced from the official `neo-project/neo-vm v3.9.0` VMUT JSON tests. The suite converts official script tokens into bytecode, executes them through `neo-vm-rs`, and compares final VM state plus result stack against the official expected output.

**Tech Stack:** Rust 2021, `serde_json`, official Neo.VM VMUT JSON fixtures, `cargo test`, `cargo clippy`.

---

### Task 1: Preserve Official Baseline Evidence

**Files:**
- Create: `D:/Git/neo-n4/external/neo-vm-rs/tests/fixtures/official-neo-vm-3.9.0/README.md`
- Copy: `D:/Git/neo-n4/artifacts/upstream/neo-vm-v3.9.0/tests/Neo.VM.Tests/Tests/**/*.json` to `D:/Git/neo-n4/external/neo-vm-rs/tests/fixtures/official-neo-vm-3.9.0/Tests/**/*.json`

- [x] Record that `neo-node v3.9.2` is the latest observed official node tag and depends on Neo core `v3.9.1`.
- [x] Record that Neo core `v3.9.1` depends on NuGet `Neo.VM` `3.9.0`.
- [x] Copy the official VMUT JSON tests into the `neo-vm-rs` test fixtures with a source README.

### Task 2: Add Official VMUT Runner

**Files:**
- Create: `D:/Git/neo-n4/external/neo-vm-rs/tests/official_neo_vm_3_9_vmut.rs`

- [x] Add a failing integration test that recursively loads the copied official JSON files.
- [x] Parse VMUT script arrays by mapping official opcode names through `neo_vm_rs::OpCode` and appending raw `0x` bytes.
- [x] Execute final `HALT` or `FAULT` cases through `neo_vm_rs::interpret_with_stack_and_syscalls`.
- [x] Compare final VM state and top-first result stack ordering against the official fixture.
- [x] Support official result item types: Null, Boolean, Integer, ByteString, Buffer, Array, Struct, Map, Pointer, and Interop.

### Task 3: Validate and Fix Drift

**Files:**
- Modify if required: `D:/Git/neo-n4/external/neo-vm-rs/src/**/*.rs`
- Modify if required: `D:/Git/neo-n4/external/neo-vm-rs/tests/official_neo_vm_3_9_vmut.rs`

- [x] Run the official VMUT suite.
- [x] If failures are parser limitations, fix the parser.
- [x] If failures are `neo-vm-rs` semantic drift, fix the VM implementation with focused tests.
- [x] Keep unsupported step-only `BREAK` debugger cases skipped and counted, because `neo-vm-rs` does not expose a step debugger API.

### Task 4: Full Verification and Publication

**Files:**
- Modify: `D:/Git/neo-n4/external/neo-vm-rs`
- Modify: `D:/Git/neo-n4` parent submodule pointer if `neo-vm-rs` changes are committed.

- [x] Run `cargo fmt --all -- --check` in `external/neo-vm-rs`.
- [x] Run `cargo test --locked` in `external/neo-vm-rs`.
- [x] Run `cargo clippy --locked --all-targets -- -D warnings` in `external/neo-vm-rs`.
- [x] Run `cargo check --locked --no-default-features --all-targets` in `external/neo-vm-rs`.
- [x] Run `cargo bench --locked --no-run` in `external/neo-vm-rs`.
- [x] Commit and push to `r3e-network/neo-vm-rs`.
- [x] Update and verify `neo-riscv-vm` and `neo-zkvm` against the new shared VM revision.
- [x] Update and verify the parent `r3e-network/neo-n4` submodule pointer.
