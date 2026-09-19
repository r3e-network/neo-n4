# Gateway outbox 可达性活性模型 — 2026-09-18

Gateway 发布 outbox 状态机的 CTL 时态逻辑模型，用 **pyModelChecking** 模型检查器
（`pyModelChecking.CTL` + Kripke）校验。它在有限状态层面回应了 outbox 的
"组合 / 活性" 义务。

## 检查内容

对 outbox 状态（`Sealed, Proving, Proved, Submitted, Confirmed, Poisoned`）建立 Kripke
结构，含真实转移：重试耗尽将 `Proving → Poisoned`，operator 恢复时 `Poisoned → Proving`，
`Confirmed` 为终态，并有 prove/submit/confirm 边。以下可达性活性成立：

- `AG(Sealed → EF Confirmed)` — 初始发布最终可确认。
- `AG EF Confirmed` — 从每个可达状态都存在到 `Confirmed` 的路径。
- `AG(Poisoned → EF Proving)` — 毒化发布可恢复。
- `AG(Submitted → EF Confirmed)` / `AG(Proved → EF Confirmed)` / `AG(Proving → EF Confirmed)`。
- `AG EF(Confirmed ∨ Poisoned)` — 无死锁陷阱；每个状态都到达某个终态。

三个负向对照去掉一条转移使性质翻转为假：没有恢复则 `Poisoned` 永不能回到活跃态；
没有确认边则 `Submitted` 永不能到达 `Confirmed`；没有证明成功边则 `Proving` 永不能到达
`Confirmed`。

## 诚实的活性语义

真实 outbox 在重试耗尽时进入 `Poisoned`，并等待 operator 调用
`RecoverPoisonedPublication` 后继续。因此正确的活性义务是**可达性活性**（`EF` /
`AG EF`）——每个状态都存在可确认或可恢复的路径。我们**不**声称在任意重入恢复下的
无条件终止（`AF Confirmed`），因为实现并不保证（毒化发布在 operator 行动前保持）。
这在模型的信任假设中说明。

## 设施

需要 `pyModelChecking==1.3.4`（已加入 `scripts/formal/requirements.txt`）。这是真正的
时态逻辑模型检查器；CTL 义务在有限 Kripke 结构上被穷举求解，非近似。

## 复现

```sh
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_outbox_liveness.py
```

Windows 使用 `.venv-formal/Scripts/python`。两项自测验证全部义务通过且工具可导入。

## 边界

本模型覆盖 outbox **状态机的活性**（最终可确认、可恢复、无死锁）。它不建模完整跨组件
组合、网络时间活性或 SP1 证明活性；那些仍是独立义务。Kripke 抽象为手写；转移集镜像
GatewayOutbox.cs / L2GatewayPlugin.cs 语义。