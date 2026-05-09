# 架构:L2 链生命周期

> 详细走一遍 Neo Elastic Network 的结构,以及一条 L2 链如何从"不存在"走到"已注册、
> 产出批次、与共享桥 + 跨链消息接通"。
>
> 配套阅读 [`architecture-walkthrough.md`](./architecture-walkthrough.md)
> (那篇讲 L2 内一笔*交易*的生命周期)与
> [`launching-an-l2.md`](./launching-an-l2.md)(运维步骤指南)。本文是架构视角:
> 每步做什么、触及哪些组件、什么线协议数据跨越哪条边界。

## 目录

1. [系统鸟瞰](#1-系统鸟瞰)
2. [4 层细解](#2-4-层细解)
3. [L2 链的解剖](#3-l2-链的解剖)
4. [创建:从零到已注册](#4-创建从零到已注册)
5. [部署:合约上链](#5-部署合约上链)
6. [运行时连接:L2 怎么跟 L1 对话](#6-运行时连接l2-怎么跟-l1-对话)
7. [跨 L2 消息传递](#7-跨-l2-消息传递)
8. [外链桥接通](#8-外链桥接通)
9. [组件交叉索引](#9-组件交叉索引)

---

## 1. 系统鸟瞰

Neo Elastic Network 由**4 层**组件 + 把它们连起来的链下基础设施组成:

<p align="center">
  <img src="../figures/architecture/system-tiers.svg" alt="4 层拓扑:第 1 层 NeoHub L1 锚、第 2 层可选 Neo Gateway、第 3 层弹性 L2 链、第 4 层链下运营者" width="900">
</p>

**什么从哪里流向哪里:**

| 流动                        | 从 → 到                                            | 线协议格式                                       |
|-----------------------------|---------------------------------------------------|------------------------------------------------|
| 已封装批次 + 证明           | 批处理器 → NeoHub.SettlementManager                | `BatchSerializer`(规范 32 字节字段)             |
| DA payload                  | DA writer → NeoFS / L1 / 委员会                   | `IDAWriter` 实现特有                           |
| 跨 L2 消息                  | L2 sender → NeoHub.MessageRouter → L2 receiver    | `MessageHasher` 规范字节                       |
| L1→L2 充值                  | 用户 → NeoHub.SharedBridge → L2NativeBridge       | `DepositPayload`                               |
| L2→L1 提款                  | L2 用户 → SettlementManager Merkle 证明            | `WithdrawalRecord` + Merkle 路径               |
| 外链 → Neo                  | EVM/Solana → Watcher → ExternalBridgeEscrow       | `ExternalCrossChainMessage`(102B + payload)   |
| 聚合证明(Phase 5)         | Gateway → SettlementManager                       | `BinaryTreeAggregator` 轮证明                  |

---

## 2. 4 层细解

### 第 1 层:NeoHub(L1)

L1 锚。**20 个合约**按关注点分组:

<p align="center">
  <img src="../figures/architecture/neohub-anatomy.svg" alt="NeoHub L1 解剖:合约按 Settlement、Bridge、Messaging、Security、Governance、External Bridge 6 个关注点分组" width="900">
</p>

位于 `contracts/NeoHub.*` —— 每个合约都经 `Neo.SmartContract.Framework` 通过类型
检查;CI 用 `nccs` 编译每一个并校验 `.nef` + `.manifest.json` 工件。

**关键关系:**
- `SettlementManager` 消费由 `VerifierRegistry` 校验过的证明,在每个被接受的批次
  上触发 `SharedBridge.ApplyWithdrawals`。
- `SharedBridge` 经 `ChainRegistry` 查链 config,经 `TokenRegistry` 查 token
  元信息。
- `OptimisticChallenge` 把 fraud-verifier 升级上提到带多签 + timelock 的
  `GovernanceController`。
- `ExternalBridgeEscrow` 经 `ExternalBridgeRegistry` 查带曲线 tag 的 verifier;
  `MpcCommitteeFraudVerifier` 罚没存放在 `ExternalBridgeBond` 的保证金。

### 第 2 层:Neo Gateway(可选,Phase 5)

把多 L2 的证明聚合为 L1 上的单次结算提交。运行 >1 条 L2 链时降低 L1 gas 成本。

- `BinaryTreeAggregator` —— 在 N 个成员批次上做 log-N 轮收敛。
- `IRoundProver` —— 3 份生产实现 + 一个递归-ZK 接缝:
  - `MultisigRoundProver` —— Secp256r1 阈值证明轮
  - `MerklePathRoundProver` —— 逐成员 inclusion 证明
  - `PassThroughRoundProver` —— 最低成本参照
  - SP1 Compress / Halo2 / Risc0 fold 变体接进同一 trait

可选:单条 L2 不需要 Gateway。多 L2 部署若想要更低的按批次 L1 gas 成本,在链
config 里把 `gatewayEnabled` 翻为 true。

### 第 3 层:L2 链

每条 L2 = **Neo 4 core(共识 + VM 内核)+ 8 个插件 + 7 个原生合约**。插件位于
`src/Neo.Plugins.L2*/`,原生合约位于 `contracts/L2Native.*`。Neo 4 core 自身作
为 git submodule 引入到 `external/neo`。

<p align="center">
  <img src="../figures/architecture/l2-components.svg" alt="L2 链组件 —— Neo 4 core(底)+ 8 个 L2 插件(中)+ 7 个 L2 原生合约(顶)" width="900">
</p>

8 个插件 + 7 个原生合约实现 `doc.md` §5–§13 的分层架构(批次封装 / 结算 / 桥 /
DA / 证明 / RPC / gateway / metrics)。

### 第 4 层:链下运营者

每条 L2 至少需要以下各一份:

| 运营者          | 干什么                                                            | 源码                                    |
|-----------------|------------------------------------------------------------------|-----------------------------------------|
| 排序器           | dBFT 2.0 共识成员;产出 L2 区块                                  | `Neo.L2.Sequencer/`                     |
| 批处理器         | 订阅 `Blockchain.Committed`,封装批次,提交到 L1                 | `Neo.L2.Batch/` + `Neo.Plugins.L2Batch` |
| 证明守护进程     | SP1 zkVM 证明该批次(Phase 4)                                   | `bridge/neo-zkvm-host/`(Rust 二进制) |
| DA writer       | 把批次 payload 发到 NeoFS / L1 / 委员会                         | `Neo.L2.DA*` + 注入的 `IDAWriter`      |
| 外链 watcher     | (仅外链桥)中继 EVM/Solana 事件 → Neo                          | `watchers/neo-bridge-watcher-*/`        |

---

## 3. L2 链的解剖

每条 L2 链由**4 个工件**完整定义:

<p align="center">
  <img src="../figures/architecture/l2-anatomy.svg" alt="一条 L2 链由什么定义 —— 4 个工件:chain.config.json(91 字节规范 config)、ITransactionExecutor 实现、L1 上的 ChainRegistry 条目(4 个 UInt160 引用 + configBytes)、链下运营者(排序器/批处理器/证明者/DA writer)" width="900">
</p>

### §16.2 链 config 维度

链 config 携带 5 个维度。运维者每条链自选;同一个 NeoHub L1 支持任意组合:

| 维度             | 取值(范围)                  | 含义                                                |
|-----------------|-------------------------------|----------------------------------------------------|
| `securityLevel` | 0 · 1 · 2 · 3                 | 0 = 侧链(最低);3 = 完整 ZK rollup(最高)         |
| `daMode`        | InMemory · External · L1 · DAC| 批次 payload 去哪儿                                 |
| `sequencerModel`| Solo · Committee · Permissionless | 区块怎么产出                                   |
| `exitModel`     | Optimistic · Permissionless · ZkValidity | 提款怎么结算                              |
| `gatewayEnabled`| bool                          | 此 L2 是否批入共享 Gateway                          |

经 `L2ChainConfigSerializer`(见 `Neo.L2.Abstractions/L2ChainConfigSerializer.cs`)
编码为 91 字节规范线协议格式。

### 模板

`neo-stack list-templates` 出货 4 个起步点:

| 模板          | securityLevel | daMode    | exitModel       | gatewayEnabled |
|---------------|---------------|-----------|-----------------|----------------|
| `rollup`      | 2             | L1        | Optimistic      | true           |
| `zk-rollup`   | 3             | L1        | ZkValidity      | true           |
| `validium`    | 2             | DAC       | Optimistic      | true           |
| `sidechain`   | 1             | InMemory  | Permissionless  | false          |

---

## 4. 创建:从零到已注册

链从 `git clone` 到首个封装批次落到 L1 的完整生命周期,以编号序列在角色间表示:

```text
          运维者         neo-stack     文件系统      L1 钱包      NeoHub.ChainRegistry      L2 排序器
              │              │              │              │                  │                        │
              │              │              │              │                  │                        │
   1.  ──── new-l2 ────▶     │              │              │                  │                        │
   2.        │   ──── 脚手架 ────────▶    │              │                  │                        │
              │              │              │              │                  │                        │
            ─── 阶段 1:仅脚手架 —— 链有身份但无链上存在 ───
              │              │              │              │                  │                        │
   3.  ──── validate ────▶   │              │              │                  │                        │
   4.        │   ──── JSON 健全性检查 ──▶ │              │                  │                        │
   5.        │  ◀──── OK / 错误 ─────     │              │                  │                        │
              │              │              │              │                  │                        │
   6.  ──── register-chain ─▶│              │              │                  │                        │
   7.        │  ◀── 91 字节 configBytes hex + ChainRegistry.RegisterChain 计划 │                       │
              │              │  (计划打印器;永不持有密钥)                    │                        │
   8.  ─── 粘贴参数 ─────────────────────▶│                                  │                        │
   9.        │              │              │  ─── RegisterChain(chainId, configBytes, ...) ─▶          │
  10.        │              │              │  ◀───────── tx 接受 ─────────                              │
              │              │              │              │                  │                        │
  11.  ─── deploy-bridge-adapter ▶         │              │                  │                        │
  12.        │  ◀── L2NativeBridge 部署计划            │                  │                        │
              │              │              │  ─── 部署 ─▶(运维钱包)                               │
              │              │              │              │                  │                        │
            ─── 阶段 2:NeoHub 知道该链;桥 + 消息传递解锁 ───
              │              │              │              │                  │                        │
  13.  ─── start-sequencer ▶│              │              │                  │                        │
  14.        │  ◀── preflight + "用 neo-cli 组合" 说明 │                  │                        │
  15.  ─── 启动 neo-cli + L2 插件 ─────────────────────────────────────────────▶                  │
  16.        │              │              │              │                  │  ─── dBFT 2.0 启动 ─▶│
              │              │              │              │                  │                        │
  17.  ─── start-batcher ──▶│              │              │                  │                        │
  18.        │              │              │              │                  │  ◀── BatchInfoContract ┘
  19.        │              │              │              │  ◀──── SettlementManager.SubmitBatch ───── │
  20.        │              │              │              │                  │ ◀── 结算被接受 ──────│
              │              │              │              │                  │                        │
            ─── 阶段 3:L2 批次落到 L1;桥 + 消息端到端工作 ───
```

3 个阶段:

| 阶段 | 此阶段后什么为真                                                  |
|------|-------------------------------------------------------------------|
| 1    | 本地文件存在;链有身份但无链上存在                                |
| 2    | NeoHub 知道该链;排序器在产出 L2 块                              |
| 3    | L2 批次落到 L1;桥 + 消息传递端到端工作                           |

### `new-l2` 组合命令

`neo-stack new-l2 --name X --chain-id Y --template Z` 命令把 3 个低层操作串在
一起。生成什么:

```text
    ./MyChain/
    ├── chain.config.json              ← 可编为 91 字节的规范 config
    ├── MyChainExecutor/
    │   ├── MyChainExecutor.csproj
    │   ├── src/
    │   │   ├── MyChainExecutor.cs     ← ITransactionExecutor stub
    │   │   ├── MyChainStateSeam.cs    ← 状态 store 绑定
    │   │   └── MyChainTxBuilder.cs    ← 充值/消息 tx helper
    │   └── README.md
    ├── MyChainExecutor.UnitTests/     ← --with-tests
    │   ├── MyChainExecutor.UnitTests.csproj
    │   └── *.Tests.cs                 ← 3 条起步测试
    └── data/  logs/  Plugins/         ← 节点工作目录
```

`MyChainExecutor` 脚手架是给需要自定义交易语义的链(例如 RWA 链带 KYC 检查、
DEX 链内置撮合)的起步点。只需要标准 NeoVM + NEP-17 的链不用定制 —— 它们用
`src/Neo.L2.Executor/` 出货的 `ApplicationEngineTransactionExecutor`。

### 三阶段准入策略

无许可链注册经 `[plan: §16.1-admission]` 把关 —— L2 链注册表分 3 层:

<p align="center">
  <img src="../figures/architecture/admission-states.svg" alt="L2 链准入的 3 阶段状态机:Approved → Stamped → Active" width="900">
</p>

---

## 5. 部署:合约上链

哪些合约去哪儿、按什么顺序:

<p align="center">
  <img src="../figures/architecture/deployment-flow.svg" alt="L2 部署流 —— 4 步有序:每网络一次性经 neo-hub-deploy 部署 NeoHub、经 neo-stack register-chain 注册一条 L2 链、经 neo-stack deploy-bridge-adapter 部署 L2 桥 adapter,以及可选地经 MessageRouter.RegisterAdapter 接好消息传递 adapter" width="900">
</p>

每条命令输出结构化计划而非直接提交 —— 框架永不持有私钥。运维者把生成的 hex /
UInt160 参数粘进自己挑的钱包(NeoLine、Neon、NEP-6、Ledger、KMS 驱动的自研签
名器)。模式见 [`docs/wallet-integration.md`](./wallet-integration.md)。

### L2 需要知道的合约地址

部署后,L2 的 config 携带 4 个 NeoHub UInt160 引用:

```toml
# 在 L2 的运行时 config 里(与 chain.config.json 分开):
neo_hub_chain_registry      = 0x...  # ChainRegistry
neo_hub_settlement_manager  = 0x...  # SettlementManager
neo_hub_shared_bridge       = 0x...  # SharedBridge
neo_hub_message_router      = 0x...  # MessageRouter(若启用跨 L2)
```

加上自身的 L2 侧合约:
```toml
l2_native_bridge_hash       = 0x...  # 此 L2 的 L2NativeBridgeContract
l2_native_message_hash      = 0x...  # 此 L2 的 L2MessageContract
l2_batch_info_hash          = 0x...  # 此 L2 的 L2BatchInfoContract
```

---

## 6. 运行时连接:L2 怎么跟 L1 对话

部署后,一条 L2 链经 3 条独立通道"接通"—— 各自按自己的节奏跑:

<p align="center">
  <img src="../figures/architecture/runtime-channels.svg" alt="L2 ↔ L1 运行时连接 —— 3 条独立通道:结算(热路径)、桥(经 DepositReady 和 WithdrawalReady 做资产转移)、跨 L2 消息(InboundMessage 和 OutboundMessage)。3 条通道独立运行,一条故障或延迟不阻塞另外两条" width="900">
</p>

### 通道 1 —— 结算(热路径)

每个 L2 块:

<p align="center">
  <img src="../figures/architecture/settlement-sequence.svg" alt="结算热路径序列 —— 5 个角色 L2 Blockchain、L2BatchPlugin、BatchSealer、证明守护进程、SettlementManager。Block.Committed → tx 批 + post-state-root → BatchSealer 构造规范 BatchCommitment → BatchPayload 到证明守护进程 → SP1 zkVM 证明 execute_batch → validity_proof + vk 回传 → SubmitBatch 到 SettlementManager → VerifierRegistry.Verify 派发 → SettlementAccepted 事件" width="900">
</p>

线协议格式:`BatchSerializer`(`Neo.L2.Batch/`)—— 规范顺序的 32 字节字段。
按 tx 的细节见 `architecture-walkthrough.md` 的"transaction lifecycle"。

### 通道 2 —— 桥(资产转移)

<p align="center">
  <img src="../figures/architecture/bridge-sequences.svg" alt="桥的双面板序列图。上面板 L1→L2 充值:L1 用户在 SharedBridge 上调 Deposit → 资产被锁、发出 DepositReady → L2 批处理器中继到 L2BridgeContract → 铸包装资产 → L2 用户余额到账。下面板 L2→L1 提款:L2 用户在 L2BridgeContract 上调 Withdraw → 销包装资产、发出 WithdrawalReady → 提款记录在下一批次封装 → L1 用户带 Merkle 证明调 ClaimWithdrawal → SharedBridge.VerifyWithdrawalLeafWithProof 释放资产" width="900">
</p>

线协议格式:L1→L2 用 `DepositPayload`,L2→L1 用 `WithdrawalRecord` + Merkle 路径。
两个 encoder 都在 `Neo.L2.Bridge/`。

### 通道 3 —— 跨 L2 消息传递(可选)

见下文 [§7](#7-跨-l2-消息传递)。

---

## 7. 跨 L2 消息传递

当 `gatewayEnabled = true` 且 `messageAdapter` 已配置时,L2-A 可以无需手动触及
L1 即可向 L2-B 发消息:

```text
    L2-A 上的用户   L2-A.L2MessageContract     NeoHub.MessageRouter     L2-B.L2MessageContract     L2-B 上的接收方
        │                       │                         │                         │                       │
        │── SendMessage(targetChainId, payload) ─▶        │                         │                       │
        │                       │                         │                         │                       │
        │                       │ 发出                                              │                       │
        │                       │ OutboundMessage 事件                              │                       │
        │                       │                         │                         │                       │
        │     (L2-A 批处理器把消息纳入下一封装批次)                                  │                       │
        │                       │                         │                         │                       │
        │                       │── (经批次结算)RouteMessage                       │                       │
        │                       │   (srcChainId, dstChainId, payload, hash) ──▶     │                       │
        │                       │                         │                         │                       │
        │                       │     (L2-B 批处理器从 MessageRouter poll 入站)                              │
        │                       │                         │── InboundMessage ──────▶│                       │
        │                       │                         │                         │                       │
        │                       │                         │                         │ VerifyMessageHash     │
        │                       │                         │                         │ (按规范 encoder      │
        │                       │                         │                         │  比对)                │
        │                       │                         │                         │                       │
        │                       │                         │                         │── 投递 payload ──────▶│
```

`MessageHasher`(`Neo.L2.Messaging/`)是规范 encoder —— 两端从线字节重算哈希;
合约从不信任线外哈希。端到端,消息跨 3 条信任边界(L2-A 共识 → L1 结算 → L2-B
共识);每条边界独立验证哈希。

---

## 8. 外链桥接通

跨外链桥(Phase B/C,`doc.md` §11.3)让外链(Eth/EVM 家族 / Solana / Tron)经
同一 SharedBridge 接口做充值 + 提款。架构上:

<p align="center">
  <img src="../figures/architecture/external-bridge-architecture.svg" alt="外链桥架构:外链上的 NeoExternalBridgeRouter.sol(整个 EVM 家族共用一份合约),watcher 守护进程 poll Locked 事件并对规范 ExternalCrossChainMessage 签名,NeoHub L1 经 MpcCommitteeVerifier 验证、经 MpcCommitteeFraudVerifier + ExternalBridgeBond 罚没等价签名,L2 链在 L2NativeExternalBridgeContract 上接收包装的外链资产" width="900">
</p>

**一份合约服务整个 EVM 家族。** 同一份 `NeoExternalBridgeRouter.sol` 原样部署到
Ethereum / BSC / Polygon / Arbitrum / Optimism / Base / Avalanche / Linea /
zkSync / Scroll / Mantle / Fantom / Celo / Tron —— 它的构造函数从
`watchers/neo-bridge-watcher-eth/src/chains.rs` 中规范的 16 槽位家族 bank 分配
取 `externalChainId`。5 步接入 runbook 见
[`external-bridge-evm-chains.md`](./external-bridge-evm-chains.md)。

watcher 守护进程(生产就绪:graceful SIGTERM、`/healthz`、`/metrics`、基于
flock 的并发实例检测、`min_confirmations` reorg buffer、`--preflight` 校验)
位于 `watchers/neo-bridge-watcher-eth/`。k8s + systemd manifest 在
[`deploy/`](../../watchers/neo-bridge-watcher-eth/deploy/)。

---

## 9. 组件交叉索引

哪个 `neo-stack` 子命令触及哪个组件:

| 子命令                  | 触及(L1)                      | 触及(文件系统)                | 触及(L2)                |
|-------------------------|-----------------------------------|----------------------------------|---------------------------|
| `create-chain`          | —                                 | `chain.config.json`              | —                         |
| `init-l2`               | —                                 | `data/`、`logs/`、`Plugins/`     | —                         |
| `register-chain`        | `ChainRegistry.RegisterChain`     | —                                | —                         |
| `deploy-bridge-adapter` | `SharedBridge.RegisterAdapter`    | —                                | `L2NativeBridgeContract`  |
| `start-sequencer`       | (仅 preflight)                   | 读 config                        | dBFT 2.0 启动             |
| `start-batcher`         | `SettlementManager.SubmitBatch`   | 读 config                        | `L2BatchPlugin` 跑        |
| `start-prover`          | (无 L1 接触)                     | 读 config                        | `L2ProverPlugin` 跑       |
| `submit-batch`          | `SettlementManager.SubmitBatch`   | 读 batch payload                 | —                         |
| `validate`              | —                                 | `chain.config.json` JSON 检查    | —                         |
| `scaffold-executor`     | —                                 | `<Name>Executor.csproj` + tests | —                         |
| `new-l2`                | create + init + scaffold 组合     | 组合                             | —                         |
| `list-templates`        | —                                 | 打印到 stdout                    | —                         |

### 运维部署计划器

NeoHub 自身(每个网络一次性):

```bash
# 生成 20 步有序 bundle:
dotnet run --project tools/Neo.Hub.Deploy -- plan

# 校验 bundle 不变量:
dotnet run --project tools/Neo.Hub.Deploy -- verify

# 每一步是结构化运维计划:{contract, method, args}。
# 运维者钱包按序执行。
```

外链桥委员会设置(每条外链):

```bash
dotnet run --project tools/Neo.External.Bridge.Cli -- committee-blob \
    --pubs-file watchers.pubs    # 一行一个 pub33 hex
# 输出:Neo blob(hex)+ 对应 Eth 地址列表

dotnet run --project tools/Neo.External.Bridge.Cli -- deploy-bundle \
    --external-chain-id 0xE0000030 \
    --verifier <UInt160> --registry <UInt160> --escrow <UInt160> \
    --eth-router 0x... --threshold 4 \
    --committee-blob 0x... --eth-addresses 0x...,0x...,...
# 输出:Neo + Eth 钱包都用得上的有序 checklist。
```

---

## 另请参阅

- [`ARCHITECTURE.md`](../../ARCHITECTURE.md) —— `doc.md` 的英文逐节摘要。
- [`WHITEPAPER.md`](../../WHITEPAPER.md) —— 正式白皮书。
- [`doc.md`](../../doc.md) —— 主中文规格(权威)。
- [`architecture-walkthrough.md`](./architecture-walkthrough.md) —— 代码库叙事
  导览,含按交易的生命周期。
- [`launching-an-l2.md`](./launching-an-l2.md) —— 跑 L2 的运维步骤指南
  (本文讲架构;那篇讲命令)。
- [`external-bridge-roadmap.md`](./external-bridge-roadmap.md) —— Phase B/C 跨
  外链桥。
- [`external-bridge-evm-chains.md`](./external-bridge-evm-chains.md) —— 5 步接
  入新 EVM 链。
- [`security-model.md`](./security-model.md) —— 威胁模型 + 缓解。
- [`tech-stack-coverage.md`](./tech-stack-coverage.md) —— 引入 vs 自实现。
