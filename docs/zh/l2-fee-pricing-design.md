# L2 费用定价表面 — 设计（Design Only，未实现）

> **状态：Proposed / Design only。** 按 2026-09-17 决策，此定价表面只设计、不实现。
> 代码现状：`L2FeeContract`（分配）与 `L2PaymasterContract`（赞助）已实现；**费用定价/估算表面缺失**。
> 本文件是唯一设计交付物，供共识评审后决定是否实现。

## 1. 背景与现状（核实过代码）

L2 费用模型继承 N3，GAS 为费用 token，委员会治理，无独立 token / 质押 / slashing。费用链路上已就位的部分：

| 能力 | 位置 | 状态 |
|---|---|---|
| 费用 token（bridged GAS, decimals=8, supply 受 L1 SharedBridge 约束） | `doc.md` §9, §9.3；`Neo.L2.Abstractions` 资产映射 | ✅ 已实现 |
| 费用**分配**（Configure 设 owner/feeAsset/sequencer/prover/DA 地址与 BPS 分成；Distribute 按分成发款，rounding 吸收尾差） | `external/neo/.../L2NativeContracts.cs` `L2FeeContract`（id -105） | ✅ 已实现 |
| 赞助费 / stablecoin fee 抽象（TopUp / FeeCharged 事件） | `L2PaymasterContract`（id -106） | ✅ 已实现 |
| 执行 gas 用量（`CanonicalReceiptV1.gasConsumed i64 LE`） | `doc.md` §8.5 类；executor 输出 | ✅ 已实现 |
| **费用定价 / 估算**（base fee、gas 单价、L1 DA 数据成本传递、`estimatefee`） | — | ❌ **缺失** |

`L2FeeContract` 只回答"**收来的钱怎么分**"，不回答"**该收多少**"。后者（定价）即本设计范围。

## 2. 规范锚点

- `doc.md` §9 / §9.3 Fee Abstraction：GAS 是默认费用；Paymaster 提供 dApp sponsored / stablecoin 替代支付。
- `doc.md` §13.2 Policy："**L2 可本地配置 fee policy，但 bridge/security policy 由 NeoHub 管控**"——本表面受此授权，且定价规则须委员会治理。
- `doc.md` §13.1 `L2FeeContract`：管理 sequencer / prover / DA fee。
- 安全约束（`doc.md` ~1430-1432）：L2 spam control 只接受 L1 原生 GAS；`SetGasToken`/`SetFee` 等必须拒绝任意替代 NEP-17，防治理误配与恶意回调。

## 3. 设计目标（最小、继承 N3、委员会治理）

1. **无新 token、无 oracle、无拍卖、无动态 base fee**——沿用 N3 固定费率语义。
2. 定价 = **执行定价 + L1 DA 数据成本传递**（对应早期"简化Gas模型"的 BaseFee + L1 DA 成本）。
3. 全部复用既有路径（VM metering 的 `gasConsumed`、`IDAWriter` 发布的 DA payload），不重造规范编码。

## 4. 提案接口（off-chain lib `Neo.L2.Fees`）

沿用仓库"记录作数据载体 + 接口可替换"的约定（参照 `IDAWriter` / `IRoundProver`）。

```csharp
namespace Neo.L2.Fees;

/// <summary>委员会治理写入的 L2 定价配置。非规范编码，仅供 off-chain 估算；链上以 governance 固化。</summary>
public sealed record L2FeeConfig
{
    public decimal BaseFee { get; init; }             // 每单位 gas 的 GAS 价（committee-set）
    public decimal L1DataFeePerByte { get; init; }    // 可选：L1 DA 数据成本 / 字节
    public bool   L1DataCostEnabled { get; init; }    // 是否传递 DA 成本
}

/// <summary>可替换定价策略：默认 CommitteeFeePolicy；未来可挂 OTLP/外部数据源而不改调用方。</summary>
public interface IL2FeePolicy
{
    L2FeeConfig Config { get; }
    BigInteger EstimateFee(long gasConsumed, long txBytes, long daBytes);
}
```

- 定价公式（示意，最终以 spec 定）：
  `fee = BaseFee * gasConsumed + (L1DataCostEnabled ? L1DataFeePerByte * daBytes : 0)`
- `daBytes` 来源：批处理 DA payload 大小（`IDAWriter` 发布路径），即"L1 DA 成本传递"。
- `BaseFee` 由委员会/NeoHub 治理写入（对齐 §13.2"本地 fee policy + NeoHub 管控安全 policy"），默认实现 `CommitteeFeePolicy`。

## 5. 集成点

| 集成点 | 说明 |
|---|---|
| L2 batch executor | 执行后已产出 `gasConsumed`（`CanonicalReceiptV1`）；定价侧据此 + `daBytes` 估算应收 |
| L2 RPC `estimatefee` | `doc.md` §14.1 估算面；调用 `IL2FeePolicy.EstimateFee` 返回给调用方 |
| `L2FeeContract` 分配 | 收来的 GAS 进入 L2 GAS 余额，再由 `Distribute` 按 BPS 分给 sequencer/prover/DA |

## 6. 未决 / 待共识

1. **L1 DA 数据成本数值来源**：取 settlement 实际 L1 gas（动态）还是静态费率（简单、继承 N3 语义）。
2. **执行期计费落地方式**：VM metering 已产 `gasConsumed`，price 侧与"应收 GAS 扣减"的对接点待定。
3. **是否写入 `doc.md` §9**：新增 `baseFee` / `l1DataFeePerByte` 字段属 spec 更新，需先提变更再实现（AGENTS.md 约束）。

## 7. 明确不做（防越界）

- ❌ 不引入定价 oracle / 拍卖 / 动态 base fee（继承 N3 固定费率）。
- ❌ 不引入替代 fee token（`doc.md` §9.3 仅 Paymaster 抽象）。
- ❌ 不把 `L2FeeContract` 的分配逻辑重写/移到 off-chain。
- ❌ 不新增 `doc.md` 未列出的配置字段（待 §6.3 spec 更新批准）。
