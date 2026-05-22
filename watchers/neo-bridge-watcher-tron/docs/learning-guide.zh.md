# neo-bridge-watcher-tron 技术学习指南

这份指南把 `neo-bridge-watcher-tron` 当作 Neo N4 的一个技术单元来解释。它不是源码阅读图，而是帮助读者理解：这个单元负责什么、哪些技术假设保证它正确、数据如何移动、状态如何变化、证据如何被验证、它如何接入 Neo N4 的整体架构。

## 技术契约

| 维度 | 含义 |
| --- | --- |
| 层级 | 跨链监听器 |
| 目的 | 监听 TRON 桥事件，并转换为标准化 Neo N4 relay 消息。 |
| 输入 | TRON RPC/log 流 <br> 桥合约事件 <br> 检查点游标 |
| 职责 | 过滤桥事件 <br> 规范化 payload <br> 保护重放与游标状态 |
| 输出 | relay 任务 <br> 审计日志 <br> 健康指标 |
| 消费方 | 网关 <br> 共享桥 <br> 运维面板 |

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

`neo-bridge-watcher-tron` 接收 TRON RPC/log 流 | 桥合约事件 | 检查点游标，拥有的边界是：过滤桥事件 | 规范化 payload | 保护重放与游标状态。它输出 relay 任务 | 审计日志 | 健康指标，然后由 网关 | 共享桥 | 运维面板 消费。

分层规则：监听、消息规范化、资产状态和最终验证分层。

## 工作流

1. 轮询源链
2. 解码日志
3. 校验确认数
4. 发出 relay 任务
5. 持久化游标

失败路径：确认不足、nonce 重放、消息不匹配或资产状态无效。

## 数据流

1. 源链日志
2. 监听器
3. 标准事件
4. 桥消息

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
