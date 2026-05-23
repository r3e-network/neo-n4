# 中文版本：NeoVM 分层边界清理计划

> 对应英文文档：[docs/superpowers/plans/2026-05-22-vm-layer-boundary-cleanup.md](../../../superpowers/plans/2026-05-22-vm-layer-boundary-cleanup.md)

## 目标

清理 VM 技术栈的分层边界：共享 NeoVM 语义下沉到 `neo-vm-rs`，`neo-riscv-vm` 和 `neo-zkvm` 只保留各自的 runtime、证明、宿主集成和工具职责。

## 当前边界

- `neo-vm-rs` 负责规范栈值、opcode 元数据、解释器行为、syscall 元数据和共享运行时语义 helper。
- `neo-riscv-vm` 负责 PolkaVM/RISC-V guest 集成、合约运行时 glue、native contract wrapper、工具和生成模块执行。
- `neo-zkvm` 负责 SP1 证明、witness 编排、证明 fixture 和 zkVM 专用 CLI 工作流。

## 主要任务

1. 将可复用的 `StackValue` 提取 helper 下沉到 `neo-vm-rs`。
2. 让 `neo-riscv-abi` 重新导出共享 helper API。
3. 瘦身 `neo-riscv-devpack` native wrapper，移除本地重复提取逻辑。
4. 删除未使用的 placeholder 模块。
5. 分别验证 `neo-vm-rs`、`neo-riscv-vm`、`neo-zkvm` 和父仓库 `neo-n4`。

## 完成状态

该计划在 2026-05-22 完成：共享提取逻辑已经由 `neo-vm-rs` 提供，下游 RISC-V 和 zkVM 仓库固定到同一共享 VM core 修订，并通过格式化、测试和 Clippy 验证。
