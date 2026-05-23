# 中文版本：NeoVM Rs 官方 NeoVM 一致性实现计划

> 对应英文文档：[docs/superpowers/plans/2026-05-22-neo-vm-rs-official-neovm-conformance.md](../../../superpowers/plans/2026-05-22-neo-vm-rs-official-neovm-conformance.md)

## 目标

本计划用于验证 `neo-vm-rs` 是否与官方 Neo N3 运行时 VM 基线一致。当前基线是 `neo-node v3.9.2`、`Neo v3.9.1`、`Neo.VM v3.9.0`。

## 方案

`neo-vm-rs` 作为 Rust 侧规范实现保留在共享 VM 层，同时引入来自官方 `neo-project/neo-vm v3.9.0` 的 VMUT JSON 测试。测试会把官方脚本 token 转换为字节码，在 `neo-vm-rs` 中执行，并比较最终 VM 状态和结果栈。

## 覆盖范围

- 保留官方 fixture 来源、版本和许可证说明。
- 添加 VMUT runner，递归加载官方 JSON 测试。
- 覆盖 `HALT`、`FAULT`、结果栈顺序、复合栈项和基础类型。
- 对不属于当前 API 的 step-only `BREAK` 调试器用例进行显式跳过并计数。

## 完成标准

- `neo-vm-rs` 通过官方 VMUT 一致性测试。
- `cargo fmt`、`cargo test`、`cargo clippy`、`cargo check --no-default-features` 和 benchmark 编译门禁通过。
- 下游 `neo-riscv-vm`、`neo-zkvm` 和父仓库 `neo-n4` 都固定到同一共享 VM 修订。
