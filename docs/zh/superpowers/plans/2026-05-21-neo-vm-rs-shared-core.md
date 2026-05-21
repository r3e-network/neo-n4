# Neo VM Rs 共享核心实施计划

> **给 agent worker 的要求：** 实施本计划时必须使用 `superpowers:subagent-driven-development`（推荐）或 `superpowers:executing-plans`，并按任务逐项执行。任务使用 checkbox (`- [ ]`) 跟踪状态。

**目标：** 创建共享的 `neo-vm-rs` Rust core，让 `neo-zkvm` 与 `neo-riscv-vm` 不再维护分歧的 NeoVM opcode、栈值、执行结果和 syscall 语义。

**架构：** 第一阶段迁移将 `neo-vm-rs` 做成稳定的 `no_std + alloc` 语义 crate，提供规范 NeoVM 数据类型。随后 `neo-zkvm` 与 `neo-riscv-vm` 通过兼容 alias 导入这些规范类型，而 SP1 proving 与 PolkaVM host execution 继续保留在各自仓库中。

**技术栈：** Rust 2021、启用 `alloc` 的 `serde`、不启用默认 feature 的 `sha2`、下游 workspace 通过 git revision 固定到 `r3e-network/neo-vm-rs`。

---

### 任务 1：稳定 `neo-vm-rs` 共享 API

**文件：**
- 修改：`D:/Git/neo-n4/external/neo-vm-rs/Cargo.toml`
- 修改：`D:/Git/neo-n4/external/neo-vm-rs/src/lib.rs`
- 新建：`D:/Git/neo-n4/external/neo-vm-rs/src/opcode.rs`
- 新建：`D:/Git/neo-n4/external/neo-vm-rs/src/stack_value.rs`
- 新建：`D:/Git/neo-n4/external/neo-vm-rs/src/execution.rs`
- 新建：`D:/Git/neo-n4/external/neo-vm-rs/src/syscall.rs`
- 新建：`D:/Git/neo-n4/external/neo-vm-rs/src/limits.rs`
- 新建：`D:/Git/neo-n4/external/neo-vm-rs/tests/shared_semantics.rs`

- [x] 用小型 `no_std + alloc` 公共 API 替换旧的不稳定 crate root。
- [x] 从 RISC-V 路径复制规范 NeoVM opcode 元数据，因为该路径拥有完整 slot opcode 范围。
- [x] 添加共享的 `StackValue`、`VmState`、`BackendKind` 和 `ExecutionResult`。
- [x] 添加共享的 `interop_hash` 和 `syscall_arg_count`。
- [x] 在 `external/neo-vm-rs` 中运行 `cargo test`。

### 任务 2：让 `neo-riscv-vm` 使用共享类型

**文件：**
- 修改：`D:/Git/neo-n4/external/neo-riscv-vm/Cargo.toml`
- 修改：`D:/Git/neo-n4/external/neo-riscv-vm/crates/neo-riscv-abi/Cargo.toml`
- 修改：`D:/Git/neo-n4/external/neo-riscv-vm/crates/neo-riscv-abi/src/lib.rs`

- [x] 在 workspace 层添加固定到 `r3e-network/neo-vm-rs@05fa120` 的 `neo-vm-rs`。
- [x] 从 `neo-vm-rs` re-export `BackendKind`、`ExecutionResult`、`StackValue`、`VmState`、`interop_hash` 和 `syscall_arg_count`。
- [x] 保持 `neo-riscv-abi` codec 模块不变，确保已有 wire format 兼容。
- [x] 运行 `cargo test -p neo-riscv-abi -p neo-riscv-guest`。

### 任务 3：让 `neo-zkvm` 使用共享语义类型

**文件：**
- 修改：`D:/Git/neo-n4/external/neo-zkvm/Cargo.toml`
- 修改：`D:/Git/neo-n4/external/neo-zkvm/crates/neo-vm-core/Cargo.toml`
- 修改：`D:/Git/neo-n4/external/neo-zkvm/crates/neo-vm-core/src/opcode.rs`

- [x] 添加对 `neo-vm-rs` 的 git dependency。
- [x] 用 `neo_vm_rs::OpCode` 的 re-export 替换本地 `OpCode` 定义。
- [x] 本阶段继续将 `NeoVM`、storage 和 native-contract 代码保留在 `neo-vm-core`。
- [x] 运行 `cargo test -p neo-vm-core --locked`。

### 任务 4：父仓库集成

**文件：**
- 修改：`D:/Git/neo-n4/.gitmodules`
- 添加 submodule：`D:/Git/neo-n4/external/neo-vm-rs`

- [x] 保持 submodule URL 为 `https://github.com/r3e-network/neo-vm-rs.git`。
- [x] 确认 `git submodule status --recursive` 包含 `external/neo-vm-rs`。

### 任务 5：跨仓库验证

**命令：**
- 在 `D:/Git/neo-n4/external/neo-vm-rs` 中运行 `cargo test`
- 在 `D:/Git/neo-n4/external/neo-riscv-vm` 中运行 `cargo test -p neo-riscv-abi -p neo-riscv-guest`
- 在 `D:/Git/neo-n4/external/neo-zkvm` 中运行 `cargo test -p neo-vm-core --locked`
- 在 `D:/Git/neo-n4/external/neo-zkvm` 中运行 `cargo run --locked --bin neo-zkvm -- run 12139E40`

- [x] 确认所有命令退出码为 0。
- [x] 记录剩余缺口：完整解释器迁移是第二阶段，需在共享语义已在两个消费者中生效后执行。

**验证完成：** 下游 workspace 切换到 `r3e-network/neo-vm-rs@05fa120` 后，`neo-vm-rs` 中的 `cargo test --locked`、`neo-riscv-vm` 中的 `cargo test -p neo-riscv-abi -p neo-riscv-guest --locked`、`neo-riscv-vm` 中排除两个已知 host-bound 目标的 workspace 测试、`neo-zkvm` 中的 `cargo test -p neo-vm-core --locked`、`neo-zkvm` 中的 `cargo test --workspace --locked`，以及 `cargo run --locked --bin neo-zkvm -- run 12139E40` 均退出 0。

**Windows 验证边界：** `neo-riscv-vm` 在 Windows 上完整运行 `cargo test --workspace --locked` 仍会因为既有 PolkaVM guest/devpack 测试目标失败，这些目标作为原生测试二进制链接时缺少 `host_call` / `host_on_instruction` host symbol。共享 ABI 迁移路径已通过定向 ABI/guest 测试，以及排除两个已知 host-bound 目标的 workspace run 验证。

**顶层验证边界：** `neo-n4` 在 Windows 上完整运行 `cargo test --workspace --locked` 仍会因为 `bridge/neo-zkvm-host` 拉入 `sp1-jit` 而失败；该依赖使用 Windows 原生 target 不支持的 POSIX `std::os::fd` 和 shared-memory API。`cargo test --workspace --locked --exclude neo-zkvm-host` 已退出 0。

**第二阶段缺口：** `neo-zkvm` 现在通过 `neo-vm-rs` 暴露规范 opcode 元数据，但其执行引擎仍包含 legacy crypto pseudo-opcode（`0xF0..0xF3`）。这些 opcode 需要单独迁移到规范 NeoVM syscall/native-contract 行为，因为 `0xF1` 与规范 `THROWIFNOT` 冲突。
