# NeoVM Rust 完整解释器抽取计划

## 目标

将 `neo-vm-rs` 从共享 ABI/元数据 crate 提升为 N4 RISC-V 执行配置和 zkVM 验证路径共同使用的规范 NeoVM2 解释器核心。

当前 crate 已经有价值但还不完整：它拥有共享的栈、结果和 opcode 元数据，而真正的解释器仍在 `neo-riscv-vm/crates/neo-riscv-guest` 中。这会造成 VM 语义重复，也让 zkVM 与 RISC-V 的一致性难以证明。

## 目标架构

- `neo-vm-rs`
  - 拥有栈和结果 ABI 类型。
  - 拥有 opcode 元数据。
  - 拥有 `no_std + alloc` 解释器。
  - 暴露 `interpret`、`interpret_with_syscalls`、retained-state helper 和 syscall provider trait。
- `neo-riscv-vm`
  - 保留 PolkaVM/RISC-V host、guest module、工具链和合约 harness 职责。
  - 依赖 `neo-vm-rs` 提供 VM 语义，不再维护私有解释器副本。
- `neo-zkvm`
  - 对 witness 生成和兼容性检查使用相同的共享解释器语义。
  - 将证明系统、circuit/proof 编排与解释器核心保持分离。

NeoVM2/RISC-V 仍然是规范默认的 Layer-2 VM profile。EVM、WASM、Move 或自定义 VM profile 应作为独立执行 profile 接入 N4，而不是替换共享 NeoVM2 核心。

## 实施任务

1. 为 `neo-vm-rs` 添加先失败的解释器行为测试：
   - `PUSH2 PUSH3 ADD RET` 返回 `HALT` 和 `Integer(5)`。
   - `INITSLOT`、`STLOC0`、`LDLOC0` 和 `RET` 保留局部变量。
   - `SYSCALL` 使用正确 API id 和可变栈调用 host provider。
   - `TRY` 捕获 syscall failure，并继续执行 catch 路径。
2. 将解释器模块从 `neo-riscv-guest` 移到 `neo-vm-rs`：
   - `interpreter/mod.rs`
   - `interpreter/helpers.rs`
   - `interpreter/runtime_types.rs`
   - `interpreter/opcodes.rs`
3. 调整 import，让解释器直接使用 `neo-vm-rs` 原生 ABI 类型：
   - `ExecutionResult`
   - `StackValue`
   - `VmState`
   - `syscall_arg_count`
4. 保持 `no_std + alloc` 兼容性，以及 PolkaVM/riscv32 retained-state 路径。
5. 从 `neo-vm-rs` 导出解释器 API。
6. 更新 `neo-riscv-guest`，让它依赖并 re-export `neo-vm-rs` 解释器 API，然后删除私有重复解释器模块。
7. 更新 `neo-zkvm` 引用，使 proof/witness 执行和直接解释器执行在适用位置使用同一套语义。
8. 推送前运行完整验证链：
   - `cargo fmt --all -- --check`
   - `cargo clippy --all-targets --all-features -- -D warnings`
   - `cargo test --locked --all-targets`
   - `cargo test --locked --no-default-features --all-targets`
   - 下游 `neo-riscv-vm` workspace 测试，仅在必要时排除已知 Windows 原生 guest-module 符号链接目标
   - 下游 `neo-zkvm` workspace 测试和 CLI smoke run

## 完成标准

- `neo-vm-rs` 包含真实解释器实现文件，不是空目录或仅元数据定义。
- 独立消费者可以调用 `neo_vm_rs::interpret(&script)` 执行 NeoVM2 bytecode。
- RISC-V 和 zkVM 路径不再有分歧的 NeoVM 栈、结果和 opcode 定义。
- 所有受支持的本地检查通过；平台特定 host-link 目标的例外需要记录清楚。

## 实施状态

已于 2026-05-21 完成：

- `r3e-network/neo-vm-rs@f61f764a3e80e9f9925615e23f6c7fc6ee31ae37` 拥有共享解释器核心。
- `r3e-network/neo-riscv-vm@d7d201c92cb907fe0cdf9af1bfad3625b36f331a` 将 `neo-riscv-guest` 作为 `neo-vm-rs` 的 facade。
- `r3e-network/neo-zkvm@c0a39cfaa21e7eb675fbacf23ccd442617e1296e` 固定到同一个共享 VM core revision。
- 父仓库 `r3e-network/neo-n4` 的 submodule 已前进到这些提交。
