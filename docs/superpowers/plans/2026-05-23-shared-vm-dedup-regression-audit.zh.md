# 共享 VM 去重回归审计实施计划

> **给 agentic workers：** 必须使用 `superpowers:subagent-driven-development`（推荐）或 `superpowers:executing-plans` 按任务执行。本文件记录本轮审计目标、边界、验证矩阵和完成证据。

**目标：** 重新审计 `neo-vm-rs`、`neo-rs`、`neo-riscv-vm` 和 `neo-zkvm`，确保共享 NeoVM 类型和行为统一沉淀到 `neo-vm-rs`；下游仓库不再携带重复 NeoVM 实现；此前一致性验证中发现的 bug 继续被回归测试保护。

**架构：** 将 `neo-vm-rs` 作为唯一的共享 VM 语义 crate。下游 host、prover、ABI 和集成层保留各自职责，但必须从 `neo-vm-rs` 导入共享 VM 类型、opcode 元数据、执行状态、结果 codec 和语义 helper。

**技术栈：** Rust 2021 Cargo workspaces、C#/.NET solution tests、`D:/Git/neo-n4/external` 下的 git submodules、`rg` 源码扫描、聚焦回归测试、Clippy。

---

### 任务 1：建立干净基线

**文件：**
- 检查：`D:/Git/neo-vm-rs`
- 检查：`D:/Git/neo-rs`
- 检查：`D:/Git/neo-n4`
- 检查：`D:/Git/neo-n4/external/neo-riscv-vm`
- 检查：`D:/Git/neo-n4/external/neo-zkvm`

- [x] 从 `r3e-network` 拉取所有仓库。
- [x] 确认每个仓库干净，并与 origin 分支对齐。
- [x] 确认 `neo-n4` 的 submodule 指针匹配目标共享 VM 修订。

### 任务 2：扫描重复的共享 VM 类型与语义

**文件：**
- 检查：`D:/Git/neo-vm-rs/src`
- 检查：`D:/Git/neo-rs/neo-core/src/neo_vm`
- 检查：`D:/Git/neo-rs/tests/tests/no_local_neo_vm_dependency.rs`
- 检查：`D:/Git/neo-n4/external/neo-riscv-vm/crates`
- 检查：`D:/Git/neo-n4/external/neo-zkvm/crates`

- [x] 搜索 `neo-vm-rs` 之外的本地 `OpCode`、`VmState`、`StackValue`、`StackItemType`、`Instruction`、执行结果、syscall hash 和 NeoVM interpreter 定义。
- [x] 将剩余命中分类为合法 host/runtime glue、下游 facade re-export 或重复实现。
- [x] 若存在重复实现，则删除或改为转发到 `neo-vm-rs`。

### 任务 3：确认历史 bug 修复仍有保护

**文件：**
- 检查/测试：`D:/Git/neo-vm-rs/tests`
- 检查/测试：`D:/Git/neo-rs/tests/tests/no_local_neo_vm_dependency.rs`
- 检查/测试：`D:/Git/neo-n4/external/neo-riscv-vm/crates/neo-riscv-host/tests`

- [x] 确认 Integer/Boolean 的 `SIZE` 和 `PICKITEM` 有回归覆盖。
- [x] 确认 `VmState` 保留 Neo C# 状态 `None`、`Halt`、`Fault`、`Break`。
- [x] 确认 `StackItemType` 字节映射保持 canonical。
- [x] 确认下游 host 代码显式处理非 final VM 状态，而不是只假定 `Halt`/`Fault`。

### 任务 4：运行验证矩阵

**文件：**
- 验证所有修改过的 workspace。

- [x] 在 `neo-vm-rs` 运行 `cargo test`、`cargo test --no-default-features` 和 Clippy。
- [x] 在 `neo-rs` 运行哨兵测试和聚焦检查。
- [x] 在 `neo-riscv-vm` 运行 `cargo test` 和 Clippy。
- [x] 在 `neo-zkvm` 运行 `cargo test` 和 Clippy。
- [x] 在 `neo-n4` 顶层运行 Rust 和 .NET 验证。

### 任务 5：发布

**文件：**
- 只修改审计和修复所必需的文件。

- [x] 先提交并推送 leaf repositories。
- [x] leaf revision 变化后，更新 `neo-n4` 的 submodule 指针和 lockfile。
- [x] 提交并推送 `neo-n4`。
- [x] 确认最终 worktree 干净并与 `r3e-network` origin 对齐。

## 完成证据

- `neo-vm-rs`：已推送 `e95512c6a0260b2412a1207831d0d47b06d1dd1f`，新增 canonical `OpCode::from_name` 元数据查找和回归覆盖。
- `neo-zkvm`：已推送 `3beba42f6f5849bb57d00a4787e38d7ae8b5ee11`，将 CLI assembler 的硬编码 opcode byte table 改为使用 `neo-vm-rs::OpCode`。
- `neo-riscv-vm`：已推送 `03eb7fa451ee590a52df1af6d4ab2b11df587c39`，更新共享 VM 依赖到 canonical revision。
- `neo-rs`：已推送 `9bad204e`，适配直接使用共享 `Instruction`，并将 RPC fault 断言改成语义断言。
- `neo-n4`：已更新 submodule 指针和根 `Cargo.lock`，指向同一个共享 VM revision。

已完成的验证命令：

- `D:/Git/neo-vm-rs`：`cargo test`、`cargo test --no-default-features`、`cargo clippy --all-targets --all-features -- -D warnings`。
- `D:/Git/neo-rs`：设置 `LIBCLANG_PATH` 和 `CXXFLAGS` 后运行完整 `cargo test`，以及 `cargo test -p neo-tests --test no_local_neo_vm_dependency -- --nocapture`。
- `D:/Git/neo-n4/external/neo-riscv-vm`：`cargo test`、`cargo clippy --all-targets --all-features -- -D warnings`。
- `D:/Git/neo-n4/external/neo-zkvm`：`cargo test`、Windows `cargo clippy --all-targets -- -D warnings`、WSL2/Linux `cargo clippy --all-targets --all-features -- -D warnings`。
- `D:/Git/neo-n4`：`cargo test`、`cargo clippy --all-targets -- -D warnings`、`dotnet test Neo.L2.sln --configuration Release --nologo`。

说明：

- Windows 下 `neo-zkvm --all-features` Clippy 不是有效目标，因为上游 `sp1-jit` 需要 POSIX `std::os::fd`、shared memory 和 semaphore API。all-features proof stack 已通过 WSL2/Linux 验证，代理使用 Windows gateway `http://172.31.160.1:7890`。
- `neo-rs` 中剩余的 `neo-core::neo_vm` 模块是兼容命名空间，不是本地 VM 实现。哨兵测试强制 canonical VM 类型和语义直接从 `neo_vm_rs` 导入。
