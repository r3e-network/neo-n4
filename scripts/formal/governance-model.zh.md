# 治理授权模型 — 2026-09-18

GovernanceController 提案授权门的归纳安全模型。这是手写抽象，**不是** C#/NeoVM
验证器。模型针对 `IsApprovedAndTimelocked`（GovernanceControllerContract 548–558 行）
背后的 council 投票 / 时间锁 / veto / epoch 门。

## 已建立的性质

门控健全：由 `IsApprovedAndTimelocked` 守卫的动作仅当以下全部成立时才可执行——
提案达到 M-of-N 审批阈值（已记录 `approvedAt`）、自首次达到阈值后配置的时间锁已流逝、
提案未被 veto、且其 epoch 匹配当前 council epoch。每个转移（approve / veto / time-advance）
都保持不变量及门控的推论（达到阈值、未 veto、时间锁已流逝、epoch 匹配）。

`approve` 仅在首次越过阈值时记录审批（后续投票不能重置计时器），`veto` 是永久性的，
时间只向前推进。被 veto 的提案永远无法再次满足门控。四个负向对照在丢弃任一单个门控
合取项时构造反例（未批准的提案、被 veto 的提案、时间锁前执行、过期 epoch）——
证明每个合取项都是 load-bearing 的。

`verify_governance.py` 共 26 项义务：初始检查、三个转移各五项门控健全性检查、
veto 阻断门控，以及 4 个 SAT 负向对照，全部通过。
`governance-result.json` 记录求解器、源码/脚本/规范哈希和信任假设。

## 边界与限制

模型证明授权门的逻辑，而非其背后的密码签名检查（`CheckWitness`/council 成员）、
提案 payload 编码/绑定，也不证明 `Runtime.Time` 来源。epoch 与时间锁按合约读取方式抽象为
布尔/整数；`Runtime.Time` 墙钟的单调性是信任假设。Council 轮换仅以其改变 epoch 匹配
标志的方式建模。这些是独立义务，此处不声称。

## 复现

```sh
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_governance.py
```

Windows 使用 `.venv-formal/Scripts/python`。五项自测覆盖源码漂移、换行、UNKNOWN、
反例拒绝以及正常模型/负向对照。