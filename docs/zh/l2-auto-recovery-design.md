# L2 自动故障恢复（Auto Recovery）— 设计（Design Only，未实现）

> **状态：Design only。** 检测→编排层是横切、配置驱动的功能，按与 gas 定价相同的纪律
> （AGENTS.md：不加 `doc.md` 外的配置字段，先提 spec）只出设计，不盲写代码。
> **动作原语已编写，未完成生产接线**：`FailoverDAWriter` 与 `RetryingGatewayProofProver`。
> 用户仅对费用设计选择了"仅设计"；本恢复文档是待评审提案，不代表用户限制了恢复实现。
> DA 切换不得跨安全 profile 或降级为 Local；同 mode 的端点也需要配套独立 reader。
> Gateway 重试只会重新等待同一幂等请求，不会重启 daemon 或重新启动证明作业。
> 本文件是唯一设计交付物，供共识评审后决定是否实现。

## 1. 现状：检测信号与动作原语都已就位，缺的是胶水

**检测层（exists）**
- 健康面：`/healthz` / `/readyz` / `/healthprobe` / `/operatorstatus`
  （`src/Neo.L2.Telemetry/MetricsRequestHandler.cs` + `LocalHost*Document` providers，含
  `HasMetricsReadinessCheck` / `HasMetricsHealthProbe` / `HasMetricsOperatorStatus`）
- 指标信号：`l2.da.is_available_*`（已实现）、`l2.settlement.poisoned`、
  `l2.batch.on_block_committed_error`、`l2.proving.*`、`l2.settlement.confirmation_lag_batches` 等
- 原语：`Neo.L2.Telemetry.CircuitBreaker`

**动作层（已实现）**
- DA 故障切换 → `FailoverDAWriter`：发布失败自动按序 primary→standby，按实际 mode 打 `l2.da.*` 指标
- Prover 重提重试 → `RetryingGatewayProofProver`：`TimeoutException` 幂等重试 + 指数退避，fail-closed 错误不重试

**缺口（缺失的编排胶水）**
仓库无通用 health-monitor / watchdog / recovery-orchestrator（`ChallengeOrchestrator` 是欺诈挑战，属不同关注点）。
各 poison 态是"停 → 人工恢复"，没有组件把检测信号连到自动恢复动作，也没有把检测结果统一汇入 operator 健康面。

## 2. 设计目标（最小、继承 N3、无新激励）

1. **无新 token / 无新激励 / 无 slashing**——恢复是运营韧性，不是经济激励。
2. **只编排"资源级"恢复**：DA 故障切换、prover 重试、节点自身恢复。
3. **编排动作必须幂等 + 滞回 + 退避**（防抖动），且**资金级动作保持人工**。
4. 复用既有检测信号 + 已实现动作原语，不重造规范编码。

## 3. 提案（off-chain lib `Neo.L2.Recovery`）

```csharp
namespace Neo.L2.Recovery;

/// <summary>一个幂等恢复动作；TryExecute 返回是否已处理（供编排器去重/滞回）。</summary>
public interface IRecoveryAction
{
    string Name { get; }
    Task<bool> TryExecuteAsync(RecoverySignal signal, CancellationToken cancellationToken);
}

/// <summary>封装一次检测到的故障信号（DA 不可用 / prover 超时 / 结算 poison…）。</summary>
public sealed record RecoverySignal(string Kind, string Detail);

/// <summary>后台循环：周期采样检测信号 → 按策略矩阵匹配 IRecoveryAction → 幂等执行，滞回+退避。</summary>
public sealed class RecoveryOrchestrator { /* ... */ }
```

- **动作实现（注入现成原语）**：
  - `DAFailoverAction` → 触发/复用 `FailoverDAWriter` 的 standby 切换（配 standby 列表）
  - `ProverResubmitAction` → 复用 `RetryingGatewayProofProver`（重试次数构造即定）
- **检测来源**：读 `l2.da.is_available_*`、`l2.settlement.poisoned` 等指标；`/readyz` 谓词；
  `CircuitBreaker` 状态。
- **编排器输出**：把当前检测结果 + 最近动作写入 `/operatorstatus`，operator 可 `curl | jq`。

## 4. 安全边界（明确不自动做）

- ❌ **不自动重提交资金级结算**：`l2.settlement.poisoned` 保持"停 → 人工 `RecoverPoisonedBatchAsync`"，不纳入自动编排。
- ❌ **不自动切换 L1 桥/金库动作**（`doc.md` §17 威胁模型：bridge/security policy 由 NeoHub 管控）。
- ✅ 只自动做**可逆、幂等**的资源级恢复（DA 切换、prover 重试）。

## 5. 集成点

| 集成点 | 说明 |
|---|---|
| 组合根 | 注入 `RecoveryOrchestrator`，启动后台采样循环 |
| `MetricsRequestHandler` | 复用 `/operatorstatus` provider 暴露编排状态 |
| 动作层 | `FailoverDAWriter` / `RetryingGatewayProofProver` 作为 `IRecoveryAction` 的载体注入 |
| 检测 | 读既有指标 + `/readyz` 谓词 + `CircuitBreaker` |

## 6. 未决 / 需 spec 更新

1. **策略矩阵**：哪些信号 → 哪些动作（含优先级、互斥、依赖）。
2. **参数**：采样周期、滞回阈值、退避上限（这些属配置，`doc.md` 未列，需先提 spec）。
3. **配置来源**：standby DA 列表、prover 重试次数当前是构造注入；接入配置（`config.schema.json`）需 spec 更新（AGENTS.md 约束）。
4. 是否在 `doc.md` §17 威胁模型 / DR runbook 增加对应章节。

## 7. 明确不做

- ❌ 不引入新激励 / slashing（继承 N3）。
- ❌ 不自动执行资金级结算重提交 / 桥动作（保持人工确认）。
- ❌ 不加 `doc.md` 未列出的配置字段（待 §6 的 spec 更新批准）。
