# 乐观挑战二分游戏模型 — 2026-09-18

`src/Neo.L2.Challenge/BisectionGame.cs` 的归纳模型：将有争议批次收窄到单笔交易以便重
执行的交互式争议二分。这是手写抽象，**不是**对 C#/NeoVM 的验证。

## 已建立的性质

游戏是基于每 tx 检查点一致性（`lo` = 最后已知一致索引、`hi` = 首个已知不一致索引）的
纯区间收窄状态机。用抽象 `diff[i]` 谓词（双方在该索引的检查点根是否不同）建模，归纳
证明：

- **前置条件蕴含初始不变量**：preState（索引 0）一致、postState（索引 n）不一致，因此
  `lo=0` 是一致索引、`hi=n` 是不一致索引。
- **不变量保持**：每轮保持 `lo` 为一致索引、`hi` 为不一致索引（`¬diff[lo] ∧ diff[hi]`），
  争议永不出区间。
- **单调收窄**：每轮严格缩小 `hi-lo`（至少 1）并递增 `rounds`，因此游戏终止。
- **轮数有界**：`rounds + (hi-lo) ≤ n` 是可达不变量，因此 rounds ≤ n。
- **结算**：相邻 `hi-lo ≤ 1` 在 `lo` 结算为单一原子争议索引；中点总是推进（不会卡死），
  一轮从不扩大区间。

`verify_bisection.py` 共 16 项义务（11 UNSAT 安全/进展、2 SAT 可达、3 SAT 负向对照：反转
分支破坏不变量、构造守卫缺失时不一致 preState 可达、区间扩大是被排除的坏状态）。全部
通过。`bisection-result.json` 记录求解器、源码/脚本/规范哈希和信任假设。

## 边界与限制

本模型覆盖**二分状态机**——区间不变量、单调收敛与结算；不覆盖链上记录、轮次截止、
欺诈证明 payload 语义或签名/witness 验证（这些在 ChallengeOrchestrator / 结算合约中，
属独立义务）。双方检查点序列被抽象为一致性谓词；检查点的密码身份不在范围内。

## 复现

```sh
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_bisection.py
```

Windows 使用 `.venv-formal/Scripts/python`。四项自测覆盖源码漂移、换行、UNKNOWN 与
归纳义务。