# Gateway outbox 崩溃原子性（写入顺序）模型 — 2026-09-18

`src/Neo.Plugins.L2Gateway/GatewayOutbox.cs`（`SavePublication`/`MarkConfirmed`）持久化
写入顺序的归纳模型。这是手写抽象，**不是**对 RocksDB 内部或 C#/NeoVM 的验证。

## 已建立的性质

SavePublication 先写 checkpoint，再把每个组件转入发布状态；MarkConfirmed 先把每个组件
转入 Confirmed，再删除 checkpoint。崩溃可发生在任意两次写之间。将其建模为四个单次写
操作，归纳证明崩溃原子性不变量在每一步保持且在基态成立：

- **组件 item 仅在 checkpoint 存在时处于发布状态。** checkpoint 缺失时每个 item 都是
  Sealed 或 Confirmed——绝无孤立的发布状态。这正是代码注释记录的写入顺序保证
  （"操作之间的崩溃可从 checkpoint 加 sealed items 恢复"）。
- 基态（checkpoint 缺失、全部 Sealed）满足不变量。
- 每个写操作——写 checkpoint、转入 pub-state、确认 item、删除 checkpoint——都保持它，
  因此**每个可达的崩溃中断快照都可恢复**。

`verify_outbox_crashatomic.py` 共 9 项义务（5 UNSAT 归纳步 + 2 SAT 可达 + 2 SAT 负向
对照：删除时含孤立 pub-state、无 checkpoint 时转入 pub-state），全部通过。
`gateway-outbox-crashatomic-result.json` 记录求解器、源码/脚本/规范哈希和信任假设。

## 边界与限制

本模型覆盖**写入顺序崩溃原子性**的协议层：在文档化顺序下，每个崩溃快照都可恢复。
它**不**建模 RocksDB 内部 WAL 或单次存储写的原子性——这些在此代码之外，需 RocksDB
形式化设施。单次写的持久性（`Sync`/`Put` 语义）被信任；模型推理的是跨写的顺序，而非
store 在单次写内的崩溃行为。item-key↔commitment 绑定取自源码并被信任。

## 复现

```sh
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_outbox_crashatomic.py
```

Windows 使用 `.venv-formal/Scripts/python`。四项自测覆盖源码漂移、换行、UNKNOWN 与
归纳义务。