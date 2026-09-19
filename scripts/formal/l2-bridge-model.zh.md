# L2 桥接供给守恒模型 — 2026-09-18

`external/neo/src/Neo/SmartContract/Native/L2NativeContracts.cs`（`ApplyDeposit` /
`InitiateWithdrawal`，约 572–610 行）中 L2 桥接代币供给记账的归纳安全模型。
这是手写抽象，**不是** C#/NeoVM 验证器。

## 已建立的性质

- 每个映射资产的 L2 流通供给等于累计 minted 减累计 burned 且**永不为负**——桥既不凭空
  铸造，也不烧毁超过桥入的量。
- `ApplyDeposit` 铸造（由每-`(sourceChainId, nonce)` dedupe 键防重放），`InitiateWithdrawal`
  在代币自身余额守卫下烧毁，因此超过已铸造供给的取款被拒绝。
- 平台代币（NEO/GAS）走同一 mint/burn 路径，因此 **L2 上不会在桥路之外发行 GAS**。
- 四个负向对照在丢弃超烧守卫、deposit-credit 对应、nonce 重放 dedupe 或余额守卫时
  构造反例（含重放同一 L1 事件使供给翻倍）。

`verify_l2_bridge.py` 共 15 项义务（8 UNSAT 安全、3 SAT 可达、4 SAT 负向对照），全部
通过。`l2-bridge-result.json` 记录求解器、源码/规范/脚本哈希和信任假设。

## 边界与限制

本模型覆盖 **L2 服务供给账本守恒**（minted = 桥入、burned = 桥出、供给永不为负）。
它**不**建模 L1 来源的 deposit 证明验证、小数位缩放运算、`BridgedNep17` 合约内部或
每调用者 NEP-17 余额语义（`TokenManagement`）；它按桥维护的账本层对聚合供给建模。
GAS 供给门控（doc.md §13.2）被信任走同一路径；模型不证明核心的 ChainMode 门控
mint/burn 钩子。这在桥路供给层面关闭了 "L2 mint/burn / GAS" 义务。

## 复现

```sh
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_l2_bridge.py
```

Windows 使用 `.venv-formal/Scripts/python`。五项自测覆盖源码漂移、换行、UNKNOWN、
反例拒绝以及正常模型/负向对照。