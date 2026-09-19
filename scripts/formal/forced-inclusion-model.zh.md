# 强制包含队列模型 — 2026-09-18

RollupHub 反审查 FIFO 队列的归纳安全模型：`EnqueueForcedTransaction`、
`ConsumeForcedTransactionsInternal`、`GetNextForcedNonce` 与 `GetPendingForcedCount`
（合约 284–320 行）。这是手写抽象，**不是** C#/NeoVM 验证器。

## 已建立的性质

- `head` 与 `tail` 从零开始并保持在有序 `uint64` 域内。
- 入队返回旧 `tail` nonce 并严格递增 `tail`，因此连续强制交易获得唯一 nonce；入队不改 head。
- 消费只推进 head；合约的 `head + count <= tail` 守卫防止下溢并保持 FIFO 顺序。
- head/tail 单调，`tail - head` 永不为负，因此 pending count 是可靠的队列深度。
- 三个负向对照在移除下溢守卫、uint64 不回绕边界守卫或 FIFO 指针纪律时分别构造反例。

`verify_forced_inclusion.py` 共 18 项义务：初始状态、两类转移的不变量/单调性/pending
检查、严格 nonce 递增证明，以及 3 个 SAT 负向对照，全部通过。
`forced-inclusion-result.json` 记录求解器、源码/脚本/规范哈希和信任假设。

## 边界与限制

模型将指针限制为 `0..2^64-1`，对应合约的 `ulong` 读取路径。存储写入使用 BigInteger；
在 `ulong.MaxValue` 入队后再次读取可能回绕，因此这是需要协议级决定的边界。模型把
不回绕守卫作为 load-bearing 边界，并明确报告该反例，不声称保护当前合约未覆盖的域。
模型不证明交易唯一性、签名有效性、DA 纳入、批次 public-input 绑定或并发生产者/消费者；
这些是独立义务。`forcedInclusionCount` 与批次 public-input 的绑定在此作为信任假设，
由 public-input 测试和结算模型覆盖。

## 复现

```sh
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_forced_inclusion.py
```

Windows 使用 `.venv-formal/Scripts/python`。五项自测覆盖源码漂移、换行、UNKNOWN、
反例拒绝以及正常模型/负向对照。