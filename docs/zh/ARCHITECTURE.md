# 架构(中文摘要)

> 这是 [`doc.md`](../../doc.md) 的中文摘要。`doc.md` 是中文权威原本;
> 本文件存在以便交叉对照。下面的章节号与 `doc.md` 对齐。

## §0 目标

构建 **Neo Elastic Network** —— 多条 Neo 4 L2 链锚定到 Neo N3 / Neo 4 L1,
共享统一的桥、结算合约套件、证明聚合层、跨链消息协议。借鉴自 ZKsync
Elastic Chain(共享桥、链注册表、证明聚合、原生互通),在 Neo 技术栈
上重新构建:dBFT 2.0 终结性、NEP-17、NeoVM、NeoFS。

## §1 分层架构

```
Neo N3 / Neo 4 L1               结算、资产托管、治理
    │
    ▼
NeoHub(L1 合约)                注册表、桥、结算、消息
    │
    ▼
Neo Gateway(可选)              证明聚合、L2 间消息根
    │
    ▼
多条 Neo 4 L2 链                Neo 4 core + L2 扩展
```

L2 链的应用方向:RWA、稳定币、DEX、游戏、企业、隐私。

## §3.2 NeoHub 组件

L1 核心合约套件:

- **ChainRegistry** —— 注册 L2 链;每条记录 = `{chainId, operatorManager, verifier, bridgeAdapter, messageAdapter, securityLevel(0-3), daMode(0-3), gatewayEnabled, permissionlessExit, active}`
- **SharedBridge** —— 托管规范 GAS / NEO / NEP-17;铸销规则;充值 + 提款最终化
- **SettlementManager** —— 接受 `L2BatchCommitment`(chainId、batchNumber、pre/postStateRoot、txRoot、receiptRoot、withdrawalRoot、l2ToL1MessageRoot、l2ToL2MessageRoot、daCommitment、publicInputHash、proofType、proof)
- **VerifierRegistry** —— 可插拔验证器:Multisig、Optimistic、ZkRiscV、Aggregated
- **MessageRouter** —— L1↔L2 与 L2↔L2 消息队列,带防重放
- **TokenRegistry** —— 规范的 L1↔L2 资产映射
- **DARegistry** —— 按链记录 DA 承诺
- **GovernanceController** —— 准入策略、verifier 升级、桥的紧急控制
- **EmergencyManager** —— 暂停、逃生通道

## §4 Neo Gateway

可选层。镜像 ZKsync Gateway:从多条 Neo L2 收集证明、聚合、维护 L2-L2
的 `globalMessageRoot`,向 NeoHub 提交聚合后的结算。**不托管资产** ——
资产仍锁在 NeoHub / SharedBridge。

## §5–§7 L2 链内部

每条 L2 = `Neo 4 core` + L2 扩展:

- **排序器** —— dBFT 委员会优于中心化排序器(单块终结性是 Neo 的强项)
- **批处理器** —— 把 L2 块打包成 `L2BatchCommitment`
- **StateRootGenerator** —— 产出 `preStateRoot`、`postStateRoot`、`txRoot`、`receiptRoot`、`withdrawalRoot`、`l2ToL1MessageRoot`、`l2ToL2MessageRoot`
- **DAWriter** —— 把批次数据写到 L1 DA、NeoFS DA 或 DAC
- **ProverAdapter** —— Stage 0(多签 attestation)→ Stage 1(乐观)→ Stage 2(ZK 有效性)
- **SettlementSubmitter** —— 把批次提交到 NeoHub 或 Gateway
- **BridgeAdapter** —— L2 侧的充值 / 提款处理
- **MessageAdapter** —— L2 侧的跨链消息
- **ForcedInclusionHandler** —— 抗审查:用户可直接把 tx 投递到 L1 强制纳入队列;排序器须在 deadline 前纳入,否则被罚没
- **DurableStateBackend** —— 默认基于 RocksDB 的 `IL2KeyValueStore`;重启后保留。6 个组件持久化状态:keyed state、RPC 证明、消息路由证明、强制纳入 nonce、排序器委员会 + 退出窗口、DA payload。详见 [`docs/zh/persistence.md`](persistence.md)。
- **ChainAuditor** —— 对生成的承诺跑 6 项不变量检查(连续性、证明有效性、非零证明、public-input-hash、批次范围、DA 可用性);发出 `l2.audit.runs` + `l2.audit.failures` 给运维仪表盘。

ChainMode:`L1Mode` | `SidechainMode` | `L2RollupMode` | `L2ValidiumMode`。

## §8 证明系统

**不要证明整个 C# 节点。**只证明确定性的状态转移函数:

```
ApplyBatch(preStateRoot, orderedTxs, l1Messages, blockContext)
  → (postStateRoot, receiptsRoot, withdrawalRoot, messageRoot)
```

Public input 包括以上所有根 + `chainId`、`batchNumber`、`daCommitment`、
`blockContextHash`。Witness 包括:有序 tx、合约字节码、storage 读写
witness、原生合约状态 witness、消耗的 L1 消息、DA 数据、执行 trace。

VM 证明目标:NeoVM2 / RISC-V(按 Neo 4 路线图,与 RISC-V 指令集兼容)。

## §9 代币模型

- **规范 GAS** 仅生活在 Neo N3 / Neo 4 L1 上。
- **L2 GAS** = SharedBridge 锁定的 GAS,在 L2 上以"已桥接 GAS"形式存在。**L2 不能独立增发规范 GAS。**
- L2 费默认用已桥接 GAS;paymaster 允许稳定币 / 赞助式付费。
- **NEO** 可以桥到 L2,但治理权留在 L1。
- **NEP-17** 经 `TokenRegistry` 映射。

## §10 Neo Connect(跨链)

- L1→L2:`NeoHub.enqueueL1ToL2Message()` → L2 监听队列 → L2 在下一批次纳入。
- L2→L1:L2 发出消息 → 批次中的 `messageRoot` → 在 NeoHub 上最终化 → 用户提交 Merkle 证明消费。
- L2→L2:源 L2 发出 → 批次最终化 → `globalMessageRoot` 更新 → 中继者向目标 L2 提交证明。
- **跨链 bundle**:面向用户的单笔 tx,内部跨多条 L2。

## §11 桥

**NeoHub 中只有一个 SharedBridge**,服务所有 L2 —— 没有按链的桥。资产
映射 = `{l1Asset, l2ChainId, l2Asset, assetType, mintBurn|lockMint, active}`。
提款只来自已最终化的 `withdrawalRoot`。所有桥消息都带 `chainId` + `nonce`
防重放。

## §12 数据可用性分层

- **L1 DA** —— 最高成本、最高安全;面向高价值链(DeFi、RWA、稳定币)。
- **NeoFS DA** —— 低成本、Neo 生态原生;面向游戏、社交、企业。
- **DAC** —— 最低成本、最高风险;**必须**在 `ChainRegistry` 显式标注。

## §13 L2 原生合约

L2 上的新原生合约:
- `L2BridgeContract` —— 桥接资产的铸 / 销
- `L2MessageContract` —— 发出 / 消费跨链消息
- `L2BatchInfoContract` —— 暴露 `chainId`、`batchNumber`、L1 已最终化高度
- `L2FeeContract` —— 排序器 / 证明者 / DA 的费管理
- `L2PaymasterContract` —— 稳定币 / 赞助式付费
- `L2SystemConfigContract` —— 从 NeoHub 同步 config

调整后的合约:`GAS`(供应受桥控制)、`NEO`(可桥但治理在 L1)、`Oracle`(本地或经 L1)、`Policy`(本地费,桥 / 安全经 NeoHub)。

## §14 RPC / SDK / 工具链

L2 RPC 新增:`getl2batch`、`getl2batchstatus`、`getl2stateroot`、
`getl2withdrawalproof`、`getl2messageproof`、`getl1depositstatus`、
`getcanonicalasset`、`getbridgedasset`、`getsecuritylevel`、
`getsecuritylabel`(完整 5 维 §16.2 标签)。

`neo-stack` CLI:`create-chain`、`init-l2`、`register-chain`、
`deploy-bridge-adapter`、`start-{sequencer,batcher,prover}`、`submit-batch`。

## §16 三层治理

- **L1**:NeoHub 升级、verifier 升级、桥升级、紧急暂停、L2 准入策略
- **L2 本地**:排序器委员会、本地费策略、本地 app-chain 参数、本地 DA 模式(在批准范围内)
- **App**:dApp 规则、RWA 发行人策略、稳定币策略、企业准入

每条 L2 必须公布安全标签:链类型、DA 模式、证明模式、排序器模型、退出
模式、桥模型。

## §17 威胁模型 + 缓解

10 类威胁(排序器审查、无效状态根、桥被攻击、重放、DA 不可用、恶意验证人
委员会、prover bug、verifier 升级攻击、消息重复、L2 合约 bug)。每类都有
点名的缓解(强制纳入、ZK 有效性证明、限速、nonce + chainId、DA 安全标签、
治理延迟 + 安全委员会否决,等等)。

## §18 分阶段上线

| Phase | 目标                                      | 安全标签                   |
| ----- | ----------------------------------------- | -------------------------- |
| 0     | Neo 4 侧链 PoC                            | 侧链                       |
| 1     | NeoHub v0 + SharedBridge                  | 已连侧链                   |
| 2     | 批次结算                                   | 已结算 L2                  |
| 3     | 乐观挑战窗口                               | 乐观 rollup                |
| 4     | NeoVM2 / RISC-V 有效性证明                 | ZK 有效性 rollup           |
| 5     | Neo Gateway 聚合 + L2-L2 消息             | Neo Elastic Network        |
| 6     | Neo Stack CLI + 模板                       | (无许可上链)               |

## §20 MVP

证明架构跑得通的最小可交付:

1. 用户能从 Neo N3 把 GAS 充值到 Neo 4 L2 devnet
2. 用户能在 L2 上部署 / 调用 Neo 合约
3. L2 产出一份 `L2BatchCommitment`
4. 该批次落到 NeoHub 上
5. 用户能用 `withdrawalRoot` 证明把 GAS 提回 N3

**MVP 之外:** 完整 ZK 证明、无许可 L2 上链、全代币桥、L2-L2 合约调用、
Gateway 聚合。这些都在后续阶段。

## §22 关键设计权衡

| 问题                  | 选择                              | 原因                                     |
| --------------------- | --------------------------------- | ---------------------------------------- |
| L2 执行内核            | Neo 4 core                        | 复用 Neo VM、原生合约、工具链            |
| 排序器                 | dBFT 委员会                       | 原生单块终结性                           |
| L1 结算                | NeoHub                            | 一份状态 / 资产 / 消息 / 治理根         |
| 桥                    | SharedBridge                      | 避免按链桥的碎片化                       |
| 证明分阶段             | Attestation → Optimistic → ZK     | 起步门槛低,保留无信任的目标             |
| VM 证明目标            | NeoVM2 / RISC-V                   | 与 Neo 4 路线图对齐                      |
| DA                    | L1 + NeoFS + DAC 分层             | 不同链不同的成本 / 安全权衡              |
| 跨链                   | Neo Connect(原生消息 + 调用)    | 不只是资产桥                             |
| 多 L2 扩展             | Neo Gateway + Neo Stack          | 网络效应                                 |
