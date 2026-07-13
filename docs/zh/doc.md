下面是一份**完整系统架构设计**，目标是以 `github.com/r3e-network/neo` 的双 core 分支作为基础：`r3e/neo-n3-core` 跟踪 `github.com/neo-project/neo` 的 `master-n3` 并作为 **Neo N3 L1 core** 维护分支，`r3e/neo-n4-core` 跟踪上游 `master` 并作为 **Neo 4 L2 chain 的执行内核**。L2 分支承载 N4 所需的 native contract、ChainMode 和执行内核改动；同时借鉴 ZKsync Elastic Chain 的 shared bridge、chain registry、proof aggregation、native interoperability 思路，构建一个类似 **Neo Elastic Network** 的多 L2 网络。

我先给结论：

> **Neo 4 core 可以作为 L2 execution core，但不能单独构成完整 L2。完整系统必须额外加入：NeoHub、shared bridge、settlement contract、state root/proof verifier、DA layer、batcher、prover、message router、gateway、withdrawal/escape hatch。**

---

# 0. 设计目标

我们设计的不是一条普通 sidechain，而是一个完整的 Neo L2 网络：

```text
Neo N3 / Neo 4 L1
    ↓
NeoHub: L2 注册、共享桥、状态结算、资产根、消息路由
    ↓
Neo Gateway: 多 L2 proof aggregation、跨 L2 message root
    ↓
多条 Neo 4 L2 Chains
    - Neo RWA Chain
    - Neo Stablecoin Chain
    - Neo DEX Chain
    - Neo Game Chain
    - Neo Enterprise Chain
    - Neo Privacy Chain
```

Neo 4 core 的角色是：

```text
Neo 4 core = L2 执行层 + 节点基础 + NeoVM2/RISC-V runtime + dBFT sequencer base
```

Neo N3 / Neo 4 L1 的角色是：

```text
L1 = settlement root + canonical asset root + governance root + final verification root
```

这个设计的核心原则是：

> **L2 可以很多条，但资产、状态验证、跨链消息和治理标准必须统一。**

这正是 ZKsync Elastic Chain 最值得学的地方。ZKsync 的 shared bridge 文档明确说，ZKsync chains 需要共同的 trust and verification standards，并由一组 L1 contracts 统一管理所有 ZKsync chains 的 proof verification；ZKsync Gateway 则作为可选 shared proof aggregation layer，用于增强 interoperability、proof aggregation 和成本效率。([ZKsync Docs][1])

---

# 1. 总体架构

```text
┌─────────────────────────────────────────────────────────────┐
│                    Neo N3 / Neo 4 L1                        │
│                                                             │
│  ┌───────────────────────────────────────────────────────┐  │
│  │ NeoHub                                                │  │
│  │ - L2 chain registry                                  │  │
│  │ - shared bridge                                      │  │
│  │ - canonical GAS / NEO / USDT / USDC / BTC / NEP-17   │  │
│  │ - L2 batch settlement                                │  │
│  │ - state root registry                                │  │
│  │ - proof verifier registry                            │  │
│  │ - L1 <-> L2 message queue                            │  │
│  │ - L2 <-> L2 message router                           │  │
│  │ - emergency pause / escape hatch                     │  │
│  └───────────────────────────────────────────────────────┘  │
│                                                             │
│  ┌───────────────────────────────────────────────────────┐  │
│  │ Neo Governance / Council / NEO Holder Referendum      │  │
│  │ - L2 admission policy                                │  │
│  │ - verifier upgrade                                   │  │
│  │ - bridge emergency control                           │  │
│  │ - DA security level registry                         │  │
│  └───────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                         Neo Gateway                         │
│                                                             │
│ - Collect proofs from multiple Neo L2s                      │
│ - Aggregate proofs                                          │
│ - Maintain inter-L2 message root                            │
│ - Submit aggregated settlement batch to NeoHub              │
│ - Optional: fast finality layer for L2-to-L2 messages        │
└─────────────────────────────────────────────────────────────┘
                              │
         ┌────────────────────┼────────────────────┐
         ▼                    ▼                    ▼
┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐
│ Neo RWA L2      │  │ Neo DEX L2      │  │ Neo Game L2     │
│                 │  │                 │  │                 │
│ Neo 4 core      │  │ Neo 4 core      │  │ Neo 4 core      │
│ NeoVM2/RISC-V   │  │ NeoVM2/RISC-V   │  │ NeoVM2/RISC-V   │
│ dBFT sequencer  │  │ dBFT sequencer  │  │ dBFT sequencer  │
│ Batcher         │  │ Batcher         │  │ Batcher         │
│ Prover adapter  │  │ Prover adapter  │  │ Prover adapter  │
│ DA adapter      │  │ DA adapter      │  │ DA adapter      │
└─────────────────┘  └─────────────────┘  └─────────────────┘
```

---

# 2. 基础事实与设计依据

当前 `neo-project/neo` master README 把该仓库描述为 Neo blockchain protocol 的 C# implementation，并列出核心目录：Ledger、Network、Persistence、Plugins、SmartContract、Wallets、Neo.CLI、Neo.VM 和 plugins 等；这些模块足够作为一条 Neo-compatible chain 的节点与执行基础。([GitHub][2])

Neo 4 roadmap 里已经把 **NeoVM 2** 定义为下一代 VM，并说明它要 fully compatible with the RISC-V instruction set、支持 fine-grained gas metering、降低成本和提升性能；同一 roadmap 还提到 Account Abstraction、ZK-based account recovery、zk-KYC、zk-Oracle，以及未来探索 Layer-2 governance。([GitHub][3])

Neo 的 dBFT2.0 提供 single block finality：文档说明新区块生成需要至少 M 个 validators 的 Commit，validator 在广播 Commit 后不会改变 view，因此新块在给定高度可获得最终性。这个特性非常适合 L2 本地排序与快速确认。([Neo文档][4])

Neo 原生 token 模型也适合做 L2 网络：NEO 是 governing token，GAS 是 network resource / fee token，NEP-17 则通过智能合约发行和管理。Neo L2 的资产设计应该围绕这个 canonical token model，而不是每条 L2 自己发行孤立 gas token。([NEO Developer Resource][5])

---

# 3. 系统分层设计

## 3.1 L1 Settlement Layer：Neo N3 / Neo 4 L1

L1 不是只做桥，而是整个 L2 网络的安全根。

```text
L1 responsibilities:
1. 注册所有 Neo L2 chain
2. 锁定 canonical GAS / NEO / USDT / USDC / BTC / NEP-17 资产
3. 接收 L2 batch commitment
4. 验证 L2 validity proof 或 optimistic challenge result
5. 记录 canonical L2 state root
6. 处理 L1 -> L2 deposit queue
7. 处理 L2 -> L1 withdrawal
8. 路由 L2 -> L2 messages
9. 管理 verifier / bridge / chain config upgrade
10. 提供 emergency pause 和 escape hatch
```

L1 上应该有一组核心合约，统称 **NeoHub**。

---

## 3.2 NeoHub：L1 上的核心合约组

NeoHub 是整个系统最重要的组件，相当于 ZKsync 的 BridgeHub + SharedBridge + Verifier Registry + Message Router 的组合。

```text
NeoHub
├── ChainRegistry
├── SharedBridge
├── SettlementManager
├── VerifierRegistry
├── ContractZkVerifier
├── MessageRouter
├── TokenRegistry
├── DARegistry
├── GovernanceController
└── EmergencyManager
```

### ChainRegistry

负责注册所有 L2。

```csharp
struct L2ChainConfig {
    uint32 chainId;
    UInt160 operatorManager;
    UInt160 verifier;
    UInt160 bridgeAdapter;
    UInt160 messageAdapter;
    byte securityLevel;       // 0 sidechain, 1 settled, 2 optimistic, 3 validity
    byte daMode;              // 0 L1 DA, 1 NeoFS DA, 2 external DA, 3 DAC
    bool gatewayEnabled;
    bool permissionlessExit;
    bool active;
}
```

核心方法：

```text
registerChain(config)
updateChainConfig(chainId, newConfig)
pauseChain(chainId)
resumeChain(chainId)
getChainConfig(chainId)
```

### SharedBridge

负责 canonical assets。

```text
SharedBridge responsibilities:
1. L1 GAS / NEO / USDT / USDC / BTC / NEP-17 escrow
2. L1 -> L2 deposit
3. L2 -> L1 withdrawal finalization
4. L2 asset mapping registry
5. canonical wrapped asset mint/burn rules
```

重要原则：

```text
Neo N3 GAS = canonical GAS
Neo L2 GAS = bridged GAS representation
Neo N3 NEO = indivisible canonical NEO (decimals = 0)
Neo L2 NEO = built-in decimal bridged NEO representation (decimals = 8)
Neo L2 USDT / USDC = built-in platform stablecoin representations (decimals = 6)
Neo L2 BTC = built-in platform BTC representation (decimals = 8)
```

也就是说，L2 上的 GAS 不应该无约束发行。L2 可以把 bridged GAS 作为 fee token，但 supply 必须由 L1 SharedBridge 约束。
同理，L1 NEO 不改变不可分割属性；每条 L2 内置的 NEO 是由桥映射出来的 decimal 表示，充值按 `10^8` 放大，提款必须能按 `10^8` 精确缩回 L1 整数 NEO。
USDT、USDC、BTC 作为全平台目录资产处理；每条 L2 使用同一套 L2 asset id 与 decimals，方便 L1↔L2 与 L2↔L2 转移在用户和应用层保持无感。

### SettlementManager

负责 batch settlement。

```csharp
struct L2BatchCommitment {
    uint32 chainId;
    ulong batchNumber;
    ulong firstBlock;
    ulong lastBlock;

    UInt256 preStateRoot;
    UInt256 postStateRoot;
    UInt256 txRoot;
    UInt256 receiptRoot;
    UInt256 withdrawalRoot;
    UInt256 l2ToL1MessageRoot;
    UInt256 l2ToL2MessageRoot;

    UInt256 daCommitment;
    UInt256 publicInputHash;

    byte proofType;       // 0 none, 1 multisig, 2 optimistic, 3 zk
    byte[] proof;
}
```

核心方法：

```text
submitBatch(batchCommitment)
verifyBatch(chainId, batchNumber)
finalizeBatch(chainId, batchNumber)
revertUnfinalizedBatch(chainId, batchNumber)
getCanonicalStateRoot(chainId)
```

### VerifierRegistry

支持不同阶段、不同 L2、不同 proof system。

```text
VerifierRegistry:
  - MultisigVerifier
  - OptimisticVerifier
  - ContractZkVerifier  # ProofType.Zk router -> L1 可部署验证器合约
  - AggregatedProofVerifier
  - FutureProofSystemVerifier
```

不要把 verifier 写死。Neo 4 L2 早期可能先用 multisig / optimistic，后续再升级到 RISC-V zk validity proof。
ZK 路径不应在普通合约中硬算证明系统数学：`ContractZkVerifier` 先校验 batch commitment、
RISC-V proof payload、verification-key id 和 publicInputHash 边界，再调用 L1 可部署验证器合约
的 `verifyZkProof(...)`。

### MessageRouter

负责 L1↔L2、L2↔L2 的消息根和消息消费。

```csharp
struct CrossChainMessage {
    uint32 sourceChainId;
    uint32 targetChainId;
    ulong nonce;
    UInt160 sender;
    UInt160 receiver;
    byte messageType;     // deposit, withdraw, call, event, governance
    byte[] payload;
    UInt256 messageHash;
}
```

核心方法：

```text
enqueueL1ToL2Message(targetChainId, receiver, payload)
publishL2ToL1Root(chainId, batchNumber, messageRoot)
consumeL2ToL1Message(message, merkleProof)   // L1 侧（MessageRouter.MarkConsumed）：仅在 SettlementManager 见证下标记 message 为已消费并做重放保护；Merkle proof 的校验由调用方在链下负责，L1 router 自身不验证 proof
publishL2ToL2Root(sourceChainId, batchNumber, messageRoot)
consumeL2ToL2Message(message, merkleProof)   // 同上：L1 router 只做去重，不做 proof 校验
```

L2 侧则不同：`L2InteropVerifier.ConsumeMessage` 会在链上验证 Merkle proof。换言之，当前实现中只有 L2 侧消费消息时才做链上 proof 校验；L1 侧的 `consumeL2ToL1Message` / `consumeL2ToL2Message` 仅做见证下的标记 + 重放保护，proof 校验留给链下调用方。

ZKsync Connect 的互操作思路可以直接借鉴：它通过智能合约和 Merkle proof 验证跨链交易/消息，支持链之间通信和交易。([ZKsync Docs][6])

---

# 4. Neo Gateway：证明聚合和跨 L2 中间层

Neo Gateway 是可选层，不应该替代 L1。它的定位是：

```text
Neo Gateway = proof aggregation layer + inter-L2 message root layer
```

对应 ZKsync Gateway 的设计：ZKsync Gateway 是可选 shared proof aggregation layer，Gateway 会把多条链的 proofs 聚合成一个 proof 后提交给 Ethereum final verification，从而减少 L1 proof verification 次数。([ZKsync Docs][7])

Neo Gateway 设计：

```text
Neo Gateway responsibilities:
1. 接收多个 Neo L2 的 batch proof
2. 验证每条 L2 的 local proof
3. 聚合成 aggregated proof
4. 生成 globalMessageRoot
5. 向 NeoHub 提交 aggregated settlement
6. 为 L2-to-L2 message 提供更快同步路径
```

架构：

```text
Neo L2-A proof ┐
Neo L2-B proof ├──> Neo Gateway Aggregator ──> AggregatedProof ──> NeoHub
Neo L2-C proof ┘
```

Gateway 不保管资产。资产仍锁在 NeoHub / SharedBridge。Gateway 只负责证明聚合和消息根聚合。

---

# 5. Neo 4 L2 Chain 内部架构

每条 L2 使用 `r3e-network/neo` 的 `r3e/neo-n4-core` 分支作为执行内核，该分支跟踪 `neo-project/neo` master，但必须新增 **L2 mode**。L1 core 相关改动单独进入 `r3e/neo-n3-core`，该分支跟踪 `neo-project/neo` master-n3。

```text
Neo 4 L2 Node
├── Neo 4 Core
│   ├── Ledger
│   ├── MemoryPool
│   ├── Blockchain
│   ├── Persistence
│   ├── SmartContract / ApplicationEngine
│   ├── Neo.VM / NeoVM2 / RISC-V
│   ├── Native Contracts
│   └── Wallet / Crypto
│
├── L2 Extensions
│   ├── Sequencer / dBFT Committee
│   ├── L1MessageProcessor
│   ├── Batcher
│   ├── StateRootGenerator
│   ├── DAWriter
│   ├── ProverAdapter
│   ├── SettlementSubmitter
│   ├── BridgeAdapter
│   ├── MessageAdapter
│   ├── ForcedInclusionHandler
│   └── L2 RPC Extensions
│
└── Plugins
    ├── RpcServer
    ├── DBFTPlugin
    ├── StateService
    ├── TokensTracker
    ├── L2BatchPlugin
    ├── L2ProofPlugin
    └── L2BridgePlugin
```

Neo-CLI 当前配置已经有 `Network`、`MillisecondsPerBlock`、`MaxTransactionsPerBlock`、`ValidatorsCount`、`StandbyCommittee` 等协议参数；插件体系也包括 DBFTPlugin、RpcServer、StateService、TokensTracker 等，这些可以作为 L2 mode 的基础扩展点。([Neo文档][8])

---

# 6. Neo 4 Core 需要新增的 L2 Mode

建议在 Neo 4 core 中引入：

```text
ChainMode:
  - L1Mode
  - SidechainMode
  - L2RollupMode
  - L2ValidiumMode
```

## 6.1 L1Mode

普通 Neo L1。

```text
- 正常 dBFT
- 正常治理
- 正常 GAS 生成
- 正常 native contracts
```

## 6.2 SidechainMode

快速启动 app-chain。

```text
- 独立 dBFT validator set
- 独立 state
- 可桥接到 NeoHub
- 不要求 L1 验证状态转换
```

适合 Phase 0。

## 6.3 L2RollupMode

真正 L2。

```text
- L2 本地 dBFT / sequencer 负责排序
- L2 batch 提交到 NeoHub
- L1 验证 proof 或 challenge
- withdrawal 只能基于 finalized batch
- DA 可以放在 L1 或 NeoFS
```

## 6.4 L2ValidiumMode

低成本 L2。

```text
- L2 执行和证明类似 rollup
- transaction data 不完全放在 L1
- DA 放 NeoFS / DAC / external DA
- NeoHub 记录 DA commitment
```

---

# 7. L2 节点角色设计

## 7.1 Sequencer / dBFT Committee

Neo 4 L2 可以用两种排序模式：

```text
Mode A: centralized sequencer
  - 简单
  - 适合早期测试网
  - 需要 forced inclusion 防审查

Mode B: dBFT sequencer committee
  - 更符合 Neo
  - one-block finality
  - 可用 GAS/NEO staking 或 governance 选出
```

我的建议是：**生产系统优先用 dBFT sequencer committee**。

原因是 Neo dBFT 有天然的 deterministic finality，L2 用户体验会很好：

```text
用户交易 -> L2 dBFT block finality -> 几秒确认
之后 batch -> NeoHub settlement -> canonical finality
```

需要区分两种 finality：

```text
L2 local finality:
  来自 L2 dBFT，用于用户快速确认

L1 settlement finality:
  来自 NeoHub accepted batch/proof，用于 withdrawal、canonical bridge、跨 L2 消息
```

---

## 7.2 Batcher

Batcher 把 L2 blocks 打包成 L1 settlement batch。

```text
Batcher input:
  - L2 blocks
  - transactions
  - receipts
  - storage writes
  - withdrawals
  - L2->L1 messages
  - L2->L2 messages
  - L1 deposit messages consumed

Batcher output:
  - L2BatchCommitment
  - DA payload
  - proof job
```

Batch 频率可以按场景调整：

```text
RWA Chain: 30 秒 - 2 分钟
DEX Chain: 5 秒 - 30 秒
Game Chain: 1 分钟 - 5 分钟
Enterprise Chain: 可配置
```

---

## 7.3 StateRootGenerator

Neo 已有围绕 state root 和 storage proof 的能力。`getproof` 方法可以基于 root hash、contract hash 和 storage key 查询 proof，并要求安装 StateService 和 RpcServer；这说明 Neo 生态已有 state proof 基础设施，可作为 L2 settlement / withdrawal proof 的参考基础。([Neo文档][9])

L2 需要生成：

```text
preStateRoot
postStateRoot
txRoot
receiptRoot
withdrawalRoot
l2ToL1MessageRoot
l2ToL2MessageRoot
```

其中：

```text
postStateRoot = batch 执行后的 L2 canonical state
withdrawalRoot = 本 batch 中所有 withdrawal requests 的 Merkle root
messageRoot = 本 batch 中所有 cross-chain messages 的 Merkle root
```

---

## 7.4 DAWriter

DAWriter 负责把 batch data 写到数据可用性层。

三种模式：

```text
Mode 1: L1 DA
  - compressed tx data 发布到 Neo N3 / Neo 4 L1
  - 成本最高
  - 安全性最好
  - 适合 DeFi / stablecoin / RWA settlement

Mode 2: NeoFS DA
  - batch data 存 NeoFS
  - NeoHub 记录 NeoFS object commitment
  - 成本低
  - 适合 game / social / enterprise

Mode 3: DAC / external DA
  - data availability committee 签名确认
  - 成本最低
  - 风险最高
  - 必须在 ChainRegistry 显示风险等级
```

Neo N3 本身已有 NeoFS、native oracle、self-sovereign identity、NNS、one-block finality 等生态能力，这些可以作为 Neo L2 网络的差异化基础设施。([NEO Developer Resource][10])

---

## 7.5 ProverAdapter

ProverAdapter 是从 sidechain 走向真正 L2 的关键。

建议支持三阶段：

```text
Stage 0: Attestation proof
  - L2 validators 多签 batch
  - NeoHub 只验证 validator signatures
  - 本质仍偏 sidechain

Stage 1: Optimistic proof
  - batch 先进入 pending
  - challenge window 内可提交 fraud proof
  - 成本较低
  - 复杂度中等

Stage 2: ZK validity proof
  - 证明 oldRoot + txs + messages -> newRoot
  - NeoHub.ContractZkVerifier 验证 envelope/VK/publicInputHash
  - L1 可部署验证器合约 验证 proof-system math
  - 安全性最高
```

NeoVM2 / RISC-V 是最重要方向。Neo 4 roadmap 已经写到 NeoVM 2 要兼容 RISC-V 指令集并支持 fine-grained gas metering，这非常适合作为 L2 proof VM 的基础。([GitHub][3])

---

# 8. 证明系统架构

## 8.1 不要证明整个 C# node

`r3e-network/neo` 的 `r3e/neo-n4-core` 分支基于 `neo-project/neo` master 的完整节点实现，包括 P2P、RPC、plugins、wallet、mempool、DB 等；`r3e/neo-n3-core` 分支基于 `master-n3`，用于 L1 core 级别改动。真正 L2 proof 不应该证明整个 node，而应该证明 deterministic state transition function：

```text
ApplyBatch(
  preStateRoot,
  orderedTransactions,
  l1Messages,
  blockContext
) -> (
  postStateRoot,
  receiptsRoot,
  withdrawalRoot,
  messageRoot
)
```

也就是说：

```text
不证明:
  - P2P gossip
  - RPC
  - wallet
  - plugins
  - logging
  - mempool
  - DB implementation

只证明:
  - transaction validity
  - contract execution
  - storage transition
  - native contract behavior
  - message/withdrawal root generation
```

## 8.2 推荐证明路径

```text
Neo 4 Core Reference Executor
        ↓
L2 Batch Executor Spec
        ↓
NeoVM2 / RISC-V Deterministic Executor
        ↓
RISC-V zkVM / prover
        ↓
Proof
        ↓
NeoHub Verifier
```

## 8.3 Public Inputs

ZK proof 的 public inputs 应该包括：

```text
chainId
batchNumber
preStateRoot
postStateRoot
txRoot
receiptRoot
withdrawalRoot
l2ToL1MessageRoot
l2ToL2MessageRoot
l1MessageHash
daCommitment
blockContextHash
```

## 8.4 Witness

Witness 包括：

```text
ordered transactions
contract bytecode
storage read/write witness
native contract state witness
L1 messages consumed
DA data
execution trace
```

---

# 9. Token / GAS / 资产模型

这是 Neo 4 L2 成败的关键。

Neo 官方 token model 中，NEO 是治理 token，GAS 是网络资源费用 token，NEP-17 是智能合约发行管理的 token。([NEO Developer Resource][5])

因此 Neo 4 L2 应该这样设计：

```text
Canonical GAS:
  只在 Neo N3 / Neo 4 L1 canonical 存在

L2 GAS:
  是 SharedBridge 上锁定 GAS 的 L2 representation

L2 fee:
  默认使用 bridged GAS
  可支持 paymaster 用 stablecoin 代付

NEO:
  L1 NEO 保持 decimals=0、不可分割
  每条 L2 内置 decimal NEO 表示(decimals=8)
  充值 1 L1 NEO -> 100000000 L2 NEO 最小单位
  提款必须能精确缩回 L1 整数 NEO
  治理权默认仍锚定 L1

NEP-17:
  通过 TokenRegistry 建立 canonical mapping
```

## 9.1 Deposit

```text
User locks GAS on NeoHub.SharedBridge
        ↓
NeoHub emits L1ToL2DepositMessage
        ↓
L2 L1MessageProcessor consumes message
        ↓
L2 mints bridged GAS balance
```

## 9.2 Withdrawal

```text
User burns / withdraws bridged GAS on L2
        ↓
L2 writes withdrawal record
        ↓
Batch includes withdrawalRoot
        ↓
Batch finalized on NeoHub
        ↓
User submits withdrawal proof
        ↓
NeoHub releases canonical GAS
```

## 9.3 Fee Abstraction

Neo 4 roadmap 提到 AA framework、programmable signatures、fee abstraction、ZK-based account recovery。这个方向应该直接内置到 L2 里。([GitHub][3])

```text
User pays:
  - GAS
  - stablecoin
  - dApp sponsored fee
  - enterprise account billing
```

Paymaster 结算：

```text
User stablecoin fee -> Paymaster
Paymaster pays bridged GAS -> L2 sequencer/prover
```

---

# 10. 跨链消息架构：Neo Connect

建议把跨链系统命名为：

```text
Neo Connect
```

它包括：

```text
L1 -> L2 message
L2 -> L1 message
L2 -> L2 message
cross-chain asset transfer
cross-chain contract call
cross-chain bundled transaction
cross-chain paymaster
```

## 10.1 L1 -> L2 Message

```text
NeoHub.enqueueL1ToL2Message()
        ↓
message stored in L1 queue
        ↓
L2 node watches queue
        ↓
L2 includes message in next batch
        ↓
L2 contract executes deposit/call
```

## 10.2 L2 -> L1 Message

```text
L2 contract emits message
        ↓
messageRoot included in batch
        ↓
batch finalized on NeoHub
        ↓
user/relayer submits message + Merkle proof
        ↓
NeoHub executes target L1 call
```

## 10.3 L2 -> L2 Message

```text
L2-A emits message to L2-B
        ↓
L2-A batch finalized on NeoHub / Neo Gateway
        ↓
globalMessageRoot updated
        ↓
relayer submits message + Merkle proof to L2-B
        ↓
L2-B verifies root and executes call
```

## 10.4 Cross-chain Bundle

示例：

```text
User on Neo RWA Chain:
  "Use stablecoin to buy asset on Neo DEX Chain"

System:
  1. RWA Chain locks/burns stablecoin
  2. Emits L2->L2 message
  3. Gateway updates message root
  4. DEX Chain verifies message proof
  5. DEX contract executes swap
  6. Result message returns to RWA Chain
```

用户看到的是一次操作，底层是多链交易。

---

# 11. Bridge 设计

Neo 4 L2 网络不能为每条 L2 各自做一套孤立桥。桥接系统必须基于 Neo Stack 的共享结算、统一资产映射、统一消息根和统一治理，而不是把每条执行链做成互不兼容的独立网络。应该统一为：

```text
NeoHub SharedBridge
```

## 11.1 SharedBridge 原则

```text
1. 所有 canonical assets 锁在 L1
2. 所有 L2 wrapped assets 有统一 mapping
3. 每条 L2 不得私自发行 canonical GAS
4. withdrawal 必须基于 finalized withdrawalRoot
5. bridge config 必须由 NeoHub governance 管理
6. 所有 bridge messages 有 nonce 和 replay protection
```

## 11.2 Asset Mapping

```csharp
struct AssetMapping {
    UInt160 l1Asset;
    uint32 l2ChainId;
    UInt160 l2Asset;
    byte assetType;       // GAS, NEO, NEP17, stablecoin, RWA
    bool mintBurn;
    bool lockMint;
    byte l1Decimals;
    byte l2Decimals;
    bool active;
}
```

平台资产规则:

```text
NEO: l1Decimals = 0, l2Decimals = 8
GAS: l1Decimals = 8, l2Decimals = 8
USDT: l1Decimals = 6, l2Decimals = 6
USDC: l1Decimals = 6, l2Decimals = 6
BTC: l1Decimals = 8, l2Decimals = 8
```

## 11.3 跨外链桥 ExternalBridge

`SharedBridge` 只服务 Neo L1 ↔ Neo L2 这条单一管辖域链路。当 L2 dApp
需要与 Ethereum / Tron / Solana 等外部链交互时，需要一套独立的、**桥
协议无关**的可插拔桥：`ExternalBridge`。

设计原则与 `VerifierRegistry` 相同：上层 API 永远不变，底层 verifier
合约可在 MPC committee → Optimistic challenge → ZK light client 之间
通过 governance 升级，而不破坏 dApp 调用路径。

### 11.3.1 合约组

```text
NeoHub.ExternalBridgeRegistry      # externalChainId → IExternalBridgeVerifier 路由
NeoHub.ExternalBridgeEscrow        # 锁仓 + 入站验证 + 凭证派发
NeoHub.ExternalBridgeBond          # 委员会绑定 + 切片
Neo Core native L2NativeExternalBridgeContract  # L2 侧入口（Send / Receive）
```

### 11.3.2 Verifier 抽象

```csharp
interface IExternalBridgeVerifier {
    bool VerifyInboundMessage(uint externalChainId, byte[] msgBytes, byte[] proofBytes);
    byte BridgeKind();   // 1=MPC, 2=Optimistic, 3=ZK
}
```

`ExternalBridgeRegistry.VerifyInbound(externalChainId, msg, proof)`
读取该外链当前注册的 verifier 哈希，`Contract.Call`
`VerifyInboundMessage`。dApp 永远看不到底层是哪种 verifier。

### 11.3.3 ExternalCrossChainMessage 线格式

```csharp
struct ExternalCrossChainMessage {
    uint32 externalChainId;     // Eth=0xE000_0001, Tron=0xE000_0010, Sol=0xE000_0020
    uint32 neoChainId;          // 目标 Neo L2 chainId（0 = L1）
    ulong  nonce;               // 单方向单 pair 的 replay key
    byte   direction;           // 1 = Neo→Foreign, 2 = Foreign→Neo
    UInt160 sender;
    UInt160 recipient;
    ulong  deadlineUnixSeconds;
    UInt256 sourceTxRef;        // Eth tx hash / Tron tx hash / Solana sig
    byte   messageType;         // 0=AssetTransfer, 1=Call, 2=AssetAndCall
    byte[] payload;
    UInt256 messageHash;        // Hash256 over canonical bytes
}
```

`externalChainId` 高位 `0xE0` 前缀保留外链命名空间，与 Neo L2 chainId
（从 1 开始）无冲突。

### 11.3.4 加密原语

Neo `CryptoLib` 已暴露：

- `VerifyWithECDsa`（secp256k1 + Keccak256）→ Eth / Tron 签名验证
- `VerifyWithEd25519` → Solana 签名验证

签名验证不是瓶颈，**轻客户端复杂度才是**。Solana 因 Tower BFT 验证
集 ~1500、每 epoch 轮换、需要推理 lockouts，不能短期做无信任轻
客户端，因此 Solana 桥的所有 Phase 都停留在 MPC committee 模型。

### 11.3.5 升级路径（Phase 顺序）

```text
Phase A：Foundation       # 三个合约 + IExternalBridgeVerifier seam（无 verifier）
Phase B：MPC Committee    # M-of-N 委员会 + Eth/Tron/Sol watchers + 链上 escrow router
Phase C：Optimistic       # 挑战窗口 + 欺诈证明（MpcCommitteeFraudVerifier）
Phase D：ZK Light Client  # Eth 优先（SP1 + sync committee SNARK）；Tron 次之；Sol 保留 MPC
```

每次 Phase 升级 = governance 通过 `ExternalBridgeRegistry.UpgradeVerifier`
切换 verifier 合约哈希，dApp 调用路径不动。

详细路线图见 `docs/external-bridge-roadmap.md`。

---

# 12. Data Availability 设计

必须在 ChainRegistry 中公开 DA 模式。

```text
SecurityLevel:
  0 = sidechain
  1 = settled sidechain
  2 = optimistic rollup
  3 = zk rollup
  4 = zk validium

DAMode:
  0 = L1 DA
  1 = NeoFS DA
  2 = external DA
  3 = DAC
```

Neo N4 的默认 / 推荐 DA 路径是 `DAMode.NeoFS`。`L1`、`External`、`DAC`
仍保留为显式覆盖项，必须在 `ChainRegistry` 中如实标注。

## 12.1 L1 DA

```text
Pros:
  - 安全性最高
  - 用户可重建 L2 state
  - escape hatch 最可靠

Cons:
  - 成本最高
```

## 12.2 NeoFS DA

```text
Pros:
  - 低成本
  - 与 Neo 生态一致
  - 适合企业、游戏、社交

Cons:
  - 安全性取决于 NeoFS availability 和证明机制
```

## 12.3 DAC

```text
Pros:
  - 成本最低
  - 性能高

Cons:
  - 如果数据不可用，用户可能无法退出
  - 必须显著标注风险
```

---

# 13. L2 Native Contracts 改造

Neo 4 core 的 native contracts 在 L1 上合理，但 L2 需要区分哪些是本地 native，哪些来自 L1。

## 13.1 L2 必须新增的 native contracts

```text
L2BridgeContract
  - mint bridged assets
  - burn withdrawal assets
  - verify deposit messages

L2MessageContract
  - emit L2->L1 messages
  - emit L2->L2 messages
  - consume inbound messages

L2BatchInfoContract
  - expose chainId, batchNumber, L1 finalized height

L2FeeContract
  - manage sequencer fee, prover fee, DA fee

L2PaymasterContract
  - support stablecoin fee / sponsored fee

L2SystemConfigContract
  - store L2 config synchronized from NeoHub
```

## 13.2 需要调整的 native contracts

```text
GAS contract:
  - L2 上 GAS supply 受 bridge 控制
  - 不独立生成 canonical GAS

NEO contract:
  - L2 上可以表示 bridged NEO
  - governance 权力默认仍以 L1 为准

Oracle:
  - 可选择 L2 local oracle
  - 或通过 L1 / NeoHub / zk-Oracle 提供

Policy:
  - L2 可本地配置 fee policy
  - 但 bridge/security policy 由 NeoHub 管控
```

---

# 14. RPC / SDK / Tooling

完整系统必须提供统一开发体验。

## 14.1 L2 RPC Extensions

```text
getl2batch(chainId, batchNumber)
getl2batchstatus(chainId, batchNumber)
getl2stateroot(chainId, batchNumber)
getl2withdrawalproof(txHash)
getl2messageproof(messageHash)
getl1depositstatus(depositId)
getcanonicalasset(l2Asset)
getbridgedasset(l1Asset, chainId)
getsecuritylevel(chainId)
```

## 14.2 Neo Stack CLI

建议开发一个：

```text
neo-stack
```

命令示例：

```bash
neo-stack create-chain --template rollup --vm neovm2-riscv
neo-stack init-l2 --chain-id 1001 --da neofs
neo-stack register-chain --l1 neo-n3-testnet
neo-stack deploy-bridge-adapter
neo-stack start-sequencer
neo-stack start-batcher
neo-stack start-prover
neo-stack submit-batch
```

## 14.3 Developer Tooling

```text
Neo DevPack
  -> compile C# / other languages to NeoVM2/RISC-V

Neo L2 SDK
  -> deposit / withdraw / cross-chain call

Neo Wallet SDK
  -> one account across L1/L2s

Neo Explorer
  -> show L1 finality, L2 finality, batch proof status

Neo Indexer
  -> index L2 messages, withdrawals, asset mappings
```

---

# 15. 关键流程设计

## 15.1 普通 L2 交易流程

```text
1. User submits tx to Neo 4 L2
2. Sequencer / dBFT committee orders tx
3. Neo 4 core executes tx through ApplicationEngine / NeoVM2
4. L2 block finalized locally
5. Batcher groups multiple L2 blocks into batch
6. StateRootGenerator computes postStateRoot
7. DAWriter publishes batch data / DA commitment
8. ProverAdapter generates proof or validator attestation
9. SettlementSubmitter submits batch to NeoHub / Gateway
10. NeoHub verifies and finalizes batch
```

用户体验：

```text
Fast confirmation:
  L2 dBFT finality

Canonical confirmation:
  NeoHub finalized batch
```

---

## 15.2 Deposit 流程

```text
1. User calls NeoHub.SharedBridge.deposit(asset, amount, targetChain, receiver)
2. L1 locks canonical asset
3. NeoHub creates L1ToL2Message
4. L2 node reads deposit queue
5. L2 includes message in next block
6. L2BridgeContract mints bridged asset
7. Batch proves message was consumed
8. NeoHub marks deposit consumed
```

---

## 15.3 Withdrawal 流程

```text
1. User calls L2BridgeContract.withdraw(asset, amount, l1Receiver)
2. L2 burns bridged asset
3. L2 creates withdrawal record
4. withdrawalRoot included in batch
5. batch finalized on NeoHub
6. User submits withdrawal proof
7. NeoHub verifies proof against finalized withdrawalRoot
8. NeoHub releases canonical asset
```

---

## 15.4 Forced Inclusion 流程

这是防止 sequencer 审查的机制。

```text
1. User submits tx directly to NeoHub forced inclusion queue
2. L2 sequencer must include it before deadline
3. If not included:
   - sequencer bond slashed
   - L2 batch finalization paused
   - fallback sequencer can include tx
```

---

## 15.5 Emergency Exit 流程

```text
1. L2 unavailable or sequencer malicious
2. User obtains state proof from last finalized state root
3. User submits proof to NeoHub
4. NeoHub verifies user balance / ownership
5. User records a verified exit CLAIM（并非直接转出资产）
```

第 5 步需澄清当前实现的两条路径（见 `EmergencyManager.EscapeHatchExit*` 与 `SharedBridge.EmergencyFinalizeWithdrawalWithProof`）：

- 逃生舱（escape hatch）只对状态树叶子做 Merkle 校验并记录一个一次性、防重放的**退出 CLAIM**（emit `EscapeHatchExit`），它本身**不转出任何资产**——通用 state leaf 并不绑定 canonical 的 (asset, amount, recipient) 元组，桥无法据此自动赔付。
- **自动赔付**仅适用于 sequencer 已经 finalize 的提款：用户走 `SharedBridge.EmergencyFinalizeWithdrawalWithProof`，针对该 batch 的 withdrawalRoot 做 Merkle 校验后由桥直接 payout。
- 对于**从未被 finalize** 的 state-leaf 退出，记录的 CLAIM 由 governance / 链下结算来释放对应托管资产，而非链上自动转出。

这要求 DA 足够强。对 validium / DAC 模式，emergency exit 可能受限，必须提前披露。

---

# 16. Governance 设计

Neo 4 roadmap 提到增强 NEO Council 角色，包括动态调整 council members、决定 consensus node admission/exit、管理核心网络参数，并提到 NEO holders 的 referendum power 和未来探索 Layer-2 governance。([GitHub][3])

> 注（实现状态）：`NeoHub.GovernanceController` 已支持 proposal-bound、threshold-approved、timelocked 的原子 council rotation。`RotateCouncil` 同时替换成员与门限并递增 `councilEpoch`；所有旧 epoch proposal 立即失效，rotation proposal 只能消费一次。timelock 参数仍在部署时固定，合约没有 `ContractManagement.Update` 路径。链上 **NEO holder referendum 仍未实现**。

Neo L2 governance 应分三层：

```text
Layer 1: Neo L1 Governance
  - NeoHub upgrade
  - verifier upgrade
  - shared bridge upgrade
  - emergency pause
  - L2 admission policy

Layer 2: L2 Local Governance
  - sequencer committee
  - local fee policy
  - local app-chain parameters
  - local DA mode within approved range

Layer 3: App Governance
  - dApp rules
  - RWA issuer policy
  - stablecoin policy
  - enterprise permissioning
```

## 16.1 L2 Admission

```text
Permissioned phase:
  - governance approves L2 registration

Semi-permissionless phase:
  - L2 can register if it uses approved verifier + approved bridge adapter

Permissionless phase:
  - any L2 can register
  - security label clearly shown
```

## 16.2 Security Labels

每条 L2 必须公开显示：

```text
Chain type:
  sidechain / optimistic / zk rollup / validium

DA mode:
  L1 / NeoFS / external DA / DAC

Proof mode:
  multisig / optimistic / zk

Sequencer:
  centralized / dBFT committee / decentralized

Exit:
  permissionless / delayed / operator-assisted

Bridge:
  canonical / non-canonical
```

---

# 17. 安全模型

## 17.1 Threat Model

```text
1. Sequencer censorship
2. Invalid state root submission
3. Bridge exploit
4. Replay attack
5. DA unavailability
6. Malicious validator committee
7. Prover bug
8. Verifier upgrade attack
9. Cross-chain message duplication
10. L2 contract bug
```

## 17.2 Mitigations

```text
Sequencer censorship:
  - forced inclusion queue
  - sequencer bond
  - fallback sequencer

Invalid state root:
  - zk validity proof
  - optimistic challenge
  - verifier registry

Bridge exploit:
  - canonical shared bridge
  - rate limits
  - emergency pause
  - staged withdrawals

Replay attack:
  - chainId
  - message nonce
  - consumed message bitmap

DA unavailability:
  - DA commitment
  - DA security label
  - L1 DA for high-value chains
  - NeoFS redundancy

Verifier upgrade attack:
  - governance delay
  - security council veto
  - emergency rollback
  - staged deployment

Cross-chain message duplication:
  - messageHash
  - consumedRoot tracking
  - targetChain-specific nonce
```

---

# 18. 分阶段落地路线

## Phase 0：Neo 4 Sidechain PoC

目标：证明 Neo 4 core 可以作为 L2-like chain 执行内核。

```text
Deliverables:
1. fork / configure `r3e-network/neo` branch `r3e/neo-n3-core` from `neo-project/neo` master-n3 for L1 core, and branch `r3e/neo-n4-core` from `neo-project/neo` master for L2 core
2. new Network ID
3. L2 genesis config
4. dBFT validator set
5. L2 RPC
6. basic contract deployment
7. basic bridged GAS representation
```

安全性质：

```text
Sidechain security
非 trustless L2
```

---

## Phase 1：NeoHub v0 + Shared Bridge

目标：把 L2 接到 Neo N3 / Neo 4 L1。

```text
Deliverables:
1. ChainRegistry
2. SharedBridge
3. TokenRegistry
4. L1 -> L2 deposit
5. L2 -> L1 withdrawal
6. message nonce / replay protection
7. bridge UI / SDK
```

安全性质：

```text
Connected sidechain
资产桥由 NeoHub 统一管理
```

---

## Phase 2：Batch Settlement

目标：让 L2 定期向 L1 提交 batch roots。

```text
Deliverables:
1. Batcher
2. StateRootGenerator
3. L2BatchCommitment format
4. SettlementManager
5. withdrawalRoot verification
6. l2ToL1MessageRoot verification
7. DA commitment
```

安全性质：

```text
Settled L2
但如果没有 proof/challenge，仍然信任 L2 operators
```

---

## Phase 3：Optimistic L2

目标：引入 challenge window。

```text
Deliverables:
1. optimistic verifier
2. fraud proof format
3. challenge bond
4. challenger reward
5. batch revert mechanism
6. delayed withdrawal finalization
```

安全性质：

```text
Optimistic rollup-like
安全性依赖至少一个 honest challenger
（v1/v2/v3 仅为审计证据且即使有 governance witness 也 fail closed；v4 对一个精确受限语义实现无许可链上重放）
```

> 注（实现状态）：`GovernanceFraudVerifier` 的 v1/v2 与 `RestrictedExecutionFraudVerifier` 的 v3 是**仅审计用结构性证据**；v3 只重新派生挑战者 payload 内的 storage roots，不绑定已提交批次，也不执行交易。它们不能触发回滚/罚没，governance witness 也不能绕过该限制。v4 则绑定 `SettlementManager` 已提交的规范头/roots、交易证明、claim id、受限 transcript 与 replay domain，并在链上执行精确的 `counter-increment-existing-key:v1` 语义；只有与 chainId、semanticId、replayDomain 精确注册的 profile 才允许无许可罚没。通用 NeoVM、多交易与其它 custom executor 语义仍 fail closed，需新增已承诺 tx-count / trace anchor 与完整单步语义后才能支持。

---

## Phase 4：NeoVM2/RISC-V ZK L2

目标：进入真正 validity proof L2。

```text
Deliverables:
1. deterministic batch executor
2. RISC-V execution trace
3. zk prover
4. NeoHub zk verifier
5. public input format
6. recursive / aggregated proof support
```

安全性质：

```text
ZK validity rollup
L1 verifies state transition correctness
```

---

## Phase 5：Neo Gateway

目标：多 L2 proof aggregation 和跨 L2 互操作。

```text
Deliverables:
1. Gateway proof collector
2. aggregated proof verifier
3. global message root
4. L2-to-L2 message verification
5. Gateway settlement mode
```

安全性质：

```text
Neo Elastic Network
多 L2 统一 proof aggregation 和 message root
```

---

## Phase 6：Neo Stack

目标：让生态能自己启动 L2。

```text
Deliverables:
1. neo-stack CLI
2. L2 templates
3. DA adapters
4. prover adapters
5. bridge adapters
6. wallet/indexer/explorer integration
7. security label dashboard
```

---

# 19. 推荐代码模块划分

在 `r3e-network/neo` core fork 基础上，不建议把所有 L2 逻辑直接塞进 core。更好的方式是核心抽象 + 插件/模块扩展；只有 native contract、ChainMode、执行内核钩子等无法在本仓库插件化的改动才进入 core fork。

```text
src/Neo/
  L2/
    Abstractions/
      IL2BatchExecutor.cs
      IL2ProofVerifier.cs
      IDAWriter.cs
      IMessageRouter.cs
      ISettlementClient.cs

    Batch/
      L2Batch.cs
      L2BatchCommitment.cs
      BatchBuilder.cs
      BatchSerializer.cs

    State/
      StateRootCalculator.cs
      WithdrawalTree.cs
      MessageTree.cs

    Bridge/
      L2BridgeContract.cs
      AssetMapping.cs
      DepositProcessor.cs
      WithdrawalProcessor.cs

    Messaging/
      CrossChainMessage.cs
      L1MessageQueue.cs
      L2Outbox.cs
      MessageProof.cs

    Proving/
      ProofRequest.cs
      ProofResult.cs
      RiscVProverAdapter.cs
      OptimisticProofAdapter.cs
      AttestationProofAdapter.cs

src/Plugins/
  L2BatchPlugin/
  L2SettlementPlugin/
  L2BridgePlugin/
  L2DAPlugin/
  L2ProverPlugin/
  L2GatewayPlugin/
```

---

# 20. 最小可行产品 MVP

如果现在要真正启动工程，我建议 MVP 范围不要太大。

## MVP 目标

```text
A Neo 4-core based L2 devnet
+ NeoHub testnet contracts
+ canonical GAS bridge
+ batch root submission
+ L2 -> L1 withdrawal proof
```

## MVP 不做

```text
暂不做 full ZK proof
暂不做 permissionless L2 launch
暂不做 all-token bridge
暂不做 L2-to-L2 contract call
暂不做 Gateway aggregation
```

## MVP 组件

```text
1. Neo 4 L2 devnet
2. L2 dBFT sequencer committee
3. SharedBridge for GAS only
4. Batcher
5. StateRootGenerator
6. SettlementManager on L1
7. WithdrawalRoot proof
8. Basic SDK
9. Basic explorer view
```

## MVP 判断标准

```text
1. 用户能从 Neo N3 deposit GAS 到 Neo 4 L2
2. 用户能在 L2 部署/调用 Neo contract
3. L2 能生成 batch commitment
4. batch 能提交到 NeoHub
5. 用户能基于 finalized withdrawalRoot 提现回 Neo N3
```

---

# 21. 最终推荐架构名称

我建议命名为：

```text
Neo Elastic Network
```

核心组件命名：

```text
NeoHub
  L1 settlement / shared bridge / chain registry

Neo Gateway
  proof aggregation / global message root

Neo Stack
  launch framework for Neo L2 chains

Neo Connect
  native cross-chain messaging / asset / contract call layer

Neo 4 L2 Core
  Neo 4 core in L2RollupMode / L2ValidiumMode
```

最终架构一句话：

> **Neo N3 / Neo 4 L1 作为 settlement and asset root，NeoHub 作为统一 L2 注册与共享桥，Neo Gateway 负责 proof aggregation 和 inter-L2 message root，Neo 4 core 作为各条 L2 的 execution engine，NeoVM2/RISC‑V 作为未来 validity proof 的统一执行基础。**

---

# 22. 关键设计取舍

| 问题      | 推荐选择                          | 原因                                       |
| ------- | ----------------------------- | ---------------------------------------- |
| L2 执行内核 | Neo 4 core                    | 运行 NeoVM2/RISC-V,复用 native contracts、tooling |
| 排序机制    | dBFT committee                | 符合 Neo，提供快速 local finality               |
| L1 结算   | NeoHub                        | 统一状态、资产、消息和治理                            |
| 桥       | SharedBridge                  | 避免每条 L2 自己建桥导致碎片化                        |
| 证明路线    | Attestation → Optimistic → ZK | 降低早期难度，保留最终 trustless 路线                 |
| VM 证明基础 | NeoVM2/RISC‑V                 | 与 Neo 4 roadmap 一致                       |
| DA      | L1 DA + NeoFS DA + DAC 分级     | 不同场景成本/安全不同                              |
| 跨链      | Neo Connect                   | 原生 message/call，不只是资产桥                   |
| 多 L2 扩展 | Neo Gateway + Neo Stack       | 形成网络效应                                   |

---

# 23. 最重要的提醒

**不要把“fork Neo 4 core 跑一条链”直接称为 L2。**

准确区分：

```text
只跑 Neo 4 core + dBFT:
  Neo 4 sidechain / app-chain

加 NeoHub bridge + batch root:
  settled sidechain / early L2

加 optimistic challenge:
  optimistic L2

加 NeoVM2/RISC-V validity proof:
  true zk/validity L2

加 Gateway + shared bridge + interop:
  Neo Elastic Network
```

所以完整系统应该这样落地：

```text
Neo 4 core
  作为 L2 execution kernel

NeoHub
  作为 L1 settlement and shared bridge

Neo Gateway
  作为 proof aggregation and interop layer

Neo Connect
  作为 cross-chain message/call layer

Neo Stack
  作为 L2 launch framework
```

这套设计既能复用当前 Neo 4 core，又不会把 sidechain 和真正 L2 混淆；同时也吸收了 ZKsync Elastic Chain 的核心思想：**多链可以扩展，但资产根、状态验证、跨链消息和用户体验必须统一。**

[1]: https://docs.zksync.io/zksync-protocol/contracts/l1-contracts/shared-bridges?utm_source=chatgpt.com "Shared bridges"
[2]: https://raw.githubusercontent.com/neo-project/neo/master/README.md "raw.githubusercontent.com"
[3]: https://github.com/neo-project/neo/issues/4198 "NEO 4 Roadmap: A Blockchain Platform for the Real World · Issue #4198 · neo-project/neo · GitHub"
[4]: https://docs.neo.org/docs/n3/foundation/consensus/dbft.html "Consensus Mechanism"
[5]: https://developers.neo.org/docs/n3/foundation/Native%20tokens "NEO Token Models | NEO Developer Resource"
[6]: https://docs.zksync.io/zksync-network/unique-features/zksync-connect?utm_source=chatgpt.com "Overview - ZKsync Docs"
[7]: https://docs.zksync.io/zksync-protocol/gateway?utm_source=chatgpt.com "Gateway Overview"
[8]: https://docs.neo.org/docs/n3/node/cli/config.html "Configuring and Starting Neo-CLI"
[9]: https://docs.neo.org/docs/n3/reference/rpc/getproof.html "getproof Method"
[10]: https://developers.neo.org/n3 "NEO Developer Resource"
