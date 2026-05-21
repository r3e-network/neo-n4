# Neo N4 规格书：从设计到实现

> 本书是 Neo N4 / Neo Elastic Network 的中文主规格书与学习手册。目标不是只给出愿景，而是让读者能从架构图、协议对象、代码入口、运维流程和测试证据一路读到“这个系统为什么这样设计、如何运行、如何实现、如何验证”。

## 本书如何定位

Neo N4 是基于 Neo Stack 的多 L2 系统实现。它使用 `r3e-network/neo` fork 作为执行内核来源，使用 NeoHub 可部署合约作为 L1 锚定层，使用 NeoFS 作为规范 DA 方向，并把 NeoVM2/RISC-V 作为默认 L2 执行目标。额外 VM 生态应作为 N4 L2 execution profile / executor 接入，而不是把本项目描述成 NeoX。

本书与仓库内其他文档的关系如下：

| 文档 | 角色 | 何时阅读 |
| --- | --- | --- |
| 本书 | 从 0 到实现的中文学习路径 | 第一次系统学习 Neo N4，或要评审整体设计时 |
| [`../doc.md`](../doc.md) | 中文母版设计稿 | 需要完整设计背景、长期路线和原始论证时 |
| [`../architecture-atlas.md`](../architecture-atlas.md) | 架构专题导航 | 已知道问题，想跳到某个专题时 |
| [`../neohub-architecture-and-workflows.md`](../neohub-architecture-and-workflows.md) | NeoHub 合约工作流 | 需要逐合约理解 L1 锚定层时 |
| [`../launching-an-l2.md`](../launching-an-l2.md) | 运维 runbook | 要实际启动、配置或演练一条 L2 时 |
| [`../security-model.md`](../security-model.md) | 威胁模型 | 要审计安全边界和剩余风险时 |

## 推荐阅读顺序

1. [系统模型](./01-system-model.md)：先理解 L1、NeoHub、Gateway、L2、DA、Prover、Bridge 分别是什么。
2. [总体架构](./02-architecture.md)：看系统长什么样，每一层如何连接。
3. [协议与数据结构](./03-protocol-data.md)：理解哪些对象被哈希、提交、证明、路由和消费。
4. [实现导读](./04-implementation.md)：进入代码库，知道每个目录负责什么。
5. [运维与部署](./05-operations.md)：从本地 devnet 到 testnet 的工作流。
6. [安全与测试](./06-security-testing.md)：理解系统如何证明自己正确、以及哪些风险仍需生产配置。
7. [学习路线](./07-reading-path.md)：按角色继续深入。

## 一张图先看全局

<p align="center">
  <img src="../figures/experience-hub/neo-n4-experience-hub.png" alt="Neo N4 Experience Hub：NeoHub、NeoFS DA、ContractZkVerifier、NeoVM2/RISC-V L2 执行、可选 VM profile 和验证证据" width="920">
</p>

这张图可以看作 Neo N4 的“驾驶舱”：

- 左侧是学习、构建、运维、验证入口；
- 中间是 L1 NeoHub、ContractZkVerifier、L2 NeoVM2/RISC-V、NeoFS DA、Gateway 和可选 VM profiles 的数据流；
- 右侧是验证证据，包括 proof、链上验证、DA 状态和测试摘要。

## 核心定义

| 术语 | 定义 |
| --- | --- |
| N4 L2 | 基于 Neo 4 执行内核的 Layer-2 链。它拥有独立状态、批次、DA、证明和 RPC，但通过 NeoHub 连接 L1。 |
| NeoHub | L1 上的一组可部署合约，负责链注册、共享桥、结算、证明路由、消息路由、治理和安全控制。 |
| ContractZkVerifier | NeoHub 中的可部署 ZK verifier router。它不把 NeoHub 做成 L1 native 合约，而是把 proof-system 验证路由到治理注册的可部署验证器合约。 |
| NeoFS DA | N4 的规范数据可用性方向。批次数据写入 DA 层，DA commitment 进入 `L2BatchCommitment`。 |
| NeoVM2/RISC-V | 默认 L2 执行目标。当前实现使用 PolkaVM-backed RISC-V 路径作为 N4 L2 执行 profile 的核心方向。 |
| Execution profile | 可插拔执行 profile，例如未来 EVM/WASM/Move profile。它们是 N4 L2 executor，不是 NeoX。 |
| Proof mode | 批次被 L1 接受所依赖的证明模式，例如 multisig、optimistic、ZK 或 gateway aggregation。 |

## 学习目标

读完本书后，读者应该能够回答这些问题，并能在代码库里找到对应实现：

| 问题 | 本书覆盖位置 |
| --- | --- |
| Neo N4 为什么不是单条 sidechain？ | 第 1 章、第 2 章 |
| NeoHub 为什么是可部署合约，而不是 L1 native 合约？ | 第 1 章、第 2 章 |
| 一笔 deposit 如何进入 L2？ | 第 3 章 |
| 一个 L2 batch 如何被 DA、证明和结算？ | 第 3 章、第 5 章 |
| NeoVM2/RISC-V 在哪里实现？ | 第 4 章 |
| NeoFS DA 在系统中承担什么责任？ | 第 1 章、第 3 章 |
| 如何找到具体代码文件？ | 第 4 章 |
| 如何本地运行和验证？ | 第 5 章 |
| 哪些测试和审计证明当前实现是完整的？ | 第 6 章 |
| 每个 NeoHub 合约具体负责什么？ | 第 8 章 |
| L2 native contracts 如何嵌入 Neo core？ | 第 9 章 |
| 批次和状态根如何形成？ | 第 10 章 |
| NeoFS DA 如何进入结算？ | 第 11 章 |
| SP1 / RISC-V 证明路径如何组织？ | 第 12 章 |
| 资产桥和 decimals 如何处理？ | 第 13 章 |
| 异构链如何接入？ | 第 14 章 |
| SDK 和应用如何使用 N4？ | 第 15 章 |
| 如何用实验学会 N4？ | 第 16 章 |

## 一句话架构

```text
Neo N4 = NeoHub L1 可部署合约锚定层
       + 多条 Neo 4 L2 执行链
       + NeoFS DA
       + 可插拔证明系统
       + 共享桥和消息路由
       + 可选 Gateway 聚合层
       + SDK / CLI / 插件 / watcher / devnet / 测试证据
```

这不是一个单独合约项目，也不是只靠文档定义的路线图。它是一个包含合约、L2 native contracts、.NET 节点库、插件、CLI、SDK、Rust zkVM host、watchers、示例和测试的完整工程仓库。

## 本书分卷

| 分卷 | 章节 | 学习目标 |
| --- | --- | --- |
| 第一卷：概念 | 第 1-3 章 | 建立系统模型、架构图和协议对象的直觉 |
| 第二卷：实现 | 第 4、8-15 章 | 按源码目录理解每个模块如何工作 |
| 第三卷：运行 | 第 5、16-17 章 | 学会运行 devnet、部署 NeoHub、检查生产门槛 |
| 第四卷：验证 | 第 6、18-19 章 | 学会测试、审计、查表和继续阅读 |

