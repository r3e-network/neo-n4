# 状态单调性不变量模型 — 2026-09-18

状态层单调性的 Z3 符号执行模型：已终局状态根形成连续链（批次 N+1 的 pre-state == 批次
N 的 post-state），规范状态根永不自发回退（只有治理显式回滚最新已终局批次才恢复其
pre-state），Gateway 已终局水位线严格递增（永不递减）。在 finalize/revert 转移上进行
有界模型检查。本模型覆盖**状态层执行语义切片**；密码学状态根身份与完整执行语义仍单独
处理。

## 已建立的不变量

- **Pre-state 匹配规范**：新批次的 pre-state 根必须匹配当前规范状态根（链连续性）。
- **规范推进到 post-state**：终局后，规范根成为批次的 post-state 根。
- **规范根永不自发回退**：规范根仅通过显式批次终局或治理回滚变更。
- **Gateway 水位单调**：Gateway 已终局水位线严格递增。
- **Gateway 发布不可逆**：一旦批次被 Gateway 发布（`batchNumber <= gatewayWatermark`），
  它就不能被回滚。
- **回滚恢复 pre-state 根**：回滚最新已终局批次将规范根恢复为该批次的 pre-state 根。

三个负向对照翻转为 unsat：pre-state 不匹配（违反链连续性）、Gateway 水位回退（违反
单调性）、尝试回滚 Gateway 已发布批次（违反不可逆性）。

`verify_state_monotonicity.py` 共 9 项义务（6 项不变量、3 项 SAT/unsat 对照），全部
通过。`state-monotonicity-result.json` 记录范围和信任假设。两项自测验证全部义务通过
且 Z3 可导入。

## 诚实范围

本模型建模**状态根链连续性与 Gateway 水位单调性**（状态层执行语义）。它不建模密码学
状态根身份（EVM 状态到 SHA-256 根的映射）、完整执行语义（EVM 执行、合约交互）或
Gateway 联邦协议。有界模型检查使用符号变量；不变量按构造对无界序列成立。

## 复现

```sh
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_state_monotonicity.py
```

Windows 使用 `.venv-formal/Scripts/python`。需要 `z3-solver`（requirements 中已钉）。