# DA 失效切换模型 — 2026-09-18

`src/Neo.Plugins.L2DA/FailoverDAWriter.cs` 写入侧失效切换策略的归纳安全模型。
这是手写抽象，**不是** C#/NeoVM 验证器。模型针对有序 tier 发布循环及其 profile 不变量。

## 已建立的性质

- 只有**瞬时** `IOException` 才推进到下一 tier；非瞬时异常、取消或格式错误/未绑定
  payload 的 receipt 会立即中止，**绝不被 fallback 掩盖**。
- 成功发布只返回绑定已发布 payload（`Crypto.Hash256(payload)`）且携带本 profile 所需
  元数据的 receipt。
- 构造函数强制每个 tier 共享主端的 `DAMode` 与共同的非 `Unspecified` `DAReceiptKind`，
  因此 failover 绝不降低 DA profile；发布循环也因此绝不在缺少该共享 profile 时运行。
- success 与 fatal 结果互斥；仅当所有 tier 都瞬时失败时才报告"所有 tier 失败"。

`verify_da_failover.py` 共 14 项义务（7 UNSAT 安全、4 SAT 可达、3 SAT 负向对照），基于
固定三 tier 模型（策略对任意 N 成立；三 tier 使公式为 ground）。全部通过。
`da-failover-result.json` 记录求解器、源码/脚本/规范哈希和信任假设。

## 边界与限制

本模型覆盖**写入侧失效切换策略**——瞬时失败的发布如何落到下一同 profile 端点，以及
非瞬时失败或坏 receipt 如何被如实暴露而非静默降级。它**不**建模 RocksDB 崩溃一致性、
持久化 outbox 的持久性/重放或 L1 确认路径；这些是独立义务。瞬态按代码表达层面建模
（`IOException` 排除 `InvalidDataException`）；实际的异常分类取自源码并被信任。

## 复现

```sh
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_da_failover.py
```

Windows 使用 `.venv-formal/Scripts/python`。五项自测覆盖源码漂移、换行、UNKNOWN、
反例拒绝以及正常模型/负向对照。