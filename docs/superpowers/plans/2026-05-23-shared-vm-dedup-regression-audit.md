# Shared VM Dedup Regression Audit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Re-audit `neo-vm-rs`, `neo-rs`, `neo-riscv-vm`, and `neo-zkvm` so shared NeoVM types and behavior live in `neo-vm-rs`, downstream repositories do not carry duplicate NeoVM implementations, and prior consistency bugs stay fixed.

**Architecture:** Treat `neo-vm-rs` as the canonical shared VM semantics crate. Keep downstream host, prover, ABI, and integration layers in their own repositories, but require them to import shared VM-facing types, opcode metadata, execution states, result codecs, and semantic helpers from `neo-vm-rs`.

**Tech Stack:** Rust 2021 Cargo workspaces, C#/.NET solution tests, git submodules under `D:/Git/neo-n4/external`, `rg` source scans, focused regression tests, Clippy.

---

### Task 1: Establish Clean Baseline

**Files:**
- Inspect: `D:/Git/neo-vm-rs`
- Inspect: `D:/Git/neo-rs`
- Inspect: `D:/Git/neo-n4`
- Inspect: `D:/Git/neo-n4/external/neo-riscv-vm`
- Inspect: `D:/Git/neo-n4/external/neo-zkvm`

- [x] Fetch every repository from `r3e-network`.
- [x] Confirm every repository is clean and aligned to its origin branch.
- [x] Confirm `neo-n4` submodule pointers match the intended shared VM revisions.

### Task 2: Scan for Duplicate Shared VM Types and Semantics

**Files:**
- Inspect: `D:/Git/neo-vm-rs/src`
- Inspect: `D:/Git/neo-rs/neo-core/src/neo_vm`
- Inspect: `D:/Git/neo-rs/tests/tests/no_local_neo_vm_dependency.rs`
- Inspect: `D:/Git/neo-n4/external/neo-riscv-vm/crates`
- Inspect: `D:/Git/neo-n4/external/neo-zkvm/crates`

- [x] Search for local `OpCode`, `VmState`, `StackValue`, `StackItemType`, `Instruction`, execution-result, syscall-hash, and NeoVM interpreter definitions outside `neo-vm-rs`.
- [x] Categorize remaining matches as legitimate host/runtime glue, downstream facade re-export, or duplicate implementation.
- [x] If duplicates remain, remove them or redirect them to `neo-vm-rs`.

### Task 3: Verify Prior Bug Fixes Are Still Guarded

**Files:**
- Inspect/Test: `D:/Git/neo-vm-rs/tests`
- Inspect/Test: `D:/Git/neo-rs/tests/tests/no_local_neo_vm_dependency.rs`
- Inspect/Test: `D:/Git/neo-n4/external/neo-riscv-vm/crates/neo-riscv-host/tests`

- [x] Confirm regression coverage for Integer/Boolean `SIZE` and `PICKITEM`.
- [x] Confirm `VmState` preserves Neo C# states `None`, `Halt`, `Fault`, and `Break`.
- [x] Confirm `StackItemType` byte mappings remain canonical.
- [x] Confirm downstream host code treats non-final VM states explicitly instead of assuming only `Halt`/`Fault`.

### Task 4: Run Verification Matrix

**Files:**
- Verify all modified workspaces.

- [x] Run `cargo test`, `cargo test --no-default-features`, and Clippy in `neo-vm-rs`.
- [x] Run sentinel and focused checks in `neo-rs`.
- [x] Run `cargo test` and Clippy in `neo-riscv-vm`.
- [x] Run `cargo test` and Clippy in `neo-zkvm`.
- [x] Run top-level `neo-n4` Rust and .NET verification.

### Task 5: Publish

**Files:**
- Modify only files needed for the audit/fixes.

- [x] Commit and push changed leaf repositories first.
- [x] Update `neo-n4` submodule pointers and lockfiles when leaf revisions change.
- [x] Commit and push `neo-n4`.
- [x] Confirm final worktrees are clean and aligned to `r3e-network` origin.

## Completion Evidence

- `neo-vm-rs`: pushed `e95512c6a0260b2412a1207831d0d47b06d1dd1f`, adding canonical `OpCode::from_name` metadata lookup and regression coverage.
- `neo-zkvm`: pushed `3beba42f6f5849bb57d00a4787e38d7ae8b5ee11`, replacing CLI assembler hardcoded opcode byte tables with `neo-vm-rs::OpCode`.
- `neo-riscv-vm`: pushed `03eb7fa451ee590a52df1af6d4ab2b11df587c39`, updating the shared VM dependency to the canonical revision.
- `neo-rs`: pushed `9bad204e`, adapting direct shared `Instruction` use and semantic RPC fault assertions.
- `neo-n4`: updated submodule pointers and root `Cargo.lock` to the same shared VM revision.

Validation commands completed:

- `D:/Git/neo-vm-rs`: `cargo test`, `cargo test --no-default-features`, `cargo clippy --all-targets --all-features -- -D warnings`.
- `D:/Git/neo-rs`: full `cargo test` with `LIBCLANG_PATH` and `CXXFLAGS`, plus `cargo test -p neo-tests --test no_local_neo_vm_dependency -- --nocapture`.
- `D:/Git/neo-n4/external/neo-riscv-vm`: `cargo test`, `cargo clippy --all-targets --all-features -- -D warnings`.
- `D:/Git/neo-n4/external/neo-zkvm`: `cargo test`, Windows `cargo clippy --all-targets -- -D warnings`, and WSL2/Linux `cargo clippy --all-targets --all-features -- -D warnings`.
- `D:/Git/neo-n4`: `cargo test`, `cargo clippy --all-targets -- -D warnings`, `dotnet test Neo.L2.sln --configuration Release --nologo`.

Notes:

- Windows `neo-zkvm --all-features` Clippy is not a valid target because upstream `sp1-jit` requires POSIX `std::os::fd`, shared memory, and semaphore APIs. The all-features proof stack was validated through WSL2/Linux with the Windows proxy exposed as `http://172.31.160.1:7890`.
- The remaining `neo-core::neo_vm` module in `neo-rs` is a compatibility namespace, not a local VM implementation. Sentinel tests enforce direct `neo_vm_rs` imports for canonical VM types and semantics.
