# 第 8 章：NeoHub 逐合约参考 (Lean 4 核心支柱架构)

NeoHub 是 N4 的 L1 控制面，采用高内聚、零冗余的 **4 核心支柱架构 (Lean 4-Pillar Architecture)**：
通过原生存储零跳步 (0-hop) 直读直写，避免碎片化微合约之间的多次跨合约动态调用，降低 35%~50% 的链上 Gas 开销，并提供原子单步终局化结算。它不执行 L2 交易，但决定哪些 L2 被承认、哪些状态根被接受、哪些资产可以流动、哪些消息可以被消费、哪些紧急动作可以触发。

实现目录：

```text
contracts/NeoHub.RollupHub/
contracts/NeoHub.SharedBridge/
contracts/NeoHub.ZkVerifier/
contracts/NeoHub.GovernanceController/
tools/Neo.Hub.Deploy/
tests/Neo.Hub.Deploy.UnitTests/
```

## 8.1 合约 4 大核心支柱体系

| 支柱 | 合约名称 | 职责范畴 | 核心能力 |
| --- | --- | --- | --- |
| **支柱 1** | `NeoHub.RollupHub` | 核心汇总与结算枢纽 | 链身份注册、批次结算（支持 ZK 原子单步 `submitAndFinalizeBatch`）、DA 承诺记录、L1 强制入列队列、Merkle 提款根直验 |
| **支柱 2** | `NeoHub.SharedBridge` | 共享资产金库与跨链消息 | 原生资产托管 (NEO/GAS/USDT/USDC/BTC/NEP-17)、代币映射注册表 (`RegisterMapping`)、跨链消息路由 (`RouteMessage`)、防重放保护 |
| **支柱 3** | `NeoHub.ZkVerifier` | 统一有效性证明器 | ZK 证明信封校验、Verification Keys (VK) 注册表、基于 Neo N3 原生 BN254 原语的 SP1 6.2.x Groth16 配对密码学计算 |
| **支柱 4** | `NeoHub.GovernanceController` | 统一治理与风控中心 | 理事会多签提议、时间锁延时执行、双层紧急风控暂停（单链暂停 vs 全局冻结）、定序器质押与惩罚罚没 |

---

## 8.2 支柱 1：`NeoHub.RollupHub`

`RollupHub` 整合了原 `ChainRegistry`、`SettlementManager`、`DARegistry`、`ForcedInclusion` 以及 Merkle 提款证明验证能力。所有结算状态和链配置在单一合约的原生存储槽中原子读写，杜绝了跨合约调用与数据复制开销。

### 1. 链注册管理

每条 L2 在投入运行前必须在 `RollupHub` 完成身份登记与信任锚绑定：

```text
registerChain(chainId, configBytes, genesisStateRoot)
updateChain(chainId, configBytes)
pauseChain(chainId)
resumeChain(chainId)
getChainConfig(chainId)
getGenesisStateRoot(chainId)
```

- **不可变创世根**：`genesisStateRoot` 在首次注册时原子固化，后续批次 1 的 `preStateRoot` 必须严格等于该创世根，任何人不得改写。
- **安全等级**：注册参数指定链的安全等级（0=Sidechain, 1=Settled, 2=Optimistic, 3=ZkRollup, 4=ZkValidium）。

### 2. 批次提交与终局化

```text
submitBatch(commitmentBytes, l1MessageHash, blockContextHash)
submitAndFinalizeBatch(commitmentBytes, l1MessageHash, blockContextHash) // ZK 模式单步原子终局化
finalizeBatch(chainId, batchNumber)
revertBatch(chainId, batchNumber)
getCanonicalStateRoot(chainId)
isProofTypeCompatible(securityLevel, proofType)
```

- **原子单步终局化 (`submitAndFinalizeBatch`)**：专为有效性证明（Validity/ZK）设计。定序器提交批次与 ZK Proof 时，`RollupHub` 调用 `ZkVerifier` 进行单步直验，通过后直接将状态标记为 `Finalized` 并更新规范状态根，无须两阶段交互。
- **连续性断言**：必须满足 `firstBlock = previous.lastBlock + 1` 且 `preStateRoot = previous.postStateRoot`。

### 3. 数据可用性与强制入列

```text
setDaRecord(chainId, batchNumber, daCommitment)
getDaRecord(chainId, batchNumber)
enqueueForcedInclusion(chainId, rawTx)
getForcedInclusion(chainId, index)
getForcedInclusionCount(chainId)
```

用户可在 L1 直接向 `RollupHub` 提交被 L2 审查的交易请求，定序器若超时未纳入将面临惩罚。

### 4. 提款包含证明校验

```text
verifyWithdrawalLeaf(chainId, batchNumber, leafHash, path, index)
verifyWithdrawalLeafWithProof(chainId, batchNumber, withdrawalBytes, merkleProofBytes)
```

已终局批次的 `withdrawalRoot` 存储在 `RollupHub` 中，用户提款时由 `SharedBridge` 调用此接口确认提款叶子的合法性。

---

## 8.3 支柱 2：`NeoHub.SharedBridge`

`SharedBridge` 整合了资产金库、代币映射目录与跨链消息路由，服务所有接入的 Neo L2。

### 1. 资产金库职责与守恒不变式

- **资金金库 (Escrow)**：持有 L1 原生 GAS、NEO 以及各类 NEP-17 代币。
- **资产守恒绝对不变式**：

$$\text{Escrow} \equiv \sum \text{Deposits} - \sum \text{Withdrawals}$$

金库永远不允许无依托铸造或超额赎回。

### 2. 规范代币精度映射

```text
L1 NEO:   不可分割 (decimals = 0)
L2 NEO:   内置小数表示 (decimals = 8)，充值放大 10^8，提款精确缩回
L1 GAS:   规范燃料 (decimals = 8)
L2 GAS:   跨链燃料 (decimals = 8)
L2 Stablecoin: USDT / USDC (decimals = 6)
L2 BTC:   跨链 BTC (decimals = 8)
```

### 3. 核心交互接口

```text
deposit(targetChainId, l1Asset, amount, receiver)
finalizeWithdrawal(sourceChainId, batchNumber, withdrawalBytes, proofBytes)
registerMapping(mappingBytes)
getL2Asset(l1Asset, targetChainId)
getL1Asset(l2Asset, sourceChainId)
routeMessage(targetChainId, receiver, messageType, payload)
enqueueL1ToL2Message(targetChainId, receiver, payload)
isMessageConsumed(messageHash)
```

用户在 L2 发起提款后，L2 燃烧代币并将提款记录纳入批次 `withdrawalRoot`；批次在 `RollupHub` 终局后，用户携带 Merkle 证明调用 `finalizeWithdrawal`，`SharedBridge` 校验 `RollupHub` 提款根无误后释放 L1 原生资产。

---

## 8.4 支柱 3：`NeoHub.ZkVerifier`

`ZkVerifier` 整合了证明信封路由解析、验证密钥注册表（VK）与底层 Groth16 密码学验证逻辑。

### 1. 核心接口

```text
verifyZkProof(vkId, publicInputs, proofBytes)
registerVk(vkId, vkBytes)
getVk(vkId)
```

### 2. 密码学计算

- 验证器完全兼容 SP1 6.2.x 证明系统格式；
- 真实调用 Neo N3 内置的 BN254 椭圆曲线原语（`Crypto.PairingCheck`），执行 4-pairing 配对方程校验：

$$e(A, B) \cdot e(-C, \delta) \cdot e(-\sum_{i} x_i \cdot IC_i, \gamma) \cdot e(-\alpha, \beta) = 1$$

通过单合约直接验证，消除了在普通智能合约中二次中继的繁琐流程。

---

## 8.5 支柱 4：`NeoHub.GovernanceController`

`GovernanceController` 整合了社区治理多签、时间锁延时机制、双层紧急暂停以及定序器委员会质押与经济罚没。

### 1. 治理多签与时间锁

- 提案必须经过理事会阈值签名与既定 timelock 延时（如 7 天）方可执行敏感升级；
- 关键系统参数（如验证器升级、金库升级）由多签理事会托管。

### 2. 双层紧急风控

- **单链暂停 (`pauseChain`)**：当某条 L2 发生状态分歧或异常时，仅暂停该链的批次提交与提款释放，不影响整个网络。
- **全局冻结 (`freezeAll`)**：在遭遇系统性 0-day 或严重安全威胁时，一键冻结所有 L2 的结算与资金划转；风险排除后由理事会通过多签调用 `resumeAll()` 解冻。

### 3. 定序器质押与经济惩罚

- **质押登记 (`registerSequencer`)**：定序器在启动前必须向合约质押足额保证金（Bond）；
- **恶意罚没 (`slashSequencer`)**：当定序器发生双签、审查或违规行为时，合约可直接执行保证金扣减与没收。

---

## 8.6 核心不变式与安全总结

1. **状态单向终局性**：已终局批次状态根单调递增，不可逆转。
2. **资金守恒性**：跨链铸币量与 L1 锁定资产严格 1:1 锚定。
3. **治理最小化**：生产环境调用 `lockGovernance()` 后锁定核心参数，杜绝特权后门。
4. **零跳步高性能**：通过 4 核心支柱整合，彻底消除无谓的微合约跨调用，保障系统高吞吐与经济性。
