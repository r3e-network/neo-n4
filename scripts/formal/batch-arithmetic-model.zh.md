# 批次算术不变量模型 — 2026-09-18

批次结算算术层的 Z3 符号执行模型：批次号严格递增、区块范围形成不重叠区间、已终局批次
永不回退、回滚约束成立。在 submit/finalize/revert 转移上进行有界模型检查（模式对无界
序列成立）。本模型覆盖**算术执行语义切片**；完整执行语义仍单独处理。

## 已建立的不变量

- **批次号以一递增**：新批次的 `batchNumber` 必须恰好是 `latestBatch + 1`（无跳跃、
  无回退）。
- **区块区间不重叠**：新批次的 `firstBlock > latestLastBlock`（上一批次的最后区块）。
- **区块区间合法**：每个批次都有 `lastBlock >= firstBlock`。
- **已终局批次单调**：规范批次水位线永不递减。
- **仅回滚 pending 或最新**：只有 `batchNumber == latestBatch + 1`（pending）或
  `batchNumber == latestBatch`（最新已终局）可被回滚——更早的已终局批次会破坏
  规范根链。
- **回滚最新批次回退水位**：回滚最新已终局批次将新水位设为 `latestBatch - 1`。

三个负向对照翻转为 unsat：批次号跳跃、区块重叠、回滚更早的已终局批次都违反模型。

`verify_batch_arithmetic.py` 共 9 项义务（6 项不变量、3 项 SAT 负向对照），全部通过。
`batch-arithmetic-result.json` 记录范围和信任假设。两项自测验证全部义务通过且 Z3 可导入。

## 诚实范围

本模型建模**批次/区块算术约束**（单调性、不重叠、回滚边界）。它不建模完整执行语义
（状态转移、EVM 执行、合约交互）、密码学身份（状态根、证明）或存储编码。有界模型检查
使用具体边界上的符号变量；不变量按构造对无界序列成立。

## 复现

```sh
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_batch_arithmetic.py
```

Windows 使用 `.venv-formal/Scripts/python`。需要 `z3-solver`（requirements 中已钉）。