# 算术溢出安全模型 — 2026-09-18

算术溢出安全层的 Z3 位向量模型：RollupHub 使用 `ulong`（uint64）表示批次号、区块号和
强制入列 nonce。这些在实践中绝不会溢出（以每秒 1 批次的速度达到 2^64 批次需要约 5840 亿年）。
本模型用 64 位位向量检查溢出安全不变量：batch/block/nonce 递增安全（远低于 2^63），
以及键构造辅助函数产生无碰撞键。本模型覆盖**溢出安全执行语义切片**；完整执行语义仍
单独处理。

## 已建立的不变量

- **批次递增安全**：远低于 2^64 的批次号递增永不溢出。
- **区块递增安全**：远低于 2^64 的区块号递增永不溢出。
- **Nonce 递增安全**：远低于 2^64 的强制入列 nonce 递增永不溢出。
- **批次键 chainId 无碰撞**：不同的 `(chainId, batchNumber)` 对产生不同的
  `BatchCommitmentKey` 前缀（键结构：前缀 + chainId(4B) + batch(8B)）。
- **批次键 batch 无碰撞**：相同 chainId、不同 batch => 不同键。
- **强制键 nonce 无碰撞**：强制入列的不同 `(chainId, nonce)` 对产生不同的
  `ForcedTxKey` 存储键。

两个负向对照：在 2^64 边界递增会溢出（回绕到 0，翻转为 unsat）；相同 `(chainId, batch)`
参数的两个键相同（碰撞，sat）。

`verify_overflow_safety.py` 共 8 项义务（6 项安全不变量、2 项对照），全部通过。
`overflow-safety-result.json` 记录范围和信任假设。两项自测验证全部义务通过且 Z3 可导入。

## 诚实范围

本模型建模**算术溢出安全切片**（ulong 递增与键构造无碰撞）。它不建模完整执行语义
（EVM 执行、合约交互）、密码学身份或键构造之外的存储编码。实际 batch/block/nonce
序列远低于 2^63；模型使用保守边界。

## 复现

```sh
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_overflow_safety.py
```

Windows 使用 `.venv-formal/Scripts/python`。需要 `z3-solver`（requirements 中已钉）。