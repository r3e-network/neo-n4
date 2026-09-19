# Gateway outbox 崩溃恢复一致性模型 — 2026-09-18

`src/Neo.Plugins.L2Gateway/GatewayOutbox.cs`（179-242 行）中 Gateway outbox 再水合
`Recover()` 的一致性模型。它证明实现文档化的**协议层**崩溃恢复保证：持久化快照
（每组件 item 状态 + 可选发布 checkpoint）如何映射到恢复的 sealed 集与 active 发布。
这是手写抽象，**不是**对 RocksDB 内部或 C#/NeoVM 的验证。

## 已建立的性质

- **Confirmed item 永不被重新 sealed**：恢复绝不会把已对账的组件送回 sealed 集
  （崩溃后不会重发已对账的批次）。
- **孤儿 Proving item 被降级为 Sealed** — 发布前崩溃使 item 可恢复而非丢失，也不会
  悬在半证明状态。
- **非活跃的 later-state item**（Proved/Submitted/Poisoned）被当作损坏而非静默再水合：
  没有 active checkpoint 时该状态不合法，因此 `Recover()` 抛错而非捏造 sealed 副本。
- **声明的 checkpoint 组件必须存在**于 store；引用缺失组件的 checkpoint 是损坏。
- **恢复是持久化快照的确定函数**：两个 (active-reference, 持久化状态) 输入相同的组件
  会以相同方式再水合。

`verify_outbox_recovery.py` 共 7 项义务（5 UNSAT 安全、1 SAT 损坏可行性、1 SAT 有效快照
可行性），全部通过。`gateway-outbox-recovery-result.json` 记录求解器、源码/脚本/规范哈希
和信任假设。

## 边界与限制

本模型覆盖**恢复协议**——实现崩溃时执行的映射及其强制的一致性不变量。它**不**建模
RocksDB 内部崩溃一致性（WAL、单次写入的原子性）、持久化写入顺序论证（SavePublication
先写 checkpoint 再写组件；MarkConfirmed 先确认再删 checkpoint），也不建模 L1 RPC 确认
语义。这些由持久化测试与 outbox 安全/活性模型覆盖。item-key↔commitment 绑定取自源码
并被信任（`BuildItemKey`/`DecodeItem`）。

## 复现

```sh
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_outbox_recovery.py
```

Windows 使用 `.venv-formal/Scripts/python`。五项自测覆盖源码漂移、换行、UNKNOWN、
反例拒绝以及正常模型/负向对照。