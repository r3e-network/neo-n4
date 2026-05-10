# 架构:术语表 + 组件目录

> 单页参考,涵盖架构各章使用的每个术语和框架出货的每个组件。刻意写得浅 ——
> 每条一两行。要更深入,顺着链接到对应章节即可。

## 目录

1. [术语表](#1-术语表)
2. [NeoHub L1 合约](#2-neohub-l1-合约21)
3. [L2 原生合约](#3-l2-原生合约7)
4. [L2 插件](#4-l2-插件8)
5. [链下运营者](#5-链下运营者)
6. [CLI 工具](#6-cli-工具7)
7. [线协议格式](#7-线协议格式速查)
8. [每个术语首次定义在哪里](#8-每个术语首次定义在哪里)

---

## 1. 术语表

| 术语                          | 一行定义                                                                                            |
|-------------------------------|----------------------------------------------------------------------------------------------------|
| **batch(批次)**              | 由 `BatchCommitment` 概括、提交到 L1 的连续 L2 区块封装包。                                        |
| **batchNumber**               | 按链单调递增的封装批次计数器。携带于 `BatchCommitment` + `PublicInputs`。                          |
| **canonical bytes(规范字节)** | 一个逻辑值的唯一字节级编码。两端都从这些字节重算哈希。                                            |
| **chain id**                  | uint32。L2 chain id 从 1024+ 起;外链 chain id 在 `0xE0_xx_xx_xx` 命名空间。                       |
| **chainConfig** / **configBytes** | L2 链 `L2ChainConfig` 在 `ChainRegistry` 中存储的 91 字节规范编码。                            |
| **committee(L2)**            | 产出 L2 区块的 dBFT 2.0 共识成员。区别于外链桥委员会。                                            |
| **committee(外链桥)**        | 对外链事件做证明的 M-of-N 签名者集合。经 `ExternalBridgeBond` 质押。                              |
| **CommittedEvent**            | Neo `Blockchain` 在 dBFT 终结后发出的 `Block.Committed` 事件。驱动 L2 批处理器。                   |
| **daCommitment**              | 承诺批次数据可用性负载的哈希。存储于 `BatchCommitment` + `PublicInputs`。                          |
| **daMode**                    | 批次负载去哪儿:0=InMemory、1=External、2=L1、3=DAC。                                              |
| **dBFT 2.0**                  | Neo 的 BFT 共识;最多容忍 1/3 拜占庭排序器。                                                      |
| **direction(方向)**          | 外链消息方向:1=NeoToForeign、2=ForeignToNeo。                                                    |
| **exitModel**                 | 提款怎么结算:0=Optimistic、1=Permissionless、2=ZkValidity。                                       |
| **externalChainId**           | uint32,在 `0xE0_xx_xx_xx` 内;在跨外链桥中标识一条外链。                                          |
| **family bank(家族 bank)**    | 外链命名空间中分给一个链家族(Eth / BSC / Polygon 等)的连续 16 槽。                              |
| **forced inclusion(强制纳入)**| L1 驱动、绕过审查排序器的机制;用户在 L1 上 post tx → L2 必须纳入。                                |
| **gatewayEnabled**            | bool —— 该 L2 是否批入可选的共享 `BinaryTreeAggregator`(Phase 5)。                              |
| **L2NativeBridge**            | NeoHub `SharedBridge` 在 L2 一侧的对应件。按 (chainId, asset) 铸造/销毁包装资产。                  |
| **MerkleProofSerializer**     | Merkle 证明的规范编码器(用于提款 + 跨 L2 消息)。                                                |
| **MessageHasher**             | `CrossChainMessage`(跨 L2)的规范编码器。两端都重算哈希。                                        |
| **min_confirmations**         | watcher config 字段:不从距外链头不足 N 确认的浅块发出事件。                                       |
| **NeoHub**                    | 锚定整个网络的 21 合约 L1 套件。见下文 §2。                                                       |
| **nonce(deposit/message)**   | 按 (源链、方向) 单调递增的计数器。带重放保护。                                                    |
| **operatorManager**           | UInt160。管理一条已注册 L2 的多签(set-verifier、pause 等)。在链 config 里。                      |
| **postStateRoot**             | UInt256。批次最后一笔 tx 之后的状态根。携带于 `BatchCommitment`。                                  |
| **preStateRoot**              | UInt256。批次第一笔 tx 之前的状态根。必须等于上一批次的 `postStateRoot`。                          |
| **proofType**                 | byte。0=Multisig、1=RiscVZk、2=Optimistic、…—— 每条链经链 config 选择。                            |
| **publicInputHash**           | UInt256。`PublicInputs`(332 字节)的 SHA256。验证器从链上承诺重算它。                             |
| **securityLevel**             | byte 0..3。0 = 侧链,3 = 完整 ZK rollup。运维者每条链自选。                                       |
| **sequencerModel**            | byte。0=Solo、1=Committee、2=Permissionless。L2 区块怎么产出。                                    |
| **SettlementManager**         | NeoHub L1 合约。验证已提交批次;承担信任的边界。                                                  |
| **§16.2 维度**                | 5 维链 config:securityLevel、daMode、sequencerModel、exitModel、gatewayEnabled。                  |
| **trust boundary(信任边界)** | 字节跨越信任域的点。系统有 5 个跨层边界。                                                         |
| **VerifierRegistry**          | NeoHub L1 合约。按 `proofType` 派发证明验证。                                                     |
| **watcher(中继器)**           | 中继外链事件(Eth/Tron/Solana → Neo)的链下守护进程。                                            |
| **wire format(线协议格式)** | 一个逻辑值的规范字节布局。见 [`architecture-wire-formats.md`](./architecture-wire-formats.md)。   |
| **withdrawalRoot**            | UInt256。本批次内 L2→L1 提款的 Merkle 根。用户凭 Merkle 证明领取。                                |

---

## 2. NeoHub L1 合约(21)

位于 `contracts/NeoHub.*`。每个都是已编译的 .nef + .manifest.json。

### 核心 5 个(每个批次都触及)

- **`SettlementManager`** — 验证已提交批次;最终化状态根 + 提款;派发到验证器。
- **`VerifierRegistry`** — 按 `proofType` 派发验证器(Multisig / RiscVZk / Optimistic / …)。
- **`ChainRegistry`** — 注册 L2 链;按 chain id 存储 91 字节 `L2ChainConfig`。
- **`SharedBridge`** — 跨所有已注册链的 L1 充值 + 提款。持有托管资产。
- **`MessageRouter`** — 经重算规范哈希路由跨 L2 消息;按 (源链, 目标链) 的 inbox。

### 桥支持(3)

- **`TokenRegistry`** — 资产元信息(symbol、decimals、原生链)。`SharedBridge` 使用。
- **`DARegistry`** — 记录已发布的 `daCommitment` 哈希;`L2DAPlugin` 在每个批次写入此处。

### 安全(5)

- **`SequencerRegistry`** — 列出每条链已注册的排序器。带保证金。
- **`SequencerBond`** — 排序器可罚没保证金。被 `OptimisticChallenge` 在欺诈被接受时罚没。
- **`ForcedInclusion`** — 抗审查:用户在 L1 post tx;L2 必须在 deadline 前纳入,否则排序器被罚。
- **`OptimisticChallenge`** — 二分博弈驱动的欺诈证明窗口。结算等 `challengeWindow` 后才最终化。
- **`EmergencyManager`** — 个别链的运维多签暂停(例如调试关键问题时)。

### 治理(2)

- **`GovernanceController`** — 多签 + timelock,用于验证器升级 + 协议参数变更。
- **`GovernanceFraudVerifier`** — 参考 fraud verifier —— v0 由治理仲裁被挑战的批次。

### 专用 fraud verifier(1)

- **`RestrictedExecutionFraudVerifier`** — v3:从 storage 证明重新派生 pre/post 状态根;接受 well-formed 声明而无需治理仲裁。

### 外链桥 —— Phase B/C(6)

- **`MpcCommitteeVerifier`** — 在规范 `ExternalCrossChainMessage` 上验证 M-of-N 委员会签名。
- **`ExternalBridgeRegistry`** — 按链的 (verifier、bridgeKind) 条目。路由到 MPC 或 ZK 轻客户端(Phase D)。
- **`ExternalBridgeEscrow`** — 为外链入站铸/销包装资产;带重放保护。
- **`ExternalBridgeBond`** — 外链桥委员会成员的可罚没保证金。
- **`ExternalBridgeStubVerifier`** — v0 测试 stub —— 自动接受任何消息。**不**用于生产。
- **`MpcCommitteeFraudVerifier`** — Phase C:从密码学上证明委员会等价签名;经 `ExternalBridgeBond` 罚没。

---

## 3. L2 原生合约(7)

位于 `contracts/L2Native.*`。部署到每条 L2 链上。

- **`L2BridgeContract`** — L2 侧桥(铸/销包装资产)。NeoHub.SharedBridge 的对应件。
- **`L2MessageContract`** — L2 侧消息 inbox/outbox。NeoHub.MessageRouter 的对应件。
- **`L2BatchInfoContract`** — 在 L2 自身上记录按批次的元信息(游标 / 最新承诺)。
- **`L2FeeContract`** — L2 gas 费用配置(基础 / 优先 / op-cost map)。
- **`L2PaymasterContract`** — 可选 gas 代付 —— 第三方为白名单 tx 付费。
- **`L2SystemConfigContract`** — 选定 chainConfig 字段在 L2 侧的镜像,供 L2 合约查询。
- **`L2NativeExternalBridgeContract`** — NeoHub.ExternalBridgeEscrow 在 L2 侧的对应件,服务外链资产。

---

## 4. L2 插件(8)

位于 `src/Neo.Plugins.L2*`。由 neo-cli 加载;订阅 `Block.Committed`。

- **`Neo.Plugins.L2Batch`** — 订阅 `Blockchain.Committed`;经 `BatchSealer` 把 tx 封进 `BatchCommitment`。
- **`Neo.Plugins.L2Settlement`** — 接好证明者 + 结算客户端;把封好的批次提到 L1。
- **`Neo.Plugins.L2Bridge`** — 托管 `AssetRegistry` + `DepositProcessor` + `WithdrawalProcessor`。
- **`Neo.Plugins.L2DA`** — 按 `DAMode` 选 DA writer;支持 InMemory / NeoFsLike / CommitteeAttested / L1。
- **`Neo.Plugins.L2Prover`** — 为配置的 `ProofType` 托管 `IL2Prover`。SP1 证明守护进程连接。
- **`Neo.Plugins.L2Rpc`** — 10 个 RPC handler(按 `doc.md` §14.1)。`IL2RpcStore` 后备(内存或 RocksDB)。
- **`Neo.Plugins.L2Gateway`** — 经 `BinaryTreeAggregator` 做可选的 Phase-5 多 L2 聚合。
- **`Neo.Plugins.L2Metrics`** — `IL2Metrics` + `MetricsHttpServer` 的组合根(`/metrics` + `/healthz` + `/readyz`)。

---

## 5. 链下运营者

| 运营者                       | 干什么                                                          | 源码                                    |
|------------------------------|----------------------------------------------------------------|----------------------------------------|
| 排序器(Sequencer)            | dBFT 2.0 共识成员;产出 L2 区块                                | `Neo.L2.Sequencer/`                    |
| 批处理器(Batcher)            | 订阅 `Block.Committed`;封装批次;提交到 L1                    | `Neo.L2.Batch/` + `Neo.Plugins.L2Batch`|
| 证明守护进程                  | SP1 zkVM 证明 `execute_batch(payload)`                         | `bridge/neo-zkvm-host/`(Rust 二进制)|
| DA writer                    | 把批次负载发布到 NeoFS / L1 / 委员会                            | `Neo.L2.DA*` + `IDAWriter` 实现       |
| 外链 watcher                  | 中继外链 Locked 事件 → Neo escrow                              | `watchers/neo-bridge-watcher-*/`(Rust)|

---

## 6. CLI 工具(7)

位于 `tools/*`。

| 工具                         | 二进制                  | 角色                                                                  |
|------------------------------|-------------------------|------------------------------------------------------------------------|
| `Neo.Stack.Cli`              | `neo-stack`             | 12 个子命令:create-chain、init-l2、register-chain、scaffold-executor、new-l2、… |
| `Neo.Hub.Deploy`             | `neo-hub-deploy`        | NeoHub 部署的 plan/scaffold/verify(20 步有序 bundle)。                |
| `Neo.L2.Devnet`              | `neo-l2-devnet`         | 进程内端到端 demo 运行器。`--executor counter` 接入样例执行器。       |
| `Neo.L2.Explore`             | `neo-l2-explore`        | 终端区块浏览器 + 状态根连续性 audit。                                  |
| `Neo.L2.Faucet.Cli`          | `neo-l2-faucet`         | 生产级 drip,带速率限制 + RocksDB 持久化 journal。                    |
| `Neo.L2.Bridge.Cli`          | `neo-bridge`            | SharedBridge 调用 hex 的生产级 CLI。                                 |
| `Neo.External.Bridge.Cli`    | `neo-external-bridge`   | 外链桥委员会 keygen + 双侧部署计划。                                   |

加上位于 `target/release/neo-bridge-watcher-eth`(Rust,需 `--features live-rpc`)
的 watcher 守护进程二进制。

---

## 7. 线协议格式速查

详情见 [`architecture-wire-formats.md`](./architecture-wire-formats.md)。

| 线协议格式                       | 大小                  | 跨越路径                                                  |
|----------------------------------|-----------------------|----------------------------------------------------------|
| `L2BatchCommitment`              | 321 + N 字节          | 批处理器 → SettlementManager                              |
| `PublicInputs`                   | 332 字节(定长)      | 证明者 → 验证器(在证明里被承诺)                       |
| `L2ChainConfig`                  | 91 字节(定长)       | `register-chain` → `ChainRegistry`                       |
| `ExternalCrossChainMessage`      | 102 + N 字节          | 外链 → Watcher → ExternalBridgeEscrow                    |
| `DepositPayload`                 | 44 + amountLen 字节   | NeoHub.SharedBridge → L2BridgeContract                   |
| `CrossChainMessage`              | (`MessageHasher`)     | L2 sender → NeoHub.MessageRouter → L2 receiver           |
| `WithdrawalRecord`               | -                     | L2BridgeContract → SharedBridge(在批次 withdrawalRoot 中)|
| `MerkleProofSerializer`          | -                     | 用户领取 → SharedBridge.VerifyWithdrawalLeafWithProof    |
| `MultisigProofPayload`           | -                     | Stage-0 证明者 → VerifierRegistry                        |
| `RiscVProofPayload`              | -                     | Phase-4 SP1 zkVM 证明者 → VerifierRegistry               |
| `OptimisticProofPayload`         | -                     | Stage-1 挑战二分 → OptimisticChallenge                   |
| `FraudProofPayload`              | -                     | 挑战胜方 → fraud verifier                                |

---

## 8. 每个术语首次定义在哪里

要更深入地理解某术语,顺着以下指针:

| 术语                   | 首次定义于                                                                                 |
|------------------------|--------------------------------------------------------------------------------------------|
| 4 层系统                | [architecture-l2-lifecycle.md §1](./architecture-l2-lifecycle.md#1-system-at-a-glance)     |
| §16.2 维度              | [architecture-l2-lifecycle.md §3](./architecture-l2-lifecycle.md#3-anatomy-of-an-l2-chain) |
| 三阶段准入              | [architecture-l2-lifecycle.md §4](./architecture-l2-lifecycle.md#4-creation-from-zero-to-registered) |
| 规范字节                | [architecture-wire-formats.md §1](./architecture-wire-formats.md#1-why-canonical-wire-formats) |
| 跨层验证链              | [architecture-trust-boundaries.md §3](./architecture-trust-boundaries.md#3-cross-tier-verification-chain) |
| 纵深防御                | [architecture-trust-boundaries.md §4](./architecture-trust-boundaries.md#4-defense-in-depth-per-flow) |
| 信任最小化梯度          | [architecture-trust-boundaries.md §6](./architecture-trust-boundaries.md#6-the-trust-minimization-gradient) |
| 外链命名空间前缀         | [architecture-wire-formats.md §5](./architecture-wire-formats.md#5-externalcrosschainmessage--external-bridge-102--n-bytes) |
| 委员会模型(外链)       | [external-bridge-roadmap.md](./external-bridge-roadmap.md)                                |
| 按链确认 buffer          | [external-bridge-evm-chains.md](./external-bridge-evm-chains.md)                          |

---

## 另请参阅

- [`architecture-atlas.md`](./architecture-atlas.md) —— 4 个架构章的索引,按角色给阅读顺序。
- [`architecture-l2-lifecycle.md`](./architecture-l2-lifecycle.md) —— 系统流。
- [`architecture-wire-formats.md`](./architecture-wire-formats.md) —— 规范字节布局。
- [`architecture-trust-boundaries.md`](./architecture-trust-boundaries.md) —— 信任模型。
- [`architecture-walkthrough.md`](./architecture-walkthrough.md) —— 按 tx 的叙事导览。
- [`tech-stack-coverage.md`](./tech-stack-coverage.md) —— 引入 vs 自实现。
