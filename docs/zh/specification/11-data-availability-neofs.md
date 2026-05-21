# 第 11 章：NeoFS 数据可用性

数据可用性回答的问题是：如果某个 batch 被 L1 接受，其他人能否拿到足够数据来重放、审计或挑战它？

## 11.1 DA 不等于证明

| 概念 | 回答的问题 |
| --- | --- |
| DA | 数据是否能取回？ |
| Execution proof | 数据执行后得到的 root 是否正确？ |
| Settlement | L1 是否接受这个 root？ |

NeoFS DA 只负责第一件事。它不证明交易正确。

## 11.2 DA writer 接口

核心接口：

```csharp
public interface IDAWriter
{
    DAMode Mode { get; }
    ValueTask<DAReceipt> PublishAsync(DAPublishRequest request, CancellationToken cancellationToken = default);
    ValueTask<bool> IsAvailableAsync(DAReceipt receipt, CancellationToken cancellationToken = default);
}
```

实现者必须保证：

- `PublishAsync` 返回的 receipt 足以定位数据；
- `IsAvailableAsync` 能检查数据仍然可取；
- receipt 中的 commitment 能进入 `L2BatchCommitment.DACommitment`。

## 11.3 NeoFS 作为规范 DA

NeoFS 的好处：

| 特性 | 对 N4 的意义 |
| --- | --- |
| 内容寻址 | batch data 可以通过 CID / root 定位 |
| 复制策略 | operator 可以配置可用性等级 |
| 存储证明 | 可接入长期 proof-of-storage |
| Neo 生态一致 | 不依赖外部 DA 网络作为默认路线 |

## 11.4 DA commitment 形态

一个生产 DA commitment 应绑定：

```text
chainId
batchNumber
daMode
content root / cid
size
encoding
replication policy
publisher
timestamp / epoch
```

如果只记录一个任意 hash，而不绑定 chain/batch，则可能发生数据重放或替换。

## 11.5 DARegistry 与 DAValidator

| 合约 | 责任 |
| --- | --- |
| `DARegistry` | 存储 batch 的 DA commitment |
| `DAValidator` | 检查 commitment 与 chain DA mode / policy 是否匹配 |

`SettlementManager` 在接受 batch 前应把 DA 检查作为门槛之一。

## 11.6 Validium 与 Rollup 的差异

| 模式 | 数据在哪里 | 风险 |
| --- | --- | --- |
| Rollup | L1 或强可用公共层 | 成本高，但恢复性强 |
| NeoFS DA | NeoFS 内容寻址和复制 | 依赖 NeoFS 可用性策略 |
| External DA | 外部 DA 网络 | 引入外部信任和运维依赖 |
| DAC / Validium | 委员会保证数据 | 成本低，但委员会失效会影响退出 |

N4 支持不同 `DAMode`，但文档和 Experience Hub 必须清楚展示每条链的 DA 安全等级。

## 11.7 DA 运维清单

生产环境至少要配置：

- NeoFS endpoint；
- 容器 / bucket / placement policy；
- 复制份数；
- pinning 或保留周期；
- data retrieval probe；
- DA failure alert；
- batch data backup；
- disaster recovery runbook。

## 11.8 本地如何验证

devnet 中应检查：

```text
DA writer = NeoFsLikeDAWriter
da_availability = pass
```

测试命令示例：

```powershell
dotnet run --project tools\Neo.L2.Devnet\Neo.L2.Devnet.csproj `
  -c Release -- 3 --config samples\general-rollup.config.json
```

如果 DA availability audit 没有出现，不应把这次 devnet 视为完整验证。

