# neo-external-bridge-router 技术学习指南

这份指南把 `neo-external-bridge-router` 当作 Neo N4 的一个技术单元来解释。它不是源码阅读图，而是帮助读者理解：这个单元负责什么、哪些技术假设保证它正确、数据如何移动、状态如何变化、证据如何被验证、它如何接入 Neo N4 的整体架构。

## 技术契约

| 维度 | 含义 |
| --- | --- |
| 层级 | 异构链桥程序 |
| 目的 | Solana 侧桥路由程序，承载 Neo N4 跨链 lock/mint/burn/unlock 流程。 |
| 输入 | Solana 指令 <br> 代币账户状态 <br> 桥权限账户 |
| 职责 | 校验路由 <br> 移动托管资产 <br> 发出桥事件 |
| 输出 | 桥事件 <br> 托管状态变化 <br> relay 证据 |
| 消费方 | watcher-sol <br> 网关 <br> 共享桥 |

## 图表集合

| # | 图 | 学什么 |
| --- | --- | --- |
| 1 | [系统位置图](figures/position.zh.svg) | 它在 Neo N4 中的位置。 |
| 2 | [技术原理图](figures/principles.zh.svg) | 保证设计正确的技术规则。 |
| 3 | [概念架构图](figures/architecture.zh.svg) | 主要技术块和边界。 |
| 4 | [工作流图](figures/workflow.zh.svg) | 运行时的有序过程。 |
| 5 | [数据流图](figures/dataflow.zh.svg) | 信息、承诺和证据如何移动。 |
| 6 | [状态模型图](figures/state-model.zh.svg) | 状态归属、转换和终局性。 |
| 7 | [证明与证据流图](figures/proof-flow.zh.svg) | 声明如何变成可验证证据。 |
| 8 | [信任边界图](figures/trust-boundaries.zh.svg) | 哪些内容被信任、检查、拒绝或观测。 |
| 9 | [集成关系图](figures/integration-map.zh.svg) | 该单元如何接入更大的 N4 栈。 |
| 10 | [运行生命周期图](figures/lifecycle.zh.svg) | 从配置到执行、证据和运维的生命周期。 |

## 架构模型

`neo-external-bridge-router` 接收 Solana 指令 | 代币账户状态 | 桥权限账户，拥有的边界是：校验路由 | 移动托管资产 | 发出桥事件。它输出 桥事件 | 托管状态变化 | relay 证据，然后由 watcher-sol | 网关 | 共享桥 消费。

分层规则：监听、消息规范化、资产状态和最终验证分层。

## 工作流

1. 接收指令
2. 检查账户
3. 更新托管
4. 发出事件
5. 监听器 relay

失败路径：确认不足、nonce 重放、消息不匹配或资产状态无效。

## 数据流

1. 指令数据
2. 路由程序
3. 事件日志
4. Neo N4 桥消息

承诺信号：消息哈希、nonce、源链高度和资产动作。

## 状态、证明和信任

- 状态转换：事件变成标准消息，再变成资产或状态动作。
- 终局条件：消息被消费且 nonce 标记为已使用。
- 信任模型：信任确认与验证规则，不信任单个 watcher 或 RPC 端点。
- 验证边界：事件、确认数、消息哈希、nonce 和资产状态同时通过。
- 重放与顺序：nonce、源链高度和消息哈希提供顺序与重放保护。

## 集成和运行

- NeoFS DA：NeoFS 保存批次数据、见证或轨迹摘要以及可取回证据。
- 证明系统：证明系统把 L2 执行声明压缩为可验证证据。
- Gateway/API：Gateway 负责用户路由、查询、提交和健康状态聚合。
- 桥与异构链：桥规则统一 L1-L2、L2-L2 和异构链消息与资产。
- 可观测证据：源事件、确认数、消息哈希、游标、提交哈希和最终状态。

在 Neo N4 仓库根目录重新生成这些技术图：

```powershell
python tools/docs/generate_crate_visual_docs.py
```
