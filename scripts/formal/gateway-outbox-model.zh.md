# Gateway outbox 模型 — 2026-09-18

`src/Neo.Plugins.L2Gateway/GatewayOutbox.cs` 与 `L2GatewayPlugin.cs` 中 Gateway 发布
outbox 状态机的归纳安全模型。这是手写抽象，**不是** C#/NeoVM 验证器。

## 已建立的性质

- 发布只沿 `Sealed → Proving → Proved → Submitted → Confirmed` 推进，且 **`Confirmed`
  为终态**：已对账的 epoch 永不被失败、重新证明或重新发布。
- **L1 确认要求已 `Submitted`**：尚未发布到 L1 的 epoch 不能被标记为 confirmed。
- `RetryCount` 永不为负，失败时单调不减；失败绝不会把发布转入 `Confirmed`。
- **只有 `RetryCount` 达到 `maxAutomaticRetries` 才会 `Poisoned`**——绝不在耗尽前。
- `RecoverPoisonedPublication` 仅适用于 `Poisoned` 发布，将 retry 重置为 0 并回到
  活跃非终态（`Proving`/`Proved`）。
- 四个负向对照在丢弃 confirm 需 submitted 守卫、毒化耗尽守卫、仅毒化可恢复守卫或
  confirmed 终态守卫时构造反例。

`verify_outbox.py` 共 17 项义务（10 UNSAT 安全、3 SAT 可达、4 SAT 负向对照），全部
通过。`gateway-outbox-result.json` 记录求解器、源码/脚本/规范哈希和信任假设。

## 边界与限制

本模型覆盖**外盒协议状态机**——epoch 如何被证明、提交、确认、重试耗尽后毒化并恢复。
它**不**建模 RocksDB 内部崩溃一致性、`SavePublication`/`MarkConfirmed` 的持久化写入
顺序（由代码注释论证、持久化测试覆盖），也不建模实际 L1 RPC 确认语义。
`maxAutomaticRetries` 是抽象常量；源码绑定同时钉住 `GatewayOutbox.cs` 与
`L2GatewayPlugin.cs`。

## 复现

```sh
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_outbox.py
```

Windows 使用 `.venv-formal/Scripts/python`。五项自测覆盖源码漂移、换行、UNKNOWN、
反例拒绝以及正常模型/负向对照。