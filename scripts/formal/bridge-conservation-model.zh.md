# 桥接守恒模型 — 2026-09-18

SharedBridge 每 (chainId, asset) escrow 记账的归纳安全模型。这是手写抽象，**不是**
C#/NeoVM 验证器。模型针对 `Deposit`、`FinalizeWithdrawal*`/`ConsumeAndPayout` 与
`Credit/DebitLockedBalance`（NeoHub.SharedBridgeContract 265–297、538–575 行）
的 locked-balance 账本。

## 已建立的性质

- escrow 永不为负：每个可达状态的 `locked >= 0`。
- 守恒成立：`locked == deposited - paid`，因此 payout 永不超过 deposit
  （不会凭空铸造资产，链也不会支付超过其 escrow 的量）。
- `deposit(amount>0)` 转入资产并计 credit；withdraw 在 `currentBal >= amount` 守卫下
  扣减并支付，因此超过 escrow 的取款被拒绝。
- 与 escrow 恰好相等的取款将其清零（合法，非故障）。
- 三个负向对照在移除余额守卫、重放保护或 deposit-credit 对应时构造反例
  （超额支付、双花、凭空 mint）。

`verify_bridge.py` 共 14 项义务：初始检查、两类转移的不变量/守恒/payout 与非负
locked 检查、排空至零可达性，以及 3 个 SAT 负向对照，全部通过。
`bridge-conservation-result.json` 记录求解器、源码/脚本/规范哈希和信任假设。

## 边界与限制

这里的守恒覆盖**L1 escrow 账本**（每链 locked balance = deposits − payouts）。它不证明
L2 mint/burn 或 GAS 供给模型（位于 L2 核心原生合约，属独立义务），也不证明消息哈希、
取款叶 Merkle 绑定或 settlement manager 的 verifyWithdrawalLeaf 路径（由结算模型与
VM 测试覆盖）。重放保护在账本层面建模为 "payout ≤ deposited"；每叶 consumed-key 位
作为防止单叶支付两次的机制被信任，而双花负向对照正说明否则守恒会被破坏。

## 复现

```sh
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_bridge.py
```

Windows 使用 `.venv-formal/Scripts/python`。五项自测覆盖源码漂移、换行、UNKNOWN、
反例拒绝以及正常模型/负向对照。