# ZKsync 弹性链 ↔ neo4 组件映射

Neo Elastic Network 借鉴了 ZKsync Elastic Chain(原名 "Hyperchains")的
*共享桥 + 链注册表 + 证明聚合* 模式。本文将每个 ZKsync 组件映射到其
neo4 对应物,标记两者有意分歧的地方,并跟踪 neo4 仍需补齐的差距。

映射基于 ZKsync 截至 2026 Q1 的 v29 era-contracts 发布。

最新的 2026-05-18 官方文档复核与生产完备结论见
[`docs/audit/zksync-elastic-chain-validation-2026-05-18.md`](../audit/zksync-elastic-chain-validation-2026-05-18.md)。

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
| **`ValidatorTimelock`** —— commit→execute 延迟 | `NeoHub.OptimisticChallenge` + `SettlementManager.StatusChallengeable` | 对等(不同机制) |
| **治理 / `ChainAdmin` / `PermanentRestriction` / `AccessControlRestriction`** | `NeoHub.GovernanceController`(含 `SetImmutableFlag` 永久限制机制) | 对等 |
| **`TransactionFilterer`**(每链 L1→L2 过滤钩子) | `MessageRouter.SetL1TxFilter` + `NeoHub.L1TxFilter` | 对等于 L1→L2 入队过滤；L2 mempool 过滤保留为运维策略 |
| **`L2AdminFactory` / 每链 `ChainAdmin`** | 缺 —— 链管理在 Hub 侧 `ChainRegistry.L2ChainConfig` 的 `operatorManager` | 有意分歧 |
| **`BridgedStandardERC20`** —— 规范 L2 代币 | Neo Core 原生 `BridgedNep17Contract` | 对等于规范桥接代币层 |
| **Boojum / Plonk 验证器合约** | `NeoHub.{MpcCommittee,Governance,RestrictedExecution,ExternalBridgeStub}*Verifier` + 经 `VerifierRegistry` 可插拔 | 对等 |
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
  大小限制而存在。NeoVM2/RISC-V 无 24KB 上限;NeoHub 在 23 个合约间按职责切分等效。
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
