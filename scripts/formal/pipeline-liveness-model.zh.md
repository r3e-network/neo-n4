# 跨组件管道活性模型 — 2026-09-18

排序器批处理管道的 CTL 时态逻辑组合模型：反审查强制入列 FIFO 队列**组合**批次结算
生命周期，用 pyModelChecking 模型检查器在乘积 Kripke 结构上校验。这将单组件 outbox
活性模型扩展到**跨组件组合**切片。

## 组合

状态为 `(队列深度 ∈ {0,1,2}, 阶段 ∈ {idle, sealed, proving, proved, submitted,
confirmed})`——18 个状态。转移：收集阶段用户入列强制交易；封装批次消耗整个队列
（全排空纪律）；批次随后证明（带重试自环）、提交、确认，epoch 完成回到收集。Kripke
结构是全的（每个状态都有后继）。

## 已建立的跨组件性质

- **管道可完全排空**：`AG EF(confirmed ∧ 队列空)`——从每个可达状态都存在到完全排空
  （批次确认、队列空）的路径。
- **队列经由批次排空**：`AG(队列非空 → EF(sealed))`——排队的强制交易只被批次取用
  消耗，绝不会被静默丢弃。
- **无死锁陷阱**：`AG EF(sealed ∨ confirmed)`。
- `AG(idle → EF(sealed))` 与 `AG(submitted → EF(confirmed))`——每个阶段都会推进。

两个负向对照翻转：去掉封装消耗边后，队列满的死锁（排队交易永远无法被消耗）可达；
去掉提交边后，Confirmed 从整个管道不可达。

`verify_pipeline_liveness.py` 共 7 项义务（5 项跨组件活性、2 项 SAT 负向对照），全部
通过。`pipeline-liveness-result.json` 记录工具、范围和信任假设。两项自测验证全部义务
通过且工具可导入。

## 诚实范围

本模型覆盖**双组件排序器管道组合**（队列 × 批次生命周期）。它不建模完整多组件网络
组合（Gateway 联邦、多链路由）、网络时间活性、SP1 证明活性或拜占庭故障场景。队列
有界为深度 2（有界模型检查；无界 FIFO 不变量在 forced-inclusion-model.md 中单独证明）；
封装消耗全部排队交易（全排空纪律；FIFO 模型覆盖部分消耗）。重试失败折叠进 proving
自环。

## 复现

```sh
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_pipeline_liveness.py
```

Windows 使用 `.venv-formal/Scripts/python`。需要 `pyModelChecking==1.3.4`。