# 第 13 章：资产桥与平台资产

N4 的资产设计目标是：用户在不同 L2 之间移动资产时，尽量感觉像同一平台，而不是每条链都有一套孤立资产。

## 13.1 平台资产目录

| 资产 | L1 来源 | L2 表示 | decimals |
| --- | --- | --- | ---: |
| NEO | L1 原生 NEO | built-in bridged NEO | L2 为 8 |
| GAS | L1 原生 GAS | built-in bridged GAS / fee asset | 8 |
| USDT | L1 / 外部 canonical asset | built-in platform USDT | 6 |
| USDC | L1 / 外部 canonical asset | built-in platform USDC | 6 |
| BTC | 外部 canonical BTC representation | built-in platform BTC | 8 |

## 13.2 为什么 L2 NEO 有 decimals

L1 NEO 不可分割，`decimals = 0`。但 L2 应用需要更细粒度的费用、DEX、抵押和 UX。因此 N4 在 L2 把 NEO 表示为 8 decimals。

规则：

```text
L1 deposit amount = n NEO
L2 minted amount = n * 10^8

L2 withdrawal amount = x
valid only if x % 10^8 == 0
L1 release amount = x / 10^8
```

## 13.3 Deposit 数据流

| 步骤 | 组件 | 说明 |
| --- | --- | --- |
| 1 | `SharedBridge` | L1 锁定资产 |
| 2 | `TokenRegistry` | 读取 l1/l2 decimals 和 target asset |
| 3 | `MessageRouter` | 把 deposit payload 放入目标链 inbox |
| 4 | L2 node | 读取并执行 deposit |
| 5 | `L2BridgeContract` | mint / credit L2 asset |
| 6 | batch settlement | deposit 消费状态进入 root |

## 13.4 Withdrawal 数据流

| 步骤 | 组件 | 说明 |
| --- | --- | --- |
| 1 | `L2BridgeContract` | burn / lock L2 表示 |
| 2 | batcher | withdrawal record 进入 `WithdrawalRoot` |
| 3 | `SettlementManager` | 接受包含 withdrawal root 的 batch |
| 4 | user / relayer | 提交 inclusion proof |
| 5 | `SharedBridge` | 检查 proof 和 spent 状态 |
| 6 | L1 asset | 释放 escrow |

## 13.5 `AssetRegistry`

.NET 侧桥逻辑入口：

```text
src/Neo.L2.Bridge/AssetRegistry.cs
src/Neo.L2.Bridge/DepositProcessor.cs
src/Neo.L2.Bridge/WithdrawalProcessor.cs
```

`AssetRegistry` 不只是 UI metadata，它决定：

- 资产是否可桥接；
- L1/L2 decimals；
- 缩放因子；
- withdrawal 是否会损失精度；
- asset id 是否 canonical。

## 13.6 精度错误示例

| 输入 | 是否允许 | 原因 |
| --- | --- | --- |
| deposit 1 L1 NEO | 允许 | L2 得到 100000000 units |
| withdraw 100000000 L2 NEO units | 允许 | 精确缩回 1 L1 NEO |
| withdraw 1 L2 NEO unit | 拒绝 | 无法表示成 L1 整数 NEO |
| withdraw 150000000 L2 NEO units | 拒绝 | 1.5 NEO 不能在 L1 表示 |

## 13.7 L2↔L2 转移

L2↔L2 不是直接让两条 L2 互相信任。规范路径应通过 shared bridge / message root：

```text
source L2 burn/lock
-> source batch root accepted on L1/Gateway
-> message routed to target L2
-> target L2 mint/credit
```

Gateway 可以优化成本和延迟，但不能绕过 canonical root。

## 13.8 安全检查

桥的安全检查至少包括：

- asset mapping 是否存在；
- decimals 缩放是否无损；
- nonce 是否未消费；
- withdrawal proof 是否属于 accepted root；
- target chain 是否 active；
- emergency pause 是否开启；
- rate limit / cap 是否触发；
- receiver 地址格式是否正确。

