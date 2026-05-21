# 第 14 章：异构链与外部桥

N4 的跨链系统应足够通用，方便未来添加 Avalanche、EVM family、Tron、Solana 等异构链。关键是抽象成“外部事件证明 + 统一消息/资产 envelope”，而不是为每条链硬写一条特殊路径。

## 14.1 外部桥总体结构

<p align="center">
  <img src="../figures/visual-guide/external-bridge-flow.svg" alt="External bridge flow" width="900">
</p>

组成：

| 组件 | 路径 | 责任 |
| --- | --- | --- |
| EVM contracts | `external/foreign-contracts/eth` | 外部链 router / escrow |
| Solana contracts | `external/foreign-contracts/sol` | Solana 侧 router |
| ETH watcher | `watchers/neo-bridge-watcher-eth` | 观察 EVM 事件 |
| SOL watcher | `watchers/neo-bridge-watcher-sol` | 观察 Solana 事件 |
| TRON watcher | `watchers/neo-bridge-watcher-tron` | 观察 Tron 事件 |
| NeoHub external contracts | `contracts/NeoHub.ExternalBridge*`, `MpcCommittee*` | 验证 committee proof 和管理 escrow |

## 14.2 通用接入模型

新增一条外部链需要定义：

| 项 | 说明 |
| --- | --- |
| chain id | 外部链唯一标识 |
| finality rule | 多少确认后事件可被接受 |
| event schema | 外部链事件字段 |
| signer / committee | watcher 如何签名 |
| router contract | 外部链侧入口 |
| asset mapping | 外部资产到 N4 asset id |
| failure handling | reorg、延迟、重复事件如何处理 |

## 14.3 Watcher 状态机

<p align="center">
  <img src="../figures/visual-guide/watcher-state-machine.svg" alt="Watcher state machine" width="850">
</p>

典型状态：

```text
Scanning -> Observed -> Confirmed -> Signed -> Submitted -> Finalized
                                      \-> Disputed
```

Watcher 不应该拥有最终真相。它只把外部链事实转换成可验证 payload，NeoHub 再按 committee / bond / fraud policy 接受。

## 14.4 Committee proof

`MpcCommitteePayload` 类似：

```text
sourceChain
eventId
eventHash
targetChain
asset
amount
receiver
signatures[]
committeeEpoch
```

必须防止：

- 同一外部事件重复提交；
- 不同链 event id 冲突；
- watcher 少数伪造；
- committee 私钥泄露后无限提款；
- 外部链 reorg 后错误 finalization。

## 14.5 Avalanche 等新链如何接入

以 Avalanche C-Chain 为例：

1. 把它作为 EVM-compatible chain profile；
2. 配置 chain id、RPC、finality confirmations；
3. 部署外部 router / escrow；
4. 在 `ExternalBridgeRegistry` 注册；
5. 给 watcher 添加 chain config；
6. 注册 asset mapping；
7. 运行小额 deposit/withdraw drill；
8. 配置监控和 rate limit；
9. 通过 committee fraud path 演练错误事件。

## 14.6 外部桥与 N4 L2 的关系

外部链资产进入 N4 后，不应该绕过 NeoHub：

```text
Foreign chain event
-> watcher proof
-> NeoHub external bridge verification
-> SharedBridge / MessageRouter
-> target N4 L2
```

这样未来外部链数量增加时，N4 内部仍然保持一套统一资产和消息模型。

