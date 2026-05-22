# neo-zkvm-guest 技术学习指南

这份指南把 `neo-zkvm-guest` 当作 Neo N4 的一个技术单元来解释。它不是源码阅读图，而是帮助读者理解：这个单元负责什么、哪些技术假设保证它正确、数据如何移动、状态如何变化、证据如何被验证、它如何接入 Neo N4 的整体架构。

## 技术契约

| 维度 | 含义 |
| --- | --- |
| 层级 | N4 零知识 guest |
| 目的 | 在 SP1 证明环境中运行确定性 Neo L2 批处理执行的 guest 程序。 |
| 输入 | 公开批次输入 <br> 私有见证 <br> 共享执行核心 |
| 职责 | 运行可验证状态转换 <br> 输出公开值 <br> 拒绝非确定性行为 |
| 输出 | SP1 公开输出 <br> 状态根 <br> 执行摘要 |
| 消费方 | neo-zkvm-host <br> NativeZkVerifier 适配器 <br> 审计工具 |

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

`neo-zkvm-guest` 接收 公开批次输入 | 私有见证 | 共享执行核心，拥有的边界是：运行可验证状态转换 | 输出公开值 | 拒绝非确定性行为。它输出 SP1 公开输出 | 状态根 | 执行摘要，然后由 neo-zkvm-host | NativeZkVerifier 适配器 | 审计工具 消费。

分层规则：guest 证明计算，host 编排，L1 验证压缩结果。

## 工作流

1. 反序列化输入
2. 执行批次
3. 提交公开值
4. 退出 guest

失败路径：证明生成失败、本地验证失败、公开输出不匹配或 verifier 拒绝。

## 数据流

1. 见证 + 批次
2. guest 执行器
3. 公开值
4. 证明产物

承诺信号：状态根、公开值、验证密钥和证明摘要。

## 状态、证明和信任

- 状态转换：guest 执行受公开值和 verifier 规则约束。
- 终局条件：verifier 接受 proof 且公开输出匹配目标状态。
- 信任模型：信任验证密钥和 verifier，不信任 prover 运行环境。
- 验证边界：公开输入、proof envelope、验证密钥和公开输出必须一致。
- 重放与顺序：proof 绑定批次范围和状态根，避免跨批次复用。

## 集成和运行

- NeoFS DA：NeoFS 保存批次数据、见证或轨迹摘要以及可取回证据。
- 证明系统：证明系统把 L2 执行声明压缩为可验证证据。
- Gateway/API：Gateway 负责用户路由、查询、提交和健康状态聚合。
- 桥与异构链：桥规则统一 L1-L2、L2-L2 和异构链消息与资产。
- 可观测证据：proof id、公开输出、验证结果、耗时和失败原因。

在 Neo N4 仓库根目录重新生成这些技术图：

```powershell
python tools/docs/generate_crate_visual_docs.py
```
