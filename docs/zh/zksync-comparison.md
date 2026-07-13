# ZKsync 弹性链 ↔ neo4 组件映射

Neo Elastic Network 借鉴了 ZKsync Elastic Chain(原名 "Hyperchains")的
*共享桥 + 链注册表 + 证明聚合* 模式。本文将每个 ZKsync 组件映射到其
neo4 对应物,标记两者有意分歧的地方,并跟踪 neo4 仍需补齐的差距。

映射基于 ZKsync 截至 2026 Q1 的 v29 era-contracts 发布。

最新的 2026-05-18 官方文档复核与生产完备结论见
[`docs/audit/zksync-elastic-chain-validation-2026-05-18.md`](../audit/zksync-elastic-chain-validation-2026-05-18.md)。

---

## Neo-native 1:1 复刻策略

在本仓库中，“与 ZKsync Elastic Chain 1:1” 的含义是:保留**组件职责**、
**安全不变量**、**运维流程**和**面向用户的合约语义**，同时替换那些与
Ethereum / EVM / EraVM 强绑定的底层实现。它**不是**逐字复制 Solidity
字节码、EraVM 系统合约、Boojum 电路布局或 Ethereum gas / account 语义。

ZKsync 官方 Gateway 文档将 Gateway 定义为可选的共享证明聚合层:链仍锚定在
Ethereum，资产仍锁在 Ethereum，Gateway 不成为通用托管层或执行层。Neo 等价实现
保持这一不变量，但把根层从 Ethereum 换成 Neo L1:资产锁在 NeoHub，global root
由 NeoHub 合约锚定，Gateway 仍只是中间件。
参考文档:[Gateway overview](https://docs.zksync.io/zksync-protocol/gateway)、
[Gateway features](https://docs.zksync.io/zksync-protocol/gateway/features)、
[shared bridges](https://docs.zksync.io/zksync-protocol/contracts/l1-contracts/shared-bridges)。

| ZKsync Elastic Chain 不变量 | Neo-native 替代 | 验收门槛 |
|---|---|---|
| Ethereum 是根信任层和最终验证者 | Neo L1 / NeoHub 是根信任层和最终验证者 | 批次根、消息根、提款、紧急退出均由 NeoHub 合约锚定；Gateway 永不成为资产托管层 |
| Bridgehub 是链注册表 + L1 入口 | `NeoHub.ChainRegistry` + `SettlementManager` + `MessageRouter` | 链注册、结算、L1→L2 消息、global root 均可从 NeoHub 到达 |
| Chain Type Manager 为同类链共享 verifier / 升级策略 | `VerifierRegistry` + `GovernanceController` + `L2ChainConfig` 中的 proof / DA 模式 | 同一 Neo-native 链类型共享 verifier 策略、分阶段升级、immutable flag 和 DA gate |
| Shared Bridge 提供生态统一流动性 | `NeoHub.SharedBridge` + `TokenRegistry` + L2 原生 `L2BridgeContract` / `BridgedNep17Contract` | 每种资产只有一个规范桥接表示；通过 withdrawal/message nullifier 防重放 |
| Gateway 是可选证明聚合中间件 | `Neo.Plugins.L2Gateway` + `MessageRouter.PublishGlobalRoot` | 链可走直连 NeoHub 结算或 Gateway 聚合路径，但资产托管不会迁移到 Gateway |
| Rollup / validium DA 选择必须显式 | `DARegistry` + `DAValidator` + NeoFS / L1 / DAC writer | 批次 finalize 检查当前 DA 策略；NeoFS 是默认 Neo-native 外部 DA 层 |
| Forced inclusion 防 sequencer 审查 | `NeoHub.ForcedInclusion` + `SequencerBond` + `ChainRegistry` pauser 接线 | 过期强制交易可触发至多一次 report、罚没和链暂停 |
| L2 系统合约暴露桥、消息、费用、AA、interop 原语 | `external/neo` 下的 Neo core 原生 L2 合约 | 在不引入 EraVM bytecode/deployer/nonce-holder 机制的情况下提供等价原语 |
| ZK 有效性证明是无信任结算目标 | RISC-V/NeoVM 执行 receipt + SP1/Neo zkVM proof boundary + `ContractZkVerifier` 路由 | 生产 ZK 链必须注册真实 verifier，并永久关闭 `envelope-only`；devnet shortcut 必须显式 |
| `zkstack` / `zksync-cli` 让运维流程可复现 | `neo-stack`、`neo-hub-deploy`、SDK、devnet runner | 运维方得到确定性 config bytes、部署计划、post-deploy 接线检查、冒烟测试和 wallet-owned signing |

### 直接复刻边界

以下 ZKsync 思路应在 Neo 允许的范围内尽量直接复刻:

- shared bridge 统一流动性模型和 replay/nullifier 纪律；
- Bridgehub / CTM / Gateway 拓扑；
- 显式 rollup-vs-validium DA 选择；
- 面向 L2↔L2 interop 的 global message-root 聚合；
- 分阶段治理、永久限制和紧急路径分离；
- 带 sequencer 问责的 forced inclusion；
- 运维 CLI 体验和可复现部署计划。

以下必须保持 Neo-native 替代，而不是直接复制:

- **EraVM / EVM 系统合约** → NeoVM2/RISC-V runtime + Neo core 原生 L2 合约；
- **Boojum / Airbender 电路栈** → Neo 执行证明 adapter（当前是 vendored Neo zkVM
  路径上的 SP1，N4 目标是 RISC-V 执行 receipt）；
- **Ethereum ETH/ERC20 记账** → GAS / NEO / NEP-17 记账与 UInt160 地址；
- **以 calldata 为中心的 DA** → NeoFS / L1 / DAC DA 模式；
- **Solidity Diamond/facet 升级机制** → 可部署 NeoHub 合约 +
  `GovernanceController` 分阶段升级窗口。

这就是“ZKsync Elastic Chain, but for Neo”的架构合约:协议边界最大化对齐，
只有在 Ethereum-specific 机制放到 Neo 上会错误或低效时，才做有意分歧。

---

## L1 信任模型（请先阅读）

ZKsync 弹性链最本质的特性是:L1 合约会为每个结算批次验证一份**有效性证明**
(Boojum/Plonk SNARK),从而 L1 无需信任 sequencer。neo4 在**拓扑结构**上与弹性链
对齐——共享桥、链注册表、聚合消息根、DA 闸、强制包含、逃生舱——但它**尚未在仓库内
提供链上有效性证明验证器**。今天一个 L1 批次结算实际信任什么,取决于该链配置的
`ProofType`:

- **`ProofType.Multisig`(Stage 0)**——L1 信任一个已注册的 secp256r1 委员会
  (`MpcCommitteeVerifier`)。签名校验是真实且完全链上的;安全性来自对委员会的诚实
  多数假设。
- **`ProofType.Optimistic`(Stage 1)**——有效性被*假定*成立,L1 依赖一个欺诈证明
  挑战窗口(`OptimisticChallenge`)。这是相对 ZKsync(纯有效性 rollup)的**乐观 rollup
  分歧**。v1/v2/v3 仍是治理仲裁的结构性证据；独立 v4 profile 会绑定 committed batch
  并执行一笔 existing-key Counter Increment。通用 NeoVM 与多交易 fraud proof fail closed。
- **`ProofType.Zk`(Stage 2)**——`ContractZkVerifier` 校验规范 batch/proof 信封，并把
  SP1 证明路由到仓库内不可变的 `Sp1Groth16Verifier`；后者通过 Neo Core 原生 BN254
  interop 执行完整的固定 SP1 Groth16 pairing 方程。生产计划注册准确 program VK，并在
  暴露 ZK settlement 路由前永久关闭 SP1 `envelope-only`。显式 envelope-only 仅保留给
  私有 devnet 和尚未接入终端验证器的其他证明系统。

简言之：neo4 现在同时交付弹性链式 proof routing 拓扑和 SP1 Groth16 链上有效性验证器。
这不是 ZKsync Boojum/Plonk 字节码复刻，而是 Neo SP1/RISC-V 路线对应的无信任结算边界。下表中
标注"对等"的行除非另有说明,均指结构 / 拓扑层面的对等;与证明验证相关的行已明确标注为
**部分**。

---

## 组件映射

| ZKsync 组件 | neo4 对应物 | 状态 |
|---|---|---|
| **`Bridgehub.sol`** —— chainId → ChainTypeManager 注册表,L1→L2 入口 | `NeoHub.ChainRegistry` + `NeoHub.MessageRouter` | 对等(拆为两个合约) |
| **`ChainTypeManager`**(原 STM)—— 链工厂 + 升级编排 | `NeoHub.VerifierRegistry` + `NeoHub.GovernanceController.PrefixApprovedVerifier` | 部分 —— 无每链工厂合约,无 DiamondProxy 模式 |
| **`SharedBridge`** —— L1 托管(legacy) | `NeoHub.SharedBridge` | 对等 |
| **`L1AssetRouter` + `L2AssetRouter`**(v24+)—— 链无关的资产路由 | 缺,由单一 `SharedBridge` 承担两个角色 | 有意分歧(单一 Hub) |
| **`L1/L2NativeTokenVault`** —— 资产 ID 派生、桥接代币部署 | `NeoHub.TokenRegistry` + Neo Core 原生 `L2BridgeContract` + 原生 `BridgedNep17Contract` | N4 设计下对等 —— L2 代币记账在 core native 层,不是运维方后期部署模板；平台资产目录包含 NEO 0→8、GAS 8→8、USDT/USDC 6→6、BTC 8→8,并在所有 L2 上保持同一 L2 asset id |
| **`L1Nullifier`** —— 提现重放保护 | `SharedBridge.PrefixWithdrawalConsumed` + `MessageRouter.PrefixConsumed` | 对等 |
| **`MessageRoot.sol`** —— 跨链聚合的 L2→L1 根 | `MessageRouter.PublishGlobalRoot`(0x05 槽)+ 链下 `Neo.Plugins.L2Gateway.BinaryTreeAggregator` | 对等 |
| **`ChainAssetHandler`** —— 每资产路由规则 | 缺 | 有意分歧(单一信任模型) |
| **`ValidatorTimelock`** —— 对**已证明**批次的 commit→execute 延迟 | `NeoHub.OptimisticChallenge` + `SettlementManager.StatusChallengeable` | **不同的安全模型,非对等** —— ZKsync 延迟的是有效性证明已在 L1 验证通过的批次的执行;neo4 的窗口则是一个*乐观*欺诈证明博弈,在无人挑战时假定有效(即 N4 的 `ProofType.Optimistic`,Stage 1)。详见上文 **L1 信任模型**。 |
| **治理 / `ChainAdmin` / `PermanentRestriction` / `AccessControlRestriction`** | `NeoHub.GovernanceController`(含 `SetImmutableFlag` 永久限制机制) | 对等 |
| **`TransactionFilterer`**(每链 L1→L2 过滤钩子) | `MessageRouter.SetL1TxFilter` + `NeoHub.L1TxFilter` | 对等于 L1→L2 入队过滤；L2 mempool 过滤保留为运维策略 |
| **`L2AdminFactory` / 每链 `ChainAdmin`** | 缺 —— 链管理在 Hub 侧 `ChainRegistry.L2ChainConfig` 的 `operatorManager` | 有意分歧 |
| **`BridgedStandardERC20`** —— 规范 L2 代币 | Neo Core 原生 `BridgedNep17Contract` | 对等于规范桥接代币层 |
| **Boojum / Plonk 验证器合约** —— 链上有效性证明数学 | `NeoHub.ContractZkVerifier` 将 `ProofType.Zk` 路由到不可变的 `NeoHub.Sp1Groth16Verifier`；后者固定 SP1 wrapper VK 并通过 Neo Core 执行 Groth16/BN254 数学 | 安全边界等价、证明栈不同 —— SP1 Groth16 替代 Boojum/Plonk；生产永久关闭 SP1 `envelope-only`。详见上文 **L1 信任模型**。 |
| **`CalldataDA` / `ValidiumL1DAValidator` / `RollupDAManager` / `RelayedSLDAValidator`** | `NeoHub.DARegistry` + `NeoHub.DAValidator` + `Neo.Plugins.L2DA` writer | 部分 —— DAC attestation gate 已有；更丰富的 NeoFS / 外部包含证明 adapter 属于运维扩展 |
| **`BytecodesSupplier` / `*Upgrade` 系列 / `UpgradeStageValidator`** | `GovernanceController` 提案管线，含通知、执行、冷却窗口 | 对等于分阶段时序；NeoVM 不需要 bytecode supplier |
| **L2 `Bootloader`** | 缺 —— NeoVM2/RISC-V runtime 提供原生派发 | 有意分歧 |
| **L2 `ContractDeployer` / `KnownCodesStorage` / `AccountCodeStorage`** | 缺 —— Neo 的 `ContractManagement` 原生承担 | 有意分歧 |
| **L2 `SystemContext`**(chainId、baseFee、blockhash) | Neo Core 原生 `L2BatchInfoContract` + `L2SystemConfigContract` | 对等 |
| **L2 `L2BaseToken`**(ETH 余额) | NEP-17 GAS 原生 | 有意分歧 |
| **L2 `L1Messenger`**(outbox) | Neo Core 原生 `L2MessageContract` | 对等 |
| **L2 `NonceHolder`** | 缺 —— Neo 的每签名者 nonce 在原生层隐式存在 | 有意分歧 |
| **L2 `DefaultAccount` + `IAccount` AA** | Neo Core 原生 `L2AccountAbstraction` | 部分 —— 有 validate / execute / paymaster hook；不是 EraVM ABI 逐字节兼容 |
| **L2 `TestnetPaymaster` + `IPaymasterFlow`** | Neo Core 原生 `L2PaymasterContract`(仅充值模式) | 部分 —— 无 `approvalBased` 流选择器 |
| **L2 `L2InteropRootStorage` / `L2MessageVerification`(v29)** | Neo Core 原生 `L2InteropVerifier` 镜像 global roots 并本地验证 Merkle inclusion | 辅助合约层面对等 |
| **ZK Gateway**(结算层证明聚合器) | `Neo.Plugins.L2Gateway`(链下)+ 链上 `MessageRouter.PublishGlobalRoot` | 对等 |
| **强制包含 / 优先队列** | `NeoHub.ForcedInclusion` + `Neo.L2.ForcedInclusion` | 对等 |
| **Sequencer 质押 / 罚没** | `NeoHub.SequencerRegistry` + `NeoHub.SequencerBond` | 对等 |
| **紧急安全升级** | `NeoHub.EmergencyManager` | 对等 |
| **审计模块(`ChainAuditor` 对应)** | `Neo.L2.Audit`(6 项不变量检查) | 对等 |
| **Foundry 测试 + 不变量测试 + Hardhat 规约** | xUnit `tests/Neo.*.UnitTests` + `tests/Neo.L2.IntegrationTests`(E2E 系列);`external/foreign-contracts/eth/` 的 Foundry 测试 | 对等 |
| **`zksync-cli` + 多语言 SDK** | `Neo.Stack.Cli` + 6 个其他 CLI;`sdk/typescript`、`sdk/rust`、`src/Neo.L2.Sdk` | 部分 —— 无 Go / Python SDK |
| **`code.zksync.io` 教程 + 样例 dApp(~15+)** | `samples/contracts/{CrossChainGreeter,WithdrawalDemo}` + `samples/executors/CounterChainExecutor` | 部分 —— 仅 3 个样例模块 |

---

## 本轮已关闭的差距(Phase-5 + 治理成熟度)

### `MessageRouter.PublishGlobalRoot` / `GetGlobalRoot`

0x05 存储槽之前已保留但未使用。ZKsync 的 `MessageRoot.sol` 在 L1 上提交
聚合的消息根,任何 L2 都可以通过对单一 L1 锚定根的 Merkle 包含证明来证明
对等链的消息。

`MessageRouter.PublishGlobalRoot(ulong batchEpoch, UInt256 globalRoot)`
现已写入该根,结算管理器见证门控,每 epoch 仅可发布一次的重放保护,以及
非零根强制。发布成功时发出 `OnGlobalRootPublished(epoch, root)` 事件。

链下 `Neo.Plugins.L2Gateway.BinaryTreeAggregator` 继续执行实际的 log(N)
聚合;新的链上入口让结果在公链可审计,并支持直接对 L1 进行跨 L2 消息校验。

### `GovernanceController.SetImmutableFlag` / `IsImmutable`

ZKsync 的 `PermanentRestriction` 机制:通过一个永久写保护的 flag,锁住
某些不变量。例:"此链一旦发布,永远不可切换 DAMode" 或 "此验证器哈希
被永久退役"。

两个入口:

- `SetImmutableFlag(byte flagId)` —— 仅 owner 快路径,幂等。存储只写,不存在
  `ClearImmutableFlag` 入口。
- `SetImmutableFlagViaProposal(byte flagId, ulong proposalId)` —— 委员会
  否决路径。需要 `IsApprovedAndTimelocked(proposalId)`。按 proposalId
  通过新 `PrefixConsumedSetImmutable = 0x0E` 槽提供重放保护。

`IsImmutable(byte flagId)` 是 `[Safe]` reader。`OnImmutableFlagSet(flagId)`
仅在首次设置时发出(幂等重置静默)。

---

## 仍待解决的差距(跟踪以备未来迭代)

### 差距 1 —— 样例覆盖偏薄

ZKsync 的 `code.zksync.io` 出货 ~15+ 教程(多签 AA、paymaster ERC20、
gated NFT mint、L1→L2 deposit)。neo4 总共 3 个样例模块。

**建议**:至少加 `Sample.Erc20PaymasterClient`、`Sample.MultisigAccount`、
`Sample.GatedMint`、`Sample.CrossChainSwap`。

### 差距 2 —— 无 Python / Go SDK

`sdk/rust` 与 `sdk/typescript` 镜像了 10 个 RPC 方法,但 ZKsync 出货
`zksync2-go` 与 `zksync2-python`(索引器 / 交易所需求高)。

**建议**:由规范的 `L2RpcClient.cs` 表面生成社区级 SDK。

---

## 有意分歧(非差距)

这些在 ZKsync 中存在是 EVM/EraVM 的特殊性所致。NeoVM2/RISC-V 的设计要么让它们
无意义,要么提供了原生等价物:

- **EraVM L2 系统合约**(`Bootloader`、`ContractDeployer`、`KnownCodesStorage`、
  `AccountCodeStorage`、`NonceHolder`、`MsgValueSimulator`、`Compressor`、
  `EvmEmulator.yul`、`EvmGasManager.yul`、`EventWriter.yul`、`EcAdd.yul`/
  `EcPairing.yul`/`Modexp.yul`/`P256Verify.yul` 预编译)—— NeoVM2/RISC-V 提供
  `ContractManagement`、原生 NEP-17 GAS、原生密码学、隐式签名者 nonce。
- **Diamond proxy + facet 模式**(`DiamondProxy.sol`、`Admin.sol`、
  `Executor.sol`、`Getters.sol`、`Mailbox.sol`)—— 为绕过以太坊 24KB 合约
  大小限制而存在。NeoVM2/RISC-V 无 24KB 上限;NeoHub 在 24 个生产合约间按职责切分等效。
- **`CTMDeploymentTracker` + `ChainAssetHandler`** —— ZKsync 用来支持
  *竞争性* 链类型与第三方资产路由器。neo4 只有一个规范 Hub。
- **`L2BaseToken` + `L2WrappedBaseToken` / `L2WrappedBaseTokenStore`** ——
  解决 ETH/WETH 非 NEP-17 的解包问题。GAS 在 Neo 中已是 NEP-17。
- **`GasBoundCaller`** —— EraVM gas 语义与 EVM 不同。NeoVM2/RISC-V gas 按指令
  确定性收取。
- **`zksolc` / `zkvyper` 编译器** —— neo4 复用上游 `neo-devpack-dotnet` +
  `Neo.SmartContract.Framework`;无需 EVM-to-zkVM 转译器。
- **每链 `ChainAdmin` 合约**(`ChainAdmin.sol`、`ChainAdminOwnable.sol`、
  `L2AdminFactory.sol`)—— neo4 的每链管理面就是 `ChainRegistry.L2ChainConfig`
  中的 `operatorManager` + sequencer / verifier 记录。
- **`EvmEmulator` / `EvmPredeploysManager`** —— ZKsync 2025 年加入以便在
  EraVM 中运行未改的 EVM 字节码以便迁移。neo4 的受众是 Neo 开发者。
- **`Airbender` / Boojum-specific verifier crate** —— neo4 已经 vendor
  `external/neo-zkvm`(纯 Rust 实现 Neo VM)+ SP1 prover。采用 Airbender
  wire-format 需要重构 prover 插件链,而目前没有拉动力。

---

## 参见

- [`ARCHITECTURE.md`](../../ARCHITECTURE.md) —— neo4 自身分层架构
- [`architecture-l1-vs-l2.md`](architecture-l1-vs-l2.md) —— 按职责的 L1/L2 拆分
- [`spec-gap-plan.md`](spec-gap-plan.md) —— 对 `doc.md` 的完整差距跟踪
- [`TASKS.md`](../../TASKS.md) —— 按仓的可执行清单(core / 本仓 / cross-repo)
